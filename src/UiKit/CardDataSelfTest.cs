using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 2.17 数据展示控件的轻量回归：布局 / 排序 / 选择 / 自绘安全。
    /// 由宿主 --selftest 调用；返回 null 表示通过，否则是中文失败说明。
    /// </summary>
    public static class CardDataSelfTest
    {
        public static string Run()
        {
            var style = new StyleSettings
            {
                FontSize = 13,
                Foreground = "#FFE6EDF3",
                Accent = "#FF2F6FEB",
                Danger = "#FFD14343"
            };

            var table = new CardTable { Width = 340, Height = 190 };
            table.AddColumn("名称", null, null, new GridLength(1, GridUnitType.Star));
            table.AddColumn("大小", null, null, new GridLength(72));
            table.AddRow("b.txt", "2 KB");
            table.AddRow("a.txt", "1 KB");
            table.ApplyStyle(style, 8);
            Render(table, 340, 190);

            if (table.Count != 2) return "CardTable 行数不是 2";
            table.SortBy(0, false);
            if (table.SortColumnIndex != 0 || table.SortDescending) return "CardTable 排序状态没记录";
            var first = table.GetItem(0) as CardTableItem;
            if (first == null || first.Cell(0) != "a.txt") return "CardTable 升序排序结果不对";

            table.SortBy(0, true);
            first = table.GetItem(0) as CardTableItem;
            if (first == null || first.Cell(0) != "b.txt") return "CardTable 降序排序结果不对";

            table.SelectedIndex = 0;
            if (table.SelectedItem == null) return "CardTable 选中项为空";
            table.ClearRows();
            if (!table.IsEmpty || table.Count != 0) return "CardTable 清空后不空";

            var empty = new CardTable { Width = 220, Height = 100, EmptyText = "没有记录" };
            empty.AddColumn("列");
            empty.ApplyStyle(style, 8);
            Render(empty, 220, 100);
            if (!empty.IsEmpty) return "CardTable 空表状态错误";

            var badge = new CardBadge("运行中", CardBadgeTone.Success);
            badge.ApplyStyle(style, 8);
            Render(badge, 80, 24);
            if (badge.Text != "运行中") return "CardBadge 文本丢失";

            var tag = new CardTag("高优先级", CardBadgeTone.Warning) { Removable = true };
            tag.ApplyStyle(style, 10);
            Render(tag, 100, 26);
            if (tag.Text != "高优先级") return "CardTag 文本丢失";

            var tile = new CardMetricTile("CPU", "42") { Unit = "%", Delta = "+3.2", Caption = "较上次", Icon = "◈" };
            tile.Trend = CardMetricTrend.Up;
            tile.ApplyStyle(style, 10);
            Render(tile, 150, 90);
            if (tile.Value != "42" || tile.Unit != "%") return "CardMetricTile 数值 / 单位丢失";

            var gauge = new CardGauge { Value = 62, Label = "CPU", Unit = "%", Width = 140, Height = 96 };
            gauge.ApplyStyle(style, 10);
            Render(gauge, 140, 96);
            if (gauge.ActualWidth <= 0 || gauge.ActualHeight <= 0) return "CardGauge 布局失败";

            var ring = new CardProgressRing { Value = 76, Label = "完成", Width = 100, Height = 100 };
            ring.ApplyStyle(style, 10);
            Render(ring, 100, 100);
            if (ring.ActualWidth <= 0 || ring.ActualHeight <= 0) return "CardProgressRing 布局失败";

            return null;
        }

        static void Render(FrameworkElement element, double width, double height)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(Math.Max(1, width)),
                (int)Math.Ceiling(Math.Max(1, height)),
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
        }
    }
}
