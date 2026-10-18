using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    /// <summary>叠层内容与设置面板预览共用的绘制表面：底色 / 背景图 / 圆角 / 投影 / 文字。</summary>
    public sealed class OverlaySurface : Border
    {
        static readonly Dictionary<string, SolidColorBrush> BrushCache =
            new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        readonly Grid _root;
        readonly Border _imageLayer;
        readonly TextCanvas _canvas;

        BitmapImage _image;
        string _imagePath;

        public OverlaySurface()
        {
            _canvas = new TextCanvas();
            _imageLayer = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            _root = new Grid();
            _root.Children.Add(_imageLayer);
            _root.Children.Add(_canvas);

            Child = _root;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        }

        public TextCanvas Canvas { get { return _canvas; } }

        public void SetText(string text, bool cursor)
        {
            SetText(text, cursor, -1);
        }

        public void SetText(string text, bool cursor, int cursorIndex)
        {
            _canvas.Text = text ?? "";
            _canvas.CursorIndex = cursorIndex;
            _canvas.CursorVisible = cursor;
        }

        /// <summary>重新按当前内容量尺寸（切换命令行时用）。</summary>
        public void ResetStableSize()
        {
            _canvas.ResetStableSize();
        }

        /// <summary>用于计算尺寸的文本；null = 跟随显示内容。启动动画期间传完整内容。</summary>
        public void SetLayoutText(string text)
        {
            _canvas.LayoutText = text;
        }

        public void Apply(Settings settings, string text)
        {
            Apply(settings, text, false);
        }

        public void Apply(Settings settings, string text, bool cursor)
        {
            StyleSettings style = settings.Style;
            OverlaySettings overlay = settings.Overlay;

            _canvas.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(style.FontFamily)
                ? "Microsoft YaHei UI"
                : style.FontFamily);
            _canvas.FontSize = Math.Max(4, style.FontSize);
            _canvas.FontWeight = style.Bold ? FontWeights.SemiBold : FontWeights.Normal;
            _canvas.Foreground = GetBrush(style.Foreground, 1.0);
            _canvas.Alignment = ParseAlignment(style.TextAlignment);
            _canvas.LineHeight = style.LineHeight > 0 ? style.LineHeight : 0;

            _canvas.Outline = style.TextOutline && style.OutlineThickness > 0.05;
            _canvas.OutlineBrush = GetBrush(style.OutlineColor, 1.0);
            _canvas.OutlineThickness = Math.Max(0.2, style.OutlineThickness);

            _canvas.FixedWidth = Math.Max(0, style.FixedWidth);
            _canvas.CursorStyle = Settings.NormalizeCursor(settings.Behavior.CursorStyle);
            _canvas.CursorBlink = settings.Behavior.CursorBlink;

            _canvas.ResetStableSize();
            _canvas.Text = text ?? "";
            _canvas.CursorVisible = cursor;

            Background = GetBrush(style.Background, style.BackgroundOpacity);
            CornerRadius = new CornerRadius(Math.Max(0, overlay.CornerRadius));
            Padding = new Thickness(0);

            double pad = Math.Max(0, style.Padding);
            _canvas.Margin = new Thickness(pad * 1.15, pad * 0.7, pad * 1.15, pad * 0.7);

            _imageLayer.CornerRadius = CornerRadius;
            ApplyImage(style);

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

        // ==================== 背景图片 ====================

        void ApplyImage(StyleSettings style)
        {
            string path = (style.BackgroundImage ?? "").Trim();
            if (path.Length == 0)
            {
                _imageLayer.Visibility = Visibility.Collapsed;
                _imageLayer.Background = null;
                _image = null;
                _imagePath = null;
                return;
            }

            if (!string.Equals(path, _imagePath, StringComparison.OrdinalIgnoreCase))
            {
                _image = LoadImage(path);
                _imagePath = path;
                if (_image == null) Log.Warn("背景图片无法加载: " + path);
            }

            if (_image == null)
            {
                _imageLayer.Visibility = Visibility.Collapsed;
                return;
            }

            var brush = new ImageBrush(_image)
            {
                Stretch = ParseStretch(style.ImageStretch),
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None
            };
            brush.Freeze();

            _imageLayer.Background = brush;
            _imageLayer.Opacity = Math.Max(0, Math.Min(1, style.ImageOpacity));
            _imageLayer.Visibility = Visibility.Visible;
        }

        static BitmapImage LoadImage(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Warn("加载背景图片失败: " + ex.Message);
                return null;
            }
        }

        public static Stretch ParseStretch(string value)
        {
            switch (Settings.NormalizeStretch(value))
            {
                case "Uniform": return Stretch.Uniform;
                case "Fill": return Stretch.Fill;
                case "None": return Stretch.None;
                default: return Stretch.UniformToFill;
            }
        }

        // ==================== 颜色 ====================

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
