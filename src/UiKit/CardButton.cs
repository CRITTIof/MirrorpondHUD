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
/// 2.36.0 起受「终末地视觉 → 工业按钮 / 切角」两个开关控制：
/// - 开启（默认）：主行动实心强调色 + 右上切角，次级行动深色块，全部 4px 微圆角；
/// - 关闭：回到 2.34 的细描边按钮，主 / 危险只染描边与文字，圆角跟随卡片。
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
    const double ModernRadius = 4;

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
        // 旧模板有 disabled 触发器；新模板没有，统一在这里保留“整块变淡”的可用性反馈。
        IsEnabledChanged += (s, e) => Opacity = IsEnabled ? 1.0 : 0.38;
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

        _modern = EndfieldVisuals.Buttons(style);
        _cut = EndfieldVisuals.CutCorners(style);

        if (_modern)
        {
            // M4/M8：按钮一族统一 4px 微圆角；主行动改用切角多边形（OnRender 绘制）。
            CornerRadius = ModernRadius;
            Template = BuildModernTemplate();
        }
        else
        {
            CornerRadius = Math.Max(0, Math.Min(7, cornerRadius));
            Template = BuildLegacyTemplate(CornerRadius, LegacyHover(), LegacyPress(), _accent);
        }

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
    bool _modern = true;
    bool _cut = true;

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
        if (!_modern)
        {
            ApplyLegacyColors();
            return;
        }

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

        ApplyTextAndSurface(_baseFill, Colors.Transparent, _textColor);
        Background = new SolidColorBrush(_baseFill);
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
    }

    /// <summary>2.34 旧观感：透明 / 淡色底 + 1px 描边，主 / 危险只染描边与文字。</summary>
    void ApplyLegacyColors()
    {
        Color background;
        Color border;
        Color text;

        if (_active)
        {
            background = WithAlpha(_accent, 0.22);
            border = WithAlpha(_accent, 0.72);
            text = _accent;
        }
        else
        {
            switch (Tone)
            {
                case CardButtonTone.Primary:
                    background = WithAlpha(_accent, 0.10);
                    border = WithAlpha(_accent, 0.42);
                    text = _accent;
                    break;

                case CardButtonTone.Danger:
                    background = Colors.Transparent;
                    border = WithAlpha(_danger, 0.38);
                    text = _danger;
                    break;

                default:
                    background = Colors.Transparent;
                    border = WithAlpha(_foreground, 0.20);
                    text = WithAlpha(_foreground, 0.84);
                    break;
            }
        }

        ApplyTextAndSurface(background, border, text);
        Background = new SolidColorBrush(background);
        BorderBrush = new SolidColorBrush(border);
        BorderThickness = new Thickness(1);
    }

    void ApplyTextAndSurface(Color background, Color border, Color text)
    {
        var textBrush = new SolidColorBrush(text);
        textBrush.Freeze();
        Foreground = textBrush;
        BorderBrush = new SolidColorBrush(border);

        // 内容是自己创建的 TextBlock：显式给字色，不依赖模板重建时的 Foreground 继承。
        var block = Content as TextBlock;
        if (block is not null) block.Foreground = textBrush;
        InvalidateVisual();
    }

    /// <summary>M4：主行动实心、次行动深色块，都不再画整圈 1px 描边；只有焦点环外置。</summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (_modern)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w > 0 && h > 0)
            {
                bool cut = _cut && (_active || Tone == CardButtonTone.Primary);
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
        }

        base.OnRender(dc);
    }

    static Geometry BuildShape(Rect rect, bool cut)
    {
        if (!cut)
        {
            var rounded = new RectangleGeometry(rect, ModernRadius, ModernRadius);
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

    Color LegacyHover()
    {
        switch (Tone)
        {
            case CardButtonTone.Primary: return WithAlpha(_accent, 0.18);
            case CardButtonTone.Danger: return WithAlpha(_danger, 0.14);
            default: return WithAlpha(_foreground, 0.10);
        }
    }

    Color LegacyPress()
    {
        switch (Tone)
        {
            case CardButtonTone.Primary: return WithAlpha(_accent, 0.30);
            case CardButtonTone.Danger: return WithAlpha(_danger, 0.26);
            default: return WithAlpha(_foreground, 0.18);
        }
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
    /// 新模板只保留内容呈现：底形、切角、悬停 / 按下 / 焦点全部由 OnRender 画，
    /// 避免模板里的 Border 再围一圈描边。
    /// </summary>
    static ControlTemplate BuildModernTemplate()
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

    /// <summary>
    /// 2.34 旧模板：细描边底 + 内容；悬停 / 按下叠淡色，焦点换强调色描边。
    /// 只在「工业按钮」开关关闭时使用。
    /// </summary>
    static ControlTemplate BuildLegacyTemplate(double radius, Color hover, Color pressed, Color focus)
    {
        string r = radius.ToString("0.##", CultureInfo.InvariantCulture);
        string hoverHex = ToHex(hover);
        string pressedHex = ToHex(pressed);
        string focusHex = ToHex(focus);

        string xaml =
            "<ControlTemplate TargetType=\"Button\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid>" +
            "<Border x:Name=\"Shell\" CornerRadius=\"" + r + "\" " +
            "Background=\"{TemplateBinding Background}\" " +
            "BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"1\"/>" +
            "<Border x:Name=\"Hover\" CornerRadius=\"" + r + "\" Background=\"" + hoverHex + "\" Opacity=\"0\"/>" +
            "<Border x:Name=\"Press\" CornerRadius=\"" + r + "\" Background=\"" + pressedHex + "\" Opacity=\"0\"/>" +
            "<ContentPresenter Margin=\"{TemplateBinding Padding}\" " +
            "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" RecognizesAccessKey=\"True\"/>" +
            "</Grid>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
            "<Setter TargetName=\"Hover\" Property=\"Opacity\" Value=\"1\"/>" +
            "</Trigger>" +
            "<Trigger Property=\"IsPressed\" Value=\"True\">" +
            "<Setter TargetName=\"Press\" Property=\"Opacity\" Value=\"1\"/>" +
            "<Setter TargetName=\"Hover\" Property=\"Opacity\" Value=\"0\"/>" +
            "</Trigger>" +
            "<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">" +
            "<Setter TargetName=\"Shell\" Property=\"BorderBrush\" Value=\"" + focusHex + "\"/>" +
            "</Trigger>" +
            "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
            "<Setter Property=\"Opacity\" Value=\"0.38\"/>" +
            "<Setter Property=\"Cursor\" Value=\"Arrow\"/>" +
            "</Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate>";

        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    static string ToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
