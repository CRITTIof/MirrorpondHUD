using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>数据控件的语气色，和 <see cref="ThemeTokens"/> 的 Accent / Danger / Muted 对齐。</summary>
    public enum CardBadgeTone
    {
        Neutral,
        Info,
        Success,
        Warning,
        Danger,
        Accent
    }

    /// <summary>指标块的趋势方向。</summary>
    public enum CardMetricTrend
    {
        None,
        Up,
        Down,
        Flat
    }

    // =====================================================================================
    // CardBadge / CardTag / CardMetricTile
    // =====================================================================================

    /// <summary>
    /// 小徽标：状态、计数、只读标记。单行、自动省略，不抢标题。
    /// </summary>
    public sealed class CardBadge : Border, ICardFormControl
    {
        readonly TextBlock _text;
        StyleSettings _style;
        CardBadgeTone _tone = CardBadgeTone.Neutral;

        public CardBadge(string text = null, CardBadgeTone tone = CardBadgeTone.Neutral)
        {
            Padding = new Thickness(6, 1, 6, 2);
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            VerticalAlignment = VerticalAlignment.Center;
            _text = new TextBlock
            {
                Text = text ?? "",
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Child = _text;
            _tone = tone;
            ApplyStyle(null, 4);
        }

        public event Action<string> TextChanged;

        public string Text
        {
            get { return _text.Text ?? ""; }
            set
            {
                _text.Text = value ?? "";
                var handler = TextChanged;
                if (handler != null) handler(_text.Text);
            }
        }

        public CardBadgeTone Tone
        {
            get { return _tone; }
            set { _tone = value; ApplyStyle(_style, CornerRadius.TopLeft); }
        }

        public bool Dot { get; set; }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(_style);
            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            Color tone = CardDataStyle.ToneColor(tokens, foreground, _tone);

            CornerRadius = new CornerRadius(Math.Max(2, Math.Min(6, cornerRadius * 0.55)));
            Background = CardPalette.Brush(tone, 0.12);
            BorderBrush = CardPalette.Brush(tone, 0.28);
            _text.FontFamily = CardPalette.ResolveFont(_style.FontFamily);
            _text.FontSize = Math.Max(9, _style.FontSize - 2);
            _text.Foreground = CardPalette.Brush(tone);
        }
    }

    /// <summary>
    /// 标签 / 胶囊：比徽标更圆，适合多个并列；可选“×”移除。
    /// </summary>
    public sealed class CardTag : Border, ICardFormControl
    {
        readonly StackPanel _row;
        readonly TextBlock _text;
        readonly TextBlock _remove;
        StyleSettings _style;
        CardBadgeTone _tone = CardBadgeTone.Neutral;
        bool _removable;

        public CardTag(string text = null, CardBadgeTone tone = CardBadgeTone.Neutral)
        {
            Padding = new Thickness(8, 2, 8, 3);
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(10);
            VerticalAlignment = VerticalAlignment.Center;
            _text = new TextBlock
            {
                Text = text ?? "",
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            _remove = new TextBlock
            {
                Text = "×",
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            _remove.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                var handler = Removed;
                if (handler != null) handler(this);
            };
            _row = new StackPanel { Orientation = Orientation.Horizontal };
            _row.Children.Add(_text);
            _row.Children.Add(_remove);
            Child = _row;
            _tone = tone;
            ApplyStyle(null, 10);
        }

        public event Action<CardTag> Removed;

        public string Text
        {
            get { return _text.Text ?? ""; }
            set { _text.Text = value ?? ""; }
        }

        public CardBadgeTone Tone
        {
            get { return _tone; }
            set { _tone = value; ApplyStyle(_style, CornerRadius.TopLeft); }
        }

        public bool Removable
        {
            get { return _removable; }
            set
            {
                _removable = value;
                _remove.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(_style);
            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            Color tone = CardDataStyle.ToneColor(tokens, foreground, _tone);

            CornerRadius = new CornerRadius(Math.Max(6, Math.Min(14, cornerRadius + 4)));
            Background = CardPalette.Brush(tone, 0.10);
            BorderBrush = CardPalette.Brush(tone, 0.24);

            FontFamily family = CardPalette.ResolveFont(_style.FontFamily);
            _text.FontFamily = family;
            _text.FontSize = Math.Max(9, _style.FontSize - 2);
            _text.Foreground = CardPalette.Brush(tone);
            _remove.FontFamily = family;
            _remove.FontSize = Math.Max(10, _style.FontSize);
            _remove.Foreground = CardPalette.Brush(tone, 0.7);
        }
    }

    /// <summary>
    /// 监控 / 行情 / 任务概览用的小指标块：标题、主数值 + 单位、趋势、说明。
    /// </summary>
    public sealed class CardMetricTile : Border, ICardFormControl
    {
        readonly StackPanel _root;
        readonly TextBlock _title;
        readonly TextBlock _value;
        readonly TextBlock _unit;
        readonly TextBlock _delta;
        readonly TextBlock _caption;
        readonly TextBlock _icon;

        StyleSettings _style;
        double _cornerRadius = 8;
        CardBadgeTone _tone = CardBadgeTone.Accent;
        CardMetricTrend _trend = CardMetricTrend.None;

        public CardMetricTile(string title = null, string value = "0")
        {
            BorderThickness = new Thickness(1);
            Padding = new Thickness(10, 8, 10, 8);
            MinWidth = 96;

            _icon = new TextBlock { Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            _title = new TextBlock { Text = title ?? "", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(_icon);
            titleRow.Children.Add(_title);

            _value = new TextBlock { Text = value ?? "0", VerticalAlignment = VerticalAlignment.Bottom };
            _unit = new TextBlock { Margin = new Thickness(4, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom };
            var valueRow = new StackPanel { Orientation = Orientation.Horizontal };
            valueRow.Children.Add(_value);
            valueRow.Children.Add(_unit);

            _delta = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _caption = new TextBlock { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var foot = new StackPanel { Orientation = Orientation.Horizontal };
            foot.Children.Add(_delta);
            foot.Children.Add(_caption);

            _root = new StackPanel { Orientation = Orientation.Vertical };
            _root.Children.Add(titleRow);
            _root.Children.Add(valueRow);
            _root.Children.Add(foot);
            Child = _root;

            ApplyStyle(null, 8);
        }

        public string Title
        {
            get { return _title.Text ?? ""; }
            set { _title.Text = value ?? ""; }
        }

        public string Value
        {
            get { return _value.Text ?? ""; }
            set { _value.Text = value ?? ""; }
        }

        public string Unit
        {
            get { return _unit.Text ?? ""; }
            set { _unit.Text = value ?? ""; }
        }

        public string Caption
        {
            get { return _caption.Text ?? ""; }
            set { _caption.Text = value ?? ""; }
        }

        public string Delta
        {
            get { return _delta.Text ?? ""; }
            set { _delta.Text = value ?? ""; }
        }

        public string Icon
        {
            get { return _icon.Text ?? ""; }
            set
            {
                _icon.Text = value ?? "";
                _icon.Visibility = string.IsNullOrEmpty(_icon.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public CardBadgeTone Tone
        {
            get { return _tone; }
            set { _tone = value; ApplyStyle(_style, _cornerRadius); }
        }

        public CardMetricTrend Trend
        {
            get { return _trend; }
            set { _trend = value; UpdateTrend(); }
        }

        /// <summary>一次设置主数值、趋势文字与说明，便于监控卡每帧刷新。</summary>
        public void SetValue(string value, string delta = null, string caption = null)
        {
            Value = value;
            if (delta != null) Delta = delta;
            if (caption != null) Caption = caption;
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _cornerRadius = cornerRadius;
            ThemeTokens tokens = ThemeResolver.Resolve(_style);
            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            Color tone = CardDataStyle.ToneColor(tokens, foreground, _tone);

            CornerRadius = new CornerRadius(Math.Max(4, Math.Min(14, cornerRadius)));
            Background = CardPalette.Brush(tokens.Panel);
            BorderBrush = CardPalette.Brush(tokens.Border);

            FontFamily family = CardPalette.ResolveFont(_style.FontFamily);
            double baseSize = Math.Max(9, _style.FontSize);
            _title.FontFamily = family;
            _title.FontSize = Math.Max(9, baseSize - 2);
            _title.Foreground = CardPalette.Brush(foreground, 0.58);
            _icon.FontFamily = family;
            _icon.FontSize = Math.Max(9, baseSize - 1);
            _icon.Foreground = CardPalette.Brush(tone, 0.9);

            _value.FontFamily = family;
            _value.FontSize = Math.Max(16, baseSize + 10);
            _value.FontWeight = FontWeights.SemiBold;
            _value.Foreground = CardPalette.Brush(foreground, 0.96);
            _unit.FontFamily = family;
            _unit.FontSize = Math.Max(9, baseSize - 2);
            _unit.Foreground = CardPalette.Brush(foreground, 0.55);

            _delta.FontFamily = family;
            _delta.FontSize = Math.Max(9, baseSize - 3);
            _caption.FontFamily = family;
            _caption.FontSize = Math.Max(9, baseSize - 3);
            _caption.Foreground = CardPalette.Brush(tokens.Muted);
            UpdateTrend();
        }

        void UpdateTrend()
        {
            ThemeTokens tokens = ThemeResolver.Resolve(_style);
            Color foreground = CardPalette.TryColor(_style == null ? null : _style.Foreground,
                Color.FromRgb(0xE6, 0xED, 0xF3));
            string marker;
            Color color;
            switch (_trend)
            {
                case CardMetricTrend.Up: marker = "▲ "; color = tokens.Accent; break;
                case CardMetricTrend.Down: marker = "▼ "; color = tokens.Danger; break;
                case CardMetricTrend.Flat: marker = "■ "; color = tokens.Muted; break;
                default: marker = ""; color = tokens.Muted; break;
            }
            _delta.Text = marker + (_delta.Text ?? "").TrimStart('▲', '▼', '■', ' ');
            _delta.Foreground = CardPalette.Brush(color);
            _delta.Tag = foreground;
        }
    }

    // =====================================================================================
    // CardGauge / CardProgressRing
    // =====================================================================================

    /// <summary>
    /// 单值仪表盘：240° 圆弧 + 中间数值 + 标签。适合 CPU / 温度 / 音量等单值展示。
    /// </summary>
    public sealed class CardGauge : FrameworkElement, ICardFormControl
    {
        StyleSettings _style;
        CardBadgeTone _tone = CardBadgeTone.Accent;
        double _value;
        double _minimum;
        double _maximum = 100;
        string _label = "";
        string _unit = "";
        string _valueFormat = "0";

        public CardGauge()
        {
            MinWidth = 96;
            MinHeight = 64;
            SnapsToDevicePixels = true;
        }

        public double Value { get { return _value; } set { _value = value; InvalidateVisual(); } }
        public double Minimum { get { return _minimum; } set { _minimum = value; InvalidateVisual(); } }
        public double Maximum { get { return _maximum; } set { _maximum = value; InvalidateVisual(); } }
        public string Label { get { return _label; } set { _label = value ?? ""; InvalidateVisual(); } }
        public string Unit { get { return _unit; } set { _unit = value ?? ""; InvalidateVisual(); } }
        public string ValueFormat { get { return _valueFormat; } set { _valueFormat = value ?? "0"; InvalidateVisual(); } }
        public CardBadgeTone Tone { get { return _tone; } set { _tone = value; InvalidateVisual(); } }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 120 : Math.Min(availableSize.Width, 220);
            double height = double.IsInfinity(availableSize.Height) ? 84 : Math.Min(availableSize.Height, 160);
            return new Size(Math.Max(80, width), Math.Max(56, height));
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;

            StyleSettings style = _style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(style);
            Color foreground = CardPalette.TryColor(style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            Color tone = CardDataStyle.ToneColor(tokens, foreground, _tone);
            FontFamily family = CardPalette.ResolveFont(style.FontFamily);

            double thickness = Math.Max(3, Math.Min(w, h) * 0.055);
            double pad = thickness + 4;
            double radius = Math.Max(1, Math.Min(w - pad * 2, h - pad * 2) / 2);
            var center = new Point(w / 2, h * 0.58);
            const double start = 150;
            const double sweep = 240;

            DrawArc(dc, center, radius, start, sweep, thickness, CardPalette.Brush(tokens.Border));
            double ratio = Ratio();
            if (ratio > 0.002)
                DrawArc(dc, center, radius, start, sweep * ratio, thickness, CardPalette.Brush(tone));

            string value = _value.ToString(_valueFormat, CultureInfo.CurrentCulture) + (_unit ?? "");
            double valueSize = Math.Max(12, Math.Min(30, radius * 0.62));
            var valueText = CardDataStyle.Text(value, family, valueSize, FontWeights.SemiBold,
                CardPalette.Brush(foreground, 0.96), this);
            dc.DrawText(valueText, new Point((w - valueText.Width) / 2, center.Y - valueText.Height * 0.62));

            if (!string.IsNullOrEmpty(_label))
            {
                var label = CardDataStyle.Text(_label, family, Math.Max(8, valueSize * 0.42),
                    FontWeights.Normal, CardPalette.Brush(tokens.Muted), this);
                dc.DrawText(label, new Point((w - label.Width) / 2, center.Y + valueText.Height * 0.55));
            }
        }

        double Ratio()
        {
            double span = _maximum - _minimum;
            if (Math.Abs(span) < 1e-9) return 0;
            return Math.Max(0, Math.Min(1, (_value - _minimum) / span));
        }

        static void DrawArc(DrawingContext dc, Point center, double radius, double startDegrees, double sweepDegrees,
                            double thickness, Brush brush)
        {
            if (radius <= 0 || Math.Abs(sweepDegrees) < 0.01) return;

            double start = startDegrees * Math.PI / 180.0;
            double end = (startDegrees + sweepDegrees) * Math.PI / 180.0;
            Point p1 = new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start));
            Point p2 = new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end));

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(p1, false, false);
                ctx.ArcTo(p2, new Size(radius, radius), 0, Math.Abs(sweepDegrees) > 180,
                          sweepDegrees >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                          true, false);
            }
            geometry.Freeze();

            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>
    /// 圆环进度：完整圆环 + 中央百分比 / 数值。适合下载、任务完成度、配额。
    /// </summary>
    public sealed class CardProgressRing : FrameworkElement, ICardFormControl
    {
        StyleSettings _style;
        CardBadgeTone _tone = CardBadgeTone.Accent;
        double _value;
        double _minimum;
        double _maximum = 100;
        string _label = "";
        string _valueFormat = "0'%'";
        bool _showValue = true;
        double _ringThickness = 6;

        public CardProgressRing()
        {
            MinWidth = 64;
            MinHeight = 64;
            SnapsToDevicePixels = true;
        }

        public double Value { get { return _value; } set { _value = value; InvalidateVisual(); } }
        public double Minimum { get { return _minimum; } set { _minimum = value; InvalidateVisual(); } }
        public double Maximum { get { return _maximum; } set { _maximum = value; InvalidateVisual(); } }
        public string Label { get { return _label; } set { _label = value ?? ""; InvalidateVisual(); } }
        public string ValueFormat { get { return _valueFormat; } set { _valueFormat = value ?? "0"; InvalidateVisual(); } }
        public bool ShowValue { get { return _showValue; } set { _showValue = value; InvalidateVisual(); } }
        public double RingThickness { get { return _ringThickness; } set { _ringThickness = Math.Max(2, value); InvalidateVisual(); } }
        public CardBadgeTone Tone { get { return _tone; } set { _tone = value; InvalidateVisual(); } }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 96 : Math.Min(availableSize.Width, 180);
            double height = double.IsInfinity(availableSize.Height) ? 96 : Math.Min(availableSize.Height, 180);
            return new Size(Math.Max(56, width), Math.Max(56, height));
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 4 || h < 4) return;

            StyleSettings style = _style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(style);
            Color foreground = CardPalette.TryColor(style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            Color tone = CardDataStyle.ToneColor(tokens, foreground, _tone);
            FontFamily family = CardPalette.ResolveFont(style.FontFamily);

            double thickness = Math.Max(2, Math.Min(_ringThickness, Math.Min(w, h) * 0.16));
            double radius = Math.Max(1, (Math.Min(w, h) - thickness) / 2 - 3);
            var center = new Point(w / 2, h / 2);
            var track = new Pen(CardPalette.Brush(tokens.Border), thickness);
            track.Freeze();
            dc.DrawEllipse(null, track, center, radius, radius);

            double ratio = Ratio();
            if (ratio > 0.002)
            {
                if (ratio >= 0.999)
                {
                    var full = new Pen(CardPalette.Brush(tone), thickness);
                    full.Freeze();
                    dc.DrawEllipse(null, full, center, radius, radius);
                }
                else
                {
                    DrawArc(dc, center, radius, -90, 360 * ratio, thickness, CardPalette.Brush(tone));
                }
            }

            if (!_showValue) return;

            string value = _value.ToString(_valueFormat, CultureInfo.CurrentCulture);
            double valueSize = Math.Max(10, Math.Min(30, radius * 0.58));
            var text = CardDataStyle.Text(value, family, valueSize, FontWeights.SemiBold,
                CardPalette.Brush(foreground, 0.96), this);
            dc.DrawText(text, new Point((w - text.Width) / 2,
                center.Y - text.Height / 2 - (string.IsNullOrEmpty(_label) ? 0 : text.Height * 0.28)));

            if (!string.IsNullOrEmpty(_label))
            {
                var label = CardDataStyle.Text(_label, family, Math.Max(8, valueSize * 0.38),
                    FontWeights.Normal, CardPalette.Brush(tokens.Muted), this);
                dc.DrawText(label, new Point((w - label.Width) / 2, center.Y + text.Height * 0.42));
            }
        }

        double Ratio()
        {
            double span = _maximum - _minimum;
            if (Math.Abs(span) < 1e-9) return 0;
            return Math.Max(0, Math.Min(1, (_value - _minimum) / span));
        }

        static void DrawArc(DrawingContext dc, Point center, double radius, double startDegrees, double sweepDegrees,
                            double thickness, Brush brush)
        {
            if (radius <= 0 || Math.Abs(sweepDegrees) < 0.01) return;

            double start = startDegrees * Math.PI / 180.0;
            double end = (startDegrees + sweepDegrees) * Math.PI / 180.0;
            var p1 = new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start));
            var p2 = new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end));

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(p1, false, false);
                ctx.ArcTo(p2, new Size(radius, radius), 0, sweepDegrees > 180,
                          sweepDegrees >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                          true, false);
            }
            geometry.Freeze();

            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    // =====================================================================================
    // CardTable
    // =====================================================================================

    /// <summary>表格列：表头、宽度、单元格取值 / 排序键。</summary>
    public sealed class CardTableColumn
    {
        public CardTableColumn(string header = null)
        {
            Header = header ?? "";
            Width = new GridLength(1, GridUnitType.Star);
        }

        public string Header { get; set; }

        /// <summary>列宽；默认 1*。固定宽度用 new GridLength(120)。</summary>
        public GridLength Width { get; set; }

        /// <summary>单元格文本；不设置时，若项是 <see cref="CardTableItem"/> 取对应 Cells。</summary>
        public Func<object, string> Value { get; set; }

        /// <summary>排序键；不设置时按 <see cref="Value"/> 的字符串排序。</summary>
        public Func<object, IComparable> SortKey { get; set; }

        public TextAlignment Alignment { get; set; } = TextAlignment.Left;

        public bool Sortable { get; set; } = true;
    }

    /// <summary>最简单的表格行：按列下标放 Cells。</summary>
    public sealed class CardTableItem
    {
        public CardTableItem(params object[] cells)
        {
            Cells = cells ?? new object[0];
        }

        public object[] Cells { get; private set; }
        public object Tag { get; set; }

        public string Cell(int index)
        {
            if (IndexOutOfRange(index)) return "";
            object value = Cells[index];
            return value == null ? "" : Convert.ToString(value, CultureInfo.CurrentCulture);
        }

        public void SetCell(int index, object value)
        {
            if (IndexOutOfRange(index)) return;
            Cells[index] = value;
        }

        bool IndexOutOfRange(int index)
        {
            return index < 0 || index >= Cells.Length;
        }

        public override string ToString()
        {
            var parts = new string[Cells.Length];
            for (int i = 0; i < Cells.Length; i++) parts[i] = Cell(i);
            return string.Join(" | ", parts);
        }
    }

    /// <summary>
    /// 卡片内表格：表头、虚拟化行、列宽、点击表头排序、单选 / 多选、双击 / 回车激活。
    /// </summary>
    public sealed class CardTable : Grid, ICardFormControl
    {
        readonly Grid _header;
        readonly Border _headerHost;
        readonly CardTableListBox _list;
        readonly Border _emptyHost;
        readonly TextBlock _emptyText;
        readonly ObservableCollection<CardTableColumn> _columns = new ObservableCollection<CardTableColumn>();
        readonly ObservableCollection<object> _rows = new ObservableCollection<object>();
        readonly CardTableRowComparer _comparer;

        ListCollectionView _view;
        IList _externalItems;
        StyleSettings _style;
        double _cornerRadius = 8;
        string _emptyTextValue = "暂无数据";
        int _sortIndex = -1;
        bool _sortDescending;

        public CardTable()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            MinHeight = 80;

            _header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            _headerHost = new Border
            {
                Child = _header,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 4)
            };
            Children.Add(_headerHost);
            SetRow(_headerHost, 0);

            _list = new CardTableListBox
            {
                Table = this,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                SelectionMode = SelectionMode.Single,
                MinHeight = 60
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(_list, true);
            _list.SelectionChanged += (s, e) =>
            {
                var handler = SelectionChanged;
                if (handler != null) handler(this, e);
            };
            _list.MouseDoubleClick += (s, e) => RaiseActivated();
            _list.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                RaiseActivated();
            };
            Children.Add(_list);
            SetRow(_list, 1);

            _emptyText = new TextBlock
            {
                Text = _emptyTextValue,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 12, 16, 12)
            };
            _emptyHost = new Border { Child = _emptyText, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            SetRow(_emptyHost, 1);
            Children.Add(_emptyHost);

            _comparer = new CardTableRowComparer(this);
            _rows.CollectionChanged += (s, e) => { RefreshView(); RefreshEmpty(); };
            _columns.CollectionChanged += (s, e) => RebuildColumns();

            _view = new ListCollectionView(_rows);
            _list.ItemsSource = _view;

            ApplyStyle(null, 8);
        }

        public ObservableCollection<CardTableColumn> Columns { get { return _columns; } }
        public ObservableCollection<object> Rows { get { return _rows; } }

        /// <summary>设置外部数据源；传入集合后 <see cref="Rows"/> 不再控制显示。</summary>
        public IEnumerable ItemsSource
        {
            get { return _view == null ? null : _view.SourceCollection as IEnumerable; }
            set
            {
                if (_view != null)
                {
                    _view.CustomSort = null;
                    _list.ItemsSource = null;
                    _view = null;
                }

                _externalItems = value as IList;
                IEnumerable source = value;
                if (_externalItems == null && value != null)
                {
                    var copy = new List<object>();
                    foreach (object item in value) copy.Add(item);
                    _externalItems = copy;
                    source = copy;
                }

                _view = new ListCollectionView(_externalItems ?? new List<object>());
                _view.CustomSort = _sortIndex >= 0 ? _comparer : null;
                _list.ItemsSource = _view;
                RefreshEmpty();
            }
        }

        public SelectionMode SelectionMode
        {
            get { return _list.SelectionMode; }
            set { _list.SelectionMode = value; }
        }

        public object SelectedItem
        {
            get { return _list.SelectedItem; }
            set { _list.SelectedItem = value; }
        }

        public int SelectedIndex
        {
            get { return _list.SelectedIndex; }
            set { _list.SelectedIndex = value; }
        }

        public IList SelectedItems { get { return _list.SelectedItems; } }
        public bool IsEmpty { get { return _list.Items.Count == 0; } }
        public int Count { get { return _list.Items.Count; } }

        /// <summary>当前视图顺序（排序后）的第 index 项；越界返回 null。</summary>
        public object GetItem(int index)
        {
            return index >= 0 && index < _list.Items.Count ? _list.Items[index] : null;
        }
        public int SortColumnIndex { get { return _sortIndex; } }
        public bool SortDescending { get { return _sortDescending; } }

        public string EmptyText
        {
            get { return _emptyTextValue; }
            set
            {
                _emptyTextValue = value ?? "";
                _emptyText.Text = _emptyTextValue;
            }
        }

        public event SelectionChangedEventHandler SelectionChanged;
        public event Action<object> ItemActivated;
        public event Action<int, bool> SortChanged;

        public CardTableColumn AddColumn(string header, Func<object, string> value = null,
                                         Func<object, IComparable> sortKey = null, GridLength? width = null,
                                         TextAlignment alignment = TextAlignment.Left)
        {
            var column = new CardTableColumn(header)
            {
                Value = value,
                SortKey = sortKey,
                Alignment = alignment
            };
            if (width.HasValue) column.Width = width.Value;
            _columns.Add(column);
            return column;
        }

        public CardTableItem AddRow(params object[] cells)
        {
            var item = new CardTableItem(cells);
            _rows.Add(item);
            return item;
        }

        public void ClearRows()
        {
            if (_externalItems != null) _externalItems.Clear();
            else _rows.Clear();
            RefreshView();
            RefreshEmpty();
        }

        public void SetEmptyText(string text)
        {
            EmptyText = text;
        }

        public void ScrollToEnd()
        {
            if (_list.Items.Count == 0) return;
            _list.ScrollIntoView(_list.Items[_list.Items.Count - 1]);
        }

        public void SortBy(int columnIndex, bool descending)
        {
            if (columnIndex < 0 || columnIndex >= _columns.Count) return;
            _sortIndex = columnIndex;
            _sortDescending = descending;
            if (_view != null)
            {
                _view.CustomSort = _comparer;
                _view.Refresh();
            }
            RebuildHeader();
            var handler = SortChanged;
            if (handler != null) handler(columnIndex, descending);
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _cornerRadius = cornerRadius;

            ThemeTokens tokens = ThemeResolver.Resolve(_style);
            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontFamily family = CardPalette.ResolveFont(_style.FontFamily);
            double size = Math.Max(9, _style.FontSize - 1);

            _headerHost.Background = CardPalette.Brush(tokens.Panel);
            _headerHost.BorderBrush = CardPalette.Brush(tokens.Border);

            _list.FontFamily = family;
            _list.FontSize = size;
            _list.Foreground = new SolidColorBrush(foreground);
            _list.ItemContainerStyle = CardDataStyle.TableRowStyle(tokens, foreground, _cornerRadius);

            _emptyText.FontFamily = family;
            _emptyText.FontSize = size;
            _emptyText.Foreground = CardPalette.Brush(tokens.Muted);
            _emptyHost.BorderThickness = new Thickness(1);
            _emptyHost.BorderBrush = CardPalette.Brush(tokens.Border);
            _emptyHost.CornerRadius = new CornerRadius(6);

            CliScrollBars.Apply(this, tokens.Scrollbar, tokens.ScrollbarHot);
            RebuildColumns();
        }

        internal string CellText(object item, int index)
        {
            if (index < 0 || index >= _columns.Count) return "";
            CardTableColumn column = _columns[index];
            if (column.Value != null) return column.Value(item) ?? "";
            var row = item as CardTableItem;
            return row == null ? "" : row.Cell(index);
        }

        internal TextAlignment CellAlignment(int index)
        {
            return index >= 0 && index < _columns.Count ? _columns[index].Alignment : TextAlignment.Left;
        }

        internal GridLength ColumnWidth(int index)
        {
            return index >= 0 && index < _columns.Count ? _columns[index].Width : new GridLength(1, GridUnitType.Star);
        }

        internal int ColumnCount { get { return _columns.Count; } }

        internal IComparable SortKeyOf(object item, int index)
        {
            if (index < 0 || index >= _columns.Count) return null;
            CardTableColumn column = _columns[index];
            if (column.SortKey != null) return column.SortKey(item);
            return CellText(item, index);
        }

        void RebuildColumns()
        {
            RebuildHeader();
            _list.Items.Refresh();
            _view.Refresh();
        }

        void RebuildHeader()
        {
            _header.Children.Clear();
            _header.ColumnDefinitions.Clear();

            for (int i = 0; i < _columns.Count; i++)
            {
                _header.ColumnDefinitions.Add(new ColumnDefinition { Width = _columns[i].Width });
                CardTableColumn column = _columns[i];
                string label = column.Header ?? "";
                if (_sortIndex == i) label += _sortDescending ? "  ▼" : "  ▲";

                var text = new TextBlock
                {
                    Text = label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = Alignment(column.Alignment),
                    FontFamily = CardPalette.ResolveFont(_style == null ? null : _style.FontFamily),
                    FontSize = Math.Max(9, (_style == null ? 13 : _style.FontSize) - 2),
                    Foreground = CardPalette.Brush(ThemeResolver.Resolve(_style).Muted)
                };
                var host = new Border
                {
                    Child = text,
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = Brushes.Transparent,
                    Cursor = column.Sortable ? Cursors.Hand : Cursors.Arrow
                };
                int index = i;
                if (column.Sortable)
                {
                    host.MouseLeftButtonUp += (s, e) =>
                    {
                        bool desc = _sortIndex == index && !_sortDescending;
                        SortBy(index, desc);
                    };
                }
                Grid.SetColumn(host, i);
                _header.Children.Add(host);
            }
        }

        static HorizontalAlignment Alignment(TextAlignment alignment)
        {
            switch (alignment)
            {
                case TextAlignment.Center: return HorizontalAlignment.Center;
                case TextAlignment.Right: return HorizontalAlignment.Right;
                default: return HorizontalAlignment.Left;
            }
        }

        void RefreshView()
        {
            if (_view == null) return;
            _view.Refresh();
        }

        void RefreshEmpty()
        {
            int count = _view == null ? 0 : _view.Count;
            _emptyHost.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_view != null && count == 0) _emptyText.Text = _emptyTextValue;
        }

        void RaiseActivated()
        {
            object item = _list.SelectedItem;
            if (item == null) return;
            var handler = ItemActivated;
            if (handler != null) handler(item);
        }

        sealed class CardTableListBox : ListBox
        {
            public CardTable Table;

            protected override DependencyObject GetContainerForItemOverride()
            {
                return new CardTableRow();
            }

            protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
            {
                base.PrepareContainerForItemOverride(element, item);
                var row = element as CardTableRow;
                if (row != null) row.Build(Table, item);
            }
        }

        sealed class CardTableRow : ListBoxItem
        {
            public void Build(CardTable table, object item)
            {
                if (table == null) return;

                var grid = new Grid();
                for (int i = 0; i < table.ColumnCount; i++)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = table.ColumnWidth(i) });
                    var cell = new TextBlock
                    {
                        Text = table.CellText(item, i),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = table.CellAlignment(i) == TextAlignment.Center
                            ? HorizontalAlignment.Center
                            : table.CellAlignment(i) == TextAlignment.Right
                                ? HorizontalAlignment.Right
                                : HorizontalAlignment.Left
                    };
                    Grid.SetColumn(cell, i);
                    grid.Children.Add(cell);
                }
                Content = grid;
            }
        }

        sealed class CardTableRowComparer : IComparer
        {
            readonly CardTable _table;

            public CardTableRowComparer(CardTable table) { _table = table; }

            public int Compare(object x, object y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                IComparable left = _table.SortKeyOf(x, _table.SortColumnIndex);
                IComparable right = _table.SortKeyOf(y, _table.SortColumnIndex);
                int result;
                if (left == null) result = right == null ? 0 : -1;
                else if (right == null) result = 1;
                else result = left.CompareTo(right);

                return _table.SortDescending ? -result : result;
            }
        }
    }

    /// <summary>数据控件共用的颜色 / 文本工具。</summary>
    internal static class CardDataStyle
    {
        public static Color ToneColor(ThemeTokens tokens, Color foreground, CardBadgeTone tone)
        {
            switch (tone)
            {
                case CardBadgeTone.Info:
                case CardBadgeTone.Success:
                case CardBadgeTone.Accent:
                    return tokens.Accent;
                case CardBadgeTone.Warning:
                    return foreground;
                case CardBadgeTone.Danger:
                    return tokens.Danger;
                default:
                    return tokens.Muted;
            }
        }

        public static FormattedText Text(string text, FontFamily family, double size, FontWeight weight,
                                        Brush brush, Visual owner)
        {
            double pixelsPerDip = 1.0;
            try { pixelsPerDip = VisualTreeHelper.GetDpi(owner).PixelsPerDip; } catch { }

            return new FormattedText(
                text ?? "",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(family ?? new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
                             weight, FontStretches.Normal),
                Math.Max(1, size),
                brush,
                pixelsPerDip);
        }

        public static Style TableRowStyle(ThemeTokens tokens, Color foreground, double cornerRadius)
        {
            const string template =
                "<ControlTemplate TargetType=\"ListBoxItem\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Border Background=\"{TemplateBinding Background}\" Padding=\"{TemplateBinding Padding}\" CornerRadius=\"3\">" +
                "<ContentPresenter HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" VerticalAlignment=\"Center\"/>" +
                "</Border></ControlTemplate>";

            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 1)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(foreground)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(template)));

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, CardPalette.Brush(foreground, 0.06)));
            style.Triggers.Add(hover);

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, CardPalette.Brush(tokens.Accent, 0.16)));
            style.Triggers.Add(selected);

            return style;
        }
    }
}
