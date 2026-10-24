using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Media
{
    /// <summary>
    /// 媒体控件：一张卡片里的图片 / 位图 / 动图 / 视频，全部由这一个 Grid 承载。
    ///
    /// · 静态图片（png / jpg / bmp / tif / ico …）用 Image 绘制；
    /// · 动图（GIF）交给 <see cref="GifAnimator"/> 按每帧自带的延时播放；
    /// · 视频交给 <see cref="VideoWindow"/>：分层窗口无法显示 MediaElement，
    ///   视频放在一个跟随卡片位置的不透明子窗口里。
    /// 控件自己按配置算好宽高，卡片窗口用 SizeToContent 就能自动贴合。
    /// </summary>
    public sealed class MediaView : Grid, IDisposable
    {
        static readonly string[] VideoExtensions =
        {
            ".mp4", ".m4v", ".wmv", ".avi", ".mov", ".mkv", ".webm", ".mpg", ".mpeg", ".ts"
        };

        readonly Image _image;
        readonly Grid _host;
        readonly TextBlock _message;
        GifAnimator _gif;
        VideoWindow _videoWindow;
        MediaSettings _settings = new MediaSettings();
        string _loadedPath;
        string _pendingVideo;
        Size _natural = new Size(320, 180);
        double _cornerRadius = 10;
        bool _disposed;

        public MediaView()
        {
            ClipToBounds = true;
            SnapsToDevicePixels = true;

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

            _host = new Grid();
            Children.Add(_host);
            Children.Add(_message);

            Loaded += (s, e) =>
            {
                TryStartVideo();
                UpdateVideoPlacement();
            };
            LayoutUpdated += (s, e) =>
            {
                // 首次 Loaded 时卡片窗口可能还没真正 Loaded，这里补一次机会。
                if (_pendingVideo != null && _videoWindow == null) TryStartVideo();
                ApplyClip();
                UpdateVideoPlacement();
            };
            Unloaded += (s, e) => { if (_videoWindow != null) _videoWindow.Stop(); };
        }

        /// <summary>媒体尺寸变化（视频打开、图片解码完成）时通知宿主重新排布叠层。</summary>
        public event Action LayoutChanged;

        public string LoadedPath { get { return _loadedPath; } }
        public bool HasVideo { get { return _videoWindow != null || _pendingVideo != null; } }

        public MediaView WithCornerRadius(double radius)
        {
            _cornerRadius = Math.Max(0, Math.Min(60, radius));
            ApplyClip();
            return this;
        }

        // ==================== 绑定 ====================

        /// <summary>按配置装载媒体；同一路径只解码 / 打开一次。</summary>
        public void Bind(MediaSettings settings)
        {
            if (_disposed) return;

            MediaSettings previous = _settings;
            _settings = settings ?? new MediaSettings();

            string path = _settings.Path ?? "";
            bool samePath = _loadedPath != null &&
                            string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase);

            if (_loadedPath != null && !samePath) Unload();

            if (path.Length == 0)
            {
                ShowMessage("媒体卡片\n在「设置 → 内容 → 媒体」里选择图片 / 动图 / 视频");
                _loadedPath = "";
                ApplySize();
                return;
            }

            if (!File.Exists(path))
            {
                ShowMessage("找不到文件：\n" + path);
                _loadedPath = path;
                ApplySize();
                return;
            }

            if (samePath)
            {
                ApplyVideoSettings(previous);
                ApplyFit();
                ApplySize();
                return;
            }

            _loadedPath = path;
            _message.Visibility = Visibility.Collapsed;

            if (IsVideo(path))
            {
                _pendingVideo = path;
                TryStartVideo();
            }
            else
            {
                LoadImage(path);
            }

            ApplySize();
        }

        static bool IsVideo(string path)
        {
            string ext = "";
            try { ext = Path.GetExtension(path) ?? ""; } catch { }
            return Array.IndexOf(VideoExtensions, ext.ToLowerInvariant()) >= 0;
        }

        // ==================== 图片 / 动图 ====================

        void LoadImage(string path)
        {
            BitmapSource still = TryLoadBitmap(path);
            if (still == null)
            {
                ShowMessage("无法解码这个文件：\n" + Path.GetFileName(path));
                return;
            }

            _natural = new Size(still.PixelWidth, still.PixelHeight);

            if (_settings.Loop)
            {
                _gif = GifAnimator.TryCreate(path, _image, true);
                if (_gif != null)
                {
                    if (_gif.CanvasSize.Width > 0 && _gif.CanvasSize.Height > 0) _natural = _gif.CanvasSize;
                    _host.Children.Clear();
                    _host.Children.Add(_image);
                    _image.Stretch = ParseStretch(_settings.Fit);
                    _gif.Play();
                    return;
                }
            }

            _image.Source = still;
            _image.Stretch = ParseStretch(_settings.Fit);
            _host.Children.Clear();
            _host.Children.Add(_image);
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
                Log.Info("视频等待卡片窗口：pending=" + _pendingVideo +
                         " window=" + (card == null ? "null" : card.IsLoaded.ToString()));
                return;   // 等卡片窗口出来再建子窗口
            }

            try
            {
                if (_videoWindow == null)
                {
                    _videoWindow = new VideoWindow(card);
                    _videoWindow.Video.MediaOpened += OnVideoOpened;
                    _videoWindow.Video.MediaFailed += OnVideoFailed;
                    _videoWindow.Video.MediaEnded += OnVideoEnded;
                    Log.Info("视频窗已创建：" + _pendingVideo);
                }

                _host.Children.Clear();
                _videoWindow.Open(_pendingVideo, _settings);
                ApplyFit();

                if (card.IsVisible) UpdateVideoPlacement();
            }
            catch (Exception ex)
            {
                Log.Warn("打开视频失败: " + ex.Message);
                ShowMessage("无法打开视频：\n" + ex.Message);
            }
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
            ShowMessage("无法播放这个视频：\n" + reason);
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

        /// <summary>把视频子窗口贴到本控件在屏幕上的位置（卡片拖动、换排布时都会重新算）。</summary>
        void UpdateVideoPlacement()
        {
            if (_videoWindow == null || _disposed) return;

            Window card = Window.GetWindow(this);
            if (card == null || !card.IsVisible) return;

            try
            {
                Point origin = PointToScreen(new Point(0, 0));
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                double sx = dpi.DpiScaleX > 0.1 ? dpi.DpiScaleX : 1;
                double sy = dpi.DpiScaleY > 0.1 ? dpi.DpiScaleY : 1;

                double radius = _settings != null && _settings.Rounded ? _cornerRadius : 0;
                _videoWindow.Place(origin.X / sx, origin.Y / sy, ActualWidth, ActualHeight, radius);
            }
            catch (Exception ex)
            {
                Log.Warn("定位视频窗失败: " + ex.Message);
            }
        }

        // ==================== 尺寸 ====================

        /// <summary>按卡片圆角裁掉媒体的四个角（关闭「圆角」时保持方角）。</summary>
        void ApplyClip()
        {
            if (_settings == null || !_settings.Rounded)
            {
                Clip = null;
                return;
            }

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height)) return;

            double radius = Math.Max(0, Math.Min(_cornerRadius, Math.Min(width, height) / 2));
            Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        }

        void ApplyFit()
        {
            Stretch stretch = ParseStretch(_settings.Fit);
            _image.Stretch = stretch;
            if (_videoWindow != null) _videoWindow.Video.Stretch = stretch;
        }

        void ApplySize()
        {
            double width;
            if (_settings.Width > 0) width = _settings.Width;
            else if (_natural.Width > 0) width = _natural.Width;
            else width = 320;

            width = Math.Max(40, Math.Min(3000, width));

            double height;
            if (_settings.Height > 0) height = _settings.Height;
            else if (_natural.Width > 0 && _natural.Height > 0) height = width * _natural.Height / _natural.Width;
            else height = width * 9 / 16.0;

            Width = width;
            Height = Math.Max(20, Math.Min(3000, height));

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
            _host.Children.Clear();
        }

        // ==================== 播放控制 ====================

        public void Play()
        {
            if (_disposed) return;
            try
            {
                if (_gif != null) _gif.Play();
                if (_videoWindow != null)
                {
                    UpdateVideoPlacement();
                    _videoWindow.Play();
                    UpdateVideoPlacement();
                }
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
                if (_videoWindow != null) _videoWindow.Pause();
            }
            catch { }
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

            _host.Children.Clear();
            _image.Source = null;
            _loadedPath = null;
        }

        void ReleaseVideo()
        {
            _pendingVideo = null;
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
            Unload();
        }
    }
}
