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

        double totalGap = _gap * (_segments - 1);
        double segmentWidth = (w - totalGap) / _segments;
        if (segmentWidth <= 0.5)
        {
            _segments = Math.Max(2, (int)(w / 3));
            totalGap = _gap * (_segments - 1);
            segmentWidth = Math.Max(1, (w - totalGap) / _segments);
        }

        int filled = (int)Math.Round(_value * _segments);
        for (int i = 0; i < _segments; i++)
        {
            double x = i * (segmentWidth + _gap);
            Brush brush = i < filled ? _fillBrush : _trackBrush;
            if (brush is null) continue;
            dc.DrawRectangle(brush, null, new Rect(x, 0, segmentWidth, h));
        }
    }
}
