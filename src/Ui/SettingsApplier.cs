using System;
using System.Collections.Generic;
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>设置应用 / 卡片同步需要宿主提供的状态与动作。</summary>
internal interface ISettingsHost
{
    Settings Settings { get; set; }
    IList<CardSlot> Cards { get; }
    bool OverlayVisible { get; set; }

    CardSlot CreateSlot(CardSettings config);
    void RebindSlot(CardSlot slot, CardSettings config, bool summoned);
    void CloseSlot(CardSlot slot);
    void ShowCard(CardSlot slot, string text, bool animate);
    void HideCard(CardSlot slot, bool animate);
    void BeginCardDrag(CardSlot slot);
    void OnCardDragging(CardSlot slot, int dx, int dy);
    void ToggleCommandCard(CardSlot slot);
    void ExitCommandLine();
    void FinishIntro();
    void RefreshVisibility();
    void UpdateCommandInfo();
    void RebuildHotkeys();
    void UpdateEngine();
    void RefreshTray();
    void SyncSettingsWindowAnchor(double x, double y, int monitor);
}

/// <summary>
/// 设置应用与卡片同步：应用设置窗里的新配置、增删 / 重排卡片窗口、拖拽后的锚点落盘、
/// 开机自启与非常驻卡片的呼出 / 收起。
///
/// 从 <see cref="OverlayController"/> 抽出来（2026-11 审阅第三批第 12 项）：
/// 控制器只保留“公开 API + 协调”，设置树仍只有 <see cref="ISettingsHost.Settings"/> 这一份，
/// 这里不另存配置引用，避免拆出双真相。
/// </summary>
internal sealed class SettingsApplier
{
    readonly ISettingsHost _host;
    readonly RenderScheduler _render;
    readonly CardLayoutCoordinator _layout;
    readonly KeyHistory _keys;

    // 拖动期间只做「其他卡片按增量跟随」：锚点等到松手才更新，避免每帧落盘与重排。
    CardSlot _dragSlot;
    int _dragLastDx;
    int _dragLastDy;

    /// <summary>
    /// S-06：本次 Apply 中“设置已生效但附属动作失败”的原因（目前是开机自启写注册表）。
    /// 设置窗在成功后把它追加到状态行，避免出现“已保存并应用”但自启其实没写进去。
    /// </summary>
    public string LastApplyWarning { get; private set; }

    public SettingsApplier(ISettingsHost host, RenderScheduler render,
                           CardLayoutCoordinator layout, KeyHistory keys)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _render = render ?? throw new ArgumentNullException(nameof(render));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    /// <summary>
    /// 每张卡片的窗口需要「外观 + 本卡位置」。这里浅拷贝一份 <see cref="Settings"/>：
    /// 外观 / CLI / 热键这些大对象共享同一个实例，只有 <see cref="OverlaySettings"/> 单独复制
    /// （窗口会改自己的 X / Y / 锁定状态，不能互相踩）。
    ///
    /// 不做整份 JSON 深拷贝：卡片越多副本越多；而且克隆绕过 Normalize，两份配置容易再次漂移。
    /// </summary>
    public Settings BuildDialog()
    {
        Settings settings = _host.Settings;
        return new Settings
        {
            SchemaVersion = settings.SchemaVersion,
            Overlay = CopyOverlay(settings.Overlay),
            Style = settings.Style,
            Cli = settings.Cli,
            Data = settings.Data,
            Behavior = settings.Behavior,
            Hotkeys = settings.Hotkeys,
            Cards = settings.Cards
        };
    }

    static OverlaySettings CopyOverlay(OverlaySettings source)
    {
        if (source is null) return new OverlaySettings();
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

    public bool Apply(Settings settings)
    {
        _host.ExitCommandLine();
        LastApplyWarning = null;

        // 位置与锁定是运行时状态：只由拖动、F8 和托盘菜单改变。
        // 设置窗口里的坐标是打开窗口那一刻的快照，这里必须以实时值为准。
        Settings current = _host.Settings;
        settings.Overlay.X = current.Overlay.X;
        settings.Overlay.Y = current.Overlay.Y;
        settings.Overlay.Monitor = current.Overlay.Monitor;
        settings.Overlay.Locked = current.Overlay.Locked;

        _host.FinishIntro();

        var summoned = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (CardSlot slot in _host.Cards) summoned[slot.Config.Id] = slot.Summoned;

        _host.Settings = settings;
        try
        {
            settings.Normalize();
            CardModel.Normalize(settings);

            SyncCards(summoned);
        }
        catch (Exception ex)
        {
            // SyncCards 可能已经改了一部分槽位（RebindSlot 先于 cards.Clear）。
            // 这里恢复旧 Settings，再用旧树重建一次卡片视图，避免“运行用旧树、内存是新树”。
            Log.Error("应用设置失败，回滚到旧设置", ex);
            _host.Settings = current;
            try
            {
                SyncCards(summoned);
                _host.RefreshVisibility();
            }
            catch (Exception rollback)
            {
                Log.Error("应用设置回滚失败（界面可能处于新旧混合状态）", rollback);
            }
            throw;
        }

        _keys.Configure(settings.Data.KeyHistoryCount, settings.Data.RepeatThreshold,
                        settings.Data.MaxKeyChars);
        CardMotion.ApplySettings(settings);

        _host.UpdateEngine();
        // 先注册热键：注册失败会改写内存里的回退组合键，统一在下面一次落盘。
        // 旧顺序是“先存盘、再让路由器存第二次并 Reload”，LastSaveError 会被后一次覆盖，
        // 保存状态可能说反（S-02），并且整棵卡片列表会被重建三次（S-04）。
        _host.RebuildHotkeys();

        // 内存里的设置已经生效；落盘失败时必须如实告诉窗口，不能谎报成功（P0-2）。
        bool saved = SaveSettings();
        ApplyAutoStart();
        _host.RefreshTray();
        _host.UpdateCommandInfo();
        _host.RefreshVisibility();
        _render.RenderNow();

        Log.Info($"设置已应用（卡片 {_host.Cards.Count} 张{(saved ? "" : "，未写入磁盘")}）");
        return saved;
    }

    /// <summary>按新设置增删 / 重排卡片窗口，保留仍在列表里的窗口和它们的呼出状态。</summary>
    void SyncCards(Dictionary<string, bool> summoned)
    {
        IList<CardSlot> cards = _host.Cards;

        var existing = new Dictionary<string, CardSlot>(StringComparer.OrdinalIgnoreCase);
        foreach (CardSlot slot in cards) existing[slot.Config.Id] = slot;

        var next = new List<CardSlot>();

        foreach (CardSettings config in _host.Settings.Cards)
        {
            if (existing.TryGetValue(config.Id, out var slot))
            {
                existing.Remove(config.Id);

                // 呼出状态跨设置应用保留：没在这次快照里出现的卡片沿用当前值。
                if (!summoned.TryGetValue(config.Id, out var summonedNow)) summonedNow = slot.Summoned;

                _host.RebindSlot(slot, config, summonedNow);
                _render.RefreshSlot(slot, config);
                next.Add(slot);
            }
            else
            {
                next.Add(_host.CreateSlot(config));
            }
        }

        foreach (CardSlot removed in existing.Values) _host.CloseSlot(removed);

        cards.Clear();
        foreach (CardSlot slot in next) cards.Add(slot);
    }

    public void ApplyAnchor(double x, double y, int monitor)
    {
        Settings settings = _host.Settings;
        settings.Overlay.X = x;
        settings.Overlay.Y = y;
        settings.Overlay.Monitor = monitor;

        if (!_host.OverlayVisible)
        {
            _host.OverlayVisible = true;
            _host.RefreshVisibility();
        }
        else
        {
            _layout.Layout();
        }

        SaveSettings();

        _host.SyncSettingsWindowAnchor(x, y, monitor);
    }

    /// <summary>
    /// 开始拖动：取消所有卡片正在跑的补位动画。拖动中的位置由
    /// <see cref="OnCardDragging"/> 直接按增量平移，不能和旧动画抢窗口。
    /// </summary>
    public void BeginCardDrag(CardSlot slot)
    {
        _dragSlot = slot;
        _dragLastDx = 0;
        _dragLastDy = 0;

        foreach (CardSlot card in _host.Cards)
        {
            if (card.Window is not null) card.Window.StopPositionMotion();
        }

        // 停掉动画后先让整叠落到目标位并挂起布局，再读取拖动起点（OverlayWindow 已调整顺序）。
        // 否则残余动画中途的坐标会被当成起点，松手时锚点算错几像素。
        _layout.BeginDrag(slot);
    }

    /// <summary>
    /// 拖动中：被拖窗口已经由 OverlayWindow 自己移动，这里把其余已显示窗口按总位移的
    /// 增量平移，形成「整叠跟着走」；锚点保持不动，松手后才落盘并做补位/对齐动画。
    /// </summary>
    public void OnCardDragging(CardSlot slot, int dx, int dy)
    {
        if (slot is null || !ReferenceEquals(slot, _dragSlot)) return;

        int stepX = dx - _dragLastDx;
        int stepY = dy - _dragLastDy;
        _dragLastDx = dx;
        _dragLastDy = dy;

        if (stepX == 0 && stepY == 0) return;

        // 拖动事件给的是物理像素增量：其余卡片也按物理像素平移，
        // 跨到不同缩放的显示器时不会因为 DIP 换算而错位。
        foreach (CardSlot card in _host.Cards)
        {
            if (ReferenceEquals(card, slot) || !card.Shown || card.Window is null) continue;
            card.Window.OffsetPhysical(stepX, stepY);
        }
    }

    /// <summary>拖拽整叠卡片后的新锚点：动画重排、落盘，并同步设置窗里的位置显示。</summary>
    public void OnCardMoved(CardSlot slot, int physicalX, int physicalY, int monitor)
    {
        if (slot is null) return;

        _dragSlot = null;
        _dragLastDx = 0;
        _dragLastDy = 0;

        Settings settings = _host.Settings;

        // 落点是物理像素；锚点是「目标工作区左上角起的 DIP」，按目标屏 DPI 反算。
        // 被拖的不一定是第一张卡，先扣掉它在叠层里的对齐 / 间距偏移。
        ScreenInfo screen = ScreenGeometry.Get(monitor);
        if (screen is null)
        {
            // 目标屏信息拿不到时也必须恢复布局，否则整叠会一直停在“拖动挂起”状态。
            _layout.EndDrag(slot, null);
            return;
        }

        double landedX = (physicalX - screen.WorkLeft) / screen.ScaleX;
        double landedY = (physicalY - screen.WorkTop) / screen.ScaleY;
        double cardWidth = slot.Window is null ? 0 : slot.Window.CardWidth;
        double cardCenterX = landedX + cardWidth / 2;

        // 先量整叠宽度：右对齐时锚点 = 被抓卡左边缘 - (整叠宽 - 被抓卡宽)。
        double stackWidth = 0;
        foreach (CardSlot card in _host.Cards)
        {
            if (card.Shown && card.Window is not null)
                stackWidth = Math.Max(stackWidth, card.Window.CardWidth);
        }

        double offsetY = slot.Dialog.Overlay.Y - settings.Overlay.Y;
        settings.Overlay.Y = landedY - offsetY;

        // 自动对齐以“被抓住的卡片”为基准：它在屏幕左半边就整叠左对齐，
        // 在右半边就整叠右对齐；显式左 / 右对齐也统一用同一个公式，
        // 保证松手时被抓卡正好停在落点，其余卡片向它看齐。
        string align = Settings.NormalizeCardAlign(settings.Style.CardAlign);
        double workWidthDip = screen.WorkWidth / (screen.ScaleX > 0.1 ? screen.ScaleX : 1.0);
        if (align == "Auto")
            align = cardCenterX < workWidthDip / 2 ? "Left" : "Right";

        settings.Overlay.X = align == "Right"
            ? landedX + cardWidth - stackWidth
            : landedX;

        settings.Overlay.Monitor = monitor;

        ClampAnchorToWorkArea(slot, settings, landedX, landedY);

        // 松手后才重排：把本次解析出的左 / 右对齐交给协调器，其余卡片用 180ms ease-out
        // 跟过去；被拖卡按公式已经落在落点。CardMotion.Enabled=false 时直接落位。
        _layout.EndDrag(slot, align);
        SaveSettings();

        _host.SyncSettingsWindowAnchor(settings.Overlay.X, settings.Overlay.Y, settings.Overlay.Monitor);

        Log.Info($"叠层移动，锚点={settings.Overlay.X},{settings.Overlay.Y} 显示器={monitor}");
    }

    /// <summary>
    /// 拖动松手后把锚点钳在目标显示器工作区内：至少保留一部分卡片可见，
    /// 并在离边缘 16px 内吸附。只影响锚点，不改变卡片顺序 / 间距。
    /// </summary>
    void ClampAnchorToWorkArea(CardSlot slot, Settings settings, double landedX, double landedY)
    {
        if (slot is null || slot.Window is null) return;

        try
        {
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0) return;

            int index = Math.Max(0, Math.Min(settings.Overlay.Monitor, screens.Length - 1));
            System.Drawing.Rectangle work = screens[index].WorkingArea;

            ScreenInfo screen = ScreenGeometry.Get(index);
            if (screen is null) return;

            double scaleX = screen.ScaleX > 0.1 ? screen.ScaleX : 1.0;
            double scaleY = screen.ScaleY > 0.1 ? screen.ScaleY : 1.0;

            double workWidth = work.Width / scaleX;
            double workHeight = work.Height / scaleY;
            if (workWidth < 40 || workHeight < 40) return;

            double cardWidth = Math.Max(40, slot.Window.CardWidth);
            double cardHeight = Math.Max(40, slot.Window.CardHeight);
            double visible = Math.Min(80, workWidth / 3.0);

            // 右对齐时 settings.Overlay.X 是整叠左锚点，不是被抓卡片的左边缘。
            // 以前直接夹锚点，宽卡拖到右边会被拉回屏幕中部；这里按被抓卡片的实际矩形夹取，
            // 再把修正量回写到锚点。
            // 拖动过程中 Dialog.Overlay.X/Y 仍是旧锚点，不能用；落点由 OnCardMoved
            // 按目标屏 WorkLeft 反算成 DIP 后传进来。多显示器时这正是旧实现会算错的地方。
            double cardX = landedX;
            double cardY = landedY;
            double minX = visible - cardWidth;
            double maxX = workWidth - visible;
            double minY = 0;
            double maxY = Math.Max(0, workHeight - 40);

            double x = cardX;
            double y = cardY;
            if (x < minX + 16) x = minX;
            else if (x > maxX - 16) x = maxX;
            if (y < minY + 16) y = minY;
            else if (y > maxY - 16) y = maxY;

            double dx = x - cardX;
            double dy = y - cardY;
            if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
            {
                settings.Overlay.X += dx;
                settings.Overlay.Y += dy;
                Log.Info($"叠层位置贴边吸附，卡片={Math.Round(x)},{Math.Round(y)} 锚点={Math.Round(settings.Overlay.X)},{Math.Round(settings.Overlay.Y)}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"叠层边缘吸附失败，保留原锚点: {ex.Message}");
        }
    }

    /// <summary>写设置文件并如实记录失败；调用方（设置窗 / 卡片宿主）按返回值提示（P0-2）。</summary>
    public bool SaveSettings()
    {
        try
        {
            bool saved = ConfigStore.Save(_host.Settings);
            if (!saved)
                Log.Warn($"设置仅内存生效，写入磁盘失败: {(ConfigStore.LastSaveError ?? "原因未知")}");
            return saved;
        }
        catch (Exception ex)
        {
            // ConfigStore.Save 设计上不抛；这里兜底，避免“保存失败”升级成 UI 线程未捕获异常。
            Log.Error("保存设置时出现未预期异常", ex);
            return false;
        }
    }

    public void ApplyAutoStart()
    {
        AutoStart.CleanLegacy();

        bool enabled = _host.Settings.Behavior.AutoStart;
        if (AutoStart.IsEnabled() != enabled)
        {
            if (!AutoStart.Set(enabled, _host.Settings.Behavior.StartMinimized))
                LastApplyWarning = $"开机自启{(enabled ? "开启" : "关闭")}失败：{AutoStart.LastError ?? "未知原因"}";
        }
        else if (enabled)
        {
            // 已开启时重写一次，保证 exe 路径 / --minimized 跟随当前设置。
            if (!AutoStart.Set(true, _host.Settings.Behavior.StartMinimized))
                LastApplyWarning = $"开机自启更新失败：{AutoStart.LastError ?? "未知原因"}";
        }

        if (!string.IsNullOrWhiteSpace(LastApplyWarning))
            Log.Warn($"设置已应用，但{LastApplyWarning}");
    }

    public void ToggleAutoStart()
    {
        _host.Settings.Behavior.AutoStart = !_host.Settings.Behavior.AutoStart;
        ApplyAutoStart();
        SaveSettings();
        _host.RefreshTray();
    }

    /// <summary>非常驻卡片的呼出 / 收起。</summary>
    public void ToggleCard(CardSlot slot)
    {
        if (slot is null) return;

        if (slot.Module.IsInteractive)
        {
            _host.ToggleCommandCard(slot);
            return;
        }

        if (!_host.OverlayVisible)
        {
            _host.OverlayVisible = true;
            _host.RefreshVisibility();
        }

        slot.Summoned = !slot.Summoned;

        if (slot.Summoned)
        {
            _host.ShowCard(slot, _render.RenderCardText(slot), true);
            slot.Window.EnsureTopmost();
        }
        else
        {
            _host.HideCard(slot, true);
        }

        Log.Info($"卡片「{slot.Config.Name}」{(slot.Summoned ? "已呼出" : "已收起")}");
        _host.RefreshTray();
    }
}
