using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Ui;

public partial class OverlayWindow : Window
{
    Settings _settings;
    bool _handleReady;

    /// <summary>
    /// G-13：置顶后「直接压在本窗口上面的那个窗口」。共享置顶调度每 2.5 秒比对它，
    /// 没变就完全不碰 SetWindowPos；只有它变了（新窗口插到上方）或 topmost 标志丢了才重摆。
    /// </summary>
    IntPtr _lastZPrev;
    bool _closed;
    bool _commandMode;
    IntPtr _previousForeground;

    /// <summary>
    /// G-18：SetWindowPos 返回 FALSE 时既不抛异常也没人检查，表现为「窗口没动 / 没置顶但日志干净」。
    /// 拖动 / 补位会高频调用它，失败按状态转移 + 60 秒补记节流，不刷屏（成功时打点用于记录恢复）。
    /// </summary>
    static readonly LogThrottle _windowPosFailure = new("卡片窗口定位", 60000);

    /// <summary>G-18：拖放判定回调由卡片实现，拖动中每帧都会调用，失败同样走节流。</summary>
    static readonly LogThrottle _dropCheckFailure = new("卡片拖放判定", 60000);

    /// <summary>
    /// 毛玻璃运行时（2026-11 审阅第三批第 12 项）：底板选择 / 截屏排除 / 兼容取图定时器
    /// 全部在 <see cref="GlassRuntime"/> 里，窗口只负责通知变化与提供自身句柄。
    /// </summary>
    readonly GlassRuntime _glassRuntime;

    /// <summary>底板需要的窗口参数（外观 + 可见性），抽出来是为了让底板类不直接读 _settings。</summary>
    internal string GlassTintColor => _settings.Style.Background;
    internal bool GlassShadow => _settings.Overlay.Shadow;
    internal bool GlassVisible => IsVisible;

    // 玻璃运行时读的窗口状态：设置对象会被 Apply / RefreshSettings 换成新的，必须每次现读。
    internal Settings GlassSettings => _settings;
    internal bool GlassHandleReady => _handleReady;
    internal bool GlassClosed => _closed;

    /// <summary>
    /// 当前实际生效的玻璃实现（运行时单一状态源）。设置窗的「当前实际」与拖动补帧都读这里，
    /// 不再各自按 <c>Style.GlassMode</c> 重算 —— 用户选 DesktopFeed 但已经降级的场景只有运行时知道。
    /// </summary>
    internal GlassRuntimeState GlassState => _glassRuntime.State;

    bool _interactive;

    /// <summary>卡片内容需要鼠标 / 键盘（见 IRichCardModule.NeedsMouseInput）：这张卡不穿透。</summary>
    public bool Interactive
    {
        get => _interactive;
        set
        {
            _interactive = value;
            // 内容层默认不参与命中测试（图片 / 视频不该挡住桌面）；交互卡片要打开它，
            // 否则按钮永远收不到鼠标事件。
            Surface.ContentHitTestVisible = value;
        }
    }

    bool _dragging;
    bool _dragEnabled = true;
    Func<IDataObject, bool> _canDrop;
    Action<IDataObject> _dropped;
    Action<bool, IDataObject> _dropState;
    Point _dragStartScreen;
    int _dragStartPhysX;
    int _dragStartPhysY;
    int _dragTargetPhysX;
    int _dragTargetPhysY;
    /// <summary>拖动中已经排队了一帧背景重取样（每个合成帧最多一次）。</summary>
    bool _dragFrameQueued;

    // 位置动画：补位 / 自动对齐翻边 / 松手回位。每个窗口独立计时，新的目标会从
    // 当前位置重新开始，避免上一段动画把窗口拽回旧目标。
    DispatcherTimer _moveTimer;
    DispatcherTimer _dragPollTimer;
    DateTime _moveStartUtc;
    double _moveFromLeft;
    double _moveFromTop;
    double _moveToLeft;
    double _moveToTop;
    int _moveDurationMs;

    // 内容级淡入淡出：只动 OverlaySurface；8px 位移由窗口位置动画完成，
    // 否则窗口大小等于卡片大小，平移内容会被窗口边界裁掉。
    int _contentMotionVersion;

    MenuItem _menuSettings;
    MenuItem _menuToggle;
    MenuItem _menuLock;

    public event Action SettingsRequested;
    public event Action HideRequested;
    public event Action ExitRequested;
    public event Action LockToggleRequested;
    /// <summary>按住卡片开始拖动前触发：调用方据此取消正在跑的补位动画。</summary>
    public event Action DragStarted;
    /// <summary>拖动中：参数是相对拖动起点的物理像素总位移（dx, dy）。其他卡片按增量跟随。</summary>
    public event Action<int, int> Dragging;
    /// <summary>拖动结束：参数是窗口落点的物理左上角与所在显示器序号。</summary>
    public event Action<int, int, int> Moved;
    /// <summary>窗口重新置顶后触发，富内容（视频子窗口）需要据此重新摆正 z 序。</summary>
    public event Action RaisedTopmost;

    public OverlayWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _glassRuntime = new GlassRuntime(this);

        SourceInitialized += OnSourceInitialized;
        Deactivated += (s, e) => { if (_settings.Overlay.Topmost) EnsureTopmost(); };

        // 亚克力底板要跟着卡片走：位置 / 尺寸 / 可见性 / DPI 任何一个变了都重摆。
        LocationChanged += (s, e) => _glassRuntime.UpdateBackdrop();
        SizeChanged += (s, e) => _glassRuntime.UpdateBackdrop();
        IsVisibleChanged += (s, e) => _glassRuntime.UpdateBackdrop();
        DpiChanged += (s, e) =>
        {
            // 缩放变了：重新取每屏 DPI。拖动跨屏时**不能** ApplyPosition：
            // 那会把窗口拉回拖动前的旧锚点，表现为“到屏幕交界就跳一下 / 尺寸错”。
            // 拖动结束后由 Moved → Layout 按新屏 DPI 重新落位。
            ScreenGeometry.Invalidate();
            if (!_dragging)
            {
                ApplyPosition();
            }
            else
            {
                // 跨屏时 WPF 会按新的 DPI 自己挪一次窗口（用旧的 DIP 坐标算），
                // 这里在它之后把窗口压回拖动目标，否则锚点会从窗口矩形反算成负数。
                int targetX = _dragTargetPhysX;
                int targetY = _dragTargetPhysY;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_dragging) SetPhysicalPosition(targetX, targetY);
                }), DispatcherPriority.Render);
            }
            _glassRuntime.UpdateBackdrop();
        };
        Closed += (s, e) =>
        {
            _closed = true;
            StopDragPoll();
            StopPositionMotion();
            CancelContentMotion();
            _glassRuntime.Dispose();
        };

        BuildContextMenu();
    }

    void OnSourceInitialized(object sender, EventArgs e)
    {
        _handleReady = true;
        ApplyPosition();
        _glassRuntime.UpdateGlassCapture();
        UpdateClickThrough();
        EnsureTopmost();
    }

    public void Apply(Settings settings, string text, CardSettings config = null)
    {
        _settings = settings;
        // 设置变了就再给两块底板一次机会（底板对象保留，不重建）
        _glassRuntime.ResetAvailability();
        Surface.ClearContent();
        Surface.SetCardMeta(config);
        Surface.Apply(settings, text, false);
        if (_handleReady) _glassRuntime.UpdateGlassCapture();
        Topmost = settings.Overlay.Topmost;
        UpdateClickThrough();
        UpdateMenuHeaders();
    }

    /// <summary>富内容卡片（图片 / 动图 / 视频）：文字层空着，直接把控件放进卡片。</summary>
    public void ApplyRich(Settings settings, FrameworkElement content)
    {
        _settings = settings;
        _glassRuntime.ResetAvailability();
        Surface.SetCardMeta(null);
        Surface.Apply(settings, "");
        Surface.SetContent(content);
        if (_handleReady) _glassRuntime.UpdateGlassCapture();
        Topmost = settings.Overlay.Topmost;
        UpdateClickThrough();
        UpdateMenuHeaders();
    }

    /// <summary>换成纯文字卡片时释放富内容（停掉视频 / 动图）。</summary>
    public void ClearRichContent() => Surface.ClearContent();

    /// <summary>只同步设置对象与鼠标穿透状态，不重画内容（F8 / 托盘切换锁定时用）。</summary>
    public void RefreshSettings(Settings settings)
    {
        _settings = settings;
        UpdateClickThrough();
        UpdateMenuHeaders();
    }

    /// <summary>
    /// 只重画卡片底层（底色 / 背景不透明度 / 圆角 / 投影 / 描边），富内容原样保留。
    /// 「只改外观」时宿主必须调它：<see cref="RefreshSettings"/> 只换设置对象，不动 Surface，
    /// 否则媒体 / CLI 这类就地换肤的卡片会保留旧的底色与圆角。
    /// </summary>
    public void RefreshSurface(Settings settings)
    {
        _settings = settings;
        Surface.Apply(settings, Surface.Text);
        if (_handleReady) _glassRuntime.UpdateGlassCapture();
    }

    public void SetText(string text, bool cursor) => Surface.SetText(text, cursor);

    public void SetText(string text, bool cursor, int cursorIndex) => Surface.SetText(text, cursor, cursorIndex);

    /// <summary>切换命令行时重新按当前内容量尺寸（该长就长、该缩就缩一次）。</summary>
    public void ResetStableSize() => Surface.ResetStableSize();

    public void SetLayoutText(string text) => Surface.SetLayoutText(text);

    // ==================== 快捷命令行 ====================

    /// <summary>
    /// 命令行面板是卡片里的富内容（和 AI 对话同一套），所以在窗口层只需要：
    /// 允许自己被激活 → 抢到前台 → 把键盘焦点交给面板的输入行。
    /// </summary>
    public void BeginCommandInput()
    {
        if (!_handleReady || _commandMode) return;

        _commandMode = true;
        _previousForeground = Native.GetForegroundWindow();

        UpdateClickThrough();
        Focusable = true;

        Activate();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        bool foreground = Native.ForceForeground(hwnd);

        // 等这一轮布局 / 激活走完再要焦点，否则 Focus 会被随后的激活流程抢走。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ICliFocusTarget target = Surface.ContentElement as ICliFocusTarget;
            if (target is not null) target.FocusInput();

            Log.Info($"命令行模式：前台={foreground}，键盘焦点在面板输入行={(target is not null && Keyboard.FocusedElement is UIElement)}");
        }), DispatcherPriority.Input);
    }

    public void EndCommandInput()
    {
        if (!_commandMode) return;

        _commandMode = false;

        Keyboard.ClearFocus();
        Focusable = false;
        UpdateClickThrough();
        EnsureTopmost();

        // 还原前台失败只影响焦点落点，不改变本窗口状态；下面马上清空记录。
        // G-18：SetForegroundWindow 受前台锁限制，失败返回 FALSE 而不是抛异常，必须看返回值。
        if (_previousForeground != IntPtr.Zero
            && _previousForeground != new WindowInteropHelper(this).Handle
            && !Native.SetForegroundWindow(_previousForeground))
        {
            Log.Debug($"命令行结束后还原前台失败（前台锁），焦点留在本窗口 err={Marshal.GetLastWin32Error()}");
        }
        _previousForeground = IntPtr.Zero;

        Log.Info("命令行模式结束");
    }

    public bool IsCommandInputActive => _commandMode;

    /// <summary>
    /// 布局协调器把整叠的左 / 右对齐告诉窗口，文字选“跟随卡片对齐”时按它排版。
    /// 不传（null）时由表面按卡片位置兜底，截图 / 设置预览走那条路径。
    /// </summary>
    public void SetResolvedCardAlignment(string value) => Surface.SetResolvedCardAlignment(value);

    public void ApplyPosition() => ApplyPosition(false);

    /// <summary>
    /// 按当前 Dialog / Settings 摆窗口。animate=true 时走 180ms ease-out 的补位动画，
    /// 用于松手后的自动对齐翻边、显隐后的补位；启动 / 截图 / 拖动中一律 animate=false。
    /// </summary>
    public void ApplyPosition(bool animate)
    {
        if (!_handleReady) return;

        // 显示 / 隐藏 / 补位已经在这张窗口上跑位置动画时，布局调度（窗口刚 Show 出来的
        // SizeChanged 就会触发一次）不能把窗口直接吸到目标位——那会把 160ms 呼出滑入
        // 截成 1 帧。这里改走动画路径，从当前位置接到新目标，视觉上保持连续。
        if (!animate && IsPositionMotionActive && IsVisible && !_closed)
            animate = true;

        int left = 0;
        int top = 0;

        try
        {
            // G-14：设备名优先，避免热插拔 / 枚举顺序变化后把卡片放到另一台屏。
            ScreenInfo screen = ScreenGeometry.Get(_settings.Overlay.Monitor, _settings.Overlay.MonitorDevice);
            if (screen is null) return;

            // 锚点是「目标工作区左上角起的 DIP」；物理位置必须按目标屏自己的 DPI 换算。
            // 不能再用主屏缩放乘整个虚拟屏坐标：4K@300% 上会把窗口放到屏外。
            left = screen.WorkLeft + (int)Math.Round(_settings.Overlay.X * screen.ScaleX);
            top = screen.WorkTop + (int)Math.Round(_settings.Overlay.Y * screen.ScaleY);
        }
        catch (Exception ex)
        {
            Log.Warn($"定位失败: {ex.Message}");
            return;
        }

        if (animate && CardMotion.Enabled && IsVisible && !_closed)
        {
            AnimateTo(left, top, CardMotion.LayoutDurationMs);
        }
        else
        {
            StopPositionMotion();
            SetPhysicalPosition(left, top);
        }

        _glassRuntime.UpdateBackdrop();

        if (_settings.Style.Acrylic) _glassRuntime.CaptureGlass();
    }

    /// <summary>
    /// 直接按物理像素移动窗口。PerMonitorV2 下这是唯一可靠的跨屏定位方式
    /// （WPF 的 Left/Top 会按目标屏 DPI 再乘一次，混合 DPI 时不可用）。
    /// </summary>
    void SetPhysicalPosition(int x, int y)
    {
        if (!_handleReady || _closed) return;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                                Native.SwpNoSize | Native.SwpNoZorder | Native.SwpNoActivate))
        {
            _windowPosFailure.Success();
            return;
        }

        // G-18：返回值 FALSE 不抛异常，位置真的没生效时必须留痕，否则只表现为「卡片不动」。
        _windowPosFailure.Failure($"SetWindowPos(x={x}, y={y}) err={Marshal.GetLastWin32Error()}");
    }

    /// <summary>当前窗口左上角的物理像素坐标；句柄未就绪时返回 false。</summary>
    public bool TryGetPhysicalPosition(out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!_handleReady) return false;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;

        if (!Native.GetWindowRect(hwnd, out var rect)) return false;

        x = rect.Left;
        y = rect.Top;
        return true;
    }

    /// <summary>按物理像素增量平移（拖动时其余卡片跟随用）。</summary>
    public void OffsetPhysical(int dx, int dy)
    {
        if (!TryGetPhysicalPosition(out var x, out var y)) return;
        SetPhysicalPosition(x + dx, y + dy);
    }

    /// <summary>
    /// 窗口位置动画：从当前物理位置在 durationMs 内 ease-out 到目标。新的目标会取消旧动画，
    /// 从当前实际位置重新开始；拖动开始前由控制器统一 StopPositionMotion。
    /// </summary>
    public void AnimateTo(int left, int top, int durationMs)
    {
        StopPositionMotion();

        if (!TryGetPhysicalPosition(out var currentX, out var currentY))
        {
            SetPhysicalPosition(left, top);
            return;
        }

        if (!CardMotion.Enabled || !IsVisible || _closed || durationMs <= 0)
        {
            SetPhysicalPosition(left, top);
            return;
        }

        if (Math.Abs(currentX - left) < 1 && Math.Abs(currentY - top) < 1)
        {
            SetPhysicalPosition(left, top);
            return;
        }

        _moveFromLeft = currentX;
        _moveFromTop = currentY;
        _moveToLeft = left;
        _moveToTop = top;
        _moveDurationMs = durationMs;
        _moveStartUtc = DateTime.UtcNow;

        _moveTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(CardMotion.FrameIntervalMs)
        };
        _moveTimer.Tick += (s, e) => TickPositionMotion();
        _moveTimer.Start();
    }

    void TickPositionMotion()
    {
        if (_closed)
        {
            StopPositionMotion();
            return;
        }

        double elapsed = (DateTime.UtcNow - _moveStartUtc).TotalMilliseconds;
        double t = _moveDurationMs <= 0 ? 1 : elapsed / _moveDurationMs;
        double eased = CardMotion.Ease(t);

        int x = (int)Math.Round(_moveFromLeft + (_moveToLeft - _moveFromLeft) * eased);
        int y = (int)Math.Round(_moveFromTop + (_moveToTop - _moveFromTop) * eased);
        SetPhysicalPosition(x, y);

        if (t >= 1)
        {
            SetPhysicalPosition((int)Math.Round(_moveToLeft), (int)Math.Round(_moveToTop));
            StopPositionMotion();
        }
    }

    public void StopPositionMotion()
    {
        if (_moveTimer is null) return;

        // 释放路径：计时器可能已随 Dispatcher 关闭。先摘引用再 Stop，失败交给 SafeRelease 记录。
        DispatcherTimer timer = _moveTimer;
        _moveTimer = null;
        SafeRelease.Run(timer.Stop, "位置动画计时器");
    }

    /// <summary>当前是否有位置动画在跑；布局协调器据此避免瞬移打断呼出 / 补位。</summary>
    public bool IsPositionMotionActive => _moveTimer is not null;

    /// <summary>呼出前把内容压透明，窗口本身保持可见以完成布局测量。</summary>
    public void PrepareShowMotion()
    {
        StopPositionMotion();
        CancelContentMotion();
        Surface.Opacity = 0;
        Surface.IsHitTestVisible = true;
    }

    /// <summary>无动画路径（启动 / 截图 / 设置重建）：直接恢复完整内容。</summary>
    public void ResetContentMotion()
    {
        StopPositionMotion();
        CancelContentMotion();
        Surface.Opacity = 1;
        Surface.IsHitTestVisible = true;
    }

    /// <summary>
    /// 实际滑入 / 滑出方向：Follow 取布局协调器已经解析出的整叠左 / 右对齐。
    /// 没有协调器的离屏场景（截图 / 预览）退回左对齐，避免方向为空。
    /// </summary>
    string ResolveSlideDirection()
    {
        string slide = CardMotion.SlideDirection;
        if (!string.Equals(slide, "Follow", StringComparison.OrdinalIgnoreCase)) return slide;

        string resolved = Surface.ResolvedCardAlignment;
        return string.Equals(resolved, "Right", StringComparison.OrdinalIgnoreCase) ? "Right" : "Left";
    }

    /// <summary>
    /// 呼出：窗口从目标位下方 8px 滑回目标位，同时内容淡入。
    /// 8px 由窗口位置动画完成——如果只平移 Surface，会被「窗口尺寸 = 卡片尺寸」裁掉。
    /// </summary>
    public void AnimateShowMotion()
    {
        if (!CardMotion.Enabled || _closed)
        {
            ResetContentMotion();
            return;
        }

        StopPositionMotion();
        CancelContentMotion();

        int version = ++_contentMotionVersion;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        TimeSpan duration = TimeSpan.FromMilliseconds(CardMotion.ShowDurationMs);

        if (!TryGetPhysicalPosition(out var targetLeft, out var targetTop))
        {
            ResetContentMotion();
            return;
        }

        ScreenInfo screen = ScreenGeometry.ForPoint(targetLeft, targetTop);
        double scaleX = screen is not null ? screen.ScaleX : 1.0;
        double scaleY = screen is not null ? screen.ScaleY : 1.0;
        int offset = (int)Math.Round(CardMotion.ShowOffset * scaleY);
        string slide = ResolveSlideDirection();
        int dx = string.Equals(slide, "Left", StringComparison.OrdinalIgnoreCase) ? -offset
            : string.Equals(slide, "Right", StringComparison.OrdinalIgnoreCase) ? offset
            : 0;
        int dy = slide == "None" ? offset : 0;

        // 从目标位左侧 / 右侧 / 下方滑回目标位；左右滑动的距离按 X 轴缩放换算。
        if (dx != 0) dx = (int)Math.Round(CardMotion.ShowOffset * scaleX) * Math.Sign(dx);
        SetPhysicalPosition(targetLeft + dx, targetTop + dy);
        AnimateTo(targetLeft, targetTop, CardMotion.ShowDurationMs);

        var opacity = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        opacity.Completed += (s, e) =>
        {
            if (version != _contentMotionVersion) return;
            Surface.BeginAnimation(UIElement.OpacityProperty, null);
            Surface.Opacity = 1;
        };

        Surface.BeginAnimation(UIElement.OpacityProperty, opacity);
    }

    /// <summary>
    /// 消失：窗口向下方滑 8px + 内容淡出；完成后由调用方决定是否真正 Hide
    /// （可能已经被重新呼出，版本号会作废这次完成回调）。
    /// </summary>
    public void AnimateHideMotion(Action completed)
    {
        if (!CardMotion.Enabled || _closed)
        {
            ResetContentMotion();
            if (completed is not null) completed();
            return;
        }

        StopPositionMotion();

        // 淡出可能发生在呼出淡入的中途：先把当前透明度记下来，再取消旧动画。
        // 否则 CancelContentMotion 会把 Opacity 恢复成 1，视觉上会先闪一下再淡出。
        double fromOpacity = Surface.Opacity;
        CancelContentMotion();
        Surface.IsHitTestVisible = false;

        int version = ++_contentMotionVersion;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        TimeSpan duration = TimeSpan.FromMilliseconds(CardMotion.HideDurationMs);

        if (!TryGetPhysicalPosition(out var px, out var py))
        {
            ResetContentMotion();
            if (completed is not null) completed();
            return;
        }

        ScreenInfo screen = ScreenGeometry.ForPoint(px, py);
        double scaleX = screen is not null ? screen.ScaleX : 1.0;
        double scaleY = screen is not null ? screen.ScaleY : 1.0;
        int offset = (int)Math.Round(CardMotion.HideOffset * scaleY);
        string slide = ResolveSlideDirection();
        int dx = string.Equals(slide, "Left", StringComparison.OrdinalIgnoreCase) ? -offset
            : string.Equals(slide, "Right", StringComparison.OrdinalIgnoreCase) ? offset
            : 0;
        // 纵向退场必须向下（正 Y）；之前写成 -offset 会向上缩，和呼出方向相同。
        int dy = slide == "None" ? offset : 0;

        if (dx != 0) dx = (int)Math.Round(CardMotion.HideOffset * scaleX) * Math.Sign(dx);
        AnimateTo(px + dx, py + dy, CardMotion.HideDurationMs);

        var opacity = new DoubleAnimation(fromOpacity, 0, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        opacity.Completed += (s, e) =>
        {
            if (version != _contentMotionVersion) return;

            StopPositionMotion();
            Surface.BeginAnimation(UIElement.OpacityProperty, null);
            Surface.Opacity = 0;

            if (completed is not null) completed();
        };

        Surface.BeginAnimation(UIElement.OpacityProperty, opacity);
    }

    /// <summary>取消内容动画并恢复基值；只动 Surface，不碰窗口 / 玻璃状态。</summary>
    void CancelContentMotion()
    {
        _contentMotionVersion++;

        if (Surface is null) return;

        Surface.BeginAnimation(UIElement.OpacityProperty, null);
        Surface.Opacity = 1;
        Surface.IsHitTestVisible = true;
    }

    public void UpdateClickThrough()
    {
        if (!_handleReady) return;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        nint ex = Native.GetExStyle(hwnd);
        // 交互卡片（按钮 / 输入框）即使在锁定状态下也不开 WS_EX_TRANSPARENT，
        // 否则鼠标事件永远到不了卡片里的控件；代价是它会挡住身下的桌面点击。
        bool clickThrough = _settings.Overlay.Locked && !_commandMode && !_interactive;
        if (clickThrough)
            ex |= Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow;
        else
            ex &= ~(Native.WsExTransparent | Native.WsExNoActivate);

        // G-18：写扩展样式失败会让「锁定 / 穿透」与设置不符，而且不会抛异常。
        if (!Native.SetExStyle(hwnd, ex))
            Log.Warn($"设置点击穿透样式失败 err={Marshal.GetLastWin32Error()}（锁定={_settings.Overlay.Locked}）");

        Cursor = _commandMode ? Cursors.IBeam
               : (clickThrough || _interactive) ? Cursors.Arrow : Cursors.SizeAll;
    }

    public void EnsureTopmost()
    {
        if (!_handleReady) return;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (!Native.SetWindowPos(hwnd, Native.HwndTopmost, 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZorder))
            _windowPosFailure.Failure($"置顶 SetWindowPos 失败 err={Marshal.GetLastWin32Error()}");
        else
            _windowPosFailure.Success();

        // G-13：记下置顶后「直接压在我们上面的窗口」，共享调度据此判断 Z 序有没有真的变化。
        _lastZPrev = Native.GetWindow(hwnd, Native.GwHwndPrev);

        // 卡片重新置顶后，底板要重新插回它正下方（别的置顶窗口可能插进来）。
        _glassRuntime.UpdateBackdrop();

        // 置顶之后自己的子窗口（视频）会被压下去，通知内容层重新摆好。
        Action handler = RaisedTopmost;
        if (handler is not null) handler();
    }

    /// <summary>
    /// G-13：共享置顶调度每 2.5 秒问一次「真的需要重新置顶吗」。
    /// 绝大多数时候 Z 序没变；只有扩展样式丢了 topmost、或上方插进了新窗口时才调 SetWindowPos。
    /// 以前每张卡每 2.5 秒无条件置顶一次，还会连带重摆底板、通知富内容子窗口，全是冗余。
    /// </summary>
    internal void RefreshTopmostIfNeeded()
    {
        if (!_handleReady || !IsVisible) return;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        nint ex = Native.GetExStyle(hwnd);
        if ((ex & Native.WsExTopmost) == 0)
        {
            // 被别的程序 / 系统清掉了 topmost 标志：这是真的没置顶，必须补一次。
            EnsureTopmost();
            return;
        }

        // GW_HWNDPREV 是直接压在卡片上面的窗口。它没变 → Z 序没变，什么都不用做。
        if (Native.GetWindow(hwnd, Native.GwHwndPrev) != _lastZPrev) EnsureTopmost();
    }

    /// <summary>
    /// G-13：非置顶时原来每 2.5 秒无条件重跑一次玻璃决策（为了在瞬态黑屏失败后自动重试）。
    /// 现在只在确实没有底板（降级到兼容取样 / 尚未启用）时才重试，正常状态不再反复 Apply 材质。
    /// </summary>
    internal void RetryBackdropIfDegraded()
    {
        if (!_settings.Style.Acrylic) return;

        GlassBackdropKind kind = _glassRuntime.State.Kind;
        if (kind == GlassBackdropKind.DesktopAcrylic || kind == GlassBackdropKind.SystemAcrylic) return;

        _glassRuntime.UpdateBackdrop();
    }

    // ==================== 毛玻璃 ====================
    // 三种实现（系统亚克力 / 桌面取样 / 兼容取样）的选择与生命周期已经搬进
    // GlassRuntime；窗口只保留底板回调与「什么时候通知」，不保留同名字段，避免双真相。
    // 实际生效的是哪一种只由 GlassRuntime.State 说了算（见上面的 GlassState）。

    /// <summary>
    /// 桌面取样连续全黑时由 GlassFeed 回调：转给玻璃运行时标记不可用并重新决策。
    /// 这个委托在 GlassBackdrops 里 attach 时按窗口类型取得，所以入口必须留在窗口上。
    /// </summary>
    internal void OnDesktopAcrylicUnavailable(string reason) => _glassRuntime.OnDesktopAcrylicUnavailable(reason);

    /// <summary>
    /// 是否允许按住卡片移动整叠。卡片内容自己处理拖动时（画板、卡片内拖拽排序）宿主把它关掉，
    /// 鼠标按下就完全交给内容，不会再「一按就拖卡」。
    /// </summary>
    public bool DragEnabled
    {
        get => _dragEnabled; set => _dragEnabled = value;
    }

    /// <summary>
    /// 让这张卡片接住拖放（模块实现 <see cref="MirrorpondHUD.Cards.ICardDropTarget"/> 时由宿主注册）。
    /// 传 null 关闭。锁定（点击穿透）状态下窗口收不到拖放，这是既有语义。
    /// </summary>
    public void EnableDrop(Func<IDataObject, bool> canDrop, Action<IDataObject> dropped,
                           Action<bool, IDataObject> dropState = null)
    {
        _canDrop = canDrop;
        _dropped = dropped;
        _dropState = dropState;
        AllowDrop = canDrop is not null;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        NotifyDropState(true, e.Data);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        NotifyDropState(false, null);
    }

    void NotifyDropState(bool over, IDataObject data)
    {
        Action<bool, IDataObject> handler = _dropState;
        if (handler is null) return;

        try { handler(over, data); }
        catch (Exception ex) { Log.Warn($"卡片拖放状态回调失败: {ex.Message}"); }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (_canDrop is null) return;

        bool ok;
        try
        {
            ok = _canDrop(e.Data);
            _dropCheckFailure.Success();
        }
        catch (Exception ex)
        {
            // 卡片自己决定能否接住拖放：抛错时本次按「不接受」处理（用户可见的行为），
            // 不能让一次判定异常打断拖动；拖动中每帧都会调用，因此走节流日志。
            _dropCheckFailure.Failure(ex.Message);
            ok = false;
        }

        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (_dropped is null) return;

        e.Handled = true;
        try { _dropped(e.Data); }
        catch (Exception ex) { Log.Warn($"卡片拖放处理失败: {ex.Message}"); }
        NotifyDropState(false, null);
    }

    // ==================== 拖动 ====================
    // 不用 DragMove()：它在鼠标抬起过快、窗口未激活等情况下会直接抛异常，表现为"拖动失灵"。
    // 这里自己抓鼠标，用物理屏幕坐标算位移，再 SetWindowPos 物理移动 —— 混合 DPI 下
    // WPF 的 Left/Top 会按目标屏 DPI 再乘一次，不能用。

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_settings.Overlay.Locked || _commandMode) return;
        if (!_dragEnabled) return;
        if (e.ClickCount > 1) return;

        _dragging = true;
        if (!Native.GetCursorPos(out var startCursor))
        {
            _dragging = false;
            return;
        }
        _dragStartScreen = new Point(startCursor.X, startCursor.Y);

        // 先让控制器收掉所有正在跑的补位动画，并把整叠落到上一轮目标位、挂起布局；
        // 然后才读物理位置作为拖动起点。顺序不能反：否则起点可能是动画中途的坐标，
        // 松手时锚点会差几像素，表现为“偶尔错位”。
        Action started = DragStarted;
        if (started is not null) started();

        if (!TryGetPhysicalPosition(out var physX, out var physY))
        {
            _dragging = false;
            return;
        }
        _dragStartPhysX = physX;
        _dragStartPhysY = physY;
        _dragTargetPhysX = physX;
        _dragTargetPhysY = physY;

        // 跨 DPI 时 WPF 可能停止派发 MouseMove 或丢捕获；用一个 15ms 轮询直接读物理光标，
        // 保证拖动在屏幕交界处仍然连续。
        StartDragPoll();
        CaptureMouse();
        e.Handled = true;
    }

    void StartDragPoll()
    {
        if (_dragPollTimer is null)
        {
            _dragPollTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };
            _dragPollTimer.Tick += (s, e) => PollDrag();
        }
        if (!_dragPollTimer.IsEnabled) _dragPollTimer.Start();
    }

    void StopDragPoll()
    {
        if (_dragPollTimer is not null && _dragPollTimer.IsEnabled) _dragPollTimer.Stop();
    }

    void PollDrag()
    {
        if (!_dragging)
        {
            StopDragPoll();
            return;
        }

        if ((Native.GetKeyState(0x01) & 0x8000) == 0
            && (Native.GetAsyncKeyState(0x01) & 0x8000) == 0)
        {
            FinishDrag();
            return;
        }

        UpdateDragFromCursor();
    }

    void UpdateDragFromCursor()
    {
        if (!Native.GetCursorPos(out var cursor)) return;

        int dx = cursor.X - (int)Math.Round(_dragStartScreen.X);
        int dy = cursor.Y - (int)Math.Round(_dragStartScreen.Y);
        _dragTargetPhysX = _dragStartPhysX + dx;
        _dragTargetPhysY = _dragStartPhysY + dy;
        SetPhysicalPosition(_dragTargetPhysX, _dragTargetPhysY);

        Action<int, int> handler = Dragging;
        if (handler is not null) handler(dx, dy);

        QueueDragFrame();
    }

    static bool IsLeftButtonDown() =>
        // GetAsyncKeyState 在窗口跨屏 / 失焦的瞬间可能返回 0；GetKeyState 读本线程消息队列，
        // 对“这次拖动还按着”更可靠。
        (Native.GetKeyState(0x01) & 0x8000) != 0
            || (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        if (!IsLeftButtonDown())
        {
            FinishDrag();
            return;
        }

        UpdateDragFromCursor();
    }

    /// <summary>
    /// 拖动中补一帧背景：位置变了但取样还停在设置里的刷新帧率（默认 10 帧 = 100 ms 一格），
    /// 画面会像粘在卡片上。这里按合成帧合并成一次，渲染优先级执行（输入事件处理完之后），
    /// 拖动期间背景跟着桌面走，松手后恢复按设置刷新。
    /// </summary>
    void QueueDragFrame()
    {
        if (_dragFrameQueued) return;

        // 实际用哪种实现只看运行时状态：设置里选的实现可能已经降级（远程桌面 / 接口失效），
        // 以前这里自己 NormalizeGlassMode 再算一遍，会和真实实现脱节。
        GlassBackdropKind kind = _glassRuntime.State.Kind;
        bool desktop = kind == GlassBackdropKind.DesktopAcrylic;
        bool compat = kind == GlassBackdropKind.CompatCapture;

        if (!desktop && !compat) return;

        _dragFrameQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _dragFrameQueued = false;
            if (!_dragging) return;
            if (desktop) GlassFeed.RequestFrame(false);
            else if (compat) _glassRuntime.CaptureGlass();
        }), DispatcherPriority.Render);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // WPF 在跨 DPI 时可能提前报 Up；以真实按键状态为准，避免拖动被截断。
        if (_dragging && !IsLeftButtonDown()) FinishDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_dragging) return;

        // 跨 DPI 时窗口会被 WPF 重新摆一次，某些情况下会丢掉鼠标捕获；
        // 左键还按着就重新抓回来继续拖，不能把“跨屏”误判成“松手”。
        if (IsLeftButtonDown() && IsVisible)
        {
            // 跨屏丢捕获后这次重抓失败会中止拖动（状态机还停在 _dragging）；
            // 不弹提示但必须留日志，否则用户只看到“拖到一半不动了”而无从归因。
            try { CaptureMouse(); }
            catch (Exception ex) { Log.Debug($"跨屏拖动重新捕获鼠标失败（拖动将结束）: {ex.Message}"); }
            return;
        }

        FinishDrag();
    }

    void SyncDragTargetToCursor()
    {
        if (!Native.GetCursorPos(out var cursor)) return;

        int dx = cursor.X - (int)Math.Round(_dragStartScreen.X);
        int dy = cursor.Y - (int)Math.Round(_dragStartScreen.Y);
        _dragTargetPhysX = _dragStartPhysX + dx;
        _dragTargetPhysY = _dragStartPhysY + dy;
    }

    void FinishDrag()
    {
        _dragging = false;
        StopDragPoll();
        if (IsMouseCaptured) ReleaseMouseCapture();

        // 预览可能因跨 DPI 丢了最后一段 MouseMove；以松手瞬间的真实光标为准，
        // 否则会把中途的旧 target 当成落点，表现为“到交界处大小/位置错了”。
        SyncDragTargetToCursor();

        int physX = _dragTargetPhysX;
        int physY = _dragTargetPhysY;
        int monitor = DetectMonitor(physX, physY);

        _glassRuntime.CaptureGlass();
        GlassFeed.RequestFrame(true);

        Action<int, int, int> handler = Moved;
        if (handler is not null) handler(physX, physY, monitor);
    }

    /// <summary>卡片本身的可见宽度（不含投影预留的 10 DIP 透明边），右对齐时用它算 X。</summary>
    public double CardWidth
    {
        get
        {
            double width = ActualWidth;
            if (_settings.Overlay.Shadow) width -= 20;
            return Math.Max(0, width);
        }
    }

    /// <summary>卡片本身的可见高度（不含投影预留的 10 DIP 透明边），堆叠时用它算间距。</summary>
    public double CardHeight
    {
        get
        {
            double height = ActualHeight;
            if (_settings.Overlay.Shadow) height -= 20;
            return Math.Max(0, height);
        }
    }

    /// <summary>
    /// 找出落点所在显示器。拖动结束时不能读窗口矩形：跨 DPI 的一瞬 WPF 可能
    /// 刚把窗口挪到旧 DIP 坐标上；用拖动目标 + 目标屏缩放估算窗口中心。
    /// </summary>
    int DetectMonitor(int physicalX, int physicalY)
    {
        try
        {
            ScreenInfo screen = ScreenGeometry.ForPoint(physicalX, physicalY);
            if (screen is null) return _settings.Overlay.Monitor;

            double scaleX = screen.ScaleX > 0.1 ? screen.ScaleX : 1.0;
            double scaleY = screen.ScaleY > 0.1 ? screen.ScaleY : 1.0;
            int cx = physicalX + (int)Math.Round(ActualWidth * scaleX / 2);
            int cy = physicalY + (int)Math.Round(ActualHeight * scaleY / 2);
            return ScreenGeometry.IndexForPoint(cx, cy);
        }
        catch (Exception ex)
        {
            Log.Warn($"识别显示器失败: {ex.Message}");
            return _settings.Overlay.Monitor;
        }
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_settings.Overlay.Locked) return;
        Action handler = SettingsRequested;
        if (handler is not null) handler();
    }

    void BuildContextMenu()
    {
        var menu = new ContextMenu();
        HudMenuStyle.Apply(menu);

        _menuToggle = new MenuItem();
        _menuToggle.Click += (s, e) => { Action h = HideRequested; if (h is not null) h(); };
        menu.Items.Add(_menuToggle);

        _menuSettings = new MenuItem();
        _menuSettings.Click += (s, e) => { Action h = SettingsRequested; if (h is not null) h(); };
        menu.Items.Add(_menuSettings);

        _menuLock = new MenuItem();
        _menuLock.Click += (s, e) => { Action h = LockToggleRequested; if (h is not null) h(); };
        menu.Items.Add(_menuLock);

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (s, e) => { Action h = ExitRequested; if (h is not null) h(); };
        menu.Items.Add(exit);

        ContextMenu = menu;
        UpdateMenuHeaders();
    }

    /// <summary>由控制器告诉每张卡片：当前有没有命令行卡片、它的热键是什么。</summary>
    public void SetCommandInfo(bool available, string hotkey)
    {
        // 右键菜单已裁成四个入口，不再展示快捷命令行；保留方法让旧调用点不用感知菜单变化。
    }

    void UpdateMenuHeaders()
    {
        if (_menuSettings is null) return;
        _menuSettings.Header = "打开设置";
        _menuSettings.InputGestureText = _settings.Hotkeys.OpenSettings;
        _menuToggle.Header = "隐藏叠层";
        _menuToggle.InputGestureText = _settings.Hotkeys.ToggleOverlay;
        _menuLock.Header = _settings.Overlay.Locked ? "解锁" : "锁定";
        _menuLock.InputGestureText = _settings.Hotkeys.ToggleLock;
    }
}
