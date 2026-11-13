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

    /// <summary>2.47.0：换行时是否播放粒子聚字；只作用于当前歌词，不影响标题 / 时间。</summary>
    public bool Particle = true;
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

        var particleRow = new CheckBox
        {
            Content = "换行时粒子聚字",
            IsChecked = extras.Particle,
            Margin = new Thickness(0, 10, 0, 2)
        };
        panel.Children.Add(particleRow);
        panel.Children.Add(new TextBlock
        {
            Text = "只作用于当前歌词，标题与时间不动；开启「减少动效」时不播放。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.6,
            Margin = new Thickness(22, 0, 0, 0)
        });

        void Commit()
        {
            extras.Source = source.SelectedIndex == 1 ? "Local" : source.SelectedIndex == 2 ? "Lrclib" : "Auto";
            extras.LrcPath = (path.Text ?? "").Trim();
            extras.OffsetMs = int.TryParse(offset.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms)
                ? Math.Max(-30000, Math.Min(30000, ms)) : 0;
            extras.Particle = particleRow.IsChecked == true;
            config.SetExtras(SettingsKey, extras);
            onChanged?.Invoke();
        }

        source.SelectionChanged += (s, e) => Commit();
        path.TextChanged += (s, e) => Commit();
        offset.TextChanged += (s, e) => Commit();
        particleRow.Checked += (s, e) => Commit();
        particleRow.Unchecked += (s, e) => Commit();
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
        // P0-7：GBK 的「中文」= D6 D0 CE C4，不能被当成非法 UTF-8 丢掉。
        string gbk = LrcFile.DecodeBytes(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 });
        return lines.Count == 3 && lines[0].Time < lines[1].Time && warning is null && gbk == "中文"
            ? null : "LRC 解析器未正确处理多时间戳/排序或 GB18030 回退";
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
    readonly CardProgressBar _progressBar;
    readonly CardParticleText _particle;
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
    int _lineIndex = -2;
    bool _particleEnabled;
    Color _particleColor;

    // P0-5：SMTC 失败节流 + 退避；P0-3/P0-4/P0-6：取词任务代次与失败重试。
    readonly LogThrottle _smtcLog = new("歌词卡 SMTC 读取失败", 30000);
    int _smtcFailures;
    DateTime _smtcRetryUtc = DateTime.MinValue;
    int _lyricsGeneration;
    string _failedKey = "";
    DateTime _loadRetryUtc = DateTime.MinValue;
    int _loadAttempts;

    public LyricsView(Settings settings, CardSettings config)
    {
        _settings = settings ?? new Settings();
        _config = config;
        _style = _settings.Style ?? new StyleSettings();
        _extras = config is null ? new LyricsExtras() : config.ExtrasAs<LyricsExtras>(LyricsCard.SettingsKey);
        _particleEnabled = _extras.Particle && _settings.Behavior is not null && !_settings.Behavior.ReduceMotion;
        _particleColor = CardPalette.TryColor(_style.Foreground, Colors.White);

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

        // 当前歌词与粒子层同格：粒子收敛前隐藏文字，收敛后由 Settled 恢复。
        _particle = new CardParticleText
        {
            TextFont = HudFont.Resolve(_style.FontFamily),
            TextSize = _style.FontSize + 4,
            TextWeight = FontWeights.SemiBold,
            ParticleSize = _style.TextParticleSize,
            ParticleCount = Math.Min(900, Math.Max(120, _style.TextParticleCount)),
            Speed = _style.TextParticleSpeed
        };
        _particle.Settled += OnParticleSettled;

        var currentHost = new Grid { Margin = new Thickness(0, 4, 0, 2) };
        currentHost.Children.Add(_current);
        currentHost.Children.Add(_particle);
        panel.Children.Add(currentHost);

        _next = MakeText(_style.FontSize, 0.55, false);
        _next.Text = "";
        _next.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(_next);

        // 2.47.0：和媒体卡 / 中转站共用一条进度条（分段开关、进度条颜色都走同一处）。
        _progressBar = new CardProgressBar { Margin = new Thickness(0, 2, 0, 4) };
        _progressBar.ApplyStyle(_style);
        panel.Children.Add(_progressBar);

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
        Unloaded += (s, e) =>
        {
            _timer.Stop();
            _particle.Stop();
            // 动画中途被卸载时文字可能还藏着；恢复可见，重新上树不能缺字。
            _current.Visibility = Visibility.Visible;
            _disposed = true;
        };
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
            DateTime now = DateTime.UtcNow;
            // P0-5：失败后退避，不要每 200ms 撞同一个错误。
            if (now < _smtcRetryUtc) return;

            if (_manager is null)
            {
                try
                {
                    _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _smtcFailures = 0;
                    _smtcLog.Success();
                }
                catch (Exception ex)
                {
                    _smtcFailures++;
                    _smtcRetryUtc = now.AddSeconds(_smtcFailures <= 1 ? 2 : _smtcFailures == 2 ? 5 : 10);
                    SetStatus("SMTC 不可用", "歌词 · 播放会话读取失败");
                    _smtcLog.Failure(ex.Message);
                    return;
                }
            }

            _session = _manager.GetCurrentSession();
            if (_session is null)
            {
                SetStatus("未检测到正在播放", "歌词 · 无播放会话");
                _smtcLog.Success();
                return;
            }

            // P0-2：标题只由当前会话决定，不随状态文案漂移。
            SetHeader(_titleText, _artistText);

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
                    SetHeader(title, artist);
                    // P0-3：取词不阻塞时钟；失败由退避重试接管（P0-4 / P0-6）。
                    StartLyricsLoad(title, artist);
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
                DateTime clockNow = DateTime.UtcNow;
                if (!_fallbackActive)
                {
                    _fallbackActive = true;
                    _fallbackAnchorUtc = clockNow;
                    _fallbackPosition = 0;
                    _wasPlaying = playing;
                }
                else
                {
                    if (_wasPlaying) _fallbackPosition += Math.Max(0, (clockNow - _fallbackAnchorUtc).TotalSeconds);
                    _fallbackAnchorUtc = clockNow;
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
            _smtcLog.Success();
            MaybeRetryLyrics();
        }
        catch (Exception ex)
        {
            // 单次属性 / 时间轴读取失败属良性竞态：不清空 manager，退避 2 秒再试。
            _smtcRetryUtc = DateTime.UtcNow.AddSeconds(2);
            SetStatus("SMTC 读取失败", "");
            _smtcLog.Failure(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>换歌：立即作废旧代次，清空行与粒子池，然后后台取词。</summary>
    void StartLyricsLoad(string title, string artist)
    {
        string key = (title ?? "") + "\n" + (artist ?? "");
        if (string.Equals(key, _lyricsKey, StringComparison.Ordinal)) return;

        _lyricsKey = key;
        _lyricsGeneration++;
        _lines = new List<LyricLine>();
        _lyricsDuration = 0;
        _loadAttempts = 0;
        _failedKey = "";
        _loadRetryUtc = DateTime.MinValue;
        _particle.Reset();
        _lineIndex = -2;
        _current.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(title)) return;

        _ = LoadLyricsAsync(key, title, artist, _lyricsGeneration);
    }

    void MaybeRetryLyrics()
    {
        if (_failedKey.Length == 0 || _loadAttempts >= 3 || DateTime.UtcNow < _loadRetryUtc) return;

        _failedKey = "";
        _ = LoadLyricsAsync(_lyricsKey, _titleText, _artistText, ++_lyricsGeneration);
    }

    bool Stale(int generation) => _disposed || generation != _lyricsGeneration;

    async Task LoadLyricsAsync(string key, string title, string artist, int generation)
    {
        try
        {
            string source = (_extras.Source ?? "Auto").Trim();
            string path = (_extras.LrcPath ?? "").Trim();
            if ((source == "Auto" || source == "Local") && path.Length > 0)
            {
                if (System.IO.File.Exists(path))
                {
                    // P0-7：先 BOM，再严格 UTF-8，最后回退 GB18030（中文歌词站常见）。
                    string text = await LrcFile.ReadAsync(path);
                    if (Stale(generation)) return;

                    List<LyricLine> parsed = LrcParser.Parse(text, out _);
                    if (parsed.Count > 0)
                    {
                        ApplyLines(parsed, parsed[parsed.Count - 1].Time + 4, "本地 LRC");
                        return;
                    }
                    SetStatus("本地 LRC 无时间轴", "");
                }
                else if (source == "Local")
                {
                    SetStatus("未找到本地 LRC", "");
                    return;
                }
            }

            if (source == "Local")
            {
                if (path.Length == 0) SetStatus("未配置本地歌词", "");
                return;
            }

            string url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title)
                       + "&artist_name=" + Uri.EscapeDataString(artist ?? "");
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (Stale(generation)) return;

            int status = (int)response.StatusCode;
            if (status == 404)
            {
                // P0-6：404 = “没有这首歌的歌词”，不是获取失败；不重试。
                _loadAttempts = 0;
                _failedKey = "";
                SetStatus("LRCLIB 无歌词", "");
                return;
            }
            if (status == 429)
            {
                TimeSpan wait = RetryAfter(response, TimeSpan.FromSeconds(10));
                ScheduleRetry(key, wait);
                SetStatus("请求过于频繁，稍后重试", "");
                return;
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"LRCLIB HTTP {status}");

            string json = await response.Content.ReadAsStringAsync(cts.Token);
            if (Stale(generation)) return;

            using JsonDocument doc = JsonDocument.Parse(json);
            double lrclibDuration = 0;
            if (doc.RootElement.TryGetProperty("duration", out JsonElement dur)
                && dur.ValueKind == JsonValueKind.Number) lrclibDuration = dur.GetDouble();
            if (doc.RootElement.TryGetProperty("syncedLyrics", out JsonElement sync)
                && sync.ValueKind == JsonValueKind.String)
            {
                List<LyricLine> lines = LrcParser.Parse(sync.GetString(), out string warning);
                double duration = lrclibDuration > 0 ? lrclibDuration
                    : (lines.Count > 0 ? lines[lines.Count - 1].Time + 4 : 0);
                if (lines.Count == 0)
                {
                    SetStatus(warning ?? "歌词为空", "");
                    return;
                }
                ApplyLines(lines, duration, "LRCLIB");
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
                ApplyLines(estimated, Math.Max(lrclibDuration, estimated.Count * 4.0),
                           "LRCLIB 无时间轴 · 按 4 秒估算");
            }
            else
            {
                SetStatus("LRCLIB 无歌词", "");
            }
        }
        catch (OperationCanceledException)
        {
            if (Stale(generation)) return;
            ScheduleRetry(key, TimeSpan.FromSeconds(10));
            SetStatus("歌词获取超时", "");
            Log.Warn("歌词卡获取歌词超时（8 秒）");
        }
        catch (Exception ex)
        {
            if (Stale(generation)) return;
            ScheduleRetry(key, TimeSpan.FromSeconds(10));
            SetStatus("歌词获取失败", "");
            Log.Warn($"歌词卡获取歌词失败: {ex.Message}");
        }
    }

    void ApplyLines(List<LyricLine> lines, double duration, string status)
    {
        _loadAttempts = 0;
        _failedKey = "";
        _lines = lines;
        _lyricsDuration = duration;
        _lineIndex = -2;
        _particle.Reset();
        _current.Visibility = Visibility.Visible;
        SetStatus(status, "");
    }

    void ScheduleRetry(string key, TimeSpan suggested)
    {
        _loadAttempts++;
        _failedKey = key;
        double seconds = _loadAttempts <= 1
            ? Math.Max(5, suggested.TotalSeconds)
            : _loadAttempts == 2 ? 15 : 60;
        _loadRetryUtc = DateTime.UtcNow.AddSeconds(seconds);
    }

    static TimeSpan RetryAfter(HttpResponseMessage response, TimeSpan fallback)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return fallback;
        if (header.Delta.HasValue && header.Delta.Value > TimeSpan.Zero) return header.Delta.Value;
        if (header.Date.HasValue)
        {
            TimeSpan delta = header.Date.Value - DateTimeOffset.UtcNow;
            if (delta > TimeSpan.Zero) return delta;
        }
        return fallback;
    }

    void UpdateLine(double position)
    {
        if (_lines.Count == 0)
        {
            SetCurrentLine("暂无歌词", -1);
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

        string text = index < 0 ? "♪" : _lines[index].Text;
        SetCurrentLine(text, index);
        _next.Text = index + 1 < _lines.Count ? _lines[index + 1].Text : "";
        _next.Opacity = 0.55;
    }

    /// <summary>
    /// 换行统一入口：只在歌词真的换了才触发粒子。
    /// 先让粒子层采样并起播，再隐藏文字——顺序反了会闪一帧“只有粒子没有字”。
    /// </summary>
    void SetCurrentLine(string text, int index)
    {
        if (index == _lineIndex && string.Equals(_current.Text, text, StringComparison.Ordinal)) return;
        _lineIndex = index;
        _current.Text = text;
        _current.Opacity = 1.0;

        if (index < 0)
        {
            // 前奏 / 间奏的占位符不参与聚字：清空粒子池，新歌第一句从静态开始。
            _particle.Stop();
            _particle.Reset();
            _current.Visibility = Visibility.Visible;
            return;
        }

        if (!_particleEnabled || string.IsNullOrWhiteSpace(text))
        {
            _particle.Stop();
            _current.Visibility = Visibility.Visible;
            return;
        }

        ParticlePlayResult result = _particle.Play(text, _particleColor);
        if (result == ParticlePlayResult.Animated)
        {
            _current.Visibility = Visibility.Hidden;
            return;
        }

        // 首池（Primed）或采样不可用：文字照常显示，下一句再聚。
        _current.Visibility = Visibility.Visible;
    }

    void OnParticleSettled()
    {
        if (_disposed) return;
        _current.Visibility = Visibility.Visible;
    }

    void UpdateProgress()
    {
        double ratio = _duration > 0 ? Math.Max(0, Math.Min(1, _position / _duration)) : 0;
        _progressBar.Value = ratio;
        _time.Text = CardFormat.Clock(_position) + " / " + CardFormat.Clock(_duration);
    }

    void SetHeader(string title, string artist) =>
        _title.Text = string.IsNullOrWhiteSpace(title)
            ? "歌词 · 未知曲目"
            : $"♪ {title}" + (string.IsNullOrWhiteSpace(artist) ? "" : $" — {artist}");

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
