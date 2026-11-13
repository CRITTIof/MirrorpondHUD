using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>一次播放请求的结果：不可用 / 首池就位（不动画）/ 正在播放。</summary>
public enum ParticlePlayResult
{
    Unavailable,
    Primed,
    Animated
}

/// <summary>
/// 卡片内的“粒子聚字”层（2.47.0）：把一段文字采样成点集，粒子从上一段文字的位置收拢到新字形，
/// 收敛后立即停表并通知调用方切回普通文字。
///
/// 与宿主信息卡的 <c>ParticleTextLayer</c> 保持同一套观感参数（方块粒子、alpha 232、
/// 指数逼近无惯性、30fps 收敛即停）；差别是这里不做逐字符遮罩与模板掩码，
/// 整段文字一起聚，调用方负责动画期间隐藏原文。
///
/// 性能：采样位图只覆盖这段文字自己的尺寸；点数 / 大小 / 速度可调且有硬上限。
/// </summary>
public sealed class CardParticleText : FrameworkElement
{
    const double FrameMs = 33.0;
    const int MinParticles = 60;
    const int MaxParticles = 1600;
    const byte AlphaThreshold = 24;
    const double SnapDistance = 0.6;
    const double Responsiveness = 24.0;
    const int MaxCandidatePoints = 40000;
    const long MaxSamplePixels = 2_000_000;

    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = new();
    readonly Random _random = new(20261113);

    double[] _x = Array.Empty<double>();
    double[] _y = Array.Empty<double>();
    double[] _tx = Array.Empty<double>();
    double[] _ty = Array.Empty<double>();
    int _count;

    RenderTargetBitmap _rtb;
    int _pixelWidth;
    int _pixelHeight;
    byte[] _pixels = Array.Empty<byte>();

    Brush _brush = Brushes.White;
    bool _playing;
    double _speed = 1.0;
    double _size = 2.2;

    public CardParticleText()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        SnapsToDevicePixels = false;
        // 必须用 Hidden 而不是 Collapsed：折叠元素不参与布局，ActualWidth 恒为 0，
        // Play 会一直判定“画布还没准备好”。Hidden 保留测量结果，只是不渲染。
        Visibility = Visibility.Hidden;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FrameMs)
        };
        _timer.Tick += OnTick;
        Unloaded += (s, e) => Stop();
    }

    /// <summary>收敛结束（自然收敛或取消）；调用方据此恢复普通文字。</summary>
    public event Action Settled;

    /// <summary>是否已经有粒子池：首池只就位不动画，和宿主信息卡一致。</summary>
    public bool HasPool => _count > 0;

    public bool IsPlaying => _playing;

    /// <summary>粒子边长（DIP），默认 2.2，取值 0.8–4.0。</summary>
    public double ParticleSize
    {
        get => _size;
        set => _size = Math.Max(0.8, Math.Min(4.0, value));
    }

    /// <summary>粒子数量上限，默认 600，取值 60–1600；实际点数由字形墨迹决定。</summary>
    public int ParticleCount { get; set; } = 600;

    /// <summary>收敛速度倍率，0.2–4.0；越大越快。</summary>
    public double Speed
    {
        get => _speed;
        set => _speed = Math.Max(0.2, Math.Min(4.0, value));
    }

    public FontFamily TextFont { get; set; } = new("Microsoft YaHei UI");

    public double TextSize { get; set; } = 16;

    public FontWeight TextWeight { get; set; } = FontWeights.SemiBold;

    /// <summary>
    /// 播放一轮：采样 <paramref name="text"/> 的字形并朝它收拢。
    /// 返回 <see cref="ParticlePlayResult.Unavailable"/> 时调用方应直接显示原文；
    /// <see cref="ParticlePlayResult.Primed"/> 表示首池就位、本来就没有位移动画；
    /// <see cref="ParticlePlayResult.Animated"/> 表示粒子在播，收敛后触发 <see cref="Settled"/>。
    /// </summary>
    public ParticlePlayResult Play(string text, Color color)
    {
        if (string.IsNullOrWhiteSpace(text)) return ParticlePlayResult.Unavailable;

        double width = ActualWidth;
        if (width < 8)
        {
            // 布局还没完成：这一轮不播，调用方直接显示文字，等下一句再聚。
            return ParticlePlayResult.Unavailable;
        }

        if (!TrySample(text, width, out List<Point> targets, out _)) return ParticlePlayResult.Unavailable;
        if (targets.Count < 8) return ParticlePlayResult.Unavailable;

        bool firstPool = _count == 0;
        ApplyTargets(targets, firstPool);
        _brush = new SolidColorBrush(Color.FromArgb(232, color.R, color.G, color.B));
        _brush.Freeze();

        if (firstPool)
        {
            // 首池：粒子直接落在目标位，不播可见位移；调用方继续显示普通文字。
            Log.Debug($"粒子聚字就位：{_count} 点 / {width:0}×{ActualHeight:0} DIP");
            return ParticlePlayResult.Primed;
        }

        Visibility = Visibility.Visible;
        _playing = true;
        _clock.Restart();
        _timer.Start();
        InvalidateVisual();
        return ParticlePlayResult.Animated;
    }

    /// <summary>清空粒子池：换歌 / 卡片重建后第一句从静态开始。</summary>
    public void Reset()
    {
        Stop();
        _count = 0;
        Array.Clear(_x, 0, _x.Length);
        Array.Clear(_y, 0, _y.Length);
    }

    /// <summary>取消本轮（不发 <see cref="Settled"/>；调用方自己决定文字状态）。</summary>
    public void Stop()
    {
        _playing = false;
        _timer.Stop();
        _clock.Stop();
        Visibility = Visibility.Hidden;
    }

    void ApplyTargets(List<Point> targets, bool firstPool)
    {
        if (firstPool)
        {
            EnsureCapacity(targets.Count);
            _count = targets.Count;
            for (int i = 0; i < _count; i++)
            {
                _x[i] = targets[i].X;
                _y[i] = targets[i].Y;
                _tx[i] = targets[i].X;
                _ty[i] = targets[i].Y;
            }
            return;
        }

        int existing = _count;
        EnsureCapacity(targets.Count);
        _count = targets.Count;

        // 目标变多：多出来的粒子从旧粒子质心附近冒出来，而不是从 (0,0) 突然出现。
        double cx = 0, cy = 0;
        for (int i = 0; i < existing; i++) { cx += _x[i]; cy += _y[i]; }
        if (existing > 0) { cx /= existing; cy /= existing; }
        for (int i = existing; i < _count; i++)
        {
            _x[i] = cx + (_random.NextDouble() * 2 - 1) * 40;
            _y[i] = cy + (_random.NextDouble() * 2 - 1) * 40;
            _tx[i] = _x[i];
            _ty[i] = _y[i];
        }

        // 按 x 排序错位配对：近似最近邻，避免 O(n²) 的逐粒子搜索。
        int[] particleOrder = new int[_count];
        for (int i = 0; i < _count; i++) particleOrder[i] = i;
        Array.Sort(particleOrder, (a, b) => _x[a].CompareTo(_x[b]));

        var sortedTargets = new List<Point>(targets);
        sortedTargets.Sort((a, b) => a.X.CompareTo(b.X));

        for (int i = 0; i < _count; i++)
        {
            Point target = sortedTargets[i];
            _tx[particleOrder[i]] = target.X;
            _ty[particleOrder[i]] = target.Y;
        }
    }

    void EnsureCapacity(int count)
    {
        if (_x.Length >= count) return;
        int size = Math.Max(count, 128);
        _x = new double[size];
        _y = new double[size];
        _tx = new double[size];
        _ty = new double[size];
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!_playing || _count == 0) return;

        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (dt <= 0) return;
        dt = Math.Min(dt, 0.1);

        double k = 1.0 - Math.Exp(-dt * Responsiveness * _speed);
        double sum = 0;
        for (int i = 0; i < _count; i++)
        {
            _x[i] += (_tx[i] - _x[i]) * k;
            _y[i] += (_ty[i] - _y[i]) * k;
            double dx = _tx[i] - _x[i];
            double dy = _ty[i] - _y[i];
            sum += Math.Sqrt(dx * dx + dy * dy);
        }

        InvalidateVisual();
        if (sum / _count < SnapDistance) Finish("收敛");
    }

    void Finish(string reason)
    {
        _playing = false;
        _timer.Stop();
        _clock.Stop();
        Visibility = Visibility.Hidden;
        Log.Debug($"粒子聚字本轮结束: {reason}");
        Settled?.Invoke();
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

    /// <summary>把文字渲染到离屏位图，再按步长取墨迹像素作为目标点。</summary>
    bool TrySample(string text, double width, out List<Point> targets, out double height)
    {
        targets = null;
        height = 0;

        try
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double scale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            var typeface = new Typeface(TextFont, FontStyles.Normal, TextWeight, FontStretches.Normal);
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                Math.Max(8, TextSize),
                Brushes.White,
                dpi.PixelsPerDip)
            {
                MaxTextWidth = width
            };

            height = formatted.Height;
            if (height < 1 || formatted.Width < 1) return false;

            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

            // 像素预算：极长的一行在 4K 下也不至于一次拷出十几 MB。
            long pixels = (long)pixelWidth * pixelHeight;
            if (pixels > MaxSamplePixels)
            {
                double shrink = Math.Sqrt((double)MaxSamplePixels / pixels);
                scale *= shrink;
                pixelWidth = Math.Max(1, (int)(pixelWidth * shrink));
                pixelHeight = Math.Max(1, (int)(pixelHeight * shrink));
            }

            if (_rtb is null || _pixelWidth != pixelWidth || _pixelHeight != pixelHeight)
            {
                _rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                _pixelWidth = pixelWidth;
                _pixelHeight = pixelHeight;
            }

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawText(formatted, new Point(0, 0));
            }
            _rtb.Render(visual);

            int stride = pixelWidth * 4;
            int bytes = stride * pixelHeight;
            if (_pixels.Length < bytes) _pixels = new byte[bytes];
            _rtb.CopyPixels(_pixels, stride, 0);

            int ink = 0;
            for (int i = 3; i < bytes; i += 4)
            {
                if (_pixels[i] > AlphaThreshold) ink++;
            }
            if (ink == 0) return false;

            int wanted = (int)Math.Max(MinParticles, Math.Min(MaxParticles, ParticleCount));
            // 与宿主信息卡同一条取点规则：先按面积粗采样得到候选，再洗牌取固定数量。
            // 否则小字行会取到全部墨迹像素（远多于设置值），粒子叠成实体字，看不出聚拢过程。
            int area = pixelWidth * pixelHeight;
            int sampleStep = area > 160_000 ? 3 : (area > 60_000 ? 2 : 1);
            var points = new List<Point>(Math.Min(ink, MaxCandidatePoints));

            for (int y = 0; y < pixelHeight && points.Count < MaxCandidatePoints; y += sampleStep)
            {
                int row = y * stride;
                for (int x = 0; x < pixelWidth && points.Count < MaxCandidatePoints; x += sampleStep)
                {
                    if (_pixels[row + x * 4 + 3] <= AlphaThreshold) continue;
                    points.Add(new Point((x + 0.5) / scale, (y + 0.5) / scale));
                }
            }

            if (points.Count == 0) return false;
            if (points.Count > wanted)
            {
                // 部分 Fisher–Yates：只把前 wanted 个洗成随机样本，避免“前几行全取、后面没有”。
                for (int i = 0; i < wanted; i++)
                {
                    int j = i + _random.Next(points.Count - i);
                    (points[i], points[j]) = (points[j], points[i]);
                }
                points.RemoveRange(wanted, points.Count - wanted);
            }

            targets = points;
            return true;
        }
        catch (Exception ex)
        {
            // 采样失败只影响这一轮动画；文字照常显示，原因进日志（AGENTS §5）。
            Log.Warn($"粒子聚字采样失败: {ex.Message}");
            return false;
        }
    }
}
