using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    public partial class OverlayWindow : Window
    {
        readonly Settings _settings;
        DispatcherTimer _topmostTimer;
        bool _handleReady;
        bool _commandMode;
        IntPtr _previousForeground;

        MenuItem _menuSettings;
        MenuItem _menuToggle;
        MenuItem _menuCommand;
        MenuItem _menuLock;

        public event Action SettingsRequested;
        public event Action HideRequested;
        public event Action ExitRequested;
        public event Action LockToggleRequested;
        public event Action PositionChanged;
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
            Surface.Apply(settings, text);
            Topmost = settings.Overlay.Topmost;
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
        }

        public void EnsureTopmost()
        {
            if (!_handleReady) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            Native.SetWindowPos(hwnd, Native.HwndTopmost, 0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZorder);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_settings.Overlay.Locked) return;

            try { DragMove(); }
            catch { }

            CapturePosition();
            Action handler = PositionChanged;
            if (handler != null) handler();
        }

        /// <summary>把窗口位置换算成「所在显示器的相对偏移」，避免多屏时反复叠加屏幕原点。</summary>
        void CapturePosition()
        {
            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                if (screens.Length == 0) return;

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

                System.Drawing.Rectangle area = screens[index].WorkingArea;
                _settings.Overlay.Monitor = index;
                _settings.Overlay.X = Left - area.Left * scale;
                _settings.Overlay.Y = Top - area.Top * scale;
            }
            catch (Exception ex)
            {
                Log.Warn("记录位置失败: " + ex.Message);
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

        void UpdateMenuHeaders()
        {
            if (_menuSettings == null) return;
            _menuSettings.Header = WithHotkey("打开设置", _settings.Hotkeys.OpenSettings);
            _menuCommand.Header = WithHotkey("快捷命令行", _settings.Hotkeys.CommandLine);
            _menuToggle.Header = WithHotkey("显示 / 隐藏叠层", _settings.Hotkeys.ToggleOverlay);
            _menuLock.Header = WithHotkey("锁定叠层", _settings.Hotkeys.ToggleLock);
        }
    }
}
