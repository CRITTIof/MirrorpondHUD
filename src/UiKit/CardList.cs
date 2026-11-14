using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 卡片里的列表：主题化的行、悬停 / 选中态、空状态、双击 / 回车激活、回收虚拟化。
///
/// 只负责“容器与交互”，行内容由卡片通过 <see cref="ItemTemplate"/> 或直接往 <see cref="Items"/> 里放控件决定。
/// 典型消费者：文件中转站、剪贴板历史、待办、搜索结果。
/// 2.36.0 起支持 <see cref="SetViewMode"/> 切换到 WrapPanel，供中转站的图标视图复用同一套选择 / 拖动逻辑。
/// </summary>
public sealed class CardList : Grid
{
    readonly CardListBox _list;
    readonly Border _emptyHost;
    readonly TextBlock _emptyText;

    StyleSettings _lastStyle;
    bool _tiles;

    public CardList()
    {
        _list = new CardListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            SelectionMode = SelectionMode.Single
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        _list.ItemsChanged += RefreshEmpty;

        _emptyText = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 12, 16, 12)
        };
        _emptyHost = new Border
        {
            Child = _emptyText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        Children.Add(_list);
        Children.Add(_emptyHost);

        _list.MouseDoubleClick += (s, e) => RaiseActivated();
        _list.PreviewKeyDown += (s, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            RaiseActivated();
        };
    }

    /// <summary>底层的 <see cref="ListBox"/>：需要 ItemTemplate / ContextMenu / 多选时直接用它。</summary>
    public ListBox Inner => _list;

    public ItemCollection Items => _list.Items;

    /// <summary>数据源（也可以直接往 <see cref="Items"/> 里放控件）。</summary>
    public IEnumerable ItemsSource
    {
        get => _list.ItemsSource;
        set
        {
            _list.ItemsSource = value;
            RefreshEmpty();
        }
    }

    public DataTemplate ItemTemplate
    {
        get => _list.ItemTemplate; set => _list.ItemTemplate = value;
    }

    public object SelectedItem
    {
        get => _list.SelectedItem; set => _list.SelectedItem = value;
    }

    public int SelectedIndex
    {
        get => _list.SelectedIndex; set => _list.SelectedIndex = value;
    }

    /// <summary>单选 / 扩展多选；中转站用 Extended 支持 Ctrl / Shift。</summary>
    public SelectionMode SelectionMode
    {
        get => _list.SelectionMode; set => _list.SelectionMode = value;
    }

    /// <summary>当前选中的行（多选时可能多条）。</summary>
    public IList SelectedItems => _list.SelectedItems;

    public bool IsEmpty => _list.Items.Count == 0;

    /// <summary>true = WrapPanel 图标网格（不虚拟化）；false = 原来的虚拟化纵向列表。</summary>
    public bool Tiles => _tiles;

    /// <summary>
    /// 切换列表 / 图标网格。图标网格的条目宽度由行自己决定，CardList 只负责换 ItemsPanel；
    /// 选择、右键、拖拽仍然走同一个 ListBox，避免中转站维护两套交互。
    /// </summary>
    public void SetViewMode(bool tiles)
    {
        if (_tiles == tiles) return;
        _tiles = tiles;

        if (tiles)
        {
            var panel = new FrameworkElementFactory(typeof(WrapPanel));
            panel.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            panel.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
            _list.ItemsPanel = new ItemsPanelTemplate(panel);
            VirtualizingPanel.SetIsVirtualizing(_list, false);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Standard);
            ScrollViewer.SetCanContentScroll(_list, false);
        }
        else
        {
            _list.ClearValue(ItemsControl.ItemsPanelProperty);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(_list, true);
        }

        // 调用方可能先 SetViewMode 再 ApplyStyle；若已经套过样式，这里补一次行模板。
        if (_lastStyle is not null) ApplyStyle(_lastStyle);
    }

    /// <summary>双击一行或按回车时触发（参数是当前选中项）。</summary>
    public event Action<object> ItemActivated;

    public event SelectionChangedEventHandler SelectionChanged
    {
        add => _list.SelectionChanged += value; remove => _list.SelectionChanged -= value;
    }

    public void SetEmptyText(string text)
    {
        _emptyText.Text = text ?? "";
        RefreshEmpty();
    }

    /// <summary>按当前外观重套：行高 / 字体 / 悬停 / 选中 / 滚动条 / 空状态颜色。</summary>
    public void ApplyStyle(StyleSettings style)
    {
        StyleSettings s = style ?? new StyleSettings();
        _lastStyle = s;

        ThemeTokens tokens = ThemeResolver.Resolve(s);
        Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

        _list.FontFamily = CardPalette.ResolveFont(s.FontFamily);
        _list.FontSize = Math.Max(10, s.FontSize - 1);
        _list.Foreground = CardPalette.Brush(foreground);
        _list.ItemContainerStyle = BuildItemStyle(tokens, foreground, EndfieldVisuals.SelectionBar(s), _tiles);

        CliScrollBars.Apply(this, tokens.Scrollbar, tokens.ScrollbarHot);

        _emptyText.FontFamily = _list.FontFamily;
        _emptyText.FontSize = _list.FontSize;
        _emptyText.Foreground = CardPalette.Brush(foreground, 0.45);

        _emptyHost.BorderThickness = new Thickness(1);
        _emptyHost.BorderBrush = CardPalette.Brush(foreground, 0.10);
        _emptyHost.CornerRadius = new CornerRadius(6);
    }

    public void ScrollToEnd()
    {
        if (_list.Items.Count == 0) return;
        _list.ScrollIntoView(_list.Items[_list.Items.Count - 1]);
    }

    public void RefreshEmpty() => _emptyHost.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    void RaiseActivated()
    {
        object item = _list.SelectedItem;
        if (item is null) return;

        Action<object> handler = ItemActivated;
        if (handler is not null) handler(item);
    }

    static Style BuildItemStyle(ThemeTokens tokens, Color foreground, bool selectionBar, bool tiles)
    {
        bool bar = selectionBar;
        string contentMargin = bar && !tiles ? "4,0,0,0" : "0";
        string barSetter = "";
        if (bar)
        {
            barSetter = tiles
                ? "<Setter TargetName=\"BottomBar\" Property=\"Visibility\" Value=\"Visible\"/>"
                : "<Setter TargetName=\"Bar\" Property=\"Visibility\" Value=\"Visible\"/>";
        }
        double selectedAlpha = selectionBar ? 0.24 : 0.16;

        const string templateXaml =
            "<ControlTemplate TargetType=\"ListBoxItem\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Border x:Name=\"ItemHost\" Background=\"{TemplateBinding Background}\" Padding=\"{TemplateBinding Padding}\" " +
            "BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" CornerRadius=\"%RADIUS%\">" +
            "<Grid>" +
            "<ContentPresenter Margin=\"%CONTENTMARGIN%\" HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" " +
            "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"/>" +
            "<Border x:Name=\"Bar\" Width=\"2\" HorizontalAlignment=\"Left\" " +
            "VerticalAlignment=\"Stretch\" Background=\"%BAR%\" Visibility=\"Collapsed\"/>" +
            "<Border x:Name=\"BottomBar\" Height=\"3\" HorizontalAlignment=\"Stretch\" " +
            "VerticalAlignment=\"Bottom\" Background=\"%BAR%\" Visibility=\"Collapsed\"/>" +
            "</Grid></Border>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsSelected\" Value=\"True\">" +
            "%BARSETTER%" +
            "</Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate>";

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty,
            tiles ? new Thickness(4) : new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(Control.MarginProperty,
            tiles ? new Thickness(3) : new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Control.ForegroundProperty, CardPalette.Brush(foreground)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

        string template = templateXaml
            .Replace("%RADIUS%", tiles ? "4" : "0")
            .Replace("%CONTENTMARGIN%", contentMargin)
            .Replace("%BAR%", ThemeResolver.WithAlpha(tokens.Accent, 0.9).ToString())
            .Replace("%BARSETTER%", barSetter);

        // K-29：行模板只跟颜色 / 圆角 / 选项有关，按这些参数缓存；解析失败保持默认行样式。
        ControlTemplate rowTemplate = CardTemplates.Get(
            $"CardList.Row|{CardTemplates.Hex(foreground)}|{CardTemplates.Hex(tokens.Accent)}|{bar}|{tiles}",
            () => (ControlTemplate)XamlReader.Parse(template), "卡片列表行模板");
        if (rowTemplate is not null)
            style.Setters.Add(new Setter(Control.TemplateProperty, rowTemplate));

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, CardPalette.Brush(foreground, 0.06)));
        style.Triggers.Add(hover);

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, CardPalette.Brush(tokens.Accent, selectedAlpha)));
        if (tiles)
        {
            var border = new Setter(Control.BorderBrushProperty, CardPalette.Brush(tokens.Accent, 0.55));
            selected.Setters.Add(border);
            selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        }
        style.Triggers.Add(selected);

        return style;
    }

    /// <summary>只为把「条目变化」转成事件；<see cref="ItemCollection"/> 本身没有稳定的集合通知。</summary>
    sealed class CardListBox : ListBox
    {
        public Action ItemsChanged;

        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);
            Action handler = ItemsChanged;
            if (handler is not null) handler();
        }
    }
}
