using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 卡片排序的跟随浮层：拖动六点抓手时，把正在拖的卡片画成半透明预览跟着指针走。
/// 只参与绘制，不接收命中；真正的排序仍由 CardListDrop 完成。
/// </summary>
internal sealed class CardDragAdorner : Adorner
{
    readonly VisualBrush _brush;
    readonly double _width;
    readonly double _height;
    Point _position = new(12, 12);

    public CardDragAdorner(UIElement adornedElement, FrameworkElement card) : base(adornedElement)
    {
        IsHitTestVisible = false;
        Opacity = 0.9;

        _width = card is not null && card.ActualWidth > 1 ? card.ActualWidth : 360;
        _height = card is not null && card.ActualHeight > 1 ? card.ActualHeight : 96;

        _brush = new VisualBrush(card ?? adornedElement)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, _width, _height),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, _width, _height)
        };
    }

    public void SetPosition(Point position) => _position = position;

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(_position.X, _position.Y, _width, _height);
        var clip = new RectangleGeometry(rect, 6, 6);
        clip.Freeze();

        dc.PushClip(clip);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, 10, 12, 13)), null, rect);
        dc.DrawRectangle(_brush, null, rect);
        dc.Pop();

        var border = new Pen(new SolidColorBrush(Color.FromArgb(180, 242, 233, 0)), 1);
        border.Freeze();
        dc.DrawRoundedRectangle(null, border, rect, 6, 6);
    }
}
