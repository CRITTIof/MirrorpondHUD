using System;
using System.Collections.Generic;
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

    // K-40：描边效果按（颜色 + 模糊半径）缓存并冻结。冻结的 Freezable 是线程安全的，
    // 所以可以跨控件、跨线程共享；以前每个 TextBlock、每次换肤都会 new 一个等价 Effect。
    static readonly Dictionary<string, DropShadowEffect> EffectCache = new(StringComparer.Ordinal);

    // 记录“这个元素当前贴的是哪个参数的效果”，参数没变就完全跳过赋值。
    static readonly DependencyProperty OutlineKeyProperty =
        DependencyProperty.RegisterAttached("OutlineKey", typeof(string), typeof(CardTextStyle),
            new PropertyMetadata(null));

    const int EffectCacheCapacity = 32;

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
            // K-40：文本控件内部的 Inline / ScrollViewer 不会再有需要补样式的 TextBlock，
            // 走到这里就停，避免在大段流式文本的整棵子树上来回遍历。
            return;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            Walk(VisualTreeHelper.GetChild(node, i), style, outline);
    }

    /// <summary>K-40：同一组（颜色、模糊半径）的描边效果只建一次，冻结后全局复用。</summary>
    static DropShadowEffect OutlineEffect(Color color, double blurRadius)
    {
        string key = $"{CardTemplates.Hex(color)}|{Math.Round(blurRadius, 3)}";
        lock (EffectCache)
        {
            if (EffectCache.TryGetValue(key, out var cached)) return cached;
        }

        var effect = new DropShadowEffect
        {
            Color = color,
            ShadowDepth = 0,
            Direction = 0,
            BlurRadius = blurRadius,
            Opacity = 0.9
        };
        effect.Freeze();

        lock (EffectCache)
        {
            if (EffectCache.Count >= EffectCacheCapacity) EffectCache.Clear();
            EffectCache[key] = effect;
        }
        return effect;
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
                element.ClearValue(OutlineKeyProperty);
            }
        }
        else
        {
            Color color = CardPalette.TryColor(style.OutlineColor, Colors.Black);
            double blur = Math.Max(1.0, style.OutlineThickness * 2.2);
            string key = $"{CardTemplates.Hex(color)}|{Math.Round(blur, 3)}";
            DropShadowEffect effect = OutlineEffect(color, blur);

            // 参数没变就不重贴：换肤 / 流式追加文本时完全不碰效果对象。
            if (!ReferenceEquals(element.Effect, effect) ||
                !string.Equals(element.GetValue(OutlineKeyProperty) as string, key, StringComparison.Ordinal))
            {
                element.Effect = effect;
                element.SetValue(OutlineKeyProperty, key);
            }
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
