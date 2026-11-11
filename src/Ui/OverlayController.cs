using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 把配置、采样、卡片窗口、托盘、热键串起来。
/// 卡片位置不再逐张保存：所有可见卡片从配置的锚点开始共享一条垂直堆叠，
/// 每张卡片只负责自己内容与「与上一张的间距」。
/// </summary>
public sealed class OverlayController : IDisposable
{
    readonly List<string> _args;
    readonly KeyHistory _keys = new();

    Settings _settings;
    MetricEngine _engine;
    KeyboardHook _hook;
    SettingsWindow _settingsWindow;
    TrayController _tray;
    HotkeyManager _hotkeys;
    HotkeyRouter _hotkeyRouter;
    EventWaitHandle _activation;
    Thread _activationThread;
    volatile bool _disposed;
    bool _overlayVisible;

    readonly List<CardSlot> _cards = new();
    readonly CardLayoutCoordinator _layout;
    readonly RenderScheduler _render;
    readonly SettingsApplier _settingsApplier;
    readonly OverlayIntro _intro;
    readonly CommandModeController _command;

    /// <summary>把控制器的公开 / 私有动作暴露给 HotkeyRouter，避免把 CardSlot 抬成公开契约。</summary>
    sealed class HotkeyHostAdapter : IHotkeyHost
    {
        readonly OverlayController _owner;

        public HotkeyHostAdapter(OverlayController owner)
        {
            _owner = owner;
        }

        public Settings Settings => _owner._settings;
        public IList<CardSlot> Cards => _owner._cards;
        public bool TrayVisible => _owner._tray is not null && _owner._tray.Visible;

        public void ToggleVisibility() => _owner.ToggleVisibility();
        public void ShowSettings() => _owner.ShowSettings();
        public void ToggleLock() => _owner.ToggleLock();
        public CardSlot FindCard(string id) => _owner.FindCard(id);
        public void ToggleCard(CardSlot slot) => _owner.ToggleCard(slot);
        public void SaveSettings() => _owner.SaveSettings();

        public void ReloadSettings()
        {
            if (_owner._settingsWindow is not null) _owner._settingsWindow.Reload(_owner._settings);
        }

        public void ShowBalloon(string title, string text)
        {
            if (_owner._tray is not null) _owner._tray.ShowBalloon(title, text);
        }
    }

    /// <summary>把控制器的状态与动作暴露给 TrayController。</summary>
    sealed class TrayHostAdapter : ITrayHost
    {
        readonly OverlayController _owner;

        public TrayHostAdapter(OverlayController owner)
        {
            _owner = owner;
        }

        public Settings Settings => _owner._settings;
        public bool OverlayVisible => _owner._overlayVisible;

        public string CommandHotkey
        {
            get
            {
                CardSlot command = _owner.FindCommandCard();
                return command is null || command.Config is null ? "" : command.Config.Hotkey;
            }
        }

        public void ToggleVisibility() => _owner.ToggleVisibility();
        public void ShowSettings() => _owner.ShowSettings();
        public void ToggleLock() => _owner.ToggleLock();
        public void ToggleAutoStart() => _owner.ToggleAutoStart();
        public void ToggleCommandLine() => _owner.ToggleCommandLine();
        public void Exit() => _owner.Exit();
    }

    /// <summary>把控制器的显示 / 键盘 / 重绘动作暴露给 CommandModeController。</summary>
    sealed class CommandHostAdapter : ICommandModeHost
    {
        readonly OverlayController _owner;

        public CommandHostAdapter(OverlayController owner)
        {
            _owner = owner;
        }

        public IList<CardSlot> Cards => _owner._cards;
        public bool OverlayVisible => _owner._overlayVisible;
        public bool IntroRunning => _owner._intro.IsRunning;

        public void FinishIntro() => _owner._intro.Finish();

        public void ShowOverlay()
        {
            _owner._overlayVisible = true;
            _owner.RefreshVisibility();
        }

        public void ShowCard(CardSlot slot, string text, bool animate) => _owner.ShowCard(slot, text, animate);
        public void HideCard(CardSlot slot, bool animate) => _owner.HideCard(slot, animate);
        public void RenderNow() => _owner.RenderNow();
        public void UpdateCommandInfo() => _owner.UpdateCommandInfo();

        public void SetKeyboardSuppressed(bool suppressed)
        {
            if (_owner._hook is not null) _owner._hook.Suppress = suppressed;
        }
    }

    /// <summary>把渲染调度需要的状态与卡片动作暴露给 RenderScheduler。</summary>
    sealed class RenderHostAdapter : IRenderHost
    {
        readonly OverlayController _owner;

        public RenderHostAdapter(OverlayController owner)
        {
            _owner = owner;
        }

        public Settings Settings => _owner._settings;
        public IList<CardSlot> Cards => _owner._cards;
        public KeyHistory Keys => _owner._keys;
        public bool OverlayVisible => _owner._overlayVisible;
        public bool IntroRunning => _owner._intro.IsRunning;

        public bool IsIntroCard(CardSlot slot) => _owner._intro.IsCard(slot);
        public void IntroLayoutChanged() => _owner._intro.LayoutChanged();
        public void LayoutIfNeeded() => _owner._layout.LayoutIfNeeded();
        public void ReleaseContent(CardSlot slot) => _owner.ReleaseContent(slot);
        public void ShowCard(CardSlot slot, string text) => _owner.ShowCard(slot, text);
    }

    /// <summary>把设置应用需要的状态与卡片动作暴露给 SettingsApplier。</summary>
    sealed class SettingsHostAdapter : ISettingsHost
    {
        readonly OverlayController _owner;

        public SettingsHostAdapter(OverlayController owner)
        {
            _owner = owner;
        }

        public Settings Settings
        {
            get => _owner._settings; set => _owner._settings = value;
        }

        public IList<CardSlot> Cards => _owner._cards;

        public bool OverlayVisible
        {
            get => _owner._overlayVisible; set => _owner._overlayVisible = value;
        }

        public CardSlot CreateSlot(CardSettings config) => _owner.CreateSlot(config);
        public void RebindSlot(CardSlot slot, CardSettings config, bool summoned) => _owner.RebindSlot(slot, config, summoned);

        public void CloseSlot(CardSlot slot) => _owner.CloseSlot(slot);
        public void ShowCard(CardSlot slot, string text, bool animate) => _owner.ShowCard(slot, text, animate);
        public void HideCard(CardSlot slot, bool animate) => _owner.HideCard(slot, animate);
        public void BeginCardDrag(CardSlot slot) => _owner._settingsApplier.BeginCardDrag(slot);
        public void OnCardDragging(CardSlot slot, int dx, int dy) => _owner._settingsApplier.OnCardDragging(slot, dx, dy);
        public void ToggleCommandCard(CardSlot slot) => _owner._command.Toggle(slot);
        public void ExitCommandLine() { if (_owner._command.IsActive) _owner._command.Exit(); }
        public void FinishIntro() => _owner._intro.Finish();
        public void RefreshVisibility() => _owner.RefreshVisibility();
        public void UpdateCommandInfo() => _owner.UpdateCommandInfo();
        public void RebuildHotkeys() { if (_owner._hotkeyRouter is not null) _owner._hotkeyRouter.Register(); }
        public void UpdateEngine() { if (_owner._engine is not null) _owner._engine.UpdateSettings(_owner._settings); }
        public void RefreshTray() { if (_owner._tray is not null) _owner._tray.Update(); }

        public void SyncSettingsWindowAnchor(double x, double y, int monitor)
        {
            if (_owner._settingsWindow is not null) _owner._settingsWindow.SyncAnchor(x, y, monitor);
        }
    }

    public OverlayController(Settings settings, List<string> args)
    {
        _settings = settings;
        _args = args ?? new List<string>();
        _layout = new CardLayoutCoordinator(_cards, () => _settings, () => _disposed,
                                            Dispatcher.CurrentDispatcher);
        _render = new RenderScheduler(new RenderHostAdapter(this), Dispatcher.CurrentDispatcher);
        _settingsApplier = new SettingsApplier(new SettingsHostAdapter(this), _render, _layout, _keys);
        _intro = new OverlayIntro(_cards, () => _settings, _render.CardTemplate,
                                  _render.RenderCardText, _layout.SyncHeight, _layout.Layout,
                                  _render.RenderNow, Dispatcher.CurrentDispatcher);
        _command = new CommandModeController(new CommandHostAdapter(this));
    }

    public Settings CurrentSettings => _settings;

    /// <summary>
    /// 当前实际生效的毛玻璃状态：取第一张有窗口的卡片（同一次运行里各卡片的实现一致，
    /// 只是降级时机可能差一帧）。设置窗用它显示「当前实际」，不再只按用户选的下拉框猜。
    /// 还没有卡片窗口时返回「未启用」。
    /// </summary>
    internal GlassRuntimeState CurrentGlassState
    {
        get
        {
            foreach (CardSlot slot in _cards)
            {
                if (slot is null || slot.Window is null) continue;
                return slot.Window.GlassState;
            }

            return new GlassRuntimeState(GlassBackdropKind.None, "", "", true, true);
        }
    }

    public void Start()
    {
        Log.Info("=== 开始启动 ===");
        _render.ResetSnapshot();
        _keys.Configure(_settings.Data.KeyHistoryCount, _settings.Data.RepeatThreshold,
                        _settings.Data.MaxKeyChars);

        bool autoStarted = _args.Contains("--autostart");
        bool forcedVisible = _args.Contains("--visible");
        bool minimized = !forcedVisible &&
                         ((!autoStarted && _settings.Behavior.StartMinimized) || _args.Contains("--minimized"));
        bool intro = _settings.Behavior.IntroEnabled && !minimized;
        CardMotion.ApplySettings(_settings);

        CreateCards();
        _overlayVisible = !minimized;

        RefreshVisibility();
        UpdateCommandInfo();

        if (minimized)
            _intro.Reset();
        else if (intro)
            _intro.Begin();

        Log.Info($"卡片已创建，数量={_cards.Count}，可见={_overlayVisible}，置顶={_settings.Overlay.Topmost}，锁定={_settings.Overlay.Locked}");

        _render.Start();

        Safe("键盘钩子", StartKeyboardHook);
        Safe("采样引擎", StartEngine);
        Safe("全局热键", StartHotkeys);
        Safe("托盘图标", StartTray);
        Safe("开机自启", _settingsApplier.ApplyAutoStart);
        Safe("单实例唤醒", StartActivationListener);

        Log.Info($"启动完成（叠层可见={_overlayVisible}，全局热键={_settings.Hotkeys.ToggleOverlay}/{_settings.Hotkeys.OpenSettings}/{_settings.Hotkeys.ToggleLock}）");

        _tray?.ShowStartupBalloon();
    }

    // ==================== 卡片 ====================

    void CreateCards()
    {
        _cards.Clear();
        _command.Reset();
        _intro.Reset();

        foreach (CardSettings config in _settings.Cards)
            _cards.Add(CreateSlot(config));
    }

    CardSlot CreateSlot(CardSettings config)
    {
        Settings dialog = _settingsApplier.BuildDialog();
        var slot = new CardSlot
        {
            Config = config,
            Module = CardRegistry.Get(config.Kind),
            Dialog = dialog,
            ContentSignature = _render.ContentSignature(config),
            StyleSignature = _render.StyleSignature()
        };
        slot.Rich = slot.Module as IRichCardModule;
        slot.Host = new CardHost(this, config);

        var window = new OverlayWindow(dialog);
        // 带按钮 / 输入框的卡片（AI 对话、按钮演示）自己声明需要鼠标，宿主据此关掉这张卡的点击穿透。
        window.Interactive = slot.Rich is not null && slot.Rich.NeedsMouseInput;
        slot.Window = window;

        WireInputCapabilities(slot);

        window.SettingsRequested += ShowSettings;
        window.HideRequested += ToggleVisibility;
        window.LockToggleRequested += ToggleLock;
        window.ExitRequested += Exit;
        window.DragStarted += () => _settingsApplier.BeginCardDrag(slot);
        window.Dragging += (dx, dy) => _settingsApplier.OnCardDragging(slot, dx, dy);
        window.Moved += (dx, dy, monitor) => _settingsApplier.OnCardMoved(slot, dx, dy, monitor);
        window.CommandLineRequested += () => ToggleCommandLine();
        window.SizeChanged += (s, e) => _layout.Schedule();
        window.RaisedTopmost += () =>
        {
            if (slot.Rich is null || slot.Content is null) return;
            try { slot.Rich.OnZOrderChanged(slot.Content); }
            catch (Exception ex) { Log.Warn($"卡片重新置顶后富内容 z 序恢复失败({slot.Config.Name}): {ex.Message}"); }
        };

        return slot;
    }

    /// <summary>
    /// 设置应用后复用一张仍然存在的卡片：换配置对象、模块与宿主服务，
    /// 并重接拖动 / 拖放能力。内容控件的保留或重建由 RenderScheduler 按签名决定。
    /// </summary>
    void RebindSlot(CardSlot slot, CardSettings config, bool summoned)
    {
        // 换配置 = 换 host 里的配置对象；模块种类可能变，内容签名会随之更新。
        slot.Config = config;
        slot.Module = CardRegistry.Get(config.Kind);
        slot.Rich = slot.Module as IRichCardModule;
        slot.Window.Interactive = slot.Rich is not null && slot.Rich.NeedsMouseInput;
        slot.Summoned = summoned;
        slot.Dialog = _settingsApplier.BuildDialog();

        if (slot.Host is null) slot.Host = new CardHost(this, config);
        else slot.Host.Config = config;

        // 设置刚变过：下一次渲染立刻生效，别被刷新间隔压住。
        slot.LastRenderUtc = DateTime.MinValue;

        // 卡片种类可能没变，但模块能力（拖动 / 拖放）要跟着重新接线。
        WireInputCapabilities(slot);
    }

    /// <summary>卡片被删掉：解绑宿主、释放富内容、关闭窗口。</summary>
    void CloseSlot(CardSlot slot)
    {
        if (ReferenceEquals(slot, _command.Card)) _command.Reset();

        ReleaseContent(slot);
        try { slot.Window.Close(); } catch { }
    }

    /// <summary>
    /// 按卡片能力接线：内容自己处理拖动（<see cref="ICardModule.HandlesOwnDrag"/>）时关掉整卡拖动；
    /// 实现 <see cref="ICardDropTarget"/> 的卡片由宿主统一注册 AllowDrop / DragOver / Drop。
    /// </summary>
    void WireInputCapabilities(CardSlot slot)
    {
        if (slot is null || slot.Window is null) return;

        slot.Window.DragEnabled = slot.Module is null || !slot.Module.HandlesOwnDrag;

        var feedback = slot.Module as ICardDropFeedback;
        Action<bool, IDataObject> dropState = null;
        if (feedback is not null)
        {
            dropState = (over, data) =>
            {
                try { feedback.OnDropStateChanged(slot.Content, over, data); }
                catch (Exception ex) { Log.Warn($"卡片拖放状态处理失败: {ex.Message}"); }
            };
        }

        var drop = slot.Module as ICardDropTarget;
        if (drop is null)
        {
            slot.Window.EnableDrop(null, null, dropState);
            return;
        }

        CardSlot captured = slot;
        slot.Window.EnableDrop(
            data =>
            {
                try { return drop.CanDrop(data); }
                catch (Exception ex) { Log.Warn($"卡片拖放判定失败: {ex.Message}"); return false; }
            },
            data =>
            {
                try { drop.Drop(data, captured.Content); }
                catch (Exception ex) { Log.Warn($"卡片拖放处理失败: {ex.Message}"); }
            },
            dropState);
    }

    /// <summary>模块改了数据但采样快照没变：排一次重画。</summary>
    internal void RequestRenderFromModule()
    {
        if (_disposed || Application.Current is null) return;
        Application.Current.Dispatcher.BeginInvoke(new Action(RenderNow), DispatcherPriority.Background);
    }

    /// <summary>
    /// 卡片绕过设置窗改了运行期字段（Chat 模式）并落盘后，刷新它的内容签名。
    /// 不刷新的话下一次应用设置会把模式变化当成“内容变了”，重建 ChatView 并清空新对话。
    /// </summary>
    internal void RefreshContentSignature(CardSettings config)
    {
        if (config is null) return;

        foreach (CardSlot slot in _cards)
        {
            if (!ReferenceEquals(slot.Config, config)) continue;
            slot.ContentSignature = _render.ContentSignature(config);
            return;
        }
    }

    /// <summary>内容尺寸变了：排一次布局校正。</summary>
    internal void RequestLayoutFromModule()
    {
        if (_disposed) return;
        _layout.Schedule();
    }

    // ==================== 显示 / 隐藏 / 堆叠 ====================

    void RefreshVisibility()
    {
        foreach (CardSlot slot in _cards)
        {
            bool show;
            if (!_overlayVisible) show = false;
            else if (slot.Module.IsInteractive) show = _command.IsActive && ReferenceEquals(slot, _command.Card);
            else show = slot.Config.Resident || slot.Summoned;

            if (show && !slot.Shown)
                ShowCard(slot, _render.HasSnapshot ? _render.RenderCardText(slot) : _render.RenderPlaceholderText(slot));
            else if (!show && slot.Shown)
                HideCard(slot);
        }

        _layout.Layout();
    }

    void ShowCard(CardSlot slot, string text) => ShowCard(slot, text, false);

    /// <summary>
    /// 显示卡片。animate=true 时内容淡入 + 上移 8px，其余已显示卡片同时补位；
    /// 启动 / 设置重建 / 截图走 animate=false，保持原来的瞬切语义。
    /// </summary>
    void ShowCard(CardSlot slot, string text, bool animate)
    {
        if (slot is null) return;

        // 刚显示要立刻画一帧，不要等模块的刷新间隔。
        slot.LastRenderUtc = DateTime.MinValue;

        if (slot.Rich is not null)
        {
            EnsureContent(slot);
            slot.Window.ApplyRich(slot.Dialog, slot.Content);
        }
        else
        {
            slot.Window.Apply(slot.Dialog, text ?? "", slot.Config);
        }

        bool motion = animate && CardMotion.Enabled;
        if (motion) slot.Window.PrepareShowMotion();
        else slot.Window.ResetContentMotion();

        slot.Window.Show();
        slot.Window.UpdateLayout();

        slot.Shown = true;
        slot.BodyHeight = slot.Window.CardHeight;
        if (motion) _layout.LayoutAnimated(slot);
        else _layout.Layout();

        if (motion) slot.Window.AnimateShowMotion();

        slot.Window.UpdateClickThrough();
        slot.Window.EnsureTopmost();

        // 富内容要等卡片窗口真正显示、布局完成后再开始播放（视频子窗口要贴到正确位置）。
        if (slot.Rich is not null && slot.Content is not null)
        {
            try { slot.Rich.OnVisibilityChanged(slot.Content, true); }
            catch (Exception ex) { Log.Warn($"卡片显示后内容启动失败({slot.Config.Name}): {ex.Message}"); }
        }
    }

    /// <summary>富内容卡片的控件按需创建；重建前释放旧控件（停掉视频 / 动图）。</summary>
    void EnsureContent(CardSlot slot)
    {
        if (slot is null || slot.Rich is null) return;
        if (slot.Content is not null) return;

        try
        {
            slot.Content = slot.Rich.CreateContent(
                new CardRenderContext(_settings, _render.LastSnapshot, _keys, slot.Config, null, slot.Host));
        }
        catch (Exception ex)
        {
            Log.Warn($"卡片内容创建失败({slot.Config.Name}): {ex.Message}");
            slot.Content = null;
        }
    }

    void ReleaseContent(CardSlot slot)
    {
        if (slot is null || slot.Content is null) return;

        try
        {
            if (slot.Rich is not null) slot.Rich.OnVisibilityChanged(slot.Content, false);
        }
        catch (Exception ex)
        {
            Log.Warn($"卡片释放前停止内容失败({slot.Config.Name}): {ex.Message}");
        }

        try
        {
            var disposable = slot.Content as IDisposable;
            if (disposable is not null) disposable.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"卡片富内容释放失败({slot.Config.Name}): {ex.Message}");
        }

        slot.Content = null;
        try { slot.Window.ClearRichContent(); }
        catch (Exception ex) { Log.Warn($"卡片富内容宿主清理失败({slot.Config.Name}): {ex.Message}"); }
    }

    void HideCard(CardSlot slot) => HideCard(slot, false);

    /// <summary>
    /// 隐藏卡片。animate=true 时先把 slot 标记为不占位并让其余卡片补位，
    /// 内容淡出 160ms 后才真正 Hide；若期间又被呼出，完成回调不会误隐藏。
    /// </summary>
    void HideCard(CardSlot slot, bool animate)
    {
        if (slot is null || !slot.Shown) return;

        if (slot.Rich is not null && slot.Content is not null)
        {
            try { slot.Rich.OnVisibilityChanged(slot.Content, false); }
            catch (Exception ex) { Log.Warn($"卡片隐藏前停止内容失败({slot.Config.Name}): {ex.Message}"); }
        }

        slot.Shown = false;
        slot.BodyHeight = 0;

        bool motion = animate && CardMotion.Enabled;
        if (!motion)
        {
            slot.Window.ResetContentMotion();
            slot.Window.Hide();
            _layout.Layout();
            return;
        }

        // 先让其余卡片补位，再淡出旧窗口；两者并行，视觉上像卡片被抽走后空位合拢。
        _layout.LayoutAnimated();
        slot.Window.AnimateHideMotion(() =>
        {
            if (_disposed || slot.Shown) return;
            try { slot.Window.Hide(); }
            catch (Exception ex) { Log.Warn($"卡片隐藏收尾失败: {ex.Message}"); }
        });
    }

    void UpdateCommandInfo()
    {
        CardSlot command = FindCommandCard();
        string hotkey = command is null || command.Config is null ? "" : command.Config.Hotkey;

        foreach (CardSlot slot in _cards)
            slot.Window.SetCommandInfo(command is not null, hotkey);
    }

    CardSlot FindCommandCard()
    {
        foreach (CardSlot slot in _cards)
            if (slot.Module is not null && slot.Module.IsInteractive) return slot;
        return null;
    }

    CardSlot FindCard(string id)
    {
        foreach (CardSlot slot in _cards)
            if (slot.Config is not null && string.Equals(slot.Config.Id, id, StringComparison.OrdinalIgnoreCase))
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
            Log.Error($"{name} 初始化失败", ex);
        }
    }

    // ==================== 各子系统 ====================

    void StartKeyboardHook()
    {
        _hook = new KeyboardHook(_keys);
        bool ok = _hook.Start();
        Log.Info($"键盘钩子：{(ok ? "已安装" : "安装失败")}");
    }

    void StartEngine()
    {
        _engine = new MetricEngine(_settings);
        _engine.Updated += _render.OnSnapshot;
        _engine.Start();
    }

    void StartHotkeys()
    {
        _hotkeys = new HotkeyManager();
        _hotkeyRouter = new HotkeyRouter(_hotkeys, new HotkeyHostAdapter(this));
        _hotkeyRouter.Register();
    }

    void StartTray()
    {
        _tray = new TrayController(new TrayHostAdapter(this));
        _tray.Start();
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
                    Log.Warn($"唤醒监听退出: {ex.Message}");
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

    /// <summary>渲染入口；定时器 / 快照 / 变量表都在 <see cref="RenderScheduler"/> 里。</summary>
    void RenderNow() => _render.RenderNow();

    // ==================== 快捷命令行 ====================

    public void ToggleCommandLine() => _command.Toggle(FindCommandCard());

    public void ExitCommandLine() => _command.Exit();

    // ==================== 操作 ====================

    public void ToggleVisibility()
    {
        if (_cards.Count == 0) return;

        if (_overlayVisible)
        {
            if (_command.IsActive) _command.Exit();
            if (_intro.IsRunning) _intro.Finish();
            _overlayVisible = false;
            foreach (CardSlot slot in _cards) HideCard(slot);
        }
        else
        {
            _overlayVisible = true;
            RefreshVisibility();
            RenderNow();
        }

        Log.Info($"叠层可见={_overlayVisible}");
        _tray?.Update();
    }

    public void ToggleLock()
    {
        if (_command.IsActive) _command.Exit();
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
            _tray?.Update();
            Log.Info($"叠层锁定={locked}");
        }
    }

    public void ShowSettings()
    {
        if (_command.IsActive) _command.Exit();

        if (_settingsWindow is null)
        {
            // 第 5 个参数把「当前实际实现」提供给设置窗：下拉框是用户的期望，运行时状态才是事实。
            _settingsWindow = new SettingsWindow(_settings, ApplySettings, ApplyAnchor, ClearKeys,
                                                 () => CurrentGlassState);
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

    public bool ApplySettings(Settings settings) => _settingsApplier.Apply(settings);

    void ApplyAnchor(double x, double y, int monitor) => _settingsApplier.ApplyAnchor(x, y, monitor);

    /// <summary>写设置文件并如实记录失败；调用方（设置窗 / 卡片宿主）按返回值提示（P0-2）。</summary>
    bool SaveSettings() => _settingsApplier.SaveSettings();

    void ToggleAutoStart() => _settingsApplier.ToggleAutoStart();

    // ==================== 热键 ====================

    /// <summary>非常驻卡片的呼出 / 收起。</summary>
    void ToggleCard(CardSlot slot) => _settingsApplier.ToggleCard(slot);

    public void Exit()
    {
        // 退出软件不是「退出设置」：先无提示关闭设置窗，避免未保存弹窗挡住 Shutdown。
        try
        {
            if (_settingsWindow is not null) _settingsWindow.CloseForShutdown();
        }
        catch (Exception ex)
        {
            Log.Warn($"退出前关闭设置窗失败: {ex.Message}");
        }

        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _render.Dispose(); } catch { }
        try { _intro.Dispose(); } catch { }

        foreach (CardSlot slot in _cards)
        {
            ICliFocusTarget target = slot.Content as ICliFocusTarget;
            if (target is not null)
            {
                try { target.Cancel(); } catch { }
            }
        }

        try { _activation?.Set(); } catch { }
        try { _activation?.Dispose(); } catch { }

        try { _engine?.Dispose(); } catch { }
        try { _hook?.Dispose(); } catch { }
        try { _hotkeyRouter?.Dispose(); } catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }

        try
        {
            if (_settingsWindow is not null) _settingsWindow.CloseForReal();
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
