using System;
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

        bool _dragging;
        Point _dragStartScreen;
        Point _dragStartWindow;

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
        public event Action<string, int> CommandTextChanged;
        public event Action CommandSubmitted;
        public event Action CommandCancelled;

        public OverlayWindow(Settings settings)
        {
            InitializeComponent();
            _settings = settings;

            SourceInitialized += OnSourceInitialized;
            Deactivated += (s, e) => { if (_settings.Overlay.Topmost) EnsureTopmost(); };

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
            Surface.Apply(settings, text);
            if (_handleReady) UpdateGlassCapture();
            Topmost = settings.Overlay.Topmost;
            UpdateClickThrough();
            UpdateMenuHeaders();
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

        public void BeginCommandInput()
        {
            if (!_handleReady || _commandMode) return;

            _commandMode = true;
            _previousForeground = Native.GetForegroundWindow();

            UpdateClickThrough();
            Focusable = true;

            CommandInput.IsEnabled = true;
            CommandInput.Text = "";
            CommandInput.Visibility = Visibility.Visible;
            InputMethod.SetIsInputMethodEnabled(CommandInput, false);

            Activate();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            bool foreground = Native.ForceForeground(hwnd);

            Keyboard.Focus(CommandInput);
            CommandInput.Focus();

            Log.Info("命令行模式：前台=" + foreground +
                     "，键盘焦点=" + ReferenceEquals(Keyboard.FocusedElement, CommandInput));
        }

        public void EndCommandInput()
        {
            if (!_commandMode) return;

            _commandMode = false;
            CommandInput.IsEnabled = false;   // 失能后键盘不会再落到这个隐藏输入框
            CommandInput.Text = "";

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

        public void SetCommandText(string text)
        {
            CommandInput.Text = text ?? "";
            CommandInput.CaretIndex = CommandInput.Text.Length;
        }

        void CommandInputTextChanged(object sender, TextChangedEventArgs e)
        {
            Action<string, int> handler = CommandTextChanged;
            if (handler != null) handler(CommandInput.Text ?? "", CommandInput.CaretIndex);
        }

        void CommandInputSelectionChanged(object sender, RoutedEventArgs e)
        {
            Action<string, int> handler = CommandTextChanged;
            if (handler != null) handler(CommandInput.Text ?? "", CommandInput.CaretIndex);
        }

        void CommandInputPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_commandMode) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Action handler = CommandSubmitted;
                if (handler != null) handler();
            }
            else if (e.Key == Key.Escape || e.Key == Key.F9)
            {
                e.Handled = true;
                Action handler = CommandCancelled;
                if (handler != null) handler();
            }
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

            if (_settings.Style.Acrylic) CaptureGlass();
        }

        public void UpdateClickThrough()
        {
            if (!_handleReady) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int ex = Native.GetExStyle(hwnd);
            bool clickThrough = _settings.Overlay.Locked && !_commandMode;
            if (clickThrough)
                ex |= Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow;
            else
                ex &= ~(Native.WsExTransparent | Native.WsExNoActivate);

            Native.SetExStyle(hwnd, ex);

            Cursor = _commandMode ? Cursors.IBeam : (clickThrough ? Cursors.Arrow : Cursors.SizeAll);
        }

        public void EnsureTopmost()
        {
            if (!_handleReady) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            Native.SetWindowPos(hwnd, Native.HwndTopmost, 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZorder);
        }

        // ==================== 毛玻璃 ====================
        // 抓取窗口后面那块桌面、缩小模糊后当作叠层背景。
        // 抓屏时给窗口设置 WDA_EXCLUDEFROMCAPTURE，这样截到的画面里不会包含叠层自己。

        void UpdateGlassCapture()
        {
            if (!_handleReady) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool enabled = _settings.Style.Acrylic && Native.IsAcrylicSupported;

            if (!enabled)
            {
                if (_glassTimer != null) _glassTimer.Stop();
                Surface.ClearGlass();
                return;
            }

            if (_glassTimer == null)
            {
                _glassTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(1200)
                };
                _glassTimer.Tick += (s, e) => CaptureGlass();
            }

            _glassTimer.Start();
            CaptureGlass();
            Log.Info("毛玻璃背景已启用（每 1.2 秒刷新一次）");
        }

        bool _glassBusy;

        void CaptureGlass()
        {
            if (!_settings.Style.Acrylic || !_handleReady || !IsVisible || _glassBusy) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            Native.Rect rect;
            if (!Native.GetWindowRect(hwnd, out rect)) return;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 4 || height < 4) return;

            _glassBusy = true;
            int left = rect.Left;
            int top = rect.Top;

            Task.Run(() =>
            {
                BitmapSource image = null;
                try
                {
                    // 抓屏瞬间把叠层从画面里排除掉，抓完立刻恢复（否则用户截图也会看不到叠层）
                    Native.SetCaptureExclusion(hwnd, true);
                    Thread.Sleep(20);
                    image = GlassCapture.Grab(left, top, width, height, _settings.Style.AcrylicBlur);
                }
                catch (Exception ex)
                {
                    Log.Warn("毛玻璃抓屏失败: " + ex.Message);
                }
                finally
                {
                    Native.SetCaptureExclusion(hwnd, false);
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _glassBusy = false;
                    if (image != null) Surface.SetGlass(image);
                }));
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

            Action<double, double, int> handler = Moved;
            if (handler != null) handler(dx, dy, monitor);
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
