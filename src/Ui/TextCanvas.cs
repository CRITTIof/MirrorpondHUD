using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 自绘文本表面：文字描边、命令行光标、以及"只增不减"的稳定尺寸。
///
/// 用 FormattedText 而不是 TextBlock，是因为需要同时拿到：
///  · 文字轮廓几何（BuildGeometry → 描边）
///  · 最后一行的精确像素宽度（光标定位）
///  · 与显示内容解耦的布局文本（启动动画期间尺寸不随逐字打字变化）
/// </summary>
public sealed class TextCanvas : FrameworkElement
{
    readonly DispatcherTimer _blink;
    bool _blinkOn = true;

    double _stableWidth;
    double _stableHeight;
    bool _hasStable;

    string _text = "";
    string _layoutText;

    FontFamily _fontFamily = new("Microsoft YaHei UI");
    FontFamily _monoFamily;
    bool _monoNumbers;
    double _fontSize = 15;
    double _iconScale = 1.15;
    FontWeight _fontWeight = FontWeights.Normal;
    Brush _foreground = Brushes.White;
    double _lineHeight;
    TextAlignment _alignment = TextAlignment.Left;

    bool _outline;
    Brush _outlineBrush = Brushes.Black;
    double _outlineThickness = 1.2;

    double _fixedWidth;

    bool _cursorVisible;
    string _cursorStyle = "Block";
    bool _cursorBlink = true;
    int _cursorIndex = -1;

    static FontFamily _keycapFamily;

    /// <summary>
    /// 内嵌的键帽字体 Reicon（只用 U+E000–U+E052 这 83 个键帽字形，图标码位不映射）。
    /// 源码里的 `{keys}` 会先被 <see cref="MirrorpondHUD.Core.Keycaps"/> 换成这些 PUA 字符，
    /// 再在这里按 run 单独用这个字体绘制，所以可以和普通文字混排。
    /// </summary>
    static FontFamily KeycapFamily
    {
        get
        {
            if (_keycapFamily is null)
            {
                try
                {
                    _keycapFamily = new FontFamily(new Uri("pack://application:,,,/"),
                                                   "./assets/keycaps/#ReiconFont");
                }
                catch
                {
                    _keycapFamily = new FontFamily("Microsoft YaHei UI");
                }
            }
            return _keycapFamily;
        }
    }

    static bool IsKeycapChar(char c) =>
        // 键帽 U+E000–U+E056（{keys} 里的按键字形）。
        c >= '\uE000' && c <= '\uE056';

    /// <summary>
    /// 图标字形：只认 <see cref="Icons"/> 登记过的 Lucide 码位。不能按 U+E100–U+E3FF 整段判断：
    /// Lucide 和键帽字体共用 PUA，整段判断会把键帽（U+E000–U+E056）误当图标。
    /// </summary>
    static bool IsIconChar(char c) => Icons.IsIconChar(c);

    public TextCanvas()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        _blink = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(530)
        };
        _blink.Tick += (s, e) =>
        {
            _blinkOn = !_blinkOn;
            InvalidateVisual();
        };

        // 卡片窗口关闭 / 隐藏时停掉闪烁定时器：否则 DispatcherTimer 会一直
        // 钉住整棵 OverlaySurface 可视树，隐藏的命令行卡仍每 530ms InvalidateVisual。
        IsVisibleChanged += (s, e) => UpdateBlinkTimer(false);
        Loaded += (s, e) => UpdateBlinkTimer(false);
        Unloaded += (s, e) => _blink.Stop();
    }

    void UpdateBlinkTimer(bool restart)
    {
        if (!_cursorVisible || !_cursorBlink || !IsVisible || !IsLoaded)
        {
            _blink.Stop();
            return;
        }

        if (restart) _blink.Stop();
        if (!_blink.IsEnabled) _blink.Start();
    }

    // ==================== 内容 ====================

    /// <summary>实际显示的文字。</summary>
    public string Text
    {
        get => _text;
        set
        {
            value = value ?? "";
            if (string.Equals(_text, value, StringComparison.Ordinal)) return;
            _text = value;
            _blinkOn = true;
            UpdateBlinkTimer(true);
            // 排版缓存以文本字符串为键，换文本自然 miss，不能在这里清表：
            // 清表会把 Measure 正在用的布局文本一起丢掉，启动动画里两边就又会各排一遍。
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 用来计算尺寸的文字（为空则用 <see cref="Text"/>）。
    /// 启动动画期间设为完整内容，这样逐字打字时整块尺寸不会抖动。
    /// </summary>
    public string LayoutText
    {
        get => _layoutText;
        set
        {
            if (string.Equals(_layoutText, value, StringComparison.Ordinal)) return;
            _layoutText = value;
            // 同 Text：排版缓存按字符串区分，换布局文本只是换一个键，不清表。
            InvalidateMeasure();
        }
    }

    /// <summary>清掉"只增不减"的尺寸记忆（换字体、字号等设置后调用）。</summary>
    public void ResetStableSize()
    {
        _hasStable = false;
        _stableWidth = 0;
        _stableHeight = 0;
        // 这是"重新来过"的信号，连排版缓存一起丢：调用方换的是整套外观，旧排版用不上。
        InvalidateLayoutCache();
        InvalidateMeasure();
    }

    // ==================== 字体与样式 ====================

    public FontFamily FontFamily
    {
        get => _fontFamily;
        set
        {
            if (value is null || value.Equals(_fontFamily)) return;
            _fontFamily = value;
            InvalidateMetrics();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// M4：数字串使用的等宽字体。null = 不启用（数字仍用 FontFamily）。
    /// 只影响 0–9 组成的 run，标点 / 单位 / 中文仍走正文；缓存会随属性变化整表作废。
    /// </summary>
    public FontFamily MonoFont
    {
        get => _monoFamily;
        set
        {
            if (value is null || value.Equals(_monoFamily)) return;
            _monoFamily = value;
            InvalidateLayoutCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>M4：是否把数字串单独用 <see cref="MonoFont"/> 排版（默认 false）。</summary>
    public bool MonoNumbers
    {
        get => _monoNumbers;
        set
        {
            if (value == _monoNumbers) return;
            _monoNumbers = value;
            InvalidateLayoutCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            value = Math.Max(2, value);
            if (Math.Abs(_fontSize - value) < 0.01) return;
            _fontSize = value;
            InvalidateMetrics();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 图标大小倍率：图标字形按「正文同字号」绘制，再乘这个倍率。
    /// Lucide 是线条图标，视觉重量比实心字形轻，默认 1.15 让图标略大一圈；
    /// 垂直方向不再靠倍率凑，而是逐个字形按墨迹中心对齐正文（见 TextRun.OffsetY）。
    /// </summary>
    public double IconScale
    {
        get => _iconScale;
        set
        {
            value = Math.Max(0.4, Math.Min(3, value));
            if (Math.Abs(_iconScale - value) < 0.005) return;
            _iconScale = value;
            // 图标 run 的 FormattedText 字号与行高都会变；正文基线不受影响，不用 InvalidateMetrics。
            InvalidateLayoutCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public FontWeight FontWeight
    {
        get => _fontWeight;
        set
        {
            if (value == _fontWeight) return;
            _fontWeight = value;
            InvalidateMetrics();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public Brush Foreground
    {
        get => _foreground;
        set
        {
            if (value is null || value.Equals(_foreground)) return;
            _foreground = value;
            // 每个 run 的 FormattedText 都持有这个画刷，缓存的排版必须连画刷一起作废。
            InvalidateLayoutCache();
            InvalidateVisual();
        }
    }

    public double LineHeight
    {
        get => _lineHeight;
        set
        {
            if (Math.Abs(_lineHeight - value) < 0.01) return;
            _lineHeight = value;
            // 行高只改行间距、不改 run 排版，但一并作废最省心：
            // 免得以后行高真参与行内布局时，这里留着"改了行高却复用旧排版"的口子。
            InvalidateLayoutCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public TextAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (value == _alignment) return;
            _alignment = value;
            InvalidateVisual();
        }
    }

    public bool Outline
    {
        get => _outline;
        set
        {
            if (value == _outline) return;
            _outline = value;
            InvalidateVisual();
        }
    }

    public Brush OutlineBrush
    {
        get => _outlineBrush;
        set
        {
            if (value is null || value.Equals(_outlineBrush)) return;
            _outlineBrush = value;
            InvalidateVisual();
        }
    }

    public double OutlineThickness
    {
        get => _outlineThickness;
        set
        {
            if (Math.Abs(_outlineThickness - value) < 0.01) return;
            _outlineThickness = value;
            InvalidateVisual();
        }
    }

    /// <summary>固定内容宽度；0 = 自适应（但仍然只增不减）。</summary>
    public double FixedWidth
    {
        get => _fixedWidth;
        set
        {
            if (Math.Abs(_fixedWidth - value) < 0.01) return;
            _fixedWidth = value;
            InvalidateMeasure();
        }
    }

    // ==================== 光标 ====================

    public bool CursorVisible
    {
        get => _cursorVisible;
        set
        {
            if (value == _cursorVisible) return;
            _cursorVisible = value;
            _blinkOn = true;
            UpdateBlinkTimer(true);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public string CursorStyle
    {
        get => _cursorStyle;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? "Block" : value.Trim();
            if (string.Equals(_cursorStyle, value, StringComparison.OrdinalIgnoreCase)) return;
            _cursorStyle = value;
            InvalidateVisual();
        }
    }

    /// <summary>光标在文本里的字符位置；-1 = 文本末尾。</summary>
    public int CursorIndex
    {
        get => _cursorIndex;
        set
        {
            if (_cursorIndex == value) return;
            _cursorIndex = value;
            InvalidateVisual();
        }
    }

    public bool CursorBlink
    {
        get => _cursorBlink;
        set
        {
            if (value == _cursorBlink) return;
            _cursorBlink = value;
            if (!value)
            {
                _blink.Stop();
                _blinkOn = true;
            }
            else
            {
                UpdateBlinkTimer(true);
            }
            InvalidateVisual();
        }
    }

    // ==================== 度量 ====================

    protected override Size MeasureOverride(Size availableSize)
    {
        PerformanceProbe.TextMeasure();
        // P-1：Measure 与 OnRender 共用同一份排版。这里按布局文本取缓存，
        // 布局文本没变时（启动动画逐字打字期间）一次 BuildLine 都不用做。
        TextLayout layout = GetLayout(_layoutText ?? _text);
        double lineHeight = LineHeightOf();

        double width = layout.Width;

        // 光标画在行尾，给右侧留一点余量，避免被裁掉
        if (_cursorVisible) width += Math.Max(3, _fontSize * 0.75);

        double height = Math.Max(1, lineHeight * layout.Lines.Count);
        width = Math.Max(1, width);

        if (_fixedWidth > 0) width = _fixedWidth;

        // 只增不减：数据从 "5%" 变成 "100%" 时窗口会长大一次，
        // 之后即使数值变短也不会缩回去，位置就不会来回跳。
        if (_hasStable)
        {
            width = Math.Max(width, _stableWidth);
            height = Math.Max(height, _stableHeight);
        }

        _stableWidth = width;
        _stableHeight = height;
        _hasStable = true;

        return new Size(Math.Ceiling(width), Math.Ceiling(height));
    }

    double LineHeightOf()
    {
        if (_lineHeight > 0) return _lineHeight;

        // P-1：行高只由字体 / 字号 / 字重与图标倍率决定，按实例缓存；
        // 原来每次 Measure 与 OnRender 各 CreateRun("Ag") 一次，8Hz 下每秒几十个 FormattedText。
        if (!double.IsNaN(_cachedLineHeight)) return _cachedLineHeight;

        TextRun run = CreateRun("Ag", RunKind.Text);
        double textHeight = run.Text.Height > 0 ? run.Text.Height : _fontSize * 1.35;

        // 图标放大到超过行高时把行高撑开，否则会被裁掉；正常倍率下这条不生效。
        double iconRoom = _fontSize * _iconScale * 0.78 + 1;
        _cachedLineHeight = Math.Max(textHeight, iconRoom);
        return _cachedLineHeight;
    }

    static List<string> Split(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string> { "" };

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] parts = normalized.Split('\n');
        return new List<string>(parts);
    }

    enum RunKind
    {
        Text,
        Keycap,
        Icon,
        /// <summary>M4：仅 0–9，启用 MonoNumbers 时单独走 MonoFont。</summary>
        Number
    }

    sealed class TextRun
    {
        public FormattedText Text;
        public double Width;
        public RunKind Kind;
        public string Source;
        /// <summary>相对统一基线的额外垂直偏移：图标 / 键帽按「墨迹中心」对齐正文，而不是按字体基线。</summary>
        public double OffsetY;
    }

    sealed class TextLine
    {
        public readonly List<TextRun> Runs = new();
        public double Width;

        public void Add(TextRun run)
        {
            Runs.Add(run);
            Width += run.Width;
        }
    }

    /// <summary>
    /// 一份文本的完整排版结果：拆好的行字符串、每行排好的 run、最宽行宽度。
    /// P-1（review-2.26.0）缓存的就是这份结果，Measure 与 OnRender 共用同一份，
    /// 不再各自 BuildLine 一遍；光标定位也直接复用这里的行宽。
    /// </summary>
    sealed class TextLayout
    {
        public readonly List<string> Lines;
        public readonly List<TextLine> Built;
        public readonly double Width;

        public TextLayout(List<string> lines, List<TextLine> built, double width)
        {
            Lines = lines;
            Built = built;
            Width = width;
        }
    }

    /// <summary>
    /// 文本 → 排版结果的缓存（P-1）。Measure 用 <c>_layoutText ?? _text</c>，OnRender 用 <c>_text</c>，
    /// 同一次刷新里两者往往不同（启动动画期间的完整布局文本 + 逐字显示的文本），
    /// 所以必须按字符串分别缓存，不能只留一个槽——只留一个槽会让 Measure / OnRender 互相覆盖，
    /// 覆盖后双方都退化成 miss，等于没优化。
    /// </summary>
    readonly Dictionary<string, TextLayout> _layoutCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 键上限只是防御：正常只有「显示文本 / 布局文本 / 光标行前缀」几个键，
    /// 但指标卡每 125ms 换一次文本，会不断产生新字符串；到上限整表清空，
    /// 宁可重建一次（最坏每十几次刷新一次），也不让字典无限增长。
    /// </summary>
    const int LayoutCacheLimit = 12;

    /// <summary>
    /// 建缓存时的样式快照。setter 改动都会主动清表，这里兜的是两类漏网情况：
    /// ① PixelsPerDip 变化没有 setter 可挂（换显示器 / 改缩放）；
    /// ② 以后新增影响排版的样式字段却忘了置脏——宁可重建，也不要把旧排版画出去。
    /// </summary>
    FontFamily _layoutCacheFamily;
    FontFamily _layoutCacheMonoFamily;
    bool _layoutCacheMonoNumbers;
    double _layoutCacheFontSize;
    double _layoutCacheIconScale;
    FontWeight _layoutCacheWeight;
    Brush _layoutCacheForeground;
    double _layoutCachePixelsPerDip;

    /// <summary>
    /// "Ag" 行高缓存：<see cref="LineHeightOf"/> 原本每次 Measure + OnRender 都要 CreateRun 一次，
    /// 而它只取决于 (字体, 字号, 字重) 与字号 × 图标倍率——这些都在 setter 里置脏。
    /// </summary>
    double _cachedLineHeight = double.NaN;

    /// <summary>
    /// 字形墨迹范围缓存（键 = 字体|字号|字重|文本）。对齐图标要用真实墨迹而不是字体度量：
    /// Lucide 线条图标在 em 框里上下留白不对称，按 ascent / descent 算出来的中心会偏。
    /// </summary>
    static readonly Dictionary<string, Rect> InkCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 统一基线（正文数字「0」的基线）。整行所有 run 都排在同一条基线上，图标再按墨迹中心微调；
    /// 这样不会因为图标放大把整行基线顶下去（之前图标把行高撑出画布、正文跟着下移就是这个原因）。
    /// </summary>
    double _refBaseline = double.NaN;
    /// <summary>正文数字的墨迹中心相对基线的偏移。</summary>
    double _refInkCenter;

    TextRun CreateRun(string text, RunKind kind)
    {
        PerformanceProbe.TextRun();
        bool icon = kind == RunKind.Icon;
        FontFamily family;
        if (icon) family = LucideIcons.FontFamily;
        else if (kind == RunKind.Keycap) family = KeycapFamily;
        else if (kind == RunKind.Number && _monoFamily is not null) family = _monoFamily;
        else family = _fontFamily;

        // 数字只是换字体，仍跟正文同字重；键帽 / 图标保持自己的绘制语汇。
        FontWeight weight = kind == RunKind.Text || kind == RunKind.Number
            ? _fontWeight : FontWeights.Normal;
        double size = icon ? _fontSize * _iconScale : _fontSize;

        var ft = new FormattedText(
            text ?? "",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
            size,
            _foreground ?? Brushes.White,
            null,
            TextFormattingMode.Display,
            PixelsPerDip());

        ft.TextAlignment = TextAlignment.Left;
        ft.Trimming = TextTrimming.None;

        var run = new TextRun
        {
            Text = ft,
            Width = ft.WidthIncludingTrailingWhitespace,
            Kind = kind,
            Source = text ?? ""
        };

        if (kind == RunKind.Keycap || kind == RunKind.Icon)
        {
            EnsureMetrics();
            Rect ink = InkBoundsOf(ft, InkKey(family, size, weight, run.Source));
            if (!ink.IsEmpty && ink.Height > 0)
                run.OffsetY = _refInkCenter - (ink.Y + ink.Height / 2 - ft.Baseline);
        }

        return run;
    }

    void EnsureMetrics()
    {
        if (!double.IsNaN(_refBaseline)) return;

        // 「0」用来量正文的墨迹中心：这些卡片的内容以数字为主，图标对齐数字的视觉中心最稳。
        TextRun probe = CreateRun("0", RunKind.Text);
        _refBaseline = probe.Text.Baseline;

        Rect ink = InkBoundsOf(probe.Text, InkKey(_fontFamily, _fontSize, _fontWeight, probe.Source));
        _refInkCenter = ink.IsEmpty ? 0 : ink.Y + ink.Height / 2 - probe.Text.Baseline;
    }

    static string InkKey(FontFamily family, double size, FontWeight weight, string text) => $"{family.Source}|{size.ToString("0.###", CultureInfo.InvariantCulture)}|{weight}|{text}";

    Rect InkBoundsOf(FormattedText ft, string key)
    {
        if (string.IsNullOrEmpty(key)) return Rect.Empty;

        if (InkCache.TryGetValue(key, out Rect cached)) return cached;

        Rect bounds;
        try
        {
            Geometry geometry = ft.BuildGeometry(new Point(0, 0));
            bounds = geometry is not null && !geometry.IsEmpty() ? geometry.Bounds : Rect.Empty;
        }
        catch
        {
            bounds = Rect.Empty;
        }

        if (InkCache.Count > 1024) InkCache.Clear();
        InkCache[key] = bounds;
        return bounds;
    }

    /// <summary>
    /// 字体 / 字号 / 字重变了：正文基线、墨迹中心、以及缓存的排版与 "Ag" 行高都要重新算。
    /// 排版缓存放在这里一起清，保证所有走 InvalidateMetrics 的 setter 都不会漏。
    /// </summary>
    void InvalidateMetrics()
    {
        _refBaseline = double.NaN;
        _refInkCenter = 0;
        InvalidateLayoutCache();
    }

    RunKind KindOf(char c)
    {
        if (IsKeycapChar(c)) return RunKind.Keycap;
        if (IsIconChar(c)) return RunKind.Icon;
        if (_monoNumbers && _monoFamily is not null && c >= '0' && c <= '9') return RunKind.Number;
        return RunKind.Text;
    }

    /// <summary>把一行拆成「普通文字 / 键帽 / 图标」三种 run；键帽用 Reicon、图标用 Lucide 绘制。</summary>
    TextLine BuildLine(string line)
    {
        PerformanceProbe.TextLine();
        var result = new TextLine();
        if (string.IsNullOrEmpty(line))
        {
            result.Add(CreateRun("", RunKind.Text));
            return result;
        }

        int start = 0;
        RunKind kind = KindOf(line[0]);
        for (int i = 1; i <= line.Length; i++)
        {
            RunKind next = i < line.Length ? KindOf(line[i]) : kind;
            if (i == line.Length || next != kind)
            {
                result.Add(CreateRun(line.Substring(start, i - start), kind));
                start = i;
                kind = next;
            }
        }
        return result;
    }

    /// <summary>
    /// 取一份文本的排版（P-1）：命中缓存直接复用，未命中才 Split + BuildLine。
    /// Measure 与 OnRender 都走这里，所以同一次刷新只排版一次；行宽也算好存下来，
    /// 光标定位不用再为整行 / 前缀重复排版。
    /// </summary>
    TextLayout GetLayout(string text)
    {
        text = text ?? "";
        EnsureLayoutCache();

        if (_layoutCache.TryGetValue(text, out TextLayout cached)) return cached;

        List<string> lines = Split(text);
        var built = new List<TextLine>(lines.Count);
        double width = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            TextLine line = BuildLine(lines[i]);
            built.Add(line);
            if (line.Width > width) width = line.Width;
        }

        var layout = new TextLayout(lines, built, width);
        if (_layoutCache.Count >= LayoutCacheLimit) _layoutCache.Clear();
        _layoutCache[text] = layout;
        return layout;
    }

    /// <summary>
    /// 校验缓存对应的样式快照，不一致就整表作废并记下新快照。
    /// Text / LayoutText 不在检查范围：它们就是缓存键，换了自然 miss（也因此不能在这里清表，
    /// 否则启动动画里 Measure 的布局文本与 OnRender 的显示文本会互相清掉对方的缓存）。
    /// </summary>
    void EnsureLayoutCache()
    {
        double pixelsPerDip = PixelsPerDip();
        if (_layoutCache.Count > 0
            && _layoutCacheFontSize == _fontSize
            && _layoutCacheIconScale == _iconScale
            && _layoutCacheWeight == _fontWeight
            && _layoutCachePixelsPerDip == pixelsPerDip
            && object.ReferenceEquals(_layoutCacheFamily, _fontFamily)
            && object.ReferenceEquals(_layoutCacheMonoFamily, _monoFamily)
            && _layoutCacheMonoNumbers == _monoNumbers
            && object.ReferenceEquals(_layoutCacheForeground, _foreground))
        {
            return;
        }

        _layoutCache.Clear();
        _layoutCacheFamily = _fontFamily;
        _layoutCacheMonoFamily = _monoFamily;
        _layoutCacheMonoNumbers = _monoNumbers;
        _layoutCacheFontSize = _fontSize;
        _layoutCacheIconScale = _iconScale;
        _layoutCacheWeight = _fontWeight;
        _layoutCacheForeground = _foreground;
        _layoutCachePixelsPerDip = pixelsPerDip;
    }

    /// <summary>
    /// 影响排版的样式变了：缓存的 FormattedText 与 "Ag" 行高全部作废。
    /// 字体基线 / 墨迹中心的作废由 <see cref="InvalidateMetrics"/> 负责，两者职责分开。
    /// </summary>
    void InvalidateLayoutCache()
    {
        _layoutCache.Clear();
        _cachedLineHeight = double.NaN;
    }

    double PixelsPerDip()
    {
        try
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (dpi > 0.1 && dpi < 10) return dpi;
        }
        catch { }
        return 1.0;
    }

    /// <summary>
    /// 换显示器 / 改缩放时 PixelsPerDip 变了，缓存的 FormattedText 是按旧 DPI 排的，必须整表作废；
    /// 连同基线 / 墨迹中心一起重算，并重新 Measure（原来这里没有处理，跨屏拖动后尺寸会停在旧值）。
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMetrics();
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ==================== 绘制 ====================

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        PerformanceProbe.TextRender();

        EnsureMetrics();

        // P-1：与 MeasureOverride 共用缓存；布局文本与显示文本不同也各有一份，互不覆盖。
        TextLayout layout = GetLayout(_text);
        List<TextLine> built = layout.Built;
        double lineHeight = LineHeightOf();

        // 先量整块：卡片宽度受「只增不减」保护，可能比当前文字宽很多。
        // 整块位置必须跟对齐方式走：左对齐时贴左、居中时居中、右对齐时贴右。
        // 之前无论选哪种都先把整块居中，导致「左对齐 / 右对齐」只剩下多行行宽差，
        // 单行时看起来完全没生效。
        double blockWidth = layout.Width;

        double blockHeight = built.Count * lineHeight;
        double originX = AlignBlockX(blockWidth);
        double originY = Math.Max(0, (ActualHeight - blockHeight) / 2);

        for (int i = 0; i < built.Count; i++)
        {
            TextLine line = built[i];
            double x = originX + AlignX(line.Width, blockWidth);
            double y = originY + i * lineHeight;

            for (int r = 0; r < line.Runs.Count; r++)
            {
                TextRun run = line.Runs[r];
                // 统一基线 + 每个 run 自己的墨迹微调：正文按基线，键帽 / 图标按视觉中心。
                DrawLine(dc, run.Text, x, y + (_refBaseline - run.Text.Baseline) + run.OffsetY);
                x += run.Width;
            }
        }

        if (_cursorVisible) DrawCursor(dc, layout, lineHeight, originX, originY, blockWidth);
    }

    void DrawLine(DrawingContext dc, FormattedText ft, double x, double y)
    {
        // 先画描边再画填充：轮廓会往外扩，先描边能让填充盖住内侧一半线宽，
        // 文字本体保持清晰，边缘得到一圈实色描边。
        if (_outline && _outlineThickness > 0.05)
        {
            Geometry geometry = ft.BuildGeometry(new Point(x, y));
            if (geometry is not null && !geometry.IsEmpty())
            {
                var pen = new Pen(_outlineBrush ?? Brushes.Black, _outlineThickness)
                {
                    LineJoin = PenLineJoin.Round,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                pen.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }
        }

        dc.DrawText(ft, new Point(x, y));
    }

    /// <summary>整块文字在画布里的水平起点；行内再按 <see cref="AlignX"/> 对齐。</summary>
    double AlignBlockX(double blockWidth)
    {
        double free = Math.Max(0, ActualWidth - blockWidth);
        switch (_alignment)
        {
            case TextAlignment.Center:
                return free / 2;
            case TextAlignment.Right:
                return free;
            default:
                return 0;
        }
    }

    double AlignX(double lineWidth, double boxWidth)
    {
        switch (_alignment)
        {
            case TextAlignment.Center:
                return Math.Max(0, (boxWidth - lineWidth) / 2);
            case TextAlignment.Right:
                return Math.Max(0, boxWidth - lineWidth);
            default:
                return 0;
        }
    }

    void DrawCursor(DrawingContext dc, TextLayout layout, double lineHeight,
                    double originX, double originY, double blockWidth)
    {
        string style = _cursorStyle ?? "Block";
        if (string.Equals(style, "None", StringComparison.OrdinalIgnoreCase)) return;
        if (_cursorBlink && !_blinkOn) return;

        List<string> lines = layout.Lines;
        if (lines.Count == 0) return;

        int offset = _cursorIndex;
        if (offset < 0 || offset > _text.Length) offset = _text.Length;

        int lineIndex = 0;
        int lineStart = 0;
        for (int i = 0; i < offset; i++)
        {
            char c = _text[i];
            if (c == '\n')
            {
                lineIndex++;
                lineStart = i + 1;
            }
            else if (c == '\r' && (i + 1 >= _text.Length || _text[i + 1] != '\n'))
            {
                lineIndex++;
                lineStart = i + 1;
            }
        }

        if (lineIndex >= lines.Count) lineIndex = Math.Max(0, lines.Count - 1);

        string line = lines[lineIndex];
        int column = 0;
        for (int i = lineStart; i < offset && i < _text.Length; i++)
        {
            if (_text[i] != '\r') column++;
        }
        if (column > line.Length) column = line.Length;

        string prefix = line.Substring(0, Math.Max(0, column));
        // 整行宽度直接取缓存里排好的结果；前缀是光标移动才变的短字符串，
        // 同样走排版缓存（命中就零分配，未命中与原来一样只排这一小段）。
        double lineWidth = layout.Built[lineIndex].Width;
        double prefixWidth = GetLayout(prefix).Width;
        double x = originX + AlignX(lineWidth, blockWidth) + prefixWidth;
        double y = originY + lineIndex * lineHeight;

        double caretWidth = Math.Max(2, _fontSize * 0.55);
        double caretHeight = Math.Max(3, lineHeight * 0.8);
        double blockTop = y + (lineHeight - caretHeight) / 2;

        Brush brush = _foreground ?? Brushes.White;

        if (string.Equals(style, "Bar", StringComparison.OrdinalIgnoreCase))
        {
            dc.DrawRectangle(brush, null, new Rect(x + 0.5, y + lineHeight * 0.06, 2, lineHeight * 0.88));
        }
        else if (string.Equals(style, "Underline", StringComparison.OrdinalIgnoreCase))
        {
            dc.DrawRectangle(brush, null, new Rect(x + 0.5, y + lineHeight - 3, caretWidth, 2));
        }
        else if (string.Equals(style, "Outline", StringComparison.OrdinalIgnoreCase))
        {
            var pen = new Pen(brush, 1.3);
            pen.Freeze();
            dc.DrawRectangle(null, pen, new Rect(x + 1, blockTop, caretWidth, caretHeight));
        }
        else
        {
            dc.DrawRectangle(brush, null, new Rect(x + 1, blockTop, caretWidth, caretHeight));
        }
    }
}
