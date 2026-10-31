using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

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

        sealed class CardSlot
        {
            public CardSettings Config;
            public ICardModule Module;
            /// <summary>模块同时是富内容卡片时（图片 / 视频 / CLI 面板）非空。</summary>
            public IRichCardModule Rich;
            public Settings Dialog;
            public OverlayWindow Window;
            public bool Summoned;
            public bool Shown;
            public double BodyHeight;

            /// <summary>富内容控件的当前实例（隐藏时保留，设置变化时重建）。</summary>
            public FrameworkElement Content;

            /// <summary>
            /// 建 / 刷新这个内容时用的**内容**签名（种类、模板、本卡设置）。
            /// 应用设置时拿它和新签名比较：一样就只刷新数据，不一样才重建控件。
            /// </summary>
            public string ContentSignature;

            /// <summary>**样式**签名（外观 / CLI 尺寸 / 圆角 / 投影）；只变它时优先就地换肤。</summary>
            public string StyleSignature;

            /// <summary>这张卡片拿到的宿主服务（状态目录、请求重画 / 布局）。</summary>
            public CardHost Host;

            /// <summary>上次真正重画的时间；和模块的刷新间隔配合做节流。</summary>
            public DateTime LastRenderUtc;
        }

        /// <summary>
        /// 每张卡片一份的宿主服务实现：把状态读写落到 <c>cards\&lt;id&gt;\</c>，
        /// 把「重画 / 重新布局」转给控制器。模块是单例，所以这只是下发对象，不长期持有。
        /// </summary>
        internal sealed class CardHost : ICardHost
        {
            readonly OverlayController _owner;

            public CardHost(OverlayController owner, CardSettings config)
            {
                _owner = owner;
                Config = config;
            }

            /// <summary>卡片配置；应用设置时由控制器换成新对象。</summary>
            public CardSettings Config { get; set; }

            public string StateDirectory
            {
                get { return CardStateStore.DirectoryFor(CardId); }
            }

            public string ReadState(string fileName)
            {
                return CardStateStore.Read(CardId, fileName);
            }

            public bool WriteState(string fileName, string text)
            {
                return CardStateStore.Write(CardId, fileName, text);
            }

            public bool DeleteState(string fileName)
            {
                return CardStateStore.Delete(CardId, fileName);
            }

            public void RequestRender()
            {
                _owner.RequestRenderFromModule();
            }

            public void RequestLayout()
            {
                _owner.RequestLayoutFromModule();
            }

            string CardId { get { return Config == null ? "" : Config.Id; } }
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
        bool _hasSnapshot;

        readonly List<CardSlot> _cards = new List<CardSlot>();

        CardSlot _commandCard;
        bool _commandMode;

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
            Settings dialog = BuildDialog();
            var slot = new CardSlot
            {
                Config = config,
                Module = CardRegistry.Get(config.Kind),
                Dialog = dialog,
                ContentSignature = ContentSignature(config),
                StyleSignature = StyleSignature()
            };
            slot.Rich = slot.Module as IRichCardModule;
            slot.Host = new CardHost(this, config);
            AttachHost(slot);

            var window = new OverlayWindow(dialog);
            // 带按钮 / 输入框的卡片（AI 对话、按钮演示）自己声明需要鼠标，宿主据此关掉这张卡的点击穿透。
            window.Interactive = slot.Rich != null && slot.Rich.NeedsMouseInput;
            slot.Window = window;

            WireInputCapabilities(slot);

            window.SettingsRequested += ShowSettings;
            window.HideRequested += ToggleVisibility;
            window.LockToggleRequested += ToggleLock;
            window.ExitRequested += Exit;
            window.Moved += (dx, dy, monitor) => OnCardMoved(slot, dx, dy, monitor);
            window.CommandLineRequested += () => ToggleCommandLine();
            window.SizeChanged += (s, e) => ScheduleLayout();
            window.RaisedTopmost += () =>
            {
                if (slot.Rich == null || slot.Content == null) return;
                try { slot.Rich.OnZOrderChanged(slot.Content); } catch { }
            };

            return slot;
        }

        /// <summary>
        /// 按卡片能力接线：内容自己处理拖动（<see cref="ICardModule.HandlesOwnDrag"/>）时关掉整卡拖动；
        /// 实现 <see cref="ICardDropTarget"/> 的卡片由宿主统一注册 AllowDrop / DragOver / Drop。
        /// </summary>
        void WireInputCapabilities(CardSlot slot)
        {
            if (slot == null || slot.Window == null) return;

            slot.Window.DragEnabled = slot.Module == null || !slot.Module.HandlesOwnDrag;

            var feedback = slot.Module as ICardDropFeedback;
            Action<bool, IDataObject> dropState = null;
            if (feedback != null)
            {
                dropState = (over, data) =>
                {
                    try { feedback.OnDropStateChanged(over, data); }
                    catch (Exception ex) { Log.Warn("卡片拖放状态处理失败: " + ex.Message); }
                };
            }

            var drop = slot.Module as ICardDropTarget;
            if (drop == null)
            {
                slot.Window.EnableDrop(null, null, dropState);
                return;
            }

            CardSlot captured = slot;
            slot.Window.EnableDrop(
                data =>
                {
                    try { return drop.CanDrop(data); }
                    catch (Exception ex) { Log.Warn("卡片拖放判定失败: " + ex.Message); return false; }
                },
                data =>
                {
                    try { drop.Drop(data, captured.Content); }
                    catch (Exception ex) { Log.Warn("卡片拖放处理失败: " + ex.Message); }
                },
                dropState);
        }

        /// <summary>让实现 <see cref="ICardHostAware"/> 的模块拿到这张卡片的宿主服务。</summary>
        void AttachHost(CardSlot slot)
        {
            if (slot == null || slot.Host == null) return;

            var aware = slot.Module as ICardHostAware;
            if (aware == null) return;

            try { aware.Attach(slot.Host); }
            catch (Exception ex) { Log.Warn("卡片宿主服务接线失败(" + slot.Config.Name + "): " + ex.Message); }
        }

        /// <summary>卡片被删 / 程序退出 / 换模块时解绑。</summary>
        void DetachHost(CardSlot slot)
        {
            if (slot == null || slot.Host == null) return;

            var aware = slot.Module as ICardHostAware;
            if (aware == null) return;

            try { aware.Detach(slot.Host); }
            catch (Exception ex) { Log.Warn("卡片宿主服务解绑失败(" + slot.Config.Name + "): " + ex.Message); }
        }

        /// <summary>模块改了数据但采样快照没变：排一次重画。</summary>
        internal void RequestRenderFromModule()
        {
            if (_disposed || Application.Current == null) return;
            Application.Current.Dispatcher.BeginInvoke(new Action(RenderNow), DispatcherPriority.Background);
        }

        /// <summary>内容尺寸变了：排一次布局校正。</summary>
        internal void RequestLayoutFromModule()
        {
            if (_disposed) return;
            ScheduleLayout();
        }

        /// <summary>
        /// 每张卡片的窗口需要「外观 + 本卡位置」。这里浅拷贝一份 <see cref="Settings"/>：
        /// 外观 / CLI / 热键这些大对象共享同一个实例，只有 <see cref="OverlaySettings"/> 单独复制
        /// （窗口会改自己的 X / Y / 锁定状态，不能互相踩）。
        ///
        /// 旧实现在这里对整份配置做 JSON 深拷贝，卡片越多副本越多；而且克隆绕过 Normalize，
        /// 两份配置容易再次漂移。
        /// </summary>
        Settings BuildDialog()
        {
            return new Settings
            {
                SchemaVersion = _settings.SchemaVersion,
                Overlay = CopyOverlay(_settings.Overlay),
                Style = _settings.Style,
                Cli = _settings.Cli,
                Data = _settings.Data,
                Behavior = _settings.Behavior,
                Hotkeys = _settings.Hotkeys,
                Cards = _settings.Cards
            };
        }

        static OverlaySettings CopyOverlay(OverlaySettings source)
        {
            if (source == null) return new OverlaySettings();
            return new OverlaySettings
            {
                X = source.X,
                Y = source.Y,
                Monitor = source.Monitor,
                Locked = source.Locked,
                Topmost = source.Topmost,
                CornerRadius = source.CornerRadius,
                Shadow = source.Shadow
            };
        }

        /// <summary>
        /// 内容维度：种类、模板、本卡自己的设置（Extras）。变了才重建控件。
        /// 序列化成字符串快照存进 <see cref="CardSlot.ContentSignature"/>——设置页在副本上改、
        /// 点「应用」时才换成新对象，旧值只能靠快照记住。
        /// </summary>
        sealed class ContentInputs
        {
            public string Kind;
            public string Content;
            public Dictionary<string, object> Extras;
        }

        /// <summary>
        /// 样式维度：所有卡片共用的外观、CLI 尺寸、圆角与投影。
        /// 只有样式变化时走 <see cref="ICardStyleAware.ApplyStyle"/>（支持就地换肤就不重建），
        /// 不支持的模块仍回退到重建。
        /// </summary>
        sealed class StyleInputs
        {
            public StyleSettings Style;
            public CliSettings Cli;
            public double CornerRadius;
            public bool Shadow;
        }

        string ContentSignature(CardSettings config)
        {
            if (config == null) return "";

            var inputs = new ContentInputs
            {
                Kind = config.Kind,
                Content = config.Content,
                Extras = config.Extras
            };

            try { return Json.Serialize(inputs); }
            catch { return null; }   // 序列化失败：当作「变了」，走重建，宁可贵一点也不要留下陈旧内容
        }

        string StyleSignature()
        {
            var inputs = new StyleInputs
            {
                Style = _settings.Style,
                Cli = _settings.Cli,
                CornerRadius = _settings.Overlay.CornerRadius,
                Shadow = _settings.Overlay.Shadow
            };

            try { return Json.Serialize(inputs); }
            catch { return null; }
        }

        string CardTemplate(CardSlot slot)
        {
            string content = slot.Config == null ? null : slot.Config.Content;
            return string.IsNullOrWhiteSpace(content) ? slot.Module.DefaultContent : content;
        }

        string RenderCardText(CardSlot slot)
        {
            return RenderCardText(slot, null);
        }

        /// <summary>用一帧里已经建好的变量表渲染（<paramref name="sharedVariables"/> 为 null 时自己建一份）。</summary>
        string RenderCardText(CardSlot slot, IDictionary<string, object> sharedVariables)
        {
            if (slot == null || slot.Module.IsInteractive) return "";

            try
            {
                return slot.Module.Render(
                    new CardRenderContext(_settings, _last, _keys, slot.Config, sharedVariables));
            }
            catch (Exception ex)
            {
                Log.Warn("卡片渲染失败(" + slot.Config.Name + "): " + ex.Message);
                return "(渲染失败)";
            }
        }

        /// <summary>首帧采样还没回来：所有占位符换成 ---，避免显示一堆假的 0。</summary>
        string RenderPlaceholderText(CardSlot slot)
        {
            if (slot == null || slot.Module.IsInteractive) return "";

            try
            {
                return TemplateEngine.RenderPlaceholder(CardTemplate(slot), "---");
            }
            catch
            {
                return "";
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
                    ShowCard(slot, _hasSnapshot ? RenderCardText(slot) : RenderPlaceholderText(slot));
                else if (!show && slot.Shown)
                    HideCard(slot);
            }

            LayoutCards();
        }

        void ShowCard(CardSlot slot, string text)
        {
            if (slot == null) return;

            // 刚显示要立刻画一帧，不要等模块的刷新间隔。
            slot.LastRenderUtc = DateTime.MinValue;

            if (slot.Rich != null)
            {
                EnsureContent(slot);
                slot.Window.ApplyRich(slot.Dialog, slot.Content);
            }
            else
            {
                slot.Window.Apply(slot.Dialog, text ?? "");
            }

            slot.Window.Opacity = 0;
            slot.Window.Show();
            slot.Window.UpdateLayout();

            slot.Shown = true;
            slot.BodyHeight = slot.Window.CardHeight;
            LayoutCards();

            slot.Window.Opacity = 1;
            slot.Window.UpdateClickThrough();
            slot.Window.EnsureTopmost();

            // 富内容要等卡片窗口真正显示、布局完成后再开始播放（视频子窗口要贴到正确位置）。
            if (slot.Rich != null && slot.Content != null)
            {
                try { slot.Rich.OnVisibilityChanged(slot.Content, true); } catch { }
            }
        }

        /// <summary>富内容卡片的控件按需创建；重建前释放旧控件（停掉视频 / 动图）。</summary>
        void EnsureContent(CardSlot slot)
        {
            if (slot == null || slot.Rich == null) return;
            if (slot.Content != null) return;

            try
            {
                slot.Content = slot.Rich.CreateContent(
                    new CardRenderContext(_settings, _last, _keys, slot.Config));
            }
            catch (Exception ex)
            {
                Log.Warn("卡片内容创建失败(" + slot.Config.Name + "): " + ex.Message);
                slot.Content = null;
            }
        }

        void ReleaseContent(CardSlot slot)
        {
            if (slot == null || slot.Content == null) return;

            try
            {
                if (slot.Rich != null) slot.Rich.OnVisibilityChanged(slot.Content, false);
            }
            catch { }

            try
            {
                var disposable = slot.Content as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
            catch { }

            slot.Content = null;
            try { slot.Window.ClearRichContent(); } catch { }
        }

        void HideCard(CardSlot slot)
        {
            if (slot == null || !slot.Shown) return;

            if (slot.Rich != null && slot.Content != null)
            {
                try { slot.Rich.OnVisibilityChanged(slot.Content, false); } catch { }
            }

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
            // 先量出整叠卡片里最宽的一张：右对齐时每张卡片的 X = 锚点 + (最宽 - 自己宽度)，
            // 这样所有卡片的右边缘落在同一条竖线上；左对齐时所有卡片共用锚点 X。
            double stackWidth = 0;
            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown) continue;
                stackWidth = Math.Max(stackWidth, slot.Window.CardWidth);
            }

            string align = ResolveCardAlign(stackWidth);
            double top = 0;
            bool first = true;

            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown) continue;

                if (!first) top += Math.Max(0, slot.Config.Spacing);

                double x = _settings.Overlay.X;
                if (align == "Right")
                    x += Math.Max(0, stackWidth - slot.Window.CardWidth);

                slot.Dialog.Overlay.X = x;
                slot.Dialog.Overlay.Y = _settings.Overlay.Y + top;
                slot.Dialog.Overlay.Monitor = _settings.Overlay.Monitor;
                slot.Dialog.Overlay.Locked = _settings.Overlay.Locked;
                slot.Window.ApplyPosition();

                top += slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight;
                first = false;
            }
        }

        /// <summary>左对齐 = 左边缘共线，右对齐 = 右边缘共线；自动按整叠卡片在屏幕左 / 右半边决定。</summary>
        string ResolveCardAlign(double stackWidth)
        {
            string mode = Settings.NormalizeCardAlign(_settings.Style.CardAlign);
            if (mode == "Left" || mode == "Right") return mode;

            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                if (screens.Length == 0) return "Left";

                int index = Math.Max(0, Math.Min(_settings.Overlay.Monitor, screens.Length - 1));
                System.Drawing.Rectangle work = screens[index].WorkingArea;

                double scale = 1.0;
                if (screens[0].Bounds.Width > 0)
                {
                    double candidate = System.Windows.SystemParameters.PrimaryScreenWidth / screens[0].Bounds.Width;
                    if (candidate > 0.1 && candidate < 10) scale = candidate;
                }

                double center = _settings.Overlay.X + stackWidth / 2;
                return center < work.Width * scale / 2 ? "Left" : "Right";
            }
            catch
            {
                return "Left";
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

            IDictionary<string, object> shared = null;
            DateTime now = DateTime.UtcNow;

            // 交互卡（命令行）自己管刷新，靠下面循环里的 IsInteractive 跳过；
            // 这里不能再按 _commandMode 整块返回，否则命令行一开，其它常驻卡片的文字就停更了。
            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown || slot.Module.IsInteractive) continue;
                if (slot.Rich != null) continue;   // 富内容（图片 / 视频）不需要逐帧刷新文字
                if (ReferenceEquals(slot, _introCard) && _phase != IntroPhase.Live) continue;

                // 模块可以声明「最快多久重画一次」：时钟不必按 8 帧/秒重算模板。
                int interval = 0;
                try { interval = slot.Module.RefreshIntervalMs(slot.Config); }
                catch { interval = 0; }

                if (_hasSnapshot && interval > 0 &&
                    (now - slot.LastRenderUtc).TotalMilliseconds < interval)
                    continue;

                if (shared == null) shared = BuildFrameVariables();

                // 首帧采样还没回来时显示占位符，不要把 "0.0%" 这种假数据显示给用户。
                slot.Window.SetText(_hasSnapshot ? RenderCardText(slot, shared) : RenderPlaceholderText(slot), false);
                slot.LastRenderUtc = now;
            }

            LayoutIfNeeded();
        }

        /// <summary>
        /// 一帧只建一次变量表：同一帧里所有文字卡片共用，时间 / 采样值完全一致。
        /// 用只读包装，避免某个模块顺手改写变量串到别的卡片。
        /// </summary>
        IDictionary<string, object> BuildFrameVariables()
        {
            try
            {
                return new ReadOnlyDictionary<string, object>(Variables.Build(_last, _settings, _keys));
            }
            catch (Exception ex)
            {
                Log.Warn("变量表构建失败: " + ex.Message);
                return null;
            }
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
            _hasSnapshot = !snapshot.Warmup;

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

            if (_hook != null) _hook.Suppress = true;

            slot.Summoned = true;
            ShowCard(slot, "");
            slot.Window.EnsureTopmost();
            slot.Window.ResetStableSize();

            // 命令行面板是富内容（和 AI 对话同一套 CliPanelView）：开始一次新会话，再让窗口把前台和键盘焦点抢过来。
            ICliFocusTarget target = slot.Content as ICliFocusTarget;
            if (target != null)
            {
                try { target.Begin(); }
                catch (Exception ex) { Log.Warn("命令行面板初始化失败: " + ex.Message); }
            }

            slot.Window.BeginCommandInput();

            // 交互卡片自己解释自己的设置并给出日志摘要（宿主不再认识 CommandSettings）。
            string summary = null;
            var activation = slot.Module as ICardActivation;
            if (activation != null)
            {
                try { summary = activation.OnActivated(slot.Config); }
                catch (Exception ex) { Log.Warn("卡片呼出回调失败: " + ex.Message); }
            }

            string cardName = slot.Config == null ? "?" : slot.Config.Name;
            Log.Info("快捷命令行已打开（卡片=" + cardName +
                     (string.IsNullOrEmpty(summary) ? "" : "，" + summary) + "）");
        }

        public void ExitCommandLine()
        {
            if (!_commandMode) return;

            _commandMode = false;

            CardSlot slot = _commandCard;
            _commandCard = null;

            if (slot != null)
            {
                // 交互卡片先收自己的状态，再让面板收尾。
                var activation = slot.Module as ICardActivation;
                if (activation != null)
                {
                    try { activation.OnDeactivated(slot.Config); }
                    catch (Exception ex) { Log.Warn("卡片收起回调失败: " + ex.Message); }
                }

                // 面板自己管着「还在跑的命令」和记录列表，收起前先让它收尾。
                ICliFocusTarget target = slot.Content as ICliFocusTarget;
                if (target != null)
                {
                    try { target.Cancel(); }
                    catch (Exception ex) { Log.Warn("命令行收尾失败: " + ex.Message); }
                }

                slot.Window.EndCommandInput();
                slot.Window.ResetStableSize();
                slot.Summoned = false;
                HideCard(slot);
            }

            if (_hook != null) _hook.Suppress = false;

            RenderNow();
            Log.Info("快捷命令行已关闭，命令与返回已清空");
        }

        // ==================== 启动动画 ====================

        CardSlot FirstLiveCard()
        {
            foreach (CardSlot slot in _cards)
                if (slot.Shown && !slot.Module.IsInteractive && slot.Rich == null) return slot;
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
                if (_settingsWindow.LoadFailed)
                {
                    _settingsWindow.CloseForReal();
                    _settingsWindow = null;
                    MessageBox.Show(
                        "无法读取当前设置，设置窗口没有打开（详情见日志）。\n" +
                        "为避免把默认值写回 settings.json，本次不允许保存。",
                        "MirrorpondHUD", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (!_settingsWindow.Reload(_settings))
            {
                MessageBox.Show(
                    "无法读取当前设置，设置窗口没有刷新（详情见日志）。\n" +
                    "为避免把默认值写回 settings.json，本次不允许保存。",
                    "MirrorpondHUD", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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
                    string contentKey = ContentSignature(config);
                    string styleKey = StyleSignature();

                    // 换配置 = 换 host 里的配置对象；模块种类可能变，先解绑旧的再绑新的。
                    DetachHost(slot);

                    slot.Config = config;
                    slot.Module = CardRegistry.Get(config.Kind);
                    slot.Rich = slot.Module as IRichCardModule;
                    slot.Window.Interactive = slot.Rich != null && slot.Rich.NeedsMouseInput;
                    slot.Summoned = summoned.TryGetValue(config.Id, out saved) ? saved : wasSummoned;
                    slot.Dialog = BuildDialog();

                    if (slot.Host == null) slot.Host = new CardHost(this, config);
                    else slot.Host.Config = config;
                    AttachHost(slot);

                    // 设置刚变过：下一次渲染立刻生效，别被刷新间隔压住。
                    slot.LastRenderUtc = DateTime.MinValue;

                    // 卡片种类可能没变，但模块能力（拖动 / 拖放）要跟着重新接线。
                    WireInputCapabilities(slot);

                    // 内容没变（改名字 / 热键 / 间距 / 只改外观）：不重建控件。
                    // 视频不重启、动图不回第一帧、AI 对话的历史也还在。
                    bool sameContent = slot.Content != null &&
                                       slot.Rich != null &&
                                       contentKey != null &&
                                       string.Equals(slot.ContentSignature, contentKey, StringComparison.Ordinal);

                    bool sameStyle = styleKey != null &&
                                     string.Equals(slot.StyleSignature, styleKey, StringComparison.Ordinal);

                    slot.ContentSignature = contentKey;
                    slot.StyleSignature = styleKey;

                    var context = new CardRenderContext(_settings, _last, _keys, config);

                    if (sameContent && sameStyle)
                    {
                        slot.Window.RefreshSettings(slot.Dialog);

                        try { slot.Rich.UpdateContent(slot.Content, context); }
                        catch (Exception ex)
                        {
                            Log.Warn("卡片内容刷新失败(" + config.Name + "): " + ex.Message);
                        }
                    }
                    else if (sameContent)
                    {
                        // 只改了外观 / CLI 尺寸：支持就地换肤的模块直接套新样式，不销毁内容。
                        var styleAware = slot.Module as ICardStyleAware;
                        if (styleAware != null)
                        {
                            slot.Window.RefreshSettings(slot.Dialog);

                            // 富内容卡的底色 / 不透明度 / 圆角 / 投影由 OverlaySurface 画；
                            // 只调模块 ApplyStyle 的话，媒体卡与 CLI 卡会一直保留旧的外观。
                            slot.Window.RefreshSurface(slot.Dialog);

                            try { styleAware.ApplyStyle(slot.Content, context); }
                            catch (Exception ex)
                            {
                                Log.Warn("卡片换肤失败(" + config.Name + "): " + ex.Message);
                            }
                        }
                        else
                        {
                            // 模块没实现就地换肤（CLI 面板的样式是构造期固化的）：回退到重建。
                            ReleaseContent(slot);

                            if (slot.Shown)
                            {
                                if (slot.Rich != null) ShowCard(slot, "");
                                else slot.Window.Apply(slot.Dialog, RenderCardText(slot));
                            }
                            else
                            {
                                slot.Window.Apply(slot.Dialog, "");
                            }
                        }
                    }
                    else
                    {
                        // 内容相关设置变了（文件 / 工具 / 模型）：按新设置重建富内容。
                        ReleaseContent(slot);

                        if (slot.Shown)
                        {
                            if (slot.Rich != null) ShowCard(slot, "");
                            else slot.Window.Apply(slot.Dialog, RenderCardText(slot));
                        }
                        else
                        {
                            slot.Window.Apply(slot.Dialog, "");
                        }
                    }

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

                DetachHost(removed);
                ReleaseContent(removed);
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
            var changedCards = new List<string>(4);

            foreach (CardSlot slot in _cards)
            {
                string action = HotkeyManager.CardAction(slot.Config.Id);
                string combination = slot.Config.Resident ? "" : slot.Config.Hotkey;

                if (string.IsNullOrWhiteSpace(combination))
                {
                    _hotkeys.Register(action, "");
                    continue;
                }

                string registered = RegisterOne(action, combination);
                if (!string.Equals(registered, combination, StringComparison.OrdinalIgnoreCase))
                {
                    // 被占用时退到 Ctrl+Alt+原键，并把实际生效的组合键写回卡片设置。
                    slot.Config.Hotkey = registered;
                    changedCards.Add(slot.Config.Name + " → " + registered);
                }

                if (string.IsNullOrWhiteSpace(registered))
                {
                    failed++;
                    Log.Warn("卡片热键注册失败: " + slot.Config.Name + " " + combination);
                }
            }

            if (changedCards.Count > 0)
            {
                Log.Warn("部分卡片热键被占用，已自动改用：" + string.Join("，", changedCards));
                ConfigStore.Save(_settings);
                if (_settingsWindow != null) _settingsWindow.Reload(_settings);
            }

            if (failed > 0 && _tray != null && _tray.Visible)
            {
                _tray.ShowBalloon("有 " + failed + " 张卡片的呼出热键注册失败",
                    "组合键可能被其它程序占用，可在「内容」页换一个");
            }

            RegisterCardActionHotkeys();
        }

        /// <summary>卡片模块提供的通用热键（媒体卡的前进 / 后退 / 变速 / 播放暂停）。</summary>
        void RegisterCardActionHotkeys()
        {
            var changed = new List<string>(4);

            foreach (ICardModule module in CardRegistry.Modules)
            {
                var source = module as ICardHotkeySource;
                if (source == null) continue;

                bool used = false;
                foreach (CardSlot slot in _cards)
                {
                    if (slot.Module != null && string.Equals(slot.Module.Kind, module.Kind, StringComparison.OrdinalIgnoreCase))
                    {
                        used = true;
                        break;
                    }
                }
                if (!used) continue;   // 没有这种卡片就不注册，免得占着组合键

                foreach (CardHotkeyAction entry in source.HotkeyActions)
                {
                    string saved = _settings.Hotkeys.GetCardAction(entry.Action);
                    if (string.IsNullOrWhiteSpace(saved)) saved = entry.DefaultHotkey;

                    string registered = RegisterOne(entry.Action, saved);
                    _settings.Hotkeys.SetCardAction(entry.Action, registered);

                    if (!string.IsNullOrWhiteSpace(saved) &&
                        !string.Equals(registered, saved, StringComparison.OrdinalIgnoreCase))
                    {
                        changed.Add(entry.Label + " " + saved + " → " + registered);
                    }
                }
            }

            if (changed.Count > 0)
            {
                Log.Warn("部分卡片通用热键被占用，已自动改用：" + string.Join("，", changed));
                ConfigStore.Save(_settings);
                if (_settingsWindow != null) _settingsWindow.Reload(_settings);
                if (_tray != null)
                    _tray.ShowBalloon("卡片热键被占用，已自动改用", string.Join("  ", changed));
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
                    else
                    {
                        ExecuteCardAction(action);
                    }
                    break;
            }
        }

        /// <summary>把卡片模块的通用热键转发给每张同类卡片的富内容。</summary>
        void ExecuteCardAction(string action)
        {
            if (string.IsNullOrEmpty(action)) return;

            foreach (CardSlot slot in _cards)
            {
                var target = slot.Module as ICardHotkeyTarget;
                var source = slot.Module as ICardHotkeySource;
                if (target == null || source == null || slot.Content == null) continue;

                bool known = false;
                foreach (CardHotkeyAction entry in source.HotkeyActions)
                {
                    if (string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known) continue;

                try { target.ExecuteHotkey(action, slot.Content); }
                catch (Exception ex) { Log.Warn("执行卡片热键失败: " + ex.Message); }
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

            foreach (CardSlot slot in _cards)
            {
                ICliFocusTarget target = slot.Content as ICliFocusTarget;
                if (target != null)
                {
                    try { target.Cancel(); } catch { }
                }

                DetachHost(slot);
            }

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
                ReleaseContent(slot);
                try { slot.Window.Close(); } catch { }
            }
            _cards.Clear();
        }
    }
}
