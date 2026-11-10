using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Threading;
using System.Windows.Media;
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
            if (!_handleReady) return;

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

            Left = left;
            Top = top;
            _glassRuntime.UpdateBackdrop();

            if (_settings.Style.Acrylic) _glassRuntime.CaptureGlass();
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
        // 三种实现（系统亚克力 / 桌面亚克力 / 兼容取样）的选择与生命周期已经搬进
        // GlassRuntime；窗口只保留底板回调与「什么时候通知」，不保留同名字段，避免双真相。

        /// <summary>
        /// 桌面亚克力取样连续全黑时由 GlassFeed 回调：转给玻璃运行时标记不可用并重新决策。
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

            bool acrylic = _settings.Style.Acrylic;
            string mode = Settings.NormalizeGlassMode(_settings.Style.GlassMode);
            bool dwm = acrylic && mode == "Dwm" && !_glassRuntime.DesktopAcrylicUnavailable;
            bool compat = acrylic && !dwm && !_glassRuntime.BackdropActive;   // 兼容实现（截屏拼接）

            if (!dwm && !compat) return;

            _dragFrameQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _dragFrameQueued = false;
                if (!_dragging) return;
                if (dwm) GlassFeed.RequestFrame(false);
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
