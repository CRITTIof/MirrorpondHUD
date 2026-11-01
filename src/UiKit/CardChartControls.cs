using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 折线图 / 柱状图共用的纯绘制工具：坐标范围、刻度、文字、系列配色、折线几何。
    /// 只做计算和 <see cref="DrawingContext"/> 绘制，不持有控件状态，颜色全部来自主题令牌。
    /// </summary>
    internal static class ChartDraw
    {
        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>卡片前景色（所有灰阶文字的基准）。</summary>
        public static Color Foreground(StyleSettings style)
        {
            return CardPalette.TryColor(style == null ? null : style.Foreground,
                                        Color.FromRgb(0xE6, 0xED, 0xF3));
        }

        public static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static Pen Pen(Color color, double thickness)
        {
            var pen = new Pen(Brush(color), Math.Max(0.5, thickness));
            pen.Freeze();
            return pen;
        }

        public static FormattedText Text(string text, FontFamily family, double size,
                                         Brush brush, double pixelsPerDip)
        {
            return new FormattedText(text ?? "",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(family ?? new FontFamily("Microsoft YaHei UI"),
                             FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                Math.Max(6, size),
                brush,
                pixelsPerDip);
        }

        /// <summary>按设置里的格式显示数值；格式串非法时退回默认格式，不抛异常。</summary>
        public static string FormatValue(double value, string format)
        {
            if (!IsFinite(value)) return "";
            string text = string.IsNullOrWhiteSpace(format) ? "0.##" : format;
            try { return value.ToString(text, CultureInfo.CurrentCulture); }
            catch { return value.ToString("0.##", CultureInfo.CurrentCulture); }
        }

        /// <summary>
        /// 计算纵轴范围与刻度步长。数据为空 / 全是非有限值返回 false。
        /// AutoScale 下所有值相同时会给正值加 padding，避免范围塌成 0 高度。
        /// </summary>
        public static bool TryRange(IEnumerable<double> values, bool autoScale, double minimum, double maximum,
                                    int tickCount, bool includeZero,
                                    out double low, out double high, out double step)
        {
            low = 0;
            high = 1;
            step = 0.5;

            bool any = false;
            double min = double.MaxValue;
            double max = double.MinValue;
            if (values != null)
            {
                foreach (double value in values)
                {
                    if (!IsFinite(value)) continue;
                    any = true;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
            }
            if (!any) return false;

            if (!autoScale && IsFinite(minimum) && IsFinite(maximum) && maximum > minimum)
            {
                min = minimum;
                max = maximum;
            }

            if (includeZero)
            {
                if (min > 0) min = 0;
                if (max < 0) max = 0;
            }

            if (!(max > min))
            {
                double pad = Math.Abs(max) > 0.000001 ? Math.Abs(max) * 0.1 : 1;
                min -= pad;
                max += pad;
            }

            tickCount = Math.Max(2, Math.Min(8, tickCount));
            double rough = (max - min) / (tickCount - 1);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            if (!IsFinite(magnitude) || magnitude <= 0) magnitude = 1;

            double normalized = rough / magnitude;
            double nice = normalized <= 1 ? 1
                        : normalized <= 2 ? 2
                        : normalized <= 2.5 ? 2.5
                        : normalized <= 5 ? 5
                        : 10;
            step = nice * magnitude;
            low = Math.Floor(min / step) * step;
            high = Math.Ceiling(max / step) * step;
            if (!(high > low)) high = low + step;
            return IsFinite(low) && IsFinite(high) && IsFinite(step) && step > 0;
        }

        /// <summary>系列默认配色：强调色 / 拒绝色 / 前景色轮换，再叠两档半透明。</summary>
        public static Color SeriesColor(ThemeTokens tokens, Color foreground, int index)
        {
            int slot = ((index % 6) + 6) % 6;
            switch (slot)
            {
                case 0: return tokens.Accent;
                case 1: return tokens.Danger;
                case 2: return foreground;
                case 3: return ThemeResolver.WithAlpha(tokens.Accent, 0.55);
                case 4: return ThemeResolver.WithAlpha(tokens.Danger, 0.55);
                default: return tokens.Muted;
            }
        }

        /// <summary>计算绘图区；控件太小 / 留白后没有空间时返回 false。</summary>
        public static bool TryPlot(double width, double height, bool yLabels, double topInset,
                                   double bottomInset, out Rect plot)
        {
            plot = new Rect(0, 0, 0, 0);
            if (width < 2 || height < 2) return false;

            double left = yLabels ? 38 : 8;
            double right = 10;
            double top = Math.Max(4, topInset);
            double bottom = Math.Max(4, bottomInset);
            double plotWidth = width - left - right;
            double plotHeight = height - top - bottom;
            if (plotWidth < 2 || plotHeight < 2) return false;

            plot = new Rect(left, top, plotWidth, plotHeight);
            return true;
        }

        /// <summary>把一条序列投影成屏幕坐标；无效值会断开折线（分段返回）。</summary>
        public static List<List<Point>> ProjectSegments(IList<double> values, Rect plot,
                                                        double low, double high)
        {
            var segments = new List<List<Point>>();
            if (values == null || values.Count == 0) return segments;

            double span = high - low;
            if (!(span > 0)) return segments;

            int count = values.Count;
            List<Point> current = null;
            for (int i = 0; i < count; i++)
            {
                double value = values[i];
                if (!IsFinite(value))
                {
                    current = null;
                    continue;
                }

                double x = count <= 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * i / (count - 1);
                double y = plot.Bottom - plot.Height * (value - low) / span;
                if (y < plot.Top) y = plot.Top;
                if (y > plot.Bottom) y = plot.Bottom;

                if (current == null)
                {
                    current = new List<Point>();
                    segments.Add(current);
                }
                current.Add(new Point(x, y));
            }

            return segments;
        }

        /// <summary>画折线（可选面积填充）。单点序列画一个小圆点。</summary>
        public static void DrawSegments(DrawingContext dc, List<List<Point>> segments,
                                        Pen pen, Brush areaBrush, double baseline)
        {
            if (segments == null || segments.Count == 0) return;

            for (int s = 0; s < segments.Count; s++)
            {
                List<Point> points = segments[s];
                if (points.Count == 0) continue;

                if (areaBrush != null)
                {
                    var area = new StreamGeometry();
                    using (StreamGeometryContext context = area.Open())
                    {
                        context.BeginFigure(new Point(points[0].X, baseline), true, true);
                        for (int i = 0; i < points.Count; i++) context.LineTo(points[i], true, false);
                        context.LineTo(new Point(points[points.Count - 1].X, baseline), true, false);
                    }
                    area.Freeze();
                    dc.DrawGeometry(areaBrush, null, area);
                }

                if (points.Count == 1)
                {
                    double radius = Math.Max(1.5, pen.Thickness * 1.2);
                    dc.DrawEllipse(pen.Brush, null, points[0], radius, radius);
                    continue;
                }

                var line = new StreamGeometry();
                using (StreamGeometryContext context = line.Open())
                {
                    context.BeginFigure(points[0], false, false);
                    for (int i = 1; i < points.Count; i++) context.LineTo(points[i], true, false);
                }
                line.Freeze();
                dc.DrawGeometry(null, pen, line);
            }
        }

        /// <summary>画横向网格 + 纵轴刻度文字（4~5 个刻度）。</summary>
        public static void DrawYAxis(DrawingContext dc, Rect plot, double low, double high, double step,
                                     bool showGrid, bool showLabels, string valueFormat,
                                     FontFamily font, double fontSize, Color gridColor, Color labelColor,
                                     double pixelsPerDip)
        {
            if (!(high > low) || !(step > 0)) return;

            Pen gridPen = Pen(gridColor, 1);
            Brush labelBrush = Brush(labelColor);
            int count = (int)Math.Round((high - low) / step);
            if (count < 0) count = 0;
            if (count > 12) count = 12;

            for (int i = 0; i <= count; i++)
            {
                double value = low + step * i;
                double y = plot.Bottom - plot.Height * (value - low) / (high - low);
                if (showGrid) dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));

                if (!showLabels) continue;
                FormattedText text = Text(FormatValue(value, valueFormat), font, fontSize, labelBrush, pixelsPerDip);
                dc.DrawText(text, new Point(plot.Left - text.Width - 4, y - text.Height / 2));
            }
        }

        /// <summary>画横轴标签：数据点太多时按等距抽稀，文字过长用省略号。</summary>
        public static void DrawXLabels(DrawingContext dc, IList<string> labels, int pointCount, Rect plot,
                                       FontFamily font, double fontSize, Color labelColor,
                                       double pixelsPerDip)
        {
            if (labels == null || labels.Count == 0 || pointCount <= 0) return;

            int maxLabels = Math.Max(2, (int)(plot.Width / 44));
            int stride = Math.Max(1, (int)Math.Ceiling(labels.Count / (double)maxLabels));
            bool aligned = labels.Count == pointCount;
            Brush brush = Brush(labelColor);

            for (int i = 0; i < labels.Count; i += stride)
            {
                if (aligned && i >= pointCount) break;

                string label = labels[i];
                if (string.IsNullOrWhiteSpace(label)) continue;

                double x;
                if (aligned)
                {
                    x = pointCount <= 1
                        ? plot.Left + plot.Width / 2
                        : plot.Left + plot.Width * i / (pointCount - 1);
                }
                else
                {
                    x = labels.Count <= 1
                        ? plot.Left + plot.Width / 2
                        : plot.Left + plot.Width * i / (labels.Count - 1);
                }

                FormattedText text = Text(label, font, fontSize, brush, pixelsPerDip);
                text.MaxTextWidth = Math.Max(20, plot.Width / Math.Max(1, maxLabels) - 4);
                text.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(text, new Point(x - text.Width / 2, plot.Bottom + 3));
            }
        }

        /// <summary>画图例；名字为空 / 放不下就停。</summary>
        public static void DrawLegend(DrawingContext dc, IList<CardChartSeries> series, Rect plot,
                                      FontFamily font, double fontSize, ThemeTokens tokens, Color foreground,
                                      double pixelsPerDip)
        {
            if (series == null || series.Count == 0 || plot.Top < 12) return;

            double size = Math.Max(6, fontSize - 4);
            double y = Math.Max(1, plot.Top - fontSize - 4);
            double x = plot.Left;
            Brush textBrush = Brush(ThemeResolver.WithAlpha(foreground, 0.72));

            for (int i = 0; i < series.Count; i++)
            {
                CardChartSeries entry = series[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;

                Color color = entry.Color.HasValue
                    ? entry.Color.Value
                    : SeriesColor(tokens, foreground, i);
                FormattedText text = Text(entry.Name, font, fontSize, textBrush, pixelsPerDip);

                double advance = size + 4 + text.Width + 14;
                if (x + advance > plot.Right + 1) break;

                dc.DrawRectangle(Brush(color), null, new Rect(x, y + (fontSize - size) / 2, size, size));
                dc.DrawText(text, new Point(x + size + 4, y));
                x += advance;
            }
        }
    }

    /// <summary>
    /// 迷你折线 / 面积图：没有坐标轴，只用来在卡片里显示一串数值的趋势。
    /// 宽度高度由调用方给，默认最小高度 40；数值可以是 NaN / Infinity（断开不画）。
    /// </summary>
    public class CardSparkline : FrameworkElement
    {
        readonly List<double> _values = new List<double>();
        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        bool _autoScale = true;
        bool _fill = true;
        double _minimum = double.NaN;
        double _maximum = double.NaN;
        double _lineThickness = 1.6;

        /// <summary>空趋势线：默认最小高度 40，颜色先用默认主题。</summary>
        public CardSparkline()
        {
            MinHeight = 40;
            SnapsToDevicePixels = true;
            ApplyStyle(null, 8);
        }

        /// <summary>数值序列（直接往里加不会自动重绘，批量改完请调 <see cref="SetValues"/> 或等待布局）。</summary>
        public IList<double> Values
        {
            get { return _values; }
            set
            {
                _values.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++) _values.Add(value[i]);
                }
                InvalidateVisual();
            }
        }

        /// <summary>是否按数据自动缩放纵轴（false 时用 Minimum / Maximum）。</summary>
        public bool AutoScale
        {
            get { return _autoScale; }
            set { if (_autoScale == value) return; _autoScale = value; InvalidateVisual(); }
        }

        /// <summary>AutoScale = false 时的纵轴下限（NaN = 仍然自动取）。</summary>
        public double Minimum
        {
            get { return _minimum; }
            set { if (_minimum.Equals(value)) return; _minimum = value; InvalidateVisual(); }
        }

        /// <summary>AutoScale = false 时的纵轴上限（NaN = 仍然自动取）。</summary>
        public double Maximum
        {
            get { return _maximum; }
            set { if (_maximum.Equals(value)) return; _maximum = value; InvalidateVisual(); }
        }

        /// <summary>折线粗细（0.5~6）。</summary>
        public double LineThickness
        {
            get { return _lineThickness; }
            set
            {
                double next = Math.Max(0.5, Math.Min(6, value));
                if (Math.Abs(_lineThickness - next) < 0.001) return;
                _lineThickness = next;
                InvalidateVisual();
            }
        }

        /// <summary>是否在折线下面填一层淡淡的面积色。</summary>
        public bool Fill
        {
            get { return _fill; }
            set { if (_fill == value) return; _fill = value; InvalidateVisual(); }
        }

        /// <summary>批量替换数据并重绘。</summary>
        public void SetValues(IEnumerable<double> values)
        {
            _values.Clear();
            if (values != null)
            {
                foreach (double value in values) _values.Add(value);
            }
            InvalidateVisual();
        }

        /// <summary>追加一个值并重绘。</summary>
        public void Add(double value)
        {
            _values.Add(value);
            InvalidateVisual();
        }

        /// <summary>套用卡片外观（颜色取主题强调色，圆角参数保留给后续扩展）。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _tokens = ThemeResolver.Resolve(style);
            _ = Math.Max(0, Math.Min(60, cornerRadius));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 2 || height < 2) return;
            if (_values.Count == 0) return;

            double low, high, step;
            if (!ChartDraw.TryRange(_values, _autoScale, _minimum, _maximum, 5, false,
                                    out low, out high, out step)) return;

            var plot = new Rect(1, 1, Math.Max(1, width - 2), Math.Max(1, height - 2));
            List<List<Point>> segments = ChartDraw.ProjectSegments(_values, plot, low, high);

            Pen pen = ChartDraw.Pen(_tokens.Accent, _lineThickness);
            Brush area = _fill ? CardPalette.Brush(_tokens.Accent, 0.16) : null;
            ChartDraw.DrawSegments(dc, segments, pen, area, plot.Bottom);
        }
    }

    /// <summary>一条图表序列：名字、数据、颜色（空 = 按主题色轮换取色）。</summary>
    public sealed class CardChartSeries
    {
        /// <summary>图例里显示的名字；空 = 不进图例。</summary>
        public string Name { get; set; } = "";

        /// <summary>数据点（NaN / Infinity 会断开折线，柱状图跳过该点）。</summary>
        public IList<double> Values { get; set; } = new List<double>();

        /// <summary>指定颜色；null = 用主题色轮换。</summary>
        public Color? Color { get; set; }

        public CardChartSeries()
        {
        }

        public CardChartSeries(string name)
        {
            Name = name ?? "";
        }
    }

    /// <summary>
    /// 折线图：横向网格、纵轴 4~5 个刻度、横轴标签、图例，可选面积填充。
    /// 空的 / 全为非有限值的数据不会崩，也不会画坐标轴以外的垃圾。
    /// </summary>
    public class CardLineChart : FrameworkElement
    {
        readonly List<CardChartSeries> _series = new List<CardChartSeries>();
        readonly List<string> _labels = new List<string>();
        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        FontFamily _font = new FontFamily("Microsoft YaHei UI");
        double _fontSize = 12;
        bool _autoScale = true;
        bool _showGrid = true;
        bool _showLegend = true;
        bool _showYLabels = true;
        bool _showXLabels = true;
        bool _fill;
        double _minimum = double.NaN;
        double _maximum = double.NaN;
        string _valueFormat = "0.##";

        /// <summary>空折线图：默认最小高度 120。</summary>
        public CardLineChart()
        {
            MinHeight = 120;
            SnapsToDevicePixels = true;
            ApplyStyle(null, 8);
        }

        /// <summary>数据序列（直接改列表不会自动重绘，改完可重新 ApplyStyle 或等待布局）。</summary>
        public IList<CardChartSeries> Series
        {
            get { return _series; }
            set
            {
                _series.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++)
                    {
                        if (value[i] != null) _series.Add(value[i]);
                    }
                }
                InvalidateVisual();
            }
        }

        /// <summary>横轴标签（数量可以少于数据点；不等长时按整段均匀分布）。</summary>
        public IList<string> Labels
        {
            get { return _labels; }
            set
            {
                _labels.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++) _labels.Add(value[i] ?? "");
                }
                InvalidateVisual();
            }
        }

        public bool AutoScale
        {
            get { return _autoScale; }
            set { if (_autoScale == value) return; _autoScale = value; InvalidateVisual(); }
        }

        public double Minimum
        {
            get { return _minimum; }
            set { if (_minimum.Equals(value)) return; _minimum = value; InvalidateVisual(); }
        }

        public double Maximum
        {
            get { return _maximum; }
            set { if (_maximum.Equals(value)) return; _maximum = value; InvalidateVisual(); }
        }

        public bool ShowGrid
        {
            get { return _showGrid; }
            set { if (_showGrid == value) return; _showGrid = value; InvalidateVisual(); }
        }

        public bool ShowLegend
        {
            get { return _showLegend; }
            set { if (_showLegend == value) return; _showLegend = value; InvalidateVisual(); }
        }

        public bool ShowYLabels
        {
            get { return _showYLabels; }
            set { if (_showYLabels == value) return; _showYLabels = value; InvalidateVisual(); }
        }

        public bool ShowXLabels
        {
            get { return _showXLabels; }
            set { if (_showXLabels == value) return; _showXLabels = value; InvalidateVisual(); }
        }

        /// <summary>纵轴刻度数字格式（默认 "0.##"）。</summary>
        public string ValueFormat
        {
            get { return _valueFormat; }
            set { _valueFormat = string.IsNullOrWhiteSpace(value) ? "0.##" : value; InvalidateVisual(); }
        }

        /// <summary>是否给每条折线下面填面积色。</summary>
        public bool Fill
        {
            get { return _fill; }
            set { if (_fill == value) return; _fill = value; InvalidateVisual(); }
        }

        /// <summary>套用卡片外观：字体、前景色、强调色 / 拒绝色 / 弱化色、圆角。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _tokens = ThemeResolver.Resolve(style);
            _foreground = ChartDraw.Foreground(style);
            _font = CardPalette.ResolveFont(style == null ? null : style.FontFamily);
            _fontSize = Math.Max(9, (style == null ? 15 : style.FontSize) - 3);
            _ = Math.Max(0, Math.Min(60, cornerRadius));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 2 || height < 2) return;

            int pointCount = 0;
            for (int i = 0; i < _series.Count; i++)
            {
                CardChartSeries entry = _series[i];
                if (entry == null || entry.Values == null) continue;
                if (entry.Values.Count > pointCount) pointCount = entry.Values.Count;
            }
            if (pointCount == 0) return;

            double topInset = _showLegend ? Math.Max(16, _fontSize + 8) : 6;
            double bottomInset = _showXLabels && _labels.Count > 0 ? Math.Max(16, _fontSize + 6) : 6;
            Rect plot;
            if (!ChartDraw.TryPlot(width, height, _showYLabels, topInset, bottomInset, out plot)) return;

            double low, high, step;
            if (!ChartDraw.TryRange(AllValues(), _autoScale, _minimum, _maximum, 5, false,
                                    out low, out high, out step)) return;

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            ChartDraw.DrawYAxis(dc, plot, low, high, step, _showGrid, _showYLabels, _valueFormat,
                                _font, _fontSize, _tokens.Border,
                                ThemeResolver.WithAlpha(_foreground, 0.45), pixelsPerDip);

            for (int i = 0; i < _series.Count; i++)
            {
                CardChartSeries entry = _series[i];
                if (entry == null || entry.Values == null || entry.Values.Count == 0) continue;

                Color color = entry.Color.HasValue
                    ? entry.Color.Value
                    : ChartDraw.SeriesColor(_tokens, _foreground, i);
                Pen pen = ChartDraw.Pen(color, 1.8);
                Brush area = _fill ? CardPalette.Brush(color, 0.16) : null;

                List<List<Point>> segments = ChartDraw.ProjectSegments(entry.Values, plot, low, high);
                ChartDraw.DrawSegments(dc, segments, pen, area, plot.Bottom);
            }

            if (_showLegend) ChartDraw.DrawLegend(dc, _series, plot, _font, Math.Max(9, _fontSize - 2),
                                                 _tokens, _foreground, pixelsPerDip);

            if (_showXLabels && _labels.Count > 0)
            {
                ChartDraw.DrawXLabels(dc, _labels, pointCount, plot, _font, Math.Max(9, _fontSize - 2),
                                      ThemeResolver.WithAlpha(_foreground, 0.55), pixelsPerDip);
            }
        }

        IEnumerable<double> AllValues()
        {
            for (int i = 0; i < _series.Count; i++)
            {
                CardChartSeries entry = _series[i];
                if (entry == null || entry.Values == null) continue;
                for (int j = 0; j < entry.Values.Count; j++) yield return entry.Values[j];
            }
        }
    }

    /// <summary>
    /// 分组柱状图：同一横轴位置上的多个序列并排成一组，单序列时就是普通柱状图。
    /// 网格 / 纵轴刻度 / 横轴标签 / 图例与折线图一致。
    /// </summary>
    public class CardBarChart : FrameworkElement
    {
        readonly List<CardChartSeries> _series = new List<CardChartSeries>();
        readonly List<string> _labels = new List<string>();
        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        FontFamily _font = new FontFamily("Microsoft YaHei UI");
        double _fontSize = 12;
        double _radius = 8;
        bool _autoScale = true;
        bool _showGrid = true;
        bool _showLegend = true;
        bool _showYLabels = true;
        bool _showXLabels = true;
        double _minimum = double.NaN;
        double _maximum = double.NaN;
        string _valueFormat = "0.##";

        /// <summary>空柱状图：默认最小高度 120。</summary>
        public CardBarChart()
        {
            MinHeight = 120;
            SnapsToDevicePixels = true;
            ApplyStyle(null, 8);
        }

        /// <summary>数据序列（直接改列表不会自动重绘，改完可重新 ApplyStyle 或等待布局）。</summary>
        public IList<CardChartSeries> Series
        {
            get { return _series; }
            set
            {
                _series.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++)
                    {
                        if (value[i] != null) _series.Add(value[i]);
                    }
                }
                InvalidateVisual();
            }
        }

        /// <summary>横轴标签（数量可以少于数据点；不等长时按整段均匀分布）。</summary>
        public IList<string> Labels
        {
            get { return _labels; }
            set
            {
                _labels.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++) _labels.Add(value[i] ?? "");
                }
                InvalidateVisual();
            }
        }

        public bool AutoScale
        {
            get { return _autoScale; }
            set { if (_autoScale == value) return; _autoScale = value; InvalidateVisual(); }
        }

        public double Minimum
        {
            get { return _minimum; }
            set { if (_minimum.Equals(value)) return; _minimum = value; InvalidateVisual(); }
        }

        public double Maximum
        {
            get { return _maximum; }
            set { if (_maximum.Equals(value)) return; _maximum = value; InvalidateVisual(); }
        }

        public bool ShowGrid
        {
            get { return _showGrid; }
            set { if (_showGrid == value) return; _showGrid = value; InvalidateVisual(); }
        }

        public bool ShowLegend
        {
            get { return _showLegend; }
            set { if (_showLegend == value) return; _showLegend = value; InvalidateVisual(); }
        }

        public bool ShowYLabels
        {
            get { return _showYLabels; }
            set { if (_showYLabels == value) return; _showYLabels = value; InvalidateVisual(); }
        }

        public bool ShowXLabels
        {
            get { return _showXLabels; }
            set { if (_showXLabels == value) return; _showXLabels = value; InvalidateVisual(); }
        }

        /// <summary>纵轴刻度数字格式（默认 "0.##"）。</summary>
        public string ValueFormat
        {
            get { return _valueFormat; }
            set { _valueFormat = string.IsNullOrWhiteSpace(value) ? "0.##" : value; InvalidateVisual(); }
        }

        /// <summary>套用卡片外观：字体、前景色、强调色 / 拒绝色 / 弱化色、圆角。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _tokens = ThemeResolver.Resolve(style);
            _foreground = ChartDraw.Foreground(style);
            _font = CardPalette.ResolveFont(style == null ? null : style.FontFamily);
            _fontSize = Math.Max(9, (style == null ? 15 : style.FontSize) - 3);
            _radius = Math.Max(0, Math.Min(60, cornerRadius));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 2 || height < 2) return;

            int pointCount = 0;
            int seriesCount = 0;
            for (int i = 0; i < _series.Count; i++)
            {
                CardChartSeries entry = _series[i];
                if (entry == null || entry.Values == null) continue;
                seriesCount++;
                if (entry.Values.Count > pointCount) pointCount = entry.Values.Count;
            }
            if (pointCount == 0 || seriesCount == 0) return;

            double topInset = _showLegend ? Math.Max(16, _fontSize + 8) : 6;
            double bottomInset = _showXLabels && _labels.Count > 0 ? Math.Max(16, _fontSize + 6) : 6;
            Rect plot;
            if (!ChartDraw.TryPlot(width, height, _showYLabels, topInset, bottomInset, out plot)) return;

            double low, high, step;
            if (!ChartDraw.TryRange(AllValues(), _autoScale, _minimum, _maximum, 5, true,
                                    out low, out high, out step)) return;

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            ChartDraw.DrawYAxis(dc, plot, low, high, step, _showGrid, _showYLabels, _valueFormat,
                                _font, _fontSize, _tokens.Border,
                                ThemeResolver.WithAlpha(_foreground, 0.45), pixelsPerDip);

            double span = high - low;
            double groupWidth = plot.Width / pointCount;
            double barWidth = Math.Max(1, groupWidth * 0.78 / seriesCount);
            double zeroY = plot.Bottom - plot.Height * (0 - low) / span;
            if (zeroY < plot.Top) zeroY = plot.Top;
            if (zeroY > plot.Bottom) zeroY = plot.Bottom;
            double barRadius = Math.Max(0, Math.Min(3, _radius * 0.35));

            int slot = 0;
            for (int s = 0; s < _series.Count; s++)
            {
                CardChartSeries entry = _series[s];
                if (entry == null || entry.Values == null) continue;

                Color color = entry.Color.HasValue
                    ? entry.Color.Value
                    : ChartDraw.SeriesColor(_tokens, _foreground, s);
                Brush brush = ChartDraw.Brush(color);

                for (int i = 0; i < entry.Values.Count && i < pointCount; i++)
                {
                    double value = entry.Values[i];
                    if (!ChartDraw.IsFinite(value)) continue;

                    double y = plot.Bottom - plot.Height * (value - low) / span;
                    if (y < plot.Top) y = plot.Top;
                    if (y > plot.Bottom) y = plot.Bottom;

                    double top = Math.Min(y, zeroY);
                    double barHeight = Math.Abs(zeroY - y);
                    if (barHeight < 1) barHeight = 1;

                    double x = plot.Left + groupWidth * i + (groupWidth - barWidth * seriesCount) / 2
                               + barWidth * slot;
                    var rect = new Rect(x, top, Math.Max(1, barWidth * 0.92), barHeight);
                    if (barRadius > 0.1) dc.DrawRoundedRectangle(brush, null, rect, barRadius, barRadius);
                    else dc.DrawRectangle(brush, null, rect);
                }
                slot++;
            }

            if (_showLegend) ChartDraw.DrawLegend(dc, _series, plot, _font, Math.Max(9, _fontSize - 2),
                                                 _tokens, _foreground, pixelsPerDip);

            if (_showXLabels && _labels.Count > 0)
            {
                ChartDraw.DrawXLabels(dc, _labels, pointCount, plot, _font, Math.Max(9, _fontSize - 2),
                                      ThemeResolver.WithAlpha(_foreground, 0.55), pixelsPerDip);
            }
        }

        IEnumerable<double> AllValues()
        {
            for (int i = 0; i < _series.Count; i++)
            {
                CardChartSeries entry = _series[i];
                if (entry == null || entry.Values == null) continue;
                for (int j = 0; j < entry.Values.Count; j++) yield return entry.Values[j];
            }
        }
    }
}
