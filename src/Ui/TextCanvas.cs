using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowOverlay.Ui
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
                FormattedText ft = Create(line);
                if (ft.WidthIncludingTrailingWhitespace > width)
                    width = ft.WidthIncludingTrailingWhitespace;
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
            FormattedText ft = Create("Ag");
            return ft.Height > 0 ? ft.Height : _fontSize * 1.35;
        }

        static List<string> Split(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string> { "" };

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] parts = normalized.Split('\n');
            return new List<string>(parts);
        }

        FormattedText Create(string line)
        {
            var ft = new FormattedText(
                line ?? "",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(_fontFamily, FontStyles.Normal, _fontWeight, FontStretches.Normal),
                _fontSize,
                _foreground ?? Brushes.White,
                null,
                TextFormattingMode.Display,
                PixelsPerDip());

            ft.TextAlignment = TextAlignment.Left;
            ft.Trimming = TextTrimming.None;
            return ft;
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

            List<string> lines = Split(_text);
            double lineHeight = LineHeightOf();

            for (int i = 0; i < lines.Count; i++)
            {
                FormattedText ft = Create(lines[i]);
                double x = AlignX(ft.WidthIncludingTrailingWhitespace, ActualWidth);
                double y = i * lineHeight;
                DrawLine(dc, ft, x, y);
            }

            if (_cursorVisible) DrawCursor(dc, lines, lineHeight);
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

        void DrawCursor(DrawingContext dc, List<string> lines, double lineHeight)
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
            double lineWidth = Create(line).WidthIncludingTrailingWhitespace;
            double prefixWidth = Create(prefix).WidthIncludingTrailingWhitespace;
            double x = AlignX(lineWidth, ActualWidth) + prefixWidth;
            double y = lineIndex * lineHeight;

            double blockWidth = Math.Max(2, _fontSize * 0.55);
            double blockHeight = Math.Max(3, lineHeight * 0.8);
            double blockTop = y + (lineHeight - blockHeight) / 2;

            Brush brush = _foreground ?? Brushes.White;

            if (string.Equals(style, "Bar", StringComparison.OrdinalIgnoreCase))
            {
                dc.DrawRectangle(brush, null, new Rect(x + 0.5, y + lineHeight * 0.06, 2, lineHeight * 0.88));
            }
            else if (string.Equals(style, "Underline", StringComparison.OrdinalIgnoreCase))
            {
                dc.DrawRectangle(brush, null, new Rect(x + 0.5, y + lineHeight - 3, blockWidth, 2));
            }
            else if (string.Equals(style, "Outline", StringComparison.OrdinalIgnoreCase))
            {
                var pen = new Pen(brush, 1.3);
                pen.Freeze();
                dc.DrawRectangle(null, pen, new Rect(x + 1, blockTop, blockWidth, blockHeight));
            }
            else
            {
                dc.DrawRectangle(brush, null, new Rect(x + 1, blockTop, blockWidth, blockHeight));
            }
        }
    }
}
