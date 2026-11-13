using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    double _maxTextWidth;
    double _pixelsPerDipOverride = double.NaN;

    /// <summary>
    /// T-11：离屏渲染（截图）时注入的 PixelsPerDip。NaN = 跟随可视树（默认）。
    ///
    /// 为什么需要：<c>--screenshot*</c> 里的 OverlaySurface 只是 Grid 子元素，从未接到
    /// PresentationSource，<see cref="VisualTreeHelper.GetDpi"/> 会退回 96 —— 截图在 2× RTB
    /// 里用 1.0 px/DIP 做 Display hinting，与屏上 150% 的字形宽度 / 笔画粗细对不上。
    /// 截图路径按真实屏幕缩放注入后，排版参数与屏上一致。
    /// </summary>
    public double PixelsPerDipOverride
    {
        get => _pixelsPerDipOverride;
        set
        {
            double next = value > 0.1 && value < 10 ? value : double.NaN;
            bool same = double.IsNaN(next)
                ? double.IsNaN(_pixelsPerDipOverride)
                : Math.Abs(next - _pixelsPerDipOverride) < 1e-6;
            if (same) return;
            _pixelsPerDipOverride = next;
            InvalidateMetrics();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// T-7：超宽时按省略号截断。0 = 不限制；默认取工作区宽度，避免长文本把卡片撑出屏。
    /// </summary>
    public double MaxTextWidth
    {
        get => _maxTextWidth;
        set
        {
            double next = value > 0 ? value : 0;
            if (Math.Abs(next - _maxTextWidth) < 0.01) return;
            _maxTextWidth = next;
            InvalidateLayoutCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    bool _cursorVisible;
    string _cursorStyle = "Block";
    bool _cursorBlink = true;
    int _cursorIndex = -1;

    static FontFamily _keycapFamily;

    /// <summary>
    /// 内嵌的键帽字体 Reicon（只用 <see cref="Keycaps.First"/>–<see cref="Keycaps.Last"/> 这一段键帽字形，
    /// 图标码位不映射）。源码里的 `{keys}` 会先被 <see cref="MirrorpondHUD.Core.Keycaps"/> 换成这些 PUA 字符，
    /// 再在这里按 run 单独用这个字体绘制，所以可以和普通文字混排。
    ///
    /// T-16：注释不再手写码位与字形个数。原来这里写「U+E000–U+E052 这 83 个」，实现与
    /// <see cref="Keycaps"/> 都是到 U+E056，按 83 个裁字体子集会让末尾 4 个键帽缺字形。
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
                catch (Exception ex)
                {
                    // 字体加载失败会让键帽 PUA 字符显示成方框，是最难排查的显示问题之一。
                    Log.Warn($"键帽字体加载失败，回退 Microsoft YaHei UI: {ex.Message}");
                    _keycapFamily = new FontFamily("Microsoft YaHei UI");
                }
            }
            return _keycapFamily;
        }
    }

    static bool IsKeycapChar(char c) =>
        // 键帽码位区间（{keys} 里的按键字形）。边界取自 Keycaps.First/Last，不再手写数字：
        // 手写数字曾经和实现差 4 个码位（T-16），而字体子集与 <see cref="Keycaps"/> 都以那里为准。
        c >= Keycaps.First && c <= Keycaps.Last;

    /// <summary>
    /// 图标字形：只认 <see cref="Icons"/> 登记过的 Lucide 码位。不能按 U+E100–U+E3FF 整段判断：
    /// Lucide 和键帽字体共用 PUA，整段判断会把键帽（<see cref="Keycaps.First"/>–<see cref="Keycaps.Last"/>）误当图标。
    /// </summary>
    static bool IsIconChar(char c) => Icons.IsIconChar(c);

    public TextCanvas()
    {
        // T-7：默认不超过主屏工作区的 97%，避免单行超长内容把卡片撑出屏幕。
        // 多显示器下可由宿主再按目标屏覆盖。
        MaxTextWidth = Math.Max(160, SystemParameters.WorkArea.Width * 0.97);
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

    // ==================== 粒子遮罩 ====================

    /// <summary>
    /// 隐藏指定字符（仍占排版宽度），用于把变化字符交给粒子层。
    /// T-8：下标先按文本元素（grapheme cluster）取整——代理对 / 组合音标 / ZWJ 序列
    /// 只隐藏一半时，FormattedText 会画出孤立代理或半个 emoji。
    /// </summary>
    public void SetHiddenChars(IEnumerable<int> indices)
    {
        _hiddenChars.Clear();
        if (indices is not null)
        {
            foreach (int index in indices)
            {
                if (index < 0 || index >= _text.Length) continue;
                _hiddenChars.Add(SnapToTextElementStart(_text, index));
            }
        }
        InvalidateVisual();
    }

    public void ClearHiddenChars()
    {
        if (_hiddenChars.Count == 0) return;
        _hiddenChars.Clear();
        InvalidateVisual();
    }

    /// <summary>
    /// 2.45.4：当前是否还有被粒子遮住的字符。自检用它断言"清理后必须全部可见"；
    /// 生产路径不依赖它做决策。
    /// </summary>
    internal bool HasHiddenChars => _hiddenChars.Count > 0;

    /// <summary>
    /// T-8：文本元素的起点表。代理对、组合音标、国旗 / ZWJ 序列会被 <see cref="StringInfo"/>
    /// 合成一个元素，后续所有"按下标切分"的地方都以它为准。
    /// </summary>
    internal static int[] TextElementStarts(string text) =>
        StringInfo.ParseCombiningCharacters(text ?? "");

    /// <summary>
    /// T-8：把任意 UTF-16 下标吸附到所在文本元素的起点；等于/超过长度时返回长度本身
    /// （光标在行尾，不能退到最后一个元素之前）。删除 / 光标移动都以这个边界为单位。
    /// </summary>
    internal static int SnapToTextElementStart(string text, int index)
    {
        if (string.IsNullOrEmpty(text) || index <= 0) return 0;
        if (index >= text.Length) return text.Length;

        int[] starts = TextElementStarts(text);
        int best = 0;
        for (int i = 0; i < starts.Length && starts[i] <= index; i++) best = starts[i];
        return best;
    }

    // ==================== 度量 ====================

    /// <summary>
    /// 2.45.5：上一次 Arrange 对应的文本与宽度。窗口是 SizeToContent，文本变长后
    /// 会先触发一次 OnRender、后完成新的 Arrange；如果直接用旧的 ActualWidth 判裁剪，
    /// 正常内容会在那一瞬被加上省略号（用户看到的“偶发截断”）。
    /// 只有“当前文本已经按这个宽度 Arrange 过”时，ActualWidth 才可信。
    /// </summary>
    string _arrangedText;
    double _arrangedWidth;

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);
        _arrangedText = _text;
        _arrangedWidth = finalSize.Width;
        return size;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        PerformanceProbe.TextMeasure();
        // P-1：Measure 与 OnRender 共用同一份排版。这里按布局文本取缓存，
        // 布局文本没变时（启动动画逐字打字期间）一次 BuildLine 都不用做。
        TextLayout layout = GetLayout(_layoutText ?? _text);
        double lineHeight = LineHeightOf();

        // 2.45.4：OnRender 画的一直是 _text，布局文本只是启动动画用来提前撑尺寸的。
        // 两者行数不一致时（动画被打断后遗留布局文本、未来新增其它布局文本来源），
        // 窗口按少的行数定高，多出来的行会被窗口边缘直接切掉——用户看到的就是"少行"。
        // 高度取两者较大值；CountLines 只数换行、不排版，打字动画不会因此变慢。
        int lineCount = Math.Max(layout.Lines.Count, CountLines(_text));

        double width = layout.Width;

        // 光标画在行尾，给右侧留一点余量，避免被裁掉
        if (_cursorVisible) width += Math.Max(3, _fontSize * 0.75);

        double height = Math.Max(1, lineHeight * lineCount);
        width = Math.Max(1, width);

        // T-7 / 2.45.4：固定宽度是用户显式约束，工作区上限只能收紧；这个组合必须和
        // DrawContent 走同一个函数（LayoutWidthLimit），否则窗口按一个宽度排、
        // 文字按另一个宽度画，超出部分会被窗口边缘无声切掉（没有省略号、没有日志）。
        if (_fixedWidth > 0) width = _fixedWidth;
        double limit = LayoutWidthLimit();
        if (limit > 0) width = Math.Min(width, limit);

        // 只增不减：数据从 "5%" 变成 "100%" 时窗口会长大一次，
        // 之后即使数值变短也不会缩回去，位置就不会来回跳。
        if (_hasStable)
        {
            width = Math.Max(width, _stableWidth);
            height = Math.Max(height, _stableHeight);
        }

        if (limit > 0) width = Math.Min(width, limit);
        _stableWidth = width;
        _stableHeight = height;
        _hasStable = true;

        return new Size(Math.Ceiling(width), Math.Ceiling(height));
    }

    /// <summary>
    /// 2.45.4：排版 / 绘制 / 采样共用的有效宽度上限（0 = 不限制）。
    /// 固定宽度是用户显式约束，MaxTextWidth 是宿主按工作区给的兜底，两者取小。
    /// 必须只有一个来源：以前 Measure 用 min(固定, 上限)、DrawContent 只看上限，
    /// 于是固定宽度下的长文本会按自然宽度画出去，被窗口边缘硬切。
    /// </summary>
    double LayoutWidthLimit()
    {
        double limit = 0;
        if (_fixedWidth > 0) limit = _fixedWidth;
        if (_maxTextWidth > 0 && (limit <= 0 || _maxTextWidth < limit)) limit = _maxTextWidth;
        return limit;
    }

    /// <summary>
    /// 2.45.4：绘制时真正可用的整块宽度。
    /// 先取设置上限（固定宽度 / 工作区），再受 Arrange 实际给出的宽度约束；
    /// 后者更窄说明窗口被外部限制得比测量值小，此时必须按可用宽度裁剪并画省略号。
    /// </summary>
    double AvailableBlockWidth(double textWidth)
    {
        double limit = LayoutWidthLimit();
        double available = limit > 0 ? limit : Math.Max(1, textWidth);

        // 只有当前文本已经按这个 Arrange 宽度布局过，ActualWidth 才代表“真的被压窄”；
        // 文本刚变、Arrange 还没跟上时它还是上一段文本的宽度，用它裁会误伤正常内容。
        if (_arrangedText == _text && _arrangedWidth > 0 && _arrangedWidth < available)
            available = _arrangedWidth;

        if (available <= 0) available = Math.Max(1, textWidth);
        return available;
    }

    /// <summary>2.45.4：裁剪日志 30s 节流（只影响日志，不影响绘制）。</summary>
    static bool ShouldLogClip(ref long lastTicks)
    {
        long now = Stopwatch.GetTimestamp();
        if (lastTicks != 0
            && Stopwatch.GetElapsedTime(lastTicks, now).TotalMilliseconds < 30000)
        {
            return false;
        }

        lastTicks = now;
        return true;
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

    /// <summary>
    /// 2.45.4：与 <see cref="Split"/> 完全同源的换行计数（CRLF / CR / LF 各算一次，
    /// 末尾换行也算一个空行）。只数换行、不建 run，用于"测量取布局文本与显示文本
    /// 行数较大值"时不额外付排版成本，也不破坏启动动画的缓存收益。
    /// </summary>
    internal static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;

        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                lines++;
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '\n')
            {
                lines++;
            }
        }
        return lines;
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
        public readonly List<int> LineStarts;

        public TextLayout(List<string> lines, List<TextLine> built, double width, List<int> lineStarts)
        {
            Lines = lines;
            Built = built;
            Width = width;
            LineStarts = lineStarts;
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
    /// 主缓存的键顺序，用于满了以后逐个淘汰最旧键（T-21）。8 个键上的 List 操作足够便宜，
    /// 换来的是"正在显示的文本不会被整表清空"。
    /// </summary>
    readonly List<string> _layoutCacheOrder = new();

    /// <summary>
    /// T-21：光标行前缀的**独立单槽**。光标每移动一次就是一个新字符串键
    /// （<c>line.Substring(0, column)</c>），命令行连续输入十几个字符就会把主缓存反复撑满；
    /// 原先满 12 个键整表清空，`_text` 与 `LayoutText` 的排版被前缀键挤掉，
    /// 命令行卡上 P-1 的收益直接归零。前缀只需要"上一次那一个"，单槽足矣。
    /// </summary>
    string _cursorLayoutKey;
    TextLayout _cursorLayout;

    /// <summary>粒子动画期间只隐藏变化字符；未变字符仍由本画布正常绘制。</summary>
    readonly HashSet<int> _hiddenChars = new();

    /// <summary>
    /// 主缓存键上限（T-21）。正常只有「显示文本 / 布局文本」两个键；指标卡每 125ms 换一次文本
    /// 会不断产生新键，所以仍需上限。淘汰策略是**按使用顺序淘汰最旧键**（<see cref="TouchLayoutCache"/>
    /// 命中即刷新），不是原来的整表清空——清空会让正在显示的文本在下一帧立刻重新 Split + BuildLine，
    /// 那正是 T-21 要避免的退化。光标前缀不计入本缓存（见 <see cref="_cursorLayout"/>）。
    /// </summary>
    const int LayoutCacheLimit = 8;

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
    /// 2.45.4：裁剪必须可见（AGENTS §5）。宽度裁剪是合法降级（设置固定宽度 / 超出工作区），
    /// 画省略号并把具体数值写进日志；高度裁剪说明"测量与绘制不同源"，是缺陷线索。
    /// 两者都按 30s 手动节流：正常刷新不会刷屏，但长时间坏着仍能留下证据。
    /// </summary>
    long _clipWidthLogTicks;
    long _clipHeightLogTicks;

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

        if (InkCache.TryGetValue(key, out var cached)) return cached;

        Rect bounds;
        try
        {
            Geometry geometry = ft.BuildGeometry(new Point(0, 0));
            bounds = geometry is not null && !geometry.IsEmpty() ? geometry.Bounds : Rect.Empty;
        }
        catch (Exception ex)
        {
            // 几何构建失败只影响图标基线的微调，按空墨迹继续排版；
            // 用 Debug 而不是 Warn，避免每个字形/每次布局刷一条日志。
            Log.Debug($"文字墨迹几何构建失败，按空墨迹排版: {ex.Message}");
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
            // T-8：组合标记 / 变体选择符 / 代理对下半属于前一个文本元素，
            // 不能在这里换 run 类型，否则 "1\u0301" 会被拆成数字 run + 正文 run，
            // 后续按元素隐藏时又会跨 run 切一半。
            RunKind next = i < line.Length
                ? (IsElementContinuation(line, i) ? kind : KindOf(line[i]))
                : kind;
            if (i == line.Length || next != kind)
            {
                result.Add(CreateRun(line.Substring(start, i - start), kind));
                start = i;
                kind = next;
            }
        }
        return result;
    }

    /// <summary>T-8：这个位置上的字符是否属于前一个文本元素（不能作为 run 的起点）。</summary>
    static bool IsElementContinuation(string line, int index)
    {
        char c = line[index];
        if (char.IsLowSurrogate(c)) return true;
        // ZWJ 与两个变体选择符：表情 / 文字样式序列的连接符，属于前一簇。
        if (c == '\u200D' || c == '\uFE0E' || c == '\uFE0F') return true;

        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
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

        if (_layoutCache.TryGetValue(text, out var cached))
        {
            TouchLayoutCache(text);
            return cached;
        }

        TextLayout layout = BuildLayout(text);
        StoreLayoutCache(text, layout);
        return layout;
    }

    /// <summary>
    /// 光标行前缀专用排版（T-21）：容量 1 的单槽，绝不写进主缓存，也不参与主缓存的淘汰。
    /// 前缀键每移动一次光标就换一个，混进主缓存只会把正式文本挤出去。
    /// </summary>
    TextLayout GetCursorLayout(string prefix)
    {
        prefix = prefix ?? "";

        // 先过一遍样式快照校验：换屏 / 换字号时主缓存与单槽必须一起作废。
        EnsureLayoutCache();
        if (_cursorLayout is not null && string.Equals(_cursorLayoutKey, prefix, StringComparison.Ordinal))
            return _cursorLayout;

        _cursorLayoutKey = prefix;
        _cursorLayout = BuildLayout(prefix);
        return _cursorLayout;
    }

    /// <summary>真正的排版（Split + 逐行 BuildLine）。缓存命中路径不走这里。</summary>
    TextLayout BuildLayout(string text)
    {
        List<string> lines = Split(text);
        var built = new List<TextLine>(lines.Count);
        var starts = new List<int>(lines.Count);
        double width = 0;
        int position = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            starts.Add(position);
            position += lines[i].Length + 1;
            TextLine line = BuildLine(lines[i]);
            built.Add(line);
            if (line.Width > width) width = line.Width;
        }

        return new TextLayout(lines, built, width, starts);
    }

    /// <summary>
    /// 写主缓存。满了先淘汰最旧键而不是整表清空（T-21）：清空会让下一帧把正在显示的文本
    /// 重新排一遍，正好抵消缓存的意义；逐个淘汰则只丢掉指标卡的历史文本。
    /// </summary>
    void StoreLayoutCache(string text, TextLayout layout)
    {
        while (_layoutCacheOrder.Count >= LayoutCacheLimit && _layoutCacheOrder.Count > 0)
        {
            string oldest = _layoutCacheOrder[0];
            _layoutCacheOrder.RemoveAt(0);
            _layoutCache.Remove(oldest);
        }

        _layoutCache[text] = layout;
        _layoutCacheOrder.Add(text);
    }

    /// <summary>缓存命中即把键挪到队尾，近似 LRU：热点文本不会因为插入早而被挤掉。</summary>
    void TouchLayoutCache(string text)
    {
        int index = _layoutCacheOrder.IndexOf(text);
        if (index < 0 || index == _layoutCacheOrder.Count - 1) return;
        _layoutCacheOrder.RemoveAt(index);
        _layoutCacheOrder.Add(text);
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
        _layoutCacheOrder.Clear();
        _cursorLayoutKey = null;
        _cursorLayout = null;
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
        _layoutCacheOrder.Clear();
        // T-21：光标前缀单槽与主缓存同寿命，样式一换必须一起作废。
        _cursorLayoutKey = null;
        _cursorLayout = null;
        _cachedLineHeight = double.NaN;
    }

    double PixelsPerDip()
    {
        // T-11：截图等离屏路径显式注入真实缩放：没有 PresentationSource 时 GetDpi 恒为 96。
        if (!double.IsNaN(_pixelsPerDipOverride)) return _pixelsPerDipOverride;

        try
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (dpi > 0.1 && dpi < 10) return dpi;
        }
        catch (Exception ex)
        {
            // DPI 读不到时按 1.0 排版会与屏上字形有差；至少留下一条线索。
            Log.Warn($"读取 PixelsPerDip 失败，按 1.0 排版: {ex.Message}");
        }
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

    protected override void OnRender(DrawingContext dc) =>
        DrawContent(dc, _hiddenChars, true);

    /// <summary>
    /// 粒子采样专用：把“只显示指定字符”的内容直接画进 DrawingContext，
    /// 不依赖可视树缓存，避免 RenderTargetBitmap 采到上一次的完整文字。
    /// </summary>
    internal void DrawForParticles(DrawingContext dc, ISet<int> hidden) =>
        DrawContent(dc, hidden, false);

    /// <summary>
    /// 2.45.2：粒子采样区域的外扩量（DIP）。
    ///
    /// 为什么要外扩：包围盒的水平范围是用「run 前缀排版宽度差」估的，把一个元素单独
    /// 拼出来的宽度与整串排版的字距可能差零点几像素；描边会向外扩；图标 / 键帽按墨迹
    /// 中心对齐，字形也可能略微超出行盒。这些都靠 padding 兜住——宁可多采几个空像素，
    /// 也不能把变化字形的边缘切掉（切掉就是粒子少一块，属于视觉回退）。
    /// </summary>
    internal double ParticleSamplePadding =>
        Math.Max(4.0, _fontSize * 0.5)
        + (_outline && _outlineThickness > 0.05 ? _outlineThickness : 0.0)
        + Math.Max(0.0, _fontSize * (_iconScale - 1.0) * 0.5);

    /// <summary>
    /// 2.45.2：粒子采样用——把一组字符按文本元素对齐后，取它们在画布 DIP 坐标系里的
    /// 逐个包围盒并追加到 <paramref name="rects"/>。
    ///
    /// 为什么要这个查询：粒子层只隐藏「变化字符」，但采样位图原来按整卡尺寸建、整块
    /// 扫描——信息卡每秒改一个数字也要清 / 渲染 / 拷贝 / 扫完整张卡。有了每元素的
    /// DIP 矩形，采样位图就能只覆盖变化字形。
    ///
    /// 坐标必须与 <see cref="DrawContent"/> 完全同源（整块对齐 + 行内对齐 + 行高 +
    /// 图标 / 键帽的基线偏移），否则区域整体偏移会切掉字形。返回 false 表示一个都取
    /// 不到（空文本 / 还没量好 / 索引全落在换行符上 / 裁剪模式），调用方必须退回整卡。
    ///
    /// 只读：不改排版缓存、不改绘制状态，可以和 OnRender 走同一份 <see cref="GetLayout"/>。
    /// </summary>
    internal bool TryGetTextElementRects(IReadOnlyCollection<int> indices, ICollection<Rect> rects)
    {
        if (indices is null || indices.Count == 0 || rects is null) return false;
        if (ActualWidth <= 0.5 || ActualHeight <= 0.5) return false;

        string text = _text ?? "";
        if (text.Length == 0) return false;

        // 与 DrawContent 同一份排版缓存：这里多一次 GetLayout 不会重新排版。
        TextLayout layout = GetLayout(text);
        List<TextLine> built = layout.Built;
        if (built.Count == 0) return false;

        // T-7：裁剪模式在整块右侧额外画一个省略号，它不受隐藏集合控制。
        // 按变化区域采样会漏掉省略号的墨迹，所以这里直接放弃收窄，由调用方退回整卡。
        // 2.45.4：裁剪判定必须和 DrawContent 同源（设置上限 + Arrange 实际宽度），
        // 否则固定宽度 / 窗口被压窄时采样区域会漏掉被硬切的部分。
        if (layout.Width > AvailableBlockWidth(layout.Width) + 0.5) return false;

        double lineHeight = LineHeightOf();
        double blockWidth = layout.Width;
        double blockHeight = built.Count * lineHeight;
        double originX = AlignBlockX(blockWidth);
        double originY = Math.Max(0, (ActualHeight - blockHeight) / 2);

        bool any = false;
        string preparedLineText = null;
        int[] preparedStarts = null;

        foreach (int raw in indices)
        {
            // T-8：先吸附到文本元素起点——代理对 / 组合字符只隐藏一半会画出孤立代理。
            int index = SnapToTextElementStart(text, raw);
            if (index < 0 || index >= text.Length) continue;

            int lineIndex = -1;
            for (int i = 0; i < built.Count; i++)
            {
                int lineStart = layout.LineStarts[i];
                int lineLength = layout.Lines[i].Length;
                if (index >= lineStart && index < lineStart + lineLength)
                {
                    lineIndex = i;
                    break;
                }
            }
            // 落在换行符 / 空行上：那个元素没有字形，跳过即可（其余元素仍要进区域）。
            if (lineIndex < 0) continue;

            string line = layout.Lines[lineIndex];
            if (!ReferenceEquals(preparedLineText, line))
            {
                preparedLineText = line;
                preparedStarts = TextElementStarts(line);
            }
            if (preparedStarts is null) continue;

            int column = index - layout.LineStarts[lineIndex];
            int elementStart = 0;
            int elementEnd = line.Length;
            for (int k = 0; k < preparedStarts.Length; k++)
            {
                if (preparedStarts[k] > column) break;
                elementStart = preparedStarts[k];
                elementEnd = k + 1 < preparedStarts.Length ? preparedStarts[k + 1] : line.Length;
            }

            TextLine builtLine = built[lineIndex];
            double x = originX + AlignX(builtLine.Width, blockWidth);
            double y = originY + lineIndex * lineHeight;
            int runStart = 0;
            for (int r = 0; r < builtLine.Runs.Count; r++)
            {
                TextRun run = builtLine.Runs[r];
                int start = runStart;
                int end = start + run.Source.Length;
                runStart = end;
                if (elementStart < start || elementStart >= end)
                {
                    x += run.Width;
                    continue;
                }

                int offset = elementStart - start;
                int length = Math.Min(elementEnd, end) - elementStart;
                if (length <= 0) break;

                // 与 DrawRunWithHidden 同样的推进方式：横向范围用"前缀宽度差"估算，
                // 误差由 <see cref="ParticleSamplePadding"/> 兜住。
                double left = offset == 0 ? 0 : CreateRun(run.Source.Substring(0, offset), run.Kind).Width;
                double right = CreateRun(run.Source.Substring(0, offset + length), run.Kind).Width;
                if (right < left) right = left;
                // 垂直方向直接用整行行盒：图标 / 描边 / 不同字体的下降部都包在里面。
                rects.Add(new Rect(x + left, y, right - left, lineHeight));
                any = true;
                break;
            }
        }

        return any;
    }

    void DrawContent(DrawingContext dc, ISet<int> hidden, bool drawCursor)
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
        // T-7 / 2.45.4：裁剪判定与 Measure 同源（LayoutWidthLimit），并用 Arrange 实际
        // 宽度兜底。以前只看 MaxTextWidth，固定宽度 / 窗口被压窄时文字会画到窗口外，
        // 被边缘硬切且没有任何提示；现在统一画省略号，并留下具体数值的日志。
        double available = AvailableBlockWidth(layout.Width);
        bool clipped = layout.Width > available + 0.5;
        double blockWidth = clipped ? available : layout.Width;

        double blockHeight = built.Count * lineHeight;
        // 裁剪时从行首开始画：居中 / 右对齐若继续按整行宽对齐，x 会是负数，
        // 行首反而被切掉。用户需要看到的是"从头开始、尾部省略"，不是中间一截。
        double originX = clipped ? 0 : AlignBlockX(blockWidth);
        double originY = Math.Max(0, (ActualHeight - blockHeight) / 2);

        // 2.45.4：裁剪时必须给省略号预留宽度，否则"…"会画在最后一个被裁字形上面
        // （同为前景色，视觉上仍是硬切）。正文裁到 textLimit，省略号从 textLimit 起画。
        TextRun ellipsis = clipped ? CreateRun("…", RunKind.Text) : null;
        double textLimit = clipped ? Math.Max(0, blockWidth - ellipsis.Width) : blockWidth;

        // 测量与绘制理论上同源；真出现"要画的内容比画布高"时必须留痕，
        // 否则用户只看到少行、日志里没有任何线索。这里不偷偷改尺寸，只报出具体数值。
        if (blockHeight > ActualHeight + 0.5)
        {
            if (ShouldLogClip(ref _clipHeightLogTicks))
            {
                Log.Warn($"文本块高 {blockHeight:0.#} DIP 超过画布高 {ActualHeight:0.#} DIP"
                    + $"（{built.Count} 行 × 行高 {lineHeight:0.#}），底部行会被裁掉");
            }
        }

        if (clipped)
        {
            if (ShouldLogClip(ref _clipWidthLogTicks))
            {
                Log.Warn($"文本宽 {layout.Width:0.#} DIP 超过可用宽 {available:0.#} DIP"
                    + $"（固定宽度 {_fixedWidth:0.#} / 工作区上限 {_maxTextWidth:0.#} / Arrange {ActualWidth:0.#}），已按省略号裁剪");
            }
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, textLimit, ActualHeight)));
        }

        for (int i = 0; i < built.Count; i++)
        {
            TextLine line = built[i];
            // 裁剪模式下整块已按左边缘对齐（originX=0），行内也不能再按整行宽右 / 居中，
            // 否则行首会被推到 0 左边。未裁剪时保持原来的对齐语义。
            double x = originX + (clipped ? 0 : AlignX(line.Width, blockWidth));
            double y = originY + i * lineHeight;
            int runStart = layout.LineStarts[i];

            for (int r = 0; r < line.Runs.Count; r++)
            {
                TextRun run = line.Runs[r];
                // 统一基线 + 每个 run 自己的墨迹微调：正文按基线，键帽 / 图标按视觉中心。
                double baseY = y + (_refBaseline - run.Text.Baseline) + run.OffsetY;
                if (hidden is null || hidden.Count == 0)
                {
                    DrawLine(dc, run.Text, x, baseY);
                }
                else
                {
                    DrawRunWithHidden(dc, run, runStart, x, y, hidden);
                }
                x += run.Width;
                runStart += run.Source.Length;
            }
        }

        if (clipped)
        {
            dc.Pop();
            double x = Math.Max(0, textLimit);
            double y = originY + Math.Max(0, built.Count - 1) * lineHeight;
            double baseY = y + (_refBaseline - ellipsis.Text.Baseline);
            DrawLine(dc, ellipsis.Text, x, baseY);
        }

        if (drawCursor && _cursorVisible)
            DrawCursor(dc, layout, lineHeight, originX, originY, blockWidth);
    }

    /// <summary>
    /// 把 run 按隐藏字符切成可见段；隐藏段只占宽度不绘制。
    /// T-8：切分点只允许落在文本元素起点，避免把代理对 / 组合字符切成两半。
    /// </summary>
    void DrawRunWithHidden(DrawingContext dc, TextRun run, int runStart, double x, double lineY, ISet<int> hidden)
    {
        string source = run.Source ?? "";
        if (source.Length == 0) return;

        int[] starts = TextElementStarts(source);
        int segmentStart = 0;
        bool visible = !hidden.Contains(runStart);
        for (int k = 1; k < starts.Length; k++)
        {
            int i = starts[k];
            bool next = !hidden.Contains(runStart + i);
            if (next == visible) continue;
            x = DrawHiddenSegment(dc, run, source, segmentStart, i - segmentStart, x, lineY, visible);
            segmentStart = i;
            visible = next;
        }

        x = DrawHiddenSegment(dc, run, source, segmentStart, source.Length - segmentStart, x, lineY, visible);
    }

    /// <summary>画（或只占位）一段文本，返回这一段之后的 x 偏移。</summary>
    double DrawHiddenSegment(DrawingContext dc, TextRun run, string source, int start, int length,
                             double x, double lineY, bool visible)
    {
        if (length <= 0) return x;

        TextRun piece = CreateRun(source.Substring(start, length), run.Kind);
        if (visible)
        {
            // 键帽 / 图标拆分后要按自己的墨迹中心重新对齐，沿用原 run 的 OffsetY 会上下偏。
            double pieceY = lineY + (_refBaseline - piece.Text.Baseline) + piece.OffsetY;
            DrawLine(dc, piece.Text, x, pieceY);
        }
        return x + piece.Width;
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
        // T-8：光标列吸附到文本元素边界。光标停在代理对 / 组合字符中间时，
        // 前缀 Substring 会截出孤立代理，量出的宽度与真实字形不符（光标偏 1–2 个字宽）。
        column = SnapToTextElementStart(line, column);

        string prefix = line.Substring(0, Math.Max(0, column));
        // 整行宽度直接取缓存里排好的结果；前缀是光标移动才变的短字符串，
        // 走专用单槽（T-21）：命中零分配，也不会把正式文本挤出主缓存。
        double lineWidth = layout.Built[lineIndex].Width;
        double prefixWidth = GetCursorLayout(prefix).Width;
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
