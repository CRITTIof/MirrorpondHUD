using System;
using System.Runtime.InteropServices;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 从 **DXGI 桌面复制**（Desktop Duplication）取屏幕像素，替代 <c>BitBlt</c> 读屏。
///
/// 为什么值得换：从屏幕 DC 读一次要 **9–17 ms**，而且跟区域大小无关（32×32 和 1600×900 一样慢）
/// —— 那是和 DWM 合成的**同步**开销，不是拷贝开销。桌面复制给的是合成器现成的帧：
/// 取帧 ≈ 0.1 ms，整屏拷进 staging + 回读卡片那块 ≈ 1 ms。玻璃一轮从 ~10 ms 掉到 ~1 ms，
/// UI 线程不再每帧被卡一个 vsync。
///
/// 它同样遵守 <c>WDA_EXCLUDEFROMCAPTURE</c>：复制出来的帧里，卡片和底板所在位置是**卡片背后的桌面**，
/// 不是卡片、也不是黑块（实测比过亮度，和 GDI 读屏一致），所以玻璃语义不变。
///
/// 实现上不引第三方包，直接按 vtable 下标调用 COM —— 下标是实测出来的，不是抄文档：
/// 本机（26100 SDK 头文件）写 IDXGIOutput1::DuplicateOutput 在 19，但 26200 的 dxgi.dll 实际在 **22**，
/// 用 19 会拿到 DXGI_ERROR_INVALID_CALL。所以这里按系统生成号选下标，并且**先验证再使用**，
/// 验证不过就整体退回 GDI 读屏（玻璃照常工作，只是慢一点）。
/// </summary>
internal sealed class DesktopFrameSource : IDisposable
{
    // ---- 实测得到的 vtable 下标 ----
    const int SlotCreateTexture2D = 5;                 // ID3D11Device
    const int SlotContextMap = 14;                     // ID3D11DeviceContext
    const int SlotContextUnmap = 15;
    const int SlotContextCopySubresourceRegion = 46;
    const int SlotContextCopyResource = 47;
    const int SlotDupGetDesc = 7;                      // IDXGIOutputDuplication
    const int SlotDupAcquireNextFrame = 8;
    const int SlotDupReleaseFrame = 14;
    const int SlotDupMapDesktopSurface = 12;           // 本机不支持（DXGI_ERROR_UNSUPPORTED），留作记录
    const int SlotFactoryEnumAdapters1 = 12;           // IDXGIFactory1
    const int SlotAdapterEnumOutputs = 7;              // IDXGIAdapter
    const int SlotOutputDuplicateOutputOld = 19;       // Win11 21H2–24H2 头文件布局
    const int SlotOutputDuplicateOutputNew = 22;       // 本机 26200 实测

    const uint FormatBgra = 87;                        // DXGI_FORMAT_B8G8R8A8_UNORM
    const uint UsageStaging = 3;
    const uint CpuAccessRead = 0x20000;
    const uint MapRead = 1;

    const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);
    const int DxgiErrorAccessLost = unchecked((int)0x887A002A);
    const int DxgiErrorNotFound = unchecked((int)0x887A002D);

    static readonly Guid IidFactory = new("770aae78-f26f-4dba-a829-253c83d1b387");
    static readonly Guid IidOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr context);

    [DllImport("dxgi.dll")]
    static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    // 一次 P/Invoke 搬一行，比 Marshal.Copy 走 byte[] 往返快得多（290 行 × 2 次拷贝 ≈ 1.5 ms → ≈0.2 ms）
    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
    static extern void MoveMemory(IntPtr destination, IntPtr source, IntPtr count);

    delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
    delegate int EnumOutputsFn(IntPtr self, uint index, out IntPtr output);
    delegate int DuplicateOutputFn(IntPtr self, IntPtr device, out IntPtr duplication);
    delegate int AcquireNextFrameFn(IntPtr self, uint timeout, IntPtr frameInfo, out IntPtr resource);
    delegate int ReleaseFrameFn(IntPtr self);
    delegate int GetDuplicationDescFn(IntPtr self, IntPtr desc);
    delegate int CreateTexture2DFn(IntPtr self, IntPtr desc, IntPtr initialData, out IntPtr texture);
    delegate int MapFn(IntPtr self, IntPtr resource, uint subresource, uint mapType, uint flags,
                       out MappedSubresource mapped);
    delegate int UnmapFn(IntPtr self, IntPtr resource, uint subresource);
    delegate int CopyResourceFn(IntPtr self, IntPtr destination, IntPtr source);
    delegate void CopyRegionFn(IntPtr self, IntPtr destination, uint destinationSubresource,
                               uint destinationX, uint destinationY, uint destinationZ,
                               IntPtr source, uint sourceSubresource, IntPtr box);

    [StructLayout(LayoutKind.Sequential)]
    struct SampleDesc { public uint Count; public uint Quality; }

    [StructLayout(LayoutKind.Sequential)]
    struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize;
        public uint Format;
        public SampleDesc Sample;
        public uint Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MappedSubresource { public IntPtr Data; public uint RowPitch; public uint DepthPitch; }

    IntPtr _device;
    IntPtr _context;
    IntPtr _duplication;
    IntPtr _staging;
    int _stagingWidth;
    int _stagingHeight;
    int _screenWidth;
    int _screenHeight;
    bool _rangeWarned;
    bool _frameAvailable;
    int _frameLeft = int.MinValue;
    int _frameTop = int.MinValue;
    int _frameWidth;
    int _frameHeight;
    bool _disposed;

    EnumAdapters1Fn _enumAdapters;
    EnumOutputsFn _enumOutputs;
    DuplicateOutputFn _duplicateOutput;
    AcquireNextFrameFn _acquire;
    ReleaseFrameFn _release;
    GetDuplicationDescFn _duplicationDesc;
    CreateTexture2DFn _createTexture;
    MapFn _map;
    UnmapFn _unmap;
    CopyResourceFn _copyResource;
    CopyRegionFn _copyRegion;

    int _failures;
    int _primingAttempts;
    bool _primed;
    bool _dead;

    /// <summary>
    /// P1-7：桌面复制是否已经进入不可用状态（staging 建不上 / 连续失败 / 预热黑帧超限）。
    /// GlassFeed 据此释放整机源并做退避重试，而不是每一帧空转。
    /// </summary>
    public bool IsDead => _dead;

    /// <summary>
    /// 请求区域是否完全落在桌面复制覆盖的输出内（目前只有主屏输出）。
    /// 越界是可预期的正常情况（卡片在副屏 / 负坐标），调用方据此继续走 GDI，
    /// 但不能把「越界」当成源故障去释放 / 重建 D3D 设备（G-10）。
    /// </summary>
    public bool Covers(int left, int top, int width, int height)
        => width > 0 && height > 0
           && left >= 0 && top >= 0
           && left + width <= _screenWidth && top + height <= _screenHeight;

    /// <summary>
    /// 按设置里的「取图方式」（Auto / Gpu / Cpu）创建桌面复制。
    /// 失败只表示「这一轮用不了」，调用方照常退回 GDI 读屏，不会没画面。
    /// </summary>
    public static bool TryCreate(string preferred, out DesktopFrameSource source, out string reason)
    {
        source = null;

        string mode = Settings.NormalizeGlassSource(preferred);

        // 环境变量还是最高优先级：留着做 A/B 对比（不用改设置就能复现某一侧）。
        string env = Environment.GetEnvironmentVariable("MIRRORPOND_GLASS_SOURCE");
        if (string.Equals(env, "gdi", StringComparison.OrdinalIgnoreCase)) mode = "Cpu";
        else if (string.Equals(env, "dup", StringComparison.OrdinalIgnoreCase)) mode = "Gpu";

        if (mode == "Cpu")
        {
            reason = "设置里选的是「CPU 读屏」（GDI BitBlt）";
            return false;
        }

        // 不再按 OS 生成号直接拒绝 Auto：CreateDuplication 会依次尝试两个 vtable 下标，
        // 并用 GetDesc 校验宽高；两个都失败才退回 GDI。这样 Win10 / 24H2 不再被
        // “没实测过”永久钉死在 18ms 的 GDI 读屏上（G-1）。

        var instance = new DesktopFrameSource();
        if (!instance.Initialize(out reason))
        {
            instance.Dispose();
            return false;
        }

        source = instance;
        return true;
    }

    bool Initialize(out string reason)
    {
        int hr = D3D11CreateDevice(IntPtr.Zero, 1 /* HARDWARE */, IntPtr.Zero,
            0x20 /* BGRA_SUPPORT */, IntPtr.Zero, 0, 7 /* D3D11_SDK_VERSION */,
            out _device, out _, out _context);
        if (hr < 0 || _device == IntPtr.Zero || _context == IntPtr.Zero)
        {
            reason = $"D3D11CreateDevice 失败（0x{hr:X8}）";
            return false;
        }

        _createTexture = Fn<CreateTexture2DFn>(_device, SlotCreateTexture2D);
        _map = Fn<MapFn>(_context, SlotContextMap);
        _unmap = Fn<UnmapFn>(_context, SlotContextUnmap);
        _copyResource = Fn<CopyResourceFn>(_context, SlotContextCopyResource);
        _copyRegion = Fn<CopyRegionFn>(_context, SlotContextCopySubresourceRegion);

        _screenWidth = GetSystemMetrics(0);
        _screenHeight = GetSystemMetrics(1);
        if (_screenWidth < 16 || _screenHeight < 16)
        {
            reason = "取不到屏幕尺寸";
            return false;
        }

        if (!CreateDuplication(out reason)) return false;

        reason = "";
        return true;
    }

    bool CreateDuplication(out string reason)
    {
        IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero, output = IntPtr.Zero, output1 = IntPtr.Zero;
        try
        {
            Guid iidFactory = IidFactory;
            int hr = CreateDXGIFactory1(ref iidFactory, out factory);
            if (hr < 0) { reason = $"CreateDXGIFactory1 失败（0x{hr:X8}）"; return false; }

            _enumAdapters = Fn<EnumAdapters1Fn>(factory, SlotFactoryEnumAdapters1);
            hr = _enumAdapters(factory, 0, out adapter);
            if (hr < 0 || adapter == IntPtr.Zero) { reason = "枚举显卡适配器失败"; return false; }

            _enumOutputs = Fn<EnumOutputsFn>(adapter, SlotAdapterEnumOutputs);
            hr = _enumOutputs(adapter, 0, out output);
            if (hr < 0 || output == IntPtr.Zero) { reason = "枚举显示器输出失败"; return false; }

            Guid iidOutput1 = IidOutput1;
            hr = Marshal.QueryInterface(output, ref iidOutput1, out output1);
            if (hr < 0 || output1 == IntPtr.Zero) { reason = "取 IDXGIOutput1 失败"; return false; }

            // vtable 下标随 dxgi.dll 版本变化：先试本机生成号对应值，再试另一个。
            // 每次都拿 GetDesc 校验宽高；猜错时宁可释放对象退回 GDI，也不能继续用。
            int[] slots = Environment.OSVersion.Version.Build >= 26200
                ? new[] { SlotOutputDuplicateOutputNew, SlotOutputDuplicateOutputOld }
                : new[] { SlotOutputDuplicateOutputOld, SlotOutputDuplicateOutputNew };

            string last = "";
            foreach (int slot in slots)
            {
                _duplicateOutput = Fn<DuplicateOutputFn>(output1, slot);
                hr = _duplicateOutput(output1, _device, out _duplication);
                if (hr < 0 || _duplication == IntPtr.Zero)
                {
                    _duplication = IntPtr.Zero;
                    last = $"DuplicateOutput（vtable 下标 {slot}）失败：0x{hr:X8}";
                    continue;
                }

                // 验收：先问一下这块复制对象的模式，宽高必须和屏幕一致。
                // 下标猜错时它会返回垃圾，这条检查能把「读不了」变成「退回 GDI」而不是画一屏黑。
                _duplicationDesc = Fn<GetDuplicationDescFn>(_duplication, SlotDupGetDesc);
                IntPtr desc = Marshal.AllocHGlobal(64);
                try
                {
                    hr = _duplicationDesc(_duplication, desc);
                    int width = Marshal.ReadInt32(desc, 0);
                    int height = Marshal.ReadInt32(desc, 4);
                    if (hr >= 0 && width == _screenWidth && height == _screenHeight)
                    {
                        _acquire = Fn<AcquireNextFrameFn>(_duplication, SlotDupAcquireNextFrame);
                        _release = Fn<ReleaseFrameFn>(_duplication, SlotDupReleaseFrame);
                        reason = "";
                        return true;
                    }
                    last = $"桌面复制的模式对不上（vtable 下标 {slot}，hr=0x{hr:X8}，{width}x{height}，屏幕 {_screenWidth}x{_screenHeight}）";
                }
                finally
                {
                    Marshal.FreeHGlobal(desc);
                }

                Marshal.Release(_duplication);
                _duplication = IntPtr.Zero;
            }

            reason = string.IsNullOrEmpty(last) ? "桌面复制初始化失败" : last;
            return false;
        }
        finally
        {
            if (output1 != IntPtr.Zero) Marshal.Release(output1);
            if (output != IntPtr.Zero) Marshal.Release(output);
            if (adapter != IntPtr.Zero) Marshal.Release(adapter);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
        }
    }

    /// <summary>只给「卡片整叠区域」建 staging（整屏 staging 每轮要拷 8 MB，区域只要几百 KB）。</summary>
    bool EnsureStaging(int width, int height)
    {
        if (_staging != IntPtr.Zero && _stagingWidth == width && _stagingHeight == height)
            return true;

        if (_staging != IntPtr.Zero) { Marshal.Release(_staging); _staging = IntPtr.Zero; }

        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = FormatBgra,
            Sample = new SampleDesc { Count = 1, Quality = 0 },
            Usage = UsageStaging,
            BindFlags = 0,
            CPUAccessFlags = CpuAccessRead,
            MiscFlags = 0
        };

        IntPtr descPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Texture2DDesc)));
        try
        {
            Marshal.StructureToPtr(desc, descPtr, false);
            int hr = _createTexture(_device, descPtr, IntPtr.Zero, out _staging);
            if (hr < 0 || _staging == IntPtr.Zero)
            {
                // P1-7：staging 建不上是桌面复制终停的入口之一，必须留 HRESULT。
                Log.Warn($"DXGI staging 纹理创建失败（{width}x{height}，hr=0x{hr:X8}）");
                _staging = IntPtr.Zero;
                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(descPtr);
        }

        _stagingWidth = width;
        _stagingHeight = height;
        _frameAvailable = false;
        return true;
    }

    /// <summary>
    /// 把取图源推进到不可用状态（P1-7）。置位是一次性的：第一次进入 dead 时写一条
    /// 带原因（含 HRESULT）的 WARN，之后 TryReadInto 静默返回 false（调用方退避重试），
    /// 不再逐帧刷日志。
    /// </summary>
    void EnterDead(string reason)
    {
        if (_dead) return;
        _dead = true;
        Log.Warn($"桌面复制进入不可用状态（本轮起回退 GDI，GlassFeed 会退避后重建）: {reason}");
    }

    /// <summary>
    /// 把屏幕区域 (left, top, width, height) 读进一块 32bpp 自顶向下的目标位图。
    /// 返回 false 表示这一轮读不了，调用方应退回 GDI 读屏。
    /// </summary>
    public bool TryReadInto(IntPtr destinationBits, int destinationStride,
                            int left, int top, int width, int height)
    {
        if (_dead || _disposed) return false;
        if (destinationBits == IntPtr.Zero || width < 1 || height < 1) return false;

        // 复制对象只覆盖主屏输出。请求矩形必须完全落在输出内：
        // 以前这里把 left/top 钳到 0，共享图仍按原 origin 取块 —— 副屏在左时会整叠读错像素；
        // 副屏在右时 width 变负又静默返回 false。这里不做任何改写，越界就明确交给 GDI。
        if (left < 0 || top < 0 ||
            left + width > _screenWidth || top + height > _screenHeight)
        {
            if (!_rangeWarned)
            {
                _rangeWarned = true;
                Log.Warn($"桌面复制请求区域超出主屏输出（{left},{top} {width}x{height}，输出 {_screenWidth}x{_screenHeight}），该区域改走 GDI 读屏");
            }
            return false;
        }

        if (!EnsureStaging(width, height)) { EnterDead("staging 纹理创建失败（详见上方日志）"); return false; }

        bool regionKnown = _frameAvailable && _frameLeft == left && _frameTop == top
                           && _frameWidth == width && _frameHeight == height;

        IntPtr frameInfo = IntPtr.Zero;
        IntPtr resource = IntPtr.Zero;
        IntPtr texture = IntPtr.Zero;
        IntPtr box = IntPtr.Zero;
        bool acquired = false;

        try
        {
            // 每轮都要问一次有没有新帧。桌面没变化时 AcquireNextFrame 会超时（这是常态），
            // 此时只要缓存的那块正好是要的区域，目标位图里就已经是同一张图了，可以直接返回
            // —— 静置时一轮的成本就是这一次 AcquireNextFrame（~0.05 ms）。
            //
            // 刚创建复制对象时第一次拿到的是黑帧（合成器还没有新帧可给），所以拿到实帧之前
            // 用一段有限等待把它「预热」出来，预热完就回到 0 超时，不再等。
            frameInfo = Marshal.AllocHGlobal(128);
            int hr = _acquire(_duplication, _primed ? 0u : 50u, frameInfo, out resource);

            if (hr == DxgiErrorWaitTimeout) return regionKnown;

            if (hr < 0)
            {
                if (hr == DxgiErrorAccessLost || hr == DxgiErrorNotFound) DropDuplication();
                _failures++;
                if (_failures > 3)
                    EnterDead($"AcquireNextFrame 连续失败（hr=0x{hr:X8}）");
                return false;
            }

            acquired = true;
            Guid iidTexture = IidTexture2D;
            hr = Marshal.QueryInterface(resource, ref iidTexture, out texture);
            if (hr < 0 || texture == IntPtr.Zero) return false;

            // 只拷要的那一块（D3D11_BOX：left, top, front, right, bottom, back）
            box = Marshal.AllocHGlobal(24);
            Marshal.WriteInt32(box, 0, left);
            Marshal.WriteInt32(box, 4, top);
            Marshal.WriteInt32(box, 8, 0);
            Marshal.WriteInt32(box, 12, left + width);
            Marshal.WriteInt32(box, 16, top + height);
            Marshal.WriteInt32(box, 20, 1);
            _copyRegion(_context, _staging, 0, 0, 0, 0, texture, 0, box);

            _frameLeft = left;
            _frameTop = top;
            _frameWidth = width;
            _frameHeight = height;
            _frameAvailable = true;

            int mapHr = _map(_context, _staging, 0, MapRead, 0, out var mapped);
            if (mapHr < 0)
            {
                _failures++;
                if (_failures > 3)
                    EnterDead($"Map 读回失败（hr=0x{mapHr:X8}）");
                _frameAvailable = false;
                return false;
            }

            try
            {
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    MoveMemory(new IntPtr(destinationBits.ToInt64() + (long)y * destinationStride),
                               new IntPtr(mapped.Data.ToInt64() + (long)y * mapped.RowPitch),
                               new IntPtr(rowBytes));
                }

                // 预热期间检查这一帧是不是全黑（抽上 / 中 / 下三行）；全黑就当没拿到，本轮退回 GDI，
                // 免得黑帧把「连续 3 帧全黑 → 退回系统亚克力」的保险丝点着了。
                if (!_primed)
                {
                    bool blank = true;
                    var probe = new byte[rowBytes];
                    int[] rows = { 0, height / 2, height - 1 };
                    foreach (int rowIndex in rows)
                    {
                        Marshal.Copy(new IntPtr(destinationBits.ToInt64() + (long)rowIndex * destinationStride),
                                     probe, 0, rowBytes);
                        for (int i = 0; i + 2 < rowBytes; i += 4)
                        {
                            if (probe[i] != 0 || probe[i + 1] != 0 || probe[i + 2] != 0) { blank = false; break; }
                        }
                        if (!blank) break;
                    }

                    if (blank)
                    {
                        _frameAvailable = false;
                        if (++_primingAttempts > 6)
                            EnterDead($"预热 {(_primingAttempts - 1)} 轮全是黑帧（合成器未给实帧）");
                        return false;
                    }

                    _primed = true;
                    _primingAttempts = 0;
                    Log.Info($"桌面复制已就绪：pitch={mapped.RowPitch}，区域 {left},{top} {width}x{height}");
                }
            }
            finally
            {
                _unmap(_context, _staging, 0);
            }

            _failures = 0;
            return true;
        }
        finally
        {
            if (acquired)
            {
                try { _release(_duplication); } catch { }
            }
            if (box != IntPtr.Zero) Marshal.FreeHGlobal(box);
            if (texture != IntPtr.Zero) Marshal.Release(texture);
            if (resource != IntPtr.Zero) Marshal.Release(resource);
            if (frameInfo != IntPtr.Zero) Marshal.FreeHGlobal(frameInfo);
        }
    }

    void DropDuplication()
    {
        if (_duplication != IntPtr.Zero)
        {
            Marshal.Release(_duplication);
            _duplication = IntPtr.Zero;
        }
        _frameAvailable = false;
        _primed = false;
        _frameLeft = int.MinValue;
        _frameTop = int.MinValue;

        if (!CreateDuplication(out var reason))
        {
            _dead = true;
            Log.Warn($"桌面复制失效后重建失败，改回 GDI 读屏：{reason}");
        }
    }

    void ReleaseAll()
    {
        if (_staging != IntPtr.Zero) { Marshal.Release(_staging); _staging = IntPtr.Zero; }
        if (_duplication != IntPtr.Zero) { Marshal.Release(_duplication); _duplication = IntPtr.Zero; }
        if (_context != IntPtr.Zero) { Marshal.Release(_context); _context = IntPtr.Zero; }
        if (_device != IntPtr.Zero) { Marshal.Release(_device); _device = IntPtr.Zero; }
        _stagingWidth = 0;
        _stagingHeight = 0;
        _frameAvailable = false;
        _primed = false;
        _frameLeft = int.MinValue;
        _frameTop = int.MinValue;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAll();
    }

    /// <summary>
    /// 目标共享 DIB 被重建（尺寸变化 / Detach 后重新挂接）时调用：
    /// 让下一次 TryReadInto 不再相信“同一区域已拷贝”，否则桌面静止时
    /// AcquireNextFrame 超时会直接返回 true，共享图仍是全零 → 黑帧误触发降级（L-3）。
    /// </summary>
    public void InvalidateDestination() => _frameAvailable = false;

    static IntPtr Method(IntPtr instance, int slot) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size);

    static T Fn<T>(IntPtr instance, int slot) where T : class => (T)(object)Marshal.GetDelegateForFunctionPointer(Method(instance, slot), typeof(T));
}
