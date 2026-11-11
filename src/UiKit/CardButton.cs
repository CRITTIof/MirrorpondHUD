using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>按钮的语气：普通、主操作（强调色）、危险操作（拒绝 / 删除）。</summary>
public enum CardButtonTone
{
    Normal,
    Primary,
    Danger
}

/// <summary>
/// 卡片里的按钮：颜色、字体、圆角都跟着卡片本身走，不依赖设置窗口那套浅色资源字典。
///
/// 视觉语言和卡片一致：默认透明底 + 1 px 细描边 + 低对比文字，悬停 / 按下才叠一层
/// 淡淡的色调；主操作 / 危险操作只把描边和文字染成强调色 / 拒绝色，不再用高饱和
/// 实心块。这样放在标题行里不会比标题还抢眼，深色卡片和浅色卡片也都成立。
///
/// 用法（富内容卡片里）：
/// <code>
/// var open = new CardButton("打开", CardButtonTone.Primary);
/// open.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
/// open.Click += (s, e) => ...;
/// </code>
/// </summary>
public class CardButton : Button
{
    public CardButton(string text, CardButtonTone tone)
    {
        Tone = tone;
        Content = new TextBlock
        {
            Text = text ?? "",
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Padding = new Thickness(12, 6, 12, 6);
        MinWidth = 0;
        MinHeight = 28;
        MaxWidth = 360;
        Cursor = System.Windows.Input.Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
    }

    public CardButton(string text) : this(text, CardButtonTone.Normal)
    {
    }

    public CardButtonTone Tone { get; private set; }

    /// <summary>按钮上的文字（用按钮自己的 TextBlock 渲染，长文本自动省略）。</summary>
    public string Text
    {
        get
        {
            var block = Content as TextBlock;
            return block is null ? "" : block.Text;
        }
        set
        {
            var block = Content as TextBlock;
            if (block is not null) block.Text = value ?? "";
        }
    }

    /// <summary>把卡片的字体、字号、前景色、圆角套到按钮上。</summary>
    public void ApplyStyle(StyleSettings style, double cornerRadius)
    {
        if (style is null) return;

        ThemeTokens tokens = ThemeResolver.Resolve(style);
        _foreground = CardPalette.TryColor(style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
        _accent = tokens.Accent;
        _danger = tokens.Danger;

        FontFamily = ResolveFont(style.FontFamily);
        FontSize = Math.Max(9, style.FontSize - 1);
        FontWeight = FontWeights.Normal;
        // M4/M8：按钮一族统一 4px 微圆角；主行动改用切角多边形（OnRender 绘制）。
        CornerRadius = 4;
        Template = BuildTemplate();
        ApplyColors();
    }

    Color _accent = Colors.SteelBlue;
    Color _danger = Color.FromRgb(0xE5, 0x53, 0x53);
    Color _foreground = Colors.White;
    Color _baseFill;
    Color _hoverFill;
    Color _pressFill;
    Color _textColor;
    bool _active;

    /// <summary>
    /// 多选 / 已选中的按钮：换成强调色淡底 + 强调色描边 + 强调色文字。
    /// 不再用实心大色块，选中态在深色 / 浅色卡片上都只是"比旁边亮一点"。
    /// </summary>
    public void SetActive(bool active)
    {
        _active = active;
        ApplyColors();
        InvalidateVisual();
    }

    /// <summary>卡片自己的圆角（0 = 直角）。按钮内部取一个小一点的圆角。</summary>
    public double CornerRadius { get; private set; }

    /// <summary>按语气 / 选中态算底色、描边、文字色；换肤和 SetActive 共用同一套。</summary>
    void ApplyColors()
    {
        if (_active)
        {
            _baseFill = _accent;
            _hoverFill = Blend(_accent, Colors.White, 0.10);
            _pressFill = Blend(_accent, Colors.Black, 0.10);
            _textColor = Contrast(_accent);
        }
        else if (Tone == CardButtonTone.Primary)
        {
            _baseFill = _accent;
            _hoverFill = Blend(_accent, Colors.White, 0.12);
            _pressFill = Blend(_accent, Colors.Black, 0.12);
            _textColor = Contrast(_accent);
        }
        else if (Tone == CardButtonTone.Danger)
        {
            _baseFill = WithAlpha(_danger, 0.12);
            _hoverFill = WithAlpha(_danger, 0.20);
            _pressFill = WithAlpha(_danger, 0.28);
            _textColor = _danger;
        }
        else
        {
            // 次级行动：深色块 + 文字，不再围一圈描边。
            _baseFill = WithAlpha(_foreground, 0.06);
            _hoverFill = WithAlpha(_foreground, 0.12);
            _pressFill = WithAlpha(_foreground, 0.18);
            _textColor = WithAlpha(_foreground, 0.88);
        }

        var textBrush = new SolidColorBrush(_textColor);
        textBrush.Freeze();
        Background = new SolidColorBrush(_baseFill);
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Foreground = textBrush;

        // 内容是自己创建的 TextBlock：显式给字色，不依赖模板重建时的 Foreground 继承。
        var block = Content as TextBlock;
        if (block is not null) block.Foreground = textBrush;
        InvalidateVisual();
    }

    /// <summary>M4：主行动实心、次行动深色块，都不再画整圈 1px 描边；只有焦点环外置。</summary>
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w > 0 && h > 0)
        {
            bool cut = _active || Tone == CardButtonTone.Primary;
            Geometry shape = BuildShape(new Rect(0, 0, w, h), cut);
            Color fill = IsPressed ? _pressFill : IsMouseOver ? _hoverFill : _baseFill;
            var brush = new SolidColorBrush(fill);
            brush.Freeze();
            dc.DrawGeometry(brush, null, shape);

            if (IsKeyboardFocused)
            {
                Geometry ring = BuildShape(new Rect(-2, -2, w + 4, h + 4), cut);
                var pen = new Pen(new SolidColorBrush(_accent), 2);
                pen.Freeze();
                dc.DrawGeometry(null, pen, ring);
            }
        }

        base.OnRender(dc);
    }

    static Geometry BuildShape(Rect rect, bool cut)
    {
        if (!cut)
        {
            var rounded = new RectangleGeometry(rect, 4, 4);
            rounded.Freeze();
            return rounded;
        }

        double size = Math.Min(10, Math.Min(rect.Width, rect.Height) / 2);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(rect.Left, rect.Top), true, true);
            ctx.LineTo(new Point(rect.Right - size, rect.Top), true, false);
            ctx.LineTo(new Point(rect.Right, rect.Top + size), true, false);
            ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
            ctx.LineTo(new Point(rect.Left, rect.Bottom), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    static Color Blend(Color baseColor, Color other, double amount)
    {
        double t = Math.Max(0, Math.Min(1, amount));
        return Color.FromArgb(
            baseColor.A,
            (byte)Math.Round(baseColor.R + (other.R - baseColor.R) * t),
            (byte)Math.Round(baseColor.G + (other.G - baseColor.G) * t),
            (byte)Math.Round(baseColor.B + (other.B - baseColor.B) * t));
    }

    static Color Contrast(Color background)
    {
        double luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
        return luminance > 0.58 ? Color.FromRgb(0x14, 0x14, 0x14) : Colors.White;
    }

    static Color WithAlpha(Color color, double alpha)
    {
        byte a = (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255.0);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    static FontFamily ResolveFont(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return new FontFamily("Microsoft YaHei UI");
        try { return new FontFamily(name); }
        catch { return new FontFamily("Microsoft YaHei UI"); }
    }

    /// <summary>
    /// 只保留内容呈现：底形、切角、悬停 / 按下 / 焦点全部由 OnRender 画，
    /// 避免模板里的 Border 再围一圈描边。
    /// </summary>
    static ControlTemplate BuildTemplate()
    {
        const string xaml =
            "<ControlTemplate TargetType=\"Button\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Grid Background=\"Transparent\">" +
            "<ContentPresenter Margin=\"{TemplateBinding Padding}\" " +
            "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" RecognizesAccessKey=\"True\"/>" +
            "</Grid>" +
            "</ControlTemplate>";
        return (ControlTemplate)XamlReader.Parse(xaml);
    }
}
