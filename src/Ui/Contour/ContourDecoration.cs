using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Contour;

/// <summary>
/// 卡片装饰层里的等高线。只画线：
/// - 不再区分首曲线 / 计曲线，全部同色同宽；
/// - 不再有高程数字、注记、示坡线；
/// - 保留级数、疏密均衡、地形起伏、平滑轮数、最小闭合环面积。
///
/// 性能模型（本次优化）：
/// - A/B 两套固定地形的原始高度网格按尺寸 + Relief 缓存，相位变化只做混合 + 同一套
///   拉伸/tanh + 直方图均衡，不再每帧重新采样两套噪声；
/// - 提取 / 串联 / 平滑 / 建几何整体放在后台线程，产物（冻结的 StreamGeometry + Pen）
///   跨线程交给 UI 线程，UI 线程只做一次字段替换 + InvalidateVisual；
/// - SetPhase 只记录相位并发起（或合并）一次生成；同一相位重复调用直接返回；
/// - 代次计数保证旧结果晚归作废；控件 Unloaded 后不再回写 UI。
///
/// 视觉与原实现一致：混合权重、拉伸/tanh、均衡、五参数语义、线色线宽均未改变；
/// 唯一差异是生成尺寸量化到 1/4 DIP（≤0.125 DIP 的亚像素误差）。
/// </summary>
internal sealed class ContourDecoration : FrameworkElement
{
    public int LevelCount = 12;
    public double Evening = 0.72;
    public double Relief = 1.0;
    public int SmoothIterations = 2;
    public double MinLoopArea = 26.0;
    public double Phase = 0.0;
    public Color LineColor = Color.FromArgb(0x40, 0xF5, 0xF5, 0xF0);
    public double LineThickness = 0.8;

    /// <summary>
    /// 相位量化步长 = 2π/256。量化本身不是问题，问题是它和固定闸门叠加：
    /// 0.2x 速度下一个形变周期 50 秒，跨一格要 195ms，于是"30fps 调用、只有 5fps 重建"。
    /// 现在量化只在"跨一格不比出帧间隔慢"时才启用（见 <see cref="PolicyFor"/>）。
    /// </summary>
    const double PhaseQuantum = Math.PI * 2.0 / 256.0;

    /// <summary>一个形变周期被量化成多少格（1/256 周期的倒数）。</summary>
    const double PhaseSteps = 256.0;

    /// <summary>慢速档出帧间隔 33ms（约 30fps）：低速率下的平滑度靠它，不能被量化拖到 5fps。</summary>
    internal const double SlowIntervalMs = 33.0;

    /// <summary>快速档出帧间隔 66ms（约 15fps）：高速下每帧形状变化大，15fps 已看不出台阶，还能省一半 CPU。</summary>
    internal const double FastIntervalMs = 66.0;

    /// <summary>
    /// 单次后台生成的耗时预算（毫秒）。超过它说明"33ms 出一帧"已经算不过来，
    /// 与其让后台线程被 30fps 追着跑，不如退回 15fps——这是"不把常态 CPU 拉回过一核"的兜底。
    /// </summary>
    internal const double BuildBudgetMs = 15.0;

    /// <summary>相位去重阈值：未量化时只有超过它才算"相位真的变了"，避免同相位重复请求。</summary>
    const double PhaseEpsilon = 1e-9;

    const double TwoPi = Math.PI * 2.0;

    /// <summary>构造时（UI 线程）捕获；后台线程只碰它，不碰任何 DependencyObject。</summary>
    readonly Dispatcher _dispatcher;
    readonly ContourGeometryBuilder _builder = new();

    /// <summary>生成失败日志节流：确定性错误在 30fps 下不能每帧写一条。</summary>
    readonly LogThrottle _failLog = new("等高线生成", 30000);

    /// <summary>成本闸门当前是否生效，以及上次状态切换日志的时刻（5 秒防抖用）。</summary>
    bool _costGuardEngaged;
    DateTime _costGuardLogUtc = DateTime.MinValue;

    /// <summary>
    /// P6 观测窗口：相位更新次数 / 重建次数 / 单次生成耗时。每秒重建几次是本次修复的
    /// 唯一硬指标，光看"卡不卡"没法比较低/高速两档，所以必须能打出来。
    /// 只写 Debug（默认 Info 等级下一条都不落盘），并且按 5 秒窗口节流。
    /// </summary>
    long _phaseUpdates;
    long _buildStarts;
    long _buildCostEmaTicks;
    DateTime _statWindowUtc = DateTime.UtcNow;

    ContourFrame _frame;
    ContourBuildRequest _currentRequest;
    ContourBuildRequest _inFlightRequest;
    ContourBuildRequest _appliedRequest;
    ContourBuildRequest _failedRequest;
    DateTime _retryNotBeforeUtc = DateTime.MinValue;

    /// <summary>P6：下一次允许因相位变化重建的时刻（由 <see cref="PolicyFor"/> 的间隔决定）。</summary>
    DateTime _nextPhaseBuildUtc = DateTime.MinValue;

    bool _inFlight;
    bool _queued;
    bool _unloaded;
    int _generation;

    public ContourDecoration()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        _dispatcher = Dispatcher;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>是否已经有一帧可用结果（空几何也算“已生成”）。截图等离屏路径用它等待首帧。</summary>
    public bool IsReady => _appliedRequest is not null || _failedRequest is not null;

    /// <summary>离屏渲染主动发一次请求：RenderTargetBitmap 不会替我们跑 OnRender 的首帧预热。</summary>
    public void Prime() => RequestBuild();

    public void Apply(StyleSettings style, Color lineColor)
    {
        if (style is null) style = new StyleSettings();

        LevelCount = Math.Max(3, Math.Min(48, style.ContourLevels));
        Evening = Math.Max(0.0, Math.Min(1.0, style.ContourEvening));
        Relief = Math.Max(0.2, Math.Min(4.0, style.ContourRelief));
        SmoothIterations = Math.Max(0, Math.Min(6, style.ContourSmooth));
        MinLoopArea = Math.Max(0.0, Math.Min(5000.0, style.ContourMinLoopArea));

        LineColor = lineColor;
        LineThickness = 0.8;
        RequestBuild();
        InvalidateVisual();
    }

    /// <summary>
    /// 每帧（约 30fps）调用。只记录相位并发起/合并一次后台生成，不做任何几何计算。
    ///
    /// P6（2.45.4）：重建节奏由 <see cref="PolicyFor"/> 按"相位速度 + 实测生成耗时"决定，
    /// 不再是固定的 1/256 量化 + 66ms。慢速去掉量化、33ms 出帧（接近 30fps）；
    /// 快速保留量化、66ms 出帧。形变周期仍按真实时间推进——这里只决定"画哪一帧"，
    /// 不改变相位速度本身；被闸门跳过的帧继续画上一帧几何，不会闪空。
    /// </summary>
    public void SetPhase(double phase, double speed)
    {
        double wrapped = WrapPhase(phase);
        double buildCostMs = BuildCostMs();
        ContourPhasePolicy policy = PolicyFor(speed, buildCostMs);
        UpdateCostGuard(buildCostMs);

        double next = policy.Quantum > 0.0 ? QuantizeTo(wrapped, policy.Quantum) : wrapped;
        if (Math.Abs(next - Phase) < PhaseEpsilon) return;

        Interlocked.Increment(ref _phaseUpdates);
        MaybeLogStats(policy, buildCostMs);

        // 时间闸门：跳过的只是这次相位更新，下一次 tick 会带着更新的相位再来（不累积误差）。
        if (DateTime.UtcNow < _nextPhaseBuildUtc) return;

        Phase = next;
        _nextPhaseBuildUtc = DateTime.UtcNow.AddMilliseconds(policy.MinIntervalMs);
        Interlocked.Increment(ref _buildStarts);
        RequestBuild();
    }

    /// <summary>归一化到 [0, 2π)：地形混合只读 cos，2π 与 0 等价。</summary>
    internal static double WrapPhase(double phase)
    {
        if (double.IsNaN(phase) || double.IsInfinity(phase)) return 0.0;
        double wrapped = phase % TwoPi;
        if (wrapped < 0) wrapped += TwoPi;
        return wrapped;
    }

    /// <summary>量化到 <paramref name="quantum"/> 的整数倍；正好落在 2π 时折回 0，避免接缝处多重建一次。</summary>
    static double QuantizeTo(double phase, double quantum)
    {
        double quantized = Math.Round(phase / quantum) * quantum;
        return quantized >= TwoPi ? 0.0 : quantized;
    }

    /// <summary>
    /// 相位速度 → 重建节奏（P6）。纯函数，<c>SelfTest</c> 直接断言。
    ///
    /// 取值链：
    /// · 出帧间隔随速度从 33ms 线性放大到 66ms（1x 仍是 30fps，2x 起 15fps）；
    /// · 单次生成超过 <see cref="BuildBudgetMs"/> 时一律退回 66ms，避免后台线程被 30fps 追着跑；
    /// · 只有"跨一格的时间"不慢于出帧间隔时才量化——这正是低速率卡顿的判定条件：
    ///   0.2x 跨一格约 195ms，量化会把 30fps 的调用变成 5fps 的重建。
    /// </summary>
    internal static ContourPhasePolicy PolicyFor(double speed, double buildCostMs)
    {
        double clamped = Math.Max(0.1, Math.Min(4.0, speed));
        double intervalMs = Math.Max(SlowIntervalMs, Math.Min(FastIntervalMs, SlowIntervalMs * clamped));
        if (buildCostMs > BuildBudgetMs) intervalMs = FastIntervalMs;

        double quantum = QuantumCrossingMs(clamped) <= intervalMs ? PhaseQuantum : 0.0;
        return new ContourPhasePolicy(quantum, intervalMs);
    }

    /// <summary>跨一个 1/256 量化格需要的时间（毫秒）= 形变周期 ÷ 格数。纯函数，策略与自检共用。</summary>
    internal static double QuantumCrossingMs(double speed)
    {
        double clamped = Math.Max(0.1, Math.Min(4.0, speed));
        return StyleSettings.ContourPeriodSeconds(clamped) * 1000.0 / PhaseSteps;
    }

    /// <summary>
    /// 成本闸门状态切换日志。用 Debug 而不是 Warn：冷启动首帧（含基础网格采样）几乎必然
    /// 超预算，但一两秒后会自行恢复——这不是错误，默认 Info 等级下不该为此刷一条警告。
    /// 状态真的在阈值附近抖动时由 1 秒防抖兜住（最多每秒一条 Debug）。
    /// </summary>
    void UpdateCostGuard(double buildCostMs)
    {
        bool engaged = buildCostMs > BuildBudgetMs;
        if (engaged == _costGuardEngaged) return;

        DateTime now = DateTime.UtcNow;
        if ((now - _costGuardLogUtc).TotalSeconds < 1.0)
        {
            _costGuardEngaged = engaged;
            return;
        }

        _costGuardEngaged = engaged;
        _costGuardLogUtc = now;
        Log.Debug(engaged
            ? $"等高线成本闸门：单次生成 {buildCostMs:0.0}ms 超过 {BuildBudgetMs:0}ms 预算，相位重建降级到 15fps"
            : $"等高线成本闸门：单次生成 {buildCostMs:0.0}ms 回到预算内，恢复 30fps 出帧");
    }

    protected override void OnRender(DrawingContext dc)
    {
        ContourFrame frame = _frame;
        if (frame is not null && frame.Geometry is not null)
        {
            dc.DrawGeometry(null, frame.Pen, frame.Geometry);
        }

        // 首次布局 / 尺寸变化 / 样式变化都靠这里补请求；生成在后台，本帧先画上一帧。
        RequestBuild();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _unloaded = false;
        RequestBuild();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 卸载后不再回写 UI：代次前进让在途结果作废。
        // _inFlight 保持到后台任务收尾，避免新任务与旧任务同时使用 builder 的可变缓冲。
        _unloaded = true;
        _generation++;
        _queued = false;
        _inFlightRequest = null;
    }

    /// <summary>
    /// 记录当前输入（尺寸 / 五参数 / 相位 / 线色线宽）并在需要时发起一次后台生成。
    /// 后台忙时只置一个"有新请求"标记，不排队；当前任务结束后补最新一次。
    /// </summary>
    void RequestBuild()
    {
        if (_unloaded) return;

        ContourBuildRequest request = SnapshotCurrent();
        if (request is null) return;

        if (_currentRequest is null || !RequestEquals(_currentRequest, request))
        {
            _currentRequest = request;
            // 代次前进：任何在途结果只要不是这一代就作废。
            _generation++;
        }

        if (_inFlight)
        {
            if (!RequestEquals(_inFlightRequest, request)) _queued = true;
            return;
        }

        if (RequestEquals(request, _appliedRequest)) return;
        // 失败退避：确定性错误（例如采样出 NaN）不要每帧重试。
        if (DateTime.UtcNow < _retryNotBeforeUtc) return;

        StartBuild(request);
    }

    void StartBuild(ContourBuildRequest request)
    {
        request.Generation = _generation;
        _inFlight = true;
        _inFlightRequest = request;
        _queued = false;
        Task.Run(() => RunBuild(request));
    }

    /// <summary>后台线程：生成 → 记录失败 → 回投 UI。这里不触碰任何 UI 状态。</summary>
    void RunBuild(ContourBuildRequest request)
    {
        ContourFrame frame = null;
        string error = null;
        long started = Stopwatch.GetTimestamp();
        try
        {
            frame = _builder.Build(request);
        }
        catch (Exception ex)
        {
            // 失败必须可见：节流后写 WARN；UI 侧保留上一帧 / 空几何。
            error = $"{ex.GetType().Name}: {ex.Message}";
            _failLog.Failure(error);
        }
        RecordBuildCost(Stopwatch.GetTimestamp() - started);

        try
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            _dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => CompleteBuild(request, frame, error)));
        }
        catch (InvalidOperationException)
        {
            // 进程退出时 Dispatcher 已关闭，结果无人接收；此时没有可更新的 UI 状态，
            // 属良性竞态（真正的失败已在上面的 catch 里落过日志）。
        }
    }

    /// <summary>5 秒一条 Debug：相位更新 / 重建次数与单次生成耗时（默认 Info 等级下不落盘）。</summary>
    void MaybeLogStats(ContourPhasePolicy policy, double buildCostMs)
    {
        DateTime now = DateTime.UtcNow;
        double seconds = (now - _statWindowUtc).TotalSeconds;
        if (seconds < 5.0) return;
        _statWindowUtc = now;

        long updates = Interlocked.Exchange(ref _phaseUpdates, 0);
        long builds = Interlocked.Exchange(ref _buildStarts, 0);
        if (updates == 0 && builds == 0) return;

        double ratio = updates <= 0 ? 0.0 : (double)builds / updates;
        Log.Debug($"等高线相位：{seconds:0.0}s 内更新 {updates} 次 / 重建 {builds} 次"
                  + $"（{builds / seconds:0.0} 次/秒，重建/更新={ratio:0.00}），"
                  + $"单次生成 {buildCostMs:0.0}ms，闸门 {policy.MinIntervalMs:0}ms，"
                  + $"量化 {(policy.Quantum > 0.0 ? "1/256" : "关")}，画布 {ActualWidth:0}×{ActualHeight:0}");
    }

    /// <summary>单次生成耗时的指数滑动平均（后台线程写、UI 线程读），用于成本闸门与日志。</summary>
    double BuildCostMs()
    {
        long ticks = Interlocked.Read(ref _buildCostEmaTicks);
        return ticks <= 0 ? 0.0 : ticks * 1000.0 / Stopwatch.Frequency;
    }

    void RecordBuildCost(long ticks)
    {
        if (ticks <= 0) return;
        long previous = Interlocked.Read(ref _buildCostEmaTicks);
        long next = previous <= 0 ? ticks : previous + ((ticks - previous) / 8);
        Interlocked.Exchange(ref _buildCostEmaTicks, next);
    }

    /// <summary>UI 线程：只接受当前代的结果；旧代 / 卸载后的结果直接丢弃。</summary>
    void CompleteBuild(ContourBuildRequest request, ContourFrame frame, string error)
    {
        _inFlight = false;
        _inFlightRequest = null;

        bool isCurrent = !_unloaded && request.Generation == _generation;
        if (isCurrent)
        {
            if (error is null)
            {
                _frame = frame;
                _appliedRequest = request;
                _failedRequest = null;
                _failLog.Success();
                InvalidateVisual();
            }
            else
            {
                // 失败：保留上一帧（首次失败则保持空几何），记录失败快照并退避 2 秒。
                _failedRequest = request;
                _retryNotBeforeUtc = DateTime.UtcNow.AddSeconds(2.0);
            }
        }

        if (_unloaded)
        {
            _queued = false;
            return;
        }

        // 后台忙期间被合并掉的新请求，或"卸载→重载 / 失败"导致当前帧不是最新输入时，
        // 在这里补一次最新请求；只补一次，不排队。
        if (_queued
            || (_currentRequest is not null
                && !RequestEquals(_currentRequest, _appliedRequest)
                && !RequestEquals(_currentRequest, _failedRequest)))
        {
            _queued = false;
            RequestBuild();
        }
    }

    ContourBuildRequest SnapshotCurrent()
    {
        double width = Quantize(ActualWidth);
        double height = Quantize(ActualHeight);
        if (!(width >= 16.0) || !(height >= 16.0)) return null;

        ContourBuildRequest request = new();
        request.Width = width;
        request.Height = height;
        // Phase 是 public 字段，外部直接赋值时可能不在 [0, 2π)；统一归一化一次，
        // 保证"请求相等"判定与 SetPhase 写入的值同域。量化已经在 SetPhase 按策略做过，
        // 这里不能再按 1/256 粗量化，否则慢速档又被拉回 5fps。
        request.Phase = WrapPhase(Phase);
        request.Levels = LevelCount;
        request.Evening = Evening;
        request.Relief = Relief;
        request.Smooth = SmoothIterations;
        request.MinLoopArea = MinLoopArea;
        request.Color = LineColor;
        request.Thickness = LineThickness;
        return request;
    }

    /// <summary>尺寸量化到 1/4 DIP：抑制布局抖动引起的整网格重建，误差远低于一个像素。</summary>
    static double Quantize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
        return Math.Round(value * 4.0) / 4.0;
    }

    static bool RequestEquals(ContourBuildRequest a, ContourBuildRequest b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        return Math.Abs(a.Width - b.Width) < 1e-9
            && Math.Abs(a.Height - b.Height) < 1e-9
            && Math.Abs(a.Phase - b.Phase) < 1e-9
            && a.Levels == b.Levels
            && Math.Abs(a.Evening - b.Evening) < 1e-9
            && Math.Abs(a.Relief - b.Relief) < 1e-9
            && a.Smooth == b.Smooth
            && Math.Abs(a.MinLoopArea - b.MinLoopArea) < 1e-9
            && a.Color == b.Color
            && Math.Abs(a.Thickness - b.Thickness) < 1e-9;
    }
}

/// <summary>
/// P6：一次相位更新应采用的节奏。是值类型 + 只读，避免每帧给 GC 添对象。
/// <see cref="Quantum"/> ≤ 0 表示"只归一化、不量化"；<see cref="MinIntervalMs"/> 是两次重建的最小间隔。
/// </summary>
internal readonly struct ContourPhasePolicy
{
    public readonly double Quantum;
    public readonly double MinIntervalMs;

    public ContourPhasePolicy(double quantum, double minIntervalMs)
    {
        Quantum = quantum;
        MinIntervalMs = minIntervalMs;
    }
}

