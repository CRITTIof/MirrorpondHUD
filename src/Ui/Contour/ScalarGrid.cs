using System;

namespace MirrorpondHUD.Ui.Contour;

/// <summary>
/// 规则采样网格：把连续地形离散成 (Cols+1) × (Rows+1) 个顶点高度。
///
/// 为什么要把采样单独抽出来：等高线提取、明暗分层、高程着色读的是同一份数据。
/// 如果各层自己采样，等值线的边界与色带边界就会差半个像素，看起来"线没压住色块"。
/// </summary>
public sealed class ScalarGrid
{
    public int Cols;
    public int Rows;
    public double CellW;
    public double CellH;
    public double[] Values;
    public double Min;
    public double Max;

    public int VertexCols => Cols + 1;

    public int VertexRows => Rows + 1;

    /// <summary>
    /// 每轴单元格上限的**唯一**来源：140（两个方向各自）。
    ///
    /// 为什么是 140 而不是更大：生产路径每帧要对整张网格做「12 级 marching squares +
    /// 每级 Chaikin 平滑」，格数是面积级增长；4K 宽卡在 140 上限下 cell ≈ 27 DIP，
    /// 这是基线验收过的观感与每帧预算。这个数字同时也是 <see cref="Build"/> 与
    /// <see cref="ComputeProductionSize"/> 的默认上限（T-15：原来这里写 512、生产写 140，
    /// 两条路径各说一套，按文档估精度会差一个数量级）。
    /// 调大它会直接抬高每帧计算量，必须连平滑轮数与降频一起评估（审阅 §4 T-15）。
    /// </summary>
    public const int MaxCellsPerAxis = 140;

    /// <summary>网格尺寸下限：再小就没有"等高线"可言了。</summary>
    public const int MinCellsPerAxis = 4;

    /// <summary>
    /// 生产路径的最小单元格边长（DIP）。与 <see cref="MaxCellsPerAxis"/> 一起构成
    /// 「按最长边选格数、再夹到上下限」这一条尺寸策略；低于 6 DIP 的格子会把线条细节
    /// 全交给 Chaikin 抹平，视觉上不再有地形感。
    /// </summary>
    public const double MinCellSize = 6.0;

    /// <summary>
    /// T-12/T-15：网格尺寸（Cols / Rows）的**唯一**推导入口。
    ///
    /// 为什么单独抽出来：生产路径（<see cref="ContourGeometryBuilder"/>）与可测 API
    /// （<see cref="Build"/>）原来各写一遍 `cell / cols / rows` 的夹取，已经漂移过
    /// （生产用 140 上限、这里用 512）。两边都改成调用本方法后，尺寸语义只有一份，
    /// 上限也只有 <see cref="MaxCellsPerAxis"/> 一份。
    ///
    /// 单元格数上限按方向独立夹取：默认 140 是「12 级提取 + 平滑」的每帧预算，
    /// 调用方不要为了"更精细"随手调大。
    /// </summary>
    public static (int Cols, int Rows) ComputeSize(double width, double height, double cellSize,
                                                   int maxCellsPerAxis = MaxCellsPerAxis)
    {
        if (!(width > 1.0) || !(height > 1.0))
        {
            throw new ArgumentOutOfRangeException("width", "采样区域必须大于 1 像素");
        }

        int maxCells = Math.Max(MinCellsPerAxis, maxCellsPerAxis);
        double cell = Math.Max(2.0, cellSize);
        int cols = (int)Math.Ceiling(width / cell);
        int rows = (int)Math.Ceiling(height / cell);
        cols = Math.Max(MinCellsPerAxis, Math.Min(cols, maxCells));
        rows = Math.Max(MinCellsPerAxis, Math.Min(rows, maxCells));
        return (cols, rows);
    }

    /// <summary>
    /// 生产路径的尺寸推导：把「最长边 / <see cref="MaxCellsPerAxis"/>，下限
    /// <see cref="MinCellSize"/>」这一条策略收在同一处，<see cref="ContourGeometryBuilder"/>
    /// 不再自己算 cellSize。自检要验网格精度时也走这里，才能和生产保持一致（T-15）。
    /// </summary>
    public static (int Cols, int Rows) ComputeProductionSize(double width, double height)
    {
        double cellSize = Math.Max(MinCellSize, Math.Max(width, height) / MaxCellsPerAxis);
        return ComputeSize(width, height, cellSize, MaxCellsPerAxis);
    }

    /// <summary>
    /// 按目标单元格尺寸采样。单元格数量上限取 <see cref="MaxCellsPerAxis"/>（两个方向各自），
    /// 防止大窗口把内存和生成耗时拖爆；下限 <see cref="MinCellsPerAxis"/>，防止退化成没有意义的网格。
    /// </summary>
    public static ScalarGrid Build(Func<double, double, double> sampler, double width, double height, double cellSize)
    {
        if (sampler is null)
        {
            throw new ArgumentNullException("sampler");
        }
        if (!(width > 1.0) || !(height > 1.0))
        {
            throw new ArgumentOutOfRangeException("width", "采样区域必须大于 1 像素");
        }

        (int cols, int rows) = ComputeSize(width, height, cellSize);

        ScalarGrid grid = new();
        grid.Cols = cols;
        grid.Rows = rows;
        grid.CellW = width / cols;
        grid.CellH = height / rows;
        grid.Values = new double[(cols + 1) * (rows + 1)];

        double min = double.MaxValue;
        double max = double.MinValue;
        int index = 0;
        for (int j = 0; j <= rows; j++)
        {
            double y = j * grid.CellH;
            for (int i = 0; i <= cols; i++)
            {
                double v = sampler(i * grid.CellW, y);
                // 非法数值会一路渗进几何和画笔，最后一帧空白且无从查起，
                // 所以在入口就拦下来，把原因抛给上层显示。
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    throw new InvalidOperationException("地形采样返回了非法数值（NaN/Infinity）");
                }
                grid.Values[index] = v;
                index++;
                if (v < min)
                {
                    min = v;
                }
                if (v > max)
                {
                    max = v;
                }
            }
        }

        grid.Min = min;
        grid.Max = max;
        return grid;
    }

    public double At(int i, int j) => Values[j * VertexCols + i];

    /// <summary>
    /// 双线性采样任意点高度。示坡线要判断"线的哪一侧更低"，
    /// 只有顶点数组不够用——探针点几乎不可能正好落在格点上。
    /// 越界时夹到边缘，不抛异常：探针本来就只是启发式判断。
    /// </summary>
    public double SampleAt(double x, double y)
    {
        double gx = x / CellW;
        double gy = y / CellH;

        int i = (int)Math.Floor(gx);
        int j = (int)Math.Floor(gy);
        if (i < 0)
        {
            i = 0;
        }
        if (j < 0)
        {
            j = 0;
        }
        if (i > Cols - 1)
        {
            i = Cols - 1;
        }
        if (j > Rows - 1)
        {
            j = Rows - 1;
        }

        double fx = gx - i;
        double fy = gy - j;
        if (fx < 0.0)
        {
            fx = 0.0;
        }
        else if (fx > 1.0)
        {
            fx = 1.0;
        }
        if (fy < 0.0)
        {
            fy = 0.0;
        }
        else if (fy > 1.0)
        {
            fy = 1.0;
        }

        double a = At(i, j);
        double b = At(i + 1, j);
        double c = At(i, j + 1);
        double d = At(i + 1, j + 1);

        double top = a + (b - a) * fx;
        double bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }
}
