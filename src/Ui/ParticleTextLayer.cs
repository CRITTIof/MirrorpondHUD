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
/// 信息与文本卡的粒子聚字层：把当前文字快照采样成点集，粒子从旧位置/外侧收拢，
/// 收敛后直接切回真正的 <see cref="TextCanvas"/>（瞬时凝固）并停表。
///
/// 性能取舍：
///  · 粒子位置跨播放保留，文字只改一个字符时大部分粒子不需要重新散开；
///  · 默认按 x 排序就近配对目标（近似最近邻，避免 O(n²)）；
///  · 点数 / 大小 / 速度 / 轮廓采样 / 是否就近都可配，点数和采样步长有硬上限；
///  · 30fps，收敛后立即停；截图 / 减少动效 / 非信息文本卡由宿主直接不调用。
/// </summary>
internal sealed class ParticleTextLayer : FrameworkElement
{
    const double FrameMs = 33.0;
    const int MinParticles = 200;
    const int MaxParticles = 3000;
    const byte AlphaThreshold = 24;
    const double Stiffness = 34.0;
    const double Damping = 0.82;

    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = new();
    readonly Random _random = new(20261111);

    double[] _x = Array.Empty<double>();
    double[] _y = Array.Empty<double>();
    double[] _vx = Array.Empty<double>();
    double[] _vy = Array.Empty<double>();
    double[] _tx = Array.Empty<double>();
    double[] _ty = Array.Empty<double>();

    Brush _brush = Brushes.White;
    TextCanvas _canvas;
    int _count;
    int _residentCount;
    bool _hasParticles;
    bool _playing;
    bool _pending;
    int _version;
    double _size = 2.2;
    double _settleMs = 700;

    public ParticleTextLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
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

        int version = ++_version;
        _pending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (version != _version || !_pending) return;
            _pending = false;
            BeginPlay(canvas, color, size, count, speed, nearest, geometry, version);
        }), DispatcherPriority.Loaded);
        return true;
    }

    public void Stop()
    {
        _version++;
        _pending = false;
        _timer.Stop();
        _clock.Stop();
        _playing = false;
        _count = 0;

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Visibility = Visibility.Collapsed;

        if (_canvas is not null)
        {
            _canvas.BeginAnimation(UIElement.OpacityProperty, null);
            _canvas.Opacity = 1;
            _canvas = null;
        }
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
            if (!BuildTargets(canvas, width, height, requestedCount, geometry, out var targets))
                return;

            EnsureParticles(targets.Count, width, height);
            AssignTargets(targets, nearest);

            _size = size;
            _settleMs = Math.Max(160, Math.Min(2800, 700.0 / speed));
            _brush = new SolidColorBrush(Color.FromArgb(232, color.R, color.G, color.B));
            _brush.Freeze();
            _canvas = canvas;
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

    void EnsureParticles(int count, double width, double height)
    {
        if (_x.Length < count)
        {
            int capacity = Math.Max(count, Math.Max(64, _x.Length * 2));
            Array.Resize(ref _x, capacity);
            Array.Resize(ref _y, capacity);
            Array.Resize(ref _vx, capacity);
            Array.Resize(ref _vy, capacity);
            Array.Resize(ref _tx, capacity);
            Array.Resize(ref _ty, capacity);
        }

        if (!_hasParticles)
        {
            _hasParticles = true;
            _residentCount = count;
            for (int i = 0; i < count; i++)
            {
                double angle = _random.NextDouble() * Math.PI * 2.0;
                double radius = Math.Max(width, height) * (0.38 + _random.NextDouble() * 0.32);
                _x[i] = width * 0.5 + Math.Cos(angle) * radius;
                _y[i] = height * 0.5 + Math.Sin(angle) * radius;
                _vx[i] = 0;
                _vy[i] = 0;
            }
            return;
        }

        if (count > _residentCount)
        {
            // 新增粒子从当前粒子群的质心附近起步，不从屏幕外突然出现。
            double cx = 0, cy = 0;
            for (int i = 0; i < _residentCount; i++)
            {
                cx += _x[i];
                cy += _y[i];
            }
            if (_residentCount > 0)
            {
                cx /= _residentCount;
                cy /= _residentCount;
            }
            else
            {
                cx = width * 0.5;
                cy = height * 0.5;
            }

            for (int i = _residentCount; i < count; i++)
            {
                _x[i] = cx + (_random.NextDouble() - 0.5) * 60;
                _y[i] = cy + (_random.NextDouble() - 0.5) * 60;
                _vx[i] = 0;
                _vy[i] = 0;
            }
        }

        _residentCount = count;
        Array.Clear(_vx, 0, count);
        Array.Clear(_vy, 0, count);
    }

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
                Point target = targets[order[rank]];
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
            Point target = targets[order[i]];
            _tx[i] = target.X;
            _ty[i] = target.Y;
        }
    }

    static bool BuildTargets(TextCanvas canvas, double width, double height,
                             int requestedCount, bool geometry, out List<Point> targets)
    {
        targets = new List<Point>();
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height));

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

    void OnTick(object sender, EventArgs e)
    {
        if (!_playing) return;

        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (dt < 0.004) dt = 0.004;
        if (dt > 0.05) dt = 0.05;

        for (int i = 0; i < _count; i++)
        {
            _vx[i] += (_tx[i] - _x[i]) * Stiffness * dt;
            _vy[i] += (_ty[i] - _y[i]) * Stiffness * dt;
            _vx[i] *= Damping;
            _vy[i] *= Damping;
            _x[i] += _vx[i] * dt;
            _y[i] += _vy[i] * dt;
        }

        InvalidateVisual();

        if (_clock.Elapsed.TotalMilliseconds >= _settleMs)
            FinishInstant();
    }

    void FinishInstant()
    {
        // 不保留 1 秒粒子静止帧，也不做淡出：到点直接切回清晰文字。
        _timer.Stop();
        _playing = false;
        _count = 0;
        Visibility = Visibility.Collapsed;
        if (_canvas is not null)
        {
            _canvas.BeginAnimation(UIElement.OpacityProperty, null);
            _canvas.Opacity = 1;
            _canvas = null;
        }
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
