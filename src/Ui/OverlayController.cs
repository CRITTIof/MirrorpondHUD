using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    /// <summary>把配置、采样、叠层窗口、托盘、热键串起来。每个子系统独立兜底，单点失败不影响其它功能。</summary>
    public sealed class OverlayController : IDisposable
    {
        enum IntroPhase
        {
            WelcomeTyping,
            WelcomeHold,
            WelcomeErase,
            ContentTyping,
            Live
        }

        readonly List<string> _args;
        readonly KeyHistory _keys = new KeyHistory();

        Settings _settings;
        MetricEngine _engine;
        KeyboardHook _hook;
        OverlayWindow _overlay;
        SettingsWindow _settingsWindow;
        TrayIcon _tray;
        HotkeyManager _hotkeys;
        DispatcherTimer _renderTimer;
        DispatcherTimer _introTimer;
        EventWaitHandle _activation;
        Thread _activationThread;
        MetricsSnapshot _last = new MetricsSnapshot();
        volatile bool _disposed;
        bool _overlayVisible;

        IntroPhase _phase = IntroPhase.Live;
        DateTime _phaseStart;
        string _introWelcome = "";
        string _introContent = "";
        string _shownText = "";

        public OverlayController(Settings settings, List<string> args)
        {
            _settings = settings;
            _args = args ?? new List<string>();
        }

        public Settings CurrentSettings { get { return _settings; } }

        public void Start()
        {
            Log.Info("=== 开始启动 ===");
            _last = new MetricsSnapshot();
            _keys.Configure(_settings.Data.KeyHistoryCount, _settings.Data.RepeatThreshold,
                            _settings.Data.MaxKeyChars);

            _overlay = new OverlayWindow(_settings);
            _overlay.SettingsRequested += ShowSettings;
            _overlay.HideRequested += ToggleVisibility;
            _overlay.LockToggleRequested += ToggleLock;
            _overlay.ExitRequested += Exit;
            _overlay.PositionChanged += SaveSettings;

            bool intro = _settings.Behavior.IntroEnabled;
            if (intro) _overlay.SetLayoutText(TemplateEngine.RenderPlaceholder(_settings.Content, "---"));
            _overlay.Apply(_settings, intro ? "" : RenderText());
            _overlay.Topmost = _settings.Overlay.Topmost;

            bool minimized = _settings.Behavior.StartMinimized || _args.Contains("--minimized");
            if (!minimized)
            {
                _overlay.Show();
                _overlayVisible = true;
                if (intro) BeginIntro();
            }
            else
            {
                _phase = IntroPhase.Live;
            }
            Log.Info("叠层窗口已创建，可见=" + _overlayVisible + "，置顶=" + _settings.Overlay.Topmost +
                     "，锁定=" + _settings.Overlay.Locked);

            _renderTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _renderTimer.Tick += (s, e) => RenderNow();
            _renderTimer.Start();

            Safe("键盘钩子", StartKeyboardHook);
            Safe("采样引擎", StartEngine);
            Safe("全局热键", StartHotkeys);
            Safe("托盘图标", StartTray);
            Safe("开机自启", ApplyAutoStart);
            Safe("单实例唤醒", StartActivationListener);

            Log.Info("启动完成（叠层可见=" + _overlayVisible + "，热键=" + _settings.Hotkeys.ToggleOverlay +
                     "/" + _settings.Hotkeys.OpenSettings + "/" + _settings.Hotkeys.ToggleLock + "）");

            ShowStartupBalloon();
        }

        void Safe(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(name + " 初始化失败", ex);
            }
        }

        // ==================== 各子系统 ====================

        void StartKeyboardHook()
        {
            _hook = new KeyboardHook(_keys);
            bool ok = _hook.Start();
            Log.Info("键盘钩子：" + (ok ? "已安装" : "安装失败"));
        }

        void StartEngine()
        {
            _engine = new MetricEngine(_settings);
            _engine.Updated += OnSnapshot;
            _engine.Start();
        }

        void StartHotkeys()
        {
            _hotkeys = new HotkeyManager();
            _hotkeys.HotkeyPressed += OnHotkey;
            RegisterHotkeys();
        }

        void StartTray()
        {
            _tray = new TrayIcon();
            _tray.ToggleVisibility += ToggleVisibility;
            _tray.OpenSettings += ShowSettings;
            _tray.ToggleLock += ToggleLock;
            _tray.ToggleAutoStart += ToggleAutoStart;
            _tray.Exit += Exit;

            _tray.Visible = _settings.Behavior.TrayEnabled;
            UpdateTray();

            int left, top, right, bottom;
            if (!_tray.Visible)
            {
                Log.Info("托盘图标：未启用");
            }
            else if (_tray.TryGetRect(out left, out top, out right, out bottom))
            {
                Log.Info("托盘图标：已显示，位置 " + left + "," + top + " - " + right + "," + bottom);
            }
            else
            {
                Log.Warn("托盘图标：已添加但未取得屏幕位置（Win11 默认收在“隐藏图标”里）");
            }

            // 托盘被安全策略拦掉时不要让用户“无处可点”，直接打开设置窗口
            if (!_tray.Visible)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(ShowSettings), DispatcherPriority.Background);
                Log.Warn("托盘不可用，已自动打开设置窗口作为兜底入口");
            }
        }

        void ShowStartupBalloon()
        {
            if (_tray == null || !_tray.Visible) return;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _tray.ShowBalloon("桌面叠层已启动",
                    "左键单击托盘图标，或按 " + _settings.Hotkeys.OpenSettings + " 打开设置");
            };
            timer.Start();
        }

        void StartActivationListener()
        {
            _activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\WindowOverlay.Activate");
            _activationThread = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        if (_activation.WaitOne(500) && !_disposed)
                        {
                            Application.Current.Dispatcher.BeginInvoke(new Action(ShowSettings));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("唤醒监听退出: " + ex.Message);
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "WindowOverlay.Activate"
            };
            _activationThread.Start();
            Log.Info("单实例唤醒监听已启动");
        }

        // ==================== 渲染 ====================

        string RenderText()
        {
            try
            {
                Dictionary<string, object> vars = Variables.Build(_last, _settings, _keys);
                return TemplateEngine.Render(_settings.Content, vars);
            }
            catch (Exception ex)
            {
                Log.Warn("渲染失败: " + ex.Message);
                return "(渲染失败)";
            }
        }

        void RenderNow()
        {
            if (!_overlayVisible || _overlay == null) return;
            if (_phase != IntroPhase.Live) return;   // 动画期间不刷新真实数据
            _overlay.SetText(RenderText(), false);
        }

        // ==================== 启动动画 ====================

        void BeginIntro()
        {
            if (_overlay == null || !_overlayVisible || !_settings.Behavior.IntroEnabled)
            {
                _phase = IntroPhase.Live;
                RenderNow();
                return;
            }

            _introWelcome = string.IsNullOrEmpty(_settings.Behavior.WelcomeText)
                ? "Welcome Back"
                : _settings.Behavior.WelcomeText;
            _introContent = TemplateEngine.RenderPlaceholder(_settings.Content, "---");
            _shownText = "";

            // 先用完整内容把整块尺寸定下来，逐字打字时窗口和文本位置都不会抖动
            _overlay.SetLayoutText(_introContent);
            _overlay.SetText("", true);
            _phase = IntroPhase.WelcomeTyping;
            _phaseStart = DateTime.UtcNow;

            if (_introTimer == null)
            {
                _introTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(20)
                };
                _introTimer.Tick += (s, e) => IntroTick();
            }

            _introTimer.Start();
            Log.Info("启动动画开始（欢迎语=" + _introWelcome + "，速度=" + _settings.Behavior.TypeSpeedMs + "ms/字）");
        }

        void ShowIntroText(string text)
        {
            if (text == _shownText) return;
            _shownText = text;
            _overlay.SetText(text, true);
        }

        void IntroTick()
        {
            if (_phase == IntroPhase.Live)
            {
                _introTimer.Stop();
                return;
            }

            double speed = Math.Max(8, _settings.Behavior.TypeSpeedMs);
            double elapsed = (DateTime.UtcNow - _phaseStart).TotalMilliseconds;
            int chars = (int)(elapsed / speed);

            switch (_phase)
            {
                case IntroPhase.WelcomeTyping:
                {
                    int n = Math.Min(chars, _introWelcome.Length);
                    ShowIntroText(_introWelcome.Substring(0, n));
                    if (n >= _introWelcome.Length)
                    {
                        _phase = IntroPhase.WelcomeHold;
                        _phaseStart = DateTime.UtcNow;
                    }
                    break;
                }

                case IntroPhase.WelcomeHold:
                    if ((DateTime.UtcNow - _phaseStart).TotalMilliseconds >= 650)
                    {
                        _phase = IntroPhase.WelcomeErase;
                        _phaseStart = DateTime.UtcNow;
                    }
                    break;

                case IntroPhase.WelcomeErase:
                {
                    int removed = Math.Min(chars, _introWelcome.Length);
                    int remain = _introWelcome.Length - removed;
                    ShowIntroText(remain <= 0 ? "" : _introWelcome.Substring(0, remain));
                    if (remain <= 0)
                    {
                        _phase = IntroPhase.ContentTyping;
                        _phaseStart = DateTime.UtcNow;
                    }
                    break;
                }

                case IntroPhase.ContentTyping:
                {
                    int n = Math.Min(chars, _introContent.Length);
                    ShowIntroText(_introContent.Substring(0, n));
                    if (n >= _introContent.Length) FinishIntro();
                    break;
                }
            }
        }

        void FinishIntro()
        {
            if (_phase == IntroPhase.Live) return;
            _phase = IntroPhase.Live;
            if (_introTimer != null) _introTimer.Stop();
            _overlay.SetLayoutText(null);
            Log.Info("启动动画结束，光标消失，开始刷新数据");
            RenderNow();
        }

        void OnSnapshot(MetricsSnapshot snapshot)
        {
            _last = snapshot;

            if (_phase != IntroPhase.Live)
            {
                // 动画期间不刷新内容，但可以按真实数据把整块尺寸先撑好，
                // 这样动画结束时不会突然变宽、文字位置也不会跳。
                Application.Current.Dispatcher.BeginInvoke(new Action(UpdateIntroLayout), DispatcherPriority.Background);
                return;
            }

            Application.Current.Dispatcher.BeginInvoke(new Action(RenderNow), DispatcherPriority.Background);
        }

        void UpdateIntroLayout()
        {
            if (_phase == IntroPhase.Live || _overlay == null) return;
            _overlay.SetLayoutText(RenderText());
        }

        // ==================== 操作 ====================

        public void ToggleVisibility()
        {
            if (_overlay == null) return;

            if (_overlayVisible)
            {
                if (_phase != IntroPhase.Live) FinishIntro();
                _overlay.Hide();
                _overlayVisible = false;
            }
            else
            {
                _overlay.SetLayoutText(null);
                _overlay.Apply(_settings, RenderText());
                _overlay.Show();
                _overlay.UpdateClickThrough();
                _overlay.EnsureTopmost();
                _overlayVisible = true;
                RenderNow();
            }

            Log.Info("叠层可见=" + _overlayVisible);
            UpdateTray();
        }

        public void ToggleLock()
        {
            _settings.Overlay.Locked = !_settings.Overlay.Locked;
            if (_overlay != null)
            {
                _overlay.Apply(_settings, RenderText());
                _overlay.UpdateClickThrough();
            }
            SaveSettings();
            UpdateTray();
            Log.Info("叠层锁定=" + _settings.Overlay.Locked);
        }

        public void ShowSettings()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow(_settings, ApplySettings, ApplyPosition, ClearKeys);
            }
            else
            {
                _settingsWindow.Reload(_settings);
            }

            _settingsWindow.Show();
            _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
            Log.Info("设置窗口已打开");
        }

        void ClearKeys()
        {
            _keys.Clear();
            RenderNow();
        }

        public void ApplySettings(Settings settings)
        {
            FinishIntro();
            _settings = settings;
            _settings.Normalize();
            ConfigStore.Save(_settings);

            _keys.Configure(_settings.Data.KeyHistoryCount, _settings.Data.RepeatThreshold,
                            _settings.Data.MaxKeyChars);

            if (_overlay != null)
            {
                _overlay.Apply(_settings, RenderText());
                _overlay.UpdateClickThrough();
                _overlay.Topmost = _settings.Overlay.Topmost;
                _overlay.ApplyPosition();
            }

            if (_engine != null) _engine.UpdateSettings(_settings);

            RegisterHotkeys();
            ApplyAutoStart();
            UpdateTray();
            RenderNow();
            Log.Info("设置已应用");
        }

        void ApplyPosition(double x, double y, int monitor)
        {
            _settings.Overlay.X = x;
            _settings.Overlay.Y = y;
            _settings.Overlay.Monitor = monitor;

            if (_overlay != null)
            {
                _overlay.ApplyPosition();
                _overlay.Show();
                _overlayVisible = true;
            }

            SaveSettings();
        }

        void SaveSettings()
        {
            ConfigStore.Save(_settings);
        }

        void ApplyAutoStart()
        {
            bool enabled = _settings.Behavior.AutoStart;
            if (AutoStart.IsEnabled() != enabled)
                AutoStart.Set(enabled, _settings.Behavior.StartMinimized);
            else if (enabled)
                AutoStart.Set(true, _settings.Behavior.StartMinimized);
        }

        void ToggleAutoStart()
        {
            _settings.Behavior.AutoStart = !_settings.Behavior.AutoStart;
            ApplyAutoStart();
            SaveSettings();
            UpdateTray();
        }

        void RegisterHotkeys()
        {
            if (_hotkeys == null) return;

            var changed = new List<string>(3);

            string toggle = RegisterOne(HotkeyManager.ActionToggleOverlay, _settings.Hotkeys.ToggleOverlay);
            string open = RegisterOne(HotkeyManager.ActionOpenSettings, _settings.Hotkeys.OpenSettings);
            string lockKey = RegisterOne(HotkeyManager.ActionToggleLock, _settings.Hotkeys.ToggleLock);

            if (toggle != _settings.Hotkeys.ToggleOverlay)
            {
                changed.Add(_settings.Hotkeys.ToggleOverlay + " → " + toggle);
                _settings.Hotkeys.ToggleOverlay = toggle;
            }
            if (open != _settings.Hotkeys.OpenSettings)
            {
                changed.Add(_settings.Hotkeys.OpenSettings + " → " + open);
                _settings.Hotkeys.OpenSettings = open;
            }
            if (lockKey != _settings.Hotkeys.ToggleLock)
            {
                changed.Add(_settings.Hotkeys.ToggleLock + " → " + lockKey);
                _settings.Hotkeys.ToggleLock = lockKey;
            }

            if (changed.Count > 0)
            {
                Log.Warn("部分热键被占用，已自动改用：" + string.Join("，", changed));
                ConfigStore.Save(_settings);
                if (_settingsWindow != null) _settingsWindow.Reload(_settings);
                if (_tray != null)
                    _tray.ShowBalloon("热键被占用，已自动改用", string.Join("  ", changed));
            }
        }

        /// <summary>优先注册配置的组合键；被占用时退一步用 Ctrl+Alt+原键。</summary>
        string RegisterOne(string action, string combination)
        {
            if (_hotkeys.Register(action, combination))
            {
                Log.Info("热键 " + combination + " 注册成功");
                return combination;
            }

            if (combination.IndexOf('+') < 0)
            {
                string fallback = "Ctrl+Alt+" + combination;
                if (_hotkeys.Register(action, fallback))
                {
                    Log.Warn("热键 " + combination + " 被占用，改用 " + fallback);
                    return fallback;
                }
            }

            Log.Warn("热键注册失败，功能仍可通过托盘菜单使用: " + combination);
            return combination;
        }

        void OnHotkey(string action)
        {
            switch (action)
            {
                case HotkeyManager.ActionToggleOverlay:
                    ToggleVisibility();
                    break;
                case HotkeyManager.ActionOpenSettings:
                    ShowSettings();
                    break;
                case HotkeyManager.ActionToggleLock:
                    ToggleLock();
                    break;
            }
        }

        void UpdateTray()
        {
            if (_tray == null) return;
            _tray.Visible = _settings.Behavior.TrayEnabled;
            _tray.Update(_overlayVisible, _settings.Overlay.Locked, _settings.Behavior.AutoStart);
        }

        public void Exit()
        {
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _renderTimer?.Stop(); } catch { }
            try { _introTimer?.Stop(); } catch { }

            try { _activation?.Set(); } catch { }
            try { _activation?.Dispose(); } catch { }

            try { _engine?.Dispose(); } catch { }
            try { _hook?.Dispose(); } catch { }
            try { _hotkeys?.Dispose(); } catch { }
            try { _tray?.Dispose(); } catch { }

            try
            {
                if (_settingsWindow != null) _settingsWindow.CloseForReal();
            }
            catch { }

            try
            {
                if (_overlay != null) _overlay.Close();
            }
            catch { }
        }
    }
}
