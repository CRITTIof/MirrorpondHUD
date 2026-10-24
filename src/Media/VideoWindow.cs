using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;

namespace MirrorpondHUD.Media
{
    /// <summary>
    /// 视频卡片专用的"视频窗"。
    ///
    /// 为什么不能直接把 MediaElement 放进卡片窗口：叠层是 WS_EX_LAYERED 的分层窗口
    /// （AllowsTransparency=true），WPF 在这种窗口上用软件渲染，MediaElement 的视频画面
    /// 会被整块丢弃，只剩背景色。所以视频单独开一个不透明的子窗口，贴在卡片的位置上：
    ///  · Owner = 卡片窗口 —— 永远压在卡片上面，卡片置顶时它也跟着置顶；
    ///  · WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW —— 鼠标穿透、不抢焦点、不进 Alt+Tab；
    ///  · SetWindowRgn 做圆角 + 卡片圆角一致，看起来还是那张圆角卡片。
    /// </summary>
    internal sealed class VideoWindow : IDisposable
    {
        readonly Window _owner;
        readonly MediaElement _video;
        Window _window;
        bool _disposed;
        bool _placing;
        Rect _lastRect = Rect.Empty;

        public VideoWindow(Window owner)
        {
            _owner = owner;
            _video = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                ScrubbingEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }

        public bool IsOpen { get { return _window != null; } }

        public MediaElement Video { get { return _video; } }

        /// <summary>打开视频并显示子窗口；重复调用只切源。</summary>
        public void Open(string path, MediaSettings settings)
        {
            if (_disposed) return;

            EnsureWindow();
            if (_window == null) return;

            _video.Stretch = ParseStretch(settings == null ? "Contain" : settings.Fit);
            _video.IsMuted = settings == null || settings.Muted;
            _video.Volume = _video.IsMuted ? 0 : 1;

            try
            {
                _video.SpeedRatio = settings != null && settings.Speed > 0 ? settings.Speed : 1.0;
            }
            catch { }

            var uri = new Uri(path, UriKind.Absolute);
            if (_video.Source == null || _video.Source != uri) _video.Source = uri;

            if (settings == null || settings.Autoplay) Play();
        }

        void EnsureWindow()
        {
            if (_window != null) return;

            try
            {
                _window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = false,
                    Background = Brushes.Black,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = true,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    SizeToContent = SizeToContent.Manual,
                    Focusable = false,
                    Content = _video,
                    Width = 1,
                    Height = 1,
                    Left = -32000,
                    Top = -32000
                };

                if (_owner != null && _owner.IsLoaded) _window.Owner = _owner;

                _window.SourceInitialized += (s, e) =>
                {
                    IntPtr hwnd = new WindowInteropHelper(_window).Handle;
                    if (hwnd == IntPtr.Zero) return;

                    int ex = Native.GetExStyle(hwnd);
                    ex |= Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow;
                    Native.SetExStyle(hwnd, ex);
                };

                _window.Show();
                _window.Hide();
            }
            catch (Exception ex)
            {
                Log.Warn("创建视频窗失败: " + ex.Message);
                _window = null;
            }
        }

        /// <summary>跟随卡片位置 / 大小；参数是屏幕 DIP 坐标与卡片圆角。</summary>
        public void Place(double left, double top, double width, double height, double radius)
        {
            if (_window == null || _disposed) return;
            if (width < 8 || height < 8) return;

            var rect = new Rect(Math.Round(left, 1), Math.Round(top, 1),
                                Math.Round(width, 1), Math.Round(height, 1));
            if (_lastRect == rect) return;

            _lastRect = rect;
            _placing = true;
            try
            {
                _window.Left = rect.X;
                _window.Top = rect.Y;
                _window.Width = rect.Width;
                _window.Height = rect.Height;

                ApplyRegion(rect.Width, rect.Height, radius);
            }
            catch { }
            finally
            {
                _placing = false;
            }
        }

        void ApplyRegion(double width, double height, double radius)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(_window).Handle;
                if (hwnd == IntPtr.Zero) return;

                double scale = 1.0;
                try
                {
                    DpiScale dpi = VisualTreeHelper.GetDpi(_window);
                    if (dpi.DpiScaleX > 0.1) scale = dpi.DpiScaleX;
                }
                catch { }

                int w = Math.Max(1, (int)Math.Round(width * scale));
                int h = Math.Max(1, (int)Math.Round(height * scale));
                int r = Math.Max(0, (int)Math.Round(radius * scale));

                IntPtr region = CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2);
                if (region == IntPtr.Zero) return;

                // SetWindowRgn 成功后 region 归系统所有，不能再删。
                if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
            }
            catch (Exception ex)
            {
                Log.Warn("视频窗圆角失败: " + ex.Message);
            }
        }

        public void Play()
        {
            if (_disposed) return;
            try
            {
                if (_window == null) return;
                if (!_window.IsVisible) _window.Show();
                RaiseAboveOwner();
                _video.Play();
            }
            catch (Exception ex)
            {
                Log.Warn("视频播放失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 把视频窗插到卡片窗口的正上方。卡片每次 SetWindowPos(HWND_TOPMOST) 之后
        /// 都会跑到自己的"从属窗口"上面去，必须重新摆一次，否则视频会被卡片背景盖住。
        /// </summary>
        public void RaiseAboveOwner()
        {
            if (_window == null || _disposed) return;

            try
            {
                IntPtr handle = new WindowInteropHelper(_window).Handle;
                if (handle == IntPtr.Zero) return;

                // hWndInsertAfter 是"排在它前面（更靠后）"的窗口，所以这里直接抬到置顶带最前面，
                // 才能保证视频压在卡片上面。卡片每次置顶后都要重新调用。
                IntPtr topmost = new IntPtr(-1);
                const uint SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
                SetWindowPos(handle, topmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        public void Pause()
        {
            try
            {
                _video.Pause();
                if (_window != null && _window.IsVisible) _window.Hide();
            }
            catch { }
        }

        public void Stop()
        {
            try
            {
                _video.Stop();
                _video.Source = null;
                if (_window != null && _window.IsVisible) _window.Hide();
            }
            catch { }
        }

        public bool IsPlacing { get { return _placing; } }

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

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll")]
        static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _video.Stop();
                _video.Close();
                _video.Source = null;
            }
            catch { }

            try
            {
                if (_window != null) _window.Close();
            }
            catch { }

            _window = null;
        }
    }
}
