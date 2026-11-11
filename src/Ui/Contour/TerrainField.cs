using System;

namespace MirrorpondHUD.Ui.Contour
{
    /// <summary>
    /// 面板坐标系里的"地形高度场"。
    ///
    /// 输入是面板像素坐标，输出 [0,1] 的归一化高度。之所以按像素坐标采样而不是
    /// 按噪声坐标：面板尺寸一变，等高线的疏密就不该跳变，用像素坐标系（配合
    /// 固定 scale）能让同一块地形在任意面板尺寸下保持同样的观感尺度。
    /// </summary>
    public sealed class TerrainField
    {
        private readonly NoiseField _noise;
        private readonly NoiseField _noiseB;
        private readonly double _scale;

        /// <param name="seed">决定地形形状；同一 seed 结果完全可复现。</param>
        /// <param name="scale">每像素对应的噪声单位，越小地形起伏越"大"。</param>
        public TerrainField(int seed, double scale)
        {
            _noise = new NoiseField(seed);
            // 第二个种子只用于原地形变：两套固定地形之间混合，轮廓不会整体平移。
            _noiseB = new NoiseField(seed + 7919);
            _scale = scale > 0 ? scale : 0.004;
        }

        /// <summary>
        /// phase 只控制两套固定地形场的混合权重（余弦往返）：
        /// 等高线在原来的位置慢慢改形，而不是整体漂移。
        /// 高度场本身无状态，所以后台线程可以安全地并发采样。
        /// </summary>
        public double Sample(double x, double y, double phase)
        {
            double blend = 0.5 - 0.5 * Math.Cos(phase);

            double h;
            if (blend <= 1e-4)
            {
                h = SampleRawA(x, y);
            }
            else
            {
                double a = SampleRawA(x, y);
                double b = SampleRawB(x, y);
                h = a + (b - a) * blend;
            }

            return Finish(h);
        }

        /// <summary>
        /// A 场（phase = 0）的原始高度，尚未做拉伸 / tanh。
        ///
        /// 相位动画只改变 A/B 两场的混合比例，两个端点场本身不随相位变化，所以可以
        /// 预先把它们各采样成一份网格缓存起来。原先每帧、每个顶点都要重新采样两套
        /// 噪声（约 24 次噪声求值），缓存后每帧只剩一次线性混合——这是这次优化的核心。
        /// </summary>
        public double SampleRawA(double x, double y)
        {
            return SampleOne(_noise, x * _scale, y * _scale);
        }

        /// <summary>B 场（phase = π）的原始高度，尚未做拉伸 / tanh。</summary>
        public double SampleRawB(double x, double y)
        {
            return SampleOne(_noiseB, x * _scale, y * _scale);
        }

        /// <summary>
        /// 原始高度 → 显示高度的后处理：拉伸 + 软限幅。
        /// 与相位无关，缓存端点场之后每帧仍走同一段代码，保证视觉逐像素不变。
        /// </summary>
        public static double Finish(double raw)
        {
            // 把 0.5 附近的主体拉开，让等高线在面板上分布得更均匀。
            double stretched = (raw - 0.5) * 2.0 + 0.56;

            // 软限幅（tanh）而不是硬截断：硬截断把越界值压成一大片完全相同的高度，
            // 那里梯度为零，等高线彻底消失——画面上就是"疏到没有线"的空洞。
            // tanh 只是把两端渐近压平，梯度处处非零，线条因此能一直画到边缘。
            return 0.5 + 0.5 * Math.Tanh(1.45 * (stretched - 0.5));
        }

        static double SampleOne(NoiseField noise, double px, double py)
        {
            // 先做一次域扭曲。直接叠 fBm 会得到一团团圆形斑块，
            // 扭一下坐标才会长出山脊与谷地的走向，等高线也才有"地形感"。
            // 扭曲层只需要大尺度，2 段倍频已经够用。
            double wx = noise.Fbm(px * 0.85 + 3.1, py * 0.85 + 1.7, 2, 2.03, 0.5) - 0.5;
            double wy = noise.Fbm(px * 0.85 + 8.4, py * 0.85 + 5.2, 2, 2.03, 0.5) - 0.5;

            double qx = px + wx * 2.8;
            double qy = py + wy * 2.8;

            // 缓丘负责大形态。
            double hills = noise.Fbm(qx, qy, 4, 2.07, 0.52);

            // 脊线负责"山的骨相"：1-|2n-1| 把噪声的零交叉折成尖脊，
            // 再平方一次让脊更窄、谷更宽。真实山地正是这个形状——
            // 等高线在山脊收成尖角、在谷地拉成 V，纯 fBm 做不出这种转折。
            double ridge = 1.0 - Math.Abs(2.0 * noise.Fbm(qx * 1.55 + 11.0, qy * 1.55 + 4.0, 4, 2.11, 0.5) - 1.0);
            double ridges = ridge * ridge;

            // 6:4 混合：纯 fBm 太"馒头"，纯脊线又过于嶙峋。
            return hills * 0.58 + ridges * 0.46;
        }
    }
}
