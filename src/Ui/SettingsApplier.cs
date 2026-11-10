using System;
using System.Collections.Generic;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
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
        void OnCardDragging(CardSlot slot, double dx, double dy);
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
        double _dragLastDx;
        double _dragLastDy;

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

        public bool Apply(Settings settings)
        {
            _host.ExitCommandLine();

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

            // 内存里的设置已经生效；落盘失败时必须如实告诉窗口，不能谎报成功（P0-2）。
            bool saved = SaveSettings();

            _host.UpdateEngine();
            _host.RebuildHotkeys();
            ApplyAutoStart();
            _host.RefreshTray();
            _host.UpdateCommandInfo();
            _host.RefreshVisibility();
            _render.RenderNow();

            Log.Info("设置已应用（卡片 " + _host.Cards.Count + " 张" + (saved ? "" : "，未写入磁盘") + "）");
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
                CardSlot slot;
                if (existing.TryGetValue(config.Id, out slot))
                {
                    existing.Remove(config.Id);

                    // 呼出状态跨设置应用保留：没在这次快照里出现的卡片沿用当前值。
                    bool summonedNow;
                    if (!summoned.TryGetValue(config.Id, out summonedNow)) summonedNow = slot.Summoned;

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
                if (card.Window != null) card.Window.StopPositionMotion();
            }
        }

        /// <summary>
        /// 拖动中：被拖窗口已经由 OverlayWindow 自己移动，这里把其余已显示窗口按总位移的
        /// 增量平移，形成「整叠跟着走」；锚点保持不动，松手后才落盘并做补位/对齐动画。
        /// </summary>
        public void OnCardDragging(CardSlot slot, double dx, double dy)
        {
            if (slot == null || !ReferenceEquals(slot, _dragSlot)) return;

            double stepX = dx - _dragLastDx;
            double stepY = dy - _dragLastDy;
            _dragLastDx = dx;
            _dragLastDy = dy;

            if (Math.Abs(stepX) < 0.01 && Math.Abs(stepY) < 0.01) return;

            foreach (CardSlot card in _host.Cards)
            {
                if (ReferenceEquals(card, slot) || !card.Shown || card.Window == null) continue;
                card.Window.Left += stepX;
                card.Window.Top += stepY;
            }
        }

        /// <summary>拖拽整叠卡片后的新锚点：动画重排、落盘，并同步设置窗里的位置显示。</summary>
        public void OnCardMoved(CardSlot slot, double dx, double dy, int monitor)
        {
            if (slot == null) return;

            _dragSlot = null;
            _dragLastDx = 0;
            _dragLastDy = 0;

            Settings settings = _host.Settings;
            settings.Overlay.X += dx;
            settings.Overlay.Y += dy;
            settings.Overlay.Monitor = monitor;

            // 松手后才重排：自动对齐可能从屏幕一侧翻到另一侧，这里用 180ms ease-out 让
            // 其余卡片跟过去；被拖卡也从落点滑到最终对齐位。CardMotion.Enabled=false 时
            // ApplyPosition 会直接落位。
            _layout.LayoutAnimated();
            SaveSettings();

            _host.SyncSettingsWindowAnchor(settings.Overlay.X, settings.Overlay.Y, settings.Overlay.Monitor);

            Log.Info("叠层移动，锚点=" + settings.Overlay.X + "," + settings.Overlay.Y + " 显示器=" + monitor);
        }

        /// <summary>写设置文件并如实记录失败；调用方（设置窗 / 卡片宿主）按返回值提示（P0-2）。</summary>
        public bool SaveSettings()
        {
            bool saved = ConfigStore.Save(_host.Settings);
            if (!saved)
                Log.Warn("设置仅内存生效，写入磁盘失败: " + (ConfigStore.LastSaveError ?? "原因未知"));
            return saved;
        }

        public void ApplyAutoStart()
        {
            AutoStart.CleanLegacy();

            bool enabled = _host.Settings.Behavior.AutoStart;
            if (AutoStart.IsEnabled() != enabled)
                AutoStart.Set(enabled, _host.Settings.Behavior.StartMinimized);
            else if (enabled)
                AutoStart.Set(true, _host.Settings.Behavior.StartMinimized);
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
            if (slot == null) return;

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

            Log.Info("卡片「" + slot.Config.Name + "」" + (slot.Summoned ? "已呼出" : "已收起"));
            _host.RefreshTray();
        }
    }
}
