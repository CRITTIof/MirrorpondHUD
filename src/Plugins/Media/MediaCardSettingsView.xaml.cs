using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;
using MirrorpondHUD.Ui.Controls.Media;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 媒体卡自己的设置区块：文件 / 缩放 / 播放 / 解码器。
///
/// 以前这 20 个属性和对应的 XAML 面板住在 <c>CardEditor</c> / <c>SettingsWindow.xaml</c>，
/// 现在整体搬进模块；宿主只提供一个 ContentControl。设置值存在 <c>CardSettings.Extras["media"]</c>。
/// </summary>
public partial class MediaCardSettingsView : UserControl, INotifyPropertyChanged
{
    readonly MediaSettings _media;
    readonly CardEditorNotifier _notifier;

    // P-24：DecoderHint 只读这个缓存；同步 File.Exists / 启动探测都挪到显式刷新里。
    string _decoderHint = "正在检测解码器…（完成后会自动刷新）";
    int _decoderHintVersion;

    public MediaCardSettingsView(CardSettings config, Action onChanged)
    {
        InitializeComponent();

        _media = MediaCard.MediaOf(config);
        _notifier = new CardEditorNotifier(onChanged);
        DataContext = this;

        // P1-8：后台预热完成后主动刷新一次提示，不阻塞设置页。
        WarmDecodersAsync();

        // P-24：自定义路径是否存在也要放在后台判断，绑定读不能触发 IO。
        RequestDecoderHintRefresh(0);
    }

    async void WarmDecodersAsync()
    {
        try
        {
            await Decoders.WarmUpAsync();
            RequestDecoderHintRefresh(0);
        }
        catch (Exception ex)
        {
            Log.Warn($"解码器预热提示刷新失败: {ex.Message}");
        }
    }

    public string MediaPath
    {
        get => _media.Path ?? "";
        set
        {
            string text = value ?? "";
            if (string.Equals(_media.Path ?? "", text, StringComparison.Ordinal)) return;
            _media.Path = text;
            Raise("MediaPath");
        }
    }

    /// <summary>0 = 完整显示，1 = 裁剪填满，2 = 拉伸填满，3 = 原始大小。</summary>
    public int MediaFitIndex
    {
        get
        {
            switch (MediaSettings.NormalizeFit(_media.Fit))
            {
                case "Cover": return 1;
                case "Fill": return 2;
                case "None": return 3;
                default: return 0;
            }
        }
        set
        {
            string fit = value == 1 ? "Cover" : value == 2 ? "Fill" : value == 3 ? "None" : "Contain";
            if (MediaSettings.NormalizeFit(_media.Fit) == fit) return;
            _media.Fit = fit;
            Raise("MediaFitIndex");
        }
    }

    public double MediaWidth
    {
        get => _media.Width;
        set
        {
            double v = Math.Round(Math.Max(0, Math.Min(3000, value)));
            if (Math.Abs(_media.Width - v) < 0.001) return;
            _media.Width = v;
            Raise("MediaWidth");
            Raise("MediaWidthText");
        }
    }

    public string MediaWidthText => _media.Width.ToString("0", CultureInfo.InvariantCulture);

    public double MediaHeight
    {
        get => _media.Height;
        set
        {
            double v = Math.Round(Math.Max(0, Math.Min(3000, value)));
            if (Math.Abs(_media.Height - v) < 0.001) return;
            _media.Height = v;
            Raise("MediaHeight");
            Raise("MediaHeightText");
        }
    }

    public string MediaHeightText => _media.Height.ToString("0", CultureInfo.InvariantCulture);

    public double MediaOpacity
    {
        get => _media.Opacity;
        set
        {
            if (Math.Abs(_media.Opacity - value) < 0.001) return;
            _media.Opacity = value;
            Raise("MediaOpacity");
        }
    }

    public bool MediaLoop
    {
        get => _media.Loop;
        set
        {
            if (_media.Loop == value) return;
            _media.Loop = value;
            Raise("MediaLoop");
        }
    }

    public bool MediaAutoplay
    {
        get => _media.Autoplay;
        set
        {
            if (_media.Autoplay == value) return;
            _media.Autoplay = value;
            Raise("MediaAutoplay");
        }
    }

    public bool MediaMuted
    {
        get => _media.Muted;
        set
        {
            if (_media.Muted == value) return;
            _media.Muted = value;
            Raise("MediaMuted");
        }
    }

    public bool MediaRounded
    {
        get => _media.Rounded;
        set
        {
            if (_media.Rounded == value) return;
            _media.Rounded = value;
            Raise("MediaRounded");
        }
    }

    public double MediaSpeed
    {
        get => _media.Speed;
        set
        {
            double v = Math.Round(value, 2);
            if (Math.Abs(_media.Speed - v) < 0.001) return;
            _media.Speed = v;
            Raise("MediaSpeed");
            Raise("MediaSpeedText");
        }
    }

    public string MediaSpeedText => $"{_media.Speed.ToString("0.00", CultureInfo.InvariantCulture)}×";

    /// <summary>0 = 自动，1 = 只用系统 Media Foundation，2 = 只用 ffmpeg。</summary>
    public int MediaDecoderIndex
    {
        get
        {
            switch (MediaSettings.NormalizeDecoder(_media.Decoder))
            {
                case "BuiltIn": return 1;
                case "FFmpeg": return 2;
                default: return 0;
            }
        }
        set
        {
            string decoder = value == 1 ? "BuiltIn" : value == 2 ? "FFmpeg" : "Auto";
            if (MediaSettings.NormalizeDecoder(_media.Decoder) == decoder) return;
            _media.Decoder = decoder;
            Raise("MediaDecoderIndex");
        }
    }

    /// <summary>自定义 ffmpeg.exe；空 = 自动在 PATH 与常见目录里找。</summary>
    public string MediaDecoderPath
    {
        get => _media.DecoderPath ?? "";
        set
        {
            string text = (value ?? "").Trim();
            if (string.Equals(_media.DecoderPath ?? "", text, StringComparison.OrdinalIgnoreCase)) return;
            _media.DecoderPath = text;
            Raise("MediaDecoderPath");
            // P-24：逐字符输入时只做一次去抖后的后台检查，UNC 路径不会卡住 UI。
            RequestDecoderHintRefresh(250);
        }
    }

    /// <summary>
    /// 探测结果提示（P-24）：只返回缓存值。以前 getter 里同步 File.Exists 并可能启动探测，
    /// 绑定每次求值都会做 IO；现在由构造、路径变更、预热 / 检测完成触发的显式刷新更新。
    /// </summary>
    public string DecoderHint => _decoderHint;

    /// <summary>
    /// 安排一次提示刷新。<paramref name="debounceMs"/> 用于输入路径时的去抖；
    /// 版本号保证只有最后一次请求的结果会落到界面。
    /// </summary>
    void RequestDecoderHintRefresh(int debounceMs)
    {
        int version = ++_decoderHintVersion;
        _ = RefreshDecoderHintAsync(version, debounceMs);
    }

    async Task RefreshDecoderHintAsync(int version, int debounceMs)
    {
        try
        {
            if (debounceMs > 0) await Task.Delay(debounceMs);
            if (version != _decoderHintVersion) return;

            // 路径在 UI 线程取快照：后台只做 File.Exists 与读 Decoders 的线程安全缓存。
            string decoderPath = _media.DecoderPath;
            string hint = await Task.Run(() => BuildDecoderHint(decoderPath));
            if (version != _decoderHintVersion) return;

            string previous = _decoderHint;
            _decoderHint = hint;
            if (!string.Equals(previous, hint, StringComparison.Ordinal)) _notifier.Notify("DecoderHint");
        }
        catch (Exception ex)
        {
            Log.Warn($"刷新解码器提示失败: {ex.Message}");
        }
    }

    /// <summary>计算提示文案；可能包含一次 File.Exists，只允许在后台线程调用（P-24）。</summary>
    static string BuildDecoderHint(string decoderPath)
    {
        if (!string.IsNullOrWhiteSpace(decoderPath))
        {
            return System.IO.File.Exists(decoderPath)
                ? $"使用自定义解码器：{decoderPath}"
                : $"自定义解码器不存在：{decoderPath}";
        }

        List<DecoderInfo> found = Decoders.Cached;
        if (found.Count == 0)
        {
            // 审阅 §18：首启缓存为空只是“还没探测完”，不能直接报“没找到”；
            // 预热结束后 WarmDecodersAsync 会主动刷新这个提示。
            if (!Decoders.HasResult)
            {
                if (!Decoders.IsDetecting) Decoders.WarmUp();   // 上一轮探测异常结束则重试
                return "正在检测解码器…（完成后会自动刷新）";
            }

            return "没找到 ffmpeg / mpv / VLC；视频只能用 Windows 自带的 Media Foundation";
        }

        DecoderInfo ffmpeg = null;
        var others = new System.Text.StringBuilder();
        foreach (DecoderInfo info in found)
        {
            if (info.Frames && ffmpeg is null) { ffmpeg = info; continue; }
            if (others.Length > 0) others.Append("、");
            others.Append(info.Kind);
        }

        if (ffmpeg is null)
            return $"找到 {others}，但没有 ffmpeg；视频仍走系统解码（可在上面指定 ffmpeg.exe）";

        string label = string.IsNullOrWhiteSpace(ffmpeg.Version)
            ? "FFmpeg"
            : $"FFmpeg {ffmpeg.Version}";
        string text = $"已找到 {label}（{ffmpeg.Path}）";
        if (others.Length > 0) text += $"；另有 {others}";
        return text;
    }

    /// <summary>重新扫一遍 PATH（设置页的「检测」按钮）；P1-8：异步探测，不冻结设置页。</summary>
    public async void RefreshDecoders()
    {
        // async void 的异常会直接进 Dispatcher：非白名单类型会被宿主判成
        // “不可信状态”并退出（S4 同类回归）。这里拆出外壳，兜住探测与 UI 刷新。
        try
        {
            await RefreshDecodersCore();
        }
        catch (Exception ex)
        {
            Log.Warn($"重新检测解码器失败: {ex.Message}");
            RequestDecoderHintRefresh(0);
        }
    }

    async Task RefreshDecodersCore()
    {
        List<DecoderInfo> found = await Decoders.DetectAsync(true);
        Log.Info($"手动重新检测解码器：{Decoders.Describe(found)}");
        RequestDecoderHintRefresh(0);
    }

    // ==================== 按钮 ====================

    void PickMediaFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择图片 / 动图 / 视频",
                Filter = $"媒体文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv;*.webm|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico|视频|*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv;*.webm|所有文件|*.*",
                CheckFileExists = true
            };

            string current = (MediaPath ?? "").Trim();
            if (current.Length > 0)
            {
                try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current); }
                catch { }
            }

            Window owner = Window.GetWindow(this);
            bool? ok = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (ok == true) MediaPath = dialog.FileName;
        }
        catch (Exception ex)
        {
            Log.Warn($"选择媒体文件失败: {ex.Message}");
        }
    }

    void ClearMediaFileClick(object sender, RoutedEventArgs e) => MediaPath = "";

    void PickDecoderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 ffmpeg.exe",
                Filter = "ffmpeg|ffmpeg.exe;ffmpeg|可执行文件|*.exe|所有文件|*.*",
                CheckFileExists = true
            };

            string current = (MediaDecoderPath ?? "").Trim();
            if (current.Length > 0)
            {
                try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current); }
                catch { }
            }

            Window owner = Window.GetWindow(this);
            bool? ok = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (ok == true) MediaDecoderPath = dialog.FileName;
        }
        catch (Exception ex)
        {
            Log.Warn($"选择解码器失败: {ex.Message}");
        }
    }

    void DetectDecodersClick(object sender, RoutedEventArgs e) => RefreshDecoders();

    public event PropertyChangedEventHandler PropertyChanged
    {
        add => _notifier.PropertyChanged += value;
        remove => _notifier.PropertyChanged -= value;
    }

    // P-25：通知逻辑统一在 CardEditorNotifier，这里只保留旧调用点的薄封装。
    void Raise(string name) => _notifier.Raise(name);
}
