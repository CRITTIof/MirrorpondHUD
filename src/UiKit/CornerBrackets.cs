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
    double _horizontalArm = 13;
    double _verticalArm = 13;
    double _thickness = 2;
    bool _chatStyle;
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
        get => _horizontalArm;
        set
        {
            double next = Math.Max(4, value);
            if (Math.Abs(_horizontalArm - next) < 0.01 && Math.Abs(_verticalArm - next) < 0.01) return;
            _horizontalArm = next;
            _verticalArm = next;
            InvalidateVisual();
        }
    }

    /// <summary>2.46.3：AI 对话设计稿的长横短竖角括号；关闭时回到等臂 L。</summary>
    public bool ChatStyle
    {
        get => _chatStyle;
        set
        {
            if (_chatStyle == value) return;
            _chatStyle = value;
            if (value)
            {
                _horizontalArm = 26;
                _verticalArm = 9;
                _thickness = 1.6;
            }
            else
            {
                _horizontalArm = 13;
                _verticalArm = 13;
                _thickness = 2;
            }
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
        double maxArm = Math.Min(w - offset * 2, h - offset * 2) / 2;
        double hArm = Math.Min(_horizontalArm, maxArm);
        double vArm = Math.Min(_verticalArm, maxArm);
        if (hArm <= 1 || vArm <= 1) return;

        double x0 = offset;
        double y0 = offset;
        double x1 = w - offset;
        double y1 = h - offset;
        var brush = new SolidColorBrush(_color);
        brush.Freeze();
        var pen = new Pen(brush, _thickness);
        pen.Freeze();

        // 左上
        dc.DrawLine(pen, new Point(x0, y0), new Point(x0 + hArm, y0));
        dc.DrawLine(pen, new Point(x0, y0), new Point(x0, y0 + vArm));
        // 右上
        dc.DrawLine(pen, new Point(x1 - hArm, y0), new Point(x1, y0));
        dc.DrawLine(pen, new Point(x1, y0), new Point(x1, y0 + vArm));
        // 左下
        dc.DrawLine(pen, new Point(x0, y1 - vArm), new Point(x0, y1));
        dc.DrawLine(pen, new Point(x0, y1), new Point(x0 + hArm, y1));
        // 右下
        dc.DrawLine(pen, new Point(x1 - hArm, y1), new Point(x1, y1));
        dc.DrawLine(pen, new Point(x1, y1 - vArm), new Point(x1, y1));
    }
}
