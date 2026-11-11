using System;

namespace MirrorpondHUD.Ui.Contour
{
    /// <summary>
    /// 确定性 value noise + 分形叠加（fBm）。
    ///
    /// 为什么自己写：仓库整体零第三方依赖（AGENTS.md §1），示例保持一致；
    /// 同时"同一 seed 必得同一片地形"是可验证性的一部分——--selftest 与
    /// --shot 都依赖这一点，换 Random 会因为调用顺序不同而漂移。
    /// </summary>
    public sealed class NoiseField
    {
        private readonly int[] _perm = new int[512];

        public NoiseField(int seed)
        {
            int[] table = new int[256];
            for (int i = 0; i < 256; i++)
            {
                table[i] = i;
            }

            // xorshift32 洗牌：不依赖 System.Random 的实现细节，跨版本结果稳定。
            // seed 为 0 时 xorshift 会卡死在 0，换成固定常量。
            uint state = seed == 0 ? 0x9E3779B9u : (uint)seed;
            for (int i = 255; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int j = (int)(state % (uint)(i + 1));
                int tmp = table[i];
                table[i] = table[j];
                table[j] = tmp;
            }

            for (int i = 0; i < 512; i++)
            {
                _perm[i] = table[i & 255];
            }
        }

        /// <summary>单层噪声，返回 [0,1]。</summary>
        public double Sample(double x, double y)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            double fx = x - xi;
            double fy = y - yi;

            // smoothstep 插值：线性插值会在格子边界留下菱形折痕，
            // 而等高线会把这些折痕原样描出来，所以必须平滑。
            double u = fx * fx * (3.0 - 2.0 * fx);
            double v = fy * fy * (3.0 - 2.0 * fy);

            double a = Grid(xi, yi);
            double b = Grid(xi + 1, yi);
            double c = Grid(xi, yi + 1);
            double d = Grid(xi + 1, yi + 1);

            double top = a + (b - a) * u;
            double bottom = c + (d - c) * u;
            return top + (bottom - top) * v;
        }

        /// <summary>
        /// 分形叠加：多倍频噪声按 gain 衰减相加，得到既有大起伏又有细节的地形。
        /// 每层都套一个轻微旋转，否则各层的格子会对齐成明显的方块感。
        /// </summary>
        public double Fbm(double x, double y, int octaves, double lacunarity, double gain)
        {
            if (octaves < 1)
            {
                octaves = 1;
            }

            double sum = 0.0;
            double amplitude = 0.5;
            double frequency = 1.0;
            double normalizer = 0.0;

            double rx = x;
            double ry = y;

            for (int i = 0; i < octaves; i++)
            {
                double nx = rx * 0.94 - ry * 0.34;
                double ny = rx * 0.34 + ry * 0.94;
                rx = nx;
                ry = ny;

                sum += amplitude * Sample(rx * frequency + 19.19 * i, ry * frequency + 7.77 * i);
                normalizer += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
            }

            return sum / normalizer;
        }

        private double Grid(int x, int y)
        {
            // 负坐标同样能落到 0..255：C# 的 int 位与保留二进制补码，-1 & 255 == 255。
            return _perm[(_perm[x & 255] + (y & 255)) & 255] * (1.0 / 255.0);
        }
    }
}
