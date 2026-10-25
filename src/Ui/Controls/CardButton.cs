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
    /// 卡片是分层透明窗口，按钮直接用 <see cref="Button"/> 的自定义模板画出圆角背景 +
    /// hover / pressed / 键盘焦点三态；模板里用两层半透明覆盖层做高亮，所以在深色卡片和
    /// 浅色卡片上都成立，不需要为每种配色各写一个模板。
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
            Padding = new Thickness(12, 5, 12, 5);
            MinWidth = 64;
            MaxWidth = 360;
            Cursor = System.Windows.Input.Cursors.Hand;
            Focusable = true;
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

            Color foreground = TryColor(style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            Color accent = TryColor(ThemeAccent, Color.FromRgb(0x2F, 0x6F, 0xEB));

            Color background;
            Color border;
            Color text;

            switch (Tone)
            {
                case CardButtonTone.Primary:
                    background = WithAlpha(accent, 0.85);
                    border = WithAlpha(accent, 0.95);
                    text = PickReadable(accent, foreground);
                    break;
                case CardButtonTone.Danger:
                    Color danger = Color.FromRgb(0xD1, 0x43, 0x43);
                    background = WithAlpha(danger, 0.85);
                    border = WithAlpha(danger, 0.95);
                    text = PickReadable(danger, foreground);
                    break;
                default:
                    background = WithAlpha(foreground, 0.10);
                    border = WithAlpha(foreground, 0.28);
                    text = foreground;
                    break;
            }

            Background = new SolidColorBrush(background);
            BorderBrush = new SolidColorBrush(border);
            Foreground = new SolidColorBrush(text);
            FontFamily = ResolveFont(style.FontFamily);
            FontSize = Math.Max(9, style.FontSize);
            FontWeight = FontWeights.SemiBold;
            CornerRadius = Math.Max(2, Math.Min(cornerRadius, 18));
            Template = BuildTemplate(CornerRadius, WithAlpha(foreground, 0.16), WithAlpha(Colors.Black, 0.22));

            _accent = accent;
            _inputForeground = foreground;
            SetActive(_active);
        }

        Color _accent = Colors.SteelBlue;
        Color _inputForeground = Colors.White;
        bool _active;

        /// <summary>
        /// 多选 / 已选中的按钮：换成强调色底 + 强调色边框。
        /// 用按钮自己的字体色再算一遍，所以深色卡片和浅色卡片都读得清。
        /// </summary>
        public void SetActive(bool active)
        {
            _active = active;

            if (active)
            {
                Background = new SolidColorBrush(WithAlpha(_accent, 0.55));
                BorderBrush = new SolidColorBrush(WithAlpha(_accent, 0.95));
                Foreground = new SolidColorBrush(PickReadable(_accent, _inputForeground));
            }
            else if (Tone == CardButtonTone.Normal)
            {
                Background = new SolidColorBrush(WithAlpha(_inputForeground, 0.10));
                BorderBrush = new SolidColorBrush(WithAlpha(_inputForeground, 0.28));
                Foreground = new SolidColorBrush(_inputForeground);
            }
        }

        /// <summary>卡片自己的圆角（0 = 直角）。</summary>
        public double CornerRadius { get; private set; }

        const string ThemeAccent = "#2F6FEB";

        static Color TryColor(string hex, Color fallback)
        {
            try { return OverlaySurface.ParseColor(hex); }
            catch { return fallback; }
        }

        static Color WithAlpha(Color color, double alpha)
        {
            byte a = (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255.0);
            return Color.FromArgb(a, color.R, color.G, color.B);
        }

        /// <summary>深色底上放白字、浅色底上放深字：按底色相对亮度挑一个。</summary>
        static Color PickReadable(Color background, Color preferred)
        {
            double luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
            if (luminance > 0.55)
            {
                double preferredLuminance = (0.2126 * preferred.R + 0.7152 * preferred.G + 0.0722 * preferred.B) / 255.0;
                return preferredLuminance < 0.55 ? preferred : Color.FromRgb(0x14, 0x18, 0x1C);
            }
            return Color.FromRgb(0xFF, 0xFF, 0xFF);
        }

        static FontFamily ResolveFont(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new FontFamily("Microsoft YaHei UI");
            try { return new FontFamily(name); }
            catch { return new FontFamily("Microsoft YaHei UI"); }
        }

        /// <summary>
        /// 按钮模板：圆角底 + 边框 + 内容；鼠标悬停时叠一层亮色，按下时叠一层暗色，
        /// 键盘焦点时边框换成强调色，禁用时整块变淡。两层覆盖用 Opacity 控制，
        /// 所以同一套模板在深色 / 浅色卡片上都成立。
        /// </summary>
        static ControlTemplate BuildTemplate(double radius, Color hover, Color pressed)
        {
            string r = radius.ToString("0.##", CultureInfo.InvariantCulture);
            string hoverHex = ToHex(hover);
            string pressedHex = ToHex(pressed);

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
                "<Setter TargetName=\"Shell\" Property=\"BorderThickness\" Value=\"2\"/>" +
                "</Trigger>" +
                "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
                "<Setter TargetName=\"Shell\" Property=\"Opacity\" Value=\"0.45\"/>" +
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
