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
///  · 2.45.2：采样位图只覆盖"变化字符的包围盒 + padding"，而不再按整卡尺寸建；
///    像素预算与填充步长仍按整卡算，保证粒子密度 / 精度与整卡采样等价；
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

    /// <summary>
    /// 2.45.2：变化包围盒（含 padding）达到整卡这个比例时不再收窄采样区域。
    /// 整段变化 / 文字铺满卡片时收窄省不下多少扫描量，却多一层"区域算得对不对"的风险；
    /// 正确性优先，这种情况退回整卡（日志里能看到原因）。
    /// </summary>
    const double RegionFallbackFraction = 0.9;

    /// <summary>
    /// 2.45.2：采样位图按这个像素粒度对齐分配。变化字符的位置 / 宽度每轮都会差几像素，
    /// 按精确尺寸建位图就会每轮新建一张 RenderTargetBitmap（等于每轮一次大分配）；
    /// 对齐后同一档尺寸可跨轮复用，实际拷贝仍只覆盖 usedWidth×usedHeight。
    /// </summary>
    const int SampleBitmapQuantum = 64;

    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = new();
    readonly Random _random = new(20261111);

    double[] _x = Array.Empty<double>();
    double[] _y = Array.Empty<double>();
    double[] _tx = Array.Empty<double>();
    double[] _ty = Array.Empty<double>();

    // —— T-5：采样缓冲按尺寸缓存，跨轮复用 ——
    /// <summary>采样用的离屏位图；对齐后的尺寸变化或有降采样时才重建（2.45.2 起按 64 像素对齐）。</summary>
    RenderTargetBitmap _rtb;
    int _rtbPixelWidth;
    int _rtbPixelHeight;
    /// <summary>实际"像素 / DIP"比例（1.0 = 原尺寸；小于 1 表示触发了像素预算降级）。</summary>
    double _rtbScale = 1.0;
    /// <summary>像素缓冲：只增不减，尺寸来回变时不反复分配大数组。</summary>
    byte[] _pixels = Array.Empty<byte>();
    /// <summary>本轮采样区域原点（DIP）：位图只覆盖区域，像素坐标反算要加回这个偏移。</summary>
    double _sampleOriginX;
    double _sampleOriginY;
    /// <summary>本轮采样区域（画布 DIP），只用于诊断日志。</summary>
    Rect _sampleRegion;
    /// <summary>本轮实际扫描的像素数（usedWidth × usedHeight），用来核对区域收窄是否真的生效。</summary>
    long _samplePixelCount;
    /// <summary>同一轮若按整卡采样会用的像素数，作为对比基准。</summary>
    long _sampleFullCardPixelCount;
    /// <summary>本轮候选点数。</summary>
    int _sampleCandidateCount;
    /// <summary>区域退回整卡的最近一次 Debug 打点（30s 节流）。</summary>
    long _regionFallbackLogTicks;
    /// <summary>文本元素矩形缓冲：跨轮复用，避免每次采样都新建 List。</summary>
    readonly List<Rect> _elementRects = new();
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

    /// <summary>
    /// T-20：本轮是否已回发过 <see cref="Finished"/>。结束路径有自然收敛、取消（<see cref="Stop"/>）、
    /// 画布未就绪的提前返回、采样不到字形、启动异常五种，宿主只该收到一次；
    /// 新一轮在 <see cref="SchedulePlay"/> 里复位。
    /// </summary>
    bool _finishNotified;

    /// <summary>画布未就绪的连续次数：只进日志，用来区分"偶尔布局慢"和"卡片一直被折叠"。</summary>
    int _earlyExitCount;
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
    /// 2.45.3：本轮"允许进入粒子"的字符掩码（true = 模板 {占位符} 的动态值）。
    /// null = 只做字形过滤（宿主拿不到模板 / 模板字面量定位失败）。
    /// </summary>
    bool[] _allowedMask;

    /// <summary>
    /// 2.45.3：按新文本重算允许掩码。动画中途换文本时，旧掩码的下标会错位，
    /// 所以必须按当前文本重算，而不是拿旧下标硬套。
    /// </summary>
    Func<string, bool[]> _allowedMaskFactory;

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
                             IReadOnlyCollection<int> targetChars = null, int? countOverride = null,
                             bool[] dynamicMask = null, Func<string, bool[]> maskFactory = null)
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
        // T-20：新一轮开始，允许本轮的结束事件回发一次（上一轮的已回发状态不能带进来）。
        _finishNotified = false;
        TextCanvas target = canvas;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (version != _version || !_pending) return;
            _pending = false;
            BeginPlay(target, color, size, count, speed, nearest, geometry, targetSet, version,
                      dynamicMask, maskFactory);
        }), DispatcherPriority.Loaded);
        return true;
    }

    public void Stop()
    {
        // T-20：取消也是"本轮结束"的一条出口。只有真的排了 / 在播的一轮才回发，
        // 避免 Unloaded 之类的空停路径给宿主发假事件。
        bool hadRound = _playing || _pending || _resampling;

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
        // 2.45.3：允许集合属于本轮，旧代的掩码 / 重算器不能带进下一轮（新轮由 BeginPlay 重新绑定）。
        _targetChars = null;
        _allowedMask = null;
        _allowedMaskFactory = null;
        ReleaseCanvas();

        if (hadRound) NotifyFinished("取消");
    }

    /// <summary>
    /// T-20：本轮结束的统一出口。自然收敛、取消、提前返回、异常全部走这里，
    /// 保证宿主收到且只收到一次 <see cref="Finished"/>，并且一定在 UI 线程上。
    ///
    /// 幂等靠 <see cref="_finishNotified"/>：同一轮重复调用直接忽略（例如异常路径里
    /// Stop 已经发过，catch 再兜一次）；新一轮由 SchedulePlay 复位。
    /// </summary>
    void NotifyFinished(string reason)
    {
        if (_finishNotified) return;

        // 宿主回调要碰可视树（ClearHiddenChars），非 UI 线程调 Stop 时必须转回 UI 线程；
        // 转投后重新检查幂等标志，避免两个线程各发一次。
        if (!CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => NotifyFinished(reason)));
            return;
        }

        _finishNotified = true;
        if (reason is not null) Log.Debug($"粒子文字本轮结束: {reason}");
        Finished?.Invoke();
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
                   HashSet<int> targetChars, int version,
                   bool[] allowedMask, Func<string, bool[]> maskFactory)
    {
        if (version != _version) return;

        Stop();
        _version = version;
        // T-20：上面这次 Stop 属于"旧一轮被新一轮顶掉"，它可能已经回发过一次；
        // 新一轮从此刻重新获得一次回发机会。
        _finishNotified = false;

        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width < 2 || height < 2 || !canvas.IsVisible)
        {
            // T-20：这一轮到此为止（布局还没完成 / 卡片被折叠），必须回发 Finished。
            // 原来直接 return，宿主会一直等一个永远不会来的动画结束事件。
            _earlyExitCount++;
            NotifyFinished($"画布未就绪 {width:0}×{height:0} DIP（第 {_earlyExitCount} 次）");
            return;
        }

        try
        {
            long started = Stopwatch.GetTimestamp();
            // T-6：先把本轮文本快照与差集绑定好；采样（含尺寸变化重采样）以它为基线。
            _playText = canvas.Text ?? "";
            _targetChars = targetChars;
            _allowedMask = allowedMask;
            _allowedMaskFactory = maskFactory;

            List<Point> targets;
            _sampling = true;
            try
            {
                if (!SampleTargets(canvas, width, height, requestedCount, geometry, out targets))
                {
                    // 空文本 / 全透明：本轮没有粒子可播，同样要收尾，否则宿主等不到结束事件。
                    NotifyFinished("没有可采样的字形");
                    return;
                }
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
                Log.Info($"粒子文字就位：{targets.Count} 点 / {width:0}×{height:0} DIP"
                    + $" / 采样 {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms"
                    + SampleRegionSummary());
                NotifyFinished("首池就位（不动画）");
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
                + $" / 采样 {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms"
                + SampleRegionSummary());
        }
        catch (Exception ex)
        {
            Log.Warn($"粒子文字启动失败: {ex.Message}");
            // Stop 负责复位状态与清遮罩并（若已有在播的一轮）回发结束事件；
            // 异常可能发生在任何状态之前，这里再兜一次，幂等标志保证宿主只收到一次。
            Stop();
            NotifyFinished("启动失败");
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
        // 2.45.3：动画中途换文本时重算的差集同样要过掩码 + 字形过滤，
        // 否则模板里写死的文字会在动画中途被卷进粒子。
        changed = FilterParticleTargets(current, changed, ResolveAllowedMask(current));
        _playText = current;
        // 空集合 = 本次变化全在模板字面量 / 图标上：明确"没有可播字符"，
        // 不能用 null（null 在任何调用方都意味着"不限制，整卡采样"）。
        _targetChars = new HashSet<int>(changed);
        if (canvas is not null)
        {
            // 空集合会先清再逐项加入（TextCanvas.SetHiddenChars），等于把遮罩撤干净；
            // 显式走 Clear 只是省一次无意义的 InvalidateVisual。
            if (_targetChars.Count > 0) canvas.SetHiddenChars(_targetChars);
            else canvas.ClearHiddenChars();
        }
        return _targetChars;
    }

    /// <summary>
    /// 2.45.3：图标 / 键帽码位。它们由专用字体绘制（TextCanvas 按 run 分流），既不是数据，
    /// 采样也只会把字形拆成一團糊状方块，所以永远不参与粒子。
    /// 键帽区间取自 Keycaps.First / Keycaps.Last（T-16 唯一来源）：Keycaps.IsKeycap 已被
    /// C-19 标记过时，这里直接按同一区间的常量判定；图标只认 Icons 登记过的码位——
    /// 两套字体共用 PUA，按整段判断会互相抢字形。
    /// </summary>
    internal static bool IsParticleGlyph(char c) =>
        (c >= Keycaps.First && c <= Keycaps.Last) || Icons.IsIconChar(c);

    /// <summary>
    /// 2.45.3：把变化下标过滤成"真正能交给粒子"的集合。
    /// 恒排除图标 / 键帽码位（与掩码是否可用无关）；掩码与文本等长时再剔除模板字面量。
    /// 掩码为空或长度不符（宿主拿不到模板、文本已被换掉）时只做字形过滤——
    /// 宁可让个别字面量动，也不能用一个错位的掩码把动态值判成静态。
    /// 纯函数，自检直接断言。
    /// </summary>
    internal static int[] FilterParticleTargets(string text, IReadOnlyList<int> changed, bool[] dynamicMask)
    {
        if (changed is null || changed.Count == 0) return Array.Empty<int>();

        text ??= "";
        bool useMask = dynamicMask is not null && dynamicMask.Length == text.Length;
        var kept = new List<int>(changed.Count);
        for (int k = 0; k < changed.Count; k++)
        {
            int index = changed[k];
            if (index < 0 || index >= text.Length) continue;
            if (IsParticleGlyph(text[index])) continue;
            if (useMask && !dynamicMask[index]) continue;
            kept.Add(index);
        }

        if (kept.Count == changed.Count && changed is int[] array) return array;
        return kept.ToArray();
    }

    /// <summary>
    /// 2.45.3：取当前文本的允许掩码。宿主给了重算器就一律按新文本重算：文本变长变短后
    /// 旧掩码的下标全部失效，长度相同也不代表位置相同。重算失败（模板定位不上）才退回
    /// 本轮开始时那份同长掩码，再不行就只做字形过滤。
    /// </summary>
    bool[] ResolveAllowedMask(string text)
    {
        bool[] fresh = _allowedMaskFactory?.Invoke(text);
        if (fresh is not null) return fresh;
        return _allowedMask is not null && _allowedMask.Length == text.Length ? _allowedMask : null;
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
    /// T-5：按采样区域渲染 / 扫描。RTB / 像素数组 / 候选点表全部按实例复用。
    ///
    /// 2.45.2：采样区域默认是整卡，但"只采变化字符"时收窄到变化字符的包围盒 + padding——
    /// 信息卡每秒改一个数字，原来也要清 / 渲染 / 拷贝 / 扫完整张卡。为了不改变成品视觉，
    /// 像素预算（降采样比例）与填充采样步长仍按整卡尺寸算：同一张卡上的粒子密度与精度
    /// 与整卡采样等价，只是被清 / 拷贝 / 扫描的像素少了。
    /// 取不到可信包围盒、裁剪省略号、或区域接近整卡时退回整卡（见 TryResolveSampleRegion）。
    /// </summary>
    bool BuildTargets(TextCanvas canvas, double width, double height,
                      int requestedCount, bool geometry,
                      HashSet<int> targetChars, out List<Point> targets)
    {
        targets = _targets;
        targets.Clear();

        // 2.45.3：调用方明确给了"没有允许字符"（本次变化全在模板字面量 / 图标上）时，
        // 不能退回整卡采样——那正好会把字面量卷进来。直接按"没有可采样的字形"收尾。
        if (targetChars is not null && targetChars.Count == 0) return false;

        // 采样遮罩与上屏遮罩同源（T-6/T-8）：按文本元素整簇隐藏，避免采到半个代理对。
        _hideSet.Clear();
        bool onlyChanged = targetChars is not null && targetChars.Count > 0;
        if (onlyChanged)
        {
            BuildHideSet(canvas.Text ?? "", targetChars, _hideSet);
        }

        // 整卡像素尺寸：决定降采样比例与填充采样步长。两者都按整卡算，
        // 才能保证"同一张卡上的粒子密度 / 精度"与整卡采样等价（见方法注释）。
        int cardPixelWidth = Math.Max(1, (int)Math.Ceiling(width) + 1);
        int cardPixelHeight = Math.Max(1, (int)Math.Ceiling(height) + 1);
        long area = Math.Max(1, (long)Math.Ceiling(width) * (long)Math.Ceiling(height));
        double scale = SampleScale(cardPixelWidth, cardPixelHeight);

        string degrade = null;
        if (scale < 0.999)
        {
            degrade = $"卡片采样 {cardPixelWidth}×{cardPixelHeight} 超出像素预算，降采样到 {scale:0.###}x";
        }

        // 采样区域：默认整卡；只有"按变化字符采样"且能拿到可信包围盒时才收窄。
        Rect region = new(0, 0, width, height);
        if (onlyChanged)
        {
            if (TryResolveSampleRegion(canvas, targetChars, width, height, out Rect narrowed, out string reason))
            {
                region = narrowed;
            }
            else
            {
                // 退回整卡是"正确性优先"的正常路径，不是错误：按 30s 节流写 Debug 留痕。
                LogRegionFallback(reason);
            }
        }

        // 直接画到 DrawingVisual，不经过 TextCanvas 的可视缓存；否则隐藏字符后
        // RenderTargetBitmap 仍可能采到上一帧的完整文字，一个变化数字会摊出整行粒子。
        // 收窄采样时把画布平移 -区域原点：位图只覆盖区域，区域外本来也没有可见字形。
        bool offset = region.X != 0 || region.Y != 0;
        using (DrawingContext dc = _sampleVisual.RenderOpen())
        {
            if (offset) dc.PushTransform(new TranslateTransform(-region.X, -region.Y));
            canvas.DrawForParticles(dc, onlyChanged ? _hideSet : null);
            if (offset) dc.Pop();
        }

        // 区域像素尺寸同样多采 1 像素：区域边界向上取整后，最右 / 最下的字形不落在位图外。
        int usedWidth = Math.Max(1, (int)Math.Ceiling(region.Width * scale) + 1);
        int usedHeight = Math.Max(1, (int)Math.Ceiling(region.Height * scale) + 1);
        // 位图按 64 像素对齐分配、只拷贝实际用到的区域：
        // 变化字符的位置 / 宽度每轮都会变一点，如果按精确尺寸建位图，就会每轮新建一张
        // RenderTargetBitmap（等于每轮一次大分配）。对齐后同一档尺寸可以跨轮复用，
        // 同时 CopyPixels 仍只拷 usedWidth×usedHeight，像素搬运量不被对齐浪费。
        int allocWidth = AlignUp(usedWidth, SampleBitmapQuantum);
        int allocHeight = AlignUp(usedHeight, SampleBitmapQuantum);

        if (_rtb is null || _rtbPixelWidth != allocWidth || _rtbPixelHeight != allocHeight
            || Math.Abs(_rtbScale - scale) > 1e-6)
        {
            _rtb = new RenderTargetBitmap(allocWidth, allocHeight,
                                          96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
            _rtbPixelWidth = allocWidth;
            _rtbPixelHeight = allocHeight;
            // 坐标反算必须用位图创建时的真实 DPI，不能跟着本次 scale 走：
            // 尺寸相同但 scale 只差一点点却复用旧位图，会让目标整体偏移。
            _rtbScale = scale;
        }

        _sampleOriginX = region.X;
        _sampleOriginY = region.Y;
        _sampleRegion = region;
        _samplePixelCount = (long)usedWidth * usedHeight;
        // 整卡采样的像素数（旧实现的位图大小），作为"本轮省了多少"的对比基准。
        _sampleFullCardPixelCount = (long)Math.Ceiling(cardPixelWidth * scale)
                                  * (long)Math.Ceiling(cardPixelHeight * scale);

        int stride = usedWidth * BytesPerPixel;
        int needed = stride * usedHeight;
        if (_pixels.Length < needed)
        {
            _pixels = new byte[needed];
        }

        // 必须 Clear：RenderTargetBitmap.Render 是累加的，不清会叠上一轮的字形。
        _rtb.Clear();
        _rtb.Render(_sampleVisual);
        _rtb.CopyPixels(new Int32Rect(0, 0, usedWidth, usedHeight), _pixels, stride, 0);

        byte[] pixels = _pixels;
        int targetCount = Math.Max(MinParticles, Math.Min(MaxParticles, requestedCount));
        // 候选上限保持原值：先随机洗牌再取前 targetCount 个，分布与旧实现等价。
        int cap = MaxCandidatePoints;

        _candidates.Clear();
        if (geometry)
        {
            // 轮廓采样：取 alpha 边界。对图标 / 键帽 / 混排也有效，不依赖单字体 FormattedText。
            for (int y = 0; y < usedHeight; y++)
            {
                int row = y * stride;
                for (int x = 0; x < usedWidth; x++)
                {
                    int index = row + x * BytesPerPixel + 3;
                    if (pixels[index] < AlphaThreshold) continue;

                    // 采样区域自带 ≥4 DIP 的 padding，所以"扫描边界"那一圈一定是空的，
                    // 把它当边缘不会凭空多出候选点（见 TryClipSampleRegion 的 padding 下限）。
                    bool edge = x == 0 || y == 0 || x == usedWidth - 1 || y == usedHeight - 1
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
            for (int y = 0; y < usedHeight; y += step)
            {
                int row = y * stride;
                for (int x = 0; x < usedWidth; x += step)
                {
                    if (pixels[row + x * BytesPerPixel + 3] < AlphaThreshold) continue;
                    _candidates.Add(ToDip(x, y, step * 0.5));
                    if (_candidates.Count >= cap) break;
                }
                if (_candidates.Count >= cap) break;
            }
        }

        _sampleCandidateCount = _candidates.Count;
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

    /// <summary>2.45.2：向上取整到 quantum 的整数倍（位图分配粒度，避免每轮重建 RTB）。</summary>
    static int AlignUp(int value, int quantum) => (value + quantum - 1) / quantum * quantum;

    /// <summary>
    /// 采样位图像素坐标 → 画布 DIP：先按位图创建时的 DPI 反算，再加回区域原点。
    /// 2.45.2 起位图只覆盖采样区域，所以必须补这个偏移，否则粒子目标整体偏到左上角。
    /// </summary>
    Point ToDip(int x, int y, double half) =>
        new(_sampleOriginX + (x + half) / _rtbScale,
            _sampleOriginY + (y + half) / _rtbScale);

    /// <summary>
    /// 2.45.2：解析本轮采样区域（画布 DIP）。
    /// 返回 false 时 <paramref name="reason"/> 说明为什么必须退回整卡采样——
    /// 取不到包围盒、外扩后为空、或已接近整卡（整段变化 / 文字铺满卡片）。
    /// 只有"只采变化字符"这条路径会调用它；文本未变的全量重采样仍然整卡。
    /// </summary>
    bool TryResolveSampleRegion(TextCanvas canvas, HashSet<int> targetChars, double width, double height,
                                out Rect region, out string reason)
    {
        region = Rect.Empty;
        reason = null;

        _elementRects.Clear();
        if (!canvas.TryGetTextElementRects(targetChars, _elementRects))
        {
            reason = "取不到变化字符的 DIP 包围盒";
            return false;
        }

        Rect merged = Rect.Empty;
        for (int i = 0; i < _elementRects.Count; i++)
        {
            merged = MergeBounds(merged, _elementRects[i]);
        }

        if (!TryClipSampleRegion(merged, canvas.ParticleSamplePadding, width, height, out region))
        {
            reason = $"变化包围盒 {merged.Width:0.#}×{merged.Height:0.#} DIP 外扩后无效或接近整卡 {width:0}×{height:0} DIP";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 2.45.2：合并文本元素包围盒。空 / 无效（NaN、无穷、零宽）矩形会被跳过——
    /// 单个元素算不出位置时不能污染整个并集（例如变化字符全是空格：
    /// 它没有墨迹，少一个元素不影响采样，而并出一个错误的框会切掉相邻字形）。
    /// 纯函数，自检直接断言。
    /// </summary>
    internal static Rect MergeBounds(Rect current, Rect next)
    {
        if (!IsUsableBounds(next)) return current;
        if (!IsUsableBounds(current)) return next;
        return Rect.Union(current, next);
    }

    static bool IsUsableBounds(Rect r) =>
        !r.IsEmpty
        && !double.IsNaN(r.X) && !double.IsNaN(r.Y)
        && !double.IsNaN(r.Width) && !double.IsNaN(r.Height)
        && !double.IsInfinity(r.X) && !double.IsInfinity(r.Y)
        && r.Width > 0 && r.Height > 0;

    /// <summary>
    /// 2.45.2：包围盒外扩 padding 后夹到卡片内，作为采样区域。返回 false 表示不能安全收窄：
    ///  · 空 / 无效矩形（例如整段都是删除，新文本里没有要重采的字形）；
    ///  · 外扩后完全落在卡片外；
    ///  · 已接近整卡（整段变化、文字铺满卡片）——收窄省不下多少扫描量，却多一层风险。
    /// padding 有 2 DIP 下限：至少多采一圈，不让字形边缘正好压在位图边界上。
    /// 纯函数，自检直接断言。
    /// </summary>
    internal static bool TryClipSampleRegion(Rect content, double padding, double cardWidth, double cardHeight,
                                             out Rect region)
    {
        region = Rect.Empty;
        if (cardWidth <= 0 || cardHeight <= 0) return false;
        if (!IsUsableBounds(content)) return false;

        double pad = Math.Max(2.0, padding);
        var inflated = new Rect(content.X - pad, content.Y - pad,
                                content.Width + pad * 2, content.Height + pad * 2);
        Rect clipped = Rect.Intersect(inflated, new Rect(0, 0, cardWidth, cardHeight));
        if (!IsUsableBounds(clipped)) return false;

        if (clipped.Width * clipped.Height >= cardWidth * cardHeight * RegionFallbackFraction) return false;

        region = clipped;
        return true;
    }

    /// <summary>
    /// 2.45.2：退回整卡采样的原因按 30s 节流写 Debug。
    /// 这是"正确性优先"的正常降级而不是错误，所以不用 Warn；但必须留痕——
    /// 否则"某张卡为什么没享受到区域收窄"会变成没法排查的黑盒。
    /// </summary>
    void LogRegionFallback(string reason)
    {
        long now = Stopwatch.GetTimestamp();
        if (_regionFallbackLogTicks != 0
            && Stopwatch.GetElapsedTime(_regionFallbackLogTicks, now).TotalMilliseconds < 30000)
        {
            return;
        }

        _regionFallbackLogTicks = now;
        Log.Debug($"粒子采样区域退回整卡: {reason}");
    }

    /// <summary>
    /// 2.45.2：采样诊断——本轮实际渲染 / 扫描的区域、像素数与候选点数。
    /// 同时给出整卡像素数，日志里能直接看出区域收窄生效了多少。
    /// </summary>
    string SampleRegionSummary()
    {
        double ratio = _sampleFullCardPixelCount <= 0
            ? 1.0
            : (double)_samplePixelCount / _sampleFullCardPixelCount;
        return $" / 采样区域 {_sampleRegion.X:0.#},{_sampleRegion.Y:0.#} "
             + $"{_sampleRegion.Width:0.#}×{_sampleRegion.Height:0.#} DIP"
             + $"，像素 {_samplePixelCount}/{_sampleFullCardPixelCount}（{ratio * 100:0}%），候选 {_sampleCandidateCount}";
    }

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
                Log.Info($"粒子文字重新采样：{_count} 点 / {w:0}×{h:0} DIP" + SampleRegionSummary());
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
        NotifyFinished(null);
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
