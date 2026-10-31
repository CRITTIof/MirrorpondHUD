using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 卡片里的单行 / 多行输入框：底色、描边、光标、选区都跟着卡片文字色走，
    /// 所以深色卡片上不会出现一块刺眼的白框。
    /// </summary>
    public class CardTextBox : TextBox
    {
        public CardTextBox()
        {
            BorderThickness = new Thickness(1);
            Padding = new Thickness(7, 4, 7, 4);
            VerticalContentAlignment = VerticalAlignment.Center;
            Background = Brushes.Transparent;
            Foreground = Brushes.White;
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            if (style == null) return;

            Color foreground = OverlaySurface.ParseColor(style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            Foreground = new SolidColorBrush(foreground);
            CaretBrush = new SolidColorBrush(foreground);
            SelectionBrush = new SolidColorBrush(Alpha(foreground, 0.35));
            Background = new SolidColorBrush(Alpha(foreground, 0.07));
            BorderBrush = new SolidColorBrush(Alpha(foreground, 0.24));
            FontFamily = ResolveFont(style.FontFamily);
            FontSize = Math.Max(9, style.FontSize);

            // TextBox 没有 CornerRadius，套一个圆角外框当背景：模板里用同色的 Border 画。
            Template = BuildTemplate(Math.Max(2, Math.Min(cornerRadius, 16)));
        }

        static ControlTemplate BuildTemplate(double radius)
        {
            string r = radius.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string xaml =
                "<ControlTemplate TargetType=\"TextBox\" " +
                "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Border CornerRadius=\"" + r + "\" Background=\"{TemplateBinding Background}\" " +
                "BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\">" +
                "<ScrollViewer x:Name=\"PART_ContentHost\" Margin=\"{TemplateBinding Padding}\" " +
                "VerticalAlignment=\"Center\"/>" +
                "</Border>" +
                "</ControlTemplate>";

            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        static Color Alpha(Color color, double alpha)
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
    }
}
