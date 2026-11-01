using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 卡片里的表单控件统一换肤入口。插件实现 <c>ICardStyleAware</c> 时，
    /// 对每个表单控件调用 <see cref="ApplyStyle"/> 即可跟随主题变化。
    /// </summary>
    public interface ICardFormControl
    {
        void ApplyStyle(StyleSettings style, double cornerRadius);
    }

    /// <summary>
    /// 主题化滑块：标题 + 当前值 + 圆角滑轨 + 圆形拖动块。
    /// 用于音量 / 透明度 / 阈值等数值设置。
    /// </summary>
    public sealed class CardSlider : Grid, ICardFormControl
    {
        readonly TextBlock _label;
        readonly TextBlock _valueText;
        readonly Slider _slider;

        StyleSettings _style;
        ThemeTokens _tokens;
        Color _foreground;
        bool _updating;

        public CardSlider(string label = null, double minimum = 0, double maximum = 100, double value = 0)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            _valueText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(_label);
            SetColumn(_valueText, 1);
            header.Children.Add(_valueText);
            Children.Add(header);

            _slider = new Slider
            {
                Minimum = minimum,
                Maximum = Math.Max(minimum, maximum),
                SmallChange = 1,
                LargeChange = Math.Max(1, (maximum - minimum) / 10.0),
                IsMoveToPointEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };
            _slider.ValueChanged += (s, e) =>
            {
                UpdateValueText();
                if (!_updating) RaiseValueChanged(_slider.Value);
            };
            SetRow(_slider, 1);
            Children.Add(_slider);

            Step = 1;
            Label = label;
            Value = value;
            ApplyStyle(null, 8);
        }

        public event Action<double> ValueChanged;

        public Slider InnerSlider { get { return _slider; } }

        public string Label
        {
            get { return _label.Text ?? ""; }
            set { _label.Text = value ?? ""; }
        }

        public double Minimum
        {
            get { return _slider.Minimum; }
            set
            {
                _slider.Minimum = value;
                if (_slider.Maximum < value) _slider.Maximum = value;
                RefreshValueText();
            }
        }

        public double Maximum
        {
            get { return _slider.Maximum; }
            set
            {
                _slider.Maximum = Math.Max(_slider.Minimum, value);
                RefreshValueText();
            }
        }

        public double Value
        {
            get { return _slider.Value; }
            set
            {
                _updating = true;
                try { _slider.Value = Math.Max(_slider.Minimum, Math.Min(_slider.Maximum, value)); }
                finally { _updating = false; }
                RefreshValueText();
            }
        }

        /// <summary>数值显示格式，默认 0.##。</summary>
        public string ValueFormat { get; set; } = "0.##";

        double _step = 1;

        /// <summary>拖动步进；0 = 连续拖动。</summary>
        public double Step
        {
            get { return _step; }
            set
            {
                _step = Math.Max(0, value);
                _slider.SmallChange = _step > 0 ? _step : 1;
                _slider.TickFrequency = _step;
                _slider.IsSnapToTickEnabled = _step > 0;
            }
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _tokens = ThemeResolver.Resolve(_style);
            _foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            FontFamily family = CardPalette.ResolveFont(_style.FontFamily);
            _label.FontFamily = family;
            _label.FontSize = Math.Max(10, _style.FontSize - 1);
            _label.Foreground = CardPalette.Brush(_foreground, 0.72);
            _valueText.FontFamily = family;
            _valueText.FontSize = Math.Max(10, _style.FontSize - 1);
            _valueText.Foreground = CardPalette.Brush(_foreground, 0.92);

            _slider.Template = BuildSliderTemplate(
                CardPalette.Brush(_tokens.Muted, 0.18),
                CardPalette.Brush(_tokens.Accent),
                CardPalette.Brush(_foreground, 0.18));
            _slider.MinHeight = 22;
            RefreshValueText();
        }

        void RefreshValueText()
        {
            UpdateValueText();
        }

        void UpdateValueText()
        {
            _valueText.Text = _slider.Value.ToString(ValueFormat, CultureInfo.CurrentCulture);
        }

        void RaiseValueChanged(double value)
        {
            Action<double> handler = ValueChanged;
            if (handler != null) handler(value);
        }

        static ControlTemplate BuildSliderTemplate(Brush track, Brush accent, Brush border)
        {
            string xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type Slider}\">" +
                "<Grid Height=\"22\" Background=\"Transparent\">" +
                "<Border Height=\"4\" CornerRadius=\"2\" Background=\"" + track + "\" VerticalAlignment=\"Center\"/>" +
                "<Track x:Name=\"PART_Track\" VerticalAlignment=\"Center\">" +
                "<Track.DecreaseRepeatButton><RepeatButton IsTabStop=\"False\" Focusable=\"False\">" +
                "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\">" +
                "<Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb Width=\"14\" Height=\"14\">" +
                "<Thumb.Template><ControlTemplate TargetType=\"{x:Type Thumb}\">" +
                "<Border CornerRadius=\"7\" Background=\"" + accent + "\" BorderThickness=\"2\" BorderBrush=\"" + border + "\"/>" +
                "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton><RepeatButton IsTabStop=\"False\" Focusable=\"False\">" +
                "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\">" +
                "<Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton></Track.IncreaseRepeatButton>" +
                "</Track></Grid></ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }
    }

    /// <summary>主题化开关：方形勾选框 + 文案；继承 CheckBox 的 Checked/Unchecked。</summary>
    public sealed class CardToggle : CheckBox, ICardFormControl
    {
        public CardToggle(string text = null)
        {
            Content = text ?? "";
            Cursor = Cursors.Hand;
            FocusVisualStyle = null;
            VerticalContentAlignment = VerticalAlignment.Center;
            ApplyStyle(null, 8);
        }

        public string Text
        {
            get { return Content as string ?? ""; }
            set { Content = value ?? ""; }
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(s);
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            FontFamily = CardPalette.ResolveFont(s.FontFamily);
            FontSize = Math.Max(10, s.FontSize - 1);
            Foreground = CardPalette.Brush(foreground, 0.92);
            Template = BuildToggleTemplate(
                CardPalette.Brush(foreground, 0.24),
                CardPalette.Brush(tokens.Accent),
                CardPalette.Brush(tokens.Accent, 0.16),
                CardPalette.Brush(foreground, 0.10));
        }

        static ControlTemplate BuildToggleTemplate(Brush border, Brush accent, Brush accentBg, Brush hover)
        {
            string xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type CheckBox}\">" +
                "<StackPanel Orientation=\"Horizontal\" Background=\"Transparent\">" +
                "<Border x:Name=\"Box\" Width=\"18\" Height=\"18\" CornerRadius=\"4\" BorderThickness=\"1\" " +
                "BorderBrush=\"" + border + "\" Background=\"Transparent\" VerticalAlignment=\"Center\">" +
                "<TextBlock x:Name=\"Check\" Text=\"✓\" FontSize=\"12\" Foreground=\"" + accent + "\" " +
                "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Visibility=\"Collapsed\"/>" +
                "</Border>" +
                "<ContentPresenter Margin=\"8,0,0,0\" VerticalAlignment=\"Center\"/>" +
                "</StackPanel>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property=\"IsChecked\" Value=\"True\">" +
                "<Setter TargetName=\"Box\" Property=\"BorderBrush\" Value=\"" + accent + "\"/>" +
                "<Setter TargetName=\"Box\" Property=\"Background\" Value=\"" + accentBg + "\"/>" +
                "<Setter TargetName=\"Check\" Property=\"Visibility\" Value=\"Visible\"/>" +
                "</Trigger>" +
                "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                "<Setter TargetName=\"Box\" Property=\"Background\" Value=\"" + hover + "\"/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }
    }

    /// <summary>主题化下拉选择：标题 + ComboBox。</summary>
    public sealed class CardSelect : Grid, ICardFormControl
    {
        readonly TextBlock _label;
        readonly ComboBox _combo;
        bool _updating;

        public CardSelect(string label = null, IEnumerable<string> items = null)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _label = new TextBlock { Margin = new Thickness(0, 0, 0, 3) };
            Children.Add(_label);
            SetRow(_label, 0);

            _combo = new ComboBox
            {
                MinHeight = 30,
                Padding = new Thickness(6, 3, 6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _combo.SelectionChanged += (s, e) =>
            {
                if (_updating) return;
                Action<string> handler = SelectionChanged;
                if (handler != null) handler(SelectedItem);
            };
            SetRow(_combo, 1);
            Children.Add(_combo);

            Label = label;
            if (items != null) ItemsSource = items;
            ApplyStyle(null, 8);
        }

        public event Action<string> SelectionChanged;

        public string Label
        {
            get { return _label.Text ?? ""; }
            set { _label.Text = value ?? ""; _label.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public IEnumerable<string> ItemsSource
        {
            get { return _combo.ItemsSource as IEnumerable<string>; }
            set { _combo.ItemsSource = value; }
        }

        public string SelectedItem
        {
            get { return _combo.SelectedItem as string; }
            set
            {
                _updating = true;
                try { _combo.SelectedItem = value; }
                finally { _updating = false; }
            }
        }

        public int SelectedIndex
        {
            get { return _combo.SelectedIndex; }
            set { _combo.SelectedIndex = value; }
        }

        public ComboBox InnerComboBox { get { return _combo; } }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(s);
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontFamily family = CardPalette.ResolveFont(s.FontFamily);

            _label.FontFamily = family;
            _label.FontSize = Math.Max(10, s.FontSize - 1);
            _label.Foreground = CardPalette.Brush(foreground, 0.72);

            _combo.FontFamily = family;
            _combo.FontSize = Math.Max(10, s.FontSize - 1);
            _combo.Foreground = CardPalette.Brush(foreground, 0.92);
            _combo.Background = CardPalette.Brush(foreground, 0.06);
            _combo.BorderBrush = CardPalette.Brush(foreground, 0.22);
            _combo.BorderThickness = new Thickness(1);
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, CardPalette.Brush(foreground, 0.92)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
            _combo.ItemContainerStyle = itemStyle;
            _combo.Template = BuildComboTemplate(
                CardPalette.Brush(foreground, 0.06),
                CardPalette.Brush(foreground, 0.22),
                CardPalette.Brush(tokens.Panel),
                CardPalette.Brush(foreground, 0.92),
                CardPalette.Brush(tokens.Accent));
        }

        static ControlTemplate BuildComboTemplate(Brush background, Brush border, Brush panel, Brush foreground, Brush accent)
        {
            string xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ComboBox}\">" +
                "<Grid>" +
                "<ToggleButton x:Name=\"Toggle\" Focusable=\"False\" ClickMode=\"Press\" " +
                "IsChecked=\"{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}\">" +
                "<ToggleButton.Template><ControlTemplate TargetType=\"{x:Type ToggleButton}\">" +
                "<Border Background=\"" + background + "\" BorderBrush=\"" + border + "\" BorderThickness=\"1\" CornerRadius=\"6\">" +
                "<Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width=\"22\"/></Grid.ColumnDefinitions>" +
                "<ContentPresenter Margin=\"8,0,0,0\" VerticalAlignment=\"Center\"/>" +
                "<TextBlock Grid.Column=\"1\" Text=\"▾\" Foreground=\"" + foreground + "\" " +
                "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Grid>" +
                "</Border></ControlTemplate></ToggleButton.Template>" +
                "</ToggleButton>" +
                "<ContentPresenter x:Name=\"ContentSite\" Margin=\"8,0,30,0\" IsHitTestVisible=\"False\" " +
                "VerticalAlignment=\"Center\" HorizontalAlignment=\"Left\" Content=\"{TemplateBinding SelectionBoxItem}\" " +
                "ContentTemplate=\"{TemplateBinding SelectionBoxItemTemplate}\"/>" +
                "<Popup x:Name=\"PART_Popup\" Placement=\"Bottom\" IsOpen=\"{TemplateBinding IsDropDownOpen}\" " +
                "StaysOpen=\"False\" AllowsTransparency=\"True\" Focusable=\"False\" PopupAnimation=\"Fade\">" +
                "<Border Background=\"" + panel + "\" BorderBrush=\"" + border + "\" BorderThickness=\"1\" " +
                "CornerRadius=\"6\" MinWidth=\"{TemplateBinding ActualWidth}\" MaxHeight=\"260\">" +
                "<ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property=\"IsKeyboardFocusWithin\" Value=\"True\"><Setter TargetName=\"Toggle\" Property=\"Focusable\" Value=\"True\"/></Trigger>" +
                "<Trigger Property=\"IsDropDownOpen\" Value=\"True\"><Setter TargetName=\"Toggle\" Property=\"Focusable\" Value=\"False\"/></Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }
    }

    /// <summary>主题化数字输入：标题 + 文本输入，失焦 / 回车提交并夹紧范围。</summary>
    public sealed class CardNumberInput : Grid, ICardFormControl
    {
        readonly TextBlock _label;
        readonly CardTextBox _box;

        StyleSettings _style;
        ThemeTokens _tokens;
        Color _foreground;

        public CardNumberInput(string label = null, double value = 0, double minimum = 0, double maximum = 100)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _label = new TextBlock { Margin = new Thickness(0, 0, 0, 3) };
            Children.Add(_label);
            SetRow(_label, 0);

            _box = new CardTextBox
            {
                MinHeight = 30,
                Padding = new Thickness(8, 3, 8, 3),
                TextAlignment = TextAlignment.Right
            };
            _box.LostKeyboardFocus += (s, e) => Commit();
            _box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Commit();
            };
            SetRow(_box, 1);
            Children.Add(_box);

            Minimum = minimum;
            Maximum = maximum;
            Label = label;
            Value = value;
            ApplyStyle(null, 8);
        }

        public event Action<double> ValueChanged;

        public string Label
        {
            get { return _label.Text ?? ""; }
            set { _label.Text = value ?? ""; _label.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public double Minimum { get; set; }
        public double Maximum { get; set; }

        public double Value
        {
            get { return Parse(_box.Text); }
            set { _box.Text = Math.Max(Minimum, Math.Min(Maximum, value)).ToString(ValueFormat, CultureInfo.CurrentCulture); }
        }

        public string ValueFormat { get; set; } = "0.##";

        public CardTextBox InnerTextBox { get { return _box; } }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _tokens = ThemeResolver.Resolve(_style);
            _foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontFamily family = CardPalette.ResolveFont(_style.FontFamily);

            _label.FontFamily = family;
            _label.FontSize = Math.Max(10, _style.FontSize - 1);
            _label.Foreground = CardPalette.Brush(_foreground, 0.72);
            _box.ApplyStyle(_style, cornerRadius);
        }

        void Commit()
        {
            double value = Parse(_box.Text);
            value = Math.Max(Minimum, Math.Min(Maximum, value));
            _box.Text = value.ToString(ValueFormat, CultureInfo.CurrentCulture);

            Action<double> handler = ValueChanged;
            if (handler != null) handler(value);
        }

        double Parse(string text)
        {
            double value;
            if (!double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
                !double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                value = Minimum;
            return Math.Max(Minimum, Math.Min(Maximum, value));
        }
    }

    /// <summary>主题化颜色选择：标题 + 色块预览 + #AARRGGBB 输入。</summary>
    public sealed class CardColorPicker : Grid, ICardFormControl
    {
        readonly TextBlock _label;
        readonly Border _preview;
        readonly CardTextBox _box;
        bool _updating;
        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        Color _danger = Color.FromRgb(0xD1, 0x43, 0x43);

        public CardColorPicker(string label = null, string value = "#FF2F6FEB")
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Children.Add(_label);

            _preview = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            SetColumn(_preview, 1);
            Children.Add(_preview);

            _box = new CardTextBox { MinHeight = 30, Padding = new Thickness(8, 3, 8, 3) };
            _box.TextChanged += (s, e) => UpdatePreview();
            SetColumn(_box, 2);
            Children.Add(_box);

            Label = label;
            Value = value;
            ApplyStyle(null, 8);
        }

        public event Action<string> ValueChanged;

        public string Label
        {
            get { return _label.Text ?? ""; }
            set { _label.Text = value ?? ""; }
        }

        public string Value
        {
            get { return _box.Text ?? ""; }
            set
            {
                _updating = true;
                try { _box.Text = value ?? ""; }
                finally { _updating = false; }
                UpdatePreview();
            }
        }

        public CardTextBox InnerTextBox { get { return _box; } }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(s);
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontFamily family = CardPalette.ResolveFont(s.FontFamily);

            _label.FontFamily = family;
            _label.FontSize = Math.Max(10, s.FontSize - 1);
            _label.Foreground = CardPalette.Brush(foreground, 0.72);
            _foreground = foreground;
            _danger = tokens.Danger;
            _preview.BorderBrush = CardPalette.Brush(foreground, 0.24);
            _box.ApplyStyle(s, cornerRadius);
            UpdatePreview();
        }

        void UpdatePreview()
        {
            Color color;
            if (TryParseColor(_box.Text, out color))
            {
                _preview.Background = new SolidColorBrush(color);
                _box.BorderBrush = CardPalette.Brush(_foreground, 0.24);
                if (!_updating) RaiseValueChanged(_box.Text ?? "");
            }
            else
            {
                _preview.Background = Brushes.Transparent;
                _box.BorderBrush = CardPalette.Brush(_danger);
            }
        }

        void RaiseValueChanged(string value)
        {
            Action<string> handler = ValueChanged;
            if (handler != null) handler(value);
        }

        static bool TryParseColor(string text, out Color color)
        {
            byte a, r, g, b;
            if (!ColorCode.TryParse(text, out a, out r, out g, out b))
            {
                color = Colors.Transparent;
                return false;
            }
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
    }

    /// <summary>主题化分段选择：一排可点按钮，始终只有一个选中。</summary>
    public sealed class CardSegmentedControl : Grid, ICardFormControl
    {
        readonly TextBlock _label;
        readonly StackPanel _items;
        readonly List<CardButton> _buttons = new List<CardButton>();
        readonly List<string> _values = new List<string>();
        int _selectedIndex = -1;
        StyleSettings _style;
        double _cornerRadius = 8;

        public CardSegmentedControl(string label = null, params string[] options)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _label = new TextBlock { Margin = new Thickness(0, 0, 0, 3) };
            Children.Add(_label);

            _items = new StackPanel { Orientation = Orientation.Horizontal };
            SetRow(_items, 1);
            Children.Add(_items);

            Label = label;
            if (options != null) SetItems(options);
            ApplyStyle(null, 8);
        }

        public event Action<int, string> SelectionChanged;

        public string Label
        {
            get { return _label.Text ?? ""; }
            set { _label.Text = value ?? ""; _label.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public IList<string> Items { get { return _values.AsReadOnly(); } }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                if (value < -1 || value >= _buttons.Count) return;
                _selectedIndex = value;
                UpdateSelection();
            }
        }

        public string SelectedItem
        {
            get { return _selectedIndex >= 0 && _selectedIndex < _values.Count ? _values[_selectedIndex] : null; }
        }

        public void SetItems(IEnumerable<string> options)
        {
            _values.Clear();
            _buttons.Clear();
            _items.Children.Clear();

            if (options != null)
            {
                foreach (string option in options)
                {
                    string value = option ?? "";
                    int index = _values.Count;
                    _values.Add(value);

                    var button = new CardButton(value, CardButtonTone.Normal);
                    button.Margin = new Thickness(0, 0, 6, 0);
                    button.Click += (s, e) => Select(index, true);
                    _buttons.Add(button);
                    _items.Children.Add(button);
                }
            }

            if (_values.Count > 0) Select(0, false);
            ApplyStyleToButtons();
        }

        public void Select(int index, bool notify)
        {
            if (index < 0 || index >= _values.Count) return;
            _selectedIndex = index;
            UpdateSelection();
            if (notify)
            {
                Action<int, string> handler = SelectionChanged;
                if (handler != null) handler(index, _values[index]);
            }
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(s);
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontFamily family = CardPalette.ResolveFont(s.FontFamily);

            _style = s;
            _cornerRadius = cornerRadius;
            _label.FontFamily = family;
            _label.FontSize = Math.Max(10, s.FontSize - 1);
            _label.Foreground = CardPalette.Brush(foreground, 0.72);
            ApplyStyleToButtons();
        }

        void ApplyStyleToButtons()
        {
            StyleSettings style = _style ?? new StyleSettings();
            foreach (CardButton button in _buttons)
                button.ApplyStyle(style, _cornerRadius);
            UpdateSelection();
        }

        void UpdateSelection()
        {
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].SetActive(i == _selectedIndex);
        }
    }

    /// <summary>表单一行：左侧标题 + 右侧内容。</summary>
    public sealed class CardFormRow : Grid, ICardFormControl
    {
        readonly TextBlock _label;
        readonly FrameworkElement _content;

        public CardFormRow(string label, FrameworkElement content)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Margin = new Thickness(0, 0, 0, 8);

            _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Children.Add(_label);
            _content = content;
            if (_content != null)
            {
                SetColumn(_content, 1);
                Children.Add(_content);
            }
            Label = label;
            ApplyStyle(null, 8);
        }

        public string Label
        {
            get { return _label.Text ?? ""; }
            set { _label.Text = value ?? ""; }
        }

        public FrameworkElement ContentElement { get { return _content; } }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            _label.FontFamily = CardPalette.ResolveFont(s.FontFamily);
            _label.FontSize = Math.Max(10, s.FontSize - 1);
            _label.Foreground = CardPalette.Brush(foreground, 0.72);

            var form = _content as ICardFormControl;
            if (form != null) form.ApplyStyle(s, cornerRadius);
        }
    }

    /// <summary>
    /// 声明式卡片设置表单：插件只需 AddRow / AddSlider / AddToggle 等，
    /// 主题变化时对整张表调用一次 ApplyStyle 即可。
    /// </summary>
    public sealed class CardSettingsForm : StackPanel, ICardFormControl
    {
        readonly List<CardFormRow> _rows = new List<CardFormRow>();
        readonly List<TextBlock> _descriptions = new List<TextBlock>();
        readonly List<Border> _dividers = new List<Border>();

        public CardSettingsForm()
        {
            Orientation = Orientation.Vertical;
        }

        public CardFormRow AddRow(string label, FrameworkElement content)
        {
            var row = new CardFormRow(label, content);
            _rows.Add(row);
            Children.Add(row);
            return row;
        }

        public TextBlock AddDescription(string text)
        {
            var block = new TextBlock
            {
                Text = text ?? "",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _descriptions.Add(block);
            Children.Add(block);
            return block;
        }

        public void AddDivider()
        {
            var line = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4, 0, 8)
            };
            _dividers.Add(line);
            Children.Add(line);
        }

        public CardSlider AddSlider(string label, double minimum, double maximum, double value)
        {
            var slider = new CardSlider(null, minimum, maximum, value);
            AddRow(label, slider);
            return slider;
        }

        public CardToggle AddToggle(string label, bool value = false)
        {
            var toggle = new CardToggle(null) { IsChecked = value };
            AddRow(label, toggle);
            return toggle;
        }

        public CardSelect AddSelect(string label, IEnumerable<string> options, string selected = null)
        {
            var select = new CardSelect(null, options);
            if (selected != null) select.SelectedItem = selected;
            AddRow(label, select);
            return select;
        }

        public CardNumberInput AddNumber(string label, double value, double minimum, double maximum)
        {
            var number = new CardNumberInput(null, value, minimum, maximum);
            AddRow(label, number);
            return number;
        }

        public CardColorPicker AddColor(string label, string value)
        {
            var color = new CardColorPicker(null, value);
            AddRow(label, color);
            return color;
        }

        public CardSegmentedControl AddSegmented(string label, params string[] options)
        {
            var segment = new CardSegmentedControl(null, options);
            AddRow(label, segment);
            return segment;
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontFamily family = CardPalette.ResolveFont(s.FontFamily);

            foreach (CardFormRow row in _rows) row.ApplyStyle(s, cornerRadius);
            foreach (TextBlock block in _descriptions)
            {
                block.FontFamily = family;
                block.FontSize = Math.Max(9, s.FontSize - 2);
                block.Foreground = CardPalette.Brush(foreground, 0.45);
            }
            foreach (Border line in _dividers)
                line.Background = CardPalette.Brush(foreground, 0.10);
        }
    }
}
