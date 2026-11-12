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

    /// <summary>T-5：候选点上限。超过就只取前 N 个（后面还要随机洗牌，不会偏向某一行）。</summary>
    const int MaxCandidatePoints = 12000;

    /// <summary>
    /// T-5：单次采样的像素预算。4K 卡片（约 1200×900 DIP）在 100% 下是 108 万像素，
    /// 还在预算内；再大的卡按面积比降采样，避免一次采样就拷出十几 MB 的字节数组。
    /// </summary>
    const long MaxSamplePixels = 2_000_000;

    /// <summary>RTB 每像素 4 字节（Pbgra32）。</summary>
    const int BytesPerPixel = 4;

    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = new();
    readonly Random _random = new(20261111);

    double[] _x = Array.Empty<double>();
    double[] _y = Array.Empty<double>();
    double[] _tx = Array.Empty<double>();
    double[] _ty = Array.Empty<double>();

    // —— T-5：采样缓冲按尺寸缓存，跨轮复用 ——
    /// <summary>采样用的离屏位图；尺寸变化或有降采样时才重建。</summary>
    RenderTargetBitmap _rtb;
    int _rtbPixelWidth;
    int _rtbPixelHeight;
    int _rtbStride;
    /// <summary>实际"像素 / DIP"比例（1.0 = 原尺寸；小于 1 表示触发了像素预算降级）。</summary>
    double _rtbScale = 1.0;
    /// <summary>像素缓冲：只增不减，尺寸来回变时不反复分配大数组。</summary>
    byte[] _pixels = Array.Empty<byte>();
    readonly DrawingVisual _sampleVisual = new();
    readonly List<Point> _candidates = new(MaxCandidatePoints);
    readonly List<Point> _targets = new();
    readonly HashSet<int> _hideSet = new();
    int[] _order = Array.Empty<int>();
    int[] _particleOrder = Array.Empty<int>();
    List<Point> _sortTargets;
    readonly LogThrottle _sampleLog = new("粒子采样降级", 30000);
    readonly Comparer<int> _particleComparer;
    readonly Comparer<int> _targetComparer;

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
    HashSet<int> _targetChars;

    /// <summary>
    /// T-6：本轮播放开始时 <see cref="TextCanvas.Text"/> 的快照。尺寸变化重采样时的
    /// 差集必须以它为基线；用"当前文本 + 上一轮下标"会把未变字符判成变化。
    /// </summary>
    string _playText = "";

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

        // T-5：比较器只建一次，避免每次配对都分配两个 lambda 闭包。
        _particleComparer = Comparer<int>.Create((a, b) => _x[a].CompareTo(_x[b]));
        _targetComparer = Comparer<int>.Create((a, b) => _sortTargets[a].X.CompareTo(_sortTargets[b].X));

        Unloaded += (s, e) => Stop();
    }

    public bool IsPlaying => _playing;

    /// <summary>是否已经建立过完整粒子池；首池只落点，不做可见位移动画。</summary>
    public bool HasPool => _hasParticles;

    /// <summary>这一轮粒子收敛结束；宿主据此恢复普通文字。</summary>
    public event Action Finished;

    /// <summary>排一次播放；真正的采样与上屏等布局完成后再做。返回 false 表示画布还没量好。</summary>
    public bool SchedulePlay(TextCanvas canvas, Color color, StyleSettings style,
                             IReadOnlyCollection<int> targetChars = null, int? countOverride = null)
    {
        if (canvas is null || canvas.ActualWidth < 2 || canvas.ActualHeight < 2) return false;

        double size = Math.Max(0.8, Math.Min(4.0, style is null ? 2.2 : style.TextParticleSize));
        int count = countOverride ?? (int)Math.Max(MinParticles, Math.Min(MaxParticles, style is null ? 900 : style.TextParticleCount));
        count = (int)Math.Max(40, Math.Min(MaxParticles, count));
        double speed = Math.Max(0.2, Math.Min(4.0, style is null ? 1.0 : style.TextParticleSpeed));
        bool nearest = style is null || style.TextParticleNearest;
        bool geometry = style is not null && style.TextParticleGeometry;

        var targetSet = targetChars is null ? null : new HashSet<int>(targetChars);
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
            BeginPlay(target, color, size, count, speed, nearest, geometry, targetSet, version);
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
        _canvas?.ClearHiddenChars();
        ReleaseCanvas();
    }

    /// <summary>把画布恢复成不透明并断开尺寸监听；不动粒子位置，下次播放继续用。</summary>
    void ReleaseCanvas()
    {
        if (_canvas is null) return;

        _canvas.SizeChanged -= OnCanvasSizeChanged;
        _canvas = null;
    }

    void BeginPlay(TextCanvas canvas, Color color, double size, int requestedCount,
                   double speed, bool nearest, bool geometry,
                   HashSet<int> targetChars, int version)
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
            // T-6：先把本轮文本快照与差集绑定好；采样（含尺寸变化重采样）以它为基线。
            _playText = canvas.Text ?? "";
            _targetChars = targetChars;

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
                Finished?.Invoke();
                return;
            }

            _speed = speed;
            _settleMs = Math.Max(80.0, Math.Min(1000.0, BaseSettleMs / speed));
            _settleElapsedMs = 0;
            _brush = new SolidColorBrush(Color.FromArgb(232, color.R, color.G, color.B));
            _brush.Freeze();
            _canvas = canvas;
            _canvas.SizeChanged += OnCanvasSizeChanged;
            // 与粒子层显示放在同一个 UI 回调里：不能先由宿主藏字、再等 Loaded 出粒子，
            // 否则 WPF 可能在两者之间合成一帧缺字画面。
            if (_targetChars is not null) _canvas.SetHiddenChars(_targetChars);
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
                       int requestedCount, bool geometry, out List<Point> targets) =>
        BuildTargets(canvas, width, height, requestedCount, geometry,
                     ResolveChangedTargets(canvas), out targets);

    /// <summary>
    /// T-6：解析本轮应交给粒子层的"变化字符"。
    /// 文本没变时直接复用本轮差集；动画期间文本被换掉时按「本轮起点 → 当前文本」重算，
    /// 索引基数同步切到当前文本——否则会把上一句的下标套在当前句上，隐藏错字符，
    /// 粒子轮廓也会来自上一句。
    /// </summary>
    HashSet<int> ResolveChangedTargets(TextCanvas canvas)
    {
        string current = canvas.Text ?? "";
        if (string.Equals(current, _playText, StringComparison.Ordinal))
            return _targetChars;

        int[] changed = OverlaySurface.ChangedIndices(_playText, current);
        _playText = current;
        _targetChars = changed.Length == 0 ? null : new HashSet<int>(changed);
        if (canvas is not null)
        {
            if (_targetChars is not null) canvas.SetHiddenChars(_targetChars);
            else canvas.ClearHiddenChars();
        }
        return _targetChars;
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
        EnsureOrderCapacity(count);

        int[] order = _order;
        for (int i = 0; i < count; i++) order[i] = i;

        if (nearest)
        {
            // 近似最近邻：粒子与目标各自按 x 排序后顺序配对。文字只改一两个字符时，
            // 大部分粒子仍会落在原来的笔画附近，不会被随机目标整句甩开。
            int[] particles = _particleOrder;
            for (int i = 0; i < count; i++) particles[i] = i;
            Array.Sort(particles, 0, count, _particleComparer);
            _sortTargets = targets;
            Array.Sort(order, 0, count, _targetComparer);

            for (int rank = 0; rank < count; rank++)
            {
                int p = particles[rank];
                Point target = ClampTarget(targets[order[rank]]);
                _tx[p] = target.X;
                _ty[p] = target.Y;
            }
            _sortTargets = null;
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

    /// <summary>T-5：配对用的两个下标数组按容量复用，不再每轮 new 两个 int[点数]。</summary>
    void EnsureOrderCapacity(int count)
    {
        if (_order.Length >= count) return;
        _order = new int[count];
        _particleOrder = new int[count];
    }

    /// <summary>
    /// T-5：整块采样。RTB / 像素数组 / 候选点表全部按实例复用；像素数超过预算时按面积比
    /// 降采样（坐标再换算回 DIP），候选点上限顺次截断。降级原因走节流日志，不静默。
    /// </summary>
    bool BuildTargets(TextCanvas canvas, double width, double height,
                      int requestedCount, bool geometry,
                      HashSet<int> targetChars, out List<Point> targets)
    {
        targets = _targets;
        targets.Clear();

        // 多采 1 像素：排版尺寸向上取整时，末排最右 / 最下一个字不会落在位图外。
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width) + 1);
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height) + 1);

        // 采样遮罩与上屏遮罩同源（T-6/T-8）：按文本元素整簇隐藏，避免采到半个代理对。
        _hideSet.Clear();
        if (targetChars is not null)
        {
            BuildHideSet(canvas.Text ?? "", targetChars, _hideSet);
        }

        // 直接画到 DrawingVisual，不经过 TextCanvas 的可视缓存；否则隐藏字符后
        // RenderTargetBitmap 仍可能采到上一帧的完整文字，一个变化数字会摊出整行粒子。
        string degrade = null;
        using (DrawingContext dc = _sampleVisual.RenderOpen())
        {
            canvas.DrawForParticles(dc, targetChars is null ? null : _hideSet);
        }

        // 像素预算：降采样只影响采样精度，粒子目标本来就只是近似轮廓，不改变成品视觉。
        double scale = SampleScale(pixelWidth, pixelHeight);
        int rtbWidth = Math.Max(1, (int)Math.Ceiling(pixelWidth * scale));
        int rtbHeight = Math.Max(1, (int)Math.Ceiling(pixelHeight * scale));
        if (scale < 0.999)
        {
            degrade = $"卡片采样 {pixelWidth}×{pixelHeight} 超出像素预算，降采样到 {rtbWidth}×{rtbHeight}";
        }

        if (_rtb is null || _rtbPixelWidth != rtbWidth || _rtbPixelHeight != rtbHeight)
        {
            _rtb = new RenderTargetBitmap(rtbWidth, rtbHeight,
                                          96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
            _rtbPixelWidth = rtbWidth;
            _rtbPixelHeight = rtbHeight;
            _rtbStride = rtbWidth * BytesPerPixel;
            // 坐标反算必须用位图创建时的真实 DPI，不能跟着本次 scale 走：
            // 尺寸相同但 scale 只差一点点时不会重建位图，用错比例会让目标整体偏移。
            _rtbScale = scale;
        }

        int stride = _rtbStride;
        int needed = stride * rtbHeight;
        if (_pixels.Length < needed)
        {
            _pixels = new byte[needed];
        }

        // 必须 Clear：RenderTargetBitmap.Render 是累加的，不清会叠上一轮的字形。
        _rtb.Clear();
        _rtb.Render(_sampleVisual);
        _rtb.CopyPixels(_pixels, stride, 0);

        byte[] pixels = _pixels;
        long area = Math.Max(1, (long)Math.Ceiling(width) * (long)Math.Ceiling(height));
        int targetCount = Math.Max(MinParticles, Math.Min(MaxParticles, requestedCount));
        // 候选上限保持原值：先随机洗牌再取前 targetCount 个，分布与旧实现等价。
        int cap = MaxCandidatePoints;

        _candidates.Clear();
        if (geometry)
        {
            // 轮廓采样：取 alpha 边界。对图标 / 键帽 / 混排也有效，不依赖单字体 FormattedText。
            for (int y = 0; y < rtbHeight; y++)
            {
                int row = y * stride;
                for (int x = 0; x < rtbWidth; x++)
                {
                    int index = row + x * BytesPerPixel + 3;
                    if (pixels[index] < AlphaThreshold) continue;

                    bool edge = x == 0 || y == 0 || x == rtbWidth - 1 || y == rtbHeight - 1
                             || pixels[row + (x - 1) * BytesPerPixel + 3] < AlphaThreshold
                             || pixels[row + (x + 1) * BytesPerPixel + 3] < AlphaThreshold
                             || pixels[row - stride + x * BytesPerPixel + 3] < AlphaThreshold
                             || pixels[row + stride + x * BytesPerPixel + 3] < AlphaThreshold;
                    if (!edge) continue;

                    _candidates.Add(ToDip(x, y, 0.5));
                    if (_candidates.Count >= cap) break;
                }
                if (_candidates.Count >= cap) break;
            }
        }

        if (_candidates.Count < targetCount)
        {
            int step = area > 160_000 ? 3 : (area > 60_000 ? 2 : 1);
            for (int y = 0; y < rtbHeight; y += step)
            {
                int row = y * stride;
                for (int x = 0; x < rtbWidth; x += step)
                {
                    if (pixels[row + x * BytesPerPixel + 3] < AlphaThreshold) continue;
                    _candidates.Add(ToDip(x, y, step * 0.5));
                    if (_candidates.Count >= cap) break;
                }
                if (_candidates.Count >= cap) break;
            }
        }

        if (_candidates.Count == 0)
        {
            _sampleLog.Success();
            return false;
        }

        if (_candidates.Count >= cap && cap >= MaxCandidatePoints)
        {
            degrade = $"候选点达到上限 {MaxCandidatePoints}，只取前段（卡片 {width:0}×{height:0} DIP）";
        }

        if (degrade is not null) _sampleLog.Failure(degrade);
        else _sampleLog.Success();

        for (int i = _candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_candidates[i], _candidates[j]) = (_candidates[j], _candidates[i]);
        }

        int take = Math.Min(targetCount, _candidates.Count);
        for (int i = 0; i < take; i++) targets.Add(_candidates[i]);
        return true;
    }

    /// <summary>像素预算换算：1.0 = 原尺寸；超过预算按面积比缩小（不小于 0.25，避免糊到采不出版形）。</summary>
    static double SampleScale(int pixelWidth, int pixelHeight)
    {
        long pixels = Math.Max(1, (long)pixelWidth * pixelHeight);
        if (pixels <= MaxSamplePixels) return 1.0;
        return Math.Max(0.25, Math.Sqrt((double)MaxSamplePixels / pixels));
    }

    /// <summary>采样位图像素坐标 → DIP：位图按 <see cref="_rtbScale"/> 缩放，像素中心要反算回去。</summary>
    Point ToDip(int x, int y, double half) =>
        new((x + half) / _rtbScale, (y + half) / _rtbScale);

    /// <summary>
    /// T-6/T-8：把"变化字符"的补集整理成按文本元素对齐的隐藏集合。
    /// 一个代理对 / 组合字符簇只要有一半在变化集合里，就不能只隐藏另一半——
    /// 否则 RTB 会画半个 emoji / 孤立代理，粒子轮廓多出方块。
    /// </summary>
    static void BuildHideSet(string text, HashSet<int> targetChars, HashSet<int> hide)
    {
        int[] starts = TextCanvas.TextElementStarts(text);
        for (int k = 0; k < starts.Length; k++)
        {
            int start = starts[k];
            int end = k + 1 < starts.Length ? starts[k + 1] : text.Length;
            bool anyChanged = false;
            for (int i = start; i < end; i++)
            {
                if (targetChars.Contains(i))
                {
                    anyChanged = true;
                    break;
                }
            }
            if (!anyChanged) hide.Add(start);
        }
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
        // 新一轮已经排队时，旧动画收尾不能清掉它刚接管的遮罩。
        if (_pending) return;

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
        _canvas?.ClearHiddenChars();
        ReleaseCanvas();
        Finished?.Invoke();
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
