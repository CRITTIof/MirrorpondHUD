using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Contour
{
    /// <summary>
    /// 卡片装饰层里的等高线。只画线：
    /// - 不再区分首曲线 / 计曲线，全部同色同宽；
    /// - 不再有高程数字、注记、示坡线；
    /// - 保留级数、疏密均衡、地形起伏、平滑轮数、最小闭合环面积。
    ///
    /// 生成在 UI 线程完成；卡片尺寸通常只有几百 DIP，网格很小。4K 大卡叠加
    /// 循环漂移时会明显更贵，性能说明见 docs/UI/endfield-m7-m8-implementation.md。
    /// </summary>
    internal sealed class ContourDecoration : FrameworkElement
    {
        const int Seed = 20261111;
        const int MaxCellsPerAxis = 140;

        public int LevelCount = 12;
        public double Evening = 0.72;
        public double Relief = 1.0;
        public int SmoothIterations = 2;
        public double MinLoopArea = 26.0;
        public double Phase = 0.0;
        public Color LineColor = Color.FromArgb(0x40, 0xF5, 0xF5, 0xF0);
        public double LineThickness = 0.8;

        Geometry _geometry;
        Pen _pen;
        bool _dirty = true;
        double _builtPhase = double.NaN;
        double _builtWidth;
        double _builtHeight;
        int _builtLevels;
        double _builtEvening;
        double _builtRelief;
        int _builtSmooth;
        double _builtMinLoop;
        Color _builtColor;
        double _builtThickness;

        public ContourDecoration()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
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
            _dirty = true;
            InvalidateVisual();
        }

        public void SetPhase(double phase)
        {
            if (Math.Abs(Phase - phase) < 1e-9) return;
            Phase = phase;
            _dirty = true;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 16.0 || height < 16.0) return;

            EnsureBuilt(width, height);
            if (_geometry == null || _pen == null) return;

            dc.DrawGeometry(null, _pen, _geometry);
        }

        void EnsureBuilt(double width, double height)
        {
            if (!_dirty
                && Math.Abs(_builtWidth - width) < 0.25
                && Math.Abs(_builtHeight - height) < 0.25
                && Math.Abs(_builtPhase - Phase) < 1e-9
                && _builtLevels == LevelCount
                && Math.Abs(_builtEvening - Evening) < 1e-9
                && Math.Abs(_builtRelief - Relief) < 1e-9
                && _builtSmooth == SmoothIterations
                && Math.Abs(_builtMinLoop - MinLoopArea) < 1e-9
                && _builtColor == LineColor
                && Math.Abs(_builtThickness - LineThickness) < 1e-9)
            {
                return;
            }

            _dirty = false;
            _builtWidth = width;
            _builtHeight = height;
            _builtPhase = Phase;
            _builtLevels = LevelCount;
            _builtEvening = Evening;
            _builtRelief = Relief;
            _builtSmooth = SmoothIterations;
            _builtMinLoop = MinLoopArea;
            _builtColor = LineColor;
            _builtThickness = LineThickness;

            var brush = new SolidColorBrush(LineColor);
            brush.Freeze();
            var pen = new Pen(brush, Math.Max(0.5, LineThickness));
            pen.Freeze();
            _pen = pen;

            try
            {
                _geometry = BuildGeometry(width, height);
            }
            catch (Exception ex)
            {
                Log.Warn("等高线生成失败: " + ex.Message);
                _geometry = null;
            }
        }

        Geometry BuildGeometry(double width, double height)
        {
            double cell = Math.Max(6.0, Math.Max(width, height) / MaxCellsPerAxis);
            double scale = 0.0034 / Math.Max(0.2, Relief);
            var field = new TerrainField(Seed, scale);

            ScalarGrid grid = ScalarGrid.Build(
                (x, y) => field.Sample(x, y, Phase), width, height, cell);
            FieldEqualizer.Apply(grid, Evening);

            ContourLevels levels = ContourLevels.FromRange(grid.Min, grid.Max, LevelCount);

            var geometry = new StreamGeometry();
            bool any = false;

            using (StreamGeometryContext ctx = geometry.Open())
            {
                foreach (double level in levels.Values)
                {
                    var segments = new List<ContourSegment>(512);
                    if (ContourExtractor.Extract(grid, level, segments) == 0) continue;

                    List<ContourPath> paths = ContourExtractor.Chain(segments, SmoothIterations);
                    foreach (ContourPath path in paths)
                    {
                        // 制图综合：太小的闭环多半是噪声碎屑；只对闭环生效。
                        if (path.Closed && Math.Abs(path.SignedArea) < MinLoopArea) continue;
                        if (path.Points == null || path.Points.Length < 2) continue;

                        ctx.BeginFigure(path.Points[0], false, path.Closed);
                        ctx.PolyLineTo(path.Points, true, false);
                        any = true;
                    }
                }
            }

            if (!any)
            {
                return null;
            }

            geometry.Freeze();
            return geometry;
        }
    }
}
