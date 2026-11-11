using System;

namespace MirrorpondHUD.Ui.Contour
{
    /// <summary>
    /// 一组等高线的取值。
    /// 2.32 起删除“首曲线 / 计曲线”的区分：所有线同色同宽，不再每 N 条加粗。
    /// </summary>
    public sealed class ContourLevels
    {
        public double[] Values;

        public int Count
        {
            get { return Values == null ? 0 : Values.Length; }
        }

        /// <summary>
        /// 在 [low, high] 上等距切 count 条。首尾各留半格，避免最低 / 最高一条
        /// 正好压在取值边界上——那种线在数值上合法，画出来却只是一条断线。
        /// </summary>
        public static ContourLevels Create(double low, double high, int count)
        {
            if (count < 2)
            {
                count = 2;
            }
            if (!(high > low))
            {
                high = low + 1e-3;
            }

            double[] values = new double[count];
            double span = high - low;

            for (int i = 0; i < count; i++)
            {
                values[i] = low + span * (i + 0.5) / count;
            }

            ContourLevels levels = new ContourLevels();
            levels.Values = values;
            return levels;
        }

        /// <summary>
        /// 按实际高度范围切线。上界只吃掉 2%：太靠顶时最高的一条会贴着面板边缘，
        /// 只剩几个像素的弧段，看着像渲染错误。
        /// </summary>
        public static ContourLevels FromRange(double min, double max, int count)
        {
            double span = max - min;
            if (span < 1e-4)
            {
                span = 1e-4;
            }
            double low = min + span * 0.06;
            double high = max - span * 0.02;
            return Create(low, high, count);
        }
    }
}
