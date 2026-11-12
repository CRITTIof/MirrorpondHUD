using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 信息与文本卡的粒子聚字层：把当前文字快照采样成点集，粒子从旧位置收拢，
/// 收敛后直接切回真正的 <see cref="TextCanvas"/>（瞬时凝固）并停表。
///
/// 性能与手感取舍：
///  · 无惯性：位置直接指数逼近目标，不存速度、不冲过头；文字变化时立即重新配对；
///  · 粒子位置跨播放保留，文字只改一个字符时大部分粒子不需要重新散开；
///  · 默认按 x 排序就近配对目标（近似最近邻，避免 O(n²)）；
///  · 画布尺寸变化（首次布局未完成、卡片长高）会重新采样，避免末排目标缺失；
///  · 点数 / 大小 / 速度 / 轮廓采样 / 是否就近都可配，点数和采样步长有硬上限；
///  · 30fps，凝固后立即停；截图 / 减少动效 / 非信息文本卡由宿主直接不调用。
/// </summary>
internal sealed class ParticleTextLayer : FrameworkElement
{
    const double FrameMs = 33.0;
    const int MinParticles = 200;
    const int MaxParticles = 3000;
    const byte AlphaThreshold = 24;
    const double SnapDistance = 0.6;
    const double BaseSettleMs = 220.0;
    const double Responsiveness = 24.0;

    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = new();
    readonly Random _random = new(20261111);

    double[] _x = Array.Empty<double>();
    double[] _y = Array.Empty<double>();
    double[] _tx = Array.Empty<double>();
    double[] _ty = Array.Empty<double>();

    Brush _brush = Brushes.White;
    TextCanvas _canvas;
    int _count;
    int _residentCount;
    int _requestedCount = 900;
    bool _hasParticles;
    bool _playing;
    bool _pending;
    bool _resampling;
    bool _sampling;
    int _version;
    double _size = 2.2;
    double _speed = 1.0;
    double _settleMs = BaseSettleMs;
    double _settleElapsedMs;
    double _sampleWidth;
    double _sampleHeight;
    bool _nearest = true;
    bool _geometry;

    public ParticleTextLayer()
    {
        IsHitTestVisible = false;
        // 窗口本身会裁，粒子层不再多裁一层：末排粒子可以稍微压到卡片边缘外。
        ClipToBounds = false;
        Visibility = Visibility.Collapsed;
        SnapsToDevicePixels = false;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FrameMs)
        };
        _timer.Tick += OnTick;

        Unloaded += (s, e) => Stop();
    }

    public bool IsPlaying => _playing;

    /// <summary>排一次播放；真正的采样与上屏等布局完成后再做。返回 false 表示画布还没量好。</summary>
    public bool SchedulePlay(TextCanvas canvas, Color color, StyleSettings style)
    {
        if (canvas is null || canvas.ActualWidth < 2 || canvas.ActualHeight < 2) return false;

        double size = Math.Max(0.8, Math.Min(4.0, style is null ? 2.2 : style.TextParticleSize));
        int count = (int)Math.Max(MinParticles, Math.Min(MaxParticles, style is null ? 900 : style.TextParticleCount));
        double speed = Math.Max(0.2, Math.Min(4.0, style is null ? 1.0 : style.TextParticleSpeed));
        bool nearest = style is null || style.TextParticleNearest;
        bool geometry = style is not null && style.TextParticleGeometry;

        _requestedCount = count;
        _nearest = nearest;
        _geometry = geometry;

        int version = ++_version;
        _pending = true;
        _resampling = false;
        TextCanvas target = canvas;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (version != _version || !_pending) return;
            _pending = false;
            BeginPlay(target, color, size, count, speed, nearest, geometry, version);
        }), DispatcherPriority.Loaded);
        return true;
    }

    public void Stop()
    {
        _version++;
        _pending = false;
        _resampling = false;
        _timer.Stop();
        _clock.Stop();
        _playing = false;
        _count = 0;

        Opacity = 1;
        Visibility = Visibility.Collapsed;
        ReleaseCanvas();
    }

    /// <summary>把画布恢复成不透明并断开尺寸监听；不动粒子位置，下次播放继续用。</summary>
    void ReleaseCanvas()
    {
        if (_canvas is null) return;

        _canvas.SizeChanged -= OnCanvasSizeChanged;
        _canvas.BeginAnimation(UIElement.OpacityProperty, null);
        _canvas.Opacity = 1;
        _canvas = null;
    }

    void BeginPlay(TextCanvas canvas, Color color, double size, int requestedCount,
                   double speed, bool nearest, bool geometry, int version)
    {
        if (version != _version) return;

        Stop();
        _version = version;

        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width < 2 || height < 2 || !canvas.IsVisible)
            return;

        try
        {
            long started = Stopwatch.GetTimestamp();
            List<Point> targets;
            _sampling = true;
            try
            {
                if (!SampleTargets(canvas, width, height, requestedCount, geometry, out targets))
                    return;
            }
            finally
            {
                _sampling = false;
            }

            bool firstPool = !_hasParticles;
            _size = size;
            _sampleWidth = width;
            _sampleHeight = height;
            ApplyTargets(targets, nearest);

            // 第一次建立粒子池只在目标位落点，不让粒子层盖住刚显示的文字；
            // 下一次文字变化才有“直接前往新目标”的位移。
            if (firstPool)
            {
                Log.Info($"粒子文字就位：{targets.Count} 点 / {width:0}×{height:0} DIP");
                return;
            }

            _speed = speed;
            _settleMs = Math.Max(80.0, Math.Min(1000.0, BaseSettleMs / speed));
            _settleElapsedMs = 0;
            _brush = new SolidColorBrush(Color.FromArgb(232, color.R, color.G, color.B));
            _brush.Freeze();
            _canvas = canvas;
            _canvas.SizeChanged += OnCanvasSizeChanged;
            _canvas.Opacity = 0;
            _count = targets.Count;
            _playing = true;
            Visibility = Visibility.Visible;
            Opacity = 1;

            _clock.Restart();
            _timer.Start();
            InvalidateVisual();
            Log.Info($"粒子文字启动：{_count} 点 / {width:0}×{height:0} DIP / "
                + (geometry ? "几何采样" : "填充采样") + (nearest ? " / 就近配对" : " / 随机配对")
                + $" / 采样 {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms");
        }
        catch (Exception ex)
        {
            Log.Warn($"粒子文字启动失败: {ex.Message}");
            Stop();
        }
    }

    /// <summary>采样时临时把画布恢复为不透明：粒子播放中画布 Opacity=0，直接渲染会采到空白。</summary>
    bool SampleTargets(TextCanvas canvas, double width, double height,
                       int requestedCount, bool geometry, out List<Point> targets)
    {
        double restore = canvas.Opacity;
        canvas.Opacity = 1;
        try
        {
            return BuildTargets(canvas, width, height, requestedCount, geometry, out targets);
        }
        finally
        {
            canvas.Opacity = restore;
        }
    }

    void EnsureCapacity(int count)
    {
        if (_x.Length >= count) return;

        int capacity = Math.Max(count, Math.Max(64, _x.Length * 2));
        Array.Resize(ref _x, capacity);
        Array.Resize(ref _y, capacity);
        Array.Resize(ref _tx, capacity);
        Array.Resize(ref _ty, capacity);
    }

    /// <summary>
    /// 配对并落位。首帧与新增粒子直接落在目标点：粒子不做从外圈聚拢，也不做散开，
    /// 之后文字变化时旧粒子保持原位、直接朝新目标移动。
    /// </summary>
    void ApplyTargets(List<Point> targets, bool nearest)
    {
        int count = targets.Count;
        int oldResident = _residentCount;
        bool first = !_hasParticles;

        EnsureCapacity(count);

        if (first)
        {
            for (int i = 0; i < count; i++)
            {
                _x[i] = targets[i].X;
                _y[i] = targets[i].Y;
            }
            _hasParticles = true;
        }
        else if (count > oldResident)
        {
            for (int i = oldResident; i < count; i++)
            {
                _x[i] = targets[i].X;
                _y[i] = targets[i].Y;
            }
        }

        AssignTargets(targets, nearest);

        int snapFrom = first ? 0 : oldResident;
        for (int i = snapFrom; i < count; i++)
        {
            _x[i] = _tx[i];
            _y[i] = _ty[i];
        }

        _residentCount = count;
    }

    /// <summary>
    /// 粒子方块有半径，目标中心必须留在图层内，否则末排只露出上半格。
    /// 单独抽成静态函数，自检可以直接验证边界。
    /// </summary>
    internal static double ClampCenter(double value, double extent, double size)
    {
        if (extent <= size) return extent * 0.5;
        double half = Math.Max(0.5, size * 0.5);
        return Math.Max(half, Math.Min(extent - half, value));
    }

    Point ClampTarget(Point target) =>
        new(ClampCenter(target.X, _sampleWidth, _size),
            ClampCenter(target.Y, _sampleHeight, _size));

    void AssignTargets(List<Point> targets, bool nearest)
    {
        int count = targets.Count;
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        if (nearest)
        {
            // 近似最近邻：粒子与目标各自按 x 排序后顺序配对。文字只改一两个字符时，
            // 大部分粒子仍会落在原来的笔画附近，不会被随机目标整句甩开。
            int[] particles = new int[count];
            for (int i = 0; i < count; i++) particles[i] = i;
            Array.Sort(particles, (a, b) => _x[a].CompareTo(_x[b]));
            Array.Sort(order, (a, b) => targets[a].X.CompareTo(targets[b].X));

            for (int rank = 0; rank < count; rank++)
            {
                int p = particles[rank];
                Point target = ClampTarget(targets[order[rank]]);
                _tx[p] = target.X;
                _ty[p] = target.Y;
            }
            return;
        }

        for (int i = count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (int i = 0; i < count; i++)
        {
            Point target = ClampTarget(targets[order[i]]);
            _tx[i] = target.X;
            _ty[i] = target.Y;
        }
    }

    static bool BuildTargets(TextCanvas canvas, double width, double height,
                             int requestedCount, bool geometry, out List<Point> targets)
    {
        targets = new List<Point>();
        // 多采 1 像素：排版尺寸向上取整时，末排最右 / 最下一个字不会落在位图外。
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width) + 1);
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height) + 1);

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);

        int stride = pixelWidth * 4;
        byte[] pixels = new byte[stride * pixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        long area = (long)pixelWidth * pixelHeight;
        int targetCount = Math.Max(MinParticles, Math.Min(MaxParticles, requestedCount));

        var candidates = new List<Point>(Math.Min(12000, targetCount * 2));
        if (geometry)
        {
            // 轮廓采样：取 alpha 边界。对图标 / 键帽 / 混排也有效，不依赖单字体 FormattedText。
            for (int y = 0; y < pixelHeight; y++)
            {
                int row = y * stride;
                for (int x = 0; x < pixelWidth; x++)
                {
                    int index = row + x * 4 + 3;
                    if (pixels[index] < AlphaThreshold) continue;

                    bool edge = x == 0 || y == 0 || x == pixelWidth - 1 || y == pixelHeight - 1
                             || pixels[row + (x - 1) * 4 + 3] < AlphaThreshold
                             || pixels[row + (x + 1) * 4 + 3] < AlphaThreshold
                             || pixels[row - stride + x * 4 + 3] < AlphaThreshold
                             || pixels[row + stride + x * 4 + 3] < AlphaThreshold;
                    if (!edge) continue;

                    candidates.Add(new Point(x + 0.5, y + 0.5));
                    if (candidates.Count >= 12000) break;
                }
                if (candidates.Count >= 12000) break;
            }
        }

        if (candidates.Count < targetCount)
        {
            int step = area > 160_000 ? 3 : (area > 60_000 ? 2 : 1);
            for (int y = 0; y < pixelHeight; y += step)
            {
                int row = y * stride;
                for (int x = 0; x < pixelWidth; x += step)
                {
                    if (pixels[row + x * 4 + 3] < AlphaThreshold) continue;
                    candidates.Add(new Point(x + step * 0.5, y + step * 0.5));
                    if (candidates.Count >= 12000) break;
                }
                if (candidates.Count >= 12000) break;
            }
        }

        if (candidates.Count == 0) return false;

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int take = Math.Min(targetCount, candidates.Count);
        var result = new List<Point>(take);
        for (int i = 0; i < take; i++) result.Add(candidates[i]);
        targets = result;
        return true;
    }

    /// <summary>
    /// 首次布局还没量完就采样会漏掉最后一行；卡片长高时目标也要补。
    /// 尺寸变化后按新画布重新采样，粒子位置保留，只换目标。
    /// </summary>
    void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_playing || _sampling || _canvas is null || _resampling) return;

        double width = _canvas.ActualWidth;
        double height = _canvas.ActualHeight;
        if (Math.Abs(width - _sampleWidth) < 0.5 && Math.Abs(height - _sampleHeight) < 0.5) return;

        _resampling = true;
        int version = _version;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _resampling = false;
            if (version != _version || !_playing || _canvas is null || _sampling) return;

            TextCanvas canvas = _canvas;
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 2 || h < 2) return;
            if (Math.Abs(w - _sampleWidth) < 0.5 && Math.Abs(h - _sampleHeight) < 0.5) return;

            _sampling = true;
            try
            {
                if (!SampleTargets(canvas, w, h, _requestedCount, _geometry, out var targets))
                    return;

                _sampleWidth = w;
                _sampleHeight = h;
                ApplyTargets(targets, _nearest);
                _count = targets.Count;
                _settleElapsedMs = 0;
                InvalidateVisual();
                Log.Info($"粒子文字重新采样：{_count} 点 / {w:0}×{h:0} DIP");
            }
            catch (Exception ex)
            {
                Log.Warn($"粒子文字重新采样失败: {ex.Message}");
            }
            finally
            {
                _sampling = false;
            }
        }), DispatcherPriority.Loaded);
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!_playing) return;

        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (dt <= 0) return;
        if (dt > 0.1) dt = 0.1;

        // 单独的凝固计时：_clock 每帧都在重启，不能拿它判断是否到点。
        _settleElapsedMs += dt * 1000.0;

        // 指数逼近：没有速度项，也不会冲过头。factor 按本帧时长求，掉帧时一步追上。
        double factor = 1.0 - Math.Exp(-Responsiveness * _speed * dt);
        bool allSnapped = true;

        for (int i = 0; i < _count; i++)
        {
            double dx = _tx[i] - _x[i];
            double dy = _ty[i] - _y[i];
            if (Math.Abs(dx) <= SnapDistance && Math.Abs(dy) <= SnapDistance)
            {
                _x[i] = _tx[i];
                _y[i] = _ty[i];
                continue;
            }

            _x[i] += dx * factor;
            _y[i] += dy * factor;
            allSnapped = false;
        }

        InvalidateVisual();

        if (allSnapped || _settleElapsedMs >= _settleMs)
            FinishInstant();
    }

    void FinishInstant()
    {
        // 收敛后把粒子贴到目标位置，下一次文字变化直接从干净字形附近起步。
        for (int i = 0; i < _count; i++)
        {
            _x[i] = _tx[i];
            _y[i] = _ty[i];
        }

        // 只停当前这一轮；不要碰 _version / _pending：
        // 它们可能已经属于刚排入的下一次播放，旧动画收尾不能把它取消。
        _timer.Stop();
        _clock.Stop();
        _playing = false;
        _count = 0;
        Visibility = Visibility.Collapsed;
        ReleaseCanvas();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_playing || _count == 0) return;

        double half = _size * 0.5;
        for (int i = 0; i < _count; i++)
        {
            double x = _x[i];
            double y = _y[i];
            if (x < -_size || y < -_size || x > ActualWidth + _size || y > ActualHeight + _size) continue;
            dc.DrawRectangle(_brush, null, new Rect(x - half, y - half, _size, _size));
        }
    }
}
