using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗「内容」页：卡片列表的重建 / 搜索过滤 / 增删 / 移动 / 展开，
/// 以及每张卡片自己的「呼出热键」录制与占位符插入。
///
/// 卡片内部的具体设置项由 <see cref="CardEditor"/> 提供，这里只负责列表编排；
/// 模块自己声明的通用热键面板在 <c>SettingsWindow.Hotkeys.cs</c>。
/// </summary>
public partial class SettingsWindow
{
    readonly List<CardEditor> _editors = new();
    readonly Dictionary<string, bool> _expanded = new(StringComparer.OrdinalIgnoreCase);
    CardEditor _dragEditor;
    Point _dragStart;
    bool _headerMouseDown;
    bool _headerDragged;

    // ==================== 卡片列表 ====================

    void RebuildCardList()
    {
        _editors.Clear();

        foreach (CardSettings config in _working.Cards)
        {
            if (!_expanded.TryGetValue(config.Id, out var expanded)) expanded = true;

            var editor = new CardEditor(config, CardRegistry.Get(config.Kind), _working, expanded);
            string id = config.Id;
            editor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "IsExpanded") _expanded[id] = editor.IsExpanded;
                else MarkDirty();
            };
            _editors.Add(editor);
        }

        for (int i = 0; i < _editors.Count; i++)
            _editors[i].SpacingEnabled = i > 0;

        ApplyCardFilter();
    }

    void ApplyCardFilter()
    {
        string keyword = (SearchBox.Text ?? "").Trim();

        // 每次都换一个新 List：ItemsControl 不对 List<T> 的增删做通知，
        // 复用同一个实例会导致增删 / 移动后界面不刷新。
        List<CardEditor> filtered;
        if (keyword.Length == 0)
        {
            filtered = new List<CardEditor>(_editors);
        }
        else
        {
            filtered = _editors.Where(e =>
                e.DisplayName.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                e.KindLabel.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        }

        CardList.ItemsSource = filtered;
        EmptyHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = keyword.Length == 0
            ? "还没有卡片，点左上角「添加卡片」新建一张"
            : $"没有匹配「{keyword}」的卡片";
    }

    void SearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_working is not null) ApplyCardFilter();
    }

    void AddCardClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        foreach (ICardModule module in CardRegistry.Modules)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = module.Icon,
                FontFamily = (FontFamily)FindResource("LucideFont"),
                FontSize = 15,
                Foreground = (Brush)FindResource("Accent"),
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = module.DisplayName,
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            var item = new MenuItem { Header = header, Tag = module };
            if (module.IsInteractive && CardModel.CountInteractive(_working.Cards) > 0)
            {
                item.IsEnabled = false;
                item.ToolTip = "只能有一张交互卡片（键盘独占）";
            }
            item.Click += (s, a) => AddCard((ICardModule)((MenuItem)s).Tag);
            menu.Items.Add(item);
        }

        menu.PlacementTarget = AddCardButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    void AddCard(ICardModule module)
    {
        if (module is null) return;

        if (module.IsInteractive && CardModel.CountInteractive(_working.Cards) > 0)
        {
            StatusText.Text = "只能有一张交互卡片（键盘独占）：先删掉现有的命令行卡。";
            return;
        }

        CardSettings card = CardModel.Create(module, _working.Cards);
        if (card is null) return;

        _working.Cards.Add(card);
        _expanded[card.Id] = true;
        MarkDirty();

        SearchBox.Text = "";
        RebuildCardList();
        StatusText.Text = $"已添加卡片「{card.Name}」，保存并应用后生效";

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { CardScroll.ScrollToEnd(); } catch { }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    void DeleteCardClick(object sender, RoutedEventArgs e)
    {
        CardEditor editor = (sender as FrameworkElement)?.Tag as CardEditor;
        if (editor is null) return;

        if (_working.Cards.Count <= 1)
        {
            StatusText.Text = "至少保留一张卡片";
            return;
        }

        MessageBoxResult answer = MessageBox.Show(this,
            "删除卡片「" + editor.DisplayName + "」？\n保存并应用后生效。",
            "镜潭一览", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        _working.Cards.Remove(editor.Config);
        _expanded.Remove(editor.Config.Id);
        MarkDirty();
        RebuildCardList();
        StatusText.Text = $"已删除卡片「{editor.DisplayName}」，保存并应用后生效";
    }

    void MoveCardClick(object sender, RoutedEventArgs e)
    {
        CardEditor editor = (sender as FrameworkElement)?.Tag as CardEditor;
        if (editor is null) return;

        int index = _working.Cards.IndexOf(editor.Config);

        var menu = new ContextMenu();
        var up = new MenuItem { Header = "上移", IsEnabled = index > 0 };
        up.Click += (s, a) => MoveCard(editor, -1);
        var down = new MenuItem { Header = "下移", IsEnabled = index >= 0 && index < _working.Cards.Count - 1 };
        down.Click += (s, a) => MoveCard(editor, 1);
        menu.Items.Add(up);
        menu.Items.Add(down);

        menu.PlacementTarget = sender as UIElement;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    void MoveCard(CardEditor editor, int delta)
    {
        int index = _working.Cards.IndexOf(editor.Config);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _working.Cards.Count) return;

        _working.Cards.RemoveAt(index);
        _working.Cards.Insert(target, editor.Config);

        // 移到非首位时给一个可见的最小间距，避免和上一张卡片贴在一起。
        if (target > 0 && editor.Config.Spacing < 1)
            editor.Config.Spacing = 12;

        RebuildCardList();
        MarkDirty();
        StatusText.Text = $"卡片「{editor.DisplayName}」已移动到第 {(target + 1)} 位";
    }

    /// <summary>复制一张卡片：Extras 也一起复制，新 Id / 名称追加“副本”。</summary>
    void DuplicateCardClick(object sender, RoutedEventArgs e)
    {
        CardEditor editor = (sender as FrameworkElement)?.Tag as CardEditor;
        if (editor is null) return;

        if (editor.Module is not null && editor.Module.IsInteractive)
        {
            StatusText.Text = "交互卡（命令行 / AI 面板）只能有一张，不能复制。";
            return;
        }

        CardSettings copy;
        try
        {
            copy = Json.Deserialize<CardSettings>(Json.Serialize(editor.Config));
        }
        catch (Exception ex)
        {
            Log.Warn($"复制卡片失败: {ex.Message}");
            StatusText.Text = $"复制卡片失败：{ex.Message}";
            return;
        }

        if (copy is null)
        {
            StatusText.Text = "复制卡片失败：配置为空";
            return;
        }

        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = string.IsNullOrWhiteSpace(copy.Name)
            ? ($"{editor.DisplayName} 副本")
            : $"{copy.Name} 副本";

        int index = _working.Cards.IndexOf(editor.Config);
        _working.Cards.Insert(index < 0 ? _working.Cards.Count : index + 1, copy);
        CardModel.Normalize(_working);
        _expanded[copy.Id] = true;
        RebuildCardList();
        MarkDirty();
        StatusText.Text = $"已复制卡片「{editor.DisplayName}」，保存后生效";
    }

    // ==================== 拖动排序 ====================

    /// <summary>
    /// 标题栏拖动：整条标题栏都可以拖，按钮 / 输入框 / 下拉框等交互控件除外。
    /// 鼠标按下时先捕获，移动超过阈值才启动 DragDrop；原地单击仍然切换展开。
    /// </summary>
    void CardHeaderDragDown(object sender, MouseButtonEventArgs e)
    {
        var header = sender as FrameworkElement;
        if (header is null || IsInteractiveCardHeaderSource(e.OriginalSource as DependencyObject)) return;

        _dragEditor = header.DataContext as CardEditor;
        _dragStart = e.GetPosition(this);
        _headerMouseDown = _dragEditor is not null;
        _headerDragged = false;
        if (!_headerMouseDown) return;

        header.CaptureMouse();
        e.Handled = true;
    }

    void CardHeaderDragMove(object sender, MouseEventArgs e)
    {
        if (!_headerMouseDown || _dragEditor is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point now = e.GetPosition(this);
        if (Math.Abs(now.X - _dragStart.X) < 4 && Math.Abs(now.Y - _dragStart.Y) < 4) return;

        _headerDragged = true;
        _headerMouseDown = false;
        var header = sender as FrameworkElement;
        if (header is not null && header.IsMouseCaptured) header.ReleaseMouseCapture();

        CardEditor drag = _dragEditor;
        DragDrop.DoDragDrop((DependencyObject)sender, drag, DragDropEffects.Move);
        // DoDragDrop 期间 CardListDragOver / CardListDrop 需要 _dragEditor 作为源；
        // 拖放结束后再清空，避免 Drop 拿到 null。
        _dragEditor = null;
    }

    void CardHeaderDragUp(object sender, MouseButtonEventArgs e)
    {
        if (!_headerMouseDown && !_headerDragged) return;

        var header = sender as FrameworkElement;
        if (header is not null && header.IsMouseCaptured) header.ReleaseMouseCapture();

        bool shouldToggle = _headerMouseDown && !_headerDragged;
        CardEditor editor = _dragEditor;
        _headerMouseDown = false;
        _headerDragged = false;
        _dragEditor = null;

        if (shouldToggle && editor is not null) editor.IsExpanded = !editor.IsExpanded;
        e.Handled = true;
    }

    /// <summary>点到标题栏里的按钮 / 输入框 / 下拉框时不要启动拖拽。</summary>
    static bool IsInteractiveCardHeaderSource(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is ButtonBase || source is TextBoxBase || source is ComboBox ||
                source is Slider || source is CheckBox)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    void CardListDragOver(object sender, DragEventArgs e)
    {
        if (_dragEditor is null) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    void CardListDrop(object sender, DragEventArgs e)
    {
        CardEditor drag = _dragEditor;
        _dragEditor = null;
        if (drag is null) return;

        e.Handled = true;
        CardEditor target = FindCardEditor(e.OriginalSource as DependencyObject);
        int from = _working.Cards.IndexOf(drag.Config);
        int to = target is null ? _working.Cards.Count : _working.Cards.IndexOf(target.Config);
        if (from < 0) return;

        // 放到自己身上 / 原位置后一位都不需要移动。
        if (to == from || to == from + 1) return;

        _working.Cards.RemoveAt(from);
        if (to > from) to--;
        if (to < 0) to = 0;
        if (to > _working.Cards.Count) to = _working.Cards.Count;
        _working.Cards.Insert(to, drag.Config);

        RebuildCardList();
        MarkDirty();
        StatusText.Text = $"卡片「{drag.DisplayName}」已移动到第 {(to + 1)} 位";
    }

    static CardEditor FindCardEditor(DependencyObject source)
    {
        while (source is not null)
        {
            var element = source as FrameworkElement;
            if (element is not null && element.DataContext is CardEditor)
                return (CardEditor)element.DataContext;

            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    void ToggleCardClick(object sender, RoutedEventArgs e)
    {
        CardEditor editor = (sender as FrameworkElement)?.Tag as CardEditor;
        if (editor is not null) editor.IsExpanded = !editor.IsExpanded;
    }

    void CardHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = sender as TextBox;
        CardEditor editor = box is null ? null : box.DataContext as CardEditor;
        if (editor is null) return;

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            editor.Hotkey = "";
            return;
        }

        if (IsModifierKey(key)) return;

        editor.Hotkey = HotkeyManager.Format(key, Keyboard.Modifiers);
    }

    void InsertTokenClick(object sender, SelectionChangedEventArgs e)
    {
        var combo = sender as ComboBox;
        if (combo is null) return;

        var token = combo.SelectedItem as TokenInfo;
        if (token is null) return;

        var box = combo.Tag as TextBox;
        if (box is not null)
        {
            string insert = "{" + token.Name + "}";
            string text = box.Text ?? "";
            int caret = Math.Min(Math.Max(0, box.CaretIndex), text.Length);
            box.Text = text.Insert(caret, insert);
            box.CaretIndex = caret + insert.Length;
            box.Focus();
        }

        combo.SelectedIndex = -1;
    }

    // ==================== 卡片内部（历史拆分后保留的辅助方法） ====================
    // 卡片自己的设置区块已迁到 CardEditor.cs；这里保留按 Tag / DataContext 取编辑器的
    // 通用辅助方法，行为与拆分前一致。

    static CardEditor EditorOf(object sender)
    {
        var element = sender as FrameworkElement;
        if (element is null) return null;
        return element.Tag as CardEditor ?? element.DataContext as CardEditor;
    }
}
