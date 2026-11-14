using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 毛玻璃取样：把卡片“后面”的画面按像素拼出来（多个窗口 + 桌面壁纸），再缩小模糊。
///
/// 做法：枚举卡片矩形内、从上层到下层的外部窗口，每个窗口用
/// <c>PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)</c> 画进离屏位图（这张图里没有叠层自己），
/// 最后把桌面窗口（Progman）垫在最底层。生成小图时，每个采样点取“盖住它的最上层窗口”的像素，
/// 所以卡片跨窗口、压住窗口边角、或者干脆只压在桌面上，都能取到正确的底色。
///
/// 全程不抓屏：截图 / 录屏 / 共享屏幕里叠层始终可见（旧的抓屏排除方案会让卡片在 OBS 里周期性消失）。
/// </summary>
internal static class GlassCapture
{
    const uint PW_RENDERFULLCONTENT = 0x00000002;
    const uint BiRGB = 0;

    /// <summary>普通窗口的取样缓存时长（毫秒）。</summary>
    public const int CacheMs = 150;

    /// <summary>桌面壁纸变化很慢，缓存久一点，省掉重复的整屏重绘。</summary>
    public const int DesktopCacheMs = 2000;

    /// <summary>一张卡片最多参考几层窗口（含桌面）。</summary>
    const int MaxLayers = 5;

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);

    // G-15：DWMWA_CLOAKED = 14；虚拟桌面 / 挂起的 UWP 窗口仍然 IsWindowVisible，
    // 但内容不该被当成“卡片后面的窗口”采样。
    const int DwmwaCloaked = 14;
    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage,
                                          out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>一层背景：一个窗口 + 它在屏幕上的矩形；`Window == 0` 表示合成出来的桌面壁纸层。</summary>
    public struct Layer
    {
        public IntPtr Window;
        public RECT Rect;
        public bool Desktop;
    }

    /// <summary>一次性缓存：同一个窗口在一个时间片内只重绘一次。</summary>
    sealed class Frame
    {
        public IntPtr Window;
        public long Stamp;
        public int Width;
        public int Height;
        public byte[] Pixels;
        public bool Blank;
    }

    static readonly List<Frame> Frames = new(6);
    // 兼容取样每组卡片各起一个 Task，N 张卡会并发进出 Grab/GetFramePixels。
    // 这个缓存是进程级的，必须串行访问；注释里“只在 UI 线程”的假设不成立（G-3）。
    static readonly object FramesSync = new();

    // ==================== 共享候选窗口（G-6） ====================
    // 兼容取样每张卡各有自己的定时器（GlassRuntime），以前每张卡每个 tick 都重新
    // EnumWindows 全表枚举一次。候选窗口与卡片矩形无关，这里按短 TTL 共享一份快照，
    // 各卡只按自己的矩形过滤；刷新发生在采集任务里（调用方已不在 UI 线程）。
    sealed class Candidate
    {
        public IntPtr Window;
        public RECT Rect;
    }

    const int CandidatesTtlMs = 120;
    const int MaxCandidates = 256;

    static readonly List<Candidate> Candidates = new(64);
    static readonly object CandidatesSync = new();
    static long _candidatesStamp;

    /// <summary>
    /// 与候选窗口同一份快照的虚拟屏矩形。以前每张卡在采集任务里各调一次
    /// <c>Screen.AllScreens</c>（WinForms 缓存并非为多线程并发调用设计），
    /// 现在跟候选列表一起在同一把锁内刷新、读取。
    /// </summary>
    static RECT _virtualScreen;

    static readonly LogThrottle _enumFailure = new("枚举卡片后方窗口", 60000);

    // pid → 进程名缓存：截图 / 录屏工具的过滤要按进程名判断，而每帧对每个候选
    // 开一次进程句柄在内核上是可观开销。进程名几乎不变，5 秒 TTL 足够。
    struct ProcessNameEntry
    {
        public string Name;
        public long Stamp;
    }

    const int ProcessNameTtlMs = 5000;

    static readonly Dictionary<uint, ProcessNameEntry> ProcessNames = new();
    static readonly object ProcessNamesSync = new();
    static readonly LogThrottle _processNameFailure = new("解析窗口进程名", 60000);
    static readonly LogThrottle _ownPidFailure = new("读取本进程 pid", 60000);
    static readonly LogThrottle _classNameFailure = new("读取窗口类名", 60000);
    static readonly LogThrottle _virtualScreenFailure = new("读取虚拟屏幕尺寸", 60000);

    // ==================== 找卡片后面的窗口 ====================

    /// <summary>
    /// 收集卡片矩形内、从上层到下层的外部窗口，最后补一层桌面壁纸。
    /// 跳过本进程窗口、截图 / 录屏工具浮层、以及拖拽图标时的拖动影像窗口，
    /// 否则它们会让毛玻璃变色 / 闪烁。
    /// </summary>
    public static List<Layer> FindLayers(int left, int top, int width, int height)
    {
        var layers = new List<Layer>(MaxLayers);
        int right = left + width;
        int bottom = top + height;
        RECT screen;

        lock (CandidatesSync)
        {
            RefreshCandidatesLocked();

            // EnumWindows 的顺序就是从上层到下层，因此这里取到的就是盖住卡片的最上面几层。
            // 矩形是枚举时刻的快照（最多晚一个 TTL）；窗口在这期间挪动时，PrintWindow
            // 仍会抓到最新内容，只是命中测试可能差一帧——对模糊背景无感。
            foreach (Candidate candidate in Candidates)
            {
                if (layers.Count >= MaxLayers) break;

                RECT rect = candidate.Rect;
                if (rect.Right <= left || rect.Left >= right) continue;
                if (rect.Bottom <= top || rect.Top >= bottom) continue;

                layers.Add(new Layer { Window = candidate.Window, Rect = rect });
            }

            screen = _virtualScreen;
        }

        // 最后垫一层合成出来的桌面壁纸（覆盖整个虚拟屏幕），保证卡片压在桌面上也有内容。
        if (screen.Width > 0 && screen.Height > 0)
            layers.Add(new Layer { Window = IntPtr.Zero, Rect = screen, Desktop = true });

        return layers;
    }

    /// <summary>
    /// 刷新共享候选窗口（调用方必须持有 <see cref="CandidatesSync"/>）。
    /// 桌面 / 拖动影像 / 截图浮层 / 本进程窗口的过滤与卡片矩形无关，在这里做一次，
    /// 所有卡片的 FindLayers 都只做矩形相交。
    /// </summary>
    static void RefreshCandidatesLocked()
    {
        long now = Environment.TickCount64;
        if (_candidatesStamp != 0 && now - _candidatesStamp <= CandidatesTtlMs) return;

        // 先推进时间戳：枚举本身失败时也要等一个 TTL 再重试，不能每个 tick 都刷一条 WARN。
        _candidatesStamp = now;

        uint self = GetOwnProcessId();
        var fresh = new List<Candidate>(64);
        bool ok = true;

        try
        {
            EnumWindows((hwnd, param) =>
            {
                if (hwnd == IntPtr.Zero) return true;
                if (fresh.Count >= MaxCandidates) return false;
                if (!IsWindowVisible(hwnd)) return true;
                if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                if (!GetWindowRect(hwnd, out var rect)) return true;
                if (rect.Width < 16 || rect.Height < 16) return true;

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == self) return true;

                string cls = ClassNameOf(hwnd);

                // 桌面（Progman / WorkerW / SHELLDLL_DefView）不再用 PrintWindow 抓：
                // 壁纸由 DWM 绘制，抓到的经常是黑的；拖图标时还会让桌面重绘。
                // 统一换成 FindLayers 末尾合成的壁纸层。
                if (IsDesktopClass(cls)) return true;

                // 拖动桌面图标 / 文件时，Explorer 会挂一个跟着鼠标走的拖动影像窗口，
                // 它盖在卡片区域上时会把玻璃变成一块扭动的图标残影，直接忽略。
                if (IsDragImageClass(cls)) return true;

                if (IsCaptureOverlay(cls, pid)) return true;

                fresh.Add(new Candidate { Window = hwnd, Rect = rect });
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            ok = false;
            _enumFailure.Failure(ex.Message);
        }

        Candidates.Clear();
        Candidates.AddRange(fresh);
        _virtualScreen = VirtualScreenRect();
        if (ok) _enumFailure.Success();
    }

    /// <summary>所有显示器并起来的虚拟屏幕（物理像素）。</summary>
    static RECT VirtualScreenRect()
    {
        try
        {
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0) return default(RECT);

            System.Drawing.Rectangle union = screens[0].Bounds;
            for (int i = 1; i < screens.Length; i++) union = System.Drawing.Rectangle.Union(union, screens[i].Bounds);

            return new RECT { Left = union.Left, Top = union.Top, Right = union.Right, Bottom = union.Bottom };
        }
        catch (Exception ex)
        {
            // 拿不到虚拟屏矩形就没有桌面壁纸层，卡片压在桌面上会取不到画面；
            // 这是可见降级，不能静默（节流避免每帧一条）。
            _virtualScreenFailure.Failure(ex.Message);
            return default(RECT);
        }
    }

    static string ClassNameOf(IntPtr hwnd)
    {
        try
        {
            var buffer = new StringBuilder(128);
            if (GetClassName(hwnd, buffer, 128) > 0) return buffer.ToString();
        }
        catch (Exception ex)
        {
            // 类名只用于过滤（桌面 / 拖动影像 / 截图浮层），取不到就会少一层过滤，
            // 但仍能继续取样；记一条节流日志留下线索。
            _classNameFailure.Failure($"hwnd={hwnd}: {ex.Message}");
        }
        return "";
    }

    static bool IsDesktopClass(string cls) => string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cls, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase);

    /// <summary>拖动图标 / 文件时跟随鼠标的拖动影像窗口。</summary>
    static bool IsDragImageClass(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return false;
        return cls.IndexOf("drag", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 截图 / 录屏工具的浮层：Windows 截图工具（ScreenClippingHost / ScreenSketch）、
    /// 以及常见的第三方截图、共享工具。它们的浮层盖在最上层，一旦被当成背景取用，
    /// 毛玻璃会突然变亮或变灰——所以直接跳过，继续取下面真正的内容。
    /// </summary>
    static bool IsCaptureOverlay(string cls, uint pid)
    {
        if (!string.IsNullOrEmpty(cls))
        {
            if (cls.IndexOf("screenclip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (cls.IndexOf("snipping", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (cls.IndexOf("screenshot", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        string name = ProcessNameOf(pid);
        switch (name)
        {
            case "snippingtool":
            case "screensketch":
            case "screenclippinghost":
            case "snipaste":
            case "sniptool":
            case "nvidia share":
            case "nvsphelper64":
            case "sharex":
            case "picpick":
            case "greenshot":
                return true;
            default:
                return false;
        }
    }

    static string ProcessNameOf(uint pid)
    {
        if (pid == 0) return "";

        long now = Environment.TickCount64;
        lock (ProcessNamesSync)
        {
            if (ProcessNames.TryGetValue(pid, out var cached) && now - cached.Stamp <= ProcessNameTtlMs)
                return cached.Name;
        }

        string name;
        try
        {
            using (Process process = Process.GetProcessById((int)pid))
                name = (process.ProcessName ?? "").Trim().ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            // 窗口在枚举与查询之间关闭 / 进程已退出：这是正常竞态，不是故障。
            // 负结果同样进缓存，避免每帧对同一个已退出的 pid 反复开进程句柄。
            name = "";
        }
        catch (InvalidOperationException)
        {
            // 进程在 GetProcessById 之后、读 ProcessName 之前退出：同样按竞态处理。
            name = "";
        }
        catch (Exception ex)
        {
            _processNameFailure.Failure($"pid={pid}: {ex.Message}");
            name = "";
        }

        lock (ProcessNamesSync)
        {
            // pid 会复用，缓存不能无限增长；超过上限时先清过期项，全新鲜就整体清空。
            if (ProcessNames.Count >= 256)
            {
                var stale = new List<uint>();
                foreach (KeyValuePair<uint, ProcessNameEntry> pair in ProcessNames)
                {
                    if (now - pair.Value.Stamp > ProcessNameTtlMs) stale.Add(pair.Key);
                }

                if (stale.Count == 0) ProcessNames.Clear();
                else foreach (uint key in stale) ProcessNames.Remove(key);
            }

            ProcessNames[pid] = new ProcessNameEntry { Name = name, Stamp = now };
        }

        return name;
    }

    static uint _ownPid;

    static uint GetOwnProcessId()
    {
        if (_ownPid != 0) return _ownPid;
        try { _ownPid = (uint)Process.GetCurrentProcess().Id; }
        catch (Exception ex)
        {
            // 拿不到自己的 pid 就过滤不掉本进程窗口，玻璃会把叠层自己拍进去；
            // 只按节流记一条，且不缓存失败结果，下一轮还会重试。
            _ownPidFailure.Failure(ex.Message);
            _ownPid = 0;
        }
        return _ownPid;
    }

    // ==================== 取样 ====================

    /// <summary>
    /// 用 <paramref name="layers"/>（上 → 下）拼出卡片区域的模糊背景；
    /// 返回 null 表示这次什么都取不到（调用方保留上一帧）。
    /// </summary>
    public static BitmapSource Grab(List<Layer> layers, int cardLeft, int cardTop,
                                    int cardWidth, int cardHeight, double blur)
    {
        if (layers is null || layers.Count == 0 || cardWidth <= 0 || cardHeight <= 0) return null;

        // 取样精度：以前最多缩到 1/12，一张 400px 宽的卡片只剩几十像素，放大后基本是一块纯色。
        // 现在只缩到 1/2–1/4，模糊半径仍按物理像素换算，玻璃里能留下画面结构（文字块、窗口边缘）。
        int divisor = Math.Max(2, Math.Min(4, (int)Math.Round(Math.Max(0, blur) / 12.0) + 2));
        int targetWidth = Math.Max(8, cardWidth / divisor);
        int targetHeight = Math.Max(8, cardHeight / divisor);

        // 每层整窗像素（带缓存）；桌面层用壁纸合成，缩放 1/2，需要单独记录比例。
        var pixels = new byte[layers.Count][];
        var scales = new double[layers.Count];
        var sourceWidths = new int[layers.Count];
        var sourceHeights = new int[layers.Count];

        for (int i = 0; i < layers.Count; i++)
        {
            Layer layer = layers[i];

            if (layer.Window == IntPtr.Zero)
            {
                if (!DesktopBackground.TryGet(layer.Rect, out var frame)) continue;

                pixels[i] = frame.Pixels;
                sourceWidths[i] = frame.Width;
                sourceHeights[i] = frame.Height;
                scales[i] = layer.Rect.Width > 0 ? layer.Rect.Width / (double)frame.Width : 1.0;
                continue;
            }

            if (layer.Rect.Width <= 0 || layer.Rect.Height <= 0) continue;
            pixels[i] = GetFramePixels(layer.Window, layer.Rect.Width, layer.Rect.Height,
                                       layer.Desktop ? DesktopCacheMs : CacheMs);
            sourceWidths[i] = layer.Rect.Width;
            sourceHeights[i] = layer.Rect.Height;
            scales[i] = 1.0;
        }

        var small = new byte[targetWidth * targetHeight * 4];
        bool any = false;

        for (int y = 0; y < targetHeight; y++)
        {
            int sy0 = cardTop + (int)((long)y * cardHeight / targetHeight);
            int sy1 = cardTop + (int)((long)(y + 1) * cardHeight / targetHeight);
            if (sy1 <= sy0) sy1 = sy0 + 1;

            int destRow = y * targetWidth * 4;

            for (int x = 0; x < targetWidth; x++)
            {
                int sx0 = cardLeft + (int)((long)x * cardWidth / targetWidth);
                int sx1 = cardLeft + (int)((long)(x + 1) * cardWidth / targetWidth);
                if (sx1 <= sx0) sx1 = sx0 + 1;

                // 每个目标像素在它覆盖的源矩形里取 2×2 个样本求平均：
                // 比原来「只取一个点」更稳（不会正好踩到文字边缘），也不会把每个源像素都读一遍。
                int dx = Math.Max(1, (sx1 - sx0) / 2);
                int dy = Math.Max(1, (sy1 - sy0) / 2);

                int sumB = 0, sumG = 0, sumR = 0, hits = 0;

                for (int oy = 0; oy < 2; oy++)
                {
                    int sy = sy0 + oy * dy;
                    if (sy >= sy1) sy = sy1 - 1;

                    for (int ox = 0; ox < 2; ox++)
                    {
                        int sx = sx0 + ox * dx;
                        if (sx >= sx1) sx = sx1 - 1;

                        int source = -1;
                        for (int i = 0; i < layers.Count; i++)
                        {
                            Layer layer = layers[i];
                            if (pixels[i] is null) continue;
                            if (sx < layer.Rect.Left || sx >= layer.Rect.Right) continue;
                            if (sy < layer.Rect.Top || sy >= layer.Rect.Bottom) continue;
                            source = i;
                            break;
                        }

                        if (source < 0) continue;

                        Layer hit = layers[source];
                        double scale = scales[source] > 0 ? scales[source] : 1.0;

                        int px = (int)((sx - hit.Rect.Left) / scale);
                        int py = (int)((sy - hit.Rect.Top) / scale);
                        if (px < 0) px = 0;
                        if (py < 0) py = 0;
                        if (px >= sourceWidths[source]) px = sourceWidths[source] - 1;
                        if (py >= sourceHeights[source]) py = sourceHeights[source] - 1;

                        int s = (py * sourceWidths[source] + px) * 4;
                        byte[] buffer = pixels[source];
                        if (s + 3 >= buffer.Length) continue;

                        sumB += buffer[s];
                        sumG += buffer[s + 1];
                        sumR += buffer[s + 2];
                        hits++;
                    }
                }

                int d = destRow + x * 4;
                small[d + 3] = 255;

                if (hits == 0)
                {
                    // 没有任何一层覆盖这个点（连桌面都没取到）：留黑，最后统一判空
                    continue;
                }

                small[d] = (byte)(sumB / hits);
                small[d + 1] = (byte)(sumG / hits);
                small[d + 2] = (byte)(sumR / hits);
                any = true;
            }
        }

        if (!any || IsBlank(small)) return null;

        int radius = (int)Math.Round(Math.Max(0, blur) / Math.Max(2, divisor));
        if (radius > 0)
        {
            // 三次盒式模糊已经很接近高斯，过渡比原来的两次更自然；滑动窗口实现，
            // 半径变大也只是线性开销。
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(small, targetWidth, targetHeight, radius, true);
                BoxBlur(small, targetWidth, targetHeight, radius, false);
            }
        }

        BitmapSource image = BitmapSource.Create(targetWidth, targetHeight, 96, 96,
                                                 PixelFormats.Bgr32, null, small, targetWidth * 4);
        image.Freeze();
        return image;
    }

    static byte[] GetFramePixels(IntPtr window, int width, int height, int cacheMs)
    {
        long now = Environment.TickCount64;

        lock (FramesSync)
        {
            for (int i = 0; i < Frames.Count; i++)
            {
                Frame cached = Frames[i];
                if (cached.Window != window) continue;
                if (cached.Width != width || cached.Height != height) continue;
                if (now - cached.Stamp > cacheMs) continue;
                return cached.Blank ? null : cached.Pixels;
            }
        }

        byte[] fresh = PrintWindowPixels(window, width, height);
        bool blank = fresh is null || IsBlank(fresh);

        lock (FramesSync)
        {
            Frames.RemoveAll(f => f.Window == window);
            Frames.Add(new Frame
            {
                Window = window,
                Stamp = now,
                Width = width,
                Height = height,
                Pixels = fresh,
                Blank = blank
            });
            if (Frames.Count > 8) Frames.RemoveAt(0);
        }

        return blank ? null : fresh;
    }

    /// <summary>把目标窗口整块画进 32bpp 位图（不含叠层自己）。</summary>
    static byte[] PrintWindowPixels(IntPtr window, int width, int height)
    {
        IntPtr hdc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;

        try
        {
            hdc = GetDC(IntPtr.Zero);
            memoryDc = CreateCompatibleDC(hdc);
            if (memoryDc == IntPtr.Zero) return null;

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRGB
                }
            };

            dib = CreateDIBSection(memoryDc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;

            previous = SelectObject(memoryDc, dib);

            if (!PrintWindow(window, memoryDc, PW_RENDERFULLCONTENT))
            {
                lock (FramesSync) { Frames.RemoveAll(f => f.Window == window); }
                return null;
            }

            int stride = width * 4;
            int bytes = stride * height;
            byte[] buffer = new byte[bytes];
            Marshal.Copy(bits, buffer, 0, bytes);
            return buffer;
        }
        catch (Exception ex)
        {
            Log.Warn($"PrintWindow 取样失败: {ex.Message}");
            return null;
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previous != IntPtr.Zero) SelectObject(memoryDc, previous);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    static bool IsBlank(byte[] pixels)
    {
        // 有些程序（独占全屏 / 硬件覆盖层）的 PrintWindow 只会给出全黑，
        // 这时候宁可丢弃这一层，也不要让卡片变成黑块。
        int step = Math.Max(4, (pixels.Length / 4096) / 4 * 4);
        int bright = 0, total = 0;

        for (int i = 0; i + 2 < pixels.Length; i += step)
        {
            total++;
            if (pixels[i] > 8 || pixels[i + 1] > 8 || pixels[i + 2] > 8) bright++;
        }

        return total == 0 || bright * 100 / Math.Max(1, total) < 2;
    }

    /// <summary>
    /// 单方向方框模糊，滑动窗口实现：每行 / 每列只扫一遍，半径再大也是 O(像素数)。
    /// 三次 H + V 组合接近高斯，配合更细的取样图，玻璃不会糊成一块纯色。
    /// </summary>
    internal static void BoxBlur(byte[] buffer, int width, int height, int radius, bool horizontal)
    {
        if (radius <= 0 || width < 3 || height < 3) return;

        int length = horizontal ? width : height;
        int lines = horizontal ? height : width;
        int step = horizontal ? 4 : width * 4;
        var line = new int[length * 4];

        for (int index = 0; index < lines; index++)
        {
            int start = horizontal ? index * width * 4 : index * 4;

            int sumB = 0, sumG = 0, sumR = 0;
            int window = 0;

            int last = Math.Min(radius, length - 1);
            for (int i = 0; i <= last; i++)
            {
                int p = start + i * step;
                sumB += buffer[p];
                sumG += buffer[p + 1];
                sumR += buffer[p + 2];
                window++;
            }

            for (int i = 0; i < length; i++)
            {
                int q = i * 4;
                line[q] = sumB / window;
                line[q + 1] = sumG / window;
                line[q + 2] = sumR / window;

                int add = i + radius + 1;
                if (add < length)
                {
                    int p = start + add * step;
                    sumB += buffer[p];
                    sumG += buffer[p + 1];
                    sumR += buffer[p + 2];
                    window++;
                }

                int remove = i - radius;
                if (remove >= 0)
                {
                    int p = start + remove * step;
                    sumB -= buffer[p];
                    sumG -= buffer[p + 1];
                    sumR -= buffer[p + 2];
                    window--;
                }
            }

            for (int i = 0; i < length; i++)
            {
                int q = start + i * step;
                buffer[q] = (byte)line[i * 4];
                buffer[q + 1] = (byte)line[i * 4 + 1];
                buffer[q + 2] = (byte)line[i * 4 + 2];
            }
        }
    }
}
