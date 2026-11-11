using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 卡片顶部的一行：标题 + 徽标 + 右侧动作按钮。
    /// 中转站（标题 / 计数 / 清空）、媒体卡（文件名 / 操作）、画板（撤销 / 清空 / 保存）共用。
    /// </summary>
    public sealed class CardHeader : Grid
    {
        readonly TextBlock _title;
        readonly TextBlock _code;
        readonly TextBlock _badge;
        string _badgeValue = "";
        bool _badgeUpper;

        public CardHeader(string title = null)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _code = new TextBlock
            {
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            _title = new TextBlock
            {
                Text = title ?? "",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _badge = new TextBlock
            {
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(_code);
            left.Children.Add(_title);
            left.Children.Add(_badge);

            Actions = new CardToolbar
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Children.Add(left);
            Children.Add(Actions);
            SetColumn(Actions, 1);
        }

        public CardToolbar Actions { get; private set; }

        public string Title
        {
            get { return _title.Text ?? ""; }
            set { _title.Text = value ?? ""; }
        }

        /// <summary>
        /// M5 铭牌编号（两位，例如 "03"）。空 = 不显示；由调用方在启用
        /// <see cref="StyleSettings.IndustrialHeader"/> 时按卡片顺序填入。
        /// </summary>
        public string Code
        {
            get { return _code.Text ?? ""; }
            set
            {
                _code.Text = value ?? "";
                _code.Visibility = string.IsNullOrEmpty(_code.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>标题右侧的小字（“12 个文件”“只读”）；null / 空串隐藏。</summary>
        public string Badge
        {
            get { return _badgeValue; }
            set
            {
                _badgeValue = value ?? "";
                UpdateBadgeText();
            }
        }

        /// <summary>M5：只有启用铭牌时才把徽标转大写，默认保持原样。</summary>
        void UpdateBadgeText()
        {
            _badge.Text = _badgeUpper ? _badgeValue.ToUpperInvariant() : _badgeValue;
            _badge.Visibility = string.IsNullOrEmpty(_badge.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(s);
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            _title.FontFamily = CardPalette.ResolveFont(s.FontFamily);
            _title.FontSize = Math.Max(11, s.FontSize + 1);
            _title.Foreground = CardPalette.Brush(foreground, 0.95);

            FontFamily mono = new FontFamily(tokens.MonoFont);
            _code.FontFamily = mono;
            _code.FontSize = Math.Max(10, s.FontSize);
            _code.FontWeight = FontWeights.SemiBold;
            _code.Foreground = CardPalette.Brush(tokens.Muted);
            _code.Visibility = string.IsNullOrEmpty(_code.Text) ? Visibility.Collapsed : Visibility.Visible;

            _badge.FontFamily = s.IndustrialHeader ? mono : _title.FontFamily;
            _badge.FontSize = Math.Max(9, s.FontSize - 1);
            _badge.Foreground = CardPalette.Brush(tokens.Muted);
            _badgeUpper = s.IndustrialHeader;
            UpdateBadgeText();
            // M4/M5：徽标里常见计数（2/5），数字列对齐可以避免刷新时左右跳。
            Typography.SetNumeralAlignment(_badge, FontNumeralAlignment.Tabular);

            Actions.ApplyStyle(s, cornerRadius);
        }
    }

    /// <summary>
    /// 一排卡片动作按钮：统一间距、统一换肤；文字按钮即可，需要图标时由调用方自己换 Content。
    /// </summary>
    public sealed class CardToolbar : StackPanel
    {
        StyleSettings _style;
        double _corner;

        public CardToolbar()
        {
            Orientation = Orientation.Horizontal;
        }

        public CardButton Add(string text, Action click, CardButtonTone tone = CardButtonTone.Normal, string tooltip = null)
        {
            var button = new CardButton(text, tone) { Margin = new Thickness(6, 0, 0, 0) };
            if (!string.IsNullOrEmpty(tooltip)) button.ToolTip = tooltip;
            if (click != null) button.Click += (s, e) => click();

            if (_style != null) button.ApplyStyle(_style, _corner);

            Children.Add(button);
            return button;
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _corner = cornerRadius;

            foreach (UIElement child in Children)
            {
                var button = child as CardButton;
                if (button != null) button.ApplyStyle(_style, _corner);
            }
        }
    }
}
