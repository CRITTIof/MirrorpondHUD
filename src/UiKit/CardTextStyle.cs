using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 富内容卡的文字样式补丁：普通文字卡由 TextCanvas 画描边，富内容卡是真实控件，
/// 这里用 0 偏移的 DropShadow 给 TextBlock / TextBox 补一层柔和描边，并只清自己加过的效果。
/// </summary>
public static class CardTextStyle
{
    static readonly DependencyProperty OutlineAppliedProperty =
        DependencyProperty.RegisterAttached("OutlineApplied", typeof(bool), typeof(CardTextStyle),
            new PropertyMetadata(false));
    static readonly DependencyProperty BoldAppliedProperty =
        DependencyProperty.RegisterAttached("BoldApplied", typeof(bool), typeof(CardTextStyle),
            new PropertyMetadata(false));
    static readonly DependencyProperty LineAppliedProperty =
        DependencyProperty.RegisterAttached("LineApplied", typeof(bool), typeof(CardTextStyle),
            new PropertyMetadata(false));

    public static void ApplyOutline(StyleSettings style, DependencyObject root)
        => ApplyRichText(style, root);

    /// <summary>描边 + 加粗 + 行高：只补没有显式设置过的 TextBlock，保留控件自己的版式。</summary>
    public static void ApplyRichText(StyleSettings style, DependencyObject root)
    {
        if (root is null) return;

        bool outline = style is not null && style.TextOutline && style.OutlineThickness > 0.05;
        Walk(root, style ?? new StyleSettings(), outline);
    }

    public static void ApplyToElement(FrameworkElement element, StyleSettings style)
    {
        if (element is null) return;
        ApplyOne(element, style ?? new StyleSettings(),
            style is not null && style.TextOutline && style.OutlineThickness > 0.05);
    }

    static void Walk(DependencyObject node, StyleSettings style, bool outline)
    {
        if (node is TextBlock || node is TextBox)
        {
            ApplyOne((FrameworkElement)node, style, outline);
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            Walk(VisualTreeHelper.GetChild(node, i), style, outline);
    }

    static void ApplyOne(FrameworkElement element, StyleSettings style, bool outline)
    {
        bool applied = (bool)element.GetValue(OutlineAppliedProperty);

        if (!outline)
        {
            if (applied)
            {
                element.Effect = null;
                element.SetValue(OutlineAppliedProperty, false);
            }
        }
        else
        {
            Color color = CardPalette.TryColor(style.OutlineColor, Colors.Black);
            var effect = new DropShadowEffect
            {
                Color = color,
                ShadowDepth = 0,
                Direction = 0,
                BlurRadius = Math.Max(1.0, style.OutlineThickness * 2.2),
                Opacity = 0.9
            };
            effect.Freeze();
            element.Effect = effect;
            element.SetValue(OutlineAppliedProperty, true);
        }

        if (element is TextBlock text)
        {
            bool boldApplied = (bool)text.GetValue(BoldAppliedProperty);
            if (style.Bold)
            {
                if (text.ReadLocalValue(TextBlock.FontWeightProperty) == DependencyProperty.UnsetValue)
                {
                    text.FontWeight = FontWeights.SemiBold;
                    text.SetValue(BoldAppliedProperty, true);
                }
            }
            else if (boldApplied)
            {
                text.ClearValue(TextBlock.FontWeightProperty);
                text.SetValue(BoldAppliedProperty, false);
            }

            bool lineApplied = (bool)text.GetValue(LineAppliedProperty);
            if (style.LineHeight > 0)
            {
                if (text.ReadLocalValue(TextBlock.LineHeightProperty) == DependencyProperty.UnsetValue)
                {
                    text.LineHeight = style.LineHeight;
                    text.SetValue(LineAppliedProperty, true);
                }
            }
            else if (lineApplied)
            {
                text.ClearValue(TextBlock.LineHeightProperty);
                text.SetValue(LineAppliedProperty, false);
            }
        }
    }
}
