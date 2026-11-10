using System;
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

namespace MirrorpondHUD.Ui
{
    public partial class OverlayWindow : Window
    {
        Settings _settings;
        DispatcherTimer _topmostTimer;
        bool _handleReady;
        bool _closed;
        bool _commandMode;
        IntPtr _previousForeground;

        /// <summary>
        /// 毛玻璃运行时（2026-11 审阅第三批第 12 项）：底板选择 / 截屏排除 / 兼容取图定时器
        /// 全部在 <see cref="GlassRuntime"/> 里，窗口只负责通知变化与提供自身句柄。
        /// </summary>
        readonly GlassRuntime _glassRuntime;

        /// <summary>底板需要的窗口参数（外观 + 可见性），抽出来是为了让底板类不直接读 _settings。</summary>
        internal string GlassTintColor { get { return _settings.Style.Background; } }
        internal bool GlassShadow { get { return _settings.Overlay.Shadow; } }
        internal bool GlassVisible { get { return IsVisible; } }

        // 玻璃运行时读的窗口状态：设置对象会被 Apply / RefreshSettings 换成新的，必须每次现读。
        internal Settings GlassSettings { get { return _settings; } }
        internal bool GlassHandleReady { get { return _handleReady; } }
        internal bool GlassClosed { get { return _closed; } }

        /// <summary>
        /// 当前实际生效的玻璃实现（运行时单一状态源）。设置窗的「当前实际」与拖动补帧都读这里，
        /// 不再各自按 <c>Style.GlassMode</c> 重算 —— 用户选 DesktopFeed 但已经降级的场景只有运行时知道。
        /// </summary>
        internal GlassRuntimeState GlassState { get { return _glassRuntime.State; } }

        bool _interactive;

        /// <summary>卡片内容需要鼠标 / 键盘（见 IRichCardModule.NeedsMouseInput）：这张卡不穿透。</summary>
        public bool Interactive
        {
            get { return _interactive; }
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
        Point _dragStartWindow;
        /// <summary>拖动中已经排队了一帧背景重取样（每个合成帧最多一次）。</summary>
        bool _dragFrameQueued;

        // 位置动画：补位 / 自动对齐翻边 / 松手回位。每个窗口独立计时，新的目标会从
        // 当前位置重新开始，避免上一段动画把窗口拽回旧目标。
        DispatcherTimer _moveTimer;
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
        MenuItem _menuCommand;
        MenuItem _menuLock;
        bool _commandAvailable;
        string _commandHotkey = "";

        public event Action SettingsRequested;
        public event Action HideRequested;
        public event Action ExitRequested;
        public event Action LockToggleRequested;
        /// <summary>按住卡片开始拖动前触发：调用方据此取消正在跑的补位动画。</summary>
        public event Action DragStarted;
        /// <summary>拖动中：参数是相对拖动起点的总位移（DIP）。其他卡片按增量跟随。</summary>
        public event Action<double, double> Dragging;
        /// <summary>拖动结束：参数是窗口相对拖动起点的位移（DIP）与所在的显示器序号。</summary>
        public event Action<double, double, int> Moved;
        public event Action CommandLineRequested;
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
            DpiChanged += (s, e) => _glassRuntime.UpdateBackdrop();
            Closed += (s, e) =>
            {
                _closed = true;
                StopTimers();
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

            _topmostTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(2500)
            };
            _topmostTimer.Tick += (s, args) =>
            {
                if (_settings.Overlay.Topmost && IsVisible)
                {
                    EnsureTopmost();            // 内部会重摆底板
                }
                else if (IsVisible && _settings.Style.Acrylic)
                {
                    // 非置顶时也要给一次机会：桌面亚克力因瞬态黑屏失败过以后，
                    // 冷却结束靠这里自动重试，而不是等用户改设置。
                    _glassRuntime.UpdateBackdrop();
                }
            };
            _topmostTimer.Start();
        }

        public void Apply(Settings settings, string text)
        {
            _settings = settings;
            // 设置变了就再给两块底板一次机会（底板对象保留，不重建）
            _glassRuntime.ResetAvailability();
            Surface.ClearContent();
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
            Surface.Apply(settings, "");
            Surface.SetContent(content);
            if (_handleReady) _glassRuntime.UpdateGlassCapture();
            Topmost = settings.Overlay.Topmost;
            UpdateClickThrough();
            UpdateMenuHeaders();
        }

        /// <summary>换成纯文字卡片时释放富内容（停掉视频 / 动图）。</summary>
        public void ClearRichContent()
        {
            Surface.ClearContent();
        }

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

        public void SetText(string text, bool cursor)
        {
            Surface.SetText(text, cursor);
        }

        public void SetText(string text, bool cursor, int cursorIndex)
        {
            Surface.SetText(text, cursor, cursorIndex);
        }

        /// <summary>切换命令行时重新按当前内容量尺寸（该长就长、该缩就缩一次）。</summary>
        public void ResetStableSize()
        {
            Surface.ResetStableSize();
        }

        public void SetLayoutText(string text)
        {
            Surface.SetLayoutText(text);
        }

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
                if (target != null) target.FocusInput();

                Log.Info("命令行模式：前台=" + foreground +
                         "，键盘焦点在面板输入行=" +
                         (target != null && Keyboard.FocusedElement is UIElement));
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

            try
            {
                if (_previousForeground != IntPtr.Zero && _previousForeground != new WindowInteropHelper(this).Handle)
                    Native.SetForegroundWindow(_previousForeground);
            }
            catch { }
            _previousForeground = IntPtr.Zero;

            Log.Info("命令行模式结束");
        }

        public bool IsCommandInputActive { get { return _commandMode; } }

        public void ApplyPosition()
        {
            ApplyPosition(false);
        }

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

            double left = _settings.Overlay.X;
            double top = _settings.Overlay.Y;

            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                if (screens.Length == 0) return;

                int index = Math.Max(0, Math.Min(_settings.Overlay.Monitor, screens.Length - 1));
                System.Windows.Forms.Screen screen = screens[index];

                double scale = 1.0;
                System.Drawing.Rectangle primary = screens[0].Bounds;
                if (primary.Width > 0)
                {
                    double candidate = SystemParameters.PrimaryScreenWidth / primary.Width;
                    if (candidate > 0.1 && candidate < 10) scale = candidate;
                }

                left = screen.WorkingArea.Left * scale + _settings.Overlay.X;
                top = screen.WorkingArea.Top * scale + _settings.Overlay.Y;
            }
            catch (Exception ex)
            {
                Log.Warn("定位失败: " + ex.Message);
            }

            if (animate && CardMotion.Enabled && IsVisible && !_closed)
            {
                AnimateTo(left, top, CardMotion.LayoutDurationMs);
            }
            else
            {
                StopPositionMotion();
                Left = left;
                Top = top;
            }

            _glassRuntime.UpdateBackdrop();

            if (_settings.Style.Acrylic) _glassRuntime.CaptureGlass();
        }

        /// <summary>
        /// 窗口位置动画：从当前位置在 durationMs 内 ease-out 到目标。新的目标会取消旧动画，
        /// 从当前实际位置重新开始；拖动开始前由控制器统一 StopPositionMotion。
        /// </summary>
        public void AnimateTo(double left, double top, int durationMs)
        {
            StopPositionMotion();

            if (!CardMotion.Enabled || !IsVisible || _closed || durationMs <= 0)
            {
                Left = left;
                Top = top;
                return;
            }

            if (Math.Abs(Left - left) < 0.5 && Math.Abs(Top - top) < 0.5)
            {
                Left = left;
                Top = top;
                return;
            }

            _moveFromLeft = Left;
            _moveFromTop = Top;
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
            double eased = CardMotion.EaseOut(t);

            Left = _moveFromLeft + (_moveToLeft - _moveFromLeft) * eased;
            Top = _moveFromTop + (_moveToTop - _moveFromTop) * eased;

            if (t >= 1)
            {
                Left = _moveToLeft;
                Top = _moveToTop;
                StopPositionMotion();
            }
        }

        public void StopPositionMotion()
        {
            if (_moveTimer == null) return;

            try { _moveTimer.Stop(); } catch { }
            _moveTimer = null;
        }

        /// <summary>当前是否有位置动画在跑；布局协调器据此避免瞬移打断呼出 / 补位。</summary>
        public bool IsPositionMotionActive
        {
            get { return _moveTimer != null; }
        }

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

            double targetTop = Top;
            Top = targetTop + CardMotion.ShowOffset;
            AnimateTo(Left, targetTop, CardMotion.ShowDurationMs);

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
                if (completed != null) completed();
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

            AnimateTo(Left, Top - CardMotion.HideOffset, CardMotion.HideDurationMs);

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

                if (completed != null) completed();
            };

            Surface.BeginAnimation(UIElement.OpacityProperty, opacity);
        }

        /// <summary>取消内容动画并恢复基值；只动 Surface，不碰窗口 / 玻璃状态。</summary>
        void CancelContentMotion()
        {
            _contentMotionVersion++;

            if (Surface == null) return;

            Surface.BeginAnimation(UIElement.OpacityProperty, null);
            Surface.Opacity = 1;
            Surface.IsHitTestVisible = true;
        }

        public void UpdateClickThrough()
        {
            if (!_handleReady) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int ex = Native.GetExStyle(hwnd);
            // 交互卡片（按钮 / 输入框）即使在锁定状态下也不开 WS_EX_TRANSPARENT，
            // 否则鼠标事件永远到不了卡片里的控件；代价是它会挡住身下的桌面点击。
            bool clickThrough = _settings.Overlay.Locked && !_commandMode && !_interactive;
            if (clickThrough)
                ex |= Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow;
            else
                ex &= ~(Native.WsExTransparent | Native.WsExNoActivate);

            Native.SetExStyle(hwnd, ex);

            Cursor = _commandMode ? Cursors.IBeam
                   : (clickThrough || _interactive) ? Cursors.Arrow : Cursors.SizeAll;
        }

        public void EnsureTopmost()
        {
            if (!_handleReady) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            Native.SetWindowPos(hwnd, Native.HwndTopmost, 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZorder);

            // 卡片重新置顶后，底板要重新插回它正下方（别的置顶窗口可能插进来）。
            _glassRuntime.UpdateBackdrop();

            // 置顶之后自己的子窗口（视频）会被压下去，通知内容层重新摆好。
            Action handler = RaisedTopmost;
            if (handler != null) handler();
        }

        // ==================== 毛玻璃 ====================
        // 三种实现（系统亚克力 / 桌面取样 / 兼容取样）的选择与生命周期已经搬进
        // GlassRuntime；窗口只保留底板回调与「什么时候通知」，不保留同名字段，避免双真相。
        // 实际生效的是哪一种只由 GlassRuntime.State 说了算（见上面的 GlassState）。

        /// <summary>
        /// 桌面取样连续全黑时由 GlassFeed 回调：转给玻璃运行时标记不可用并重新决策。
        /// 这个委托在 GlassBackdrops 里 attach 时按窗口类型取得，所以入口必须留在窗口上。
        /// </summary>
        internal void OnDesktopAcrylicUnavailable(string reason)
        {
            _glassRuntime.OnDesktopAcrylicUnavailable(reason);
        }

        /// <summary>
        /// 窗口关闭时停掉置顶刷新定时器（P2-4）。原来只 ReleaseBackdrop，
        /// 定时器仍每 2.5 秒把已关闭的窗口整棵可视树钉在 Dispatcher 上。
        /// 玻璃取图定时器随 <see cref="GlassRuntime.Dispose"/> 一起停。
        /// </summary>
        void StopTimers()
        {
            if (_topmostTimer != null)
            {
                try { _topmostTimer.Stop(); } catch { }
                _topmostTimer = null;
            }
        }

        /// <summary>
        /// 是否允许按住卡片移动整叠。卡片内容自己处理拖动时（画板、卡片内拖拽排序）宿主把它关掉，
        /// 鼠标按下就完全交给内容，不会再「一按就拖卡」。
        /// </summary>
        public bool DragEnabled
        {
            get { return _dragEnabled; }
            set { _dragEnabled = value; }
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
            AllowDrop = canDrop != null;
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
            if (handler == null) return;

            try { handler(over, data); }
            catch (Exception ex) { Log.Warn("卡片拖放状态回调失败: " + ex.Message); }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (_canDrop == null) return;

            bool ok;
            try { ok = _canDrop(e.Data); }
            catch { ok = false; }

            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            if (_dropped == null) return;

            e.Handled = true;
            try { _dropped(e.Data); }
            catch (Exception ex) { Log.Warn("卡片拖放处理失败: " + ex.Message); }
            NotifyDropState(false, null);
        }

        // ==================== 拖动 ====================
        // 不用 DragMove()：它在鼠标抬起过快、窗口未激活等情况下会直接抛异常，表现为"拖动失灵"。
        // 这里自己抓鼠标，用屏幕坐标（设备像素）算位移，再按 DPI 换算成 WPF 的 DIP。

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_settings.Overlay.Locked || _commandMode) return;
            if (!_dragEnabled) return;
            if (e.ClickCount > 1) return;

            _dragging = true;
            _dragStartScreen = PointToScreen(e.GetPosition(this));
            _dragStartWindow = new Point(Left, Top);

            // 先让控制器收掉所有正在跑的补位动画；否则拖动时窗口会和旧动画抢位置。
            Action started = DragStarted;
            if (started != null) started();

            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishDrag();
                return;
            }

            Point now = PointToScreen(e.GetPosition(this));
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
            double sy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;

            Left = _dragStartWindow.X + (now.X - _dragStartScreen.X) / sx;
            Top = _dragStartWindow.Y + (now.Y - _dragStartScreen.Y) / sy;

            Action<double, double> handler = Dragging;
            if (handler != null) handler(Left - _dragStartWindow.X, Top - _dragStartWindow.Y);

            QueueDragFrame();
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
            if (_dragging) FinishDrag();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_dragging) FinishDrag();
        }

        void FinishDrag()
        {
            _dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();

            double dx = Left - _dragStartWindow.X;
            double dy = Top - _dragStartWindow.Y;
            int monitor = DetectMonitor();

            _glassRuntime.CaptureGlass();
            GlassFeed.RequestFrame(true);

            Action<double, double, int> handler = Moved;
            if (handler != null) handler(dx, dy, monitor);
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

        /// <summary>找出窗口中心所在的显示器（拖动跨屏时用）。</summary>
        int DetectMonitor()
        {
            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                if (screens.Length == 0) return _settings.Overlay.Monitor;

                double scale = PrimaryScale();
                double cx = Left + ActualWidth / 2;
                double cy = Top + ActualHeight / 2;

                int index = 0;
                double best = double.MaxValue;

                for (int i = 0; i < screens.Length; i++)
                {
                    System.Drawing.Rectangle wa = screens[i].WorkingArea;
                    double l = wa.Left * scale;
                    double t = wa.Top * scale;
                    double w = wa.Width * scale;
                    double h = wa.Height * scale;
                    double distance = Math.Abs(cx - (l + w / 2)) + Math.Abs(cy - (t + h / 2));
                    if (distance < best)
                    {
                        best = distance;
                        index = i;
                    }
                }

                return index;
            }
            catch (Exception ex)
            {
                Log.Warn("识别显示器失败: " + ex.Message);
                return _settings.Overlay.Monitor;
            }
        }

        static double PrimaryScale()
        {
            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                if (screens.Length > 0 && screens[0].Bounds.Width > 0)
                {
                    double scale = SystemParameters.PrimaryScreenWidth / screens[0].Bounds.Width;
                    if (scale > 0.1 && scale < 10) return scale;
                }
            }
            catch { }
            return 1.0;
        }

        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_settings.Overlay.Locked) return;
            Action handler = SettingsRequested;
            if (handler != null) handler();
        }

        void BuildContextMenu()
        {
            var menu = new ContextMenu();

            _menuSettings = new MenuItem();
            _menuSettings.Click += (s, e) => { Action h = SettingsRequested; if (h != null) h(); };
            menu.Items.Add(_menuSettings);

            _menuCommand = new MenuItem();
            _menuCommand.Click += (s, e) => { Action h = CommandLineRequested; if (h != null) h(); };
            menu.Items.Add(_menuCommand);

            _menuToggle = new MenuItem();
            _menuToggle.Click += (s, e) => { Action h = HideRequested; if (h != null) h(); };
            menu.Items.Add(_menuToggle);

            _menuLock = new MenuItem();
            _menuLock.Click += (s, e) => { Action h = LockToggleRequested; if (h != null) h(); };
            menu.Items.Add(_menuLock);

            menu.Items.Add(new Separator());

            var exit = new MenuItem { Header = "退出" };
            exit.Click += (s, e) => { Action h = ExitRequested; if (h != null) h(); };
            menu.Items.Add(exit);

            ContextMenu = menu;
            UpdateMenuHeaders();
        }

        static string WithHotkey(string text, string hotkey)
        {
            return string.IsNullOrWhiteSpace(hotkey) ? text : text + " (" + hotkey + ")";
        }

        /// <summary>由控制器告诉每张卡片：当前有没有命令行卡片、它的热键是什么。</summary>
        public void SetCommandInfo(bool available, string hotkey)
        {
            _commandAvailable = available;
            _commandHotkey = hotkey ?? "";
            if (_menuCommand != null)
                _menuCommand.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            UpdateMenuHeaders();
        }

        void UpdateMenuHeaders()
        {
            if (_menuSettings == null) return;
            _menuSettings.Header = WithHotkey("打开设置", _settings.Hotkeys.OpenSettings);
            _menuCommand.Header = WithHotkey("快捷命令行", _commandHotkey);
            _menuToggle.Header = WithHotkey("显示 / 隐藏叠层", _settings.Hotkeys.ToggleOverlay);
            _menuLock.Header = WithHotkey("锁定叠层", _settings.Hotkeys.ToggleLock);
        }
    }
}
