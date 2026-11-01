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
        /// <summary>一次注册的结果：设置里应该写回什么、是不是真的生效了。</summary>
        struct Bound
        {
            /// <summary>写回设置 / 卡片 / 通用动作的组合键；失败时是保留下来的旧键。</summary>
            public string Effective;
            /// <summary>新键（或回退键、空白不启用）已经生效。</summary>
            public bool Applied;
            /// <summary>原键被占用，实际生效的是回退键。</summary>
            public bool FellBack;
            /// <summary>注册失败时旧绑定仍然有效（HotkeyManager 保留下来的）。</summary>
            public bool HadOld;
        }

        readonly HotkeyManager _manager;
        readonly IHotkeyHost _host;

        // 每个动作「上一次真正生效」的组合键。注册失败时用它把设置恢复成旧键，
        // 保证 UI 显示的热键和实际生效的热键一致（2.13.4 审阅文档第 2 节）。
        readonly Dictionary<string, string> _effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public HotkeyRouter(HotkeyManager manager, IHotkeyHost host)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _manager.HotkeyPressed += OnHotkey;
        }

        /// <summary>
        /// 设置变化 / 卡片变化后重新注册全部热键。
        ///
        /// 这里**不能**先 UnregisterAll：<see cref="HotkeyManager.Register"/> 已经做到了
        /// “新键注册失败时保留旧绑定”，但路由器先把全部旧绑定清掉，那个保护就失效了——
        /// 用户把热键改成被占用的组合键时，旧热键仍然会丢（2.13.4 审阅文档第 2 节）。
        /// 现在改成逐动作“先注册新键、成功后再摘旧键”（摘旧键由 HotkeyManager 内部完成），
        /// 最后再统一清理删掉的卡片和不再使用的模块动作。
        /// </summary>
        public void Register()
        {
            if (_manager == null) return;

            var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changed = new List<string>(4);         // 原键被占用，已自动改用回退键
            var failed = new List<string>(4);          // 原键与回退键都失败，保留旧键
            var changedCards = new List<string>(4);
            var failedCards = new List<string>(4);
            var changedActions = new List<string>(4);
            var failedActions = new List<string>(4);

            Settings settings = _host.Settings;
            bool saved = false;

            saved |= BindGlobal(HotkeyManager.ActionToggleOverlay, settings.Hotkeys.ToggleOverlay,
                                value => settings.Hotkeys.ToggleOverlay = value,
                                desired, changed, failed);
            saved |= BindGlobal(HotkeyManager.ActionOpenSettings, settings.Hotkeys.OpenSettings,
                                value => settings.Hotkeys.OpenSettings = value,
                                desired, changed, failed);
            saved |= BindGlobal(HotkeyManager.ActionToggleLock, settings.Hotkeys.ToggleLock,
                                value => settings.Hotkeys.ToggleLock = value,
                                desired, changed, failed);

            // 每张非常驻卡片一个呼出热键；相同组合键会合并成同一个注册，按下时同时呼出。
            foreach (CardSlot slot in _host.Cards)
            {
                string action = HotkeyManager.CardAction(slot.Config.Id);
                string requested = slot.Config.Resident ? "" : slot.Config.Hotkey;

                Bound bound = Bind(action, requested, desired);

                if (!string.Equals(bound.Effective, requested ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    // 回退成功 → 写回实际键；失败 → 恢复旧键（没有旧键时保持用户输入）。
                    slot.Config.Hotkey = bound.Effective;
                    saved = true;
                }

                if (!bound.Applied)
                    failedCards.Add(slot.Config.Name + " " + DescribeFailure(requested, bound));
                else if (bound.FellBack)
                    changedCards.Add(slot.Config.Name + " → " + bound.Effective);
            }

            saved |= BindCardActions(desired, changedActions, failedActions);

            // 删卡 / 改成常驻 / 模块不再使用：把管理器里还挂着的旧动作摘掉。
            // 不能靠 UnregisterAll——那样会误伤这一轮仍然有效的旧绑定。
            DetachStale(desired);

            if (saved)
            {
                _host.SaveSettings();
                _host.ReloadSettings();
            }

            if (changed.Count > 0)
            {
                Log.Warn("部分热键被占用，已自动改用：" + string.Join("，", changed));
                _host.ShowBalloon("热键被占用，已自动改用", string.Join("  ", changed));
            }
            if (failed.Count > 0)
            {
                Log.Warn("部分热键注册失败，已保留原组合键：" + string.Join("，", failed));
                _host.ShowBalloon("热键注册失败，已保留原组合键", string.Join("  ", failed));
            }
            if (changedCards.Count > 0)
            {
                Log.Warn("部分卡片热键被占用，已自动改用：" + string.Join("，", changedCards));
                if (_host.TrayVisible)
                    _host.ShowBalloon("卡片热键被占用，已自动改用", string.Join("  ", changedCards));
            }
            if (failedCards.Count > 0)
            {
                Log.Warn("部分卡片热键注册失败：" + string.Join("，", failedCards));
                if (_host.TrayVisible)
                    _host.ShowBalloon("有 " + failedCards.Count + " 张卡片的呼出热键注册失败",
                        "组合键可能被其它程序占用，可在「内容」页换一个（原热键仍然有效）");
            }
            if (changedActions.Count > 0)
            {
                Log.Warn("部分卡片通用热键被占用，已自动改用：" + string.Join("，", changedActions));
                _host.ShowBalloon("卡片热键被占用，已自动改用", string.Join("  ", changedActions));
            }
            if (failedActions.Count > 0)
            {
                Log.Warn("部分卡片通用热键注册失败：" + string.Join("，", failedActions));
                if (_host.TrayVisible)
                    _host.ShowBalloon("有 " + failedActions.Count + " 个卡片热键注册失败",
                        "组合键可能被其它程序占用（原热键仍然有效）");
            }
        }

        /// <summary>注册一个全局动作；返回是否需要把结果写回设置。</summary>
        bool BindGlobal(string action, string requested, Action<string> store,
                        HashSet<string> desired, List<string> changed, List<string> failed)
        {
            requested = requested ?? "";
            Bound bound = Bind(action, requested, desired);

            if (!bound.Applied) failed.Add(DescribeFailure(requested, bound));
            else if (bound.FellBack) changed.Add(requested + " → " + bound.Effective);

            if (string.Equals(bound.Effective, requested, StringComparison.OrdinalIgnoreCase)) return false;

            store(bound.Effective);
            return true;
        }

        /// <summary>卡片模块提供的通用热键（媒体卡的前进 / 后退 / 变速 / 播放暂停）。</summary>
        bool BindCardActions(HashSet<string> desired, List<string> changed, List<string> failed)
        {
            Settings settings = _host.Settings;
            bool saved = false;

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
                    string stored = settings.Hotkeys.GetCardAction(entry.Action) ?? "";
                    string requested = stored;
                    if (string.IsNullOrWhiteSpace(requested)) requested = entry.DefaultHotkey ?? "";

                    Bound bound = Bind(entry.Action, requested, desired);

                    // 和旧行为一致：把实际键（含默认键 / 回退键 / 失败时保留的旧键）写进设置，
                    // 这样设置页显示的就是真正生效的组合键。
                    if (!string.Equals(bound.Effective, stored, StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Hotkeys.SetCardAction(entry.Action, bound.Effective);
                        saved = true;
                    }

                    if (!bound.Applied) failed.Add(entry.Label + " " + DescribeFailure(requested, bound));
                    else if (bound.FellBack) changed.Add(entry.Label + " " + requested + " → " + bound.Effective);
                }
            }

            return saved;
        }

        /// <summary>
        /// 给一个动作注册组合键，并记录实际生效的键。
        ///
        /// 关键：失败时**不**注销旧绑定——HotkeyManager.Register 已经保留了旧键，
        /// 这里只负责把设置恢复成旧键（有的话），而不是顺手把旧键一起清掉。
        /// </summary>
        Bound Bind(string action, string combination, HashSet<string> desired)
        {
            desired.Add(action);

            var result = new Bound { Effective = combination ?? "", Applied = false };

            if (string.IsNullOrWhiteSpace(combination))
            {
                // 空白 = 不启用；同时解除可能存在的旧绑定（清空热键 / 改成常驻都走这里）。
                _manager.Detach(action);
                _effective.Remove(action);
                result.Applied = true;
                return result;
            }

            if (_manager.Register(action, combination))
            {
                _effective[action] = combination;
                result.Effective = combination;
                result.Applied = true;
                Log.Info("热键 " + combination + " 注册成功");
                return result;
            }

            if (combination.IndexOf('+') < 0)
            {
                string fallback = "Ctrl+Alt+" + combination;
                if (_manager.Register(action, fallback))
                {
                    _effective[action] = fallback;
                    result.Effective = fallback;
                    result.Applied = true;
                    result.FellBack = true;
                    Log.Warn("热键 " + combination + " 被占用，改用 " + fallback);
                    return result;
                }
            }

            // 原键和回退键都失败：HotkeyManager 内部已经保留了旧绑定。
            // 把设置恢复成旧键，用户看到的热键才和真正能按的键一致；
            // 没有旧键（首次注册就失败）时保留用户输入，只提示本次未生效。
            string old;
            if (_effective.TryGetValue(action, out old) && !string.IsNullOrWhiteSpace(old))
            {
                result.Effective = old;
                result.HadOld = true;
            }
            else
            {
                result.Effective = combination;
            }

            Log.Warn("热键注册失败（多半被其它程序占用），保留旧绑定: " + combination + "（" + action + "）");
            return result;
        }

        /// <summary>这一轮不再需要的动作：从 HotkeyManager 里摘掉，避免删掉的卡片还占着组合键。</summary>
        void DetachStale(HashSet<string> desired)
        {
            var stale = new List<string>(4);
            foreach (string action in _effective.Keys)
            {
                if (!desired.Contains(action)) stale.Add(action);
            }

            foreach (string action in stale)
            {
                _manager.Detach(action);
                _effective.Remove(action);
                Log.Info("已解除不再使用 / 已删除卡片的热键: " + action);
            }
        }

        static string DescribeFailure(string requested, Bound bound)
        {
            return bound.HadOld
                ? requested + "（注册失败，仍使用 " + bound.Effective + "）"
                : requested + "（注册失败，本次未生效）";
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
