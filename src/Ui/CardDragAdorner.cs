using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 卡片排序的跟随浮层：拖动六点抓手时，把卡片的标题栏快照画成一张折叠卡跟着指针走。
/// 只参与绘制，不接收命中；真正的排序仍由 CardListDrop 完成。
/// </summary>
internal sealed class CardDragAdorner : Adorner
{
    readonly ImageSource _preview;
    readonly double _width;
    readonly double _height;
    Point _position = new(12, 12);

    public CardDragAdorner(UIElement adornedElement, ImageSource preview, double width, double height)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
        Opacity = 0.94;

        _preview = preview;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
    }

    /// <summary>
    /// 把标题栏按当前 DPI 抓成位图。拖动时源卡片会被压扁 / 淡出，
    /// 所以必须在开始拖动前拍快照，不能再用 VisualBrush 实时引用它。
    /// </summary>
    public static ImageSource Capture(FrameworkElement element)
    {
        if (element is null || element.ActualWidth < 1 || element.ActualHeight < 1) return null;

        try
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(element);
            int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
            var bitmap = new RenderTargetBitmap(width, height,
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(element);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Warn($"卡片拖动跟随快照失败: {ex.Message}");
            return null;
        }
    }

    public void SetPosition(Point position) => _position = position;

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(_position.X, _position.Y, _width, _height);
        var clip = new RectangleGeometry(rect, 8, 8);
        clip.Freeze();

        // 5px 的接触阴影让折叠卡从列表上"抬"起来，但不做成大黑块。
        var shadow = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0));
        shadow.Freeze();
        var shadowRect = new Rect(rect.X + 2, rect.Y + 3, rect.Width, rect.Height);
        dc.DrawRoundedRectangle(shadow, null, shadowRect, 8, 8);

        dc.PushClip(clip);
        dc.DrawImage(_preview, rect);
        dc.Pop();

        var border = new Pen(new SolidColorBrush(Color.FromArgb(210, 242, 233, 0)), 1);
        border.Freeze();
        dc.DrawRoundedRectangle(null, border, rect, 8, 8);
    }
}
