using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 卡片里的图标：使用内嵌的 Lucide 字体（ISC），自动设置字体族，颜色 / 大小可跟卡片外观走。
///
/// <code>
/// var icon = new CardIcon(LucideIcons.Cpu) { Size = 20 };
/// icon.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
/// </code>
/// </summary>
public sealed class CardIcon : TextBlock, ICardFormControl
{
    // Size 与 FontSize 必须同源（K-38）：存一份数值，setter 才能判断是否真的变了。
    double _size = 16;

    public CardIcon(string icon = null, double size = 16)
    {
        Text = icon ?? "";
        Size = size;
        FontFamily = LucideIcons.FontFamily;
        FontSize = size;
        TextWrapping = TextWrapping.NoWrap;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        SnapsToDevicePixels = true;
        ApplyStyle(null, 8);
    }

    /// <summary>Lucide 字形，推荐用 <see cref="LucideIcons"/> 里的常量或 <see cref="LucideIcons.Get"/>。</summary>
    public string Icon
    {
        get => Text ?? ""; set => Text = value ?? "";
    }

    /// <summary>图标字号（DIP）；默认 16。改它会同步 <see cref="TextBlock.FontSize"/>（K-38）。</summary>
    public double Size
    {
        get => _size;
        set
        {
            double next = Math.Max(6, value);
            if (Math.Abs(_size - next) < 1e-9) return;
            _size = next;

            // K-38：Size 与字号是同一个度量，运行期改 Size 必须立刻生效，
            // 否则调用方还得再 ApplyStyle 一次才看得出变化。
            FontSize = next;
            InvalidateMeasure();
        }
    }

    /// <summary>套用卡片字体 / 前景色；Lucide 字体族始终固定，不会被正文设置覆盖。</summary>
    public void ApplyStyle(StyleSettings style, double cornerRadius)
    {
        StyleSettings s = style ?? new StyleSettings();
        Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

        FontFamily = LucideIcons.FontFamily;
        FontSize = Math.Max(6, Size);
        Foreground = CardPalette.Brush(foreground, 0.86);
    }
}
