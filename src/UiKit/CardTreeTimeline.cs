using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>把空字符串的 ToolTip 变成 null，避免叶子节点弹出空白提示框。</summary>
    internal sealed class EmptyTextToNullConverter : IValueConverter
    {
        public static readonly EmptyTextToNullConverter Instance = new EmptyTextToNullConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            if (text != null) return text.Length == 0 ? null : text;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 树里的一个节点：文字、可选图标 / 提示 / 业务对象，以及子节点集合与展开状态。
    /// 属性变更会通知界面，所以宿主可以就地更新文字或展开态。
    /// </summary>
    public sealed class CardTreeItem : INotifyPropertyChanged
    {
        string _text = "";
        string _icon = "";
        string _tooltip;
        object _tag;
        bool _isExpanded;

        /// <summary>显示文字。</summary>
        public string Text
        {
            get { return _text; }
            set { if (Set(ref _text, value ?? "", nameof(Text))) return; }
        }

        /// <summary>可选图标字形（空 = 不显示）。</summary>
        public string Icon
        {
            get { return _icon; }
            set { Set(ref _icon, value ?? "", nameof(Icon)); }
        }

        /// <summary>可选悬停提示（空 / null = 不弹提示）。</summary>
        public string Tooltip
        {
            get { return _tooltip; }
            set { Set(ref _tooltip, value, nameof(Tooltip)); }
        }

        /// <summary>宿主自定义的业务对象，控件不解释它。</summary>
        public object Tag
        {
            get { return _tag; }
            set { if (Set(ref _tag, value, nameof(Tag))) return; }
        }

        /// <summary>是否展开子节点（双向绑定到 TreeViewItem）。</summary>
        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { if (Set(ref _isExpanded, value, nameof(IsExpanded))) return; }
        }

        /// <summary>子节点集合。</summary>
        public ObservableCollection<CardTreeItem> Children { get; } = new ObservableCollection<CardTreeItem>();

        public CardTreeItem()
        {
        }

        public CardTreeItem(string text)
        {
            Text = text ?? "";
        }

        /// <summary>追加一个子节点并返回它，方便链式建树。</summary>
        public CardTreeItem Add(string text, object tag = null)
        {
            var child = new CardTreeItem(text) { Tag = tag };
            Children.Add(child);
            return child;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>给调试 / 自动化留一个可读的名字。</summary>
        public override string ToString()
        {
            return _text;
        }

        bool Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return true;
            field = value;
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
            return false;
        }
    }

    /// <summary>
    /// 主题化树控件：自绘展开箭头、选中 / 悬停背景、空状态，滚动条复用 CliScrollBars，
    /// 不出现系统默认的白底和蓝色选中条。双击 / 回车触发 <see cref="ItemActivated"/>。
    /// </summary>
    public sealed class CardTree : Grid
    {
        readonly TreeView _tree;
        readonly Border _emptyHost;
        readonly TextBlock _emptyText;
        readonly ObservableCollection<CardTreeItem> _roots = new ObservableCollection<CardTreeItem>();

        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        FontFamily _font = new FontFamily("Microsoft YaHei UI");
        double _fontSize = 13;
        double _radius = 8;

        /// <summary>空树：默认套默认主题，宿主随后调 <see cref="ApplyStyle"/>。</summary>
        public CardTree()
        {
            _tree = new TreeView
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_tree, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(_tree, true);
            VirtualizingPanel.SetIsVirtualizing(_tree, true);
            VirtualizingPanel.SetVirtualizationMode(_tree, VirtualizationMode.Recycling);
            _tree.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse(
                "<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<VirtualizingStackPanel/></ItemsPanelTemplate>");
            _tree.ItemsSource = _roots;
            _tree.ItemTemplate = BuildItemTemplate();
            _tree.SelectedItemChanged += OnSelectedItemChanged;
            _tree.PreviewMouseLeftButtonDown += OnMouseDown;
            _tree.MouseDoubleClick += OnDoubleClick;
            _tree.KeyDown += OnKeyDown;

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

            Children.Add(_tree);
            Children.Add(_emptyHost);

            _roots.CollectionChanged += OnRootsChanged;
            _emptyText.Text = "暂无数据";
            ApplyStyle(null, 8);
            RefreshEmpty();
        }

        /// <summary>根节点集合（直接 Add / Remove 也会刷新空状态）。</summary>
        public IList<CardTreeItem> Roots
        {
            get { return _roots; }
            set
            {
                _roots.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++)
                    {
                        if (value[i] != null) _roots.Add(value[i]);
                    }
                }
            }
        }

        /// <summary>当前选中项（未生成容器时设置会尽量展开父节点后再选中）。</summary>
        public CardTreeItem SelectedItem
        {
            get { return _tree.SelectedItem as CardTreeItem; }
            set { Select(value); }
        }

        /// <summary>空树时显示的提示文字。</summary>
        public string EmptyText
        {
            get { return _emptyText.Text ?? ""; }
            set { _emptyText.Text = value ?? ""; RefreshEmpty(); }
        }

        /// <summary>选中项变化（参数是当前选中节点；清空选择时不触发）。</summary>
        public event Action<CardTreeItem> SelectionChanged;

        /// <summary>双击 / 回车激活一个节点。</summary>
        public event Action<CardTreeItem> ItemActivated;

        /// <summary>追加一个根节点并返回它。</summary>
        public CardTreeItem AddRoot(string text, object tag = null)
        {
            var item = new CardTreeItem(text) { Tag = tag };
            _roots.Add(item);
            return item;
        }

        /// <summary>追加一个准备好的根节点。</summary>
        public void AddRoot(CardTreeItem item)
        {
            if (item == null) return;
            _roots.Add(item);
        }

        /// <summary>清空所有根节点。</summary>
        public void Clear()
        {
            _roots.Clear();
        }

        /// <summary>递归展开所有节点。</summary>
        public void ExpandAll()
        {
            SetExpanded(_roots, true);
        }

        /// <summary>递归收起所有节点。</summary>
        public void CollapseAll()
        {
            SetExpanded(_roots, false);
        }

        /// <summary>套用卡片外观：字体、前景色、选中 / 悬停背景、圆角、滚动条、空状态。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _tokens = ThemeResolver.Resolve(style);
            _foreground = CardPalette.TryColor(style == null ? null : style.Foreground,
                                               Color.FromRgb(0xE6, 0xED, 0xF3));
            _font = CardPalette.ResolveFont(style == null ? null : style.FontFamily);
            _fontSize = Math.Max(10, (style == null ? 15 : style.FontSize) - 1);
            _radius = Math.Max(0, Math.Min(12, cornerRadius));

            _tree.FontFamily = _font;
            _tree.FontSize = _fontSize;
            _tree.Foreground = CardPalette.Brush(_foreground, 0.9);

            // 用隐式资源样式，嵌套层级的 TreeViewItem 也会套用同一套自绘模板。
            _tree.Resources[typeof(TreeViewItem)] = BuildItemStyle(_tokens, _foreground, _fontSize, _radius);
            CliScrollBars.Apply(this, _tokens.Scrollbar, _tokens.ScrollbarHot);

            _emptyText.FontFamily = _font;
            _emptyText.FontSize = _fontSize;
            _emptyText.Foreground = CardPalette.Brush(_foreground, 0.45);
            _emptyHost.BorderThickness = new Thickness(1);
            _emptyHost.BorderBrush = CardPalette.Brush(_foreground, 0.10);
            _emptyHost.CornerRadius = new CornerRadius(6);
        }

        void OnRootsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshEmpty();
        }

        void RefreshEmpty()
        {
            _emptyHost.Visibility = _roots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as CardTreeItem;
            if (item == null) return;
            Action<CardTreeItem> handler = SelectionChanged;
            if (handler != null) handler(item);
        }

        void Select(CardTreeItem item)
        {
            if (item == null) return;

            var path = new List<CardTreeItem>();
            if (!FindPath(_roots, item, path)) return;

            for (int i = 0; i < path.Count - 1; i++) path[i].IsExpanded = true;
            _tree.UpdateLayout();

            TreeViewItem container = FindContainer(_tree, item);
            if (container == null)
            {
                // 容器可能还没生成（折叠 / 虚拟化）：再强制布局一次。
                _tree.UpdateLayout();
                container = FindContainer(_tree, item);
            }
            if (container == null) return;

            container.IsSelected = true;
            container.BringIntoView();
        }

        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item == null || !item.HasItems) return;

            // 只有左侧小三角那一列切换展开，点文字仍然只是选中。
            Point point = e.GetPosition(item);
            if (point.X > 18) return;

            item.IsExpanded = !item.IsExpanded;
            e.Handled = true;
        }

        void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item == null) return;

            // 双击小三角已经由单击处理过展开，不再当“激活”。
            Point point = e.GetPosition(item);
            if (item.HasItems && point.X <= 18) return;

            var data = item.DataContext as CardTreeItem;
            if (data == null) return;

            Action<CardTreeItem> handler = ItemActivated;
            if (handler != null) handler(data);
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var item = _tree.SelectedItem as CardTreeItem;
            if (item == null) return;

            Action<CardTreeItem> handler = ItemActivated;
            if (handler != null) handler(item);
            e.Handled = true;
        }

        static void SetExpanded(IEnumerable<CardTreeItem> items, bool expanded)
        {
            if (items == null) return;
            foreach (CardTreeItem item in items)
            {
                if (item == null) continue;
                item.IsExpanded = expanded;
                SetExpanded(item.Children, expanded);
            }
        }

        static bool FindPath(IEnumerable<CardTreeItem> items, CardTreeItem target, List<CardTreeItem> path)
        {
            if (items == null) return false;
            foreach (CardTreeItem item in items)
            {
                if (item == null) continue;
                path.Add(item);
                if (ReferenceEquals(item, target)) return true;
                if (FindPath(item.Children, target, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        static TreeViewItem FindContainer(ItemsControl parent, object item)
        {
            if (parent == null) return null;
            foreach (object child in parent.Items)
            {
                var container = parent.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                if (container == null) continue;
                if (ReferenceEquals(child, item)) return container;

                TreeViewItem nested = FindContainer(container, item);
                if (nested != null) return nested;
            }
            return null;
        }

        internal static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                T match = source as T;
                if (match != null) return match;

                DependencyObject parent = null;
                try
                {
                    if (source is Visual || source is System.Windows.Media.Media3D.Visual3D)
                        parent = VisualTreeHelper.GetParent(source);
                }
                catch { parent = null; }
                if (parent == null) parent = LogicalTreeHelper.GetParent(source);
                source = parent;
            }
            return null;
        }

        static DataTemplate BuildItemTemplate()
        {
            const string xaml =
                "<HierarchicalDataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "ItemsSource=\"{Binding Children}\">" +
                "<StackPanel Orientation=\"Horizontal\" Background=\"Transparent\">" +
                "<TextBlock Text=\"{Binding Icon}\" Margin=\"0,0,6,0\" VerticalAlignment=\"Center\" FontSize=\"12\"/>" +
                "<TextBlock Text=\"{Binding Text}\" VerticalAlignment=\"Center\"/>" +
                "</StackPanel></HierarchicalDataTemplate>";
            return (DataTemplate)XamlReader.Parse(xaml);
        }

        static Style BuildItemStyle(ThemeTokens tokens, Color foreground, double fontSize, double radius)
        {
            string template =
                "<ControlTemplate TargetType=\"TreeViewItem\" " +
                "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Grid>" +
                "<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>" +
                "<Border x:Name=\"ItemHost\" Background=\"Transparent\" CornerRadius=\"%RADIUS%\" " +
                "Padding=\"3,2,6,2\" Margin=\"0,0,0,1\">" +
                "<Grid>" +
                "<Grid.ColumnDefinitions><ColumnDefinition Width=\"15\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions>" +
                "<TextBlock x:Name=\"Glyph\" Text=\"\u25B8\" FontSize=\"%GLYPH%\" Foreground=\"%MUTED%\" " +
                "VerticalAlignment=\"Center\" HorizontalAlignment=\"Left\" Visibility=\"Collapsed\"/>" +
                "<ContentPresenter Grid.Column=\"1\" ContentSource=\"Header\" HorizontalAlignment=\"Stretch\" " +
                "VerticalAlignment=\"Center\"/>" +
                "</Grid>" +
                "</Border>" +
                "<ItemsPresenter x:Name=\"ItemsHost\" Grid.Row=\"1\" Margin=\"15,0,0,0\" Visibility=\"Collapsed\"/>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<MultiTrigger><MultiTrigger.Conditions>" +
                "<Condition Property=\"HasItems\" Value=\"True\"/>" +
                "<Condition Property=\"IsExpanded\" Value=\"False\"/>" +
                "</MultiTrigger.Conditions>" +
                "<Setter TargetName=\"Glyph\" Property=\"Visibility\" Value=\"Visible\"/></MultiTrigger>" +
                "<MultiTrigger><MultiTrigger.Conditions>" +
                "<Condition Property=\"HasItems\" Value=\"True\"/>" +
                "<Condition Property=\"IsExpanded\" Value=\"True\"/>" +
                "</MultiTrigger.Conditions>" +
                "<Setter TargetName=\"Glyph\" Property=\"Visibility\" Value=\"Visible\"/>" +
                "<Setter TargetName=\"Glyph\" Property=\"Text\" Value=\"\u25BE\"/>" +
                "<Setter TargetName=\"ItemsHost\" Property=\"Visibility\" Value=\"Visible\"/></MultiTrigger>" +
                "<Trigger SourceName=\"ItemHost\" Property=\"IsMouseOver\" Value=\"True\">" +
                "<Setter TargetName=\"ItemHost\" Property=\"Background\" Value=\"%HOVER%\"/></Trigger>" +
                "<Trigger Property=\"IsSelected\" Value=\"True\">" +
                "<Setter TargetName=\"ItemHost\" Property=\"Background\" Value=\"%SELECTED%\"/></Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            template = template
                .Replace("%RADIUS%", radius.ToString("0.##", CultureInfo.InvariantCulture))
                .Replace("%GLYPH%", Math.Max(7, fontSize - 3).ToString("0.##", CultureInfo.InvariantCulture))
                .Replace("%MUTED%", tokens.Muted.ToString())
                .Replace("%HOVER%", ThemeResolver.WithAlpha(foreground, 0.06).ToString())
                .Replace("%SELECTED%", ThemeResolver.WithAlpha(tokens.Accent, 0.16).ToString());

            var style = new Style(typeof(TreeViewItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(template)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.ForegroundProperty, CardPalette.Brush(foreground, 0.9)));
            style.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty,
                new Binding("IsExpanded") { Mode = BindingMode.TwoWay }));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                new Binding("Tooltip") { Converter = EmptyTextToNullConverter.Instance }));
            return style;
        }
    }

    /// <summary>时间线圆点 / 文字的语气。</summary>
    public enum CardTimelineTone
    {
        Normal,
        Accent,
        Success,
        Warning,
        Danger
    }

    /// <summary>
    /// 时间线上的一条：时间、标题、详情、语气、可选图标与宿主业务对象。
    /// 主题色由 <see cref="CardTimeline"/> 在 Add / ApplyStyle 时写入。
    /// </summary>
    public sealed class CardTimelineItem : INotifyPropertyChanged
    {
        string _time = "";
        string _title = "";
        string _detail = "";
        string _icon = "";
        CardTimelineTone _tone = CardTimelineTone.Normal;
        object _tag;

        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        SolidColorBrush _toneBrush;
        SolidColorBrush _lineBrush;

        public CardTimelineItem()
        {
            UpdateBrushes();
        }

        /// <summary>左侧时间文字（字符串，控件不解析时间）。</summary>
        public string Time
        {
            get { return _time; }
            set { Set(ref _time, value ?? "", nameof(Time)); }
        }

        /// <summary>标题。</summary>
        public string Title
        {
            get { return _title; }
            set { Set(ref _title, value ?? "", nameof(Title)); }
        }

        /// <summary>详情（空 = 只显示标题）。</summary>
        public string Detail
        {
            get { return _detail; }
            set { Set(ref _detail, value ?? "", nameof(Detail)); }
        }

        /// <summary>可选图标字形。</summary>
        public string Icon
        {
            get { return _icon; }
            set { Set(ref _icon, value ?? "", nameof(Icon)); }
        }

        /// <summary>语气：决定左侧圆点颜色。</summary>
        public CardTimelineTone Tone
        {
            get { return _tone; }
            set
            {
                if (_tone == value) return;
                _tone = value;
                UpdateBrushes();
                Raise(nameof(Tone));
            }
        }

        /// <summary>宿主自定义业务对象。</summary>
        public object Tag
        {
            get { return _tag; }
            set { Set(ref _tag, value, nameof(Tag)); }
        }

        /// <summary>圆点颜色（跟随语气与主题）。</summary>
        public SolidColorBrush ToneBrush
        {
            get { return _toneBrush; }
        }

        /// <summary>连接圆点的竖线颜色（跟随主题描边色）。</summary>
        public SolidColorBrush LineBrush
        {
            get { return _lineBrush; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void ApplyTheme(ThemeTokens tokens, Color foreground)
        {
            _tokens = tokens ?? ThemeResolver.Resolve(null);
            _foreground = foreground;
            UpdateBrushes();
        }

        void UpdateBrushes()
        {
            _toneBrush = CardPalette.Brush(ToneColor());
            _lineBrush = CardPalette.Brush(_tokens.Border);
            Raise(nameof(ToneBrush));
            Raise(nameof(LineBrush));
        }

        Color ToneColor()
        {
            switch (_tone)
            {
                case CardTimelineTone.Accent:
                    return _tokens.Accent;
                case CardTimelineTone.Danger:
                    return _tokens.Danger;
                case CardTimelineTone.Success:
                    // 主题令牌没有独立“成功色”，用强调色表达成功语气。
                    return _tokens.Accent;
                case CardTimelineTone.Warning:
                    return ThemeResolver.WithAlpha(_foreground, 0.85);
                default:
                    return _tokens.Muted;
            }
        }

        void Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            Raise(name);
        }

        void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 主题化时间线：左侧时间、中间圆点 + 竖线、右侧标题 / 详情，虚拟化列表 + 空状态。
    /// 双击 / 回车触发 <see cref="ItemActivated"/>；<see cref="SelectionChanged"/> 可选订阅。
    /// </summary>
    public sealed class CardTimeline : Grid
    {
        readonly ListBox _list;
        readonly Border _emptyHost;
        readonly TextBlock _emptyText;
        readonly ObservableCollection<CardTimelineItem> _items = new ObservableCollection<CardTimelineItem>();

        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        FontFamily _font = new FontFamily("Microsoft YaHei UI");
        double _fontSize = 13;
        double _radius = 8;

        /// <summary>空时间线：默认套默认主题，宿主随后调 <see cref="ApplyStyle"/>。</summary>
        public CardTimeline()
        {
            _list = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                SelectionMode = SelectionMode.Single
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(_list, true);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            _list.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse(
                "<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<VirtualizingStackPanel/></ItemsPanelTemplate>");
            _list.ItemsSource = _items;
            _list.ItemTemplate = BuildItemTemplate();
            _list.SelectionChanged += OnSelectionChanged;
            _list.MouseDoubleClick += OnDoubleClick;
            _list.KeyDown += OnKeyDown;

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

            _items.CollectionChanged += OnItemsChanged;
            _emptyText.Text = "暂无时间线";
            ApplyStyle(null, 8);
            RefreshEmpty();
        }

        /// <summary>时间线数据（直接 Add / Remove 也会刷新空状态与主题色）。</summary>
        public IList<CardTimelineItem> Items
        {
            get { return _items; }
            set
            {
                _items.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++)
                    {
                        CardTimelineItem item = value[i];
                        if (item == null) continue;
                        item.ApplyTheme(_tokens, _foreground);
                        _items.Add(item);
                    }
                }
            }
        }

        /// <summary>空时间线时显示的提示文字。</summary>
        public string EmptyText
        {
            get { return _emptyText.Text ?? ""; }
            set { _emptyText.Text = value ?? ""; RefreshEmpty(); }
        }

        /// <summary>条目数量。</summary>
        public int Count
        {
            get { return _items.Count; }
        }

        /// <summary>当前选中项（只读；通过点击 / 键盘选择）。</summary>
        public CardTimelineItem SelectedItem
        {
            get { return _list.SelectedItem as CardTimelineItem; }
        }

        /// <summary>选中项变化（可选订阅）。</summary>
        public event Action<CardTimelineItem> SelectionChanged;

        /// <summary>双击 / 回车激活一条。</summary>
        public event Action<CardTimelineItem> ItemActivated;

        /// <summary>追加一条并返回它。</summary>
        public CardTimelineItem Add(string time, string title, string detail = null,
                                    CardTimelineTone tone = CardTimelineTone.Normal, string icon = null)
        {
            var item = new CardTimelineItem
            {
                Time = time,
                Title = title,
                Detail = detail,
                Tone = tone,
                Icon = icon
            };
            item.ApplyTheme(_tokens, _foreground);
            _items.Add(item);
            return item;
        }

        /// <summary>清空所有条目。</summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>滚到最后一条（没有条目时什么都不做）。</summary>
        public void ScrollToEnd()
        {
            if (_items.Count == 0) return;
            _list.UpdateLayout();
            _list.ScrollIntoView(_items[_items.Count - 1]);
        }

        /// <summary>套用卡片外观：字体、前景色、悬停 / 选中背景、圆点颜色、滚动条、空状态。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _tokens = ThemeResolver.Resolve(style);
            _foreground = CardPalette.TryColor(style == null ? null : style.Foreground,
                                               Color.FromRgb(0xE6, 0xED, 0xF3));
            _font = CardPalette.ResolveFont(style == null ? null : style.FontFamily);
            _fontSize = Math.Max(10, (style == null ? 15 : style.FontSize) - 1);
            _radius = Math.Max(0, Math.Min(12, cornerRadius));

            _list.FontFamily = _font;
            _list.FontSize = _fontSize;
            _list.Foreground = CardPalette.Brush(_foreground, 0.9);
            _list.ItemContainerStyle = BuildItemStyle(_tokens, _foreground, _radius);
            CliScrollBars.Apply(this, _tokens.Scrollbar, _tokens.ScrollbarHot);

            for (int i = 0; i < _items.Count; i++) _items[i].ApplyTheme(_tokens, _foreground);

            _emptyText.FontFamily = _font;
            _emptyText.FontSize = _fontSize;
            _emptyText.Foreground = CardPalette.Brush(_foreground, 0.45);
            _emptyHost.BorderThickness = new Thickness(1);
            _emptyHost.BorderBrush = CardPalette.Brush(_foreground, 0.10);
            _emptyHost.CornerRadius = new CornerRadius(6);
        }

        void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshEmpty();
        }

        void RefreshEmpty()
        {
            _emptyHost.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = _list.SelectedItem as CardTimelineItem;
            if (item == null) return;
            Action<CardTimelineItem> handler = SelectionChanged;
            if (handler != null) handler(item);
        }

        void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var container = CardTree.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            var item = container == null ? _list.SelectedItem as CardTimelineItem
                                         : container.DataContext as CardTimelineItem;
            if (item == null) return;

            Action<CardTimelineItem> handler = ItemActivated;
            if (handler != null) handler(item);
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var item = _list.SelectedItem as CardTimelineItem;
            if (item == null) return;

            Action<CardTimelineItem> handler = ItemActivated;
            if (handler != null) handler(item);
            e.Handled = true;
        }

        static DataTemplate BuildItemTemplate()
        {
            const string xaml =
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Grid>" +
                "<Grid.ColumnDefinitions>" +
                "<ColumnDefinition Width=\"52\"/><ColumnDefinition Width=\"18\"/><ColumnDefinition Width=\"*\"/>" +
                "</Grid.ColumnDefinitions>" +
                "<TextBlock Grid.Column=\"0\" Text=\"{Binding Time}\" TextAlignment=\"Right\" " +
                "TextTrimming=\"CharacterEllipsis\" Margin=\"0,1,8,0\" FontSize=\"11\" Opacity=\"0.65\"/>" +
                "<Grid Grid.Column=\"1\">" +
                "<Border Width=\"1\" HorizontalAlignment=\"Center\" Background=\"{Binding LineBrush}\"/>" +
                "<Border Width=\"7\" Height=\"7\" CornerRadius=\"3.5\" Background=\"{Binding ToneBrush}\" " +
                "VerticalAlignment=\"Top\" HorizontalAlignment=\"Center\" Margin=\"0,5,0,0\"/>" +
                "</Grid>" +
                "<StackPanel Grid.Column=\"2\" Margin=\"6,0,0,3\">" +
                "<StackPanel Orientation=\"Horizontal\">" +
                "<TextBlock Text=\"{Binding Icon}\" Margin=\"0,0,5,0\" FontSize=\"12\" Opacity=\"0.85\"/>" +
                "<TextBlock Text=\"{Binding Title}\" FontWeight=\"SemiBold\" TextTrimming=\"CharacterEllipsis\"/>" +
                "</StackPanel>" +
                "<TextBlock Text=\"{Binding Detail}\" Margin=\"0,1,0,0\" Opacity=\"0.62\" TextWrapping=\"Wrap\"/>" +
                "</StackPanel>" +
                "</Grid></DataTemplate>";
            return (DataTemplate)XamlReader.Parse(xaml);
        }

        static Style BuildItemStyle(ThemeTokens tokens, Color foreground, double radius)
        {
            string template =
                "<ControlTemplate TargetType=\"ListBoxItem\" " +
                "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Border x:Name=\"ItemHost\" Background=\"Transparent\" CornerRadius=\"%RADIUS%\" " +
                "Padding=\"4,3,6,3\" Margin=\"0,0,0,1\">" +
                "<ContentPresenter HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" " +
                "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger SourceName=\"ItemHost\" Property=\"IsMouseOver\" Value=\"True\">" +
                "<Setter TargetName=\"ItemHost\" Property=\"Background\" Value=\"%HOVER%\"/></Trigger>" +
                "<Trigger Property=\"IsSelected\" Value=\"True\">" +
                "<Setter TargetName=\"ItemHost\" Property=\"Background\" Value=\"%SELECTED%\"/></Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            template = template
                .Replace("%RADIUS%", radius.ToString("0.##", CultureInfo.InvariantCulture))
                .Replace("%HOVER%", ThemeResolver.WithAlpha(foreground, 0.06).ToString())
                .Replace("%SELECTED%", ThemeResolver.WithAlpha(tokens.Accent, 0.16).ToString());

            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(template)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.ForegroundProperty, CardPalette.Brush(foreground, 0.9)));
            return style;
        }
    }
}
