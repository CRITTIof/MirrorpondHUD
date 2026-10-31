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
        DateTime _lastExclude = DateTime.MinValue;

        public bool IsActive { get { return _active; } }

        public bool IsUnavailable { get { return _unavailable; } }

        public bool Apply(OverlayWindow window, out string reason)
        {
            reason = null;
            if (_unavailable) return false;

            string why;
            if (!GlassFeed.IsAvailable(out why))
            {
                _unavailable = true;
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

        /// <summary>取样连续全黑时由窗口标记；之后再选到它就直接跳过。</summary>
        public void MarkUnavailable()
        {
            _unavailable = true;
        }

        /// <summary>设置变了就再给它一次机会。</summary>
        public void ResetAvailability()
        {
            _unavailable = false;
        }
    }
}
