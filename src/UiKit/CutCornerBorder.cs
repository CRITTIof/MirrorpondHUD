using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MirrorpondHUD.Ui.Controls;

public enum CutCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// 45° 切角容器：一个角按 <see cref="CutSize"/> 切掉，子内容不被裁切。
/// 规范要求焦点环画在外层、形状画在内层；这个控件只负责形状，宿主负责焦点环。
/// </summary>
public sealed class CutCornerBorder : Decorator
{
    Brush _background;
    Brush _borderBrush;
    Thickness _borderThickness = new(0);
    Thickness _padding = new(0);
    double _cutSize = 10;
    CutCorner _corner = CutCorner.TopRight;

    public CutCornerBorder()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public Brush Background
    {
        get => _background;
        set
        {
            _background = value;
            InvalidateVisual();
        }
    }

    public Brush BorderBrush
    {
        get => _borderBrush;
        set
        {
            _borderBrush = value;
            InvalidateVisual();
        }
    }

    public Thickness BorderThickness
    {
        get => _borderThickness;
        set
        {
            _borderThickness = value;
            InvalidateVisual();
        }
    }

    public Thickness Padding
    {
        get => _padding;
        set
        {
            _padding = value;
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    public double CutSize
    {
        get => _cutSize;
        set
        {
            _cutSize = Math.Max(0, value);
            InvalidateVisual();
        }
    }

    public CutCorner Corner
    {
        get => _corner;
        set
        {
            _corner = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not null)
        {
            Child.Measure(new Size(
                Math.Max(0, constraint.Width - _padding.Left - _padding.Right),
                Math.Max(0, constraint.Height - _padding.Top - _padding.Bottom)));
        }

        Size desired = Child is null ? new Size(0, 0) : Child.DesiredSize;
        return new Size(desired.Width + _padding.Left + _padding.Right,
                        desired.Height + _padding.Top + _padding.Bottom);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not null)
        {
            Child.Arrange(new Rect(
                _padding.Left,
                _padding.Top,
                Math.Max(0, arrangeSize.Width - _padding.Left - _padding.Right),
                Math.Max(0, arrangeSize.Height - _padding.Top - _padding.Bottom)));
        }
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double cut = Math.Min(_cutSize, Math.Min(w, h) / 2);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            switch (_corner)
            {
                case CutCorner.TopLeft:
                    ctx.BeginFigure(new Point(cut, 0), true, true);
                    ctx.LineTo(new Point(w, 0), true, false);
                    ctx.LineTo(new Point(w, h), true, false);
                    ctx.LineTo(new Point(0, h), true, false);
                    ctx.LineTo(new Point(0, cut), true, false);
                    break;
                case CutCorner.BottomLeft:
                    ctx.BeginFigure(new Point(0, 0), true, true);
                    ctx.LineTo(new Point(w, 0), true, false);
                    ctx.LineTo(new Point(w, h), true, false);
                    ctx.LineTo(new Point(cut, h), true, false);
                    ctx.LineTo(new Point(0, h - cut), true, false);
                    break;
                case CutCorner.BottomRight:
                    ctx.BeginFigure(new Point(0, 0), true, true);
                    ctx.LineTo(new Point(w, 0), true, false);
                    ctx.LineTo(new Point(w, h - cut), true, false);
                    ctx.LineTo(new Point(w - cut, h), true, false);
                    ctx.LineTo(new Point(0, h), true, false);
                    break;
                default:
                    ctx.BeginFigure(new Point(0, 0), true, true);
                    ctx.LineTo(new Point(w - cut, 0), true, false);
                    ctx.LineTo(new Point(w, cut), true, false);
                    ctx.LineTo(new Point(w, h), true, false);
                    ctx.LineTo(new Point(0, h), true, false);
                    break;
            }
        }
        geometry.Freeze();

        Pen pen = null;
        if (_borderBrush is not null && (_borderThickness.Left > 0 || _borderThickness.Top > 0))
        {
            pen = new Pen(_borderBrush, Math.Max(_borderThickness.Left, _borderThickness.Top));
            pen.Freeze();
        }
        dc.DrawGeometry(_background, pen, geometry);
    }
}
