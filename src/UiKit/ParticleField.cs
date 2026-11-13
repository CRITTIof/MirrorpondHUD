using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 粒子聚字的运动场（2.47.0）：只保存位置 / 目标 / 收敛计算，不碰可视树与采样。
///
/// 拆出来的目的：采样（<see cref="ParticleTextSampler"/>）与绘制（<see cref="CardParticleText"/>）
/// 可以各自演进；宿主信息卡以后要复用同一套指数收敛时，只依赖这个类即可。
/// 无惯性：位置按指数逼近目标，不存速度、不冲过头。
/// </summary>
public sealed class ParticleField
{
    double[] _x = Array.Empty<double>();
    double[] _y = Array.Empty<double>();
    double[] _tx = Array.Empty<double>();
    double[] _ty = Array.Empty<double>();

    public int Count { get; private set; }

    public bool HasPool => Count > 0;

    /// <summary>清空粒子池（换歌 / 卡片重建）。</summary>
    public void Reset()
    {
        Count = 0;
        if (_x.Length == 0) return;
        Array.Clear(_x, 0, _x.Length);
        Array.Clear(_y, 0, _y.Length);
    }

    /// <summary>首池：粒子直接落在目标位，不做可见位移。</summary>
    public void Prime(IReadOnlyList<Point> targets)
    {
        if (targets is null || targets.Count == 0)
        {
            Count = 0;
            return;
        }

        EnsureCapacity(targets.Count);
        Count = targets.Count;
        for (int i = 0; i < Count; i++)
        {
            _x[i] = _tx[i] = targets[i].X;
            _y[i] = _ty[i] = targets[i].Y;
        }
    }

    /// <summary>
    /// 换句：每个粒子在**自己的目标字形附近**随机生成，再向目标收拢。
    /// 不复用上一句的位置——否则短句换行时，粒子会先散成上一整行再飞回来。
    /// </summary>
    public void ScatterAround(IReadOnlyList<Point> targets, double radius, Random random)
    {
        if (targets is null || targets.Count == 0)
        {
            Count = 0;
            return;
        }

        EnsureCapacity(targets.Count);
        Count = targets.Count;

        double r = Math.Max(2.0, radius);
        for (int i = 0; i < Count; i++)
        {
            Point target = targets[i];
            double angle = random.NextDouble() * Math.PI * 2.0;
            // 开方分布让粒子在圆面内均匀铺开，不会都挤在目标点上。
            double distance = r * (0.2 + 0.8 * Math.Sqrt(random.NextDouble()));
            _tx[i] = target.X;
            _ty[i] = target.Y;
            _x[i] = target.X + Math.Cos(angle) * distance;
            _y[i] = target.Y + Math.Sin(angle) * distance;
        }
    }

    /// <summary>推进一帧，返回平均剩余距离；调用方用它判断是否收敛。</summary>
    public double Step(double dt, double speed)
    {
        if (Count == 0 || dt <= 0) return 0;

        double k = 1.0 - Math.Exp(-dt * 24.0 * Math.Max(0.2, speed));
        double sum = 0;
        for (int i = 0; i < Count; i++)
        {
            _x[i] += (_tx[i] - _x[i]) * k;
            _y[i] += (_ty[i] - _y[i]) * k;
            double dx = _tx[i] - _x[i];
            double dy = _ty[i] - _y[i];
            sum += Math.Sqrt(dx * dx + dy * dy);
        }
        return sum / Count;
    }

    /// <summary>逐粒子画方块；越界的直接跳过，和宿主粒子层同一套裁剪判断。</summary>
    public void Render(DrawingContext dc, Brush brush, double size, double width, double height)
    {
        if (Count == 0 || brush is null) return;

        double half = size * 0.5;
        for (int i = 0; i < Count; i++)
        {
            double x = _x[i];
            double y = _y[i];
            if (x < -size || y < -size || x > width + size || y > height + size) continue;
            dc.DrawRectangle(brush, null, new Rect(x - half, y - half, size, size));
        }
    }

    void EnsureCapacity(int count)
    {
        if (_x.Length >= count) return;
        int size = Math.Max(count, 128);
        _x = new double[size];
        _y = new double[size];
        _tx = new double[size];
        _ty = new double[size];
    }
}
