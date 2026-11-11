using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>叠层内容与设置面板预览共用的绘制表面：底色 / 背景图 / 圆角 / 投影 / 文字。</summary>
    public sealed class OverlaySurface : Border
    {
        static readonly Dictionary<string, SolidColorBrush> BrushCache =
            new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        readonly Grid _root;
        readonly Border _tintLayer;
        readonly Border _glassLayer;
        readonly Border _imageLayer;
        readonly Border _decorLayer;
        readonly Border _loopLayer;
        readonly Border _loopBand;
        readonly Border _contentLayer;
        readonly TextCanvas _canvas;

        BitmapImage _image;
        string _imagePath;

        public OverlaySurface()
        {
            _canvas = new TextCanvas();
            _tintLayer = new Border { IsHitTestVisible = false };
            _glassLayer = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _imageLayer = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            // M6：装饰纹样画在底色 / 背景图之上、文字与富内容之下；不参与命中测试。
            _decorLayer = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            // M7 循环呼吸：常驻合成动画；默认关闭，描述见 OverlaySurface.ApplyLoopMotion。
            _loopLayer = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Opacity = 0
            };
            _loopBand = new Border
            {
                Height = 2,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransform = new TranslateTransform()
            };
            _contentLayer = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                ClipToBounds = true
            };

            _root = new Grid();
            _root.Children.Add(_glassLayer);
            _root.Children.Add(_tintLayer);
            _root.Children.Add(_imageLayer);
            _root.Children.Add(_decorLayer);
            _root.Children.Add(_loopLayer);
            _root.Children.Add(_loopBand);
            _root.Children.Add(_canvas);
            _root.Children.Add(_contentLayer);


            Child = _root;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        }

        public TextCanvas Canvas { get { return _canvas; } }

        /// <summary>
        /// 富内容里的控件能不能收到鼠标。默认 false（图片 / 视频不该挡住桌面点击）；
        /// 交互卡片（按钮 / 输入框）由宿主设成 true，否则命中测试会停在内容层外面。
        /// </summary>
        public bool ContentHitTestVisible
        {
            get { return _contentLayer.IsHitTestVisible; }
            set { _contentLayer.IsHitTestVisible = value; }
        }

        /// <summary>富内容（图片 / 动图 / 视频）的宿主；有内容时文字层隐藏。</summary>
        public void SetContent(FrameworkElement content)
        {
            if (content == null)
            {
                ClearContent();
                return;
            }

            _contentLayer.Child = content;
            _contentLayer.Visibility = Visibility.Visible;
            _canvas.Visibility = Visibility.Collapsed;
        }

        public void ClearContent()
        {
            _contentLayer.Child = null;
            _contentLayer.Visibility = Visibility.Collapsed;
            _canvas.Visibility = Visibility.Visible;
        }

        public bool HasContent { get { return _contentLayer.Child != null; } }

        /// <summary>当前富内容控件（宿主用它把焦点交给 CLI 面板的输入行）。</summary>
        public FrameworkElement ContentElement { get { return _contentLayer.Child as FrameworkElement; } }

        public void SetText(string text, bool cursor)
        {
            SetText(text, cursor, -1);
        }

        /// <summary>当前文字层内容（只重画底层时用来原样恢复）。</summary>
        public string Text { get { return _canvas.Text; } }

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
            ThemeTokens tokens = ThemeResolver.Resolve(style);
            _canvas.MonoFont = new FontFamily(tokens.MonoFont);
            _canvas.MonoNumbers = style.MonoReadouts;
            _canvas.FontSize = Math.Max(4, style.FontSize);
            _canvas.IconScale = style.IconScale > 0 ? style.IconScale : 1.0;
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

            Background = Brushes.Transparent;
            CornerRadius = new CornerRadius(Math.Max(0, overlay.CornerRadius));
            _tintLayer.Background = GetBrush(style.Background, style.BackgroundOpacity);
            _tintLayer.CornerRadius = CornerRadius;
            _glassLayer.CornerRadius = CornerRadius;
            if (!style.Acrylic) ClearGlass();
            Padding = new Thickness(0);

            double pad = Math.Max(0, style.Padding);
            // 富内容（图片 / 视频）铺满卡片，文字按内边距缩进。
            _canvas.Margin = new Thickness(pad * 1.15, pad * 0.7, pad * 1.15, pad * 0.7);
            _contentLayer.Margin = new Thickness(0);
            _contentLayer.CornerRadius = CornerRadius;

            _imageLayer.CornerRadius = CornerRadius;
            ApplyImage(style);
            ApplyDecoration(style, tokens);
            ApplyLoopMotion(style, tokens);

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

        // ==================== M6 装饰纹样 ====================

        /// <summary>
        /// 低透明度纹样只作点缀：网格 / 扫描线 / 45° 斜纹 / 半调网点 / 等高线。
        /// 用冻结的 DrawingBrush 平铺，不建可视树、不参与命中测试；None 时整层折叠。
        /// </summary>
        void ApplyDecoration(StyleSettings style, ThemeTokens tokens)
        {
            string kind = Settings.NormalizeDecoration(style.Decoration);
            if (string.Equals(kind, "None", StringComparison.Ordinal))
            {
                _decorLayer.Background = null;
                _decorLayer.Visibility = Visibility.Collapsed;
                return;
            }

            DrawingBrush brush = BuildDecorationBrush(kind, tokens.Border);
            if (brush == null)
            {
                _decorLayer.Background = null;
                _decorLayer.Visibility = Visibility.Collapsed;
                return;
            }

            _decorLayer.CornerRadius = CornerRadius;
            _decorLayer.Background = brush;
            // 纹样只做“坐标系”暗示：32% 是深色底上可见、又不压过文字的档位。
            _decorLayer.Opacity = 0.32;
            _decorLayer.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// M7 循环动效：None / Breath（整卡明暗） / Scanline（横线扫过） / Pulse（描边明暗）。
        ///
        /// 性能代价（必须知道）：
        /// - 每个开启的卡片有一个**常驻**合成动画；不经过 DispatcherTimer，不产生每帧托管分配，
        ///   但 GPU 合成器会持续重绘这一层卡片大小的矩形（Scanline 只重绘一条 2px 横线）。
        /// - 4K@300% 的卡片约 1200×900 物理像素，4 张 Breath 卡约 4 个全卡面积/帧的填充率；
        ///   Scanline 的成本远低于 Breath；Pulse 只重绘 1px 描边。
        /// - 集成显卡 / 远程桌面 / 高刷新屏上会多占一点 GPU 与电量；默认关闭，ReduceMotion 一票否决。
        /// - 关掉后立即 BeginAnimation(null) 并折叠，不保留动画时钟。
        /// </summary>
        void ApplyLoopMotion(StyleSettings style, ThemeTokens tokens)
        {
            _loopLayer.CornerRadius = CornerRadius;
            _loopLayer.BeginAnimation(UIElement.OpacityProperty, null);
            _loopLayer.Opacity = 0;
            _loopLayer.Background = null;
            _loopLayer.BorderBrush = null;
            _loopLayer.BorderThickness = new Thickness(0);
            _loopLayer.Visibility = Visibility.Collapsed;

            _loopBand.BeginAnimation(UIElement.OpacityProperty, null);
            _loopBand.Opacity = 0;
            _loopBand.Background = null;
            _loopBand.Visibility = Visibility.Collapsed;
            var bandTransform = _loopBand.RenderTransform as TranslateTransform;
            if (bandTransform != null) bandTransform.BeginAnimation(TranslateTransform.YProperty, null);

            string effect = Settings.NormalizeLoopEffect(style.LoopEffect, style.LoopMotion);
            if (effect == "None" || !CardMotion.Enabled) return;

            double durationMs = Math.Max(600, Math.Min(20000, style.LoopDurationMs));
            double amplitude = Math.Max(0.02, Math.Min(0.8, style.LoopAmplitude));
            var brush = new SolidColorBrush(tokens.Accent);
            brush.Freeze();

            if (effect == "Scanline")
            {
                _loopBand.Background = brush;
                _loopBand.Opacity = amplitude;
                _loopBand.Visibility = Visibility.Visible;

                if (bandTransform != null)
                {
                    // 固定 800 DIP 的行程覆盖常见卡片高度；超出部分在窗口外，不可见。
                    var sweep = new DoubleAnimation(-4, 800, TimeSpan.FromMilliseconds(durationMs))
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    bandTransform.BeginAnimation(TranslateTransform.YProperty, sweep);
                }
                return;
            }

            _loopLayer.Visibility = Visibility.Visible;
            if (effect == "Pulse")
            {
                // 描边脉冲：只重绘 1px 边框，比整卡填充便宜。
                _loopLayer.BorderBrush = brush;
                _loopLayer.BorderThickness = new Thickness(1);
            }
            else
            {
                _loopLayer.Background = brush;
            }

            var pulse = new DoubleAnimation(0.0, amplitude, TimeSpan.FromMilliseconds(durationMs / 2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            _loopLayer.BeginAnimation(UIElement.OpacityProperty, pulse);
        }

        static DrawingBrush BuildDecorationBrush(string kind, Color color)
        {
            double size;
            Drawing drawing;

            switch (kind)
            {
                case "Grid":
                    size = 24;
                    drawing = Stroked(new GeometryGroup
                    {
                        Children =
                        {
                            new LineGeometry(new Point(0, 0), new Point(size, 0)),
                            new LineGeometry(new Point(0, 0), new Point(0, size))
                        }
                    }, color);
                    break;

                case "Scanline":
                    size = 4;
                    drawing = Stroked(new LineGeometry(new Point(0, 0), new Point(size, 0)), color);
                    break;

                case "Hatch":
                    size = 12;
                    drawing = Stroked(new LineGeometry(new Point(0, size), new Point(size, 0)), color);
                    break;

                case "Halftone":
                    size = 8;
                    var dot = new SolidColorBrush(color);
                    dot.Freeze();
                    drawing = new GeometryDrawing(dot, null,
                        new EllipseGeometry(new Point(size / 2, size / 2), 1.1, 1.1));
                    break;

                case "Contour":
                    // 不规则波形等高线 + 两个偏移闭合环；不要画同心圆（那会像靶心 / 排队）。
                    size = 96;
                    drawing = Stroked(BuildContourGeometry(), color);
                    break;

                default:
                    return null;
            }

            var brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, size, size),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        static Drawing Stroked(Geometry geometry, Color color)
        {
            var pen = new Pen(new SolidColorBrush(color), 1);
            pen.Freeze();
            var drawing = new GeometryDrawing(null, pen, geometry);
            drawing.Freeze();
            return drawing;
        }

        /// <summary>
        /// 等高线纹样的“地形”部分：几条相位 / 振幅不同的波形线 + 两个偏心闭合环。
        /// 波形在 x=0 与 x=96 处同高，平铺接缝连续；闭合环只作为局部山头，不是同心圆。
        /// </summary>
        static Geometry BuildContourGeometry()
        {
            var group = new GeometryGroup();

            for (int i = 0; i < 5; i++)
            {
                double y = 10 + i * 19;
                double amp = i % 2 == 0 ? 7 : 4;
                var line = new StreamGeometry();
                using (StreamGeometryContext ctx = line.Open())
                {
                    ctx.BeginFigure(new Point(0, y), false, false);
                    ctx.BezierTo(new Point(24, y - amp), new Point(48, y + amp), new Point(72, y), true, false);
                    ctx.BezierTo(new Point(82, y - amp * 0.6), new Point(90, y + amp * 0.6),
                                 new Point(96, y), true, false);
                }
                line.Freeze();
                group.Children.Add(line);
            }

            // 两个偏心的闭合“山头”，形状不对称；与波形错开，避免排成一行。
            var hillA = new StreamGeometry();
            using (StreamGeometryContext ctx = hillA.Open())
            {
                ctx.BeginFigure(new Point(24, 38), true, true);
                ctx.BezierTo(new Point(30, 28), new Point(46, 26), new Point(52, 36),
                             true, false);
                ctx.BezierTo(new Point(57, 45), new Point(42, 52), new Point(30, 46),
                             true, false);
                ctx.BezierTo(new Point(26, 43), new Point(23, 41), new Point(24, 38),
                             true, false);
            }
            hillA.Freeze();
            group.Children.Add(hillA);

            var hillB = new StreamGeometry();
            using (StreamGeometryContext ctx = hillB.Open())
            {
                ctx.BeginFigure(new Point(66, 72), true, true);
                ctx.BezierTo(new Point(72, 62), new Point(86, 64), new Point(88, 73),
                             true, false);
                ctx.BezierTo(new Point(90, 82), new Point(77, 88), new Point(68, 82),
                             true, false);
                ctx.BezierTo(new Point(65, 79), new Point(64, 75), new Point(66, 72),
                             true, false);
            }
            hillB.Freeze();
            group.Children.Add(hillB);

            group.Freeze();
            return group;
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

        // ==================== 毛玻璃 ====================

        /// <summary>设置玻璃底图（已经在 CPU 上模糊好的小图，直接铺满卡片）。</summary>
        public void SetGlass(ImageSource image)
        {
            if (image == null)
            {
                ClearGlass();
                return;
            }

            _glassLayer.Child = null;

            var brush = new ImageBrush(image)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            brush.Freeze();

            // 取样图很小（几十像素），放大时用高质量插值，边缘不会出现方块。
            RenderOptions.SetBitmapScalingMode(_glassLayer, BitmapScalingMode.HighQuality);

            _glassLayer.Background = brush;
            _glassLayer.Visibility = Visibility.Visible;
        }

        public void ClearGlass()
        {
            _glassLayer.Background = null;
            _glassLayer.Child = null;
            _glassLayer.Visibility = Visibility.Collapsed;
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

        /// <summary>解析颜色；非法值返回白色（取色预览等“总能画出来”的场景用）。</summary>
        public static Color ParseColor(string hex)
        {
            return ParseColor(hex, Colors.White);
        }

        /// <summary>解析颜色；非法值返回 <paramref name="fallback"/>。唯一实现委托给 <see cref="ColorCode"/>。</summary>
        public static Color ParseColor(string hex, Color fallback)
        {
            byte a, r, g, b;
            if (!ColorCode.TryParse(hex, out a, out r, out g, out b)) return fallback;
            return Color.FromArgb(a, r, g, b);
        }
    }
}
