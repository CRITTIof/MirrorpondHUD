using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    public enum CardStatusKind
    {
        Normal,
        Info,
        Success,
        Warning,
        Error,
        Busy
    }

    /// <summary>
    /// 卡片底部的一行状态：图标 + 文字 + 可选进度条，支持错误态与转圈。
    ///
    /// 对话 / 命令行 / 媒体卡各自拼过类似的东西，中转站也要“正在复制 2/5 / 已取消 / 失败原因”；
    /// 这里统一成一块，颜色与字号跟着外观令牌走。
    /// </summary>
    public sealed class CardStatus : Grid
    {
        static readonly string[] Spin = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        readonly TextBlock _icon;
        readonly TextBlock _text;
        readonly Grid _progressHost;
        readonly Border _track;
        readonly Border _fill;
        readonly DispatcherTimer _timer;

        StyleSettings _style;
        ThemeTokens _tokens;
        Color _foreground;
        CardStatusKind _kind = CardStatusKind.Normal;
        double? _progress;
        bool _busy;
        int _frame;

        public CardStatus()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _icon = new TextBlock
            {
                Width = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                TextAlignment = TextAlignment.Center
            };
            _text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(_icon);
            line.Children.Add(_text);
            Children.Add(line);

            _track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5) };
            _fill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0
            };
            _track.SizeChanged += (s, e) => UpdateProgress();

            _progressHost = new Grid { Margin = new Thickness(19, 2, 0, 0), Visibility = Visibility.Collapsed };
            _progressHost.Children.Add(_track);
            _progressHost.Children.Add(_fill);
            SetRow(_progressHost, 1);
            Children.Add(_progressHost);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _timer.Tick += (s, e) =>
            {
                _frame = (_frame + 1) % Spin.Length;
                _icon.Text = Spin[_frame];
            };

            _tokens = ThemeResolver.Resolve(null);
            _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
            ApplyStyle(null);
        }

        public CardStatusKind Kind { get { return _kind; } }

        public string Text
        {
            get { return _text.Text ?? ""; }
            set { SetText(value); }
        }

        /// <summary>设置文字与状态（Busy 会自动开始转圈）。</summary>
        public void SetText(string text, CardStatusKind kind = CardStatusKind.Normal)
        {
            _text.Text = text ?? "";
            _kind = kind;

            if (kind == CardStatusKind.Busy) StartSpin();
            else StopSpin();

            UpdateVisual();
        }

        /// <summary>只切换“进行中”状态，保留当前文字；文字为空时补「处理中…」。</summary>
        public void SetBusy(bool busy)
        {
            _busy = busy;
            if (busy)
            {
                if (string.IsNullOrEmpty(_text.Text)) _text.Text = "处理中…";
                StartSpin();
                UpdateVisual();
            }
            else
            {
                StopSpin();
                if (_kind == CardStatusKind.Busy) _kind = CardStatusKind.Normal;
                UpdateVisual();
            }
        }

        /// <summary>0~1 的确定进度；null 隐藏进度条。调用方自己管“先 SetBusy(true) 再 SetProgress”。</summary>
        public void SetProgress(double? value)
        {
            _progress = value;
            _progressHost.Visibility = value.HasValue ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgress();
        }

        public void ApplyStyle(StyleSettings style)
        {
            _style = style ?? new StyleSettings();
            _tokens = ThemeResolver.Resolve(_style);
            _foreground = OverlaySurface.ParseColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            FontFamily family = CardPalette.ResolveFont(_style.FontFamily);
            double size = Math.Max(9, _style.FontSize - 2);

            _icon.FontFamily = family;
            _icon.FontSize = size + 1;
            _text.FontFamily = family;
            _text.FontSize = size;

            _track.Background = CardPalette.Brush(_foreground, 0.12);
            UpdateVisual();
        }

        void UpdateVisual()
        {
            _text.Foreground = CardPalette.Brush(_foreground, 0.72);
            _fill.Background = CardPalette.Brush(
                _kind == CardStatusKind.Error ? _tokens.Danger : _tokens.Accent);

            switch (_kind)
            {
                case CardStatusKind.Error:
                    _icon.Text = "!";
                    _icon.Foreground = CardPalette.Brush(_tokens.Danger);
                    _text.Foreground = CardPalette.Brush(_tokens.Danger, 0.95);
                    break;
                case CardStatusKind.Warning:
                    _icon.Text = "!";
                    _icon.Foreground = CardPalette.Brush(_foreground, 0.9);
                    break;
                case CardStatusKind.Success:
                    _icon.Text = "✓";
                    _icon.Foreground = CardPalette.Brush(_tokens.Accent);
                    break;
                case CardStatusKind.Info:
                    _icon.Text = "i";
                    _icon.Foreground = CardPalette.Brush(_tokens.Muted);
                    break;
                case CardStatusKind.Busy:
                    if (!_busy) _icon.Text = Spin[0];
                    _icon.Foreground = CardPalette.Brush(_tokens.Accent);
                    break;
                default:
                    _icon.Text = "●";
                    _icon.Foreground = CardPalette.Brush(_foreground, 0.45);
                    break;
            }
        }

        void StartSpin()
        {
            _busy = true;
            _frame = 0;
            _icon.Text = Spin[0];
            if (!_timer.IsEnabled) _timer.Start();
        }

        void StopSpin()
        {
            _busy = false;
            if (_timer.IsEnabled) _timer.Stop();
        }

        void UpdateProgress()
        {
            if (!_progress.HasValue) return;

            double value = Math.Max(0.0, Math.Min(1.0, _progress.Value));
            _fill.Width = Math.Max(0, _track.ActualWidth * value);
        }
    }
}
