using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

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
    CardDragAdorner _dragAdorner;
    AdornerLayer _dragAdornerLayer;

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
        HudMenuStyle.Apply(menu, _settingsDark);

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
            // 滚动只是让新卡片进入视野：列表可能已重排 / 卸载，失败不影响添加结果，
            // 用 SafeRelease 留 Debug 记录即可（X-9）。
            SafeRelease.Run(() => CardScroll.ScrollToEnd(), "卡片列表滚动到底");
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

        // 原系统弹窗是 OKCancel：保持“确定 / 取消”的按钮语义与结果判断，不改成 Yes/No。
        HudDialogResult answer = HudDialog.Show(this,
            "镜潭一览", "删除卡片「" + editor.DisplayName + "」？\n保存并应用后生效。",
            HudDialogButtons.OkCancel, HudDialogIcon.Question, _working.Style);
        if (answer != HudDialogResult.Ok) return;

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
        HudMenuStyle.Apply(menu, _settingsDark);
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

    readonly Dictionary<string, TranslateTransform> _dragShifts = new();
    FrameworkElement _dragSourceContainer;
    double _dragStride;
    int _dragInsertIndex = -1;

    /// <summary>只有左侧六点抓手能启动拖拽；标题栏其它区域仍然点击展开 / 折叠。</summary>
    void CardDragHandleDown(object sender, MouseButtonEventArgs e)
    {
        var element = sender as FrameworkElement;
        _dragEditor = element is null ? null : element.Tag as CardEditor;
        _dragStart = e.GetPosition(this);
        _dragInsertIndex = -1;

        // 捕获鼠标，否则指针离开小抓手后 PreviewMouseMove 不再进来，拖不起来。
        if (element is not null) element.CaptureMouse();
        e.Handled = true;
    }

    void CardDragHandleMove(object sender, MouseEventArgs e)
    {
        if (_dragEditor is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point now = e.GetPosition(this);
        if (Math.Abs(now.X - _dragStart.X) < 4 && Math.Abs(now.Y - _dragStart.Y) < 4) return;

        var element = sender as FrameworkElement;
        if (element is not null && element.IsMouseCaptured) element.ReleaseMouseCapture();

        var card = CardList.ItemContainerGenerator.ContainerFromItem(_dragEditor) as FrameworkElement;
        FrameworkElement header = FindCardHeader(card);
        if (card is not null && header is not null)
        {
            // 跟随层只取标题栏快照：视觉上就是一张折叠卡，不再把展开的设置项拖来拖去。
            ImageSource ghost = CardDragAdorner.Capture(header);
            double headerHeight = Math.Max(30, header.ActualHeight);
            _dragSourceContainer = card;
            _dragStride = headerHeight + 10;

            _dragAdornerLayer = AdornerLayer.GetAdornerLayer(CardList);
            Point listPoint = e.GetPosition(CardList);
            if (_dragAdornerLayer is not null && ghost is not null)
            {
                // T-17：拖动层的颜色/圆角走当前工作设置里的主题令牌，不再用浮层内的硬编码颜色。
                // _working 在列表重建时就已就位；万一为空则交给 ThemeResolver 的默认派生。
                double cornerRadius = _working?.Overlay is null ? 8 : _working.Overlay.CornerRadius;
                _dragAdorner = new CardDragAdorner(CardList, ghost, Math.Max(96, card.ActualWidth), headerHeight,
                                                   _working?.Style, cornerRadius);
                _dragAdorner.SetPosition(new Point(listPoint.X + 14, listPoint.Y + 14));
                _dragAdornerLayer.Add(_dragAdorner);
                CardList.GiveFeedback += OnCardDragGiveFeedback;
            }

            // 源卡片收成标题栏高度：列表腾出紧凑的空位，下面的卡片平滑上移。
            card.ClipToBounds = true;
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(110))
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            card.BeginAnimation(UIElement.OpacityProperty, fade);
            var shrink = new DoubleAnimation(card.ActualHeight, headerHeight, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            card.BeginAnimation(FrameworkElement.HeightProperty, shrink);

            _dragInsertIndex = _working.Cards.IndexOf(_dragEditor.Config);
        }

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, _dragEditor, DragDropEffects.Move);
        }
        finally
        {
            EndDragVisual();
        }
    }

    void OnCardDragGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_dragAdorner is null) return;
        Point point = Mouse.GetPosition(CardList);
        _dragAdorner.SetPosition(new Point(point.X + 14, point.Y + 14));
        _dragAdorner.InvalidateVisual();
        e.UseDefaultCursors = true;
    }

    void CardDragHandleUp(object sender, MouseButtonEventArgs e)
    {
        var element = sender as FrameworkElement;
        if (element is not null && element.IsMouseCaptured) element.ReleaseMouseCapture();
        if (_dragAdorner is null) _dragEditor = null;
    }

    void CardListDragOver(object sender, DragEventArgs e)
    {
        if (_dragEditor is null) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        int insert = ComputeInsertIndex(e.GetPosition(CardList));
        if (insert == _dragInsertIndex) return;

        _dragInsertIndex = insert;
        UpdateCardShifts(insert);
    }

    void CardListDragLeave(object sender, DragEventArgs e)
    {
        if (_dragEditor is null) return;

        // 子控件之间移动也会冒泡 DragLeave；只有真正离开列表范围才收回让位动画。
        Point point = e.GetPosition(CardList);
        if (point.X < 0 || point.Y < 0 || point.X > CardList.ActualWidth || point.Y > CardList.ActualHeight)
        {
            ResetCardShifts(true);
            // 复位后必须让下一次 DragOver 重新计算；否则同一下标会跳过 UpdateCardShifts。
            _dragInsertIndex = -1;
        }
    }

    void CardListDrop(object sender, DragEventArgs e)
    {
        CardEditor drag = _dragEditor;
        if (drag is null) return;

        e.Handled = true;
        int insert = _dragInsertIndex;

        int from = _working.Cards.IndexOf(drag.Config);
        if (from < 0)
        {
            EndDragVisual();
            return;
        }

        int to = insert;
        if (to < 0)
        {
            CardEditor target = FindCardEditor(e.OriginalSource as DependencyObject);
            to = target is null ? _working.Cards.Count : _working.Cards.IndexOf(target.Config);
        }

        // 放到自己身上 / 原位置后一位都不需要移动。
        if (to == from || to == from + 1)
        {
            EndDragVisual();
            return;
        }

        _working.Cards.RemoveAt(from);
        if (to > from) to--;
        if (to < 0) to = 0;
        if (to > _working.Cards.Count) to = _working.Cards.Count;
        _working.Cards.Insert(to, drag.Config);

        // 先按新顺序重建，再撤跟随层 / 还原源卡片：避免旧容器先展开再跳位。
        RebuildCardList();
        EndDragVisual();
        MarkDirty();
        StatusText.Text = $"卡片「{drag.DisplayName}」已移动到第 {(to + 1)} 位";
    }

    /// <summary>按指针在未平移布局中的位置算插入下标；被拖卡自己跳过。</summary>
    int ComputeInsertIndex(Point point)
    {
        int from = _dragEditor is null ? -1 : _working.Cards.IndexOf(_dragEditor.Config);
        if (from < 0) return -1;

        int last = -1;
        foreach (CardEditor editor in DisplayedEditors())
        {
            int dataIndex = _working.Cards.IndexOf(editor.Config);
            if (dataIndex < 0) continue;
            last = Math.Max(last, dataIndex);

            if (ReferenceEquals(editor, _dragEditor)) continue;

            var container = CardList.ItemContainerGenerator.ContainerFromItem(editor) as FrameworkElement;
            if (container is null) continue;

            double shift = _dragShifts.TryGetValue(editor.Config.Id, out var transform) ? transform.Y : 0;
            Point middle = container.TranslatePoint(new Point(0, container.ActualHeight / 2), CardList);
            if (point.Y < middle.Y - shift) return dataIndex;
        }

        // 过滤状态下列表最后一张不一定是数据末尾；插到它后面即可，不能直接落到整表末尾。
        return last >= 0 ? Math.Min(_working.Cards.Count, last + 1) : from;
    }

    /// <summary>目标下标变化时，把夹在源 / 目标之间的可见卡片平移一个"折叠卡 + 间距"的槽位。</summary>
    void UpdateCardShifts(int insert)
    {
        int from = _dragEditor is null ? -1 : _working.Cards.IndexOf(_dragEditor.Config);
        if (from < 0) return;

        foreach (CardEditor editor in DisplayedEditors())
        {
            if (ReferenceEquals(editor, _dragEditor)) continue;

            int dataIndex = _working.Cards.IndexOf(editor.Config);
            if (dataIndex < 0) continue;

            double target = 0;
            if (insert > from && dataIndex > from && dataIndex < insert) target = -_dragStride;
            else if (insert < from && dataIndex >= insert && dataIndex < from) target = _dragStride;
            AnimateCardShift(editor, target);
        }
    }

    void AnimateCardShift(CardEditor editor, double target)
    {
        var container = CardList.ItemContainerGenerator.ContainerFromItem(editor) as FrameworkElement;
        if (container is null) return;

        string key = editor.Config.Id;
        if (!_dragShifts.TryGetValue(key, out var transform))
        {
            transform = new TranslateTransform();
            container.RenderTransform = transform;
            _dragShifts[key] = transform;
        }

        double current = transform.Y;
        if (CardMotion.Enabled && CardMotion.LayoutDurationMs > 0)
        {
            var animation = new DoubleAnimation(current, target,
                TimeSpan.FromMilliseconds(CardMotion.LayoutDurationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.YProperty, animation);
        }
        else
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = target;
        }
    }

    List<CardEditor> DisplayedEditors() =>
        CardList.ItemsSource is IEnumerable<CardEditor> source ? source.ToList() : new List<CardEditor>(_editors);

    void ResetCardShifts(bool animate)
    {
        foreach (KeyValuePair<string, TranslateTransform> pair in _dragShifts)
        {
            TranslateTransform transform = pair.Value;
            if (animate && CardMotion.Enabled && CardMotion.LayoutDurationMs > 0)
            {
                var animation = new DoubleAnimation(transform.Y, 0,
                    TimeSpan.FromMilliseconds(CardMotion.LayoutDurationMs))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                transform.BeginAnimation(TranslateTransform.YProperty, animation);
            }
            else
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = 0;
            }
        }

        if (!animate) _dragShifts.Clear();
    }

    /// <summary>结束 / 取消拖动时统一收尾：撤跟随层、还原源卡片、复位让位平移。</summary>
    void EndDragVisual()
    {
        CardList.GiveFeedback -= OnCardDragGiveFeedback;

        if (_dragAdornerLayer is not null && _dragAdorner is not null)
            _dragAdornerLayer.Remove(_dragAdorner);
        _dragAdorner = null;
        _dragAdornerLayer = null;

        ResetCardShifts(false);

        if (_dragSourceContainer is not null)
        {
            _dragSourceContainer.BeginAnimation(UIElement.OpacityProperty, null);
            _dragSourceContainer.Opacity = 1;
            _dragSourceContainer.BeginAnimation(FrameworkElement.HeightProperty, null);
            _dragSourceContainer.ClearValue(FrameworkElement.HeightProperty);
            _dragSourceContainer.ClipToBounds = false;
            _dragSourceContainer = null;
        }

        _dragShifts.Clear();
        _dragStride = 0;
        _dragInsertIndex = -1;
        _dragEditor = null;
    }

    static FrameworkElement FindCardHeader(FrameworkElement container)
    {
        if (container is null) return null;

        int count = VisualTreeHelper.GetChildrenCount(container);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(container, i) as FrameworkElement;
            if (child is null) continue;
            if (string.Equals(child.Name, "CardHeader", StringComparison.Ordinal)) return child;
            if (string.Equals(child.Tag as string, "CardHeader", StringComparison.Ordinal)) return child;
            FrameworkElement nested = FindCardHeader(child);
            if (nested is not null) return nested;
        }
        return null;
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

    void CardHeaderClick(object sender, MouseButtonEventArgs e)
    {
        CardEditor editor = (sender as FrameworkElement)?.DataContext as CardEditor;
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
