using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 把配置、采样、卡片窗口、托盘、热键串起来。
    /// 卡片位置不再逐张保存：所有可见卡片从配置的锚点开始共享一条垂直堆叠，
    /// 每张卡片只负责自己内容与「与上一张的间距」。
    /// </summary>
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

        readonly List<CommandEntry> _commandEntries = new List<CommandEntry>();

        sealed class CommandEntry
        {
            public string Prompt;
            public string Command;
            public string Output;
            public bool Done;
        }

        sealed class CardSlot
        {
            public CardSettings Config;
            public ICardModule Module;
            public Settings Dialog;
            public OverlayWindow Window;
            public bool Summoned;
            public bool Shown;
            public double BodyHeight;
        }

        Settings _settings;
        MetricEngine _engine;
        KeyboardHook _hook;
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
        bool _layoutScheduled;

        readonly List<CardSlot> _cards = new List<CardSlot>();

        CardSlot _commandCard;
        bool _commandMode;
        bool _commandRunning;
        string _commandInput = "";
        int _commandCaret;
        CancellationTokenSource _commandCancel;

        CardSlot _introCard;
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

            bool autoStarted = _args.Contains("--autostart");
            bool forcedVisible = _args.Contains("--visible");
            bool minimized = !forcedVisible &&
                             ((!autoStarted && _settings.Behavior.StartMinimized) || _args.Contains("--minimized"));
            bool intro = _settings.Behavior.IntroEnabled && !minimized;

            CreateCards();
            _overlayVisible = !minimized;

            RefreshVisibility();
            UpdateCommandInfo();

            if (minimized)
                _phase = IntroPhase.Live;
            else if (intro)
                BeginIntro();

            Log.Info("卡片已创建，数量=" + _cards.Count + "，可见=" + _overlayVisible +
                     "，置顶=" + _settings.Overlay.Topmost + "，锁定=" + _settings.Overlay.Locked);

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

            Log.Info("启动完成（叠层可见=" + _overlayVisible +
                     "，全局热键=" + _settings.Hotkeys.ToggleOverlay + "/" + _settings.Hotkeys.OpenSettings +
                     "/" + _settings.Hotkeys.ToggleLock + "）");

            ShowStartupBalloon();
        }

        // ==================== 卡片 ====================

        void CreateCards()
        {
            _cards.Clear();
            _commandCard = null;
            _introCard = null;

            foreach (CardSettings config in _settings.Cards)
                _cards.Add(CreateSlot(config));
        }

        CardSlot CreateSlot(CardSettings config)
        {
            Settings dialog = BuildDialog(config);
            var slot = new CardSlot
            {
                Config = config,
                Module = CardRegistry.Get(config.Kind),
                Dialog = dialog
            };

            var window = new OverlayWindow(dialog);
            slot.Window = window;

            window.SettingsRequested += ShowSettings;
            window.HideRequested += ToggleVisibility;
            window.LockToggleRequested += ToggleLock;
            window.ExitRequested += Exit;
            window.Moved += (dx, dy, monitor) => OnCardMoved(slot, dx, dy, monitor);
            window.CommandLineRequested += () => ToggleCommandLine();
            window.CommandSubmitted += SubmitCommand;
            window.CommandCancelled += ExitCommandLine;
            window.CommandTextChanged += OnCommandInputChanged;
            window.SizeChanged += (s, e) => ScheduleLayout();

            return slot;
        }

        Settings BuildDialog(CardSettings config)
        {
            Settings dialog = CloneSettings(_settings);
            dialog.Overlay.X = _settings.Overlay.X;
            dialog.Overlay.Y = _settings.Overlay.Y;
            dialog.Overlay.Monitor = _settings.Overlay.Monitor;
            dialog.Overlay.Locked = _settings.Overlay.Locked;
            return dialog;
        }

        static Settings CloneSettings(Settings source)
        {
            try { return Json.Deserialize<Settings>(Json.Serialize(source)); }
            catch { return new Settings(); }
        }

        string CardTemplate(CardSlot slot)
        {
            string content = slot.Config == null ? null : slot.Config.Content;
            return string.IsNullOrWhiteSpace(content) ? slot.Module.DefaultContent : content;
        }

        string RenderCardText(CardSlot slot)
        {
            if (slot == null || slot.Module.IsInteractive) return "";

            try
            {
                return slot.Module.Render(new CardRenderContext(_settings, _last, _keys, slot.Config));
            }
            catch (Exception ex)
            {
                Log.Warn("卡片渲染失败(" + slot.Config.Name + "): " + ex.Message);
                return "(渲染失败)";
            }
        }

        // ==================== 显示 / 隐藏 / 堆叠 ====================

        void RefreshVisibility()
        {
            foreach (CardSlot slot in _cards)
            {
                bool show;
                if (!_overlayVisible) show = false;
                else if (slot.Module.IsInteractive) show = _commandMode && ReferenceEquals(slot, _commandCard);
                else show = slot.Config.Resident || slot.Summoned;

                if (show && !slot.Shown)
                    ShowCard(slot, RenderCardText(slot));
                else if (!show && slot.Shown)
                    HideCard(slot);
            }

            LayoutCards();
        }

        void ShowCard(CardSlot slot, string text)
        {
            if (slot == null) return;

            slot.Window.Apply(slot.Dialog, text ?? "");
            slot.Window.Opacity = 0;
            slot.Window.Show();
            slot.Window.UpdateLayout();

            slot.Shown = true;
            slot.BodyHeight = slot.Window.CardHeight;
            LayoutCards();

            slot.Window.Opacity = 1;
            slot.Window.UpdateClickThrough();
            slot.Window.EnsureTopmost();
        }

        void HideCard(CardSlot slot)
        {
            if (slot == null || !slot.Shown) return;
            slot.Shown = false;
            slot.BodyHeight = 0;
            slot.Window.Hide();
            LayoutCards();
        }

        void SyncHeight(CardSlot slot)
        {
            if (slot == null) return;
            slot.BodyHeight = slot.Shown ? slot.Window.CardHeight : 0;
        }

        void ScheduleLayout()
        {
            if (_layoutScheduled || _disposed) return;
            _layoutScheduled = true;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _layoutScheduled = false;
                if (_disposed) return;
                foreach (CardSlot slot in _cards) SyncHeight(slot);
                LayoutCards();
            }), DispatcherPriority.Render);
        }

        /// <summary>从锚点开始按顺序把可见卡片往下排，间距取每张卡片自己的 Spacing。</summary>
        void LayoutCards()
        {
            double top = 0;
            bool first = true;

            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown) continue;

                if (!first) top += Math.Max(0, slot.Config.Spacing);

                slot.Dialog.Overlay.X = _settings.Overlay.X;
                slot.Dialog.Overlay.Y = _settings.Overlay.Y + top;
                slot.Dialog.Overlay.Monitor = _settings.Overlay.Monitor;
                slot.Dialog.Overlay.Locked = _settings.Overlay.Locked;
                slot.Window.ApplyPosition();

                top += slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight;
                first = false;
            }
        }

        void OnCardMoved(CardSlot slot, double dx, double dy, int monitor)
        {
            if (slot == null) return;

            _settings.Overlay.X += dx;
            _settings.Overlay.Y += dy;
            _settings.Overlay.Monitor = monitor;

            LayoutCards();
            SaveSettings();

            if (_settingsWindow != null)
                _settingsWindow.SyncAnchor(_settings.Overlay.X, _settings.Overlay.Y, _settings.Overlay.Monitor);

            Log.Info("叠层移动，锚点=" + _settings.Overlay.X + "," + _settings.Overlay.Y + " 显示器=" + monitor);
        }

        void UpdateCommandInfo()
        {
            CardSlot command = FindCommandCard();
            string hotkey = command == null || command.Config == null ? "" : command.Config.Hotkey;

            foreach (CardSlot slot in _cards)
                slot.Window.SetCommandInfo(command != null, hotkey);
        }

        CardSlot FindCommandCard()
        {
            foreach (CardSlot slot in _cards)
                if (slot.Module != null && slot.Module.IsInteractive) return slot;
            return null;
        }

        CardSlot FindCard(string id)
        {
            foreach (CardSlot slot in _cards)
                if (slot.Config != null && string.Equals(slot.Config.Id, id, StringComparison.OrdinalIgnoreCase))
                    return slot;
            return null;
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
            _tray.CommandLine += ToggleCommandLine;
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
                _tray.ShowBalloon("镜潭一览已启动",
                    "左键单击托盘图标，或按 " + _settings.Hotkeys.OpenSettings + " 打开设置");
            };
            timer.Start();
        }

        void StartActivationListener()
        {
            _activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\MirrorpondHUD.Activate");
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
                Name = "MirrorpondHUD.Activate"
            };
            _activationThread.Start();
            Log.Info("单实例唤醒监听已启动");
        }

        // ==================== 渲染 ====================

        void RenderNow()
        {
            if (!_overlayVisible) return;
            if (_commandMode)
            {
                RenderCommandLine();
                return;
            }

            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown || slot.Module.IsInteractive) continue;
                if (ReferenceEquals(slot, _introCard) && _phase != IntroPhase.Live) continue;
                slot.Window.SetText(RenderCardText(slot), false);
            }

            LayoutIfNeeded();
        }

        /// <summary>
        /// 文字变长 / 命令行展开后窗口会自己长高，这里在每次渲染后校正一次堆叠位置，
        /// 不依赖 Window.SizeChanged 一定按时触发。
        /// </summary>
        void LayoutIfNeeded()
        {
            bool changed = false;
            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown) continue;
                double height = slot.Window.CardHeight;
                if (Math.Abs(height - slot.BodyHeight) > 0.5)
                {
                    slot.BodyHeight = height;
                    changed = true;
                }
            }

            if (changed) LayoutCards();
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

        // ==================== 快捷命令行 ====================

        public void ToggleCommandLine()
        {
            ToggleCommandLine(FindCommandCard());
        }

        void ToggleCommandLine(CardSlot slot)
        {
            if (slot == null || slot.Module == null || !slot.Module.IsInteractive)
            {
                Log.Info("没有可用的命令行卡片");
                return;
            }

            if (_commandMode && ReferenceEquals(_commandCard, slot))
            {
                ExitCommandLine();
                return;
            }

            if (_commandMode) ExitCommandLine();
            if (_phase != IntroPhase.Live) FinishIntro();

            if (!_overlayVisible)
            {
                _overlayVisible = true;
                RefreshVisibility();
            }

            EnterCommandLine(slot);
        }

        void EnterCommandLine(CardSlot slot)
        {
            _commandMode = true;
            _commandCard = slot;
            _commandEntries.Clear();
            _commandInput = "";
            _commandCaret = 0;

            if (_hook != null) _hook.Suppress = true;

            slot.Summoned = true;
            slot.Window.ResetStableSize();
            ShowCard(slot, "");
            slot.Window.EnsureTopmost();
            slot.Window.BeginCommandInput();
            RenderCommandLine();

            Log.Info("快捷命令行已打开（工具=" + CommandRunner.ToolLabel(_settings.Command) +
                     "，目录=" + CommandRunner.ResolveWorkingDirectory(_settings.Command) + "）");
        }

        public void ExitCommandLine()
        {
            if (!_commandMode) return;

            _commandMode = false;
            _commandRunning = false;

            try { _commandCancel?.Cancel(); } catch { }
            try { _commandCancel?.Dispose(); } catch { }
            _commandCancel = null;

            _commandEntries.Clear();
            _commandInput = "";
            _commandCaret = 0;

            if (_hook != null) _hook.Suppress = false;

            CardSlot slot = _commandCard;
            _commandCard = null;

            if (slot != null)
            {
                slot.Window.EndCommandInput();
                slot.Window.ResetStableSize();
                slot.Summoned = false;
                HideCard(slot);
            }

            RenderNow();
            Log.Info("快捷命令行已关闭，命令与返回已清空");
        }

        void OnCommandInputChanged(string text, int caret)
        {
            if (!_commandMode) return;
            _commandInput = text ?? "";
            _commandCaret = caret;
            RenderCommandLine();
        }

        string CommandPrompt
        {
            get { return CommandRunner.BuildPrompt(_settings.Command); }
        }

        string BuildCommandDisplay()
        {
            string prompt = CommandPrompt;
            var sb = new System.Text.StringBuilder(512);

            foreach (CommandEntry entry in _commandEntries)
            {
                sb.Append(entry.Prompt).Append('\n');
                sb.Append(entry.Command).Append('\n');

                if (!entry.Done) sb.Append("(运行中…)").Append('\n');
                else if (!string.IsNullOrEmpty(entry.Output)) sb.Append(entry.Output).Append('\n');

                sb.Append('\n');
            }

            sb.Append(prompt).Append('\n');
            sb.Append(_commandInput ?? "");
            return sb.ToString();
        }

        void RenderCommandLine()
        {
            if (!_commandMode || _commandCard == null) return;

            string text = BuildCommandDisplay();
            int caret = Math.Max(0, Math.Min(_commandCaret, (_commandInput ?? "").Length));
            int cursorIndex = text.Length - ((_commandInput ?? "").Length - caret);
            _commandCard.Window.SetText(text, true, cursorIndex);
            LayoutIfNeeded();
        }

        void SubmitCommand()
        {
            if (!_commandMode) return;

            string command = (_commandInput ?? "").Trim();
            if (command.Length == 0) return;

            var entry = new CommandEntry
            {
                Prompt = CommandPrompt,
                Command = command,
                Done = false
            };
            _commandEntries.Add(entry);

            _commandInput = "";
            _commandCaret = 0;
            _commandCard.Window.SetCommandText("");
            TrimCommandEntries();
            RenderCommandLine();

            if (_commandRunning)
            {
                entry.Done = true;
                entry.Output = "(上一条命令仍在执行，请稍候)";
                RenderCommandLine();
                return;
            }

            _commandRunning = true;
            _commandCancel = new CancellationTokenSource();
            CancellationToken token = _commandCancel.Token;
            Settings snapshot = _settings;

            Task.Run(() =>
            {
                CommandResult result = CommandRunner.Run(snapshot, command, token);

                try
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        entry.Output = CommandRunner.FormatOutput(result, snapshot.Command);
                        entry.Done = true;
                        _commandRunning = false;

                        try { _commandCancel?.Dispose(); } catch { }
                        _commandCancel = null;

                        if (!_commandMode) return;
                        TrimCommandEntries();
                        RenderCommandLine();
                    }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    Log.Warn("命令行结果刷新失败: " + ex.Message);
                }
            });
        }

        void TrimCommandEntries()
        {
            int max = Math.Max(1, _settings.Command.MaxLines);
            int total = 0;
            foreach (CommandEntry entry in _commandEntries) total += CountEntryLines(entry);

            while (_commandEntries.Count > 1 && total > max)
            {
                CommandEntry first = _commandEntries[0];
                if (!first.Done) break;
                total -= CountEntryLines(first);
                _commandEntries.RemoveAt(0);
            }
        }

        static int CountEntryLines(CommandEntry entry)
        {
            int lines = 3; // 提示符 + 命令 + 空行
            if (!entry.Done) return lines + 1;
            if (string.IsNullOrEmpty(entry.Output)) return lines;

            int count = 1;
            for (int i = 0; i < entry.Output.Length; i++)
                if (entry.Output[i] == '\n') count++;
            return lines + count;
        }

        // ==================== 启动动画 ====================

        CardSlot FirstLiveCard()
        {
            foreach (CardSlot slot in _cards)
                if (slot.Shown && !slot.Module.IsInteractive) return slot;
            return null;
        }

        void BeginIntro()
        {
            _introCard = FirstLiveCard();
            if (_introCard == null || !_settings.Behavior.IntroEnabled)
            {
                _phase = IntroPhase.Live;
                RenderNow();
                return;
            }

            _introWelcome = string.IsNullOrEmpty(_settings.Behavior.WelcomeText)
                ? "Welcome Back"
                : _settings.Behavior.WelcomeText;
            _introContent = TemplateEngine.RenderPlaceholder(CardTemplate(_introCard), "---");
            _shownText = "";

            // 先用完整内容把整块尺寸定下来，逐字打字时窗口和文本位置都不会抖动
            _introCard.Window.SetLayoutText(_introContent);
            _introCard.Window.SetText("", true);
            SyncHeight(_introCard);

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
            if (text == _shownText || _introCard == null) return;
            _shownText = text;
            _introCard.Window.SetText(text, true);
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

            if (_introCard != null)
            {
                _introCard.Window.SetLayoutText(null);
                _introCard.Window.SetText(RenderCardText(_introCard), false);
                SyncHeight(_introCard);
            }

            _introCard = null;
            LayoutCards();

            Log.Info("启动动画结束，光标消失，开始刷新数据");
            RenderNow();
        }

        void UpdateIntroLayout()
        {
            if (_phase == IntroPhase.Live || _introCard == null) return;
            _introCard.Window.SetLayoutText(RenderCardText(_introCard));
            SyncHeight(_introCard);
            LayoutCards();
        }

        // ==================== 操作 ====================

        public void ToggleVisibility()
        {
            if (_cards.Count == 0) return;

            if (_overlayVisible)
            {
                if (_commandMode) ExitCommandLine();
                if (_phase != IntroPhase.Live) FinishIntro();
                _overlayVisible = false;
                foreach (CardSlot slot in _cards) HideCard(slot);
            }
            else
            {
                _overlayVisible = true;
                RefreshVisibility();
                RenderNow();
            }

            Log.Info("叠层可见=" + _overlayVisible);
            UpdateTray();
        }

        public void ToggleLock()
        {
            if (_commandMode) ExitCommandLine();
            SetLocked(!_settings.Overlay.Locked);
        }

        /// <summary>设置叠层是否锁定（锁定 = 鼠标穿透）。</summary>
        void SetLocked(bool locked)
        {
            bool changed = _settings.Overlay.Locked != locked;
            _settings.Overlay.Locked = locked;

            foreach (CardSlot slot in _cards)
            {
                slot.Dialog.Overlay.Locked = locked;
                slot.Window.RefreshSettings(slot.Dialog);
            }

            if (changed)
            {
                SaveSettings();
                UpdateTray();
                Log.Info("叠层锁定=" + locked);
            }
        }

        public void ShowSettings()
        {
            if (_commandMode) ExitCommandLine();

            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow(_settings, ApplySettings, ApplyAnchor, ClearKeys);
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
            if (_commandMode) ExitCommandLine();

            // 位置与锁定是运行时状态：只由拖动、F8 和托盘菜单改变。
            // 设置窗口里的坐标是打开窗口那一刻的快照，这里必须以实时值为准。
            settings.Overlay.X = _settings.Overlay.X;
            settings.Overlay.Y = _settings.Overlay.Y;
            settings.Overlay.Monitor = _settings.Overlay.Monitor;
            settings.Overlay.Locked = _settings.Overlay.Locked;

            FinishIntro();

            var summoned = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (CardSlot slot in _cards) summoned[slot.Config.Id] = slot.Summoned;

            _settings = settings;
            _settings.Normalize();
            CardModel.Normalize(_settings);

            SyncCards(summoned);

            _keys.Configure(_settings.Data.KeyHistoryCount, _settings.Data.RepeatThreshold,
                            _settings.Data.MaxKeyChars);

            ConfigStore.Save(_settings);
            if (_engine != null) _engine.UpdateSettings(_settings);

            RegisterHotkeys();
            ApplyAutoStart();
            UpdateTray();
            UpdateCommandInfo();
            RefreshVisibility();
            RenderNow();

            Log.Info("设置已应用（卡片 " + _cards.Count + " 张）");
        }

        /// <summary>按新设置增删 / 重排卡片窗口，保留仍在列表里的窗口和它们的呼出状态。</summary>
        void SyncCards(Dictionary<string, bool> summoned)
        {
            var existing = new Dictionary<string, CardSlot>(StringComparer.OrdinalIgnoreCase);
            foreach (CardSlot slot in _cards) existing[slot.Config.Id] = slot;

            var next = new List<CardSlot>();

            foreach (CardSettings config in _settings.Cards)
            {
                CardSlot slot;
                if (existing.TryGetValue(config.Id, out slot))
                {
                    existing.Remove(config.Id);

                    bool wasSummoned = slot.Summoned;
                    bool saved;
                    slot.Config = config;
                    slot.Module = CardRegistry.Get(config.Kind);
                    slot.Summoned = summoned.TryGetValue(config.Id, out saved) ? saved : wasSummoned;
                    slot.Dialog = BuildDialog(config);
                    slot.Window.Apply(slot.Dialog, "");

                    next.Add(slot);
                }
                else
                {
                    next.Add(CreateSlot(config));
                }
            }

            foreach (CardSlot removed in existing.Values)
            {
                if (ReferenceEquals(removed, _commandCard))
                {
                    _commandMode = false;
                    _commandCard = null;
                }

                try { removed.Window.Close(); } catch { }
            }

            _cards.Clear();
            _cards.AddRange(next);
        }

        void ApplyAnchor(double x, double y, int monitor)
        {
            _settings.Overlay.X = x;
            _settings.Overlay.Y = y;
            _settings.Overlay.Monitor = monitor;

            if (!_overlayVisible)
            {
                _overlayVisible = true;
                RefreshVisibility();
            }
            else
            {
                LayoutCards();
            }

            SaveSettings();

            if (_settingsWindow != null)
                _settingsWindow.SyncAnchor(x, y, monitor);
        }

        void SaveSettings()
        {
            ConfigStore.Save(_settings);
        }

        void ApplyAutoStart()
        {
            AutoStart.CleanLegacy();

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

        // ==================== 热键 ====================

        void RegisterHotkeys()
        {
            if (_hotkeys == null) return;

            // 先清空：设置里删掉的卡片不应该继续占用旧组合键。
            _hotkeys.UnregisterAll();

            var changed = new List<string>(4);

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

            // 每张非常驻卡片一个呼出热键；相同组合键会合并成同一个注册，按下时同时呼出。
            int failed = 0;
            foreach (CardSlot slot in _cards)
            {
                string action = HotkeyManager.CardAction(slot.Config.Id);
                string combination = slot.Config.Resident ? "" : slot.Config.Hotkey;

                if (string.IsNullOrWhiteSpace(combination))
                {
                    _hotkeys.Register(action, "");
                    continue;
                }

                if (!_hotkeys.Register(action, combination))
                {
                    failed++;
                    Log.Warn("卡片热键注册失败: " + slot.Config.Name + " " + combination);
                }
            }

            if (failed > 0 && _tray != null && _tray.Visible)
            {
                _tray.ShowBalloon("有 " + failed + " 张卡片的呼出热键注册失败",
                    "组合键可能被其它程序占用，可在「内容」页换一个");
            }
        }

        /// <summary>优先注册配置的组合键；被占用时退一步用 Ctrl+Alt+原键。空白 = 不启用。</summary>
        string RegisterOne(string action, string combination)
        {
            if (string.IsNullOrWhiteSpace(combination))
            {
                _hotkeys.Register(action, "");
                return "";
            }

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
                default:
                    if (action.StartsWith("card:", StringComparison.Ordinal))
                    {
                        CardSlot slot = FindCard(action.Substring(5));
                        if (slot != null) ToggleCard(slot);
                    }
                    break;
            }
        }

        /// <summary>非常驻卡片的呼出 / 收起。</summary>
        void ToggleCard(CardSlot slot)
        {
            if (slot == null) return;

            if (slot.Module.IsInteractive)
            {
                ToggleCommandLine(slot);
                return;
            }

            if (!_overlayVisible)
            {
                _overlayVisible = true;
                RefreshVisibility();
            }

            slot.Summoned = !slot.Summoned;

            if (slot.Summoned)
            {
                ShowCard(slot, RenderCardText(slot));
                slot.Window.EnsureTopmost();
            }
            else
            {
                HideCard(slot);
            }

            Log.Info("卡片「" + slot.Config.Name + "」" + (slot.Summoned ? "已呼出" : "已收起"));
            UpdateTray();
        }

        void UpdateTray()
        {
            if (_tray == null) return;
            _tray.Visible = _settings.Behavior.TrayEnabled;

            CardSlot command = FindCommandCard();
            _tray.SetCommandHotkey(command == null ? "" : command.Config.Hotkey);
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
            try { _commandCancel?.Cancel(); } catch { }
            try { _commandCancel?.Dispose(); } catch { }

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

            foreach (CardSlot slot in _cards)
            {
                try { slot.Window.Close(); } catch { }
            }
            _cards.Clear();
        }
    }
}
