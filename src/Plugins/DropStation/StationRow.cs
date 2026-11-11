using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 中转站条目的视觉构建：列表行是图标 + 名称 + 大小，图标视图是大 / 中尺寸方块。
/// 抽出来让 StationView 保持纯交互协调；四种模式的尺寸集中在 <see cref="StationDisplayMode"/>。
/// </summary>
internal static class StationRow
{
    public static FrameworkElement Build(StationItem item, StationSettings station, ICardHost host,
                                         Color foreground, string view)
    {
        string mode = StationDisplayMode.Normalize(view);
        return StationDisplayMode.IsTiles(mode)
            ? BuildTile(item, station, host, foreground, mode)
            : BuildLine(item, station, host, foreground, mode);
    }

    static FrameworkElement BuildLine(StationItem item, StationSettings station, ICardHost host,
                                      Color foreground, string view)
    {
        double iconSize = StationDisplayMode.IconSize(view);

        var grid = new Grid
        {
            Tag = item,
            ToolTip = item.DisplayPath,
            MinHeight = string.Equals(view, StationDisplayMode.SmallIcon) ? 36 : 0
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Image
        {
            Width = iconSize,
            Height = iconSize,
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (station.ShowIcons)
            icon.Source = host is null ? null : host.Shell.GetFileIcon(
                item.IsDirectory ? item.DisplayPath : item.Name, (int)iconSize, !item.IsDirectory);
        grid.Children.Add(icon);

        string name = DisplayName(item);
        var title = new TextBlock
        {
            Text = name,
            Foreground = CardPalette.Brush(foreground, item.Missing ? 0.40 : 0.92),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var detail = new TextBlock
        {
            Text = Detail(item),
            Foreground = CardPalette.Brush(foreground, item.Missing ? 0.30 : 0.45),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);

        return grid;
    }

    static FrameworkElement BuildTile(StationItem item, StationSettings station, ICardHost host,
                                      Color foreground, string view)
    {
        double iconSize = StationDisplayMode.IconSize(view);
        double tileWidth = StationDisplayMode.TileWidth(view);
        double tileHeight = StationDisplayMode.TileHeight(view);

        var panel = new StackPanel
        {
            Tag = item,
            Width = tileWidth,
            MinHeight = tileHeight,
            ToolTip = item.DisplayPath + "\n" + Detail(item)
        };

        if (station.ShowIcons)
        {
            var icon = new Image
            {
                Width = iconSize,
                Height = iconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (host is not null)
                icon.Source = host.Shell.GetFileIcon(
                    item.IsDirectory ? item.DisplayPath : item.Name, (int)iconSize, !item.IsDirectory);

            var iconHost = new Grid
            {
                Height = iconSize + 10,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            iconHost.Children.Add(icon);
            panel.Children.Add(iconHost);
        }

        var title = new TextBlock
        {
            Text = DisplayName(item),
            Foreground = CardPalette.Brush(foreground, item.Missing ? 0.40 : 0.92),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = tileWidth - 10,
            MaxHeight = 40,
            Margin = new Thickness(2, station.ShowIcons ? 5 : 8, 2, 0)
        };
        panel.Children.Add(title);

        return panel;
    }

    static string DisplayName(StationItem item)
    {
        string name = string.IsNullOrEmpty(item.Name) ? item.DisplayPath : item.Name;
        return item.Missing ? name + "（已丢失）" : name;
    }

    static string Detail(StationItem item)
    {
        string meta = item.IsDirectory ? "文件夹" : StationFiles.FormatSize(item.Size);
        if (!string.IsNullOrEmpty(item.StoredPath))
            meta = string.IsNullOrEmpty(meta) ? "已收纳" : meta + " · 已收纳";
        return meta;
    }
}
