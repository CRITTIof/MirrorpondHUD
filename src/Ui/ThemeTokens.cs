using System;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 外观令牌：颜色都是**带 alpha 的最终值**，控件直接拿来画，不再自己乘 alpha。
    /// 软色（Muted / Border / Panel / Scrollbar）想半透明时在配置里写 <c>#AARRGGBB</c>。
    /// </summary>
    public sealed class ThemeTokens
    {
        public Color Accent;
        public Color Danger;
        public Color Muted;
        public Color Border;
        public Color Panel;
        public Color Scrollbar;
        public Color ScrollbarHot;
        public string MonoFont;
    }

    /// <summary>
    /// 令牌解析：配置里填了就用填的，没填就按前景色派生。
    /// 派生值刻意等于旧代码里的 alpha 常量（0.45 / 0.10 / 0.045 / 0.18 / 0.40），
    /// 所以「只把硬编码换成令牌」这一步不会改变任何默认观感。
    /// </summary>
    public static class ThemeResolver
    {
        public const string DefaultAccent = "#2F6FEB";
        public const string DefaultDanger = "#D14343";
        public const string DefaultMonoFont = "Cascadia Mono, Consolas, Microsoft YaHei UI";

        public static ThemeTokens Resolve(StyleSettings style)
        {
            StyleSettings s = style ?? new StyleSettings();
            Color foreground = Parse(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            return new ThemeTokens
            {
                Accent = Parse(s.Accent, Color.FromRgb(0x2F, 0x6F, 0xEB)),
                Danger = Parse(s.Danger, Color.FromRgb(0xD1, 0x43, 0x43)),
                Muted = Parse(s.Muted, WithAlpha(foreground, 0.45)),
                Border = Parse(s.Border, WithAlpha(foreground, 0.10)),
                Panel = Parse(s.Panel, WithAlpha(foreground, 0.045)),
                Scrollbar = Parse(s.Scrollbar, WithAlpha(foreground, 0.18)),
                ScrollbarHot = Parse(s.ScrollbarHot, WithAlpha(foreground, 0.40)),
                MonoFont = string.IsNullOrWhiteSpace(s.MonoFont) ? DefaultMonoFont : s.MonoFont.Trim()
            };
        }

        public static Color WithAlpha(Color color, double alpha)
        {
            byte a = (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255.0);
            return Color.FromArgb(a, color.R, color.G, color.B);
        }

        /// <summary>空串算合法（= 派生）；非空时必须是 #RRGGBB 或 #AARRGGBB。</summary>
        public static bool IsValidColor(string hex)
        {
            return Settings.IsColorToken(hex);
        }

        static Color Parse(string hex, Color fallback)
        {
            string value = (hex ?? "").Trim();
            if (value.Length == 0) return fallback;

            try { return OverlaySurface.ParseColor(value); }
            catch { return fallback; }
        }
    }
}
