using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>中转站列表行的视觉构建：图标 + 名称 + 大小 / 收纳标记。抽出来让 StationView 保持纯 UI 协调。</summary>
    internal static class StationRow
    {
        public static FrameworkElement Build(StationItem item, StationSettings station, ICardHost host, Color foreground)
        {
            var grid = new Grid { Tag = item, ToolTip = item.DisplayPath };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Image
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (station.ShowIcons)
                icon.Source = host == null ? null : host.Shell.GetFileIcon(item.IsDirectory ? item.DisplayPath : item.Name, 16, !item.IsDirectory);
            grid.Children.Add(icon);

            string name = string.IsNullOrEmpty(item.Name) ? item.DisplayPath : item.Name;
            if (item.Missing) name += "（已丢失）";

            var title = new TextBlock
            {
                Text = name,
                Foreground = CardPalette.Brush(foreground, item.Missing ? 0.40 : 0.92),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            string meta = item.IsDirectory ? "文件夹" : StationFiles.FormatSize(item.Size);
            if (!string.IsNullOrEmpty(item.StoredPath)) meta = string.IsNullOrEmpty(meta) ? "已收纳" : meta + " · 已收纳";

            var detail = new TextBlock
            {
                Text = meta,
                Foreground = CardPalette.Brush(foreground, item.Missing ? 0.30 : 0.45),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(detail, 2);
            grid.Children.Add(detail);

            return grid;
        }
    }
}
