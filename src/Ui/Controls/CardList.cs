using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 卡片里的列表：主题化的行、悬停 / 选中态、空状态、双击 / 回车激活、回收虚拟化。
    ///
    /// 只负责“容器与交互”，行内容由卡片通过 <see cref="ItemTemplate"/> 或直接往 <see cref="Items"/> 里放控件决定。
    /// 典型消费者：文件中转站、剪贴板历史、待办、搜索结果。
    /// </summary>
    public sealed class CardList : Grid
    {
        readonly CardListBox _list;
        readonly Border _emptyHost;
        readonly TextBlock _emptyText;

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
        public ListBox Inner { get { return _list; } }

        public ItemCollection Items { get { return _list.Items; } }

        /// <summary>数据源（也可以直接往 <see cref="Items"/> 里放控件）。</summary>
        public IEnumerable ItemsSource
        {
            get { return _list.ItemsSource; }
            set
            {
                _list.ItemsSource = value;
                RefreshEmpty();
            }
        }

        public DataTemplate ItemTemplate
        {
            get { return _list.ItemTemplate; }
            set { _list.ItemTemplate = value; }
        }

        public object SelectedItem
        {
            get { return _list.SelectedItem; }
            set { _list.SelectedItem = value; }
        }

        public int SelectedIndex
        {
            get { return _list.SelectedIndex; }
            set { _list.SelectedIndex = value; }
        }

        /// <summary>单选 / 扩展多选；中转站用 Extended 支持 Ctrl / Shift。</summary>
        public SelectionMode SelectionMode
        {
            get { return _list.SelectionMode; }
            set { _list.SelectionMode = value; }
        }

        /// <summary>当前选中的行（多选时可能多条）。</summary>
        public IList SelectedItems { get { return _list.SelectedItems; } }

        public bool IsEmpty { get { return _list.Items.Count == 0; } }

        /// <summary>双击一行或按回车时触发（参数是当前选中项）。</summary>
        public event Action<object> ItemActivated;

        public event SelectionChangedEventHandler SelectionChanged
        {
            add { _list.SelectionChanged += value; }
            remove { _list.SelectionChanged -= value; }
        }

        public void SetEmptyText(string text)
        {
            _emptyText.Text = text ?? "";
            RefreshEmpty();
        }

        /// <summary>按当前外观重套：行高 / 字体 / 悬停 / 选中 / 滚动条 / 空状态颜色。</summary>
        public void ApplyStyle(StyleSettings style, CliSettings cli)
        {
            StyleSettings s = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(s);
            Color foreground = OverlaySurface.ParseColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            _list.FontFamily = CardPalette.ResolveFont(s.FontFamily);
            _list.FontSize = Math.Max(10, s.FontSize - 1);
            _list.Foreground = new SolidColorBrush(foreground);
            _list.ItemContainerStyle = BuildItemStyle(tokens, foreground);

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

        public void RefreshEmpty()
        {
            _emptyHost.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void RaiseActivated()
        {
            object item = _list.SelectedItem;
            if (item == null) return;

            Action<object> handler = ItemActivated;
            if (handler != null) handler(item);
        }

        static Style BuildItemStyle(ThemeTokens tokens, Color foreground)
        {
            const string templateXaml =
                "<ControlTemplate TargetType=\"ListBoxItem\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Border Background=\"{TemplateBinding Background}\" Padding=\"{TemplateBinding Padding}\" CornerRadius=\"4\">" +
                "<ContentPresenter HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" " +
                "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"/>" +
                "</Border></ControlTemplate>";

            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 1)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(foreground)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(templateXaml)));

            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, CardPalette.Brush(foreground, 0.06)));
            style.Triggers.Add(hover);

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, CardPalette.Brush(tokens.Accent, 0.16)));
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
                if (handler != null) handler();
            }
        }
    }
}
