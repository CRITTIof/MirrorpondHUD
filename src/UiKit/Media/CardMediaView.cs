using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls.Media;

/// <summary>
/// 媒体控件：一张卡片里的图片 / 位图 / 动图 / 视频，全部由这一个 Grid 承载。
///
/// 布局和文字卡片一致：卡片自己的底色、毛玻璃、圆角由外层 OverlaySurface 画，
/// 这里在四周留出和文字一样的内边距，顶部一行文件名（超长省略），
/// 视频再在底部加一条播放进度条，所以媒体卡看起来仍是同一叠卡片里的一张。
///
/// · 静态图片（png / jpg / bmp / tif / ico …）用 Image 绘制；
/// · 动图（GIF）交给 <see cref="GifAnimator"/> 按每帧自带的延时播放；
/// · 视频默认交给 <see cref="VideoWindow"/>（分层窗口无法显示 MediaElement，
///   视频放在一个跟随卡片位置的不透明子窗口里）；
/// · 选了 ffmpeg 或系统解不开时交给 <see cref="FfmpegPlayer"/>，逐帧画回卡片里。
/// </summary>
public sealed class CardMediaView : Grid, IDisposable
{
    static readonly string[] VideoExtensions =
    {
        ".mp4", ".m4v", ".wmv", ".avi", ".mov", ".mkv", ".webm", ".mpg", ".mpeg", ".ts"
    };

    readonly CardHeader _header;
    readonly Border _rule;
    readonly Grid _hostFrame;
    readonly Grid _host;
    readonly CornerBrackets _brackets;
    readonly Image _image;
    readonly TextBlock _message;
    readonly CardStatus _status;
    readonly CardFooter _metaFooter;

    GifAnimator _gif;
    VideoWindow _videoWindow;
    FfmpegPlayer _player;
    DispatcherTimer _progressTimer;

    /// <summary>P1-6：音频启动失败的如实备注（null = 音频正常）；追加在右上角状态行。</summary>
    string _audioNote;

    MediaSettings _settings = new();
    StyleSettings _style;
    string _loadedPath;
    // K-13：播放中源文件被删除 / 移走时，每 2 秒探测一次。
    DateTime _lastFileCheck;
    string _pendingVideo;
    // 每次开始一次「探测 + 启动」就递增；await 回来时代号变了说明用户已经换了文件 /
    // 释放了视频，本次探测结果必须丢弃（否则会给新视频启动一个旧 player）。
    int _videoRun;

    // K-12：图片解码代次独立于视频；后台解完回来先校验代次与路径，旧代结果直接丢弃。
    int _imageRun;
    // K-16：逐帧复用同一张 WriteableBitmap，只有帧尺寸变化才重建（避免每帧 GC）。
    WriteableBitmap _frameBitmap;
    // K-7：非循环视频是否已自然播完；状态行显示"已结束"，播放键可从头重播。
    bool _mediaEnded;
    // K-10：分层合成不支持时只降级一次，避免每次布局都重启媒体。
    bool _videoOpacityFallback;

    // K-11：已应用的设置值快照。不能拿 previous 与 _settings 做引用比较 ——
    // MediaCard.ExtrasAs<MediaSettings> 每次返回同一个实例，两个变量引用相等，
    // 所有差量都会被判成"没变"（L-4 的修复因此被抵消）。这里比较真实值。
    bool _appliedValid;
    double _appliedSpeed;
    bool _appliedMuted;
    bool _appliedLoop;
    bool _appliedAutoplay;
    string _appliedDecoder = "";
    string _appliedDecoderPath = "";

    // K-15：ffmpeg 首次查找在后台进行，UI 先显示加载态，查找完成后只重试一次。
    bool _waitingForFfmpeg;
    Task<string> _ffmpegRetryTask;

    // 每 250ms 读一次 MediaElement.Position/NaturalDuration；窗口关闭瞬间会抛，
    // 属良性竞态但也会掩盖真实问题，用 LogThrottle 做状态转移 + 30s 节流。
    static readonly LogThrottle MediaStateThrottle = new("读取视频状态", 30000);

    Size _natural = new(320, 180);
    double _cornerRadius = 10;
    double _padX = 15;
    double _padY = 12;
    double _speed = 1;
    bool _playing = true;
    Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
    double _clipWidth;
    double _clipHeight;
    double _clipRadius = -1;
    string _metaCode = "";
    bool _disposed;

    public CardMediaView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;

        _header = new CardHeader();
        _header.Visibility = Visibility.Collapsed;

        _rule = new Border { Height = 1, Visibility = Visibility.Collapsed };

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _message = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16)
        };

        _host = new Grid { ClipToBounds = true };
        _host.Children.Add(_image);
        _host.Children.Add(_message);

        _brackets = new CornerBrackets
        {
            Margin = new Thickness(-4),
            ArmLength = 13,
            Thickness = 2,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _hostFrame = new Grid();
        _hostFrame.Children.Add(_host);
        _hostFrame.Children.Add(_brackets);

        // 和对话卡一致的底部状态行：媒体时间 / 倍速 / 音频备注都走这里，
        // 不再在标题右侧塞一块半透明黑底，标题区也就有和 CLU 面板同样的呼吸。
        _status = new CardStatus
        {
            ShowIcon = false,
            Visibility = Visibility.Collapsed
        };

        _metaFooter = new CardFooter { Visibility = Visibility.Collapsed };

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_header);
        Children.Add(_rule);
        Children.Add(_hostFrame);
        Children.Add(_status);
        Children.Add(_metaFooter);
        SetRow(_header, 0);
        SetRow(_rule, 1);
        SetRow(_hostFrame, 2);
        SetRow(_status, 3);
        SetRow(_metaFooter, 4);

        ApplyChrome();

        Loaded += (s, e) =>
        {
            TryStartVideo();
            UpdateVideoPlacement();
        };
        LayoutUpdated += (s, e) =>
        {
            // 首次 Loaded 时卡片窗口可能还没真正 Loaded，这里补一次机会。
            if (_pendingVideo is not null && _videoWindow is null && _player is null) TryStartVideo();
            UpdateVideoPlacement();
        };
        Unloaded += (s, e) => { if (_videoWindow is not null) _videoWindow.Stop(); };

        // 只在媒体区域真的变尺寸时重算圆角裁剪。
        // 放进 LayoutUpdated 会变成「设 Clip → 重绘 → 再布局」的循环，白烧 CPU。
        _host.SizeChanged += (s, e) => ApplyClip();
    }

    /// <summary>媒体尺寸变化（视频打开、图片解码完成）时通知宿主重新排布叠层。</summary>
    public event Action LayoutChanged;

    public string LoadedPath => _loadedPath;
    public bool HasVideo => _videoWindow is not null || _player is not null || _pendingVideo is not null;

    /// <summary>当前绑定的媒体路径（截图 / 自检读取用；空 = 没选文件）。</summary>
    public string MediaPath => _settings is null ? "" : (_settings.Path ?? "");

    public CardMediaView WithCornerRadius(double radius)
    {
        _cornerRadius = Math.Max(0, Math.Min(60, radius));
        ApplyClip();
        return this;
    }

    /// <summary>用全局外观（字体 / 颜色 / 内边距）画文件名与进度条，和文字卡片保持一致。</summary>
    public CardMediaView WithStyle(StyleSettings style)
    {
        _style = style;
        ApplyChrome();
        return this;
    }

    /// <summary>
    /// 主题 / 字号 / 圆角变化时就地换肤：不重建控件，所以视频不会重启、动图不会回到第一帧。
    /// 只重算 chrome（文件名 / 进度条）与圆角裁剪，代价是一次布局，而不是一次解码。
    /// </summary>
    public void ApplyStyle(StyleSettings style, double cornerRadius)
    {
        _cornerRadius = Math.Max(0, Math.Min(60, cornerRadius));
        ApplyClip();
        WithStyle(style);
    }

    void ApplyChrome()
    {
        StyleSettings style = _style;
        ThemeTokens tokens = ThemeResolver.Resolve(style);

        // 比 2.35 更松一档：标题、画面、状态、页脚之间都留出独立间距，
        // 不再让文件名和画面贴在一起。夹在上下限内，Padding=0 时也不会挤没。
        _padX = Math.Max(12, (style is null ? 14 : style.Padding) * 1.1);
        _padY = Math.Max(10, (style is null ? 14 : style.Padding) * 0.85);

        Color foreground = ParseColor(style is null ? null : style.Foreground);
        if (foreground != _foreground) _foreground = foreground;

        _header.ApplyStyle(style, _cornerRadius);
        _header.Margin = new Thickness(_padX, Math.Max(8, _padY * 0.55), _padX, Math.Max(6, _padY * 0.35));

        _rule.Background = Solid(_foreground, 0.14);
        _rule.Margin = new Thickness(_padX, 0, _padX, Math.Max(8, _padY * 0.55));

        _hostFrame.Margin = new Thickness(_padX, 0, _padX, Math.Max(8, _padY * 0.6));
        _host.Margin = new Thickness(0);
        _status.Margin = new Thickness(_padX, 0, _padX, Math.Max(3, _padY * 0.22));
        _status.ApplyStyle(style);
        _status.TextFont = new FontFamily(tokens.MonoFont);
        _metaFooter.Margin = new Thickness(_padX, 4, _padX, Math.Max(10, _padY * 0.9));
        _metaFooter.ApplyStyle(style);
        _brackets.ApplyStyle(Solid(_foreground, 0.62).Color);
        _message.Foreground = Solid(_foreground, 0.55);
        if (_message.Visibility == Visibility.Visible)
            _brackets.Visibility = EndfieldVisuals.CornerBrackets(style)
                ? Visibility.Visible : Visibility.Collapsed;

        UpdateStatus();
        ApplySize();
    }

    static SolidColorBrush Solid(Color color, double alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    static Color ParseColor(string text)
    {
        Color fallback = Color.FromRgb(0xE6, 0xED, 0xF3);
        return ColorCode.TryParse(text, out var a, out var r, out var g, out var b)
            ? Color.FromArgb(a, r, g, b)
            : fallback;
    }

    /// <summary>M6：卡片模块注入面包屑与系统页脚；标题由 CardHeader 自己排版。</summary>
    public void SetMeta(string code, string footer)
    {
        _metaCode = code ?? "";
        _header.Code = _metaCode;
        _metaFooter.Left = footer ?? "";
        _metaFooter.ApplyStyle(_style);
        _metaFooter.Visibility = string.IsNullOrEmpty(_metaFooter.Left)
            ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrEmpty(_loadedPath)) RefreshName(_loadedPath);
    }

    void RefreshName(string path)
    {
        _header.Title = FileName(path);
        _header.ToolTip = path;
        _header.Badge = KindLabel(path);
    }

    static string KindLabel(string path)
    {
        string extension = SafeExtension(path);
        if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)) return "动图";
        return IsVideo(path) ? "视频" : "图片";
    }

    // ==================== 绑定 ====================

    /// <summary>按配置装载媒体；同一路径只解码 / 打开一次。</summary>
    public void Bind(MediaSettings settings)
    {
        if (_disposed) return;

        _settings = settings ?? new MediaSettings();
        _host.Opacity = _settings.Opacity;

        string path = _settings.Path ?? "";
        bool samePath = _loadedPath is not null &&
                        string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase);

        if (_loadedPath is not null && !samePath) Unload();

        if (path.Length == 0)
        {
            ShowMessage("媒体卡片\n在「设置 → 内容 → 媒体」里选择图片 / 动图 / 视频");
            _header.Title = "媒体";
            _header.Badge = "未选择";
            _header.Visibility = Visibility.Visible;
            _rule.Visibility = Visibility.Visible;
            _status.Visibility = Visibility.Collapsed;
            _status.SetProgress(null);
            _metaFooter.Visibility = string.IsNullOrEmpty(_metaFooter.Left)
                ? Visibility.Collapsed : Visibility.Visible;
            _loadedPath = "";
            ApplySize();
            return;
        }

        RefreshName(path);
        _header.Visibility = Visibility.Visible;
        _rule.Visibility = Visibility.Visible;
        _message.Visibility = Visibility.Collapsed;
        _brackets.Visibility = Visibility.Collapsed;
        _metaFooter.Visibility = string.IsNullOrEmpty(_metaFooter.Left)
            ? Visibility.Collapsed : Visibility.Visible;

        if (!File.Exists(path))
        {
            ShowMessage("找不到文件：\n" + path);
            _loadedPath = path;
            ApplySize();
            return;
        }

        bool video = IsVideo(path);
        _status.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        _status.SetProgress(video ? 0 : null);
        _brackets.Visibility = Visibility.Collapsed;
        _speed = _settings.Speed > 0 ? _settings.Speed : 1;
        // 先给出一行 00:00 / 00:00；真正的时长等 MediaOpened / ffmpeg 启动后由 StartProgress 刷新。
        UpdateStatus();

        if (samePath)
        {
            // 同一条媒体重新套设置（改名字、调不透明度、换字号之后宿主会走 UpdateContent）：
            // 保留用户按热键暂停 / 播放过的状态，不要退回设置里的 Autoplay。
            bool wasPlaying = _playing;

            // K-11：用"已应用值"快照判断解码器是否真的变了；previous 与 _settings
            // 常常是同一个对象，引用比较永远为 false（换解码器会失效）。
            string decoder = MediaSettings.NormalizeDecoder(_settings.Decoder);
            string decoderPath = _settings.DecoderPath ?? "";
            bool decoderChanged =
                !string.Equals(_appliedDecoder, decoder, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_appliedDecoderPath, decoderPath, StringComparison.OrdinalIgnoreCase);

            if (video && decoderChanged)
            {
                _appliedDecoder = decoder;
                _appliedDecoderPath = decoderPath;
                _videoOpacityFallback = false;

                // 审阅 §19：不能先把 _pendingVideo 交给 ReleaseVideo——它内部会清空 pending
                // 并 ++_videoRun，TryStartVideo 看到 _pendingVideo == null 就直接 return，
                // 视频会停在最后一帧且没有任何提示。先释放旧播放器，再把待启动路径交回启动流程。
                ReleaseVideo();
                _pendingVideo = path;
                TryStartVideo();

                // 换解码器必然重开视频（Open / StartFfmpeg 会按 Autoplay 起播），这里把状态还原回去。
                // StartFfmpeg 是异步的，Play/Pause 在 player 建好前调用不生效；
                // StartFfmpeg 里会再按 _playing 校正一次。
                if (wasPlaying) Play();
                else Pause();
            }

            ApplyVideoSettings();
            ApplyFit();
            _host.Opacity = _settings.Opacity;
            ApplyVideoOpacity();
            ApplySize();
            return;
        }

        _playing = _settings.Autoplay;
        _loadedPath = path;
        _message.Visibility = Visibility.Collapsed;

        // K-11：新一轮媒体直接按当前设置起播，所以快照就是当前值。
        _appliedDecoder = MediaSettings.NormalizeDecoder(_settings.Decoder);
        _appliedDecoderPath = _settings.DecoderPath ?? "";
        _appliedSpeed = _settings.Speed > 0 ? _settings.Speed : 1.0;
        _appliedMuted = _settings.Muted;
        _appliedLoop = _settings.Loop;
        _appliedAutoplay = _settings.Autoplay;
        _appliedValid = true;
        _mediaEnded = false;
        _videoOpacityFallback = false;
        _waitingForFfmpeg = false;
        _ffmpegRetryTask = null;
        _frameBitmap = null;

        if (video)
        {
            _pendingVideo = path;
            StartPoster(path);
            TryStartVideo();
        }
        else
        {
            LoadImage(path);
        }

        ApplySize();
    }

    static string FileName(string path)
    {
        try { return Path.GetFileName(path); }
        catch { return path; }
    }

    static bool IsVideo(string path) => Array.IndexOf(VideoExtensions, SafeExtension(path)) >= 0;

    static string SafeExtension(string path)
    {
        try { return (Path.GetExtension(path) ?? "").ToLowerInvariant(); }
        catch { return ""; }
    }

    // ==================== 图片 / 动图 ====================

    async void LoadImage(string path)
    {
        // K-12：解码放后台。WPF 的 BitmapDecoder 在 MTA 线程可用，Freeze() 后能跨线程
        // 交给 UI；旧实现整张图在 UI 线程同步解，4K 图 / 长 GIF 会把整个 HUD 冻住。
        int run = ++_imageRun;

        bool isGif = string.Equals(SafeExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);
        if (isGif && _settings.Loop)
        {
            // K-12：GIF 只解一次。旧实现先用 BitmapDecoder 解首帧、再让 GifAnimator 用
            // GDI+ 解全部帧，等于两遍全尺寸解码；现在直接交给 GifAnimator。
            GifAnimator gif = await Task.Run(() => GifAnimator.TryLoad(path, true));
            if (!IsCurrentImage(path, run))
            {
                if (gif is not null) gif.Dispose();
                return;
            }

            if (gif is not null)
            {
                if (gif.CanvasSize.Width > 0 && gif.CanvasSize.Height > 0) _natural = gif.CanvasSize;
                gif.AttachTarget(_image);
                _image.Stretch = ParseStretch(_settings.Fit);
                _gif = gif;
                _gif.Play();
                ApplySize();
                return;
            }

            // 超预算 / 读取失败：GifAnimator 已把原因写进 LastSkipReason，退回静态首帧。
            if (!string.IsNullOrEmpty(GifAnimator.LastSkipReason))
            {
                _status.Visibility = Visibility.Visible;
                _status.SetText("动图过大，已仅显示首帧", CardStatusKind.Warning);
                _status.ToolTip = GifAnimator.LastSkipReason;
            }
        }

        BitmapSource still = await Task.Run(() => TryLoadBitmap(path));
        if (!IsCurrentImage(path, run)) return;

        // WPF 解不开的格式（webp / avif / heic …）交给 ffmpeg 转一帧出来。
        // P1-8：走异步 + 整体超时 + Kill(true)，ffmpeg 卡住时不再冻住 UI。
        if (still is null)
        {
            string ffmpeg = Decoders.FindFfmpeg(_settings);
            // K-15：首次查找在后台进行；这里最多等 3 秒拿到结果，避免"刚开机第一张 webp
            // 被误判为无法解码"。超时按没有 ffmpeg 处理（日志里已有原因）。
            if (ffmpeg is null) ffmpeg = await Decoders.WaitForFfmpegAsync(3000);
            if (!IsCurrentImage(path, run)) return;

            if (ffmpeg is not null)
            {
                Log.Info($"系统解不开图片，改用 ffmpeg：{path}");
                still = await FfmpegPlayer.ExtractImageAsync(ffmpeg, path);

                // await 期间可能已经换了文件 / 卡片被删。
                if (!IsCurrentImage(path, run)) return;
            }
        }

        if (still is null)
        {
            ShowMessage("无法解码这个文件：\n" + FileName(path) +
                        (Decoders.FindFfmpeg(_settings) is null ? "\n（可在设置里指定 ffmpeg 解码器）" : ""));
            return;
        }

        _natural = new Size(still.PixelWidth, still.PixelHeight);
        _image.Source = still;
        _image.Stretch = ParseStretch(_settings.Fit);
        ApplySize();
    }

    /// <summary>K-12：代次 + 路径双校验；任一不符说明用户已经换了文件 / 释放了视图。</summary>
    bool IsCurrentImage(string path, int run) =>
        !_disposed && run == _imageRun &&
        string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// K-12：后台线程解图（只允许后台调用）。按像素预算降采样：媒体卡最宽 1280，
    /// 超过约 32MP 的源图纯属浪费内存（4K 照片 8MP 已经够用），更大的图先缩小再 Freeze，
    /// 避免一次解码吃掉几百 MB；降采样原因写日志，不静默改行为。
    /// </summary>
    static BitmapSource TryLoadBitmap(string path)
    {
        const long MaxPixels = 32L * 1000 * 1000;

        try
        {
            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count == 0) return null;

                BitmapSource frame = decoder.Frames[0];
                long pixels = (long)frame.PixelWidth * frame.PixelHeight;
                if (pixels > MaxPixels)
                {
                    double scale = Math.Sqrt((double)MaxPixels / pixels);
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    scaled.Freeze();
                    Log.Warn($"图片过大（{frame.PixelWidth}×{frame.PixelHeight}），已降采样到 {scaled.PixelWidth}×{scaled.PixelHeight} 以控制内存");
                    return scaled;
                }

                BitmapSource frozen = frame.Clone();
                frozen.Freeze();
                return frozen;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"解码图片失败: {ex.Message}");
            return null;
        }
    }

    // ==================== 视频 ====================

    void TryStartVideo()
    {
        if (_disposed || _pendingVideo is null) return;

        Window card = Window.GetWindow(this);
        if (card is null || !card.IsLoaded)
        {
            return;   // 等卡片窗口出来再建子窗口 / 开解码器
        }

        string ffmpeg = Decoders.FindFfmpeg(_settings);
        string decoder = MediaSettings.NormalizeDecoder(_settings.Decoder);
        string path = _pendingVideo;

        // K-15：首次查找 ffmpeg 已改成后台进行（FindFfmpeg 不再同步扫 PATH）。
        // 用户明确选了「仅 ffmpeg」时必须等它查完：此时落到系统解码基本放不了，
        // 表现就是"选了 ffmpeg 却打不开"。先显示加载态，查完自动重试一次。
        if (decoder == "FFmpeg" && ffmpeg is null)
        {
            Task<string> lookup = Decoders.EnsureFfmpegLookup();
            if (lookup.IsCompleted)
            {
                // 已经查完确实没有：明确告知，而不是永远停在"正在查找…"。
                // 必须清掉 _pendingVideo，否则 LayoutUpdated 会反复触发 TryStartVideo → 死循环。
                Log.Warn("选择了 ffmpeg 解码，但机器上找不到 ffmpeg.exe");
                _pendingVideo = null;
                ShowMessage("找不到 ffmpeg 解码器：\n请在设置里指定 ffmpeg.exe 路径");
                _waitingForFfmpeg = false;
                return;
            }

            if (!_waitingForFfmpeg)
            {
                _waitingForFfmpeg = true;
                Log.Warn("选择了 ffmpeg 解码，正在后台查找 ffmpeg.exe（首次查找不阻塞 UI）");
                _status.Visibility = Visibility.Visible;
                _status.SetText("正在查找 ffmpeg 解码器…", CardStatusKind.Normal);
            }

            RetryWhenFfmpegReady(path);
            return;
        }

        _waitingForFfmpeg = false;

        // 「自动」= 先让系统解（能硬解、有声音），解不开再在 OnVideoFailed 里换 ffmpeg；
        // 用户明确选了「仅 ffmpeg」才一开始就走外部解码。
        if (decoder == "FFmpeg" && ffmpeg is not null)
        {
            StartFfmpeg(ffmpeg, path);
            return;
        }

        try
        {
            if (_videoWindow is null)
            {
                _videoWindow = new VideoWindow(card);
                _videoWindow.Video.MediaOpened += OnVideoOpened;
                _videoWindow.Video.MediaFailed += OnVideoFailed;
                _videoWindow.Video.MediaEnded += OnVideoEnded;
                Log.Info("视频子窗已创建（Owner=卡片，鼠标穿透 / 不进 Alt+Tab）");
            }

            _videoWindow.Open(path, _settings);
            // P1-6：把“成功打开”的记录放到 Open 之后——万一 EnsureWindow / Uri 抛异常，
            // 日志顺序不该先说成功；原来的 INFO 还把文件路径写进了“创建”那条，语义含混。
            Log.Info($"视频已交给系统解码器打开：{path}");
            ApplyFit();
            ApplyVideoOpacity();   // K-10：把卡片不透明度同步到独立视频窗
            StartProgress();

            if (card.IsVisible) UpdateVideoPlacement();
        }
        catch (Exception ex)
        {
            // 打开失败是终态：清掉待启动路径，避免 LayoutUpdated 每次布局都重试出错。
            Log.Warn($"打开视频失败: {ex.Message}");
            _pendingVideo = null;
            ShowMessage("无法打开视频：\n" + ex.Message);
        }
    }

    /// <summary>用 ffmpeg 解出一帧当封面，先填住视频区域（异步，不阻塞卡片显示）。</summary>
    void StartPoster(string path)
    {
        string ffmpeg = Decoders.FindFfmpeg(_settings);
        if (ffmpeg is null) return;

        string captured = path;
        StartPosterAsync(ffmpeg, captured);
    }

    /// <summary>
    /// P1-8：解封面完全在后台完成，ffmpeg 有整体超时 + Kill(true)，
    /// 结果再回 Dispatcher 上贴；卡住最多是封面晚到 / 不到，不影响 UI。
    /// </summary>
    async void StartPosterAsync(string ffmpeg, string captured)
    {
        BitmapSource frame = await FfmpegPlayer.ExtractImageAsync(ffmpeg, captured);
        if (frame is null) return;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed || _image.Source is not null) return;
            if (!string.Equals(_loadedPath, captured, StringComparison.OrdinalIgnoreCase)) return;
            _image.Source = frame;
            _message.Visibility = Visibility.Collapsed;
        }));
    }

    /// <summary>
    /// 同步取一帧当封面（离屏截图用：视频没有窗口可渲染）。
    /// 只给截图 / 命令行这类非交互路径用；卡片内的 UI 线程路径走 StartPosterAsync ——
    /// 这个方法的同步等待最长就是 ExtractImage 的 6s 整体超时（P1-8 已保证可超时）。
    /// </summary>
    public void ShowPoster(string path)
    {
        if (_disposed) return;

        string ffmpeg = Decoders.FindFfmpeg(_settings);
        if (ffmpeg is null) return;

        BitmapSource frame = FfmpegPlayer.ExtractImage(ffmpeg, path);
        if (frame is null) return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _image.Source = frame;
                _message.Visibility = Visibility.Collapsed;
            }));
            return;
        }

        _image.Source = frame;
        _message.Visibility = Visibility.Collapsed;
    }

    /// <summary>这个路径要不要走视频分支（宿主 / 截图工具复用）。</summary>
    public static bool IsVideoPath(string path) => IsVideo(path);

    /// <summary>
    /// 用 ffmpeg 解码：帧直接画在卡片里，不需要子窗口。
    /// P1-8：探测走 ProbeAsync（整体超时 + Kill(true)），原来在 UI 线程同步 Probe，
    /// ffmpeg 卡死（损坏容器 / 杀软拦截 / 离线网络盘）时整个 HUD 一起冻住。
    /// </summary>
    async void StartFfmpeg(string ffmpeg, string path)
    {
        try
        {
            ReleaseVideo();

            // 探测代号：ReleaseVideo 会 ++，所以 await 回来时代号变了就说明
            // 视频已经被释放 / 换了别的文件，本次结果必须丢弃。
            int run = ++_videoRun;

            VideoInfo info = await FfmpegPlayer.ProbeAsync(ffmpeg, path);

            if (_disposed || run != _videoRun) return;

            double aspect = info.Width > 0 && info.Height > 0 ? (double)info.Height / info.Width : 9.0 / 16;
            _natural = new Size(info.Width > 0 ? info.Width : 1280, info.Height > 0 ? info.Height : 720);

            _player = new FfmpegPlayer();
            // K-16：解码器只发"有新帧"的合并通知（同一时刻最多一次），这里用 Render
            // 优先级回调取最新帧写进复用的 WriteableBitmap，Dispatcher 队列不会堆积。
            _player.FrameAvailable += () =>
                Dispatcher.BeginInvoke(new Action(DisplayFrame), DispatcherPriority.Render);
            _player.Failed += message =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowMessage("ffmpeg 解码失败：\n" + message);
                }));
            };
            _player.AudioUnavailable += message =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed || _player is null) return;
                    // P1-6：音频起不来时视频照播，但状态行必须如实标注，
                    // 不能让用户以为是自己关了声音。
                    _audioNote = string.IsNullOrEmpty(message) ? "音频不可用" : $"音频不可用（{message}）";
                    UpdateStatus();
                }));
            };
            // K-6：运行期音频死亡（热拔 / 写失败）与"起不来"分开提示，否则用户只会
            // 看到视频继续播、状态行毫无变化。
            _player.AudioFailed += message =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed || _player is null) return;
                    _audioNote = string.IsNullOrEmpty(message) ? "音频中断" : $"音频中断（{message}）";
                    UpdateStatus();
                }));
            };
            // K-7：非循环视频自然播完：播放器状态机已收尾，卡片按钮 / 状态行也要跟上。
            _player.Completed += () =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed || _player is null) return;
                    _playing = false;
                    _mediaEnded = true;
                    // 播完不需要每 250ms 再刷一次进度；下次 Play 会重新启动计时器。
                    if (_progressTimer is not null) _progressTimer.Stop();
                    UpdateStatus();
                }), DispatcherPriority.Background);
            };

            _audioNote = null;   // 新一轮播放重置上一轮的音频备注
            _mediaEnded = false;
            _frameBitmap = null; // 换播放器/换文件：帧缓冲必须重建（尺寸可能不同）

            ApplySize();

            int width = DecodeWidth(aspect);
            int height = Math.Max(16, (int)Math.Round(width * aspect));

            _image.Stretch = ParseStretch(_settings.Fit);
            _image.Source = null;

            _player.Start(ffmpeg, path, width, height, info.Fps,
                          _settings.Loop, info.Duration, _settings.Autoplay,
                          _speed, 0, !_settings.Muted, 100);

            // 探测是异步的：Bind / 解码器切换时调用的 Play()/Pause() 那时 _player 还是 null，
            // 不会生效。这里按当前用户状态校正一次，避免换解码器后暂停的视频自己播起来
            // （或正在播的视频被 Autoplay=false 卡住）。
            if (_playing) _player.Resume();
            else _player.Pause();

            // K-11：起播用的就是当前设置值，记进"已应用"快照，之后同路径 Bind 才能正确求差。
            _appliedValid = true;
            _appliedSpeed = _speed;
            _appliedMuted = _settings.Muted;
            _appliedLoop = _settings.Loop;
            _appliedAutoplay = _settings.Autoplay;
            _appliedDecoder = DecoderKey();
            _appliedDecoderPath = _settings.DecoderPath ?? "";

            Log.Info($"ffmpeg 解码：{FileName(path)} {width}×{height} {info.Fps:0.##}fps");
            StartProgress();
        }
        catch (Exception ex)
        {
            Log.Warn($"ffmpeg 解码启动失败: {ex.Message}");
            ShowMessage("ffmpeg 解码启动失败：\n" + ex.Message);
        }
    }

    int DecodeWidth(double aspect)
    {
        double dpi = 1;
        try { dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; }
        catch (Exception ex)
        {
            // 取不到 DPI 按 100% 处理即可（解码宽度只是视觉尺寸），留痕便于排查显示异常。
            Log.Warn($"读取卡片 DPI 失败，解码宽度按 100% 计算: {ex.Message}");
        }

        double mediaWidth = _settings.Width > 0 ? _settings.Width : 320;
        int width = (int)Math.Round(mediaWidth * (dpi > 0.1 ? dpi : 1));

        width = (int)Math.Max(160, Math.Min(1280, width));
        return width - width % 2;
    }

    /// <summary>
    /// K-16：把 ffmpeg 的最新一帧写进复用缓冲。只有帧尺寸变化才重建 WriteableBitmap，
    /// 中间帧由解码器的"最新一帧"槽位直接丢弃，所以这里永远是当前最新画面。
    /// </summary>
    void DisplayFrame()
    {
        FfmpegPlayer player = _player;
        if (_disposed || player is null) return;

        VideoFrame frame = player.TakeFrame();
        if (frame is null)
        {
            // 这一代已经被替换 / 停止：复位通知标志，避免后续帧再也通知不到。
            player.SkipFrame();
            return;
        }

        try
        {
            if (_frameBitmap is null ||
                _frameBitmap.PixelWidth != frame.Width ||
                _frameBitmap.PixelHeight != frame.Height)
            {
                _frameBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96,
                                                   PixelFormats.Bgra32, null);
                _image.Source = _frameBitmap;
            }

            _frameBitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height),
                                     frame.Buffer, frame.Width * 4, 0);
            _message.Visibility = Visibility.Collapsed;
            _image.Stretch = ParseStretch(_settings.Fit);
        }
        catch (Exception ex)
        {
            Log.Warn($"显示视频帧失败: {ex.Message}");
        }
        finally
        {
            // 无论成功与否都要归还缓冲，否则帧池（3 块）很快耗尽，画面会停住。
            player.ReleaseFrame(frame);
        }
    }

    /// <summary>当前解码器设置的归一化键（K-11 差量用，避免依赖对象同一性）。</summary>
    string DecoderKey() => MediaSettings.NormalizeDecoder(_settings.Decoder);

    void OnVideoOpened(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_videoWindow is null) return;

            double w = _videoWindow.Video.NaturalVideoWidth;
            double h = _videoWindow.Video.NaturalVideoHeight;
            if (w > 0 && h > 0)
            {
                _natural = new Size(w, h);
                ApplySize();
            }

            UpdateVideoPlacement();
            StartProgress();
        }
        catch (Exception ex)
        {
            Log.Warn($"视频打开后处理失败: {ex.Message}");
        }
    }

    void OnVideoFailed(object sender, ExceptionRoutedEventArgs e)
    {
        string reason = e is not null && e.ErrorException is not null
            ? e.ErrorException.Message
            : "系统缺少这个视频格式的解码器";
        Log.Warn($"视频播放失败: {reason}");

        string ffmpeg = Decoders.FindFfmpeg(_settings);
        string decoder = MediaSettings.NormalizeDecoder(_settings.Decoder);

        // K-15：系统解码失败时 ffmpeg 可能还在后台查找中；不要直接报"没解码器"，
        // 显示加载态并等查找完成后再试一次（否则第一张 mkv 会假失败）。
        if (ffmpeg is null && decoder != "BuiltIn" && _pendingVideo is not null)
        {
            Task<string> lookup = Decoders.EnsureFfmpegLookup();
            if (!lookup.IsCompleted)
            {
                Log.Info("系统解码失败，后台仍在查找 ffmpeg，待查找完成后重试");
                _status.Visibility = Visibility.Visible;
                _status.SetText("正在查找 ffmpeg 解码器…", CardStatusKind.Normal);
                RetryWhenFfmpegReady(_pendingVideo);
                return;
            }
        }

        // 「自动」模式：系统放不了就换 ffmpeg，用户不用自己判断格式。
        if (ffmpeg is not null && decoder != "BuiltIn" && _pendingVideo is not null)
        {
            Log.Info($"系统解码失败，改用 ffmpeg：{FileName(_pendingVideo)}");
            StartFfmpeg(ffmpeg, _pendingVideo);
            return;
        }

        // 给了最终提示：清掉待启动路径，别让 LayoutUpdated 反复重试（用户已经看到失败原因）。
        _pendingVideo = null;
        ShowMessage("无法播放这个视频：\n" + reason +
                    (ffmpeg is null ? "\n（可在设置里指定 ffmpeg 解码器）" : ""));
    }

    void OnVideoEnded(object sender, RoutedEventArgs e)
    {
        if (_videoWindow is null) return;

        // K-7（系统解码路径）：循环就重播；非循环要把卡片状态收尾成"已结束"，
        // 否则状态行一直显示 ▶，播放键按下去的第一次是"暂停"而不是重播。
        if (_settings.Loop)
        {
            try
            {
                _videoWindow.Video.Position = TimeSpan.Zero;
                _videoWindow.Video.Play();
            }
            catch (Exception ex)
            {
                Log.Warn($"系统解码视频循环重播失败: {ex.Message}");
            }
            return;
        }

        _playing = false;
        _mediaEnded = true;
        if (_progressTimer is not null) _progressTimer.Stop();
        UpdateStatus();
    }

    /// <summary>
    /// 把设置里的静音 / 速度 / 循环 / 自动播放同步到当前播放器。
    ///
    /// K-11：这里绝对不能拿 "previous vs _settings" 的引用差量来判变化 ——
    /// MediaCard 用 <c>ExtrasAs&lt;MediaSettings&gt;</c> 取设置，每次返回同一个实例，
    /// previous 与 _settings 引用相等，所有差量都会判成"没变"，L-4 的修复被完全抵消。
    /// 改为与"已应用值"快照比较：快照是真实的 bool/double/string 值，不依赖对象同一性。
    /// </summary>
    void ApplyVideoSettings()
    {
        double speed = _settings.Speed > 0 ? _settings.Speed : 1.0;
        bool speedChanged = !_appliedValid || Math.Abs(_appliedSpeed - speed) > 0.001;
        bool mutedChanged = !_appliedValid || _appliedMuted != _settings.Muted;
        bool loopChanged = !_appliedValid || _appliedLoop != _settings.Loop;
        bool autoplayChanged = !_appliedValid || _appliedAutoplay != _settings.Autoplay;

        // ffmpeg 路径没有 _videoWindow；设置页改静音 / 速度 / 循环 / 自动播放时，
        // 以前这里直接 return，用户看到“保存成功”但播放行为完全没变（L-4）。
        if (_videoWindow is null)
        {
            if (_player is not null)
            {
                try
                {
                    if (speedChanged)
                    {
                        _speed = speed;
                        _player.SetSpeed(speed);
                    }

                    if (mutedChanged) _player.SetMuted(_settings.Muted);
                    if (loopChanged) _player.SetLoop(_settings.Loop);

                    if (autoplayChanged)
                    {
                        if (_settings.Autoplay) Play();
                        else Pause();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"更新 ffmpeg 视频设置失败: {ex.Message}");
                }
            }
        }
        else
        {
            try
            {
                _videoWindow.Video.IsMuted = _settings.Muted;
                _videoWindow.Video.Volume = _settings.Muted ? 0 : 1;
                _videoWindow.Video.SpeedRatio = speed;

                if (speedChanged) _speed = speed;

                if (autoplayChanged)
                {
                    // 走 Play / Pause 而不是直接动 Video：_playing 和底部状态行要一起更新，
                    // 否则「设置里改了自动播放」之后按钮显示的状态会和画面不一致。
                    if (_settings.Autoplay) Play();
                    else Pause();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"更新视频设置失败: {ex.Message}");
            }
        }

        // 记录"已应用值"：即使当前没有播放器也记下来（StartFfmpeg 会按 _settings 起播）。
        _appliedSpeed = speed;
        _appliedMuted = _settings.Muted;
        _appliedLoop = _settings.Loop;
        _appliedAutoplay = _settings.Autoplay;
        _appliedValid = true;
    }

    /// <summary>把视频子窗口贴到媒体区域在屏幕上的位置（卡片拖动、换排布时都会重新算）。</summary>
    void UpdateVideoPlacement()
    {
        if (_videoWindow is null || _disposed) return;

        Window card = Window.GetWindow(this);
        if (card is null || !card.IsVisible) return;
        if (_host.ActualWidth < 8 || _host.ActualHeight < 8) return;

        try
        {
            Point origin = _host.PointToScreen(new Point(0, 0));
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double sx = dpi.DpiScaleX > 0.1 ? dpi.DpiScaleX : 1;
            double sy = dpi.DpiScaleY > 0.1 ? dpi.DpiScaleY : 1;

            double radius = _settings is not null && _settings.Rounded ? _cornerRadius : 0;
            _videoWindow.Place(origin.X / sx, origin.Y / sy, _host.ActualWidth, _host.ActualHeight, radius);
        }
        catch (Exception ex)
        {
            Log.Warn($"定位视频窗失败: {ex.Message}");
        }
    }

    /// <summary>
    /// K-15：ffmpeg 首次查找在后台进行，查完后重试一次启动。
    /// 只挂一次 ContinueWith，避免 LayoutUpdated 反复触发时堆出一串重试。
    /// </summary>
    void RetryWhenFfmpegReady(string path)
    {
        Task<string> lookup = Decoders.EnsureFfmpegLookup();
        if (lookup.IsCompleted) return;   // 已经查完还是没找到：不再重试（否则会死循环）
        if (ReferenceEquals(_ffmpegRetryTask, lookup)) return;

        _ffmpegRetryTask = lookup;
        _ = lookup.ContinueWith(_ =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                if (!string.Equals(_pendingVideo, path, StringComparison.OrdinalIgnoreCase)) return;
                _waitingForFfmpeg = false;
                TryStartVideo();
            }));
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// K-10：把媒体不透明度同步到独立视频子窗口（系统解码路径）。
    /// 优先分层窗口整窗 alpha；不支持时降级到 WPF 合成路径（ffmpeg 逐帧 / 静态封面），
    /// 并把原因写日志 + 状态行 —— 不允许"视频永远不透明"这种静默不一致。
    /// 需真机验证：不同 GPU / 远程桌面下分层窗口与 MediaElement 的合成表现。
    /// </summary>
    void ApplyVideoOpacity()
    {
        if (_videoWindow is null || _disposed) return;

        double opacity = _settings is null ? 1 : _settings.Opacity;
        bool supported = _videoWindow.SetOpacity(opacity);
        if (supported || opacity >= 0.999)
        {
            _videoOpacityFallback = false;
            return;
        }

        if (_videoOpacityFallback) return;
        _videoOpacityFallback = true;
        Log.Warn($"系统解码的视频窗不支持半透明合成（不透明度 {opacity:0.##}），改用 WPF 合成路径");
        FallbackForOpacity();
    }

    /// <summary>
    /// K-10 降级：独立视频子窗口无法半透明时，改走"帧画在卡片里"的 ffmpeg 路径
    /// （帧由 WPF 合成，跟随 _host.Opacity）；没有 ffmpeg 则退成静态封面并明确提示。
    /// </summary>
    void FallbackForOpacity()
    {
        string path = _loadedPath;
        if (string.IsNullOrEmpty(path)) return;

        string ffmpeg = Decoders.FindFfmpeg(_settings);
        if (ffmpeg is not null && DecoderKey() != "BuiltIn" && File.Exists(path))
        {
            Log.Info("为保持不透明度一致，视频改用 ffmpeg 逐帧合成");
            StartFfmpeg(ffmpeg, path);
            return;
        }

        // 没有可用的 ffmpeg：系统解码做不到半透明，宁可显示半透明封面也不要显示全不透明视频。
        ReleaseVideo();
        ShowMessage("系统解码不支持半透明视频\n已降级为静态封面");
        StartPoster(path);
    }

    // ==================== 进度条 ====================

    void StartProgress()
    {
        if (_progressTimer is null)
        {
            _progressTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _progressTimer.Tick += (s, e) =>
            {
                UpdateProgress();
                UpdateStatus();
            };
        }

        _status.Visibility = Visibility.Visible;
        _status.SetProgress(0);
        if (!_progressTimer.IsEnabled) _progressTimer.Start();
        UpdateProgress();
        UpdateStatus();
    }

    void UpdateProgress()
    {
        if (_disposed) return;

        // K-13：文件被删除 / 移走时不能一直显示最后一帧；停止并给出可见状态。
        if (!string.IsNullOrEmpty(_loadedPath) && (DateTime.UtcNow - _lastFileCheck).TotalSeconds >= 2)
        {
            _lastFileCheck = DateTime.UtcNow;
            if (!File.Exists(_loadedPath))
            {
                string gone = _loadedPath;
                Unload();
                ShowMessage("文件已被移动或删除：\n" + FileName(gone));
                return;
            }
        }

        double position = 0;
        double duration = 0;

        if (_player is not null)
        {
            position = _player.Position;
            duration = _player.Duration;
        }
        else if (_videoWindow is not null)
        {
            try
            {
                position = _videoWindow.Video.Position.TotalSeconds;
                Duration natural = _videoWindow.Video.NaturalDuration;
                if (natural.HasTimeSpan) duration = natural.TimeSpan.TotalSeconds;
                MediaStateThrottle.Success();
            }
            catch (Exception ex)
            {
                // 与 UpdateStatus 共用节流器：窗口关闭 / 换源瞬间抛异常是良性的，
                // 但持续失败必须能从日志看出来（X-9）。
                MediaStateThrottle.Failure(ex.Message);
            }
        }

        double fraction = duration > 0.05 ? Math.Min(1, Math.Max(0, position / duration)) : 0;
        _status.SetProgress(fraction);
    }

    // ==================== 尺寸 ====================

    /// <summary>按卡片圆角裁掉媒体区域的四个角（关闭「圆角」时保持方角）。</summary>
    void ApplyClip()
    {
        if (_host is null) return;

        if (_settings is null || !_settings.Rounded)
        {
            _host.Clip = null;
            return;
        }

        double width = _host.ActualWidth;
        double height = _host.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double radius = Math.Max(0, Math.Min(_cornerRadius, Math.Min(width, height) / 2));

        // 尺寸 / 圆角没变就别换 Geometry：每次换都会让 WPF 重新渲染一遍。
        if (Math.Abs(_clipWidth - width) < 0.01 && Math.Abs(_clipHeight - height) < 0.01 &&
            Math.Abs(_clipRadius - radius) < 0.01 && _host.Clip is not null)
            return;

        _clipWidth = width;
        _clipHeight = height;
        _clipRadius = radius;
        _host.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }

    void ApplyFit()
    {
        Stretch stretch = ParseStretch(_settings.Fit);
        _image.Stretch = stretch;
        if (_videoWindow is not null) _videoWindow.Video.Stretch = stretch;
    }

    void ApplySize()
    {
        if (_disposed) return;

        double width;
        if (_settings.Width > 0) width = _settings.Width;
        else if (_natural.Width > 0) width = _natural.Width;
        else width = 320;

        width = Math.Max(40, Math.Min(3000, width));

        double height;
        if (_settings.Height > 0) height = _settings.Height;
        else if (_natural.Width > 0 && _natural.Height > 0) height = width * _natural.Height / _natural.Width;
        else height = width * 9 / 16.0;

        height = Math.Max(20, Math.Min(3000, height));

        Width = width + _padX * 2;
        _host.Width = width;
        _host.Height = height;

        ApplyClip();

        Action handler = LayoutChanged;
        if (handler is not null) handler();
    }

    static Stretch ParseStretch(string fit)
    {
        switch (MediaSettings.NormalizeFit(fit))
        {
            case "Cover": return Stretch.UniformToFill;
            case "Fill": return Stretch.Fill;
            case "None": return Stretch.None;
            default: return Stretch.Uniform;
        }
    }

    void ShowMessage(string text)
    {
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
        _image.Source = null;
        _status.SetProgress(null);
        _status.Visibility = Visibility.Collapsed;
        _brackets.Visibility = EndfieldVisuals.CornerBrackets(_style)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== 播放控制 ====================

    public void Play()
    {
        if (_disposed) return;
        try
        {
            if (_gif is not null) _gif.Play();

            if (_player is not null)
            {
                // K-7：已播完时 FfmpegPlayer.Resume 会从头重开一代，播放键因此恢复可用；
                // 进度计时器在 Completed 时被停掉，这里重新启动。
                _player.Resume();
                _mediaEnded = false;
                RestartProgressTimer();
            }

            if (_videoWindow is not null)
            {
                // K-7（系统解码路径）：自然播完后 Position 停在末尾，直接 Play() 不会动。
                if (_mediaEnded)
                {
                    try { _videoWindow.Video.Position = TimeSpan.Zero; }
                    catch (Exception ex) { Log.Warn($"重播前重置视频位置失败: {ex.Message}"); }
                    _mediaEnded = false;
                    RestartProgressTimer();
                }

                UpdateVideoPlacement();
                _videoWindow.Play();
                UpdateVideoPlacement();
            }

            _playing = true;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            Log.Warn($"媒体播放失败: {ex.Message}");
        }
    }

    /// <summary>播完后进度计时器会被停掉；再次播放时按需恢复（K-7）。</summary>
    void RestartProgressTimer()
    {
        if (_progressTimer is not null && !_progressTimer.IsEnabled) _progressTimer.Start();
    }

    public void Pause()
    {
        if (_disposed) return;
        try
        {
            if (_gif is not null) _gif.Stop();
            if (_player is not null) _player.Pause();
            if (_videoWindow is not null) _videoWindow.Pause();
        }
        catch (Exception ex)
        {
            Log.Warn($"媒体暂停失败: {ex.Message}");
        }
        finally
        {
            // 暂停失败也要让状态行和 _playing 保持一致，不能停在旧值。
            _playing = false;
            UpdateStatus();
        }
    }

    // ==================== 热键控制 ====================

    /// <summary>媒体卡片的通用热键：播放 / 暂停、前进、后退、加速、减速。</summary>
    public void HandleHotkey(string action)
    {
        switch (action)
        {
            case "media.play-pause":
                TogglePlayPause();
                break;
            case "media.forward":
                SeekBy(5);
                break;
            case "media.backward":
                SeekBy(-5);
                break;
            case "media.speed-up":
                ChangeSpeed(0.25);
                break;
            case "media.speed-down":
                ChangeSpeed(-0.25);
                break;
        }
    }

    public void TogglePlayPause()
    {
        if (_disposed) return;

        try
        {
            if (_player is not null)
            {
                // K-7：已播完时 Resume 是"从头重播"，不是"置一个没人等的事件"。
                if (_player.IsEnded || _player.IsPaused)
                {
                    _player.Resume();
                    _playing = true;
                    _mediaEnded = false;
                    RestartProgressTimer();
                }
                else
                {
                    _player.Pause();
                    _playing = false;
                }
            }
            else if (_videoWindow is not null)
            {
                if (_playing)
                {
                    _videoWindow.Pause();
                    _playing = false;
                }
                else
                {
                    if (_mediaEnded)
                    {
                        try { _videoWindow.Video.Position = TimeSpan.Zero; }
                        catch (Exception ex) { Log.Warn($"重播前重置视频位置失败: {ex.Message}"); }
                        _mediaEnded = false;
                        RestartProgressTimer();
                    }

                    _videoWindow.Play();
                    _playing = true;
                }
            }
            else if (_gif is not null)
            {
                if (_playing) _gif.Stop();
                else _gif.Play();
                _playing = !_playing;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"切换播放状态失败: {ex.Message}");
        }

        UpdateStatus();
    }

    /// <summary>相对当前位置快进 / 快退（负数是后退）。</summary>
    public void SeekBy(double seconds)
    {
        if (_disposed) return;

        try
        {
            if (_player is not null)
            {
                _player.SeekBy(seconds);
            }
            else if (_videoWindow is not null)
            {
                MediaElement video = _videoWindow.Video;
                double duration = 0;
                try
                {
                    Duration natural = video.NaturalDuration;
                    if (natural.HasTimeSpan) duration = natural.TimeSpan.TotalSeconds;
                }
                catch (Exception ex)
                {
                    // 拿不到时长时按"没有上限"跳转即可（位置仍会被 MediaElement 自己夹断），
                    // 但读取失败的原因要留痕（X-9）。
                    Log.Warn($"读取视频时长失败，跳转不做上限夹取: {ex.Message}");
                }

                double target = video.Position.TotalSeconds + seconds;
                if (target < 0) target = 0;
                if (duration > 0 && target > duration - 0.1) target = Math.Max(0, duration - 0.1);
                video.Position = TimeSpan.FromSeconds(target);

                // K-7：用户主动跳转说明不想再停在"已结束"，清掉状态并让播放键接着当前位置走。
                _mediaEnded = false;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"视频跳转失败: {ex.Message}");
        }

        UpdateProgress();
    }

    /// <summary>调整播放速度（±0.25×，夹在 0.25–4）。</summary>
    public void ChangeSpeed(double delta)
    {
        if (_disposed) return;

        double next = Math.Max(0.25, Math.Min(4.0, _speed + delta));
        next = Math.Round(next * 100) / 100;
        if (Math.Abs(next - _speed) < 0.001) return;

        _speed = next;

        try
        {
            if (_player is not null) _player.SetSpeed(next);
            else if (_videoWindow is not null) _videoWindow.Video.SpeedRatio = next;
        }
        catch (Exception ex)
        {
            Log.Warn($"调整播放速度失败: {ex.Message}");
        }

        UpdateStatus();
    }

    // ==================== 状态行 ====================

    /// <summary>底部状态行：播放 / 暂停、当前时长 / 总时长、非 1× 时的速度、音频备注。</summary>
    void UpdateStatus()
    {
        if (_status.Visibility != Visibility.Visible) return;

        double position = 0;
        double duration = 0;

        if (_player is not null)
        {
            position = _player.Position;
            duration = _player.Duration;
        }
        else if (_videoWindow is not null)
        {
            try
            {
                position = _videoWindow.Video.Position.TotalSeconds;
                Duration natural = _videoWindow.Video.NaturalDuration;
                if (natural.HasTimeSpan) duration = natural.TimeSpan.TotalSeconds;
                MediaStateThrottle.Success();
            }
            catch (Exception ex)
            {
                // 卡片关闭 / 视频源切换瞬间会抛，属良性竞态；用节流器记状态转移，
                // 既不刷屏也不静默（X-9：不能吞掉真正的状态读取失败）。
                MediaStateThrottle.Failure(ex.Message);
            }
        }

        string text = (_mediaEnded ? "■ " : (_playing ? "▶ " : "❚❚ ")) +
                      Clock(position) + " / " + Clock(duration);

        if (_mediaEnded) text += " · 已结束";

        if (Math.Abs(_speed - 1) > 0.001)
            text += $" · {_speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}×";

        // P1-6：音频不可用的备注（ffmpeg 解码、WaveOut 起不来时）。
        if (_audioNote is not null) text += $" · {_audioNote}";

        _status.SetText(text, CardStatusKind.Normal);
    }

    static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;

        long total = (long)Math.Round(seconds);
        if (total >= 3600)
            return $"{total / 3600:0}:{total / 60 % 60:00}:{total % 60:00}";

        return $"{total / 60:00}:{total % 60:00}";
    }

    /// <summary>把视频子窗口重新摆到卡片上方（卡片置顶后会被压下去）。</summary>
    public void RaiseVideo()
    {
        if (_videoWindow is null) return;
        UpdateVideoPlacement();
        _videoWindow.RaiseAboveOwner();
    }

    void Unload()
    {
        // K-12：作废正在后台解码的图片，旧结果回来会被 IsCurrentImage 丢掉。
        _imageRun++;
        _frameBitmap = null;
        _mediaEnded = false;
        _waitingForFfmpeg = false;

        // 释放路径：GifAnimator.Dispose 只停表 / 清帧，异常没有可补救动作。
        try { if (_gif is not null) _gif.Dispose(); } catch { }
        _gif = null;

        ReleaseVideo();

        _image.Source = null;
        _loadedPath = null;
    }

    void ReleaseVideo()
    {
        _pendingVideo = null;
        _audioNote = null;
        _mediaEnded = false;
        _waitingForFfmpeg = false;
        _ffmpegRetryTask = null;
        _frameBitmap = null;

        // 让正在 await 的 StartFfmpeg 探测作废（否则它回来时会重新起一个 player）。
        _videoRun++;

        if (_progressTimer is not null) _progressTimer.Stop();
        _status.SetProgress(null);
        _status.Visibility = Visibility.Collapsed;

        if (_player is not null)
        {
            // 释放路径：播放器 Dispose 内部已做有界收尾，异常无处上报；
            // 下面把引用置 null，保证不会继续用半损坏的实例。
            try { _player.Dispose(); } catch { }
            _player = null;
        }

        if (_videoWindow is null) return;

        try
        {
            _videoWindow.Video.MediaOpened -= OnVideoOpened;
            _videoWindow.Video.MediaFailed -= OnVideoFailed;
            _videoWindow.Video.MediaEnded -= OnVideoEnded;
            _videoWindow.Dispose();
        }
        catch (Exception ex)
        {
            // 关闭子窗口失败可能留下一个孤儿视频窗（盖在桌面上），必须可见。
            Log.Warn($"关闭视频子窗失败: {ex.Message}");
        }

        _videoWindow = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_progressTimer is not null)
        {
            _progressTimer.Stop();
            _progressTimer = null;
        }

        Unload();
    }
}
