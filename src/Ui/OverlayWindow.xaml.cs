using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        bool _commandMode;
        IntPtr _previousForeground;
        DispatcherTimer _glassTimer;

        /// <summary>系统亚克力底板（方案 G）：null = 没启用 / 不可用，毛玻璃走截屏取样。</summary>
        GlassBackdrop _backdrop;
        bool _systemActive;
        bool _backdropUnavailable;

        /// <summary>桌面亚克力（截屏 + 模糊画进卡片）：卡片在这种模式下会被排除在截屏之外。</summary>
        bool _acrylicActive;
        bool _acrylicUnavailable;

        /// <summary>
        /// 有一块底板正在卡片下面画模糊。三种实现是互斥的：谁在干活谁就是 true，
        /// 切换实现时另一块必须收掉（否则会留下没人管的玻璃板，见 <see cref="RetireSystemBackdrop"/>）。
        /// </summary>
        bool BackdropActive { get { return _systemActive || _acrylicActive; } }

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
        Point _dragStartScreen;
        Point _dragStartWindow;
        /// <summary>拖动中已经排队了一帧背景重取样（每个合成帧最多一次）。</summary>
        bool _dragFrameQueued;
        /// <summary>上一次重设「截屏排除」的时间：拖动时每个鼠标移动都会走到那里，不节流会卡成幻灯片。</summary>
        DateTime _lastExclude = DateTime.MinValue;

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

            SourceInitialized += OnSourceInitialized;
            Deactivated += (s, e) => { if (_settings.Overlay.Topmost) EnsureTopmost(); };

            // 亚克力底板要跟着卡片走：位置 / 尺寸 / 可见性 / DPI 任何一个变了都重摆。
            LocationChanged += (s, e) => UpdateBackdrop();
            SizeChanged += (s, e) => UpdateBackdrop();
            IsVisibleChanged += (s, e) => UpdateBackdrop();
            DpiChanged += (s, e) => UpdateBackdrop();
            Closed += (s, e) => ReleaseBackdrop();

            BuildContextMenu();
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            _handleReady = true;
            ApplyPosition();
            UpdateGlassCapture();
            UpdateClickThrough();
            EnsureTopmost();

            _topmostTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(2500)
            };
            _topmostTimer.Tick += (s, args) =>
            {
                if (_settings.Overlay.Topmost && IsVisible) EnsureTopmost();
            };
            _topmostTimer.Start();
        }

        public void Apply(Settings settings, string text)
        {
            _settings = settings;
            _backdropUnavailable = false;   // 设置变了就再给两块底板一次机会
            _acrylicUnavailable = false;
            Surface.ClearContent();
            Surface.Apply(settings, text, false);
            if (_handleReady) UpdateGlassCapture();
            Topmost = settings.Overlay.Topmost;
            UpdateClickThrough();
            UpdateMenuHeaders();
        }

        /// <summary>富内容卡片（图片 / 动图 / 视频）：文字层空着，直接把控件放进卡片。</summary>
        public void ApplyRich(Settings settings, FrameworkElement content)
        {
            _settings = settings;
            _backdropUnavailable = false;
            _acrylicUnavailable = false;
            Surface.Apply(settings, "");
            Surface.SetContent(content);
            if (_handleReady) UpdateGlassCapture();
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
            UpdateBackdrop();

            if (_settings.Style.Acrylic) CaptureGlass();
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
            UpdateBackdrop();

            // 置顶之后自己的子窗口（视频）会被压下去，通知内容层重新摆好。
            Action handler = RaisedTopmost;
            if (handler != null) handler();
        }

        // ==================== 毛玻璃 ====================
        // 三种实现：
        // 1) 系统亚克力（默认）：卡片下面一个独立的底板窗口，挂未文档化的 accent 亚克力，
        //    DWM / GPU 负责模糊，进程侧零逐帧工作，模糊半径由系统决定；
        // 2) 桌面亚克力：截屏 + 自己模糊，把结果**画进卡片自己的内容层**（见 GlassFeed）。
        //    代价：为了让截屏不拍到自己，卡片必须排除在截屏之外 —— 这个模式下截图 / 录像里没有卡片；
        // 3) 兼容：把卡片后面的画面拼出来（枚举窗口 + PrintWindow + 壁纸），缩小模糊后当底色，
        //    可调半径与帧率，任何系统都能用。
        // 1 / 2 不可用时自动退回 3。

        /// <summary>
        /// 决定这一帧由谁提供模糊。
        /// 三种实现互斥：选中的那块接管之前，先用 <see cref="RetireSystemBackdrop"/> /
        /// <see cref="RetireAcrylicBackdrop"/> 把另一块收掉 —— 否则切过去以后旧底板还在屏幕上
        /// （系统亚克力会留下灰色玻璃板，桌面亚克力会留下「卡片仍然不进截图」）。
        /// 没有底板接管时 <see cref="BackdropActive"/> 为 false，兼容实现的截屏取样照常工作。
        /// </summary>
        void UpdateBackdrop()
        {
            if (!_handleReady) return;

            bool acrylic = _settings.Style.Acrylic;
            string mode = Settings.NormalizeGlassMode(_settings.Style.GlassMode);

            // 取样参数（取图方式 / 模糊强度 / 刷新帧率 / 是否留投影边）随时可能被改，变了下一轮就按新值来。
            // 放在 acrylic 判断之前，免得「先关毛玻璃改设置、再打开」时沿用旧参数。
            GlassFeed.Configure(_settings.Style.GlassSource, _settings.Style.GlassBlur,
                                _settings.Style.GlassFps, _settings.Overlay.Shadow);

            if (!acrylic)
            {
                HideBackdrops();
                return;
            }

            if (mode == "Dwm" && !_acrylicUnavailable)
            {
                RetireSystemBackdrop();
                UpdateAcrylicBackdrop();
            }
            else if (mode == "Dwm" || mode == "System")
            {
                RetireAcrylicBackdrop();
                UpdateSystemBackdrop();
            }
            else
            {
                HideBackdrops();
            }
        }

        /// <summary>收起系统亚克力底板（底板窗口留着，下次切回来直接复用）。</summary>
        void RetireSystemBackdrop()
        {
            if (!_systemActive) return;

            _systemActive = false;
            if (_backdrop != null) _backdrop.Follow(this, _settings.Overlay.Shadow, false);
        }

        /// <summary>收起桌面亚克力：停掉取样、清掉卡片里的玻璃层，并把卡片从「排除截屏」里放回来。</summary>
        void RetireAcrylicBackdrop()
        {
            if (!_acrylicActive) return;

            _acrylicActive = false;
            _lastExclude = DateTime.MinValue;
            GlassFeed.Detach(this);
            GlassFeed.ExcludeFromCapture(this, false);
        }

        /// <summary>方案 G：未文档化的 accent 亚克力底板。</summary>
        void UpdateSystemBackdrop()
        {
            if (_backdropUnavailable)
            {
                HideBackdrops();
                return;
            }

            if (_backdrop == null)
            {
                string reason;
                if (!GlassBackdrop.TryCreate(out _backdrop, out reason))
                {
                    _backdropUnavailable = true;
                    Log.Warn("系统亚克力底板不可用（" + reason + "），毛玻璃改用截屏取样");
                    HideBackdrops();
                    return;
                }

                Log.Info("毛玻璃改用系统亚克力底板：模糊在 DWM / GPU 上完成，进程侧不再抓屏");
            }

            if (!_systemActive)
            {
                _systemActive = true;
                Log.Info("毛玻璃背景已启用（系统亚克力底板，模糊半径由系统决定）");
            }

            _backdrop.SetTint(OverlaySurface.ParseColor(_settings.Style.Background));
            _backdrop.Follow(this, _settings.Overlay.Shadow, IsVisible);
        }

        /// <summary>
        /// 桌面亚克力：取屏幕那块画面、缩小模糊，画进**卡片自己的内容层**（见 <see cref="GlassFeed"/>）。
        /// 卡片必须排除在截屏之外，否则取样会把自己拍进去。
        /// </summary>
        void UpdateAcrylicBackdrop()
        {
            if (_acrylicUnavailable)
            {
                HideBackdrops();
                return;
            }

            string reason;
            if (!GlassFeed.IsAvailable(out reason))
            {
                _acrylicUnavailable = true;
                Log.Warn("桌面亚克力不可用（" + reason + "），毛玻璃退回系统亚克力底板");
                HideBackdrops();
                return;
            }

            bool first = !_acrylicActive;
            _acrylicActive = true;

            // 取样的是屏幕：卡片（连同 owner 是它的子窗口，比如视频窗）必须先排除在截屏之外。
            // 这活儿要 EnumWindows 全表 + 逐个 SetWindowDisplayAffinity，拖动时每个鼠标移动都会走到这里，
            // 不节流会把 UI 线程占满、取样反而停住（表现就是"拖动时背景不刷新"）。2 秒重设一次足够覆盖新建的子窗口。
            if (first || (DateTime.UtcNow - _lastExclude).TotalSeconds >= 2.0)
            {
                GlassFeed.ExcludeFromCapture(this, true);
                _lastExclude = DateTime.UtcNow;
            }

            GlassFeed.Attach(this, OnAcrylicBackdropUnavailable);

            if (first) Log.Info("毛玻璃背景已启用（桌面亚克力：截屏 + 模糊后画进卡片，卡片不进截图）");
        }

        /// <summary>
        /// 取不到背景（连续几帧取样全黑：截屏被安全策略挡掉，或者屏幕本来就是黑的）。
        /// 退回系统亚克力 / 兼容实现，同时把卡片从「排除截屏」里放回来。
        /// </summary>
        void OnAcrylicBackdropUnavailable(string reason)
        {
            if (_acrylicUnavailable) return;

            _acrylicUnavailable = true;
            Log.Warn("桌面亚克力不可用（" + reason + "），毛玻璃退回系统亚克力底板");

            // 顺序：先撤掉截屏排除，再按「系统」那条路重新决定用哪块底板
            RetireAcrylicBackdrop();
            UpdateGlassCapture();
        }

        /// <summary>关掉毛玻璃 / 切回兼容实现：两块底板和截屏排除都要撤掉。</summary>
        void HideBackdrops()
        {
            RetireSystemBackdrop();
            RetireAcrylicBackdrop();
        }

        void ReleaseBackdrop()
        {
            HideBackdrops();

            if (_backdrop != null)
            {
                try { _backdrop.Dispose(); }
                catch (Exception ex) { Log.Warn("释放亚克力底板失败: " + ex.Message); }
                _backdrop = null;
            }
        }

        void UpdateGlassCapture()
        {
            if (!_handleReady) return;

            UpdateBackdrop();

            // 系统亚克力由底板窗口在卡片下面画模糊，卡片内部这层要关掉，也不用跑定时器；
            // 桌面亚克力的模糊图**正是画在卡片内部**（GlassFeed），这里不能清。
            if (BackdropActive)
            {
                if (_glassTimer != null) _glassTimer.Stop();
                if (_systemActive) Surface.ClearGlass();
                return;
            }

            bool enabled = _settings.Style.Acrylic;

            if (!enabled)
            {
                if (_glassTimer != null) _glassTimer.Stop();
                Surface.ClearGlass();
                return;
            }

            int fps = Math.Max(2, Math.Min(30, _settings.Style.AcrylicFps));
            var interval = TimeSpan.FromMilliseconds(1000.0 / fps);

            if (_glassTimer == null)
            {
                _glassTimer = new DispatcherTimer(DispatcherPriority.Background)
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
                Log.Info("毛玻璃背景已启用（" + fps + " 帧/秒，模糊 " + _settings.Style.AcrylicBlur + "px）");
            }

            CaptureGlass();
        }

        bool _glassBusy;

        /// <summary>
        /// 兼容实现每画出一帧玻璃就记一条 GLAS 时间戳（MIRRORPOND_GLASS_TRACE=1）。
        /// 卡片排除在截屏之外以后没法靠截图验收，只能靠这种时间戳量刷新节奏。
        /// </summary>
        bool TraceGlassFrames
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("MIRRORPOND_GLASS_TRACE");
                return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        void CaptureGlass()
        {
            if (BackdropActive) return;                     // 系统底板接管时不抓屏
            if (!_settings.Style.Acrylic || !_handleReady || _glassBusy) return;
            if (!IsVisible || WindowState == WindowState.Minimized) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            Native.Rect rect;
            if (!Native.GetWindowRect(hwnd, out rect)) return;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 8 || height < 8) return;

            int left = rect.Left;
            int top = rect.Top;
            double blur = _settings.Style.AcrylicBlur;

            // 卡片后面可能压着好几个窗口，甚至只压着桌面：
            // FindLayers 会按 z 序收集（桌面垫底），Grab 再逐点选最上层的那一层。
            List<GlassCapture.Layer> layers = GlassCapture.FindLayers(left, top, width, height);
            if (layers.Count == 0)
            {
                Surface.ClearGlass();
                return;
            }

            _glassBusy = true;

            Task.Run(() =>
            {
                BitmapSource image = null;
                try
                {
                    image = GlassCapture.Grab(layers, left, top, width, height, blur);
                }
                catch (Exception ex)
                {
                    Log.Warn("毛玻璃取样失败: " + ex.Message);
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _glassBusy = false;
                        if (image != null)
                        {
                            Surface.SetGlass(image);
                            if (TraceGlassFrames)
                                Log.Info("GLAS " + DateTime.Now.ToString("HH:mm:ss.fff") + " hwnd=" + hwnd.ToInt64());
                        }
                    }));
                }
                catch
                {
                    _glassBusy = false;
                }
            });
        }

        // ==================== 拖动 ====================
        // 不用 DragMove()：它在鼠标抬起过快、窗口未激活等情况下会直接抛异常，表现为"拖动失灵"。
        // 这里自己抓鼠标，用屏幕坐标（设备像素）算位移，再按 DPI 换算成 WPF 的 DIP。

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_settings.Overlay.Locked || _commandMode) return;
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
            bool dwm = acrylic && mode == "Dwm" && !_acrylicUnavailable;
            bool compat = acrylic && !dwm && !BackdropActive;   // 兼容实现（截屏拼接）

            if (!dwm && !compat) return;

            _dragFrameQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _dragFrameQueued = false;
                if (!_dragging) return;
                if (dwm) GlassFeed.RequestFrame(false);
                else if (compat) CaptureGlass();
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

            CaptureGlass();
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
