using System;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 卡片动效的集中参数。2026-11 动效批次（A 方案：克制专业）：
    /// · 拖动时其他卡片同位移刚性跟随；
    /// · 松手后的补位 / 自动对齐翻边 180ms ease-out；
    /// · 呼出 / 消失 160ms 淡入淡出 + 8px 位移；
    /// · 截图 / 自检模式关闭动效，避免离屏验收抓到中间帧。
    /// </summary>
    internal static class CardMotion
    {
        public const int LayoutDurationMs = 180;
        public const int ShowDurationMs = 160;
        public const int HideDurationMs = 160;
        public const double ShowOffset = 8;
        public const double HideOffset = 8;

        /// <summary>窗口位置动画的刷新间隔；60Hz 出头，和 Dispatcher Render 同量级。</summary>
        public const int FrameIntervalMs = 15;

        static bool _enabled = true;

        /// <summary>截图 / --selftest 等离屏模式会关掉，正常运行时保持开启。</summary>
        public static bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        /// <summary>三次 ease-out：起步快、收尾稳，适合卡片补位这种短距离移动。</summary>
        public static double EaseOut(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double inv = 1 - t;
            return 1 - inv * inv * inv;
        }
    }
}
