using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Contour;

namespace MirrorpondHUD.Ui;

/// <summary>叠层内容与设置面板预览共用的绘制表面：底色 / 背景图 / 圆角 / 投影 / 文字。</summary>
public sealed class OverlaySurface : Border
{
    static readonly Dictionary<string, SolidColorBrush> BrushCache =
        new(StringComparer.OrdinalIgnoreCase);

    readonly Grid _root;
    readonly Border _tintLayer;
    readonly Border _glassLayer;
    readonly Border _imageLayer;
    readonly Border _decorLayer;
    readonly Border _loopLayer;
    readonly Border _loopBand;
    readonly Border _contentLayer;
    readonly TextCanvas _canvas;
    readonly ContourDecoration _contourLayer;
    readonly Border _contourHost;
    DispatcherTimer _contourTimer;
    readonly Stopwatch _contourWatch = new();
    double _contourPhase;
    double _contourSpeed = 1.0;

    Settings _lastSettings;
    string _resolvedCardAlign;
    string _lastCardAlignSetting;

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
        _contourLayer = new ContourDecoration { Visibility = Visibility.Visible };
        _contourLayer.SizeChanged += (s, e) => _contourLayer.InvalidateVisual();
        _contourHost = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            ClipToBounds = true,
            Child = _contourLayer
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
        _root.Children.Add(_contourHost);
        _root.Children.Add(_loopLayer);
        _root.Children.Add(_loopBand);
        _root.Children.Add(_canvas);
        _root.Children.Add(_contentLayer);
        Unloaded += (s, e) => StopContourLoop();


        Child = _root;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
    }

    public TextCanvas Canvas => _canvas;

    /// <summary>装饰层是否为等高线且首帧几何已就绪；截图等离屏路径用。</summary>
    public bool ContourReady => _contourHost.Visibility != Visibility.Visible || _contourLayer.IsReady;

    /// <summary>主动预热等高线首帧；离屏渲染不会自动跑 OnRender 的请求。</summary>
    public void PrimeContour()
    {
        if (_contourHost.Visibility == Visibility.Visible) _contourLayer.Prime();
    }

    /// <summary>
    /// 布局协调器算出的整叠对齐（Left / Right）。只影响“跟随卡片对齐”的文字；
    /// 空 = 让表面按卡片位置自己判断（设置窗预览 / 截图等没有协调器的场景）。
    /// </summary>
    public void SetResolvedCardAlignment(string value)
    {
        string normalized = string.IsNullOrEmpty(value)
            ? null
            : (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase) ? "Right" : "Left");
        if (string.Equals(_resolvedCardAlign, normalized, StringComparison.Ordinal)) return;
        _resolvedCardAlign = normalized;
        UpdateTextAlignment();
    }

    /// <summary>
    /// 文字对齐：Left / Center / Right / Justify 直接映射；Card 跟随整叠对齐。
    /// 没有协调器传入的整叠对齐时，用卡片自身在屏幕上的位置兜底。
    /// </summary>
    void UpdateTextAlignment()
    {
        if (_lastSettings is null) return;

        string mode = Settings.NormalizeTextAlign(_lastSettings.Style.TextAlignment);
        if (!string.Equals(mode, "Card", StringComparison.Ordinal))
        {
            _canvas.Alignment = ParseAlignment(mode);
            return;
        }

        string cardAlign = _resolvedCardAlign;
        if (string.IsNullOrEmpty(cardAlign))
        {
            cardAlign = Settings.NormalizeCardAlign(_lastSettings.Style.CardAlign);
            if (cardAlign == "Auto") cardAlign = ResolveAutoCardAlign();
        }

        _canvas.Alignment = string.Equals(cardAlign, "Right", StringComparison.OrdinalIgnoreCase)
            ? TextAlignment.Right
            : TextAlignment.Left;
    }

    string ResolveAutoCardAlign()
    {
        try
        {
            ScreenInfo screen = ScreenGeometry.Get(_lastSettings.Overlay.Monitor);
            if (screen is null) return "Left";

            double scaleX = screen.ScaleX > 0.1 ? screen.ScaleX : 1.0;
            double workWidth = screen.WorkWidth / scaleX;
            double width = ActualWidth > 0 ? ActualWidth : 320;
            double center = _lastSettings.Overlay.X + width / 2;
            return center < workWidth / 2 ? "Left" : "Right";
        }
        catch (Exception ex)
        {
            Log.Warn($"判断文字跟随对齐失败，回落左对齐: {ex.Message}");
            return "Left";
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_lastSettings is not null
            && string.Equals(Settings.NormalizeTextAlign(_lastSettings.Style.TextAlignment),
                "Card", StringComparison.Ordinal)
            && string.IsNullOrEmpty(_resolvedCardAlign))
        {
            UpdateTextAlignment();
        }
    }

    /// <summary>
    /// 富内容里的控件能不能收到鼠标。默认 false（图片 / 视频不该挡住桌面点击）；
    /// 交互卡片（按钮 / 输入框）由宿主设成 true，否则命中测试会停在内容层外面。
    /// </summary>
    public bool ContentHitTestVisible
    {
        get => _contentLayer.IsHitTestVisible; set => _contentLayer.IsHitTestVisible = value;
    }

    /// <summary>富内容（图片 / 动图 / 视频）的宿主；有内容时文字层隐藏。</summary>
    public void SetContent(FrameworkElement content)
    {
        if (content is null)
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

    public bool HasContent => _contentLayer.Child is not null;

    /// <summary>当前富内容控件（宿主用它把焦点交给 CLI 面板的输入行）。</summary>
    public FrameworkElement ContentElement => _contentLayer.Child as FrameworkElement;

    public void SetText(string text, bool cursor) => SetText(text, cursor, -1);

    /// <summary>当前文字层内容（只重画底层时用来原样恢复）。</summary>
    public string Text => _canvas.Text;

    public void SetText(string text, bool cursor, int cursorIndex)
    {
        _canvas.Text = text ?? "";
        _canvas.CursorIndex = cursorIndex;
        _canvas.CursorVisible = cursor;
    }

    /// <summary>重新按当前内容量尺寸（切换命令行时用）。</summary>
    public void ResetStableSize() => _canvas.ResetStableSize();

    /// <summary>用于计算尺寸的文本；null = 跟随显示内容。启动动画期间传完整内容。</summary>
    public void SetLayoutText(string text) => _canvas.LayoutText = text;

    public void Apply(Settings settings, string text) => Apply(settings, text, false);

    public void Apply(Settings settings, string text, bool cursor)
    {
        StyleSettings style = settings.Style;
        OverlaySettings overlay = settings.Overlay;
        _lastSettings = settings;

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
        _canvas.LineHeight = style.LineHeight > 0 ? style.LineHeight : 0;

        // 卡片对齐设置变了，上一轮布局传下来的 Left / Right 就过期了；
        // 先清掉，让本次 Apply 按新的显式值（或卡片自身位置）排版，等下一次布局再覆盖。
        string cardAlignSetting = Settings.NormalizeCardAlign(style.CardAlign);
        if (_lastCardAlignSetting is not null
            && !string.Equals(_lastCardAlignSetting, cardAlignSetting, StringComparison.Ordinal))
        {
            _resolvedCardAlign = null;
        }
        _lastCardAlignSetting = cardAlignSetting;

        UpdateTextAlignment();

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
            if (_image is null) Log.Warn($"背景图片无法加载: {path}");
        }

        if (_image is null)
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
            _contourHost.Visibility = Visibility.Collapsed;
            StopContourLoop();
            return;
        }

        if (string.Equals(kind, "Contour", StringComparison.Ordinal))
        {
            // 等高线不是平铺画刷：它是按卡片尺寸 + 地形场实时生成的几何。
            _decorLayer.Background = null;
            _decorLayer.Visibility = Visibility.Collapsed;
            _contourHost.CornerRadius = CornerRadius;
            _contourHost.Visibility = Visibility.Visible;
            _contourLayer.Apply(style, ThemeResolver.WithAlpha(tokens.Border, 0.22));
            return;
        }

        _contourHost.Visibility = Visibility.Collapsed;
        StopContourLoop();

        DrawingBrush brush = BuildDecorationBrush(kind, tokens.Border, style.DecorationDensity);
        if (brush is null)
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
    /// M7 循环动效：None / Breath（整卡明暗） / Scanline（横线扫过） / Pulse（描边明暗） /
    /// Contour（等高线相位漂移）。
    ///
    /// 性能代价（必须知道）：
    /// - Breath / Pulse 是 WPF 合成器动画，无每帧托管分配，但持续占用 GPU 填充/合成；
    ///   Scanline 只重绘一条 2px 横线；
    /// - Contour 不是合成动画：约 30fps 重算地形与几何；基础高度网格与容器已缓存，
    ///   几何在后台生成，UI 线程只提交冻结结果（详见 ContourDecoration）。
    /// - 4K@300% 的卡片约 1200×900 物理像素；默认关闭，ReduceMotion 一票否决。
    /// - 关掉后立即 BeginAnimation(null) / 停表并折叠，不保留动画时钟。
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
        if (bandTransform is not null) bandTransform.BeginAnimation(TranslateTransform.YProperty, null);
        StopContourLoop();

        string effect = Settings.NormalizeLoopEffect(style.LoopEffect, style.LoopMotion);
        if (effect == "None" || !CardMotion.Enabled) return;

        double durationMs = Math.Max(600, Math.Min(20000, style.LoopDurationMs));
        double amplitude = Math.Max(0.02, Math.Min(0.8, style.LoopAmplitude));
        var brush = new SolidColorBrush(tokens.Accent);
        brush.Freeze();

        if (effect == "Contour")
        {
            StartContourLoop(style.ContourSpeed);
            return;
        }

        if (effect == "Scanline")
        {
            _loopBand.Background = brush;
            _loopBand.Opacity = amplitude;
            _loopBand.Visibility = Visibility.Visible;

            if (bandTransform is not null)
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

    void StartContourLoop(double styleSpeed)
    {
        if (_contourHost.Visibility != Visibility.Visible) return;

        // 原地形变：phase 只改变两套固定地形场的混合权重，不移动噪声坐标。
        // 速度 1.0 ≈ 4 秒一个形变周期；2.0 约 2 秒，0.5 约 8 秒。
        _contourSpeed = Math.Max(0.1, Math.Min(4.0, styleSpeed));

        if (_contourTimer is null)
        {
            // 130ms（约 8fps）在等高线上会明显一顿一顿；30fps 配合后台几何生成，
            // 相位推进按真实经过时间计算，UI 偶发卡顿不会让动画越走越慢。
            _contourTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _contourTimer.Tick += (s, e) =>
            {
                double elapsed = _contourWatch.IsRunning ? _contourWatch.Elapsed.TotalMilliseconds : 33.0;
                _contourWatch.Restart();
                // 休眠 / 卡顿后不要一次跳过半个周期，最多按 100ms 推进。
                if (elapsed < 0) elapsed = 0;
                if (elapsed > 100) elapsed = 100;

                _contourPhase += (2.0 * Math.PI * elapsed / 4000.0) * _contourSpeed;
                if (_contourPhase > 2.0 * Math.PI) _contourPhase -= 2.0 * Math.PI;
                _contourLayer.SetPhase(_contourPhase);
            };
        }
        if (!_contourTimer.IsEnabled)
        {
            _contourWatch.Restart();
            _contourTimer.Start();
        }
    }

    void StopContourLoop()
    {
        if (_contourTimer is not null && _contourTimer.IsEnabled) _contourTimer.Stop();
        _contourWatch.Stop();
    }

    static DrawingBrush BuildDecorationBrush(string kind, Color color, double density)
    {
        double densityValue = Math.Max(0.0, Math.Min(2.0, density));
        double factor = densityValue <= 1.0
            ? 1.6 - 1.2 * densityValue
            : Math.Max(0.18, 0.4 / densityValue);
        double size;
        Drawing drawing;

        switch (kind)
        {
            case "Grid":
                size = 24 * factor;
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
                size = 4 * factor;
                drawing = Stroked(new LineGeometry(new Point(0, 0), new Point(size, 0)), color);
                break;

            case "Hatch":
                size = 12 * factor;
                drawing = Stroked(new LineGeometry(new Point(0, size), new Point(size, 0)), color);
                break;

            case "Halftone":
                size = 8 * factor;
                var dot = new SolidColorBrush(color);
                dot.Freeze();
                drawing = new GeometryDrawing(dot, null,
                    new EllipseGeometry(new Point(size / 2, size / 2), 1.1, 1.1));
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
            Log.Warn($"加载背景图片失败: {ex.Message}");
            return null;
        }
    }

    // ==================== 毛玻璃 ====================

    /// <summary>设置玻璃底图（已经在 CPU 上模糊好的小图，直接铺满卡片）。</summary>
    public void SetGlass(ImageSource image)
    {
        if (image is null)
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
            case "card":
            case "follow":
            case "followcard": return TextAlignment.Left;
            default: return TextAlignment.Left;
        }
    }

    public static SolidColorBrush GetBrush(string hex, double opacity)
    {
        string key = $"{(hex ?? "#000000")}|{opacity:0.00}";
        if (BrushCache.TryGetValue(key, out var cached)) return cached;

        Color color = ParseColor(hex);
        color.A = (byte)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    /// <summary>解析颜色；非法值返回白色（取色预览等“总能画出来”的场景用）。</summary>
    public static Color ParseColor(string hex) => ParseColor(hex, Colors.White);

    /// <summary>解析颜色；非法值返回 <paramref name="fallback"/>。唯一实现委托给 <see cref="ColorCode"/>。</summary>
    public static Color ParseColor(string hex, Color fallback)
    {
        if (!ColorCode.TryParse(hex, out var a, out var r, out var g, out var b)) return fallback;
        return Color.FromArgb(a, r, g, b);
    }
}
