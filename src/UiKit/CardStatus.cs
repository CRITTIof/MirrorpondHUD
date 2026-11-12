using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

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
/// 卡片底部的一行状态：图标 + 文字 + 可选进度，支持错误态与转圈。
///
/// 2.36.0 起进度条受「终末地视觉 → 分段进度」控制：
/// 开启时是 28 段直角块，关闭时回到 3px 半圆端头；两种实现共用同一个 0–1 进度值。
/// 对话 / 命令行 / 媒体卡 / 中转站都用这一块，避免各自拼一套状态行。
/// </summary>
public sealed class CardStatus : Grid, IDisposable
{
    static readonly string[] Spin = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    readonly TextBlock _icon;
    readonly TextBlock _text;
    readonly Grid _progressHost;
    readonly CardSegmentBar _segment;
    readonly Grid _legacyProgress;
    readonly Border _track;
    readonly Border _fill;
    readonly CornerBrackets _brackets;
    DispatcherTimer _timer;
    bool _disposed;

    StyleSettings _style;
    ThemeTokens _tokens;
    Color _foreground;
    CardStatusKind _kind = CardStatusKind.Normal;
    double? _progress;
    bool _busy;
    bool _showIcon = true;
    bool _segmentMode = true;
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

        _segment = new CardSegmentBar
        {
            Height = 4,
            Value = 0,
            Segments = 28,
            Gap = 2
        };

        _track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5) };
        _fill = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0
        };
        _legacyProgress = new Grid();
        _legacyProgress.Children.Add(_track);
        _legacyProgress.Children.Add(_fill);
        _track.SizeChanged += (s, e) => UpdateProgress();

        _progressHost = new Grid { Margin = new Thickness(19, 3, 0, 0), Visibility = Visibility.Collapsed };
        _progressHost.Children.Add(_legacyProgress);
        _progressHost.Children.Add(_segment);
        SetRow(_progressHost, 1);
        Children.Add(_progressHost);

        _brackets = new CornerBrackets
        {
            Margin = new Thickness(-4),
            ArmLength = 12,
            Thickness = 2,
            IsHitTestVisible = false
        };
        SetRowSpan(_brackets, 2);
        Children.Add(_brackets);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _timer.Tick += OnSpinTick;

        // K-31：90ms 转圈表原来没有停止出口 —— Busy 时卡片被换肤重建 / 删除，定时器还在
        // 空转并强引用控件。离树、隐藏时停表；重新上树且仍在忙碌时恢复。
        Loaded += (s, e) => ResumeSpinIfNeeded();
        Unloaded += (s, e) => StopTimer();
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible) ResumeSpinIfNeeded();
            else StopTimer();
        };

        _tokens = ThemeResolver.Resolve(null);
        _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        ApplyStyle(null);
    }

    public CardStatusKind Kind => _kind;

    public string Text
    {
        get => _text.Text ?? ""; set => SetText(value);
    }

    /// <summary>如媒体卡要把时间读数换成等宽字体；ApplyStyle 之后设置即可，会一直保留。</summary>
    public FontFamily TextFont
    {
        get => _text.FontFamily;
        set => _text.FontFamily = value ?? CardPalette.ResolveFont(null);
    }

    /// <summary>像媒体进度这种只需要文字、不需要再叠一个状态点的场景可关闭图标。</summary>
    public bool ShowIcon
    {
        get => _showIcon;
        set
        {
            if (_showIcon == value) return;
            _showIcon = value;
            UpdateIconLayout();
        }
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

    public void ApplyStyle(StyleSettings style, double cornerRadius = -1)
    {
        _style = style ?? new StyleSettings();
        _tokens = ThemeResolver.Resolve(_style);
        _foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

        FontFamily family = CardPalette.ResolveFont(_style.FontFamily);
        double size = Math.Max(9, _style.FontSize - 2);

        _icon.FontFamily = family;
        _icon.FontSize = size + 1;
        _text.FontFamily = family;
        _text.FontSize = size;

        _segmentMode = EndfieldVisuals.SegmentProgress(_style);
        _segment.Visibility = _segmentMode ? Visibility.Visible : Visibility.Collapsed;
        _legacyProgress.Visibility = _segmentMode ? Visibility.Collapsed : Visibility.Visible;

        _segment.TrackBrush = CardPalette.Brush(_foreground, 0.12);
        _track.Background = CardPalette.Brush(_foreground, 0.12);
        _fill.Background = CardPalette.Brush(_tokens.Accent, 0.9);
        _track.CornerRadius = new CornerRadius(1.5);
        _fill.CornerRadius = new CornerRadius(1.5);

        _brackets.ApplyStyle(CardPalette.TryColor(_style.Foreground, _foreground));
        UpdateIconLayout();
        UpdateVisual();
        UpdateProgress();
    }

    void UpdateIconLayout()
    {
        _icon.Visibility = _showIcon ? Visibility.Visible : Visibility.Collapsed;
        _progressHost.Margin = new Thickness(_showIcon ? 19 : 0, 3, 0, 0);
    }

    void UpdateVisual()
    {
        _text.Foreground = CardPalette.Brush(_foreground, 0.72);
        _segment.FillBrush = ProgressBrush();
        _fill.Background = ProgressBrush();
        _brackets.Visibility = EndfieldVisuals.CornerBrackets(_style)
                               && (_kind == CardStatusKind.Error || _kind == CardStatusKind.Warning)
            ? Visibility.Visible
            : Visibility.Collapsed;

        switch (_kind)
        {
            case CardStatusKind.Error:
                _icon.Text = "!";
                _icon.Foreground = CardPalette.Brush(_tokens.Danger);
                _text.Foreground = CardPalette.Brush(_tokens.Danger, 0.95);
                break;
            case CardStatusKind.Warning:
                _icon.Text = "!";
                _icon.Foreground = CardPalette.Brush(_tokens.Warning);
                break;
            case CardStatusKind.Success:
                _icon.Text = "✓";
                _icon.Foreground = CardPalette.Brush(_tokens.Success);
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

    Brush ProgressBrush() => _kind == CardStatusKind.Error ? CardPalette.Brush(_tokens.Danger)
        : _kind == CardStatusKind.Success ? CardPalette.Brush(_tokens.Success)
        : CardPalette.Brush(_tokens.Accent);

    void StartSpin()
    {
        if (_busy) return;
        _busy = true;
        _frame = 0;
        _icon.Text = Spin[0];
        ResumeSpinIfNeeded();
    }

    void StopSpin()
    {
        _busy = false;
        StopTimer();
    }

    void OnSpinTick(object sender, EventArgs e)
    {
        _frame = (_frame + 1) % Spin.Length;
        _icon.Text = Spin[_frame];
    }

    /// <summary>K-31：只有“仍然忙碌 + 当前可见”才转圈，离屏的控件不该占用 UI 定时器。</summary>
    void ResumeSpinIfNeeded()
    {
        if (_disposed || !_busy || !IsVisible) return;
        DispatcherTimer timer = _timer;
        if (timer is not null && !timer.IsEnabled) timer.Start();
    }

    void StopTimer()
    {
        DispatcherTimer timer = _timer;
        if (timer is not null && timer.IsEnabled) timer.Stop();
    }

    /// <summary>K-31：释放路径 —— 先停表再断开 Tick，控件不再被定时器强引用。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DispatcherTimer timer = _timer;
        _timer = null;
        if (timer is null) return;
        timer.Stop();
        timer.Tick -= OnSpinTick;
    }

    void UpdateProgress()
    {
        if (!_progress.HasValue) return;

        double value = Math.Max(0.0, Math.Min(1.0, _progress.Value));
        if (_segmentMode)
        {
            _segment.Value = value;
            return;
        }

        _fill.Width = Math.Max(0, _track.ActualWidth * value);
    }
}
