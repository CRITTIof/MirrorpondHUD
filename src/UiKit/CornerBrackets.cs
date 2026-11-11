using System;
using System.Windows;
using System.Windows.Media;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 四角 L 形角括号。规范：2px 线宽、12–14px 臂长、画在宿主之外约 4px。
/// 只做视觉，不参与命中测试；调用方用负 Margin 或外层留白决定外扩距离。
/// </summary>
public sealed class CornerBrackets : FrameworkElement
{
    double _armLength = 13;
    double _thickness = 2;
    double _offset;
    Color _color = Color.FromRgb(0xF2, 0xF2, 0xF2);

    public CornerBrackets()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        Visibility = Visibility.Collapsed;
    }

    public double ArmLength
    {
        get => _armLength;
        set
        {
            if (Math.Abs(_armLength - value) < 0.01) return;
            _armLength = Math.Max(4, value);
            InvalidateVisual();
        }
    }

    public double Thickness
    {
        get => _thickness;
        set
        {
            if (Math.Abs(_thickness - value) < 0.01) return;
            _thickness = Math.Max(0.5, value);
            InvalidateVisual();
        }
    }

    /// <summary>从自身边界向内缩的距离；默认 0，由调用方的 Margin 负责“外扩 4px”。</summary>
    public double Offset
    {
        get => _offset;
        set
        {
            if (Math.Abs(_offset - value) < 0.01) return;
            _offset = Math.Max(0, value);
            InvalidateVisual();
        }
    }

    public void ApplyStyle(Color color)
    {
        _color = color;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        double offset = Math.Min(_offset, Math.Min(w, h) / 4);
        double arm = Math.Min(_armLength, Math.Min(w - offset * 2, h - offset * 2) / 2);
        if (arm <= 1) return;

        double x0 = offset;
        double y0 = offset;
        double x1 = w - offset;
        double y1 = h - offset;
        var brush = new SolidColorBrush(_color);
        brush.Freeze();
        var pen = new Pen(brush, _thickness);
        pen.Freeze();

        // 左上
        dc.DrawLine(pen, new Point(x0, y0), new Point(x0 + arm, y0));
        dc.DrawLine(pen, new Point(x0, y0), new Point(x0, y0 + arm));
        // 右上
        dc.DrawLine(pen, new Point(x1 - arm, y0), new Point(x1, y0));
        dc.DrawLine(pen, new Point(x1, y0), new Point(x1, y0 + arm));
        // 左下
        dc.DrawLine(pen, new Point(x0, y1 - arm), new Point(x0, y1));
        dc.DrawLine(pen, new Point(x0, y1), new Point(x0 + arm, y1));
        // 右下
        dc.DrawLine(pen, new Point(x1 - arm, y1), new Point(x1, y1));
        dc.DrawLine(pen, new Point(x1, y1 - arm), new Point(x1, y1));
    }
}
