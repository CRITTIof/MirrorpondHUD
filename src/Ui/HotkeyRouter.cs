using System;
using System.Collections.Generic;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>热键路由器需要宿主提供的动作。控制器实现它，路由器不直接抓控制器字段。</summary>
    internal interface IHotkeyHost
    {
        Settings Settings { get; }
        IList<CardSlot> Cards { get; }
        bool TrayVisible { get; }

        void ToggleVisibility();
        void ShowSettings();
        void ToggleLock();
        CardSlot FindCard(string id);
        void ToggleCard(CardSlot slot);
        void SaveSettings();
        void ReloadSettings();
        void ShowBalloon(string title, string text);
    }

    /// <summary>
    /// 全局热键 + 卡片呼出热键 + 卡片通用动作热键的注册 / 回退 / 分发。
    ///
    /// 从 <see cref="OverlayController"/> 抽出来：控制器只负责“启动时、应用设置后调 Register”，
    /// 这里负责热键被占用时的回退、写回实际组合键、托盘提示和动作路由。
    /// </summary>
    internal sealed class HotkeyRouter : IDisposable
    {
        readonly HotkeyManager _manager;
        readonly IHotkeyHost _host;

        public HotkeyRouter(HotkeyManager manager, IHotkeyHost host)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _manager.HotkeyPressed += OnHotkey;
        }

        /// <summary>设置变化 / 卡片变化后重新注册全部热键；先清空，删掉的卡片不占旧组合键。</summary>
        public void Register()
        {
            if (_manager == null) return;

            _manager.UnregisterAll();

            var changed = new List<string>(4);
            Settings settings = _host.Settings;

            string toggle = RegisterOne(HotkeyManager.ActionToggleOverlay, settings.Hotkeys.ToggleOverlay);
            string open = RegisterOne(HotkeyManager.ActionOpenSettings, settings.Hotkeys.OpenSettings);
            string lockKey = RegisterOne(HotkeyManager.ActionToggleLock, settings.Hotkeys.ToggleLock);

            if (toggle != settings.Hotkeys.ToggleOverlay)
            {
                changed.Add(settings.Hotkeys.ToggleOverlay + " → " + toggle);
                settings.Hotkeys.ToggleOverlay = toggle;
            }
            if (open != settings.Hotkeys.OpenSettings)
            {
                changed.Add(settings.Hotkeys.OpenSettings + " → " + open);
                settings.Hotkeys.OpenSettings = open;
            }
            if (lockKey != settings.Hotkeys.ToggleLock)
            {
                changed.Add(settings.Hotkeys.ToggleLock + " → " + lockKey);
                settings.Hotkeys.ToggleLock = lockKey;
            }

            if (changed.Count > 0)
            {
                Log.Warn("部分热键被占用，已自动改用：" + string.Join("，", changed));
                _host.SaveSettings();
                _host.ReloadSettings();
                _host.ShowBalloon("热键被占用，已自动改用", string.Join("  ", changed));
            }

            // 每张非常驻卡片一个呼出热键；相同组合键会合并成同一个注册，按下时同时呼出。
            int failed = 0;
            var changedCards = new List<string>(4);

            foreach (CardSlot slot in _host.Cards)
            {
                string action = HotkeyManager.CardAction(slot.Config.Id);
                string combination = slot.Config.Resident ? "" : slot.Config.Hotkey;

                if (string.IsNullOrWhiteSpace(combination))
                {
                    _manager.Register(action, "");
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
                _host.SaveSettings();
                _host.ReloadSettings();
                if (_host.TrayVisible)
                    _host.ShowBalloon("卡片热键被占用，已自动改用", string.Join("  ", changedCards));
            }

            if (failed > 0 && _host.TrayVisible)
            {
                _host.ShowBalloon("有 " + failed + " 张卡片的呼出热键注册失败",
                    "组合键可能被其它程序占用，可在「内容」页换一个");
            }

            RegisterCardActions();
        }

        /// <summary>卡片模块提供的通用热键（媒体卡的前进 / 后退 / 变速 / 播放暂停）。</summary>
        void RegisterCardActions()
        {
            var changed = new List<string>(4);
            Settings settings = _host.Settings;

            foreach (ICardModule module in CardRegistry.Modules)
            {
                var source = module as ICardHotkeySource;
                if (source == null) continue;

                bool used = false;
                foreach (CardSlot slot in _host.Cards)
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
                    string saved = settings.Hotkeys.GetCardAction(entry.Action);
                    if (string.IsNullOrWhiteSpace(saved)) saved = entry.DefaultHotkey;

                    string registered = RegisterOne(entry.Action, saved);
                    settings.Hotkeys.SetCardAction(entry.Action, registered);

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
                _host.SaveSettings();
                _host.ReloadSettings();
                _host.ShowBalloon("卡片热键被占用，已自动改用", string.Join("  ", changed));
            }
        }

        /// <summary>优先注册配置的组合键；被占用时退一步用 Ctrl+Alt+原键。空白 = 不启用。</summary>
        string RegisterOne(string action, string combination)
        {
            if (string.IsNullOrWhiteSpace(combination))
            {
                _manager.Register(action, "");
                return "";
            }

            if (_manager.Register(action, combination))
            {
                Log.Info("热键 " + combination + " 注册成功");
                return combination;
            }

            if (combination.IndexOf('+') < 0)
            {
                string fallback = "Ctrl+Alt+" + combination;
                if (_manager.Register(action, fallback))
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
                    _host.ToggleVisibility();
                    break;
                case HotkeyManager.ActionOpenSettings:
                    _host.ShowSettings();
                    break;
                case HotkeyManager.ActionToggleLock:
                    _host.ToggleLock();
                    break;
                default:
                    if (action.StartsWith("card:", StringComparison.Ordinal))
                    {
                        CardSlot slot = _host.FindCard(action.Substring(5));
                        if (slot != null) _host.ToggleCard(slot);
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

            foreach (CardSlot slot in _host.Cards)
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

        public void Dispose()
        {
            if (_manager != null) _manager.HotkeyPressed -= OnHotkey;
        }
    }
}
