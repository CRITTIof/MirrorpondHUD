using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 中转站条目的视觉构建：列表行是图标 + 名称 + 大小，图标视图是大 / 中尺寸方块。
/// 抽出来让 StationView 保持纯交互协调；四种模式的尺寸集中在 <see cref="StationDisplayMode"/>。
///
/// P-17：这里**不再同步取图标**。旧实现每建一行就调一次 <c>GetFileIcon</c>，
/// 500 条上限 + 每次搜索按键全量重建 = UI 线程被图标提取与控件构造打满。
/// 现在行先放占位，图标由视图在后台线程取回后填入；行对象本身可复用，
/// 用 <see cref="StationRowHandle.Refresh"/> 更新文本与颜色即可。
/// </summary>
internal static class StationRow
{
    public static StationRowHandle Create(StationItem item, StationSettings station, Color foreground, string view)
    {
        string mode = StationDisplayMode.Normalize(view);
        return StationDisplayMode.IsTiles(mode)
            ? StationRowHandle.CreateTile(item, station, foreground, mode)
            : StationRowHandle.CreateLine(item, station, foreground, mode);
    }
}

/// <summary>
/// 一行 / 一个图块的控件组合。持有子控件引用，列表增量更新时只改内容、不重建容器；
/// 图标槽同时带占位文本，图标取不到（目录、网络盘、Shell 失败）时仍有可见反馈。
/// </summary>
internal sealed class StationRowHandle
{
    readonly Image _icon;
    readonly TextBlock _placeholder;
    readonly TextBlock _title;
    readonly TextBlock _detail;

    // P-17：行会被频繁 Refresh（搜索每敲一个键都会走一遍）。只有颜色 / 文本真的变化时才写
    // 依赖属性——否则每键都会重新分配 500 个 SolidColorBrush 并让整列表失效。
    bool _missingState;
    Color _foregroundState;
    bool _colorsPrimed;

    StationRowHandle(StationItem item, StationSettings station, Color foreground, string view, bool tile)
    {
        Item = item;
        Tile = tile;
        View = view;
        ShowIcons = station is not null && station.ShowIcons;
        IconSize = StationDisplayMode.IconSize(view);

        double iconSize = IconSize;
        _icon = new Image
        {
            Width = iconSize,
            Height = iconSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _placeholder = new TextBlock
        {
            Text = InitialOf(DisplayName(item)),
            FontSize = Math.Max(9, iconSize * 0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var iconHost = new Grid
        {
            Width = tile ? double.NaN : iconSize,
            Height = tile ? iconSize + 10 : iconSize,
            HorizontalAlignment = tile ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = tile ? new Thickness(0) : new Thickness(0, 0, 9, 0),
            Visibility = ShowIcons ? Visibility.Visible : Visibility.Collapsed
        };
        iconHost.Children.Add(_icon);
        iconHost.Children.Add(_placeholder);

        if (tile) Root = BuildTile(item, station, view, iconHost, out _title);
        else Root = BuildLine(item, foreground, iconSize, iconHost, out _title, out _detail);

        Refresh(item, foreground);
    }

    public StationItem Item { get; private set; }

    public FrameworkElement Root { get; }

    /// <summary>true = 图标网格里的图块，false = 列表行。切换显示方式时行形状不同，不能复用。</summary>
    public bool Tile { get; }

    public string View { get; }

    public bool ShowIcons { get; }

    public double IconSize { get; }

    /// <summary>当前图标槽对应的缓存键；迟到回调发现键不一致就不再写入。</summary>
    public string IconKey { get; set; }

    /// <summary>取图标用的参数：目录按路径取（可能碰磁盘），文件按扩展名取（不碰磁盘）。</summary>
    public string IconSourcePath => Item.IsDirectory ? Item.DisplayPath : Item.Name;

    public bool IconExtensionOnly => !Item.IsDirectory;

    internal static StationRowHandle CreateLine(StationItem item, StationSettings station, Color foreground, string view)
        => new(item, station, foreground, view, false);

    internal static StationRowHandle CreateTile(StationItem item, StationSettings station, Color foreground, string view)
        => new(item, station, foreground, view, true);

    static FrameworkElement BuildLine(StationItem item, Color foreground, double iconSize, Grid iconHost,
                                      out TextBlock title, out TextBlock detail)
    {
        var grid = new Grid
        {
            Tag = item,
            MinHeight = iconSize >= 24 ? 36 : 0
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(iconHost);

        title = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        detail = new TextBlock
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);

        return grid;
    }

    static FrameworkElement BuildTile(StationItem item, StationSettings station, string view,
                                      Grid iconHost, out TextBlock title)
    {
        double tileWidth = StationDisplayMode.TileWidth(view);
        double tileHeight = StationDisplayMode.TileHeight(view);

        var panel = new StackPanel
        {
            Tag = item,
            Width = tileWidth,
            MinHeight = tileHeight
        };
        panel.Children.Add(iconHost);

        title = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = tileWidth - 10,
            MaxHeight = 40,
            Margin = new Thickness(2, station is not null && station.ShowIcons ? 5 : 8, 2, 0)
        };
        panel.Children.Add(title);

        return panel;
    }

    /// <summary>
    /// 就地刷新一行的文本与颜色（存在性 / 名称 / 主题变化）。容器与图标槽保持不变，
    /// 所以状态回填不会引起整列表重建或图标重取。
    /// </summary>
    public void Refresh(StationItem item, Color foreground)
    {
        Item = item;
        Root.Tag = item;

        string name = DisplayName(item);
        string initial = InitialOf(name);
        string detail = Detail(item);

        if (!string.Equals(_title.Text, name, StringComparison.Ordinal)) _title.Text = name;
        if (_detail is not null && !string.Equals(_detail.Text, detail, StringComparison.Ordinal)) _detail.Text = detail;
        if (!string.Equals(_placeholder.Text, initial, StringComparison.Ordinal)) _placeholder.Text = initial;

        string tooltip = Tile ? item.DisplayPath + "\n" + detail : item.DisplayPath;
        if (!string.Equals(Root.ToolTip as string, tooltip, StringComparison.Ordinal)) Root.ToolTip = tooltip;

        if (!_colorsPrimed || _missingState != item.Missing || _foregroundState != foreground)
        {
            _colorsPrimed = true;
            _missingState = item.Missing;
            _foregroundState = foreground;

            _title.Foreground = CardPalette.Brush(foreground, item.Missing ? 0.40 : 0.92);
            if (_detail is not null)
                _detail.Foreground = CardPalette.Brush(foreground, item.Missing ? 0.30 : 0.45);
            _placeholder.Foreground = CardPalette.Brush(foreground, 0.55);
        }

        // 图标槽的可见性由显示设置决定；ShowIcons 变化会让视图重建行（形状签名不同）。
        _icon.Visibility = _icon.Source is null ? Visibility.Collapsed : Visibility.Visible;
        _placeholder.Visibility = _icon.Source is null && ShowIcons ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>填入后台取回的图标；null 表示取不到，保留占位（失败原因由视图记日志）。</summary>
    public void SetIcon(ImageSource source)
    {
        if (!ShowIcons) return;

        _icon.Source = source;
        bool loaded = source is not null;
        _icon.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        _placeholder.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
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

    static string InitialOf(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        string trimmed = name.Trim();
        return trimmed.Length == 0 ? "?" : trimmed.Substring(0, 1).ToUpperInvariant();
    }
}
