using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Media
{
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
    public sealed class MediaView : Grid, IDisposable
    {
        static readonly string[] VideoExtensions =
        {
            ".mp4", ".m4v", ".wmv", ".avi", ".mov", ".mkv", ".webm", ".mpg", ".mpeg", ".ts"
        };

        readonly Grid _header;
        readonly TextBlock _name;
        readonly TextBlock _statusText;
        readonly Border _statusHost;
        readonly Grid _host;
        readonly Image _image;
        readonly TextBlock _message;
        readonly Grid _footer;
        readonly Border _track;
        readonly Border _fill;

        GifAnimator _gif;
        VideoWindow _videoWindow;
        FfmpegPlayer _player;
        DispatcherTimer _progressTimer;

        MediaSettings _settings = new MediaSettings();
        StyleSettings _style;
        string _loadedPath;
        string _pendingVideo;
        Size _natural = new Size(320, 180);
        double _cornerRadius = 10;
        double _padX = 14;
        double _padY = 10;
        double _titleSize = 12;
        double _speed = 1;
        bool _playing = true;
        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        double _clipWidth;
        double _clipHeight;
        double _clipRadius = -1;
        bool _disposed;

        public MediaView()
        {
            ClipToBounds = true;
            SnapsToDevicePixels = true;

            _name = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // 卡片底色可能是浅色的（毛玻璃 / 淡背景），状态行加一层深色圆角小底，
            // 保证「▶ 00:05 / 00:12 · 1.5×」在任何背景上都看得清。
            _statusHost = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0),
                Child = _statusText
            };

            _header = new Grid();
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _header.Children.Add(_name);
            _header.Children.Add(_statusHost);
            Grid.SetColumn(_name, 0);
            Grid.SetColumn(_statusHost, 1);

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

            _track = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };

            _fill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 0
            };

            var trackHost = new Grid();
            trackHost.Children.Add(_track);
            trackHost.Children.Add(_fill);

            _footer = new Grid { Visibility = Visibility.Collapsed };
            _footer.Children.Add(trackHost);

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Children.Add(_header);
            Children.Add(_host);
            Children.Add(_footer);
            SetRow(_header, 0);
            SetRow(_host, 1);
            SetRow(_footer, 2);

            ApplyChrome();

            Loaded += (s, e) =>
            {
                TryStartVideo();
                UpdateVideoPlacement();
            };
            LayoutUpdated += (s, e) =>
            {
                // 首次 Loaded 时卡片窗口可能还没真正 Loaded，这里补一次机会。
                if (_pendingVideo != null && _videoWindow == null && _player == null) TryStartVideo();
                UpdateVideoPlacement();
            };
            Unloaded += (s, e) => { if (_videoWindow != null) _videoWindow.Stop(); };

            // 只在媒体区域真的变尺寸时重算圆角裁剪。
            // 放进 LayoutUpdated 会变成「设 Clip → 重绘 → 再布局」的循环，白烧 CPU。
            _host.SizeChanged += (s, e) => ApplyClip();
            _track.SizeChanged += (s, e) => UpdateProgress();
        }

        /// <summary>媒体尺寸变化（视频打开、图片解码完成）时通知宿主重新排布叠层。</summary>
        public event Action LayoutChanged;

        public string LoadedPath { get { return _loadedPath; } }
        public bool HasVideo { get { return _videoWindow != null || _player != null || _pendingVideo != null; } }

        public MediaView WithCornerRadius(double radius)
        {
            _cornerRadius = Math.Max(0, Math.Min(60, radius));
            ApplyClip();
            return this;
        }

        /// <summary>用全局外观（字体 / 颜色 / 内边距）画文件名与进度条，和文字卡片保持一致。</summary>
        public MediaView WithStyle(StyleSettings style)
        {
            _style = style;
            ApplyChrome();
            return this;
        }

        void ApplyChrome()
        {
            StyleSettings style = _style;

            _padX = Math.Max(6, style == null ? 14 : style.Padding);
            _padY = Math.Max(5, (style == null ? 14 : style.Padding) * 0.7);
            _titleSize = Math.Max(11, (style == null ? 15 : Math.Max(4, style.FontSize)) * 0.8);

            Color foreground = ParseColor(style == null ? null : style.Foreground);
            if (foreground != _foreground) _foreground = foreground;

            _name.FontFamily = new FontFamily(style == null || string.IsNullOrWhiteSpace(style.FontFamily)
                ? "Microsoft YaHei UI"
                : style.FontFamily);
            _name.FontSize = _titleSize;
            _name.FontWeight = style != null && style.Bold ? FontWeights.SemiBold : FontWeights.Normal;
            _name.Foreground = Solid(_foreground, 0.92);
            _header.Margin = new Thickness(_padX, Math.Max(2, _padY * 0.5), _padX, Math.Max(1, _padY * 0.3));

            _statusText.FontFamily = _name.FontFamily;
            _statusText.FontSize = Math.Max(10, _titleSize * 0.9);
            _statusText.Foreground = Solid(_foreground, 0.95);

            _host.Margin = new Thickness(_padX, 0, _padX, 0);
            _footer.Margin = new Thickness(_padX, 5, _padX, Math.Max(7, _padY * 0.85));

            _track.Background = Solid(_foreground, 0.20);
            _fill.Background = Solid(_foreground, 0.85);

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
            if (string.IsNullOrWhiteSpace(text)) return fallback;

            try
            {
                object value = ColorConverter.ConvertFromString(text.Trim());
                if (value is Color) return (Color)value;
            }
            catch { }

            return fallback;
        }

        // ==================== 绑定 ====================

        /// <summary>按配置装载媒体；同一路径只解码 / 打开一次。</summary>
        public void Bind(MediaSettings settings)
        {
            if (_disposed) return;

            MediaSettings previous = _settings;
            _settings = settings ?? new MediaSettings();
            _host.Opacity = _settings.Opacity;

            string path = _settings.Path ?? "";
            bool samePath = _loadedPath != null &&
                            string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase);

            if (_loadedPath != null && !samePath) Unload();

            if (path.Length == 0)
            {
                ShowMessage("媒体卡片\n在「设置 → 内容 → 媒体」里选择图片 / 动图 / 视频");
                _name.Text = "";
                _header.Visibility = Visibility.Collapsed;
                _footer.Visibility = Visibility.Collapsed;
                _statusHost.Visibility = Visibility.Collapsed;
                _loadedPath = "";
                ApplySize();
                return;
            }

            _name.Text = FileName(path);
            _name.ToolTip = path;
            _header.Visibility = Visibility.Visible;

            if (!File.Exists(path))
            {
                ShowMessage("找不到文件：\n" + path);
                _loadedPath = path;
                ApplySize();
                return;
            }

            bool video = IsVideo(path);
            _footer.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            _statusHost.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            _speed = _settings.Speed > 0 ? _settings.Speed : 1;
            _playing = _settings.Autoplay;

            if (samePath)
            {
                bool decoderChanged = previous != null &&
                    (MediaSettings.NormalizeDecoder(previous.Decoder) != MediaSettings.NormalizeDecoder(_settings.Decoder) ||
                     !string.Equals(previous.DecoderPath ?? "", _settings.DecoderPath ?? "",
                                    StringComparison.OrdinalIgnoreCase));

                if (video && decoderChanged)
                {
                    _pendingVideo = path;
                    ReleaseVideo();
                    TryStartVideo();
                }

                ApplyVideoSettings(previous);
                ApplyFit();
                _host.Opacity = _settings.Opacity;
                ApplySize();
                return;
            }

            _loadedPath = path;
            _message.Visibility = Visibility.Collapsed;

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

        static bool IsVideo(string path)
        {
            return Array.IndexOf(VideoExtensions, SafeExtension(path)) >= 0;
        }

        static string SafeExtension(string path)
        {
            try { return (Path.GetExtension(path) ?? "").ToLowerInvariant(); }
            catch { return ""; }
        }

        // ==================== 图片 / 动图 ====================

        void LoadImage(string path)
        {
            BitmapSource still = TryLoadBitmap(path);
            string ffmpeg = Decoders.FindFfmpeg(_settings);

            // WPF 解不开的格式（webp / avif / heic …）交给 ffmpeg 转一帧出来。
            if (still == null && ffmpeg != null)
            {
                Log.Info("系统解不开图片，改用 ffmpeg：" + path);
                still = FfmpegPlayer.ExtractImage(ffmpeg, path);
            }

            if (still == null)
            {
                ShowMessage("无法解码这个文件：\n" + FileName(path) +
                            (ffmpeg == null ? "\n（可在设置里指定 ffmpeg 解码器）" : ""));
                return;
            }

            _natural = new Size(still.PixelWidth, still.PixelHeight);

            if (_settings.Loop && string.Equals(SafeExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
            {
                _gif = GifAnimator.TryCreate(path, _image, true);
                if (_gif != null)
                {
                    if (_gif.CanvasSize.Width > 0 && _gif.CanvasSize.Height > 0) _natural = _gif.CanvasSize;
                    _image.Stretch = ParseStretch(_settings.Fit);
                    _gif.Play();
                    return;
                }
            }

            _image.Source = still;
            _image.Stretch = ParseStretch(_settings.Fit);
        }

        static BitmapSource TryLoadBitmap(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                    if (decoder.Frames.Count == 0) return null;

                    BitmapSource frozen = decoder.Frames[0].Clone();
                    frozen.Freeze();
                    return frozen;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("解码图片失败: " + ex.Message);
                return null;
            }
        }

        // ==================== 视频 ====================

        void TryStartVideo()
        {
            if (_disposed || _pendingVideo == null) return;

            Window card = Window.GetWindow(this);
            if (card == null || !card.IsLoaded)
            {
                return;   // 等卡片窗口出来再建子窗口 / 开解码器
            }

            string ffmpeg = Decoders.FindFfmpeg(_settings);
            string decoder = MediaSettings.NormalizeDecoder(_settings.Decoder);
            string path = _pendingVideo;

            if (decoder == "FFmpeg" && ffmpeg == null)
            {
                Log.Warn("选择了 ffmpeg 解码，但 PATH 里找不到 ffmpeg.exe，改回系统解码");
            }

            // 「自动」= 先让系统解（能硬解、有声音），解不开再在 OnVideoFailed 里换 ffmpeg；
            // 用户明确选了「仅 ffmpeg」才一开始就走外部解码。
            if (decoder == "FFmpeg" && ffmpeg != null)
            {
                StartFfmpeg(ffmpeg, path);
                return;
            }

            try
            {
                if (_videoWindow == null)
                {
                    _videoWindow = new VideoWindow(card);
                    _videoWindow.Video.MediaOpened += OnVideoOpened;
                    _videoWindow.Video.MediaFailed += OnVideoFailed;
                    _videoWindow.Video.MediaEnded += OnVideoEnded;
                    Log.Info("视频窗已创建：" + path);
                }

                _videoWindow.Open(path, _settings);
                ApplyFit();
                StartProgress();

                if (card.IsVisible) UpdateVideoPlacement();
            }
            catch (Exception ex)
            {
                Log.Warn("打开视频失败: " + ex.Message);
                ShowMessage("无法打开视频：\n" + ex.Message);
            }
        }

        /// <summary>用 ffmpeg 解出一帧当封面，先填住视频区域（异步，不阻塞卡片显示）。</summary>
        void StartPoster(string path)
        {
            string ffmpeg = Decoders.FindFfmpeg(_settings);
            if (ffmpeg == null) return;

            string captured = path;
            System.Threading.Tasks.Task.Run(() => FfmpegPlayer.ExtractImage(ffmpeg, captured))
                .ContinueWith(task =>
                {
                    if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) return;
                    BitmapSource frame = task.Result;
                    if (frame == null) return;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_disposed || _image.Source != null) return;
                        if (!string.Equals(_loadedPath, captured, StringComparison.OrdinalIgnoreCase)) return;
                        _image.Source = frame;
                        _message.Visibility = Visibility.Collapsed;
                    }));
                });
        }

        /// <summary>同步取一帧当封面（离屏截图用：视频没有窗口可渲染）。</summary>
        public void ShowPoster(string path)
        {
            string ffmpeg = Decoders.FindFfmpeg(_settings);
            if (ffmpeg == null) return;

            BitmapSource frame = FfmpegPlayer.ExtractImage(ffmpeg, path);
            if (frame == null) return;

            _image.Source = frame;
            _message.Visibility = Visibility.Collapsed;
        }

        /// <summary>这个路径要不要走视频分支（宿主 / 截图工具复用）。</summary>
        public static bool IsVideoPath(string path)
        {
            return IsVideo(path);
        }

        /// <summary>用 ffmpeg 解码：帧直接画在卡片里，不需要子窗口。</summary>
        void StartFfmpeg(string ffmpeg, string path)
        {
            try
            {
                ReleaseVideo();

                VideoInfo info = FfmpegPlayer.Probe(ffmpeg, path);
                double aspect = info.Width > 0 && info.Height > 0 ? (double)info.Height / info.Width : 9.0 / 16;
                _natural = new Size(info.Width > 0 ? info.Width : 1280, info.Height > 0 ? info.Height : 720);

                _player = new FfmpegPlayer();
                _player.FrameReady += frame =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_disposed || _player == null) return;
                        _image.Source = frame;
                        _message.Visibility = Visibility.Collapsed;
                    }), DispatcherPriority.Render);
                };
                _player.Failed += message =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ShowMessage("ffmpeg 解码失败：\n" + message);
                    }));
                };

                ApplySize();

                int width = DecodeWidth(aspect);
                int height = Math.Max(16, (int)Math.Round(width * aspect));

                _image.Stretch = ParseStretch(_settings.Fit);
                _image.Source = null;

                _player.Start(ffmpeg, path, width, height, info.Fps,
                              _settings.Loop, info.Duration, _settings.Autoplay,
                              _speed, 0, !_settings.Muted, 100);

                Log.Info("ffmpeg 解码：" + FileName(path) + " " + width + "×" + height +
                         " " + info.Fps.ToString("0.##") + "fps");
                StartProgress();
            }
            catch (Exception ex)
            {
                Log.Warn("ffmpeg 解码启动失败: " + ex.Message);
                ShowMessage("ffmpeg 解码启动失败：\n" + ex.Message);
            }
        }

        int DecodeWidth(double aspect)
        {
            double dpi = 1;
            try { dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }

            double mediaWidth = _settings.Width > 0 ? _settings.Width : 320;
            int width = (int)Math.Round(mediaWidth * (dpi > 0.1 ? dpi : 1));

            width = (int)Math.Max(160, Math.Min(1280, width));
            return width - width % 2;
        }

        void OnVideoOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_videoWindow == null) return;

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
                Log.Warn("视频打开后处理失败: " + ex.Message);
            }
        }

        void OnVideoFailed(object sender, ExceptionRoutedEventArgs e)
        {
            string reason = e != null && e.ErrorException != null
                ? e.ErrorException.Message
                : "系统缺少这个视频格式的解码器";
            Log.Warn("视频播放失败: " + reason);

            string ffmpeg = Decoders.FindFfmpeg(_settings);
            string decoder = MediaSettings.NormalizeDecoder(_settings.Decoder);

            // 「自动」模式：系统放不了就换 ffmpeg，用户不用自己判断格式。
            if (ffmpeg != null && decoder != "BuiltIn" && _pendingVideo != null)
            {
                Log.Info("系统解码失败，改用 ffmpeg：" + FileName(_pendingVideo));
                StartFfmpeg(ffmpeg, _pendingVideo);
                return;
            }

            ShowMessage("无法播放这个视频：\n" + reason +
                        (ffmpeg == null ? "\n（可在设置里指定 ffmpeg 解码器）" : ""));
        }

        void OnVideoEnded(object sender, RoutedEventArgs e)
        {
            if (_videoWindow == null || !_settings.Loop) return;
            try
            {
                _videoWindow.Video.Position = TimeSpan.Zero;
                _videoWindow.Video.Play();
            }
            catch { }
        }

        void ApplyVideoSettings(MediaSettings previous)
        {
            if (_videoWindow == null) return;
            try
            {
                _videoWindow.Video.IsMuted = _settings.Muted;
                _videoWindow.Video.Volume = _settings.Muted ? 0 : 1;
                _videoWindow.Video.SpeedRatio = _settings.Speed > 0 ? _settings.Speed : 1.0;

                if (previous != null && previous.Autoplay != _settings.Autoplay)
                {
                    if (_settings.Autoplay) _videoWindow.Play();
                    else _videoWindow.Pause();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("更新视频设置失败: " + ex.Message);
            }
        }

        /// <summary>把视频子窗口贴到媒体区域在屏幕上的位置（卡片拖动、换排布时都会重新算）。</summary>
        void UpdateVideoPlacement()
        {
            if (_videoWindow == null || _disposed) return;

            Window card = Window.GetWindow(this);
            if (card == null || !card.IsVisible) return;
            if (_host.ActualWidth < 8 || _host.ActualHeight < 8) return;

            try
            {
                Point origin = _host.PointToScreen(new Point(0, 0));
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                double sx = dpi.DpiScaleX > 0.1 ? dpi.DpiScaleX : 1;
                double sy = dpi.DpiScaleY > 0.1 ? dpi.DpiScaleY : 1;

                double radius = _settings != null && _settings.Rounded ? _cornerRadius : 0;
                _videoWindow.Place(origin.X / sx, origin.Y / sy, _host.ActualWidth, _host.ActualHeight, radius);
            }
            catch (Exception ex)
            {
                Log.Warn("定位视频窗失败: " + ex.Message);
            }
        }

        // ==================== 进度条 ====================

        void StartProgress()
        {
            if (_progressTimer == null)
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

            _footer.Visibility = Visibility.Visible;
            _statusHost.Visibility = Visibility.Visible;
            if (!_progressTimer.IsEnabled) _progressTimer.Start();
            UpdateProgress();
            UpdateStatus();
        }

        void UpdateProgress()
        {
            if (_disposed) return;

            double position = 0;
            double duration = 0;

            if (_player != null)
            {
                position = _player.Position;
                duration = _player.Duration;
            }
            else if (_videoWindow != null)
            {
                try
                {
                    position = _videoWindow.Video.Position.TotalSeconds;
                    Duration natural = _videoWindow.Video.NaturalDuration;
                    if (natural.HasTimeSpan) duration = natural.TimeSpan.TotalSeconds;
                }
                catch { }
            }

            double fraction = duration > 0.05 ? Math.Min(1, Math.Max(0, position / duration)) : 0;
            double width = _track.ActualWidth * fraction;
            _fill.Width = double.IsNaN(width) || width < 0 ? 0 : width;
        }

        // ==================== 尺寸 ====================

        /// <summary>按卡片圆角裁掉媒体区域的四个角（关闭「圆角」时保持方角）。</summary>
        void ApplyClip()
        {
            if (_host == null) return;

            if (_settings == null || !_settings.Rounded)
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
                Math.Abs(_clipRadius - radius) < 0.01 && _host.Clip != null)
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
            if (_videoWindow != null) _videoWindow.Video.Stretch = stretch;
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
            if (handler != null) handler();
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
            _footer.Visibility = Visibility.Collapsed;
            _statusHost.Visibility = Visibility.Collapsed;
        }

        // ==================== 播放控制 ====================

        public void Play()
        {
            if (_disposed) return;
            try
            {
                if (_gif != null) _gif.Play();
                if (_player != null) _player.Resume();
                if (_videoWindow != null)
                {
                    UpdateVideoPlacement();
                    _videoWindow.Play();
                    UpdateVideoPlacement();
                }
                _playing = true;
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Log.Warn("媒体播放失败: " + ex.Message);
            }
        }

        public void Pause()
        {
            try
            {
                if (_gif != null) _gif.Stop();
                if (_player != null) _player.Pause();
                if (_videoWindow != null) _videoWindow.Pause();
                _playing = false;
                UpdateStatus();
            }
            catch { }
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
                if (_player != null)
                {
                    if (_player.IsPaused) { _player.Resume(); _playing = true; }
                    else { _player.Pause(); _playing = false; }
                }
                else if (_videoWindow != null)
                {
                    if (_playing) { _videoWindow.Pause(); _playing = false; }
                    else { _videoWindow.Play(); _playing = true; }
                }
                else if (_gif != null)
                {
                    if (_playing) _gif.Stop();
                    else _gif.Play();
                    _playing = !_playing;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("切换播放状态失败: " + ex.Message);
            }

            UpdateStatus();
        }

        /// <summary>相对当前位置快进 / 快退（负数是后退）。</summary>
        public void SeekBy(double seconds)
        {
            if (_disposed) return;

            try
            {
                if (_player != null)
                {
                    _player.SeekBy(seconds);
                }
                else if (_videoWindow != null)
                {
                    MediaElement video = _videoWindow.Video;
                    double duration = 0;
                    try
                    {
                        Duration natural = video.NaturalDuration;
                        if (natural.HasTimeSpan) duration = natural.TimeSpan.TotalSeconds;
                    }
                    catch { }

                    double target = video.Position.TotalSeconds + seconds;
                    if (target < 0) target = 0;
                    if (duration > 0 && target > duration - 0.1) target = Math.Max(0, duration - 0.1);
                    video.Position = TimeSpan.FromSeconds(target);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("视频跳转失败: " + ex.Message);
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
                if (_player != null) _player.SetSpeed(next);
                else if (_videoWindow != null) _videoWindow.Video.SpeedRatio = next;
            }
            catch (Exception ex)
            {
                Log.Warn("调整播放速度失败: " + ex.Message);
            }

            UpdateStatus();
        }

        // ==================== 状态行 ====================

        static FontFamily _iconFont;

        static FontFamily IconFont()
        {
            if (_iconFont != null) return _iconFont;

            try
            {
                var found = Application.Current == null ? null : Application.Current.TryFindResource("ReiconFont");
                _iconFont = found as FontFamily;
            }
            catch { }

            if (_iconFont == null)
                _iconFont = new FontFamily("pack://application:,,,/assets/keycaps/#ReiconFont");

            return _iconFont;
        }

        /// <summary>右上角：播放 / 暂停状态、当前时长 / 总时长、非 1× 时的速度。</summary>
        void UpdateStatus()
        {
            if (_statusText == null) return;
            if (_statusHost.Visibility != Visibility.Visible) return;

            double position = 0;
            double duration = 0;

            if (_player != null)
            {
                position = _player.Position;
                duration = _player.Duration;
            }
            else if (_videoWindow != null)
            {
                try
                {
                    position = _videoWindow.Video.Position.TotalSeconds;
                    Duration natural = _videoWindow.Video.NaturalDuration;
                    if (natural.HasTimeSpan) duration = natural.TimeSpan.TotalSeconds;
                }
                catch { }
            }

            _statusText.Inlines.Clear();
            _statusText.Inlines.Add(new Run(_playing ? Icons.Play : Icons.PauseMedia)
            {
                FontFamily = IconFont(),
                FontSize = _statusText.FontSize + 1
            });
            _statusText.Inlines.Add(new Run(" " + Clock(position) + " / " + Clock(duration)));

            if (Math.Abs(_speed - 1) > 0.001)
            {
                _statusText.Inlines.Add(new Run(" · " + _speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×"));
            }
        }

        static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;

            long total = (long)Math.Round(seconds);
            if (total >= 3600)
                return (total / 3600).ToString("0") + ":" + (total / 60 % 60).ToString("00") + ":" + (total % 60).ToString("00");

            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        /// <summary>把视频子窗口重新摆到卡片上方（卡片置顶后会被压下去）。</summary>
        public void RaiseVideo()
        {
            if (_videoWindow == null) return;
            UpdateVideoPlacement();
            _videoWindow.RaiseAboveOwner();
        }

        void Unload()
        {
            try { if (_gif != null) _gif.Dispose(); } catch { }
            _gif = null;

            ReleaseVideo();

            _image.Source = null;
            _loadedPath = null;
        }

        void ReleaseVideo()
        {
            _pendingVideo = null;

            if (_progressTimer != null) _progressTimer.Stop();
            _footer.Visibility = Visibility.Collapsed;
            _statusHost.Visibility = Visibility.Collapsed;
            _fill.Width = 0;

            if (_player != null)
            {
                try { _player.Dispose(); } catch { }
                _player = null;
            }

            if (_videoWindow == null) return;

            try
            {
                _videoWindow.Video.MediaOpened -= OnVideoOpened;
                _videoWindow.Video.MediaFailed -= OnVideoFailed;
                _videoWindow.Video.MediaEnded -= OnVideoEnded;
                _videoWindow.Dispose();
            }
            catch { }

            _videoWindow = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_progressTimer != null)
            {
                _progressTimer.Stop();
                _progressTimer = null;
            }

            Unload();
        }
    }
}
