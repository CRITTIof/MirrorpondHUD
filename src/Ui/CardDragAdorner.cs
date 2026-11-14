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
///
/// T-17：颜色与圆角来自主题令牌，不再硬编码；画笔 / 圆角几何在构造时建一次并冻结，
/// 拖动过程中每帧只改复用的平移变换。原先 <see cref="OnRender"/> 每次鼠标移动都要
/// new 一个裁剪几何、两个画刷和一个 Pen（60+ 次/秒），而且在浅色主题下那条固定黄描边
/// 和背景打架。尺寸（宽高）与主题在浮层存活期间不会变——一次拖动就是一个实例，
/// 换主题会重建卡片列表并结束拖动——所以"尺寸 / 主题变化才重建"在这里等价于
/// 构造时建一次。
/// </summary>
internal sealed class CardDragAdorner : Adorner
{
    /// <summary>5px 量级的接触阴影：让折叠卡从列表上"抬"起来，但不做成大黑块。</summary>
    const double ShadowOffsetX = 2;
    const double ShadowOffsetY = 3;

    readonly ImageSource _preview;
    readonly double _width;
    readonly double _height;
    Point _position = new(12, 12);

    // —— 构造时建好、拖动期间复用（全部冻结，跨帧共享安全） ——
    readonly Rect _localRect;
    readonly double _cornerRadius;
    readonly RectangleGeometry _clip;
    readonly SolidColorBrush _shadowBrush;
    readonly Pen _borderPen;

    // 位置每帧都变，但变换对象本身可以复用：OnRender 每次都会重录整个绘图内容，
    // 旧位置不会残留在可视树下，所以不需要为每个位置分配新的 Transform。
    readonly TranslateTransform _bodyOffset = new();
    readonly TranslateTransform _shadowOffset = new();

    /// <param name="style">
    /// 主题令牌来源（设置窗当前工作的 Settings.Style）。null 时按 ThemeResolver 的默认派生
    /// 走——和控件库其余"没拿到样式就画默认令牌"的路径一致，不额外吞错。
    /// </param>
    /// <param name="cornerRadius">卡片圆角（Overlay.CornerRadius），夹到宽高一半以内。</param>
    public CardDragAdorner(UIElement adornedElement, ImageSource preview, double width, double height,
                           StyleSettings style, double cornerRadius)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
        Opacity = 0.94;

        _preview = preview;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _localRect = new Rect(0, 0, _width, _height);

        // 0 必须真的是 0（直角），不能被最小值悄悄抬回 8；上限取短边一半，
        // 超过一半的半径在 RectangleGeometry 里会渲染成畸形。
        _cornerRadius = Math.Max(0.0, Math.Min(cornerRadius, Math.Min(_width, _height) * 0.5));

        // 边框用强调色：旧实现固定 #D2F2E900，浅色主题下与背景冲突（审阅 T-17）。
        // 阴影保持中性黑：投影不是主题色，深色 / 浅色主题下都应该是暗色。
        ThemeTokens tokens = ThemeResolver.Resolve(style);
        _shadowBrush = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0));
        _shadowBrush.Freeze();
        var borderBrush = new SolidColorBrush(tokens.Accent);
        borderBrush.Freeze();
        _borderPen = new Pen(borderBrush, 1);
        _borderPen.Freeze();

        _clip = new RectangleGeometry(_localRect, _cornerRadius, _cornerRadius);
        _clip.Freeze();
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
        // 阴影先画：它不能被圆角裁剪，否则只剩卡片内部一圈淡影。
        _shadowOffset.X = _position.X + ShadowOffsetX;
        _shadowOffset.Y = _position.Y + ShadowOffsetY;
        dc.PushTransform(_shadowOffset);
        dc.DrawRoundedRectangle(_shadowBrush, null, _localRect, _cornerRadius, _cornerRadius);
        dc.Pop();

        _bodyOffset.X = _position.X;
        _bodyOffset.Y = _position.Y;
        dc.PushTransform(_bodyOffset);
        dc.PushClip(_clip);
        dc.DrawImage(_preview, _localRect);
        dc.Pop();
        dc.DrawRoundedRectangle(null, _borderPen, _localRect, _cornerRadius, _cornerRadius);
        dc.Pop();
    }
}
