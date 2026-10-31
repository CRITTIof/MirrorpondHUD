using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
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
            Padding = new Thickness(10, 3, 10, 3);
            MinWidth = 0;
            MinHeight = 22;
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
                return block == null ? "" : block.Text;
            }
            set
            {
                var block = Content as TextBlock;
                if (block != null) block.Text = value ?? "";
            }
        }

        /// <summary>把卡片的字体、字号、前景色、圆角套到按钮上。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            if (style == null) return;

            ThemeTokens tokens = ThemeResolver.Resolve(style);
            _foreground = OverlaySurface.ParseColor(style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            _accent = tokens.Accent;
            _danger = tokens.Danger;

            FontFamily = ResolveFont(style.FontFamily);
            FontSize = Math.Max(9, style.FontSize - 1);
            FontWeight = FontWeights.Normal;
            CornerRadius = Math.Max(0, Math.Min(7, cornerRadius));

            Color hover;
            Color pressed;
            switch (Tone)
            {
                case CardButtonTone.Primary:
                    hover = WithAlpha(_accent, 0.18);
                    pressed = WithAlpha(_accent, 0.30);
                    break;
                case CardButtonTone.Danger:
                    hover = WithAlpha(_danger, 0.14);
                    pressed = WithAlpha(_danger, 0.26);
                    break;
                default:
                    hover = WithAlpha(_foreground, 0.10);
                    pressed = WithAlpha(_foreground, 0.18);
                    break;
            }

            Template = BuildTemplate(CornerRadius, hover, pressed, _accent);
            ApplyColors();
        }

        Color _accent = Colors.SteelBlue;
        Color _danger = Color.FromRgb(0xE5, 0x53, 0x53);
        Color _foreground = Colors.White;
        bool _active;

        /// <summary>
        /// 多选 / 已选中的按钮：换成强调色淡底 + 强调色描边 + 强调色文字。
        /// 不再用实心大色块，选中态在深色 / 浅色卡片上都只是"比旁边亮一点"。
        /// </summary>
        public void SetActive(bool active)
        {
            _active = active;
            ApplyColors();
        }

        /// <summary>卡片自己的圆角（0 = 直角）。按钮内部取一个小一点的圆角。</summary>
        public double CornerRadius { get; private set; }

        /// <summary>按语气 / 选中态算底色、描边、文字色；换肤和 SetActive 共用同一套。</summary>
        void ApplyColors()
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

            var textBrush = new SolidColorBrush(text);
            Background = new SolidColorBrush(background);
            BorderBrush = new SolidColorBrush(border);
            Foreground = textBrush;

            // 内容是自己创建的 TextBlock：显式给字色，不依赖模板重建时的 Foreground 继承。
            var block = Content as TextBlock;
            if (block != null) block.Foreground = textBrush;
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
        /// 按钮模板：细描边底 + 内容；鼠标悬停时叠一层很淡的亮色，按下时叠一层稍深的颜色，
        /// 键盘焦点时描边换成强调色，禁用时整块（文字 + 描边）一起变淡。
        /// </summary>
        static ControlTemplate BuildTemplate(double radius, Color hover, Color pressed, Color focus)
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

        static string ToHex(Color color)
        {
            return "#" + color.A.ToString("X2") + color.R.ToString("X2")
                 + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }
}
