using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 卡片进度条的唯一实现（2.47.0）。
///
/// 以前媒体 / 中转站走 <see cref="CardStatus"/>，歌词卡自己拼一条 3px Border：
/// 后者既不吃「分段进度」开关，也没有进度条颜色令牌，改一处颜色要改两套代码。
/// 现在两种形态都收在这里：
///  · 分段（终末地 M5）：<see cref="CardSegmentBar"/>，28 段直角块，与仪表读数同一几何语言；
///  · 连续：3px 半圆端头，旧观感。
/// 只有这一个控件同时改值 / 换色，调用方拿 0–1 的进度直接赋值即可。
/// </summary>
public sealed class CardProgressBar : Grid
{
    readonly CardSegmentBar _segment;
    readonly Grid _legacy;
    readonly Border _track;
    readonly Border _fill;
    bool _segmented = true;
    double _value;

    public CardProgressBar()
    {
        SnapsToDevicePixels = true;

        _segment = new CardSegmentBar
        {
            Height = 4,
            Value = 0,
            Segments = 28,
            Gap = 2
        };

        _track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5) };
        _fill = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0
        };
        // 连续态只有 3px，而分段态 4px；垂直居中让 1px 差落在上下而不是整条贴顶。
        _legacy = new Grid { VerticalAlignment = VerticalAlignment.Center };
        _legacy.Children.Add(_track);
        _legacy.Children.Add(_fill);

        Children.Add(_legacy);
        Children.Add(_segment);
        SizeChanged += (s, e) => UpdateFillWidth();
    }

    /// <summary>0–1 的确定进度；越界自动夹紧。</summary>
    public double Value
    {
        get => _value;
        set
        {
            double next = Math.Max(0, Math.Min(1, value));
            if (Math.Abs(_value - next) < 1e-9) return;
            _value = next;
            _segment.Value = next;
            UpdateFillWidth();
        }
    }

    /// <summary>true = 直角分段；false = 3px 连续条。</summary>
    public bool Segmented => _segmented;

    /// <summary>轨道画刷（连续 / 分段共用）。</summary>
    public Brush TrackBrush
    {
        get => _track.Background;
        set
        {
            _track.Background = value;
            _segment.TrackBrush = value;
        }
    }

    /// <summary>填充画刷。状态色覆盖由调用方决定（<see cref="CardStatus"/> 按 Kind 换色）。</summary>
    public Brush FillBrush
    {
        get => _fill.Background;
        set
        {
            _fill.Background = value;
            _segment.FillBrush = value;
        }
    }

    /// <summary>
    /// 按卡片样式刷新：分段开关取「终末地视觉 → 分段进度」，轨道按前景色派生，
    /// 填充取进度条颜色令牌（空配置时等于强调色）。
    /// </summary>
    public void ApplyStyle(StyleSettings style)
    {
        StyleSettings s = style ?? new StyleSettings();
        ThemeTokens tokens = ThemeResolver.Resolve(s);
        Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

        TrackBrush = CardPalette.Brush(foreground, 0.12);
        FillBrush = CardPalette.Brush(tokens.Progress);

        _segmented = EndfieldVisuals.SegmentProgress(s);
        _segment.Visibility = _segmented ? Visibility.Visible : Visibility.Collapsed;
        _legacy.Visibility = _segmented ? Visibility.Collapsed : Visibility.Visible;
    }

    void UpdateFillWidth() => _fill.Width = Math.Max(0, _track.ActualWidth * _value);
}
