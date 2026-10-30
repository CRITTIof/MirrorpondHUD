using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MirrorpondHUD.Ui
{
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

        FontFamily _fontFamily = new FontFamily("Microsoft YaHei UI");
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
                if (_keycapFamily == null)
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

        static bool IsKeycapChar(char c)
        {
            // 键帽 U+E000–U+E056（{keys} 里的按键字形）。
            return c >= '\uE000' && c <= '\uE056';
        }

        /// <summary>Reicon 图标字形：软件 U+E100、硬件 U+E200、界面 U+E300。</summary>
        static bool IsIconChar(char c)
        {
            return (c >= '\uE100' && c <= '\uE1FF')
                || (c >= '\uE200' && c <= '\uE2FF')
                || (c >= '\uE300' && c <= '\uE3FF');
        }

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
        }

        // ==================== 内容 ====================

        /// <summary>实际显示的文字。</summary>
        public string Text
        {
            get { return _text; }
            set
            {
                value = value ?? "";
                if (string.Equals(_text, value, StringComparison.Ordinal)) return;
                _text = value;
                _blinkOn = true;
                if (_cursorVisible && _cursorBlink)
                {
                    _blink.Stop();
                    _blink.Start();
                }
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
            get { return _layoutText; }
            set
            {
                if (string.Equals(_layoutText, value, StringComparison.Ordinal)) return;
                _layoutText = value;
                InvalidateMeasure();
            }
        }

        /// <summary>清掉"只增不减"的尺寸记忆（换字体、字号等设置后调用）。</summary>
        public void ResetStableSize()
        {
            _hasStable = false;
            _stableWidth = 0;
            _stableHeight = 0;
            InvalidateMeasure();
        }

        // ==================== 字体与样式 ====================

        public FontFamily FontFamily
        {
            get { return _fontFamily; }
            set
            {
                if (value == null || value.Equals(_fontFamily)) return;
                _fontFamily = value;
                InvalidateMetrics();
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public double FontSize
        {
            get { return _fontSize; }
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
        /// 实测 Reicon 方框字形的墨迹高约 0.74 em，和数字高度（约 0.72 em）基本一致，
        /// 所以 1.0 = 图标与文字等高；默认 1.15 让图标略大一圈，观感上才不显小。
        /// 垂直方向不再靠倍率凑，而是逐个字形按墨迹中心对齐正文（见 TextRun.OffsetY）。
        /// </summary>
        public double IconScale
        {
            get { return _iconScale; }
            set
            {
                value = Math.Max(0.4, Math.Min(3, value));
                if (Math.Abs(_iconScale - value) < 0.005) return;
                _iconScale = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public FontWeight FontWeight
        {
            get { return _fontWeight; }
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
            get { return _foreground; }
            set
            {
                if (value == null || value.Equals(_foreground)) return;
                _foreground = value;
                InvalidateVisual();
            }
        }

        public double LineHeight
        {
            get { return _lineHeight; }
            set
            {
                if (Math.Abs(_lineHeight - value) < 0.01) return;
                _lineHeight = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public TextAlignment Alignment
        {
            get { return _alignment; }
            set
            {
                if (value == _alignment) return;
                _alignment = value;
                InvalidateVisual();
            }
        }

        public bool Outline
        {
            get { return _outline; }
            set
            {
                if (value == _outline) return;
                _outline = value;
                InvalidateVisual();
            }
        }

        public Brush OutlineBrush
        {
            get { return _outlineBrush; }
            set
            {
                if (value == null || value.Equals(_outlineBrush)) return;
                _outlineBrush = value;
                InvalidateVisual();
            }
        }

        public double OutlineThickness
        {
            get { return _outlineThickness; }
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
            get { return _fixedWidth; }
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
            get { return _cursorVisible; }
            set
            {
                if (value == _cursorVisible) return;
                _cursorVisible = value;
                _blinkOn = true;
                if (value && _cursorBlink)
                {
                    _blink.Stop();
                    _blink.Start();
                }
                else
                {
                    _blink.Stop();
                }
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public string CursorStyle
        {
            get { return _cursorStyle; }
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
            get { return _cursorIndex; }
            set
            {
                if (_cursorIndex == value) return;
                _cursorIndex = value;
                InvalidateVisual();
            }
        }

        public bool CursorBlink
        {
            get { return _cursorBlink; }
            set
            {
                if (value == _cursorBlink) return;
                _cursorBlink = value;
                if (!value)
                {
                    _blink.Stop();
                    _blinkOn = true;
                }
                else if (_cursorVisible)
                {
                    _blink.Stop();
                    _blink.Start();
                }
                InvalidateVisual();
            }
        }

        // ==================== 度量 ====================

        protected override Size MeasureOverride(Size availableSize)
        {
            string layout = _layoutText ?? _text;
            List<string> lines = Split(layout);
            double lineHeight = LineHeightOf();

            double width = 0;
            foreach (string line in lines)
            {
                double lineWidth = BuildLine(line).Width;
                if (lineWidth > width) width = lineWidth;
            }

            // 光标画在行尾，给右侧留一点余量，避免被裁掉
            if (_cursorVisible) width += Math.Max(3, _fontSize * 0.75);

            double height = Math.Max(1, lineHeight * lines.Count);
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

            TextRun run = CreateRun("Ag", RunKind.Text);
            double textHeight = run.Text.Height > 0 ? run.Text.Height : _fontSize * 1.35;

            // 图标放大到超过行高时把行高撑开，否则会被裁掉；正常倍率下这条不生效。
            double iconRoom = _fontSize * _iconScale * 0.78 + 1;
            return Math.Max(textHeight, iconRoom);
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
            Icon
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
            public readonly List<TextRun> Runs = new List<TextRun>();
            public double Width;

            public void Add(TextRun run)
            {
                Runs.Add(run);
                Width += run.Width;
            }
        }

        /// <summary>
        /// 字形墨迹范围缓存（键 = 字体|字号|字重|文本）。对齐图标要用真实墨迹而不是字体度量：
        /// Reicon 的方框字形在 em 框里上下留白不对称，按 ascent / descent 算出来的中心会偏上。
        /// </summary>
        static readonly Dictionary<string, Rect> InkCache = new Dictionary<string, Rect>(StringComparer.Ordinal);

        /// <summary>
        /// 统一基线（正文数字「0」的基线）。整行所有 run 都排在同一条基线上，图标再按墨迹中心微调；
        /// 这样不会因为图标放大把整行基线顶下去（之前图标把行高撑出画布、正文跟着下移就是这个原因）。
        /// </summary>
        double _refBaseline = double.NaN;
        /// <summary>正文数字的墨迹中心相对基线的偏移。</summary>
        double _refInkCenter;

        TextRun CreateRun(string text, RunKind kind)
        {
            bool icon = kind == RunKind.Icon;
            FontFamily family = (icon || kind == RunKind.Keycap) ? KeycapFamily : _fontFamily;
            FontWeight weight = kind == RunKind.Text ? _fontWeight : FontWeights.Normal;
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

            if (kind != RunKind.Text)
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

        static string InkKey(FontFamily family, double size, FontWeight weight, string text)
        {
            return family.Source + "|" + size.ToString("0.###", CultureInfo.InvariantCulture)
                 + "|" + weight + "|" + text;
        }

        Rect InkBoundsOf(FormattedText ft, string key)
        {
            if (string.IsNullOrEmpty(key)) return Rect.Empty;

            Rect cached;
            if (InkCache.TryGetValue(key, out cached)) return cached;

            Rect bounds;
            try
            {
                Geometry geometry = ft.BuildGeometry(new Point(0, 0));
                bounds = geometry != null && !geometry.IsEmpty() ? geometry.Bounds : Rect.Empty;
            }
            catch
            {
                bounds = Rect.Empty;
            }

            if (InkCache.Count > 1024) InkCache.Clear();
            InkCache[key] = bounds;
            return bounds;
        }

        /// <summary>字体变了，正文基线 / 墨迹中心要重新量。</summary>
        void InvalidateMetrics()
        {
            _refBaseline = double.NaN;
            _refInkCenter = 0;
        }

        static RunKind KindOf(char c)
        {
            if (IsKeycapChar(c)) return RunKind.Keycap;
            if (IsIconChar(c)) return RunKind.Icon;
            return RunKind.Text;
        }

        /// <summary>把一行拆成「普通文字 / 键帽 / 图标」三种 run；后两种用内嵌字体的 PUA 字形绘制。</summary>
        TextLine BuildLine(string line)
        {
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

        // ==================== 绘制 ====================

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            EnsureMetrics();

            List<string> lines = Split(_text);
            double lineHeight = LineHeightOf();

            // 先量整块：卡片宽度受「只增不减」保护，可能比当前文字宽很多，
            // 整块按实测尺寸居中，左右留白才对称（之前extra宽度全堆在右边，看着就是"文字没居中"）。
            var built = new List<TextLine>(lines.Count);
            double blockWidth = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                TextLine line = BuildLine(lines[i]);
                built.Add(line);
                if (line.Width > blockWidth) blockWidth = line.Width;
            }

            double blockHeight = lines.Count * lineHeight;
            double originX = Math.Max(0, (ActualWidth - blockWidth) / 2);
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

            if (_cursorVisible) DrawCursor(dc, lines, lineHeight, originX, originY, blockWidth);
        }

        void DrawLine(DrawingContext dc, FormattedText ft, double x, double y)
        {
            // 先画描边再画填充：轮廓会往外扩，先描边能让填充盖住内侧一半线宽，
            // 文字本体保持清晰，边缘得到一圈实色描边。
            if (_outline && _outlineThickness > 0.05)
            {
                Geometry geometry = ft.BuildGeometry(new Point(x, y));
                if (geometry != null && !geometry.IsEmpty())
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

        void DrawCursor(DrawingContext dc, List<string> lines, double lineHeight,
                        double originX, double originY, double blockWidth)
        {
            string style = _cursorStyle ?? "Block";
            if (string.Equals(style, "None", StringComparison.OrdinalIgnoreCase)) return;
            if (_cursorBlink && !_blinkOn) return;

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
            if (lines.Count == 0) return;

            string line = lines[lineIndex];
            int column = 0;
            for (int i = lineStart; i < offset && i < _text.Length; i++)
            {
                if (_text[i] != '\r') column++;
            }
            if (column > line.Length) column = line.Length;

            string prefix = line.Substring(0, Math.Max(0, column));
            double lineWidth = BuildLine(line).Width;
            double prefixWidth = BuildLine(prefix).Width;
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
}
