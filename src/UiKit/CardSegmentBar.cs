using System;
using System.Windows;
using System.Windows.Media;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 直角分段进度条：轨道与填充都按固定段数绘制，没有圆头。
/// 规范 M5：与仪表读数同一种几何语言；不使用 3px 半圆端头。
/// </summary>
public sealed class CardSegmentBar : FrameworkElement
{
    Brush _trackBrush;
    Brush _fillBrush;
    double _value;
    int _segments = 24;
    double _gap = 2;

    public CardSegmentBar()
    {
        Height = 4;
        SnapsToDevicePixels = true;
    }

    // K-39：实际绘制用的段数 / 段宽由布局阶段算好并存这里，OnRender 只读，不再回写 _segments。
    int _layoutSegments = 24;
    double _layoutSegmentWidth;
    double _layoutWidth = double.NaN;

    /// <summary>
    /// 按可用宽度算一个能画出来的分段布局。宽度太小（段宽 <= 0.5）时段数会退化成
    /// “每 3px 一段”，这是原来的视觉规则，只是不再发生在 OnRender 里。
    /// 纯计算，不碰任何字段，渲染路径也用它算兜底值。
    /// </summary>
    static void ComputeSegments(double width, int requestedSegments, double gap,
                                out int segments, out double segmentWidth)
    {
        segments = requestedSegments;
        segmentWidth = 0;
        if (width <= 0) return;

        double totalGap = gap * (segments - 1);
        segmentWidth = (width - totalGap) / segments;
        if (segmentWidth > 0.5) return;

        segments = Math.Max(2, (int)(width / 3));
        totalGap = gap * (segments - 1);
        segmentWidth = Math.Max(1, (width - totalGap) / segments);
    }

    void UpdateLayoutSegments(double width)
    {
        _layoutWidth = width;
        ComputeSegments(width, _segments, _gap, out _layoutSegments, out _layoutSegmentWidth);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        UpdateLayoutSegments(arranged.Width);
        return arranged;
    }

    public double Value
    {
        get => _value;
        set
        {
            double next = Math.Max(0, Math.Min(1, value));
            if (Math.Abs(_value - next) < 1e-9) return;
            _value = next;
            InvalidateVisual();
        }
    }

    public int Segments
    {
        get => _segments;
        set
        {
            int next = Math.Max(2, value);
            if (_segments == next) return;
            _segments = next;
            // K-39：段数参与布局计算（Arrange），所以要同时请求重排与重绘。
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    public double Gap
    {
        get => _gap;
        set
        {
            double next = Math.Max(0, value);
            if (Math.Abs(_gap - next) < 0.01) return;
            _gap = next;
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    public Brush TrackBrush
    {
        get => _trackBrush;
        set
        {
            _trackBrush = value;
            InvalidateVisual();
        }
    }

    public Brush FillBrush
    {
        get => _fillBrush;
        set
        {
            _fillBrush = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0 || _trackBrush is null) return;

        // K-39：只读布局阶段算好的分段。OnRender 不再回写 _segments —— 渲染副作用改公开
        // 状态会让属性值和实际段数对不上，也会在布局回调里制造重入。
        int segments = _layoutSegments;
        double segmentWidth = _layoutSegmentWidth;
        if (double.IsNaN(_layoutWidth) || Math.Abs(_layoutWidth - w) > 0.01)
            ComputeSegments(w, _segments, _gap, out segments, out segmentWidth);

        if (segments <= 0 || segmentWidth <= 0) return;

        int filled = (int)Math.Round(_value * segments);
        for (int i = 0; i < segments; i++)
        {
            double x = i * (segmentWidth + _gap);
            Brush brush = i < filled ? _fillBrush : _trackBrush;
            if (brush is null) continue;
            dc.DrawRectangle(brush, null, new Rect(x, 0, segmentWidth, h));
        }
    }
}
