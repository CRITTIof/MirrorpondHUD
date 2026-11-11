using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 卡片底部的系统页脚：上方一条发丝线，左侧短状态，右侧版本 / 显示器。
/// 规范 M6 的“系统页脚”落地；没有网格或大色块。
/// </summary>
public sealed class CardFooter : Grid
{
    readonly Border _rule;
    readonly TextBlock _left;
    readonly TextBlock _right;

    public CardFooter()
    {
        MinHeight = 20;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 5) };
        Children.Add(_rule);

        var line = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _left = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        _right = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        line.Children.Add(_left);
        line.Children.Add(_right);
        SetColumn(_right, 1);

        Children.Add(line);
        SetRow(line, 1);
    }

    public string Left
    {
        get => _left.Text ?? "";
        set => _left.Text = value ?? "";
    }

    public string Right
    {
        get => _right.Text ?? "";
        set => _right.Text = value ?? "";
    }

    public void ApplyStyle(StyleSettings style)
    {
        StyleSettings s = style ?? new StyleSettings();
        ThemeTokens tokens = ThemeResolver.Resolve(s);
        Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
        FontFamily mono = new FontFamily(tokens.MonoFont);

        double size = Math.Max(8, s.FontSize - 3);
        _left.FontFamily = mono;
        _right.FontFamily = mono;
        _left.FontSize = size;
        _right.FontSize = size;
        _left.Foreground = CardPalette.Brush(foreground, 0.42);
        _right.Foreground = CardPalette.Brush(foreground, 0.34);
        _rule.Background = CardPalette.Brush(foreground, 0.10);
        _left.Visibility = string.IsNullOrEmpty(_left.Text) ? Visibility.Collapsed : Visibility.Visible;
        _right.Visibility = string.IsNullOrEmpty(_right.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
