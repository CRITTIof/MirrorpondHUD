using System;
using System.Windows;
using System.Windows.Media;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 2.46.3：卡片角处理。Round = 原圆角；Cut = 45° 切角。
/// 有边缘条的一侧永远是直角（设计稿要求），所以几何需要知道边缘条在哪一侧。
/// </summary>
internal static class CardShapeGeometry
{
    public const string Round = "Round";
    public const string Cut = "Cut";

    public static bool IsCut(string style) =>
        string.Equals(style, Cut, StringComparison.OrdinalIgnoreCase);

    public static double CutFor(Size size) =>
        Math.Max(8.0, Math.Min(28.0, Math.Min(size.Width, size.Height) * 0.07));

    public static Geometry Build(Size size, string cornerStyle, double radius, bool edgeBar, bool barRight)
    {
        double w = Math.Max(1, size.Width);
        double h = Math.Max(1, size.Height);

        if (IsCut(cornerStyle))
        {
            double cut = Math.Min(CutFor(size), Math.Min(w, h) / 2);
            var cutGeo = new StreamGeometry();
            using (StreamGeometryContext ctx = cutGeo.Open())
            {
                if (edgeBar && barRight)
                {
                    // 边缘条在右：右侧直角，左侧上下切角。
                    ctx.BeginFigure(new Point(cut, 0), true, true);
                    ctx.LineTo(new Point(w, 0), true, false);
                    ctx.LineTo(new Point(w, h), true, false);
                    ctx.LineTo(new Point(cut, h), true, false);
                    ctx.LineTo(new Point(0, h - cut), true, false);
                    ctx.LineTo(new Point(0, cut), true, false);
                }
                else
                {
                    // 边缘条在左（或未启用）：左侧直角，右侧上下切角。
                    ctx.BeginFigure(new Point(0, 0), true, true);
                    ctx.LineTo(new Point(w - cut, 0), true, false);
                    ctx.LineTo(new Point(w, cut), true, false);
                    ctx.LineTo(new Point(w, h - cut), true, false);
                    ctx.LineTo(new Point(w - cut, h), true, false);
                    ctx.LineTo(new Point(0, h), true, false);
                }
            }
            cutGeo.Freeze();
            return cutGeo;
        }

        double left = edgeBar && !barRight ? 0 : Math.Max(0, radius);
        double right = edgeBar && barRight ? 0 : Math.Max(0, radius);
        left = Math.Min(left, Math.Min(w, h) / 2);
        right = Math.Min(right, Math.Min(w, h) / 2);

        if (left <= 0.01 && right <= 0.01) return new RectangleGeometry(new Rect(0, 0, w, h));

        var geo = new StreamGeometry();
        using (StreamGeometryContext ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(left, 0), true, true);
            ctx.LineTo(new Point(w - right, 0), true, false);
            if (right > 0.01)
                ctx.ArcTo(new Point(w, right), new Size(right, right), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(w, h - right), true, false);
            if (right > 0.01)
                ctx.ArcTo(new Point(w - right, h), new Size(right, right), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(left, h), true, false);
            if (left > 0.01)
                ctx.ArcTo(new Point(0, h - left), new Size(left, left), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(0, left), true, false);
            if (left > 0.01)
                ctx.ArcTo(new Point(left, 0), new Size(left, left), 0, false, SweepDirection.Clockwise, true, true);
        }
        geo.Freeze();
        return geo;
    }
}

/// <summary>轮廓上的 1px 线；单独一层，避免被内容层背景盖住。</summary>
internal sealed class CardFrame : FrameworkElement
{
    public string CornerStyle = CardShapeGeometry.Round;
    public double Radius = 10;
    public bool EdgeBar;
    public bool BarRight;
    public Brush Stroke;

    public CardFrame()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    public void Configure(string cornerStyle, double radius, bool edgeBar, bool barRight, Brush stroke)
    {
        CornerStyle = cornerStyle;
        Radius = radius;
        EdgeBar = edgeBar;
        BarRight = barRight;
        Stroke = stroke;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        Size size = RenderSize;
        if (size.Width < 1 || size.Height < 1) return;

        // 内缩 0.5px：1px 线有一半落在裁切外，内缩后线完整。
        const double inset = 0.5;
        var rectSize = new Size(Math.Max(1, size.Width - inset * 2), Math.Max(1, size.Height - inset * 2));
        Geometry geometry = CardShapeGeometry.Build(rectSize, CornerStyle, Radius, EdgeBar, BarRight);
        var pen = new Pen(Stroke ?? Brushes.Transparent, 1);
        pen.Freeze();
        dc.PushTransform(new TranslateTransform(inset, inset));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }
}
