using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;
using Windows.Media.Control;

namespace MirrorpondHUD.Cards;

/// <summary>歌词卡自己的设置，存在 <c>CardSettings.Extras["lyrics"]</c>。</summary>
public sealed class LyricsExtras
{
    /// <summary>Auto / Local / Lrclib。</summary>
    public string Source = "Auto";
    public string LrcPath = "";
    public int OffsetMs;
}

/// <summary>
/// 歌词卡：SMTC 识别当前播放会话，用 Position + 经过时间外推播放时钟；
/// 歌词从本地 LRC 或 LRCLIB 获取，显示当前行 / 下一行 / 进度。
/// </summary>
public sealed class LyricsCard : RichCardModuleBase, ICardSettingsModule, ICardEditorSection,
                                  ICardSelfTest, ICardSummary
{
    public const string SettingsKey = "lyrics";

    public override string Kind => "lyrics";
    public override string DisplayName => "歌词";
    public override string Icon => Icons.Play;
    public override int Order => 35;
    public override bool NeedsMouseInput => true;

    public override FrameworkElement CreateContent(CardRenderContext context)
        => new LyricsView(context.Settings, context.Config);

    public void InitializeCard(CardSettings config)
    {
        if (config is null) return;
        LyricsExtras extras = config.ExtrasAs<LyricsExtras>(SettingsKey);
        extras.Source = NormalizeSource(extras.Source);
        extras.LrcPath = (extras.LrcPath ?? "").Trim();
        extras.OffsetMs = Math.Max(-30000, Math.Min(30000, extras.OffsetMs));
        config.SetExtras(SettingsKey, extras);
    }

    public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged)
    {
        LyricsExtras extras = config.ExtrasAs<LyricsExtras>(SettingsKey);
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = "跟随系统正在播放的歌曲显示歌词。默认自动：先用本地歌词，没有就联网查找。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var sourceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        sourceRow.Children.Add(new TextBlock { Text = "歌词来源", VerticalAlignment = VerticalAlignment.Center, Width = 78 });
        var source = new ComboBox { Width = 150 };
        source.Items.Add("自动（推荐）");
        source.Items.Add("只用本地歌词");
        source.Items.Add("只从网络获取");
        source.SelectedIndex = extras.Source == "Local" ? 1 : extras.Source == "Lrclib" ? 2 : 0;
        sourceRow.Children.Add(source);
        panel.Children.Add(sourceRow);

        var pathRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        pathRow.Children.Add(new TextBlock { Text = "本地歌词", VerticalAlignment = VerticalAlignment.Center, Width = 78 });
        var path = new TextBox { Width = 300, Text = extras.LrcPath ?? "" };
        var browse = new Button { Content = "选择…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(9, 2, 9, 2) };
        browse.Click += (s, e) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 LRC 文件",
                Filter = "歌词文件 (*.lrc)|*.lrc|所有文件 (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true) path.Text = dialog.FileName;
        };
        pathRow.Children.Add(path);
        pathRow.Children.Add(browse);
        panel.Children.Add(pathRow);

        var offsetRow = new StackPanel { Orientation = Orientation.Horizontal };
        offsetRow.Children.Add(new TextBlock { Text = "时间偏移", VerticalAlignment = VerticalAlignment.Center, Width = 78 });
        var offset = new TextBox { Width = 80, Text = extras.OffsetMs.ToString(CultureInfo.InvariantCulture) };
        offsetRow.Children.Add(offset);
        offsetRow.Children.Add(new TextBlock
        {
            Text = "歌词比声音快就调大；慢就调小（毫秒）",
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.6
        });
        panel.Children.Add(offsetRow);

        void Commit()
        {
            extras.Source = source.SelectedIndex == 1 ? "Local" : source.SelectedIndex == 2 ? "Lrclib" : "Auto";
            extras.LrcPath = (path.Text ?? "").Trim();
            extras.OffsetMs = int.TryParse(offset.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms)
                ? Math.Max(-30000, Math.Min(30000, ms)) : 0;
            config.SetExtras(SettingsKey, extras);
            onChanged?.Invoke();
        }

        source.SelectionChanged += (s, e) => Commit();
        path.TextChanged += (s, e) => Commit();
        offset.TextChanged += (s, e) => Commit();
        return panel;
    }

    public string Validate(CardSettings config) => null;

    public string Summary(CardSettings config)
    {
        LyricsExtras extras = config is null ? new LyricsExtras() : config.ExtrasAs<LyricsExtras>(SettingsKey);
        return extras.Source == "Local" ? "本地 LRC" : extras.Source == "Lrclib" ? "LRCLIB" : "自动 · SMTC";
    }

    public string RunSelfTest()
    {
        List<LyricLine> lines = LrcParser.Parse("[00:01.00]a\n[00:02.50][00:03.00]b", out string warning);
        return lines.Count == 3 && lines[0].Time < lines[1].Time && warning is null
            ? null : "LRC 解析器未正确处理多时间戳/排序";
    }

    static string NormalizeSource(string value) =>
        string.Equals(value, "Local", StringComparison.OrdinalIgnoreCase) ? "Local"
        : string.Equals(value, "Lrclib", StringComparison.OrdinalIgnoreCase) ? "Lrclib"
        : "Auto";
}

/// <summary>歌词界面：只在 UI 线程改控件；网络/SMTC 失败进状态行，不抛到卡片。</summary>
internal sealed class LyricsView : Grid
{
    static readonly HttpClient Http = CreateHttp();

    readonly Settings _settings;
    readonly CardSettings _config;
    readonly LyricsExtras _extras;
    readonly StyleSettings _style;
    readonly TextBlock _title;
    readonly TextBlock _current;
    readonly TextBlock _next;
    readonly TextBlock _status;
    readonly Border _progressFill;
    readonly TextBlock _time;
    readonly DispatcherTimer _timer;

    GlobalSystemMediaTransportControlsSessionManager _manager;
    GlobalSystemMediaTransportControlsSession _session;
    DateTime _nextPropertiesUtc = DateTime.MinValue;
    string _titleText = "";
    string _artistText = "";
    string _lyricsKey = "";
    List<LyricLine> _lines = new();
    bool _busy;
    bool _disposed;
    bool _fallbackActive;
    DateTime _fallbackAnchorUtc = DateTime.UtcNow;
    double _fallbackPosition;
    bool _wasPlaying;
    double _lyricsDuration;
    double _position;
    double _duration;

    public LyricsView(Settings settings, CardSettings config)
    {
        _settings = settings ?? new Settings();
        _config = config;
        _style = _settings.Style ?? new StyleSettings();
        _extras = config is null ? new LyricsExtras() : config.ExtrasAs<LyricsExtras>(LyricsCard.SettingsKey);

        Width = Math.Max(320, Math.Min(520, _style.FixedWidth > 0 ? _style.FixedWidth : 420));
        Background = Brushes.Transparent;
        Margin = new Thickness(Math.Max(12, _style.Padding), 8, Math.Max(12, _style.Padding), 8);

        var panel = new StackPanel();
        _title = MakeText(Math.Max(10, _style.FontSize - 3), 0.62, false);
        _title.Text = "歌词 · 等待播放会话";
        _title.Margin = new Thickness(0, 0, 0, 2);
        panel.Children.Add(_title);

        _current = MakeText(_style.FontSize + 4, 1.0, true);
        _current.Text = "未检测到正在播放";
        _current.Margin = new Thickness(0, 4, 0, 2);
        panel.Children.Add(_current);

        _next = MakeText(_style.FontSize, 0.55, false);
        _next.Text = "";
        _next.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(_next);

        var bar = new Grid { Height = 3, Margin = new Thickness(0, 2, 0, 4) };
        bar.Children.Add(new Border
        {
            Background = CardPalette.Brush(ThemeResolver.Resolve(_style).Border),
            CornerRadius = new CornerRadius(1.5)
        });
        _progressFill = new Border
        {
            Background = CardPalette.Brush(ThemeResolver.Resolve(_style).Accent),
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        bar.Children.Add(_progressFill);
        panel.Children.Add(bar);

        var bottom = new Grid();
        _time = MakeText(Math.Max(9, _style.FontSize - 4), 0.48, false);
        _time.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(_time);
        _status = MakeText(Math.Max(9, _style.FontSize - 4), 0.48, false);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        bottom.Children.Add(_status);
        panel.Children.Add(bottom);

        Children.Add(panel);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (s, e) => Tick();
        Loaded += (s, e) => _timer.Start();
        Unloaded += (s, e) => { _timer.Stop(); _disposed = true; };
    }

    TextBlock MakeText(double size, double alpha, bool bold) => new()
    {
        FontFamily = HudFont.Resolve(_style.FontFamily),
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = CardPalette.Brush(CardPalette.TryColor(_style.Foreground, Colors.White), alpha),
        TextWrapping = TextWrapping.Wrap
    };

    async void Tick()
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            if (_manager is null)
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _session = _manager.GetCurrentSession();
            if (_session is null)
            {
                SetStatus("未检测到正在播放", "歌词 · 无播放会话");
                return;
            }

            if (DateTime.UtcNow >= _nextPropertiesUtc)
            {
                _nextPropertiesUtc = DateTime.UtcNow.AddSeconds(3);
                GlobalSystemMediaTransportControlsSessionMediaProperties props =
                    await _session.TryGetMediaPropertiesAsync();
                string title = props?.Title ?? "";
                string artist = props?.Artist ?? "";
                if (!string.Equals(title, _titleText, StringComparison.Ordinal)
                    || !string.Equals(artist, _artistText, StringComparison.Ordinal))
                {
                    _titleText = title;
                    _artistText = artist;
                    _fallbackActive = false;
                    _fallbackPosition = 0;
                    _fallbackAnchorUtc = DateTime.UtcNow;
                    _wasPlaying = false;
                    _title.Text = string.IsNullOrWhiteSpace(title)
                        ? "歌词 · 未知曲目"
                        : $"♪ {title}" + (string.IsNullOrWhiteSpace(artist) ? "" : $" — {artist}");
                    await LoadLyricsAsync(title, artist);
                }
            }

            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = _session.GetTimelineProperties();
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = _session.GetPlaybackInfo();
            bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // 网易云等播放器可能把 Position/EndTime/LastUpdatedTime 全部报成 0 / 1601。
            // 这时不能外推（会得到几百亿秒，永远停在最后一句），改用“LRCLIB 时长 + 本地经过时间”兜底。
            bool timelineValid = timeline.Position > TimeSpan.Zero
                                 || timeline.EndTime > timeline.StartTime
                                 || timeline.LastUpdatedTime.Year >= 2000;
            double duration;
            double position;
            if (timelineValid)
            {
                duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
                position = timeline.Position.TotalSeconds;
                if (playing && timeline.LastUpdatedTime.Year >= 2000)
                    position += Math.Max(0, (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds);
                _fallbackActive = false;
            }
            else
            {
                duration = _lyricsDuration;
                DateTime now = DateTime.UtcNow;
                if (!_fallbackActive)
                {
                    _fallbackActive = true;
                    _fallbackAnchorUtc = now;
                    _fallbackPosition = 0;
                    _wasPlaying = playing;
                }
                else
                {
                    if (_wasPlaying) _fallbackPosition += Math.Max(0, (now - _fallbackAnchorUtc).TotalSeconds);
                    _fallbackAnchorUtc = now;
                    _wasPlaying = playing;
                }
                position = _fallbackPosition;
            }
            if (duration <= 0.5) duration = _lyricsDuration;
            position = Math.Max(0, duration > 0 ? Math.Min(duration, position) : position);
            _position = position;
            _duration = duration;
            UpdateLine(position);
            UpdateProgress();
        }
        catch (Exception ex)
        {
            SetStatus("SMTC 读取失败", "歌词 · " + ex.Message);
            _manager = null;
            Log.Warn($"歌词卡读取播放会话失败: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    async Task LoadLyricsAsync(string title, string artist)
    {
        string key = (title ?? "") + "\n" + (artist ?? "");
        if (string.Equals(key, _lyricsKey, StringComparison.Ordinal)) return;
        _lyricsKey = key;
        _lines = new List<LyricLine>();
        _lyricsDuration = 0;
        if (string.IsNullOrWhiteSpace(title)) return;

        try
        {
            string source = (_extras.Source ?? "Auto").Trim();
            string path = (_extras.LrcPath ?? "").Trim();
            if ((source == "Auto" || source == "Local") && path.Length > 0)
            {
                if (System.IO.File.Exists(path))
                {
                    string text = await System.IO.File.ReadAllTextAsync(path);
                    _lines = LrcParser.Parse(text, out _);
                    if (_lines.Count > 0) _lyricsDuration = _lines[_lines.Count - 1].Time + 4;
                    if (_lines.Count > 0) { SetStatus("本地 LRC", ""); return; }
                }
            }

            if (source == "Local") { SetStatus("未找到本地 LRC", ""); return; }

            string url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title)
                       + "&artist_name=" + Uri.EscapeDataString(artist ?? "");
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            string json = await Http.GetStringAsync(url, cts.Token);
            using JsonDocument doc = JsonDocument.Parse(json);
            double lrclibDuration = 0;
            if (doc.RootElement.TryGetProperty("duration", out JsonElement dur)
                && dur.ValueKind == JsonValueKind.Number) lrclibDuration = dur.GetDouble();
            if (doc.RootElement.TryGetProperty("syncedLyrics", out JsonElement sync)
                && sync.ValueKind == JsonValueKind.String)
            {
                _lines = LrcParser.Parse(sync.GetString(), out string warning);
                _lyricsDuration = lrclibDuration > 0 ? lrclibDuration
                    : (_lines.Count > 0 ? _lines[_lines.Count - 1].Time + 4 : 0);
                SetStatus(_lines.Count == 0 ? (warning ?? "歌词为空") : "LRCLIB", "");
            }
            else if (doc.RootElement.TryGetProperty("plainLyrics", out JsonElement plain)
                     && plain.ValueKind == JsonValueKind.String
                     && !string.IsNullOrWhiteSpace(plain.GetString()))
            {
                // 没有时间轴时按每行 4 秒估算，至少能随播放推进；状态行明确标注。
                string[] rows = plain.GetString().Replace("\r\n", "\n").Split('\n');
                var estimated = new List<LyricLine>();
                foreach (string row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row)) continue;
                    estimated.Add(new LyricLine { Time = estimated.Count * 4.0, Text = row.Trim() });
                }
                _lines = estimated;
                _lyricsDuration = Math.Max(lrclibDuration, estimated.Count * 4.0);
                SetStatus("LRCLIB 无时间轴 · 按 4 秒估算", "");
            }
            else
            {
                SetStatus("LRCLIB 无歌词", "");
            }
        }
        catch (Exception ex)
        {
            SetStatus("歌词获取失败", ex.Message);
            Log.Warn($"歌词卡获取歌词失败: {ex.Message}");
        }
    }

    void UpdateLine(double position)
    {
        if (_lines.Count == 0)
        {
            _current.Text = "暂无歌词";
            _next.Text = "";
            return;
        }

        double adjusted = position + _extras.OffsetMs / 1000.0;
        int index = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Time <= adjusted + 0.15) index = i;
            else break;
        }

        _current.Text = index < 0 ? "♪" : _lines[index].Text;
        _next.Text = index + 1 < _lines.Count ? _lines[index + 1].Text : "";
        _current.Opacity = 1.0;
        _next.Opacity = 0.55;
    }

    void UpdateProgress()
    {
        double ratio = _duration > 0 ? Math.Max(0, Math.Min(1, _position / _duration)) : 0;
        _progressFill.Width = Math.Max(0, ActualWidth * ratio);
        _time.Text = Format(_position) + " / " + Format(_duration);
    }

    static string Format(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    void SetStatus(string status, string title)
    {
        if (_status is not null) _status.Text = status ?? "";
        if (!string.IsNullOrWhiteSpace(title) && _title is not null) _title.Text = title;
    }

    static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MirrorpondHUD/2.46.6 (lyrics card)");
        return client;
    }
}
