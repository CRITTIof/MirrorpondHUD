using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MirrorpondHUD.Ui.Contour;

/// <summary>
/// 一次后台生成请求的不可变快照。尺寸量化到 1/4 DIP：既避免布局抖动（0.01 DIP 的
/// 舍入差）触发无意义的整网格重建，又与原实现 0.25 DIP 的重建阈值同量级，
/// 肉眼看不出差别。
/// </summary>
internal sealed class ContourBuildRequest
{
    public double Width;
    public double Height;
    public double Phase;
    public int Levels;
    public double Evening;
    public double Relief;
    public int Smooth;
    public double MinLoopArea;
    public Color Color;
    public double Thickness;

    /// <summary>发起请求时的代次；结果回来时与 UI 侧当前代次比对，旧代直接作废。</summary>
    public int Generation;
}

/// <summary>已冻结、可跨线程交给 UI 线程的一帧结果。</summary>
internal sealed class ContourFrame
{
    /// <summary>可能为 null：这一帧确实没有可画的等高线（不是失败）。</summary>
    public readonly Geometry Geometry;

    public readonly Pen Pen;

    public ContourFrame(Geometry geometry, Pen pen)
    {
        Geometry = geometry;
        Pen = pen;
    }
}

/// <summary>
/// 等高线几何的后台生成器：缓存 A/B 两套原始高度网格，每帧只做
/// 混合 + 拉伸/tanh + 直方图均衡 + marching squares + 串联/平滑 + 建几何。
///
/// 视觉等价的关键：
/// - A 场 / B 场原始高度与旧实现逐点同值（同样的种子、scale、噪声采样顺序）；
/// - 混合权重仍是 blend = 0.5 - 0.5·cos(phase)，blend ≤ 1e-4 时仍只取 A 场；
/// - 拉伸 / tanh 仍走 <see cref="TerrainField.Finish"/>，与旧代码逐行一致；
/// - 均衡、取级、提取、串联、Chaikin、最小闭环过滤的参数与顺序完全不变。
///
/// 线程模型：实例由 ContourDecoration 独占，同一时刻只允许一个后台任务调用 Build
/// （UI 侧用 _inFlight 串行化），因此所有可变缓冲（网格、线段 List、端点字典、
/// 点数组池）不需要加锁，也不存在静态共享状态导致多卡片串扰。
/// </summary>
internal sealed class ContourGeometryBuilder
{
    const int Seed = 20261111;
    const double BaseScale = 0.0034;
    const int HistogramBins = 1024;

    // —— 基础网格缓存：A 场（phase=0）与 B 场（phase=π）的原始高度 ——
    double[] _rawA = new double[0];
    double[] _rawB = new double[0];
    TerrainField _terrain;
    double _terrainRelief = double.NaN;
    bool _gridValid;
    double _gridWidth;
    double _gridHeight;
    double _gridRelief = double.NaN;
    int _gridCols;
    int _gridRows;
    double _gridCellW;
    double _gridCellH;

    // —— 每帧复用缓冲（只在后台线程使用） ——
    readonly ScalarGrid _work = new();
    double[] _workValues = new double[0];
    readonly int[] _histogram = new int[HistogramBins];
    readonly double[] _cdf = new double[HistogramBins + 1];
    readonly List<ContourSegment> _segments = new(2048);
    readonly ContourChainScratch _chain = new();

    /// <summary>
    /// 本帧已经写进几何的折线。它们必须活到 geometry.Freeze() 之后才能归还点数组，
    /// 避免"数组被后续路径复用"与 StreamGeometry 内部延迟拷贝之间的任何耦合。
    /// </summary>
    readonly List<ContourPath> _drawnPaths = new(256);

    // 画笔按"颜色 + 线宽"缓存：样式不变时不必每帧 new Brush/Pen。
    Pen _pen;
    Color _penColor;
    double _penThickness = double.NaN;

    /// <summary>
    /// 在后台线程生成一帧（冻结的几何 + 画笔）。可能抛异常，由调用方记录日志并保留上一帧。
    /// </summary>
    public ContourFrame Build(ContourBuildRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException("request");
        }

        EnsureBaseGrid(request);
        FillWorkGrid(request);
        FieldEqualizer.Apply(_work, request.Evening, _histogram, _cdf);

        Geometry geometry = BuildGeometry(request);
        Pen pen = GetPen(request.Color, request.Thickness);
        return new ContourFrame(geometry, pen);
    }

    /// <summary>
    /// 基础网格只在"尺寸 / Relief"变化时重建。Levels / Evening / Smooth / 颜色等
    /// 只影响后处理与提取，不触碰两套噪声的采样结果。
    /// </summary>
    void EnsureBaseGrid(ContourBuildRequest request)
    {
        if (_gridValid
            && _gridWidth == request.Width
            && _gridHeight == request.Height
            && _gridRelief == request.Relief)
        {
            return;
        }

        // T-12/T-15：尺寸推导只走 ScalarGrid 的单一入口。生产路径原来在这里内联了一份，
        // 与可测 API 的 512 上限各写一个数，已经漂移过；现在 cell 下限（MinCellSize）与
        // 每轴格数上限（MaxCellsPerAxis=140）都只有 ScalarGrid 那一份定义，
        // 调整上限时不会再出现"截图与真机网格不同"的隐性差异。
        (int cols, int rows) = ScalarGrid.ComputeProductionSize(request.Width, request.Height);

        // Relief 变了才需要新的 scale（噪声种子与采样顺序保持不变）。
        if (_terrain is null || _terrainRelief != request.Relief)
        {
            _terrain = new TerrainField(Seed, BaseScale / Math.Max(0.2, request.Relief));
            _terrainRelief = request.Relief;
        }

        double cellW = request.Width / cols;
        double cellH = request.Height / rows;
        int count = (cols + 1) * (rows + 1);
        if (_rawA.Length != count)
        {
            _rawA = new double[count];
            _rawB = new double[count];
        }

        int index = 0;
        for (int j = 0; j <= rows; j++)
        {
            double y = j * cellH;
            for (int i = 0; i <= cols; i++)
            {
                double x = i * cellW;
                double a = _terrain.SampleRawA(x, y);
                double b = _terrain.SampleRawB(x, y);
                // 非法数值会一路渗进几何和画笔，最后一帧空白且无从查起，
                // 所以在采样入口就拦下来，把原因抛给上层记录。
                if (double.IsNaN(a) || double.IsInfinity(a)
                    || double.IsNaN(b) || double.IsInfinity(b))
                {
                    throw new InvalidOperationException("地形采样返回了非法数值（NaN/Infinity）");
                }
                _rawA[index] = a;
                _rawB[index] = b;
                index++;
            }
        }

        _gridCols = cols;
        _gridRows = rows;
        _gridCellW = cellW;
        _gridCellH = cellH;
        _gridWidth = request.Width;
        _gridHeight = request.Height;
        _gridRelief = request.Relief;
        _gridValid = true;
    }

    /// <summary>
    /// 把缓存的 A/B 原始高度按当前相位混合，再走同一套拉伸 / tanh，写进复用的工作网格。
    /// 每帧的分配只剩"几乎没有"——数组全部复用。
    /// </summary>
    void FillWorkGrid(ContourBuildRequest request)
    {
        int count = (_gridCols + 1) * (_gridRows + 1);
        if (_workValues.Length != count)
        {
            _workValues = new double[count];
        }

        _work.Cols = _gridCols;
        _work.Rows = _gridRows;
        _work.CellW = _gridCellW;
        _work.CellH = _gridCellH;
        _work.Values = _workValues;

        double blend = 0.5 - 0.5 * Math.Cos(request.Phase);
        bool onlyA = blend <= 1e-4;
        double[] rawA = _rawA;
        double[] rawB = _rawB;
        double min = double.MaxValue;
        double max = double.MinValue;

        for (int i = 0; i < count; i++)
        {
            double h;
            if (onlyA)
            {
                h = rawA[i];
            }
            else
            {
                double a = rawA[i];
                h = a + (rawB[i] - a) * blend;
            }

            double v = TerrainField.Finish(h);
            _workValues[i] = v;
            if (v < min)
            {
                min = v;
            }
            if (v > max)
            {
                max = v;
            }
        }

        _work.Min = min;
        _work.Max = max;
    }

    Geometry BuildGeometry(ContourBuildRequest request)
    {
        ContourLevels levels = ContourLevels.FromRange(_work.Min, _work.Max, request.Levels);

        StreamGeometry geometry = new();
        bool any = false;
        _drawnPaths.Clear();

        using (StreamGeometryContext ctx = geometry.Open())
        {
            foreach (double level in levels.Values)
            {
                _segments.Clear();
                if (ContourExtractor.Extract(_work, level, _segments) == 0)
                {
                    continue;
                }

                ContourExtractor.ChainInto(_segments, request.Smooth, _chain);
                List<ContourPath> paths = _chain.Paths;
                for (int k = 0; k < paths.Count; k++)
                {
                    ContourPath path = paths[k];
                    // 制图综合：太小的闭环多半是噪声碎屑；只对闭环生效。
                    if (path.Closed && Math.Abs(path.SignedArea) < request.MinLoopArea)
                    {
                        // 被制图综合过滤掉的路径也要归还池，否则每帧都会漏掉一批点数组。
                        _chain.ReleasePath(path);
                        continue;
                    }
                    if (path.Points is null || path.Points.Length < 2)
                    {
                        _chain.ReleasePath(path);
                        continue;
                    }

                    ctx.BeginFigure(path.Points[0], false, path.Closed);
                    ctx.PolyLineTo(path.Points, true, false);
                    _drawnPaths.Add(path);
                    any = true;
                }
            }
        }

        if (!any)
        {
            // 空结果：没有折线写进几何，直接丢弃本帧暂存的路径引用。
            _drawnPaths.Clear();
            _chain.DetachPaths();
            return null;
        }

        // 先冻结几何，再归还点数组：确保几何内部已完成拷贝，不依赖任何复用时机。
        geometry.Freeze();
        for (int i = 0; i < _drawnPaths.Count; i++)
        {
            _chain.ReleasePath(_drawnPaths[i]);
        }
        _drawnPaths.Clear();
        _chain.DetachPaths();
        return geometry;
    }

    Pen GetPen(Color color, double thickness)
    {
        if (_pen is not null && _penColor == color && _penThickness == thickness)
        {
            return _pen;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, Math.Max(0.5, thickness));
        pen.Freeze();
        _pen = pen;
        _penColor = color;
        _penThickness = thickness;
        return pen;
    }
}
