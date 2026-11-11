using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 中转站的内容控件：标题、搜索、列表、拖入高亮、状态行，以及拖放 / 选择 / 右键等 UI 协调。
/// 持久化在 <see cref="StationStore"/>，复制 / 移动在 <see cref="StationCopyController"/>，
/// 文件操作在 <see cref="StationFileActions"/>（审阅第三批第 11 项）。
/// </summary>
internal sealed class StationView : Grid, IDisposable, IStationCopySink
{
    static bool ShouldStartDrag(Point origin, Point current) => Math.Abs(current.X - origin.X) >= 4 || Math.Abs(current.Y - origin.Y) >= 4;

    Settings _settings;
    readonly CardSettings _config;
    readonly StationSettings _station;
    readonly ICardHost _host;
    readonly StationStore _store;
    readonly StationCopyController _copy;
    readonly StationFileActions _files;
    /// <summary>
    /// 内衬容器：富内容层（<c>OverlaySurface._contentLayer</c>）Margin=0，内容会直接贴住
    /// 卡片圆角。所有内容都放进这层，由 <see cref="ApplySpacing"/> 统一给留白。
    /// 只在构造期建一次，换肤时只改它的 Margin —— 不能把边距挂在每次 Rebuild 的列表行上，
    /// 否则换肤 / 搜索会一层层累积。
    /// </summary>
    readonly Grid _content;
    readonly CardHeader _header;
    readonly CardButton _openDirectory;
    readonly CardButton _clear;
    readonly CardButton _cancelCopy;
    readonly CardInput _search;
    readonly CardList _list;
    readonly CardStatus _status;
    readonly CardFooter _footer;
    readonly CardDropOverlay _dropOverlay;
    readonly StationSelection _selection;
    readonly List<StationItem> _items = new();

    /// <summary>宿主是否已经销毁本视图；销毁后所有后台回调直接丢弃，不再碰控件 / 写盘。</summary>
    volatile bool _disposed;

    Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
    int _autoRemoved;
    string _filter = "";
    bool _syncingFilter;
    StationItem _dragItem;
    Point _dragOrigin;

    public StationView(Settings settings, CardSettings config, ICardHost host)
    {
        _settings = settings ?? new Settings();
        _config = config ?? new CardSettings { Id = "dropstation", Kind = "dropstation" };
        _station = DropStationCard.StationOf(_config).Clone();
        _host = host;

        _store = new StationStore(_host, _station.MaxEntries);
        _copy = new StationCopyController(_store, _station, Dispatcher, this);
        _files = new StationFileActions(_host, _station, SetStatus, PersistAndRebuild);

        _content = new Grid();
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_content);

        _header = new CardHeader("文件中转站");
        _openDirectory = _header.Actions.Add("打开目录", _files.OpenDirectory, CardButtonTone.Normal, "在资源管理器中打开接收目录");
        _header.Actions.Add("粘贴", PasteFromClipboard, CardButtonTone.Normal, "从剪贴板添加文件");
        _clear = _header.Actions.Add("清空", ClearAll, CardButtonTone.Normal, "清空列表（不删除文件）");
        _content.Children.Add(_header);

        _search = new CardInput
        {
            Placeholder = "搜索文件名…",
            Visibility = Visibility.Collapsed
        };
        _search.TextChanged += OnSearchChanged;
        _search.PreviewKeyDown += (s, e) =>
        {
            if (e.Key != Key.Escape || _filter.Length == 0) return;
            e.Handled = true;
            ClearSearch();
        };
        SetRow(_search, 1);
        _content.Children.Add(_search);

        _list = new CardList
        {
            MinHeight = 72,
            SelectionMode = SelectionMode.Extended
        };
        _list.SetEmptyText("把文件拖进来");
        _selection = new StationSelection(_list, _items, Matches);
        _list.ItemActivated += row => _files.Open(_selection.ItemOf(row));
        _list.Inner.ContextMenu = BuildMenu();
        _list.Inner.PreviewMouseLeftButtonDown += OnRowMouseDown;
        _list.Inner.PreviewMouseMove += OnRowMouseMove;
        _list.Inner.PreviewMouseLeftButtonUp += (s, e) => _dragItem = null;
        _list.Inner.PreviewMouseRightButtonDown += OnRowRightButtonDown;

        _dropOverlay = new CardDropOverlay();

        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_dropOverlay);
        SetRow(body, 2);
        _content.Children.Add(body);

        _status = new CardStatus();

        var statusRow = new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusRow.Children.Add(_status);

        _cancelCopy = new CardButton("取消", CardButtonTone.Normal)
        {
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };
        _cancelCopy.ToolTip = "取消排队和进行中的收纳";
        _cancelCopy.Click += (s, e) => CancelCopies();
        Grid.SetColumn(_cancelCopy, 1);
        statusRow.Children.Add(_cancelCopy);

        SetRow(statusRow, 3);
        _content.Children.Add(statusRow);

        _footer = new CardFooter { Margin = new Thickness(0, 8, 0, 0) };
        SetRow(_footer, 4);
        _content.Children.Add(_footer);

        ApplyStyle(_settings);
        LoadItems();
        Rebuild();
        // 列表文件只读：拒绝加载 / 写盘是保护动作，但必须让用户看见，不能只留日志。
        if (_store.IsReadOnly)
            SetStatus($"列表文件只读（{_store.ReadOnlyReason}）；本次改动不会写入磁盘", CardStatusKind.Warning);
        else if (_autoRemoved > 0) SetStatus($"已自动移除 {_autoRemoved} 个丢失项", CardStatusKind.Success);
        else SetStatus(_items.Count == 0 ? "把文件拖进来，或从资源管理器拖入。" : "就绪", CardStatusKind.Normal);
    }

    /// <summary>
    /// 宿主销毁视图时调用（应用设置换内容 / 删卡 / 退出；<c>OverlayController.ReleaseContent</c>
    /// 已经会对 <see cref="IDisposable"/> 调 Dispose）。P1-6：
    /// · 复制任务比控件活得久：先取消排队和进行中的任务；
    /// · 停掉 <see cref="CardStatus"/> 内部的 DispatcherTimer，否则旧视图一直被计时器强引用；
    /// · 置 <see cref="_disposed"/>，让已经排进 Dispatcher 的 UI 回调和整表写盘作废；
    ///   但「复制完成后目标已落盘」的单条结果仍会合并进 items.json
    ///   （见 <see cref="StationStore.MergeCompletedAfterDispose"/>），避免目标文件成为孤儿。
    ///
    /// 收尾直接调用 <see cref="CardJobQueue.Dispose"/>：队列内部会先取消全部任务，
    /// 再等最后一个在飞任务退出后才释放信号量（延迟 DisposeGate，P2-10 竞态已修），
    /// 所以这里不再需要「只用 CancelAll、不 Dispose」的绕行。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _copy.Dispose(); }
        catch (Exception ex) { Log.Warn($"中转站队列收尾失败: {ex.Message}"); }

        try
        {
            _status.SetBusy(false);   // CardStatus 在 Busy 时转 DispatcherTimer，这一步会 Stop
            _status.SetProgress(null);
        }
        catch
        {
            // 良性竞态：控件可能已随窗口卸载，这里只是尽力停掉计时器与进度条。
        }
    }

    public void ApplyStyle(Settings settings)
    {
        _settings = settings ?? _settings;
        _foreground = CardPalette.TryColor(_settings.Style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

        CliSettings cli = _settings.Cli ?? new CliSettings();
        Width = Math.Max(240, cli.MaxWidth);

        _header.Code = CardMeta.Header(_settings, _config);
        _header.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _footer.Left = CardMeta.Footer(_settings);
        _footer.ApplyStyle(_settings.Style);
        _search.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _list.ApplyStyle(_settings.Style, cli);
        _status.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _cancelCopy.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _dropOverlay.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _list.SetEmptyText(EmptyText());
        ApplySpacing(cli);
        Rebuild();
    }

    /// <summary>
    /// 按主题 Padding 重算整块内容的留白与纵向节奏。富内容层没有外边距，这里就是卡片里
    /// 唯一的「内衬」：默认 Padding=14 时左右 14、上 / 下 13，标题下 11、搜索下 8、
    /// 状态行上 9、状态文字与「取消」按钮之间 12。
    /// 全部夹在上下限内：Padding=0 时仍有 14/12/12 的呼吸，Padding=80 时也不会把列表挤没。
    /// 只改 Margin，不增删控件 —— ApplyStyle 会重建列表行，边距绝不能放进 Rebuild。
    /// </summary>
    void ApplySpacing(CliSettings cli)
    {
        StyleSettings style = _settings.Style ?? new StyleSettings();
        double pad = Math.Max(0, style.Padding);

        double padX = RoundSpacing(pad, 1.0, 14, 16);
        double padTop = RoundSpacing(pad, 0.9, 12, 14);
        double padBottom = RoundSpacing(pad, 0.9, 12, 14);
        double headerGap = RoundSpacing(pad, 0.8, 10, 12);
        double searchGap = RoundSpacing(pad, 0.57, 7, 9);
        double statusGap = RoundSpacing(pad, 0.65, 8, 10);
        double cancelGap = RoundSpacing(pad, 0.85, 12, 14);

        _content.Margin = new Thickness(padX, padTop, padX, padBottom);

        _header.Margin = new Thickness(0, 0, 0, headerGap);
        _search.Margin = new Thickness(0, 0, 0, searchGap);
        _status.Margin = new Thickness(0, statusGap, 0, 0);
        // 状态文字和「取消」按钮不能贴在一起；顶边与状态文字对齐。
        _cancelCopy.Margin = new Thickness(cancelGap, statusGap, 0, 0);

        // 上下内衬也算卡片 chrome：从列表高度上限里扣掉，整卡才仍然守 Cli.MaxHeight 的约定。
        double chrome = padTop + padBottom;
        _list.MaxHeight = Math.Max(120, cli.MaxHeight - 104 - chrome);
    }

    /// <summary>按主题 Padding 缩放一档间距，夹到 [min, max] 后取整，避免 0.4 DIP 这类碎值。</summary>
    static double RoundSpacing(double pad, double scale, double min, double max) => Math.Round(Settings.Clamp(pad * scale, min, max));

    public void ShowDropHint(bool over, int count)
    {
        if (!over || count <= 0)
        {
            _dropOverlay.Hide();
            return;
        }
        _dropOverlay.Show(count, true);
    }

    public void HandleDrop(string[] paths)
    {
        _dropOverlay.Hide();
        AddPaths(paths);
    }

    // ==================== IStationCopySink：复制控制器的最小回填 ====================

    public bool IsDisposed => _disposed;

    public void CopyStatus(string text, CardStatusKind kind) => SetStatus(text, kind);

    public void CopyItemRemoved(StationItem item) => _items.Remove(item);

    public void CopyPersistAndRebuild() => PersistAndRebuild();

    public void CopyStateChanged() => RefreshCopyStatus();

    // ==================== 添加 ====================

    /// <summary>从剪贴板取文件列表；没有文件时给出提示，不弹对话框。</summary>
    void PasteFromClipboard()
    {
        if (_host is null || !_host.Shell.TryGetClipboardFiles(out var paths) || paths is null || paths.Length == 0)
        {
            SetStatus("剪贴板里没有文件。", CardStatusKind.Warning);
            return;
        }

        int added = AddPaths(paths);
        if (added == 0) SetStatus("剪贴板里没有可添加的文件。", CardStatusKind.Normal);
    }

    /// <summary>拖入 / 粘贴共用：归一化路径、去重、按模式加进列表或排队收纳。返回新增条数。</summary>
    int AddPaths(string[] paths)
    {
        if (paths is null || paths.Length == 0) return 0;

        if (_station.Mode != "Reference" && string.IsNullOrWhiteSpace(_station.Directory))
        {
            SetStatus("先到设置里选接收目录。", CardStatusKind.Error);
            return 0;
        }

        int added = 0;
        foreach (string raw in paths)
        {
            string source = StationFiles.FullPath(raw);
            if (source is null || StationFiles.ContainsPath(_items, source)) continue;

            bool isDirectory = Directory.Exists(source);
            if (!isDirectory && !File.Exists(source))
            {
                SetStatus($"文件不存在：{Path.GetFileName(source)}", CardStatusKind.Error);
                continue;
            }

            // 盘符根 / UNC 共享根：目标组合会丢掉接收目录，Move 还会递归删源根。
            if (isDirectory && StationFiles.IsRootPath(source))
            {
                SetStatus($"不能收纳盘符根或共享根：{source}", CardStatusKind.Error);
                continue;
            }

            if (isDirectory && FolderWouldContainReceiveDirectory(source))
            {
                SetStatus("接收目录不能放在要收纳的文件夹里。", CardStatusKind.Error);
                continue;
            }

            string name = StationFiles.ItemName(source, isDirectory);
            if (_station.Mode == "Reference")
            {
                _items.Add(new StationItem
                {
                    Id = StationFiles.NewId(),
                    Path = source,
                    Name = name,
                    Size = isDirectory ? 0 : StationFiles.SafeSize(source),
                    AddedUtc = StationFiles.UtcNow(),
                    IsDirectory = isDirectory
                });
                added++;
            }
            else
            {
                var item = new StationItem
                {
                    Id = StationFiles.NewId(),
                    Path = source,
                    Name = name,
                    AddedUtc = StationFiles.UtcNow(),
                    IsDirectory = isDirectory
                };
                _items.Add(item);
                _copy.Enqueue(item);
                added++;
            }
        }

        if (added == 0) return 0;

        _store.Trim(_items);
        Persist();
        Rebuild();
        if (_station.Mode == "Reference") SetStatus($"{added} 项已加入", CardStatusKind.Success);
        else RefreshCopyStatus();
        return added;
    }

    /// <summary>复制 / 移动文件夹时不能把接收目录放进源文件夹内部，否则递归会自我嵌套。</summary>
    bool FolderWouldContainReceiveDirectory(string source)
    {
        if (_station.Mode == "Reference" || string.IsNullOrWhiteSpace(_station.Directory)) return false;
        return StationFiles.FolderWouldContainReceiveDirectory(source, _station.Directory);
    }

    // ==================== 列表 ====================

    void Rebuild()
    {
        if (_items.Count == 0 && _filter.Length > 0)
        {
            _filter = "";
            if (!string.IsNullOrEmpty(_search.Text))
            {
                _syncingFilter = true;
                _search.Text = "";
                _syncingFilter = false;
            }
        }

        List<StationItem> selectedItems = _selection.SelectedItems();
        _list.Items.Clear();

        foreach (StationItem item in _items)
        {
            if (!Matches(item)) continue;

            FrameworkElement row = StationRow.Build(item, _station, _host, _foreground);
            _list.Items.Add(row);
            if (selectedItems.Contains(item)) _list.Inner.SelectedItems.Add(row);
        }

        _list.SetEmptyText(EmptyText());
        _list.RefreshEmpty();
        UpdateHeader();
    }

    bool Matches(StationItem item)
    {
        if (_filter.Length == 0) return true;

        string name = string.IsNullOrEmpty(item.Name) ? item.DisplayPath : item.Name;
        if (name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return item.DisplayPath.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void OnSearchChanged(string text)
    {
        if (_syncingFilter) return;

        _filter = (text ?? "").Trim();
        _selection.Anchor = null;
        Rebuild();
    }

    void ClearSearch()
    {
        _filter = "";
        _selection.Anchor = null;
        if (!string.IsNullOrEmpty(_search.Text))
        {
            _syncingFilter = true;
            _search.Text = "";
            _syncingFilter = false;
        }
        Rebuild();
    }

    // ==================== 鼠标 / 选择 ====================

    /// <summary>
    /// 行内左键按下：手动选中并标记 Handled。整卡拖动在窗口的冒泡处理里，
    /// Preview 阶段吃掉左键后，从行上开始拖动就不会把整张卡片拖走。
    /// </summary>
    void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        Point point = e.GetPosition(_list.Inner);
        StationItem item = _selection.ItemAt(point, out var row);
        if (item is null) { _dragItem = null; return; }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _selection.ApplyRowClick(item, row, ctrl, shift);

        _list.Inner.Focus();
        _dragOrigin = point;
        _dragItem = _selection.IsRowSelected(row) && item.PathExists()
            ? item : null;
        e.Handled = true;

        // 行内按下已被吃掉，CardList 的 MouseDoubleClick 不会再触发；
        // 这里按 ClickCount 自己处理双击打开，避免和拖出阈值冲突。
        if (e.ClickCount > 1)
        {
            _dragItem = null;
            _files.Open(item);
        }
    }

    /// <summary>行内移动超过阈值才拖出；只允许 Copy，拖完不移除列表项。</summary>
    void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point point = e.GetPosition(_list.Inner);
        if (!ShouldStartDrag(_dragOrigin, point)) return;

        StationItem item = _dragItem;
        _dragItem = null;

        List<StationItem> selected = _selection.SelectedItems();
        if (!selected.Contains(item)) selected = new List<StationItem> { item };

        var paths = new List<string>();
        foreach (StationItem candidate in selected)
        {
            string candidatePath = candidate.DisplayPath;
            if (candidate.PathExists()) paths.Add(candidatePath);
        }

        if (paths.Count == 0)
        {
            SetStatus($"文件已不存在：{item.DisplayPath}", CardStatusKind.Error);
            return;
        }

        e.Handled = true;
        DragDropEffects effect = _host is null ? DragDropEffects.None : _host.Shell.BeginDragFiles(_list, paths, DragDropEffects.Copy);
        if (effect == DragDropEffects.None)
            SetStatus("拖出已取消", CardStatusKind.Normal);
        else if (paths.Count == 1)
            SetStatus($"已拖出：{(string.IsNullOrEmpty(item.Name) ? Path.GetFileName(paths[0]) : item.Name)}",
                      CardStatusKind.Success);
        else
            SetStatus($"已拖出 {paths.Count} 个文件", CardStatusKind.Success);
    }

    void OnRowRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        StationItem item = _selection.ItemAt(e.GetPosition(_list.Inner), out var row);
        if (item is null) return;

        _selection.KeepSelection(item, row);
    }

    // ==================== 右键菜单 ====================

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "打开" };
        open.Click += (s, e) => _files.Open(_selection.Selected());
        menu.Items.Add(open);

        var reveal = new MenuItem { Header = "在资源管理器中显示" };
        reveal.Click += (s, e) => _files.Reveal(_selection.Selected());
        menu.Items.Add(reveal);

        var copy = new MenuItem { Header = "复制路径" };
        copy.Click += (s, e) => _files.CopyPaths(_selection.SelectedItems());
        menu.Items.Add(copy);

        menu.Items.Add(new Separator());

        var remove = new MenuItem { Header = "从列表移除" };
        remove.Click += (s, e) => RemoveSelected();
        menu.Items.Add(remove);

        var recycle = new MenuItem { Header = "移到回收站…" };
        recycle.Click += (s, e) => RecycleSelectedFiles();
        menu.Items.Add(recycle);

        menu.Items.Add(new Separator());

        var missing = new MenuItem { Header = "移除丢失项" };
        missing.Click += (s, e) => RemoveMissing();
        menu.Items.Add(missing);

        menu.Opened += (s, e) =>
        {
            List<StationItem> selected = _selection.SelectedItems();
            StationItem item = selected.Count == 0 ? null : selected[0];

            recycle.IsEnabled = _files.HasDeletable(selected);
            open.IsEnabled = item is not null;
            reveal.IsEnabled = item is not null;
            copy.IsEnabled = item is not null;
            remove.IsEnabled = item is not null;

            remove.Header = selected.Count > 1 ? $"从列表移除 {selected.Count} 项" : "从列表移除";
            recycle.Header = selected.Count > 1 ? "把选中项移到回收站…" : "移到回收站…";

            int missingCount = _files.CountMissing(_items);
            missing.Header = missingCount == 0 ? "移除丢失项" : $"移除 {missingCount} 个丢失项";
            missing.IsEnabled = missingCount > 0;
        };

        return menu;
    }

    // ==================== 操作 ====================

    internal void RemoveMissing()
    {
        if (_files.RemoveMissing(_items) > 0) _selection.Anchor = null;
    }

    void RecycleSelectedFiles()
    {
        if (_files.DeleteFiles(Window.GetWindow(this), _selection.SelectedItems(), _items) > 0)
            _selection.Anchor = null;
    }

    void RemoveSelected()
    {
        List<StationItem> selected = _selection.SelectedItems();
        if (selected.Count == 0) return;

        foreach (StationItem item in selected) _items.Remove(item);
        _selection.Anchor = null;
        Persist();
        Rebuild();
        SetStatus(selected.Count > 1 ? $"已从列表移除 {selected.Count} 项" : "已从列表移除",
                  CardStatusKind.Normal);
    }

    void ClearAll()
    {
        if (_items.Count == 0) return;

        _items.Clear();
        _selection.Anchor = null;
        Persist();
        Rebuild();
        SetStatus("列表已清空（文件未删除）", CardStatusKind.Normal);
    }

    // ==================== 复制状态行 ====================

    internal void CancelCopies() => _copy.Cancel();

    /// <summary>复制期间的状态行：当前文件 + 百分比 + 速度；取消中显示进度；结束后清掉进度条。</summary>
    void RefreshCopyStatus()
    {
        bool busy = _copy.IsBusy;
        _cancelCopy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _cancelCopy.IsEnabled = !_copy.CancelRequested;
        if (!busy)
        {
            _status.SetBusy(false);
            _status.SetProgress(null);
            return;
        }

        if (_copy.CancelRequested)
        {
            _status.SetText($"正在取消收纳 {_copy.Done}/{_copy.Total}…", CardStatusKind.Busy);
            _status.SetProgress(_copy.Total == 0 ? 0 : (double)_copy.Done / _copy.Total);
            return;
        }

        int current = Math.Min(_copy.Total, _copy.Done + 1);
        string text = $"正在收纳 {current}/{_copy.Total}";
        if (!string.IsNullOrEmpty(_copy.ActiveName))
        {
            text += $" · {_copy.ActiveName}";
            if (_copy.ActivePercent > 0) text += $" {(int)Math.Round(_copy.ActivePercent * 100)}%";
            if (_copy.ActiveSpeed > 0) text += $" · {FormatSpeed(_copy.ActiveSpeed)}";
        }
        else
        {
            text += "…";
        }

        _status.SetText(text, CardStatusKind.Busy);
        _status.SetProgress(string.IsNullOrEmpty(_copy.ActiveName)
            ? (_copy.Total == 0 ? 0 : (double)_copy.Done / _copy.Total)
            : _copy.ActivePercent);
    }

    static string FormatSpeed(double mbPerSecond)
    {
        if (mbPerSecond >= 1) return $"{mbPerSecond:0.0} MB/s";
        return $"{mbPerSecond * 1024:0} KB/s";
    }

    // ==================== 持久化 / 状态 ====================

    void LoadItems()
    {
        List<StationItem> loaded = _store.Load(_station.AutoRemoveMissing, out var autoRemoved);
        _items.AddRange(loaded);
        _autoRemoved = autoRemoved;
    }

    /// <summary>
    /// 已销毁的旧视图绝不能再写整表，否则会用旧快照覆盖新视图的结果；
    /// 销毁后复制完成的那一条走 <see cref="StationStore.MergeCompletedAfterDispose"/>（只合并这一条）。
    /// </summary>
    void Persist()
    {
        if (_disposed || _store.IsReadOnly) return;
        _store.Persist(_items);
    }

    void PersistAndRebuild()
    {
        // 只读时 Persist 是空操作；状态行必须说清楚「内存里改了、磁盘没写」，
        // 否则用户会以为添加 / 移到回收站已经保存。
        if (_store.IsReadOnly)
        {
            Rebuild();
            SetStatus($"列表文件只读（{_store.ReadOnlyReason}）；本次改动不会写入磁盘", CardStatusKind.Warning);
            return;
        }

        Persist();
        Rebuild();
    }

    void UpdateHeader()
    {
        int visible = 0;
        foreach (StationItem item in _items)
            if (Matches(item)) visible++;

        bool hasDirectory = false;
        foreach (StationItem item in _items)
            if (item.IsDirectory) { hasDirectory = true; break; }
        string unit = hasDirectory ? " 项" : " 个文件";

        _header.Badge = _filter.Length == 0
            ? _items.Count + unit
            : $"{visible}/{_items.Count}{unit}";

        _search.Visibility = (_items.Count > 0 || _filter.Length > 0)
            ? Visibility.Visible : Visibility.Collapsed;

        bool canOpen = _station.Mode != "Reference" && !string.IsNullOrWhiteSpace(_station.Directory);
        _openDirectory.Visibility = canOpen ? Visibility.Visible : Visibility.Collapsed;
        _openDirectory.IsEnabled = canOpen;
        _clear.IsEnabled = _items.Count > 0;
    }

    string EmptyText()
    {
        if (_items.Count > 0 && _filter.Length > 0) return "没有匹配的文件。";

        if (_station.Mode != "Reference" && !string.IsNullOrWhiteSpace(_station.Directory))
            return _station.Mode == "Move"
                ? $"把文件 / 文件夹拖进来，移动到 {_station.Directory}"
                : $"把文件 / 文件夹拖进来，收纳到 {_station.Directory}";

        return "把文件 / 文件夹拖进来（只记录路径，不移动文件）";
    }

    void SetStatus(string text, CardStatusKind kind)
    {
        _status.SetText(text, kind);
        if (kind != CardStatusKind.Busy) _status.SetBusy(false);
    }
}
