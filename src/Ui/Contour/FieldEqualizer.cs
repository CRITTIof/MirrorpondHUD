using System;

namespace MirrorpondHUD.Ui.Contour
{
    /// <summary>
    /// 疏密均衡：把高度场按"面积占比"重新分配，让等高线在画面上的间距更均匀。
    ///
    /// 为什么需要它：等高线的间距由坡度决定——陡处挤成一团、缓处大片空白，
    /// 这是地形图的物理事实，但当成背景看就"一头糊、一头空"。
    /// 这里用一次直方图均衡（CDF 重映射），把"有多少面积落在这一档高度"变成
    /// 均匀分布，于是每一级等高线分到的画面积大致相同，线距也就趋于一致。
    ///
    /// strength 是混合系数：0 = 完全按真实坡度，1 = 完全摊匀。
    /// 取中间值既压平了两端，又不至于把地形读成一面斜坡。
    /// </summary>
    public static class FieldEqualizer
    {
        /// <summary>直方图档数。1024 档在 2 万级顶点上是足够细的分辨率，代价可忽略。</summary>
        private const int Bins = 1024;

        public static void Apply(ScalarGrid grid, double strength)
        {
            Apply(grid, strength, null, null);
        }

        /// <summary>
        /// 复用直方图 / CDF 缓冲的版本。相位动画每帧都要均衡一次，
        /// 旧写法每次都 new int[1024] + new double[1025]，30fps 下会给 Gen0 添无谓压力。
        /// 缓冲由调用方按实例持有（同一时刻只被一个线程使用）；长度不符时内部自行分配。
        /// </summary>
        internal static void Apply(ScalarGrid grid, double strength, int[] histogram, double[] cdf)
        {
            if (grid == null)
            {
                throw new ArgumentNullException("grid");
            }
            if (strength <= 0.001 || grid.Values == null || grid.Values.Length == 0)
            {
                return;
            }
            if (strength > 1.0)
            {
                strength = 1.0;
            }

            double min = grid.Min;
            double max = grid.Max;
            double span = max - min;
            if (span < 1e-9)
            {
                // 完全平坦的区域没有可分配的分布，保持原样。
                return;
            }

            double[] values = grid.Values;
            if (histogram == null || histogram.Length != Bins)
            {
                histogram = new int[Bins];
            }
            else
            {
                Array.Clear(histogram, 0, Bins);
            }
            if (cdf == null || cdf.Length != Bins + 1)
            {
                cdf = new double[Bins + 1];
            }
            // cdf[0] 表示"低于最低档"的占比，恒为 0；复用缓冲时必须显式清零。
            cdf[0] = 0.0;
            double binScale = Bins / span;

            for (int i = 0; i < values.Length; i++)
            {
                int bin = (int)((values[i] - min) * binScale);
                if (bin < 0)
                {
                    bin = 0;
                }
                else if (bin >= Bins)
                {
                    bin = Bins - 1;
                }
                histogram[bin]++;
            }

            // cdf[b] = 取值低于第 b 档左边界的顶点占比。
            int accumulated = 0;
            for (int b = 0; b < Bins; b++)
            {
                accumulated += histogram[b];
                cdf[b + 1] = (double)accumulated / values.Length;
            }

            for (int i = 0; i < values.Length; i++)
            {
                double t = (values[i] - min) / span;
                if (t < 0.0)
                {
                    t = 0.0;
                }
                else if (t > 1.0)
                {
                    t = 1.0;
                }

                double position = t * Bins;
                int bin = (int)position;
                if (bin < 0)
                {
                    bin = 0;
                }
                else if (bin > Bins - 1)
                {
                    bin = Bins - 1;
                }

                double fraction = position - bin;
                if (fraction < 0.0)
                {
                    fraction = 0.0;
                }
                else if (fraction > 1.0)
                {
                    fraction = 1.0;
                }

                // 百分位（0..1）就是均衡后的目标值；再与原值线性混合。
                double percentile = cdf[bin] + (cdf[bin + 1] - cdf[bin]) * fraction;
                values[i] = t + (percentile - t) * strength;
            }

            // 均衡后值域固定为 [0,1]，取级集合因此可以稳定地按 0..1 切。
            grid.Min = 0.0;
            grid.Max = 1.0;
        }
    }
}
