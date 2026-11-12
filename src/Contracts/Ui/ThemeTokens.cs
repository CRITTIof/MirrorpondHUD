using System;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 外观令牌：颜色都是**带 alpha 的最终值**，控件直接拿来画，不再自己乘 alpha。
/// 软色（Muted / Border / Panel / Scrollbar）想半透明时在配置里写 <c>#AARRGGBB</c>。
/// </summary>
public sealed class ThemeTokens
{
    public Color Accent;
    public Color Danger;
    /// <summary>M3 成功色；空配置时等于 Accent（默认观感不变）。</summary>
    public Color Success;
    /// <summary>M3 警告色；空配置时等于前景 0.9（默认观感不变）。</summary>
    public Color Warning;
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
        Color foreground = ParseColor(s.Foreground, Colors.White);
        Color accent = ParseColor(s.Accent, Color.FromRgb(0x2F, 0x6F, 0xEB));

        return new ThemeTokens
        {
            Accent = accent,
            Danger = ParseColor(s.Danger, Color.FromRgb(0xD1, 0x43, 0x43)),
            // 空串 = 旧行为：Success 跟着 Accent，Warning 是前景 0.9。
            Success = ParseColor(s.Success, accent),
            Warning = ParseColor(s.Warning, WithAlpha(foreground, 0.9)),
            Muted = ParseColor(s.Muted, WithAlpha(foreground, 0.45)),
            Border = ParseColor(s.Border, WithAlpha(foreground, 0.10)),
            Panel = ParseColor(s.Panel, WithAlpha(foreground, 0.045)),
            Scrollbar = ParseColor(s.Scrollbar, WithAlpha(foreground, 0.18)),
            ScrollbarHot = ParseColor(s.ScrollbarHot, WithAlpha(foreground, 0.40)),
            MonoFont = string.IsNullOrWhiteSpace(s.MonoFont) ? DefaultMonoFont : s.MonoFont.Trim()
        };
    }

    static Color ParseColor(string hex, Color fallback)
    {
        if (!ColorCode.TryParse(hex, out var a, out var r, out var g, out var b)) return fallback;
        return Color.FromArgb(a, r, g, b);
    }

    public static Color WithAlpha(Color color, double alpha)
    {
        byte a = (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255.0);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    /// <summary>空串算合法（= 派生）；非空时必须是 #RRGGBB 或 #AARRGGBB。</summary>
    public static bool IsValidColor(string hex) => Settings.IsColorToken(hex);
}
