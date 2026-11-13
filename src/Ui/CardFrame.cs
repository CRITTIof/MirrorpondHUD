using System;
using System.Windows;
using System.Windows.Media;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 2.46.2：卡片轮廓的三种设计语言（对应 samples 的三张 Endfield 构想）：
/// CutAngle = 右上/右下 45° 切角；RightAngle = 全直角；RoundAngle = 全圆角。
/// 内容不参与形状判断，只由 OverlaySurface 统一裁切。
/// </summary>
internal static class CardShapeGeometry
{
    public const string CutAngle = "CutAngle";
    public const string RightAngle = "RightAngle";
    public const string RoundAngle = "RoundAngle";

    public static bool IsCut(string shape) => string.Equals(shape, CutAngle, StringComparison.OrdinalIgnoreCase);
    public static bool IsRight(string shape) => string.Equals(shape, RightAngle, StringComparison.OrdinalIgnoreCase);

    // 设计稿的切角约为卡片高度的 5–8%；小卡至少 8 DIP，大卡封顶 28 DIP。
    public static double CutFor(Size size) =>
        Math.Max(8.0, Math.Min(28.0, Math.Min(size.Width, size.Height) * 0.07));

    /// <summary>返回闭合轮廓；坐标原点在卡片内容左上角。</summary>
    public static Geometry Build(Size size, string shape, double radius)
    {
        double w = Math.Max(1, size.Width);
        double h = Math.Max(1, size.Height);
        var rect = new Rect(0, 0, w, h);

        if (IsRight(shape)) return new RectangleGeometry(rect);

        if (IsCut(shape))
        {
            double cut = CutFor(size);
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(w - cut, 0), true, false);
                ctx.LineTo(new Point(w, cut), true, false);
                ctx.LineTo(new Point(w, h - cut), true, false);
                ctx.LineTo(new Point(w - cut, h), true, false);
                ctx.LineTo(new Point(0, h), true, false);
            }
            geo.Freeze();
            return geo;
        }

        double r = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
        return new RectangleGeometry(rect, r, r);
    }
}

/// <summary>轮廓上的 1px 线；单独一层，避免被内容层背景盖住。</summary>
internal sealed class CardFrame : FrameworkElement
{
    public string Shape = CardShapeGeometry.RoundAngle;
    public double Radius = 10;
    public Brush Stroke;

    public CardFrame()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    public void Configure(string shape, double radius, Brush stroke)
    {
        Shape = shape;
        Radius = radius;
        Stroke = stroke;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        Size size = RenderSize;
        if (size.Width < 1 || size.Height < 1) return;

        // 内缩 0.5px：1px 线有一半落在裁切外，内缩后线完整。
        double inset = 0.5;
        var rectSize = new Size(Math.Max(1, size.Width - inset * 2), Math.Max(1, size.Height - inset * 2));
        Geometry geometry = CardShapeGeometry.Build(rectSize, Shape, Radius);
        var pen = new Pen(Stroke ?? Brushes.Transparent, 1);
        pen.Freeze();
        dc.PushTransform(new TranslateTransform(inset, inset));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }
}
