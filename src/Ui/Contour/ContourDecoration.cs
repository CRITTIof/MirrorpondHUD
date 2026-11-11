using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Contour
{
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

        /// <summary>构造时（UI 线程）捕获；后台线程只碰它，不碰任何 DependencyObject。</summary>
        readonly Dispatcher _dispatcher;
        readonly ContourGeometryBuilder _builder = new ContourGeometryBuilder();

        /// <summary>生成失败日志节流：确定性错误在 30fps 下不能每帧写一条。</summary>
        readonly LogThrottle _failLog = new LogThrottle("等高线生成", 30000);

        ContourFrame _frame;
        ContourBuildRequest _currentRequest;
        ContourBuildRequest _inFlightRequest;
        ContourBuildRequest _appliedRequest;
        ContourBuildRequest _failedRequest;
        DateTime _retryNotBeforeUtc = DateTime.MinValue;

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
        public bool IsReady
        {
            get { return _appliedRequest != null || _failedRequest != null; }
        }

        /// <summary>离屏渲染主动发一次请求：RenderTargetBitmap 不会替我们跑 OnRender 的首帧预热。</summary>
        public void Prime()
        {
            RequestBuild();
        }

        public void Apply(StyleSettings style, Color lineColor)
        {
            if (style == null) style = new StyleSettings();

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
        /// 每帧（约 30fps）调用。只记录相位并发起/合并一次后台生成，不做任何几何计算；
        /// 同一相位重复调用直接返回。新结果到达前画面保持上一帧。
        /// </summary>
        public void SetPhase(double phase)
        {
            if (Math.Abs(Phase - phase) < 1e-9) return;
            Phase = phase;
            RequestBuild();
        }

        protected override void OnRender(DrawingContext dc)
        {
            ContourFrame frame = _frame;
            if (frame != null && frame.Geometry != null)
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
            if (request == null) return;

            if (_currentRequest == null || !RequestEquals(_currentRequest, request))
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
            try
            {
                frame = _builder.Build(request);
            }
            catch (Exception ex)
            {
                // 失败必须可见：节流后写 WARN；UI 侧保留上一帧 / 空几何。
                error = ex.GetType().Name + ": " + ex.Message;
                _failLog.Failure(error);
            }

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

        /// <summary>UI 线程：只接受当前代的结果；旧代 / 卸载后的结果直接丢弃。</summary>
        void CompleteBuild(ContourBuildRequest request, ContourFrame frame, string error)
        {
            _inFlight = false;
            _inFlightRequest = null;

            bool isCurrent = !_unloaded && request.Generation == _generation;
            if (isCurrent)
            {
                if (error == null)
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
                || (_currentRequest != null
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

            ContourBuildRequest request = new ContourBuildRequest();
            request.Width = width;
            request.Height = height;
            request.Phase = Phase;
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
            if (a == null || b == null) return false;

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
}
