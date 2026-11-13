using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 把一段文字采样成粒子目标点（2.47.0）。
///
/// 与宿主信息卡同一条取点规则：先按面积粗采样得到候选，再洗牌取固定数量；
/// 否则小字行会取到全部墨迹像素，粒子叠成实体字，看不出聚拢过程。
/// 采样位图按 64 像素对齐缓存，连续换行不必每次新建 RenderTargetBitmap。
/// </summary>
public sealed class ParticleTextSampler
{
    const byte AlphaThreshold = 24;
    const int MaxCandidatePoints = 40000;
    const long MaxSamplePixels = 2_000_000;
    const int BitmapQuantum = 64;

    RenderTargetBitmap _rtb;
    int _pixelWidth;
    int _pixelHeight;
    byte[] _pixels = Array.Empty<byte>();

    /// <summary>字体 / 字号 / 宽度变化时采样结果不可复用，调用方重建实例即可。</summary>
    public bool TrySample(string text, FontFamily font, double fontSize, FontWeight weight,
                          double width, DpiScale dpi, int wanted, Random random,
                          out List<Point> targets)
    {
        targets = null;
        if (string.IsNullOrWhiteSpace(text) || width < 8) return false;

        try
        {
            double scale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            var typeface = new Typeface(font, FontStyles.Normal, weight, FontStretches.Normal);
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                Math.Max(8, fontSize),
                Brushes.White,
                dpi.PixelsPerDip)
            {
                MaxTextWidth = width
            };

            double height = formatted.Height;
            if (height < 1 || formatted.Width < 1) return false;

            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

            // 像素预算：极长的一行在 4K 下也不至于一次拷出十几 MB。
            long pixels = (long)pixelWidth * pixelHeight;
            if (pixels > MaxSamplePixels)
            {
                double shrink = Math.Sqrt((double)MaxSamplePixels / pixels);
                scale *= shrink;
                pixelWidth = Math.Max(1, (int)(pixelWidth * shrink));
                pixelHeight = Math.Max(1, (int)(pixelHeight * shrink));
            }

            EnsureBitmap(pixelWidth, pixelHeight, scale);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawText(formatted, new Point(0, 0));
            }
            _rtb.Render(visual);

            // 位图按 64 像素对齐分配，行步长必须用对齐后的宽度；实际扫描仍只看 pixelWidth×pixelHeight。
            int stride = _pixelWidth * 4;
            int bytes = stride * _pixelHeight;
            if (_pixels.Length < bytes) _pixels = new byte[bytes];
            _rtb.CopyPixels(_pixels, stride, 0);

            int ink = 0;
            for (int y = 0; y < pixelHeight; y++)
            {
                int row = y * stride;
                for (int x = 0; x < pixelWidth; x++)
                {
                    if (_pixels[row + x * 4 + 3] > AlphaThreshold) ink++;
                }
            }
            if (ink == 0) return false;

            int area = pixelWidth * pixelHeight;
            int sampleStep = area > 160_000 ? 3 : (area > 60_000 ? 2 : 1);
            var points = new List<Point>(Math.Min(ink, MaxCandidatePoints));

            for (int y = 0; y < pixelHeight && points.Count < MaxCandidatePoints; y += sampleStep)
            {
                int row = y * stride;
                for (int x = 0; x < pixelWidth && points.Count < MaxCandidatePoints; x += sampleStep)
                {
                    if (_pixels[row + x * 4 + 3] <= AlphaThreshold) continue;
                    points.Add(new Point((x + 0.5) / scale, (y + 0.5) / scale));
                }
            }

            if (points.Count == 0) return false;
            if (points.Count > wanted)
            {
                // 部分 Fisher–Yates：只把前 wanted 个洗成随机样本，避免“前几行全取、后面没有”。
                for (int i = 0; i < wanted; i++)
                {
                    int j = i + random.Next(points.Count - i);
                    (points[i], points[j]) = (points[j], points[i]);
                }
                points.RemoveRange(wanted, points.Count - wanted);
            }

            targets = points;
            return true;
        }
        catch (Exception ex)
        {
            // 采样失败只影响这一轮动画；文字照常显示，原因进日志（AGENTS §5）。
            Log.Warn($"粒子聚字采样失败: {ex.Message}");
            return false;
        }
    }

    void EnsureBitmap(int pixelWidth, int pixelHeight, double scale)
    {
        int quantumWidth = ((pixelWidth + BitmapQuantum - 1) / BitmapQuantum) * BitmapQuantum;
        int quantumHeight = ((pixelHeight + BitmapQuantum - 1) / BitmapQuantum) * BitmapQuantum;
        if (_rtb is not null && _pixelWidth == quantumWidth && _pixelHeight == quantumHeight) return;

        _pixelWidth = quantumWidth;
        _pixelHeight = quantumHeight;
        _rtb = new RenderTargetBitmap(quantumWidth, quantumHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
    }
}
