using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MirrorpondHUD.Core;


namespace MirrorpondHUD.Ui.Controls.Media;

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

    // K-9：region 只依赖"尺寸 + 圆角"，与窗口位置无关。历史 P-9 把 X/Y 也算进等价判断，
    // 于是拖动卡片时每帧都 CreateRoundRectRgn + SetWindowRgn(bRedraw=true)，整窗重绘 → 闪。
    double _lastRegionWidth = -1;
    double _lastRegionHeight = -1;
    double _lastRegionRadius = -1;

    // K-10：卡片不透明度。视频在独立 HWND 里，不继承卡片窗口的 WPF Opacity，
    // 必须自己用分层窗口的整窗 alpha 同步，否则"图片半透明、视频全不透明"。
    double _opacity = 1;
    bool _layered;

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

    public bool IsOpen => _window is not null;

    public MediaElement Video => _video;

    /// <summary>打开视频并显示子窗口；重复调用只切源。</summary>
    public void Open(string path, MediaSettings settings)
    {
        if (_disposed) return;

        EnsureWindow();
        // EnsureWindow 失败时它自己已经写了日志（"创建视频窗失败"）；窗口没建起来
        // Open 只能放弃继续，调用方（TryStartVideo）随后把 MediaFailed 的路径走完。
        if (_window is null) return;

        _video.Stretch = ParseStretch(settings is null ? "Contain" : settings.Fit);
        _video.IsMuted = settings is null || settings.Muted;
        _video.Volume = _video.IsMuted ? 0 : 1;

        try
        {
            _video.SpeedRatio = settings is not null && settings.Speed > 0 ? settings.Speed : 1.0;
        }
        catch (Exception ex)
        {
            // MediaElement 在部分格式上不支持变速：不算致命，但要留痕并把提示交给状态行。
            Log.Warn($"系统解码器不支持这个速度（{settings?.Speed:0.##}×），按 1× 播放: {ex.Message}");
            try { _video.SpeedRatio = 1.0; } catch (Exception reset) { Log.Warn($"恢复 1× 播放失败: {reset.Message}"); }
        }

        var uri = new Uri(path, UriKind.Absolute);
        if (_video.Source is null || _video.Source != uri) _video.Source = uri;

        if (settings is null || settings.Autoplay) Play();
    }

    void EnsureWindow()
    {
        if (_window is not null) return;

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

            if (_owner is not null && _owner.IsLoaded) _window.Owner = _owner;

            _window.SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(_window).Handle;
                if (hwnd == IntPtr.Zero) return;

                int ex = MediaNative.GetExStyle(hwnd);
                ex |= MediaNative.WsExTransparent | MediaNative.WsExNoActivate | MediaNative.WsExToolWindow;
                MediaNative.SetExStyle(hwnd, ex);

                // K-10：窗口句柄就绪后再套一次透明度（Place 可能在窗口建好前就调用过）。
                if (_opacity < 0.999) ApplyOpacity();
            };

            _window.Show();
            _window.Hide();
        }
        catch (Exception ex)
        {
            Log.Warn($"创建视频窗失败: {ex.Message}");
            _window = null;
        }
    }

    /// <summary>跟随卡片位置 / 大小；参数是屏幕 DIP 坐标与卡片圆角。</summary>
    public void Place(double left, double top, double width, double height, double radius)
    {
        if (_window is null || _disposed) return;
        if (width < 8 || height < 8) return;

        var rect = new Rect(Math.Round(left, 1), Math.Round(top, 1),
                            Math.Round(width, 1), Math.Round(height, 1));
        if (_lastRect == rect) return;

        // K-9：分开判断"移动"与"改尺寸"。region 与位置无关，只有尺寸 / 圆角变化才重建。
        Rect previous = _lastRect;
        bool sizeChanged = previous.IsEmpty ||
                           Math.Abs(previous.Width - rect.Width) > 0.05 ||
                           Math.Abs(previous.Height - rect.Height) > 0.05;
        bool moved = previous.IsEmpty ||
                     Math.Abs(previous.X - rect.X) > 0.05 ||
                     Math.Abs(previous.Y - rect.Y) > 0.05;

        _lastRect = rect;
        _placing = true;
        try
        {
            // 只赋值真正变化的维度：WPF 里重复设置 Left/Top 也会走一次窗口移动 + 布局。
            if (moved)
            {
                _window.Left = rect.X;
                _window.Top = rect.Y;
            }

            if (sizeChanged)
            {
                _window.Width = rect.Width;
                _window.Height = rect.Height;
            }

            if (sizeChanged || Math.Abs(_lastRegionRadius - radius) > 0.01 ||
                Math.Abs(_lastRegionWidth - rect.Width) > 0.05 ||
                Math.Abs(_lastRegionHeight - rect.Height) > 0.05)
                ApplyRegion(rect.Width, rect.Height, radius);
        }
        catch (Exception ex)
        {
            Log.Warn($"定位视频窗失败: {ex.Message}");
        }
        finally
        {
            _placing = false;
        }
    }

    /// <summary>
    /// K-10：把卡片不透明度同步到视频子窗口。
    /// 返回 false = 当前窗口/驱动组合不支持分层合成（远程桌面、部分老驱动），
    /// 调用方必须走可合成降级路径并把原因写进日志/状态行，不能假装成功。
    /// </summary>
    public bool SetOpacity(double value)
    {
        _opacity = double.IsNaN(value) ? 1 : Math.Max(0, Math.Min(1, value));
        if (_disposed || _window is null) return _opacity >= 0.999;
        return ApplyOpacity();
    }

    bool ApplyOpacity()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return false;   // 窗口还没建好：SourceInitialized 里会补

            if (_opacity >= 0.999)
            {
                // 不透明时移除分层样式：分层窗口会让 MediaElement 走更慢的合成路径。
                if (_layered)
                {
                    int ex = MediaNative.GetExStyle(hwnd);
                    MediaNative.SetExStyle(hwnd, ex & ~MediaNative.WsExLayered);
                    _layered = false;
                }
                return true;
            }

            if (!_layered)
            {
                int ex = MediaNative.GetExStyle(hwnd);
                MediaNative.SetExStyle(hwnd, ex | MediaNative.WsExLayered);
                _layered = true;
            }

            byte alpha = (byte)Math.Round(_opacity * 255);
            if (MediaNative.SetWindowAlpha(hwnd, alpha)) return true;

            // 失败必须留痕：这是"视频不透明度不生效"的唯一线索。
            Log.Warn($"视频窗不透明度设置失败（{_opacity:0.##}），将按不支持分层合成处理");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"视频窗不透明度设置异常: {ex.Message}");
            return false;
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
            catch (Exception ex)
            {
                // 取 DPI 失败按 100% 处理即可（位置/尺寸仍可用），只记录一次原因。
                Log.Warn($"读取视频窗 DPI 失败，按 100% 处理: {ex.Message}");
            }

            int w = Math.Max(1, (int)Math.Round(width * scale));
            int h = Math.Max(1, (int)Math.Round(height * scale));
            int r = Math.Max(0, (int)Math.Round(radius * scale));

            IntPtr region = CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2);
            if (region == IntPtr.Zero)
            {
                Log.Warn("创建视频窗圆角 region 失败，保持窗口方形");
                return;
            }

            // K-9：bRedraw=false —— 尺寸变化本身会触发 WM_SIZE 重绘；强制整窗重绘
            // 只会让拖动/贴边时视频画面闪一下。
            // SetWindowRgn 成功后 region 归系统所有，不能再删。
            if (SetWindowRgn(hwnd, region, false) == 0)
            {
                DeleteObject(region);
                Log.Warn("设置视频窗圆角 region 失败，保持窗口方形");
                return;
            }

            _lastRegionWidth = width;
            _lastRegionHeight = height;
            _lastRegionRadius = radius;
        }
        catch (Exception ex)
        {
            Log.Warn($"视频窗圆角失败: {ex.Message}");
        }
    }

    public void Play()
    {
        if (_disposed) return;
        try
        {
            if (_window is null) return;
            if (!_window.IsVisible) _window.Show();
            RaiseAboveOwner();
            _video.Play();
        }
        catch (Exception ex)
        {
            Log.Warn($"视频播放失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 把视频窗插到卡片窗口的正上方。卡片每次 SetWindowPos(HWND_TOPMOST) 之后
    /// 都会跑到自己的"从属窗口"上面去，必须重新摆一次，否则视频会被卡片背景盖住。
    /// </summary>
    public void RaiseAboveOwner()
    {
        if (_window is null || _disposed) return;

        try
        {
            IntPtr handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;

            // hWndInsertAfter 是"排在它前面（更靠后）"的窗口，所以这里直接抬到置顶带最前面，
            // 才能保证视频压在卡片上面。卡片每次置顶后都要重新调用。
            IntPtr topmost = new(-1);
            const uint SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
            if (!SetWindowPos(handle, topmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
                Log.Warn("把视频窗抬到卡片之上失败（可能被卡片背景盖住）");
        }
        catch (Exception ex)
        {
            Log.Warn($"把视频窗抬到卡片之上失败: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    public void Pause()
    {
        try
        {
            _video.Pause();
            if (_window is not null && _window.IsVisible) _window.Hide();
        }
        catch (Exception ex)
        {
            // 暂停失败会让用户以为按键没生效：必须留日志（状态行由 CardMediaView 同步）。
            Log.Warn($"暂停系统解码视频失败: {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            _video.Stop();
            _video.Source = null;
            if (_window is not null && _window.IsVisible) _window.Hide();
        }
        catch (Exception ex)
        {
            Log.Warn($"停止系统解码视频失败: {ex.Message}");
        }
    }

    public bool IsPlacing => _placing;

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

        // 释放路径：MediaElement.Stop/Close 在句柄已失效时会抛，但没有任何补救动作，
        // 也不该把它当成用户可见失败（X-9：保留并注明原因）。
        try
        {
            _video.Stop();
            _video.Close();
            _video.Source = null;
        }
        catch { }

        // 释放路径：窗口此时可能已经随卡片一起关闭，Close 抛异常无补救动作。
        try
        {
            if (_window is not null) _window.Close();
        }
        catch { }

        _window = null;
    }
}
