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
    /// 按目标单元格尺寸采样。单元格数量上限 512（两个方向各自），
    /// 防止 4K 窗口把内存和生成耗时拖爆；下限 4，防止退化成没有意义的网格。
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

        double cell = Math.Max(2.0, cellSize);
        int cols = (int)Math.Ceiling(width / cell);
        int rows = (int)Math.Ceiling(height / cell);
        cols = Math.Max(4, Math.Min(cols, 512));
        rows = Math.Max(4, Math.Min(rows, 512));

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
