using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗口的明暗调色板。主题资源都是可变 SolidColorBrush；切换时只改颜色，
/// 已经套用 StaticResource 的控件会立即跟着变，不需要重建窗口。
/// </summary>
internal static class SettingsTheme
{
    static readonly Dictionary<string, (Color Dark, Color Light)> Palette = new()
    {
        ["Accent"] = (Rgb(0xF2, 0xE9, 0x00), Rgb(0xB7, 0xA4, 0x00)),
        ["AccentSoft"] = (Rgb(0x26, 0x26, 0x0E), Rgb(0xF4, 0xF1, 0xC7)),
        ["AccentText"] = (Rgb(0x11, 0x11, 0x11), Rgb(0x11, 0x11, 0x11)),
        ["WindowBg"] = (Rgb(0x0A, 0x0C, 0x0D), Rgb(0xF2, 0xF2, 0xEC)),
        ["CardBg"] = (Rgb(0x11, 0x14, 0x16), Rgb(0xFF, 0xFF, 0xFF)),
        ["BorderSoft"] = (Rgb(0x2A, 0x2F, 0x32), Rgb(0xD8, 0xD8, 0xD0)),
        ["TextPrimary"] = (Rgb(0xF2, 0xF2, 0xED), Rgb(0x15, 0x15, 0x15)),
        ["TextSecondary"] = (Rgb(0x9A, 0xA0, 0xA4), Rgb(0x64, 0x64, 0x5C)),
        ["Danger"] = (Rgb(0xFF, 0x6B, 0x5E), Rgb(0xB8, 0x3A, 0x2E)),
        ["PanelBg"] = (Rgb(0x17, 0x1B, 0x1D), Rgb(0xF6, 0xF6, 0xF0)),
        ["HeaderBg"] = (Rgb(0x15, 0x19, 0x1B), Rgb(0xF8, 0xF8, 0xF2)),
        ["HeaderBorder"] = (Rgb(0x26, 0x2C, 0x2F), Rgb(0xE5, 0xE5, 0xDC)),
        ["BadgeBg"] = (Rgb(0x20, 0x26, 0x29), Rgb(0xEC, 0xEF, 0xE5)),
        ["HoverBg"] = (Rgb(0x1E, 0x24, 0x27), Rgb(0xEE, 0xEF, 0xE7)),
        ["PressedBg"] = (Rgb(0x26, 0x2D, 0x31), Rgb(0xE2, 0xE4, 0xDA)),
        ["AccentHover"] = (Rgb(0xFF, 0xF3, 0x4D), Rgb(0xC9, 0xB6, 0x00)),
        ["AccentPressed"] = (Rgb(0xD9, 0xD0, 0x00), Rgb(0x9E, 0x8D, 0x00)),
        ["DisabledBg"] = (Rgb(0x15, 0x18, 0x1A), Rgb(0xED, 0xED, 0xE6)),
        ["DisabledText"] = (Rgb(0x5E, 0x66, 0x6B), Rgb(0x9A, 0x9A, 0x90)),
        ["SelectedBg"] = (Rgb(0x22, 0x26, 0x0F), Rgb(0xF5, 0xF2, 0xC8)),
        ["SelectedStrongBg"] = (Rgb(0x2E, 0x33, 0x13), Rgb(0xEB, 0xE6, 0xA8)),
        ["InputBg"] = (Rgb(0x0E, 0x11, 0x13), Rgb(0xFF, 0xFF, 0xFF)),
        ["PopupBg"] = (Rgb(0x12, 0x15, 0x17), Rgb(0xFF, 0xFF, 0xFF)),
        ["PreviewBg"] = (Rgb(0x0B, 0x10, 0x20), Rgb(0x0B, 0x10, 0x20)),
        // 主行动按钮：暗色是"黑底信号黄字"，亮色才是"信号黄底黑字"；
        // 2.40 的暗色亮黄底在深色窗里像一块灯箱，2.41 反转。
        ["PrimaryBg"] = (Rgb(0xF2, 0xE9, 0x00), Rgb(0xF2, 0xE9, 0x00)),
        ["PrimaryText"] = (Rgb(0x11, 0x11, 0x11), Rgb(0x11, 0x11, 0x11)),
        ["PrimaryBorder"] = (Rgb(0xF2, 0xE9, 0x00), Rgb(0xD9, 0xD0, 0x00)),
        ["PrimaryHover"] = (Rgb(0xFF, 0xF3, 0x4D), Rgb(0xFF, 0xF3, 0x4D)),
        ["PrimaryPressed"] = (Rgb(0xD9, 0xD0, 0x00), Rgb(0xD9, 0xD0, 0x00)),
        ["SliderTrack"] = (Rgb(0x2A, 0x2F, 0x32), Rgb(0xD8, 0xD8, 0xD0)),
        ["Scrollbar"] = (Rgb(0x3A, 0x41, 0x45), Rgb(0xC9, 0xC9, 0xC0)),
        ["ScrollbarHot"] = (Rgb(0x5A, 0x64, 0x6A), Rgb(0x9C, 0x9C, 0x91))
    };

    /// <summary>X-10：调色板键集合；自检用它断言 Theme.xaml 没有漏键。</summary>
    internal static IEnumerable<string> PaletteKeys => Palette.Keys;

    public static bool IsDark(string value) =>
        string.Equals(Settings.NormalizeSettingsTheme(value), "Dark", StringComparison.OrdinalIgnoreCase);

    public static void Apply(bool dark, string previewBg = null)
    {
        ResourceDictionary resources = Application.Current?.Resources;
        if (resources is null) return;

        foreach (KeyValuePair<string, (Color Dark, Color Light)> pair in Palette)
        {
            Color color = dark ? pair.Value.Dark : pair.Value.Light;
            // X-10：优先改已有画刷的 Color —— StaticResource 与 DynamicResource 消费者都会刷新。
            // 只有资源缺失或画刷被冻结时才替换实例。
            if (resources[pair.Key] is SolidColorBrush brush && !brush.IsFrozen)
                brush.Color = color;
            else
                resources[pair.Key] = new SolidColorBrush(color);
        }

        // 主题缩略图衬底允许用户单独调；非法值已在 Settings.Normalize 兜底，
        // 这里再判一次是为了兼容设置窗打开期间的半成品输入。
        if (!string.IsNullOrWhiteSpace(previewBg) && Settings.IsHexColor(previewBg))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(previewBg.Trim());
                resources["PreviewBg"] = new SolidColorBrush(color);
            }
            catch (Exception ex)
            {
                Log.Warn($"设置窗预览底色解析失败（{previewBg}）: {ex.Message}");
            }
        }
    }

    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
