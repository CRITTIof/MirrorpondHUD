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

    // K-21：目标位置按**物理像素**保存。窗口的 Left/Top/Width/Height 是 DIP，跨不同缩放屏幕时
    // 必须用窗口自己所在屏幕的 DPI 换算；先存像素，才能在 WM_DPICHANGED 之后用同一目标重算。
    Rect _targetPx = Rect.Empty;
    double _targetRadiusDips;
    bool _dpiWarned;

    // K-9：region 只依赖"尺寸 + 圆角"，与窗口位置无关。历史 P-9 把 X/Y 也算进等价判断，
    // 于是拖动卡片时每帧都 CreateRoundRectRgn + SetWindowRgn(bRedraw=true)，整窗重绘 → 闪。
    // K-21 起这三个值都是物理像素（之前是 DIP），region 本身也只跟像素有关。
    double _lastRegionWidth = -1;
    double _lastRegionHeight = -1;
    double _lastRegionRadius = -1;

    // K-19：扩展样式 / z-order 的失败要可见，但不能每帧刷屏：用状态转移 + 最小间隔节流。
    static readonly LogThrottle StyleThrottle = new("设置视频窗扩展样式", 60000);
    static readonly LogThrottle ZOrderThrottle = new("调整视频窗 z-order", 60000);
    bool _zOrderFallbackLogged;
    bool _nonTopmostAnchorFallbackLogged;

    static readonly IntPtr HwndTopmost = new(-1);
    const uint GwHwndPrev = 3;
    const uint GwOwner = 4;

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
                // K-20：不再默认置顶。视频窗的层级由 RaiseAboveOwner 以卡片为锚点决定，
                // 默认 Topmost=true 会把它抬到所有置顶窗口之上。
                Topmost = false,
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

            // K-21：跨不同缩放屏幕时窗口 DPI 会变（WM_DPICHANGED）。WPF 会按系统建议矩形缩放窗口，
            // 必须用保存的物理像素目标重新落位，并按新 DPI 重建圆角 region。
            _window.DpiChanged += (s, e) => ApplyPlacement();

            _window.SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(_window).Handle;
                if (hwnd == IntPtr.Zero) return;

                // K-19：鼠标穿透 / 不抢焦点 / 不进 Alt+Tab 全靠这三项样式，
                // 读或写失败都会让视频窗变成"抢焦点、挡鼠标"的窗口，必须留痕。
                if (!MediaNative.TryGetExStyle(hwnd, out int ex))
                {
                    StyleThrottle.Failure("读取视频窗扩展样式失败，鼠标穿透 / 不抢焦点可能失效");
                }
                else if (!MediaNative.SetExStyle(hwnd, ex | MediaNative.WsExTransparent |
                                                       MediaNative.WsExNoActivate | MediaNative.WsExToolWindow))
                {
                    StyleThrottle.Failure("设置视频窗扩展样式失败，鼠标穿透 / 不抢焦点可能失效");
                }
                else
                {
                    StyleThrottle.Success();
                }

                // K-10：窗口句柄就绪后再套一次透明度（Place 可能在窗口建好前就调用过）。
                if (_opacity < 0.999) ApplyOpacity();

                // K-21：句柄就绪后按当前 DPI 补一次落位 / region（Place 可能早于句柄创建）。
                ApplyPlacement();
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

    /// <summary>
    /// 跟随卡片位置 / 大小。参数是**物理像素**的屏幕坐标与卡片圆角（DIP）。
    /// K-21：跨不同缩放屏幕时，DIP 换算必须用视频窗自己所在屏幕的 DPI，
    /// 所以先把像素目标存下来，再由 <see cref="ApplyPlacement"/> 统一换算与落位。
    /// </summary>
    public void Place(double leftPx, double topPx, double widthPx, double heightPx, double radiusDips)
    {
        if (_window is null || _disposed) return;
        if (widthPx < 8 || heightPx < 8) return;

        var target = new Rect(Math.Round(leftPx, 1), Math.Round(topPx, 1),
                              Math.Round(widthPx, 1), Math.Round(heightPx, 1));
        if (_targetPx == target && Math.Abs(_targetRadiusDips - radiusDips) < 0.01) return;

        _targetPx = target;
        _targetRadiusDips = Math.Max(0, radiusDips);
        ApplyPlacement();
    }

    /// <summary>
    /// K-21：按保存的像素目标 + 窗口当前 DPI 落位并重建 region。
    /// 首次放置、SourceInitialized、WM_DPICHANGED 都走这里，保证"换算用哪套 DPI"只有一处。
    /// </summary>
    void ApplyPlacement()
    {
        if (_window is null || _disposed || _targetPx.IsEmpty) return;

        Matrix fromDevice = FromDevice();
        Point topLeft = fromDevice.Transform(new Point(_targetPx.X, _targetPx.Y));
        Vector size = fromDevice.Transform(new Vector(_targetPx.Width, _targetPx.Height));
        double radiusPx = fromDevice.M11 > 0.01 ? _targetRadiusDips / fromDevice.M11 : _targetRadiusDips;

        _placing = true;
        try
        {
            // 只赋值真正变化的维度：重复设置 Left/Top 也会多走一次窗口移动 + 布局。
            if (Math.Abs(_window.Left - topLeft.X) > 0.05) _window.Left = topLeft.X;
            if (Math.Abs(_window.Top - topLeft.Y) > 0.05) _window.Top = topLeft.Y;
            if (Math.Abs(_window.Width - size.X) > 0.05) _window.Width = size.X;
            if (Math.Abs(_window.Height - size.Y) > 0.05) _window.Height = size.Y;

            ApplyRegion(_targetPx.Width, _targetPx.Height, radiusPx);
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
    /// 屏幕像素 → 窗口 DIP 的换算矩阵。窗口还没接上合成树时按自身 DPI 兜底，
    /// 等 SourceInitialized / DpiChanged 后会再用真实矩阵重算一次（K-21）。
    /// </summary>
    Matrix FromDevice()
    {
        PresentationSource source = PresentationSource.FromVisual(_window);
        if (source?.CompositionTarget is not null) return source.CompositionTarget.TransformFromDevice;

        double sx = 1, sy = 1;
        try
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(_window);
            if (dpi.DpiScaleX > 0.1) sx = dpi.DpiScaleX;
            if (dpi.DpiScaleY > 0.1) sy = dpi.DpiScaleY;
        }
        catch (Exception ex)
        {
            // 只在第一次失败时记录：后面每次布局都会重试，刷屏没有意义。
            if (!_dpiWarned)
            {
                _dpiWarned = true;
                Log.Warn($"读取视频窗 DPI 失败，先按 100% 处理（随后 DpiChanged 会再校正）: {ex.Message}");
            }
        }
        return new Matrix(1.0 / sx, 0, 0, 1.0 / sy, 0, 0);
    }

    /// <summary>按物理像素尺寸与圆角重建 region；尺寸 / 圆角没变就不碰窗口（K-9）。</summary>
    void ApplyRegion(double widthPx, double heightPx, double radiusPx)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int w = Math.Max(1, (int)Math.Round(widthPx));
            int h = Math.Max(1, (int)Math.Round(heightPx));
            int r = Math.Max(0, (int)Math.Round(radiusPx));

            if (Math.Abs(_lastRegionWidth - w) < 0.5 &&
                Math.Abs(_lastRegionHeight - h) < 0.5 &&
                Math.Abs(_lastRegionRadius - r) < 0.5)
                return;

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
                // K-19：SetWindowRgn 失败后 region 还归我们所有，删掉才不会漏 GDI 句柄；
                // 连删除都失败说明句柄状态异常，必须留痕（长时间拖窗口可能累积泄漏）。
                if (!DeleteObject(region)) Log.Warn("删除视频窗 region 失败，GDI 资源可能泄漏");
                Log.Warn("设置视频窗圆角 region 失败，保持窗口方形");
                return;
            }

            _lastRegionWidth = w;
            _lastRegionHeight = h;
            _lastRegionRadius = r;
        }
        catch (Exception ex)
        {
            Log.Warn($"视频窗圆角失败: {ex.Message}");
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
                    // K-19：读 / 写失败都要可见；写失败就保留 _layered，下次再试。
                    if (MediaNative.TryGetExStyle(hwnd, out int ex) &&
                        MediaNative.SetExStyle(hwnd, ex & ~MediaNative.WsExLayered))
                    {
                        StyleThrottle.Success();
                        _layered = false;
                    }
                    else
                    {
                        StyleThrottle.Failure("移除视频窗分层样式失败，视频窗会继续走较慢的合成路径");
                    }
                }
                return true;
            }

            if (!_layered)
            {
                // K-19：启用分层合成失败（远程桌面 / 老驱动 / 句柄失效）时返回 false，
                // 让调用方走 K-10 的 WPF 合成降级，而不是假称透明度已生效。
                if (!MediaNative.TryGetExStyle(hwnd, out int ex))
                {
                    StyleThrottle.Failure("读取视频窗扩展样式失败，无法启用分层合成");
                    Log.Warn("视频窗不透明度设置失败：读不到扩展样式，按不支持分层合成处理");
                    return false;
                }

                if (!MediaNative.SetExStyle(hwnd, ex | MediaNative.WsExLayered))
                {
                    StyleThrottle.Failure("启用视频窗分层样式失败，按不支持分层合成处理");
                    Log.Warn("视频窗不透明度设置失败：无法启用分层合成");
                    return false;
                }

                StyleThrottle.Success();
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
    /// K-20：把视频窗摆到卡片窗口的**正上方**，而不是抬到 HWND_TOPMOST ——
    /// 后者会压过所有置顶窗口，连鼠标穿透的卡片都会遮住别人的置顶窗。
    /// 现在的锚点是"卡片上方紧邻的那个窗口"：视频窗插在锚点与卡片之间，
    /// 卡片的置顶状态决定视频窗所在的 z-order 带，层级跟着卡片走。
    /// </summary>
    public void RaiseAboveOwner()
    {
        if (_window is null || _disposed) return;

        try
        {
            IntPtr handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;

            IntPtr owner = GetWindow(handle, GwOwner);
            if (owner == IntPtr.Zero && _owner is not null && _owner.IsLoaded)
            {
                // 视频窗创建时卡片可能还没 Loaded，owner 关系没建立；这里补一次，
                // 补上之后才能以卡片为锚点（否则视频只能退回置顶，层级不跟卡片）。
                _window.Owner = _owner;
                owner = GetWindow(handle, GwOwner);
            }

            if (owner == IntPtr.Zero)
            {
                // 没有 owner 关系就没有锚点，无法跟随卡片。退回置顶至少保证视频可见，
                // 但必须留痕：这正是"视频压过其它置顶窗口"的已知退化路径（K-20）。
                const uint NoMoveNoSizeNoActivate = 0x0002 | 0x0001 | 0x0010;
                if (!SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, NoMoveNoSizeNoActivate))
                {
                    ZOrderThrottle.Failure("视频窗没有 owner 句柄，且退回置顶也失败，视频可能被卡片盖住");
                }
                else if (!_zOrderFallbackLogged)
                {
                    // 退化本身要留一条原因：否则只能从"视频压过别的置顶窗"猜出这里走了退路。
                    _zOrderFallbackLogged = true;
                    Log.Warn("视频窗没有 owner 句柄，退回 HWND_TOPMOST（层级不再跟随卡片，可能压过其它置顶窗口）");
                }
                return;
            }

            IntPtr anchor = GetWindow(owner, GwHwndPrev);
            if (anchor == handle)
            {
                ZOrderThrottle.Success();   // 已经紧贴卡片上方，不用再动
                return;
            }

            IntPtr insertAfter;
            string fallback = null;
            bool? ownerTopmost = TryIsTopmost(owner);

            if (ownerTopmost is null)
            {
                // 读不到卡片的置顶状态：退回置顶至少保证视频可见（否则可能沉到卡片背后），
                // 但原因必须留痕，真机上才能对应到"视频压过其它置顶窗口"的现象。
                insertAfter = HwndTopmost;
                fallback = "读取卡片扩展样式失败，退回置顶以免视频被卡片盖住";
            }
            else if (ownerTopmost == true)
            {
                // 卡片本身置顶：锚点存在时插在锚点下方（正好紧贴卡片上方）；
                // 没有锚点说明卡片就是最前排，这时视频窗也只能排在最前面。
                insertAfter = anchor != IntPtr.Zero ? anchor : HwndTopmost;
                if (anchor == IntPtr.Zero) fallback = "卡片是最前排的置顶窗口，视频窗只能排在它前面";
            }
            else
            {
                // 卡片不置顶（含“仅桌面”模式）：只有锚点也不是置顶窗时才插到它下面。
                // 绝不能用 HWND_TOP（IntPtr.Zero）——那会把视频抬到所有普通窗口上方，
                // 看起来就像“富内容卡还置顶”。没有安全锚点时保持 owner 关系，不强行改 Z 序。
                bool? anchorTopmost = TryIsTopmost(anchor);
                if (anchor != IntPtr.Zero && anchorTopmost == false)
                {
                    insertAfter = anchor;
                }
                else
                {
                    if (!_nonTopmostAnchorFallbackLogged)
                    {
                        _nonTopmostAnchorFallbackLogged = true;
                        Log.Debug("视频窗没有非置顶锚点；保持 owner 关系，不把它抬到普通窗口之上");
                    }
                    return;
                }
            }

            const uint SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
            if (!SetWindowPos(handle, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
            {
                ZOrderThrottle.Failure($"调整视频窗 z-order 失败（锚点 {DescribeInsertAfter(insertAfter)}），视频可能被卡片盖住");
                return;
            }

            ZOrderThrottle.Success();
            if (fallback is not null && !_zOrderFallbackLogged)
            {
                // 退化只在每个视频窗首次发生时记一条：每次卡片重排都会调用 RaiseAboveOwner。
                _zOrderFallbackLogged = true;
                Log.Warn($"视频窗 z-order 退化：{fallback}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"把视频窗抬到卡片之上失败: {ex.Message}");
        }
    }

    /// <summary>读窗口的 WS_EX_TOPMOST；null = 读不到（不拿"读不到"当真值猜）。</summary>
    static bool? TryIsTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        if (!MediaNative.TryGetExStyle(hwnd, out int ex)) return null;
        return (ex & MediaNative.WsExTopmost) != 0;
    }

    static string DescribeInsertAfter(IntPtr insertAfter) =>
        insertAfter == HwndTopmost ? "HWND_TOPMOST"
        : insertAfter == IntPtr.Zero ? "HWND_TOP"
        : $"0x{insertAfter.ToInt64():X}";

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

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

        // 释放路径统一走 SafeRelease（X-9）：MediaElement 在句柄已失效时 Stop/Close 会抛，
        // 属于预期竞态（记 Debug）；其它异常仍以 Warn 留痕，不再空 catch。
        SafeRelease.Run(() =>
        {
            _video.Stop();
            _video.Close();
            _video.Source = null;
        }, "视频 MediaElement");

        // 窗口可能已随卡片一起关闭，Close 抛异常也没有补救动作，同样交给 SafeRelease。
        Window window = _window;
        SafeRelease.Run(() => window?.Close(), "视频子窗");
        _window = null;
    }
}
