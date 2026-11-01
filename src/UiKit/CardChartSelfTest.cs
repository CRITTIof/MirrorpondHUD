using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 图表 / 树 / 时间线控件的离屏自检：实例化全部控件、套主题、Measure/Arrange 并渲染到
    /// <see cref="RenderTargetBitmap"/>（至少 320×180），检查空数据、数据操作、自动缩放、
    /// 展开 / 选中、时间线计数与滚动都不崩且结果正确。
    ///
    /// 返回 null 表示通过，否则返回中文失败说明。只依赖 Contracts / UiKit，不引用宿主类型。
    /// </summary>
    public static class CardChartSelfTest
    {
        /// <summary>跑一遍自检；通过返回 null，失败返回中文说明。</summary>
        public static string Run()
        {
            try
            {
                return RunCore();
            }
            catch (Exception ex)
            {
                return "图表 / 树 / 时间线自检异常：" + ex.GetType().Name + "：" + ex.Message;
            }
        }

        static string RunCore()
        {
            var style = new StyleSettings
            {
                FontFamily = "Microsoft YaHei UI",
                FontSize = 15,
                Foreground = "#E6EDF3",
                Accent = "#2F6FEB",
                Danger = "#D14343"
            };

            // ==================== 迷你趋势线 ====================
            var spark = new CardSparkline();
            spark.ApplyStyle(style, 8);
            Render(spark, 240, 40);                                       // 空数据

            spark.SetValues(new double[] { 1, double.NaN, 3, double.PositiveInfinity, 2 });
            if (spark.Values.Count != 5)
                return "CardSparkline.SetValues 后数量为 " + spark.Values.Count + "，应为 5";

            spark.Add(4);
            if (spark.Values.Count != 6)
                return "CardSparkline.Add 后数量为 " + spark.Values.Count + "，应为 6";
            Render(spark, 240, 40);                                       // NaN / Infinity 安全

            spark.AutoScale = true;
            spark.SetValues(new double[] { 7, 7, 7 });                    // 所有值相同
            Render(spark, 240, 40);

            spark.AutoScale = false;
            spark.Minimum = 0;
            spark.Maximum = 10;
            spark.Fill = true;
            spark.LineThickness = 2.4;
            Render(spark, 240, 40);
            Render(spark, 1, 1);                                          // 宽高 < 2 不崩

            // ==================== 折线图 ====================
            var line = new CardLineChart();
            line.ApplyStyle(style, 10);
            Render(line, 320, 180);                                       // 空数据

            line.Series = new List<CardChartSeries>
            {
                new CardChartSeries("CPU") { Values = new List<double> { 12, 30, double.NaN, 55, 40 } },
                new CardChartSeries("内存") { Values = new List<double> { 40, 42, 44, 47, 50 } }
            };
            line.Labels = new List<string> { "00", "05", "10", "15", "20" };
            line.ShowGrid = true;
            line.ShowLegend = true;
            line.ShowYLabels = true;
            line.ShowXLabels = true;
            line.Fill = true;
            Render(line, 400, 220);

            line.Series = new List<CardChartSeries>
            {
                new CardChartSeries("常数") { Values = new List<double> { 5, 5, 5, 5 } }
            };
            line.Labels = new List<string>();                             // 标签为空 / 数量不同都安全
            Render(line, 320, 180);

            line.AutoScale = false;
            line.Minimum = 0;
            line.Maximum = 100;
            Render(line, 320, 180);

            line.Series = null;
            line.Labels = null;
            Render(line, 320, 180);
            Render(line, 1, 1);

            // ==================== 柱状图 ====================
            var bar = new CardBarChart();
            bar.ApplyStyle(style, 10);
            Render(bar, 320, 180);                                        // 空数据

            bar.Series = new List<CardChartSeries>
            {
                new CardChartSeries("收入") { Values = new List<double> { 12, 30, double.NaN, 55 } },
                new CardChartSeries("支出") { Values = new List<double> { 8, -6, 22, 40 } }
            };
            bar.Labels = new List<string> { "Q1", "Q2", "Q3", "Q4" };
            bar.ShowGrid = true;
            bar.ShowLegend = true;
            Render(bar, 400, 220);

            bar.Series = new List<CardChartSeries>                        // 单序列
            {
                new CardChartSeries("单序列") { Values = new List<double> { 1, 2, 3, 4, 5 } }
            };
            bar.Labels = new List<string> { "1", "2", "3", "4", "5" };
            Render(bar, 320, 180);

            bar.ShowLegend = false;
            bar.ShowGrid = false;
            bar.ShowYLabels = false;
            bar.ShowXLabels = false;
            Render(bar, 320, 180);

            bar.Series = null;
            bar.Labels = null;
            Render(bar, 320, 180);
            Render(bar, 1, 1);

            // ==================== 树 ====================
            var tree = new CardTree();
            tree.ApplyStyle(style, 8);
            tree.EmptyText = "暂无节点";
            Render(tree, 320, 200);                                       // 空树

            tree.AddRoot("根节点 1");
            CardTreeItem root = tree.AddRoot("根节点 2", "tag-2");
            CardTreeItem first = root.Add("子节点 A");
            root.Add("子节点 B").Add("孙节点");

            bool selectionRaised = false;
            tree.SelectionChanged += item =>
            {
                if (ReferenceEquals(item, first)) selectionRaised = true;
            };

            tree.ExpandAll();
            tree.UpdateLayout();
            if (!root.IsExpanded) return "CardTree.ExpandAll 后根节点没有展开";
            Render(tree, 320, 220);

            tree.SelectedItem = first;
            tree.UpdateLayout();
            if (!ReferenceEquals(tree.SelectedItem, first)) return "CardTree.SelectedItem 没有选中目标节点";
            if (!selectionRaised) return "CardTree.SelectionChanged 没有随选中触发";

            tree.CollapseAll();
            if (root.IsExpanded) return "CardTree.CollapseAll 后根节点仍然展开";

            tree.Clear();
            if (tree.Roots.Count != 0) return "CardTree.Clear 后根节点数量不为 0";
            Render(tree, 320, 200);

            // ==================== 时间线 ====================
            var timeline = new CardTimeline();
            timeline.ApplyStyle(style, 8);
            timeline.EmptyText = "暂无事件";
            Render(timeline, 360, 200);                                   // 空时间线

            timeline.Add("12:00", "第一条", "详情一");
            timeline.Add("12:01", "第二条", null, CardTimelineTone.Accent);
            timeline.Add("12:02", "第三条", "详情三", CardTimelineTone.Danger, "!");
            if (timeline.Count != 3) return "CardTimeline.Count 为 " + timeline.Count + "，应为 3";
            Render(timeline, 360, 200);

            timeline.ScrollToEnd();
            timeline.Clear();
            if (timeline.Count != 0) return "CardTimeline.Clear 后 Count 不为 0";
            Render(timeline, 360, 200);

            return null;
        }

        /// <summary>测量 / 排布 / 渲染到离屏位图；控件小于 2px 也不能抛异常。</summary>
        static void Render(FrameworkElement element, int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);

            element.Measure(new Size(w, h));
            element.Arrange(new Rect(0, 0, w, h));
            element.UpdateLayout();

            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);

            if (bitmap.PixelWidth != w || bitmap.PixelHeight != h)
                throw new InvalidOperationException("离屏渲染尺寸异常");
        }
    }
}
