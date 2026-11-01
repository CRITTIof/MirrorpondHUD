using System;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>这一帧由谁提供模糊。</summary>
    internal enum GlassBackdropKind
    {
        None,
        SystemAcrylic,
        DesktopAcrylic,
        CompatCapture
    }

    /// <summary>
    /// 「选哪种毛玻璃实现」的纯决策：设置 + 两块底板的可用性 → 实现。
    /// 不碰窗口、不做 IO，方便自检覆盖；窗口只按结果 Attach / Retire。
    /// </summary>
    internal static class GlassPolicy
    {
        public static GlassBackdropKind Resolve(bool acrylic, string mode, bool systemAvailable, bool desktopAvailable)
        {
            if (!acrylic) return GlassBackdropKind.None;

            switch (Settings.NormalizeGlassMode(mode))
            {
                case "Dwm":
                    if (desktopAvailable) return GlassBackdropKind.DesktopAcrylic;
                    return systemAvailable ? GlassBackdropKind.SystemAcrylic : GlassBackdropKind.CompatCapture;

                case "System":
                    return systemAvailable ? GlassBackdropKind.SystemAcrylic : GlassBackdropKind.CompatCapture;

                default:
                    // 兼容实现（截屏拼接）没有底板，窗口自己取样。
                    return GlassBackdropKind.CompatCapture;
            }
        }
    }

    /// <summary>
    /// 一块底板：接管窗口背后的模糊。两种实现互斥——同一时刻只该有一块 active。
    /// <see cref="Apply"/> 返回 false 表示这块用不了（调用方退回兼容取样）。
    /// </summary>
    internal interface IGlassBackdrop
    {
        bool IsActive { get; }

        bool IsUnavailable { get; }

        bool Apply(OverlayWindow window, out string reason);

        void Retire(OverlayWindow window);
    }

    /// <summary>
    /// 系统亚克力：卡片下面一个独立底板窗口，挂未文档化的 accent 亚克力，
    /// DWM / GPU 负责模糊，进程侧零逐帧工作。底板窗口留着复用，切走时只 Follow 成“不跟”。
    /// </summary>
    internal sealed class SystemAcrylicBackdrop : IGlassBackdrop
    {
        GlassBackdrop _backdrop;
        bool _active;
        bool _unavailable;

        public bool IsActive { get { return _active; } }

        public bool IsUnavailable { get { return _unavailable; } }

        public bool Apply(OverlayWindow window, out string reason)
        {
            reason = null;
            if (_unavailable) return false;

            if (_backdrop == null)
            {
                string why;
                if (!GlassBackdrop.TryCreate(out _backdrop, out why))
                {
                    _unavailable = true;
                    reason = why;
                    return false;
                }

                Log.Info("毛玻璃改用系统亚克力底板：模糊在 DWM / GPU 上完成，进程侧不再抓屏");
            }

            if (!_active)
            {
                _active = true;
                Log.Info("毛玻璃背景已启用（系统亚克力底板，模糊半径由系统决定）");
            }

            _backdrop.SetTint(OverlaySurface.ParseColor(window.GlassTintColor));
            _backdrop.Follow(window, window.GlassShadow, window.GlassVisible);
            return true;
        }

        public void Retire(OverlayWindow window)
        {
            if (!_active) return;

            _active = false;
            if (_backdrop != null) _backdrop.Follow(window, window.GlassShadow, false);
        }

        /// <summary>设置变了就再给它一次机会（底板窗口保留，不重建）。</summary>
        public void ResetAvailability()
        {
            _unavailable = false;
        }

        public void Dispose()
        {
            if (_backdrop == null) return;

            try { _backdrop.Dispose(); } catch { }
            _backdrop = null;
        }
    }

    /// <summary>
    /// 桌面亚克力：截屏 + 自己模糊，把结果画进卡片自己的内容层（<see cref="GlassFeed"/>）。
    /// 代价是卡片必须排除在截屏之外，否则取样会把自己拍进去。
    /// </summary>
    internal sealed class DesktopAcrylicBackdrop : IGlassBackdrop
    {
        bool _active;
        bool _unavailable;
        bool _permanent;
        int _failures;
        DateTime _failedAt = DateTime.MinValue;
        long _healthyAtFailure;
        DateTime _lastExclude = DateTime.MinValue;

        public bool IsActive { get { return _active; } }

        /// <summary>
        /// 这块底板还能不能用。失败分两种：
        /// 1) 环境本身不支持（远程桌面）—— 一直不可用，等设置重新应用；
        /// 2) 取样连续全黑（锁屏 / 切换显示器 / 瞬间读不到屏幕）—— 冷却几秒后自动再试，
        ///    期间只要别的卡片恢复正常取样就立刻重试。以前这里是「一次失败永久跳过」，
        ///    一次瞬态黑屏就能把单张卡永远钉在系统亚克力上，直到用户改设置。
        /// </summary>
        public bool IsUnavailable
        {
            get
            {
                if (!_unavailable) return false;
                if (_permanent) return true;
                if (GlassFeed.HealthyRounds != _healthyAtFailure) return false;  // 取样已恢复：马上重试
                return (DateTime.UtcNow - _failedAt).TotalSeconds < RetrySeconds;
            }
        }

        /// <summary>3s / 6s / 12s / 15s 封顶：环境真读不到屏幕时不来回抖，恢复后最多 15 秒内回到桌面亚克力。</summary>
        double RetrySeconds
        {
            get { return Math.Min(15.0, 3.0 * Math.Pow(2, Math.Max(1, Math.Min(_failures, 5)) - 1)); }
        }

        public bool Apply(OverlayWindow window, out string reason)
        {
            reason = null;

            if (_unavailable)
            {
                if (IsUnavailable) return false;        // 还在冷却：让系统亚克力继续顶着
                // 失败之后屏幕取样恢复过，说明只是瞬态；从头开始计失败次数。
                if (GlassFeed.HealthyRounds != _healthyAtFailure) _failures = 0;
                _unavailable = false;
            }

            string why;
            if (!GlassFeed.IsAvailable(out why))
            {
                _unavailable = true;
                _permanent = true;
                reason = why;
                return false;
            }

            bool first = !_active;
            _active = true;

            // 取样的是屏幕：卡片（连同 owner 是它的子窗口，比如视频窗）必须先排除在截屏之外。
            // 这活儿要 EnumWindows 全表 + 逐个 SetWindowDisplayAffinity，拖动时每个鼠标移动都会走到这里，
            // 不节流会把 UI 线程占满、取样反而停住。2 秒重设一次足够覆盖新建的子窗口。
            if (first || (DateTime.UtcNow - _lastExclude).TotalSeconds >= 2.0)
            {
                GlassFeed.ExcludeFromCapture(window, true);
                _lastExclude = DateTime.UtcNow;
            }

            GlassFeed.Attach(window, window.OnDesktopAcrylicUnavailable);

            if (first) Log.Info("毛玻璃背景已启用（桌面亚克力：截屏 + 模糊后画进卡片，卡片不进截图）");
            return true;
        }

        public void Retire(OverlayWindow window)
        {
            if (!_active) return;

            _active = false;
            _lastExclude = DateTime.MinValue;
            GlassFeed.Detach(window);
            GlassFeed.ExcludeFromCapture(window, false);
        }

        /// <summary>整叠取样全黑时由窗口标记：冷却一段时间再试，而不是永久放弃。</summary>
        public void MarkUnavailable()
        {
            _unavailable = true;
            _failedAt = DateTime.UtcNow;
            _healthyAtFailure = GlassFeed.HealthyRounds;
            if (_failures < int.MaxValue) _failures++;
        }

        /// <summary>设置变了就重置失败历史，再给它一次机会。</summary>
        public void ResetAvailability()
        {
            _unavailable = false;
            _permanent = false;
            _failures = 0;
            _failedAt = DateTime.MinValue;
        }
    }
}
