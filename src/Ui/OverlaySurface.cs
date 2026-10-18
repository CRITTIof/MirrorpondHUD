using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    /// <summary>叠层内容与设置面板预览共用的绘制表面。</summary>
    public sealed class OverlaySurface : Border
    {
        static readonly Dictionary<string, SolidColorBrush> BrushCache =
            new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        readonly TextBlock _text;

        public OverlaySurface()
        {
            _text = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                SnapsToDevicePixels = true
            };

            Child = _text;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        }

        public TextBlock Label { get { return _text; } }

        public void Apply(Settings settings, string text)
        {
            StyleSettings style = settings.Style;
            OverlaySettings overlay = settings.Overlay;

            _text.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(style.FontFamily)
                ? "Microsoft YaHei UI"
                : style.FontFamily);
            _text.FontSize = Math.Max(4, style.FontSize);
            _text.FontWeight = style.Bold ? FontWeights.SemiBold : FontWeights.Normal;
            _text.Foreground = GetBrush(style.Foreground, 1.0);
            _text.TextAlignment = ParseAlignment(style.TextAlignment);
            _text.LineHeight = style.LineHeight > 0 ? style.LineHeight : double.NaN;
            _text.LineStackingStrategy = style.LineHeight > 0
                ? LineStackingStrategy.BlockLineHeight
                : LineStackingStrategy.MaxHeight;
            _text.Text = text ?? "";

            Background = GetBrush(style.Background, style.BackgroundOpacity);
            CornerRadius = new CornerRadius(Math.Max(0, overlay.CornerRadius));

            double pad = Math.Max(0, style.Padding);
            Padding = new Thickness(pad * 1.15, pad * 0.7, pad * 1.15, pad * 0.7);

            if (overlay.Shadow)
            {
                Effect = new DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 4,
                    Direction = 270,
                    Opacity = 0.55,
                    Color = Colors.Black
                };
                Margin = new Thickness(10);
            }
            else
            {
                Effect = null;
                Margin = new Thickness(0);
            }
        }

        public static TextAlignment ParseAlignment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return TextAlignment.Left;
            switch (value.Trim().ToLowerInvariant())
            {
                case "center": return TextAlignment.Center;
                case "right": return TextAlignment.Right;
                case "justify": return TextAlignment.Justify;
                default: return TextAlignment.Left;
            }
        }

        public static SolidColorBrush GetBrush(string hex, double opacity)
        {
            string key = (hex ?? "#000000") + "|" + opacity.ToString("0.00");
            if (BrushCache.TryGetValue(key, out SolidColorBrush cached)) return cached;

            Color color = ParseColor(hex);
            color.A = (byte)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            BrushCache[key] = brush;
            return brush;
        }

        public static Color ParseColor(string hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && hex.TrimStart().StartsWith("#"))
                {
                    string s = hex.Trim().TrimStart('#');
                    if (s.Length == 6)
                    {
                        return Color.FromRgb(
                            Convert.ToByte(s.Substring(0, 2), 16),
                            Convert.ToByte(s.Substring(2, 2), 16),
                            Convert.ToByte(s.Substring(4, 2), 16));
                    }
                    if (s.Length == 8)
                    {
                        return Color.FromArgb(
                            Convert.ToByte(s.Substring(0, 2), 16),
                            Convert.ToByte(s.Substring(2, 2), 16),
                            Convert.ToByte(s.Substring(4, 2), 16),
                            Convert.ToByte(s.Substring(6, 2), 16));
                    }
                }
            }
            catch { }
            return Colors.White;
        }
    }
}
