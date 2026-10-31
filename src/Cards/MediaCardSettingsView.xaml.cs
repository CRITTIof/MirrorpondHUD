using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;
using MirrorpondHUD.Media;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 媒体卡自己的设置区块：文件 / 缩放 / 播放 / 解码器。
    ///
    /// 以前这 20 个属性和对应的 XAML 面板住在 <c>CardEditor</c> / <c>SettingsWindow.xaml</c>，
    /// 现在整体搬进模块；宿主只提供一个 ContentControl。设置值存在 <c>CardSettings.Extras["media"]</c>。
    /// </summary>
    public partial class MediaCardSettingsView : UserControl, INotifyPropertyChanged
    {
        readonly MediaSettings _media;
        readonly Action _onChanged;

        public MediaCardSettingsView(CardSettings config, Action onChanged)
        {
            InitializeComponent();

            _media = MediaCard.MediaOf(config);
            _onChanged = onChanged;
            DataContext = this;
        }

        public string MediaPath
        {
            get { return _media.Path ?? ""; }
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
            get { return _media.Width; }
            set
            {
                double v = Math.Round(Math.Max(0, Math.Min(3000, value)));
                if (Math.Abs(_media.Width - v) < 0.001) return;
                _media.Width = v;
                Raise("MediaWidth");
                Raise("MediaWidthText");
            }
        }

        public string MediaWidthText { get { return _media.Width.ToString("0", CultureInfo.InvariantCulture); } }

        public double MediaHeight
        {
            get { return _media.Height; }
            set
            {
                double v = Math.Round(Math.Max(0, Math.Min(3000, value)));
                if (Math.Abs(_media.Height - v) < 0.001) return;
                _media.Height = v;
                Raise("MediaHeight");
                Raise("MediaHeightText");
            }
        }

        public string MediaHeightText { get { return _media.Height.ToString("0", CultureInfo.InvariantCulture); } }

        public double MediaOpacity
        {
            get { return _media.Opacity; }
            set
            {
                if (Math.Abs(_media.Opacity - value) < 0.001) return;
                _media.Opacity = value;
                Raise("MediaOpacity");
            }
        }

        public bool MediaLoop
        {
            get { return _media.Loop; }
            set
            {
                if (_media.Loop == value) return;
                _media.Loop = value;
                Raise("MediaLoop");
            }
        }

        public bool MediaAutoplay
        {
            get { return _media.Autoplay; }
            set
            {
                if (_media.Autoplay == value) return;
                _media.Autoplay = value;
                Raise("MediaAutoplay");
            }
        }

        public bool MediaMuted
        {
            get { return _media.Muted; }
            set
            {
                if (_media.Muted == value) return;
                _media.Muted = value;
                Raise("MediaMuted");
            }
        }

        public bool MediaRounded
        {
            get { return _media.Rounded; }
            set
            {
                if (_media.Rounded == value) return;
                _media.Rounded = value;
                Raise("MediaRounded");
            }
        }

        public double MediaSpeed
        {
            get { return _media.Speed; }
            set
            {
                double v = Math.Round(value, 2);
                if (Math.Abs(_media.Speed - v) < 0.001) return;
                _media.Speed = v;
                Raise("MediaSpeed");
                Raise("MediaSpeedText");
            }
        }

        public string MediaSpeedText
        {
            get { return _media.Speed.ToString("0.00", CultureInfo.InvariantCulture) + "×"; }
        }

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
                Raise("DecoderHint");
            }
        }

        /// <summary>自定义 ffmpeg.exe；空 = 自动在 PATH 与常见目录里找。</summary>
        public string MediaDecoderPath
        {
            get { return _media.DecoderPath ?? ""; }
            set
            {
                string text = (value ?? "").Trim();
                if (string.Equals(_media.DecoderPath ?? "", text, StringComparison.OrdinalIgnoreCase)) return;
                _media.DecoderPath = text;
                Raise("MediaDecoderPath");
                Raise("DecoderHint");
            }
        }

        /// <summary>探测结果：找到哪些解码器、ffmpeg 在哪。</summary>
        public string DecoderHint
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_media.DecoderPath))
                {
                    return System.IO.File.Exists(_media.DecoderPath)
                        ? "使用自定义解码器：" + _media.DecoderPath
                        : "自定义解码器不存在：" + _media.DecoderPath;
                }

                List<DecoderInfo> found = Decoders.Detect();
                if (found.Count == 0)
                    return "没找到 ffmpeg / mpv / VLC；视频只能用 Windows 自带的 Media Foundation";

                DecoderInfo ffmpeg = null;
                var others = new System.Text.StringBuilder();
                foreach (DecoderInfo info in found)
                {
                    if (info.Frames && ffmpeg == null) { ffmpeg = info; continue; }
                    if (others.Length > 0) others.Append("、");
                    others.Append(info.Kind);
                }

                if (ffmpeg == null)
                    return "找到 " + others + "，但没有 ffmpeg；视频仍走系统解码（可在上面指定 ffmpeg.exe）";

                string label = string.IsNullOrWhiteSpace(ffmpeg.Version)
                    ? "FFmpeg"
                    : "FFmpeg " + ffmpeg.Version;
                string text = "已找到 " + label + "（" + ffmpeg.Path + "）";
                if (others.Length > 0) text += "；另有 " + others;
                return text;
            }
        }

        /// <summary>重新扫一遍 PATH（设置页的「检测」按钮）。</summary>
        public void RefreshDecoders()
        {
            List<DecoderInfo> found = Decoders.Detect(true);
            Log.Info("手动重新检测解码器：" + Decoders.Describe(found));
            Raise("DecoderHint");
        }

        // ==================== 按钮 ====================

        void PickMediaFileClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择图片 / 动图 / 视频",
                    Filter = "媒体文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv;*.webm|" +
                             "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico|" +
                             "视频|*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv;*.webm|所有文件|*.*",
                    CheckFileExists = true
                };

                string current = (MediaPath ?? "").Trim();
                if (current.Length > 0)
                {
                    try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current); }
                    catch { }
                }

                Window owner = Window.GetWindow(this);
                bool? ok = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (ok == true) MediaPath = dialog.FileName;
            }
            catch (Exception ex)
            {
                Log.Warn("选择媒体文件失败: " + ex.Message);
            }
        }

        void ClearMediaFileClick(object sender, RoutedEventArgs e)
        {
            MediaPath = "";
        }

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
                bool? ok = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (ok == true) MediaDecoderPath = dialog.FileName;
            }
            catch (Exception ex)
            {
                Log.Warn("选择解码器失败: " + ex.Message);
            }
        }

        void DetectDecodersClick(object sender, RoutedEventArgs e)
        {
            RefreshDecoders();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void Raise(string name)
        {
            _onChanged?.Invoke();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
