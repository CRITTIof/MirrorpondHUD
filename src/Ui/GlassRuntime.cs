using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 一块卡片窗口的毛玻璃运行时：底板选择 / 创建 / 退役、截屏排除、兼容取图定时器与运行期降级。
///
/// 三种实现：
/// 1) 系统亚克力（默认）：卡片下面一个独立的底板窗口，挂未文档化的 accent 亚克力，
///    DWM / GPU 负责模糊，进程侧零逐帧工作，模糊半径由系统决定；
/// 2) 桌面取样（设置值 DesktopFeed）：截屏 + 自己模糊，把结果**画进卡片自己的内容层**（见 GlassFeed）。
///    代价：为了让截屏不拍到自己，卡片必须排除在截屏之外 —— 这个模式下截图 / 录像里没有卡片；
/// 3) 兼容：把卡片后面的画面拼出来（枚举窗口 + PrintWindow + 壁纸），缩小模糊后当底色，
///    可调半径与帧率，任何系统都能用。
/// 1 / 2 不可用时自动退回 3。
///
/// 从 <see cref="OverlayWindow"/> 迁入（2026-11 审阅第三批第 12 项）：窗口只保留
/// 「什么时候通知」与自身句柄 / 可见性。
///
/// 状态单源（AGENTS.md §10 推迟项落地）：谁 active、谁不可用、是否正在抓图只有
/// <see cref="_state"/> 这一份真相，对外只读 <see cref="State"/>；所有实现切换 / 降级
/// 统一走 <see cref="SetState"/>，日志也只在「实现或原因真的变了」时记一条 —— 以前每次
/// Apply 失败都 Warn，拖动时会把主日志刷满。Acrylic / Glass 双套模糊字段已由 Schema 7
/// 合并为 <c>Style.BlurRadius</c> / <c>Style.BlurFps</c>，这里不再区分两套参数。
/// </summary>
internal sealed class GlassRuntime : IDisposable
{
    readonly OverlayWindow _window;

    /// <summary>两块底板（系统亚克力 / 桌面取样）。谁 active 由 <see cref="GlassPolicy"/> 决定。</summary>
    readonly SystemAcrylicBackdrop _systemBackdrop = new();
    readonly DesktopAcrylicBackdrop _desktopBackdrop = new();

    /// <summary>当前实际生效的玻璃实现。每次变化换一个新实例，读到的对象自洽。</summary>
    GlassRuntimeState _state = new(GlassBackdropKind.None, "", "", true, true);

    /// <summary>
    /// 降级原因：用户选的实现用不了时（桌面取样被判不可用 / 底板应用失败）记住原因，
    /// 由随后的重新决策写进 <see cref="State"/>.Reason。只在设置重新应用（<see cref="ResetAvailability"/>）、
    /// 用户改了玻璃实现方式、或桌面取样恢复成功时清掉，避免每次 UpdateBackdrop 都把同一件事记一遍日志。
    /// </summary>
    string _fallbackReason = "";

    /// <summary>
    /// 上一次决策时用的 GlassMode：设置应用走的是 RefreshSurface（不经过 ResetAvailability），
    /// 靠它识别「用户改了实现方式」，把上一种实现留下的降级原因作废。
    /// </summary>
    string _lastMode;

    DispatcherTimer _glassTimer;
    bool _glassBusy;
    // 采集代次与开始时刻：PrintWindow 没有超时，目标进程挂起时 _glassBusy 会永远卡在
    // true。超时后作废旧代次并放开 busy；旧结果回来时按代次丢弃（G-5）。
    long _captureGen;
    DateTime _captureStartedUtc;

    public GlassRuntime(OverlayWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// 当前实际生效的玻璃状态（单一数据源）。窗口的 <c>GlassState</c>、设置窗的「当前实际」、
    /// 拖动补帧都读它，不再各自按设置重算。
    /// </summary>
    public GlassRuntimeState State => _state;

    /// <summary>
    /// 有一块底板正在卡片下面画模糊。三种实现是互斥的：谁在干活谁就是 true，
    /// 切换实现时另一块必须收掉（否则会留下没人管的玻璃板）。
    /// </summary>
    public bool BackdropActive => _systemBackdrop.IsActive || _desktopBackdrop.IsActive;

    /// <summary>设置变了就再给两块底板一次机会（底板对象保留，不重建），降级原因也一并作废。</summary>
    public void ResetAvailability()
    {
        _systemBackdrop.ResetAvailability();
        _desktopBackdrop.ResetAvailability();
        _fallbackReason = "";
    }

    /// <summary>
    /// 决定这一帧由谁提供模糊：纯决策在 <see cref="GlassPolicy"/>，这里只负责
    /// 「收掉不该用的底板 → 让该用的那块接管」。两块互斥，切走时必须先 Retire，
    /// 否则会留下没人管的玻璃板（系统亚克力会留灰板，桌面取样会留「卡片不进截图」）。
    /// </summary>
    public void UpdateBackdrop()
    {
        if (!_window.GlassHandleReady) return;

        Settings settings = _window.GlassSettings;

        // 用户改了实现方式：上一种实现留下的降级原因不再适用（否则「当前实际」会一直挂着旧原因）。
        string mode = Settings.NormalizeGlassMode(settings.Style.GlassMode);
        if (!string.Equals(mode, _lastMode, StringComparison.Ordinal))
        {
            _lastMode = mode;
            _fallbackReason = "";
        }

        // 取样参数（取图方式 / 模糊强度 / 刷新帧率 / 是否留投影边）随时可能被改。
        // 放在 acrylic 判断之前，免得「先关毛玻璃改设置、再打开」时沿用旧参数。
        // Schema 7 起兼容取样与桌面取样共用 BlurRadius / BlurFps 一套字段。
        GlassFeed.Configure(settings.Style.GlassSource, settings.Style.BlurRadius,
                            settings.Style.BlurFps, settings.Overlay.Shadow);

        if (!settings.Style.Acrylic)
        {
            // 毛玻璃没开：两块底板都要撤掉，状态回到 None（截屏排除也会一起撤）。
            HideBackdrops();
            _fallbackReason = "";
            SetState(GlassBackdropKind.None, "", "",
                     !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);
            return;
        }

        GlassBackdropKind kind = GlassPolicy.Resolve(
            settings.Style.Acrylic, settings.Style.GlassMode,
            !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);

        if (kind != GlassBackdropKind.SystemAcrylic) _systemBackdrop.Retire(_window);
        if (kind != GlassBackdropKind.DesktopAcrylic) _desktopBackdrop.Retire(_window);

        string reason;
        bool applied;

        switch (kind)
        {
            case GlassBackdropKind.SystemAcrylic:
                applied = _systemBackdrop.Apply(_window, out reason);
                break;

            case GlassBackdropKind.DesktopAcrylic:
                applied = _desktopBackdrop.Apply(_window, out reason);
                break;

            default:
                // 兼容实现没有底板，窗口自己取样；用户显式选 Compat 时 Reason 为空。
                SetState(GlassBackdropKind.CompatCapture, _fallbackReason, "",
                         !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);
                return;
        }

        if (!applied)
        {
            // 底板用不了：撤掉、退回兼容取样，把失败原因留在状态里。
            // 这里不再像以前那样每次失败都 Warn —— SetState 只在实现 / 原因变化时记一条。
            HideBackdrops();
            _fallbackReason = string.IsNullOrEmpty(reason) ? "底板不可用" : reason;
            SetState(GlassBackdropKind.CompatCapture, _fallbackReason, _fallbackReason,
                     !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);
            return;
        }

        // 桌面取样成功说明「桌面不可用」已经过去，降级原因作废（恢复也要记一条，由 SetState 写）。
        // 系统亚克力成功则保留桌面降级的说明，「当前实际」里能看出为什么不是用户选的那种。
        if (kind == GlassBackdropKind.DesktopAcrylic) _fallbackReason = "";

        SetState(kind, _fallbackReason, "",
                 !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);
    }

    /// <summary>
    /// 统一写状态：只有「实现或原因」变化时才记一条日志（正常情况下 Info，降级 / 失败 Warn）。
    /// 这是取代原先「每次 Apply 失败都 Warn」的唯一日志出口，避免拖动时每帧刷一条。
    /// </summary>
    void SetState(GlassBackdropKind kind, string reason, string lastError,
                  bool systemAvailable, bool desktopAvailable)
    {
        string text = reason ?? "";
        string error = lastError ?? "";
        bool changed = _state.Kind != kind
                       || !string.Equals(_state.Reason, text, StringComparison.Ordinal);

        _state = new GlassRuntimeState(kind, text, error, systemAvailable, desktopAvailable);
        if (!changed) return;

        // None 带原因只出现在窗口关闭（"窗口已关闭"），不算失败；其余非空原因都是降级。
        bool degraded = kind != GlassBackdropKind.None && text.Length > 0;
        string line = $"毛玻璃运行时：{_state.Describe()}";
        if (degraded || error.Length > 0) Log.Warn(line);
        else Log.Info(line);
    }

    /// <summary>重新决定底板 + 起停兼容取图定时器。窗口位置 / 尺寸 / 可见性 / 设置变化后调用。</summary>
    public void UpdateGlassCapture()
    {
        if (!_window.GlassHandleReady) return;

        UpdateBackdrop();

        // 系统亚克力由底板窗口在卡片下面画模糊，卡片内部这层要关掉，也不用跑定时器；
        // 桌面取样的模糊图**正是画在卡片内部**（GlassFeed），这里不能清。
        if (BackdropActive)
        {
            if (_glassTimer is not null) _glassTimer.Stop();
            if (_systemBackdrop.IsActive) _window.Surface.ClearGlass();
            return;
        }

        Settings settings = _window.GlassSettings;
        bool enabled = settings.Style.Acrylic;

        if (!enabled)
        {
            if (_glassTimer is not null) _glassTimer.Stop();
            _window.Surface.ClearGlass();
            SetState(GlassBackdropKind.None, "", "",
                     !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);
            return;
        }

        // 没有底板接管 = 兼容取样（窗口自己截屏）。状态在这里补齐（幂等，不重复记日志）；
        // LastError 沿用刚由 UpdateBackdrop 写下的底板失败原因，不要在同一轮里被自己清掉。
        SetState(GlassBackdropKind.CompatCapture, _fallbackReason, _state.LastError,
                 !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);

        // Schema 7：兼容取样与桌面取样共用 BlurFps；取值与 GlassFeed.Configure 的钳制保持一致。
        int fps = Math.Max(2, Math.Min(30, settings.Style.BlurFps));
        TimeSpan interval = TimeSpan.FromMilliseconds(1000.0 / fps);

        if (_glassTimer is null)
        {
            _glassTimer = new DispatcherTimer(DispatcherPriority.Background, _window.Dispatcher)
            {
                Interval = interval
            };
            _glassTimer.Tick += (s, e) => CaptureGlass();
        }
        else
        {
            _glassTimer.Interval = interval;
        }

        if (!_glassTimer.IsEnabled)
        {
            _glassTimer.Start();
            Log.Info($"毛玻璃背景已启用（{fps} 帧/秒，模糊 {settings.Style.BlurRadius}px）");
        }

        CaptureGlass();
    }

    /// <summary>
    /// 兼容实现每画出一帧玻璃就记一条 GLAS 时间戳（MIRRORPOND_GLASS_TRACE=1）。
    /// 卡片排除在截屏之外以后没法靠截图验收，只能靠这种时间戳量刷新节奏。
    ///
    /// G-19：开关只在首次使用时读一次并缓存。以前每画一帧读一次环境变量（每帧一次查询），
    /// 而且进程启动后再改环境变量本来也不会生效（环境块早已复制）。
    /// </summary>
    static readonly bool TraceGlassFrames = ReadTraceSwitch();

    static bool ReadTraceSwitch()
    {
        string value = Environment.GetEnvironmentVariable("MIRRORPOND_GLASS_TRACE");
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G-19：按窗口可见性收口兼容取样定时器。窗口隐藏 / 最小化后 CaptureGlass 每轮都会
    /// 在「不可见」处早退，但定时器仍在按帧率空转；这里隐藏时停表、重新可见时恢复。
    /// 只碰定时器，不参与底板决策（那仍然只由 UpdateBackdrop / UpdateGlassCapture 负责）。
    /// </summary>
    public void SyncCaptureTimerToVisibility()
    {
        if (_window.GlassClosed) return;

        bool visible = _window.IsVisible && _window.WindowState != WindowState.Minimized;

        if (!visible)
        {
            if (_glassTimer is not null && _glassTimer.IsEnabled)
            {
                _glassTimer.Stop();
                Log.Debug("窗口隐藏 / 最小化，暂停兼容玻璃取样");
            }
            return;
        }

        // 重新可见：只有「本该跑兼容取样」时才恢复（底板接管 / 毛玻璃关闭时不启动）。
        if (_glassTimer is not null && !_glassTimer.IsEnabled
            && !BackdropActive && _window.GlassSettings.Style.Acrylic)
        {
            _glassTimer.Start();
            Log.Debug("窗口重新可见，恢复兼容玻璃取样");
        }
    }

    public void CaptureGlass()
    {
        if (_window.GlassClosed) return;                // 窗口已关闭：不再抓屏，也不写 UI
        if (BackdropActive) return;                     // 系统底板接管时不抓屏

        Settings settings = _window.GlassSettings;
        if (!settings.Style.Acrylic || !_window.GlassHandleReady) return;

        if (_glassBusy)
        {
            // PrintWindow 是同步跨进程调用，没有超时参数：目标窗口挂起时采集线程
            // 可能永远不返回。超过 2 秒作废这一代并放开 busy，让下一轮重新取，
            // 同时留下一条可归因的日志（否则表现为玻璃永久冻结、日志 0 条）。
            if ((DateTime.UtcNow - _captureStartedUtc).TotalSeconds < 2.0) return;
            _captureGen++;
            _glassBusy = false;
            Log.Warn("兼容玻璃取样超过 2 秒未返回（目标窗口可能挂起），丢弃本次并允许下一轮重试");
        }
        if (!_window.IsVisible || _window.WindowState == WindowState.Minimized) return;

        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (!Native.GetWindowRect(hwnd, out var rect)) return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 8 || height < 8) return;

        int left = rect.Left;
        int top = rect.Top;

        // Schema 7：兼容取样与桌面取样共用同一个模糊半径（像素）。
        double blur = settings.Style.BlurRadius;

        // G-6：FindLayers 会枚举全表窗口、逐候选开进程句柄，是内核级开销，不能留在 UI 线程。
        // 以前它就在这儿同步调用，N 张卡 = 每帧 N 次全表枚举。现在和 Grab 一起丢进采集任务；
        // 候选窗口列表由 GlassCapture 按短 TTL 共享，各卡只按自己的矩形过滤。
        // 层列表允许晚一帧（≤120ms），对模糊背景无感。
        long gen = ++_captureGen;
        _captureStartedUtc = DateTime.UtcNow;
        _glassBusy = true;

        Task.Run(() =>
        {
            BitmapSource image = null;
            bool noLayers = false;
            try
            {
                List<GlassCapture.Layer> layers = GlassCapture.FindLayers(left, top, width, height);
                if (layers.Count == 0)
                {
                    // 连桌面层都没拼出来（虚拟屏尺寸读不到）：这一轮什么都取不到。
                    noLayers = true;
                }
                else
                {
                    image = GlassCapture.Grab(layers, left, top, width, height, blur);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"毛玻璃取样失败: {ex.Message}");
            }

            try
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 旧代次（已被超时作废）回来时不能改新代状态，否则会把别人正在跑的采集标成空闲。
                    if (gen != _captureGen) return;
                    _glassBusy = false;
                    // 审阅 §21：Closed 只停了定时器，Start 之前投递的抓屏完成后仍会写 UI。
                    // 窗口关闭后到达的在途结果直接丢弃。
                    if (_window.GlassClosed) return;
                    if (noLayers)
                    {
                        // 采集层为空：清掉旧玻璃，别停在过期画面上（与以前在 UI 线程提前返回的行为一致）。
                        _window.Surface.ClearGlass();
                        return;
                    }
                    if (image is not null)
                    {
                        _window.Surface.SetGlass(image);
                        if (TraceGlassFrames)
                            Log.Info($"GLAS {DateTime.Now:HH:mm:ss.fff} hwnd={hwnd.ToInt64()}");
                    }
                }));
            }
            catch (Exception ex)
            {
                if (gen == _captureGen) _glassBusy = false;
                Log.Warn($"投递玻璃帧失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 取不到背景（连续几帧取样全黑：截屏被安全策略挡掉，或者屏幕本来就是黑的）。
    /// 标记这块底板不可用、收掉，再按剩下的选择重新决定；降级原因随状态一起写出去
    /// （日志由 SetState 统一记，不再在这里单独 Warn）。
    /// </summary>
    public void OnDesktopAcrylicUnavailable(string reason)
    {
        if (_desktopBackdrop.IsUnavailable) return;

        _desktopBackdrop.MarkUnavailable();
        string why = string.IsNullOrEmpty(reason) ? "取样失败" : reason;
        _fallbackReason = $"桌面取样不可用（{why}）";

        // 顺序：先撤掉截屏排除，再按「系统亚克力 / 兼容」那条路重新决定用哪块底板。
        _desktopBackdrop.Retire(_window);
        UpdateGlassCapture();
    }

    /// <summary>关掉毛玻璃 / 切回兼容实现：两块底板和截屏排除都要撤掉。</summary>
    void HideBackdrops()
    {
        _systemBackdrop.Retire(_window);
        _desktopBackdrop.Retire(_window);
    }

    /// <summary>
    /// 窗口关闭时收尾：停掉取图定时器（P2-4：否则它会每帧把已关闭窗口的可视树
    /// 钉在 Dispatcher 上），撤掉截屏排除，释放系统亚克力底板。
    /// 状态也在这里收口，避免设置窗还显示旧实现。
    /// </summary>
    public void Dispose()
    {
        if (_glassTimer is not null)
        {
            // 释放路径：Dispatcher 可能已随窗口关闭而停转，Stop 失败交给 SafeRelease 记录（X-9）。
            DispatcherTimer timer = _glassTimer;
            _glassTimer = null;
            SafeRelease.Run(timer.Stop, "兼容玻璃取样定时器");
        }

        HideBackdrops();
        _systemBackdrop.Dispose();

        _fallbackReason = "";
        SetState(GlassBackdropKind.None, "窗口已关闭", "", false, false);
    }
}
