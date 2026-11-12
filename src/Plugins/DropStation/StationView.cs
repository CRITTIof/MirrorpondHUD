using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>存在性后台检查的并发上限：数量小到不会打满线程池 / 网络盘，又明显快于逐条串行。</summary>
    const int ProbeWorkers = 4;

    /// <summary>图标提取的并发上限：目录图标会碰磁盘，500 条不能一次性全砸到网络盘上。</summary>
    const int IconWorkers = 4;

    /// <summary>
    /// P-11：存在性检查的后台调度。每次启动一代，结果回来先校验视图未销毁、再校验
    /// 「该条目最近一次探针」的代次，旧代次直接丢弃，避免快速切换视图时旧结果改写新状态。
    /// </summary>
    readonly object _probeSync = new();
    readonly List<CancellationTokenSource> _probeCancels = new();
    readonly Dictionary<StationItem, int> _probeLatest = new();
    readonly LogThrottle _probeThrottle = new("中转站存在性检查", 60000);
    int _probeGeneration;

    /// <summary>
    /// P-17：按条目引用复用行容器。搜索只改 Visible 列表 / 增删条目，不重建行，
    /// 否则每敲一个字都要重建 500 个控件、重取 500 次图标。
    /// </summary>
    readonly Dictionary<StationItem, StationRowHandle> _rows = new();

    /// <summary>图标缓存（含失败结果）：同一扩展名 / 目录路径只取一次；null 表示取不到，用占位。</summary>
    readonly Dictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _iconFailed = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>同一图标键的等待者：多个同扩展名行的请求合并成一次后台提取。</summary>
    readonly Dictionary<string, List<StationRowHandle>> _iconWaiters = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim _iconGate = new(IconWorkers);
    readonly LogThrottle _iconThrottle = new("中转站图标加载", 60000);

    /// <summary>行形状签名（显示方式 + 是否显示图标）：变化时旧容器不能复用，必须重建。</summary>
    string _rowSignature = "";

    /// <summary>本轮回填是否新建过行：只有新建行才需要整树重套描边 / 行高，搜索命中不重套。</summary>
    bool _rowsCreated;


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

        // P-11：条目已经先显示出来，存在性在后台有界并发回填；构造期不再做任何同步文件系统检查。
        StartProbe(_items, reportStatus: true);
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

        // P-11：存在性检查可能正卡在断开的网络盘上；取消后后台任务在下一次检查点退出，
        // 迟到的结果会因为 _disposed / 代次校验被丢弃（阻塞中的单次 IO 无法中断，需真机观察）。
        List<CancellationTokenSource> cancels;
        lock (_probeSync)
        {
            cancels = new List<CancellationTokenSource>(_probeCancels);
            _probeCancels.Clear();
        }
        foreach (CancellationTokenSource cts in cancels)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException)
            {
                // 良性竞态：后台任务刚收尾并释放了自己的 CTS，取消已无意义。
            }
            cts.Dispose();
        }

        _rows.Clear();
        _iconCache.Clear();
        _iconFailed.Clear();
        _iconWaiters.Clear();

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

        // 显示方式存在 Extras 里；设置窗改动后宿主可能只调 ApplyStyle 不重建控件。
        StationSettings latest = DropStationCard.StationOf(_config);
        _station.View = latest.View;

        _header.Code = CardMeta.Header(_settings, _config);
        _header.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _footer.Left = CardMeta.Footer(_settings, _config);
        _footer.ApplyStyle(_settings.Style);
        _search.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _list.SetViewMode(StationDisplayMode.IsTiles(_station.View));
        _list.ApplyStyle(_settings.Style);
        _status.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _cancelCopy.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _dropOverlay.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
        _list.SetEmptyText(EmptyText());
        ApplySpacing(cli);
        // 换肤要把描边 / 行高重新套到整棵视觉树上；普通重建只在新建行时套一次。
        Rebuild(restyle: true);
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

    public bool HasItem(StationItem item) => _items.Contains(item);

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
        var fresh = new List<StationItem>();
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
                var reference = new StationItem
                {
                    Id = StationFiles.NewId(),
                    Path = source,
                    Name = name,
                    Size = isDirectory ? 0 : StationFiles.SafeSize(source),
                    AddedUtc = StationFiles.UtcNow(),
                    IsDirectory = isDirectory
                };
                _items.Add(reference);
                fresh.Add(reference);
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
                fresh.Add(item);
                _copy.Enqueue(item);
                added++;
            }
        }

        if (added == 0) return 0;

        _store.Trim(_items);
        Persist();
        Rebuild();
        // 只有本次新加的条目需要探存在性；全量重探会在网络盘上做无谓的重复 IO。
        StartProbe(fresh, reportStatus: false);
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

    /// <summary>
    /// P-17：重建列表改为**增量同步**。旧实现每次 <c>Items.Clear()</c> 后逐条新建行，
    /// 并且每行同步取一次图标；搜索每敲一个字就是 500 次控件构造 + 500 次图标提取，
    /// UI 线程直接被打满。现在：
    /// · 行按条目引用缓存在 <see cref="_rows"/>，只有新增条目才建容器；
    /// · 搜索 / 删除只调整 <see cref="CardList.Items"/> 的成员与顺序（差分替换中间段）；
    /// · 图标由 <see cref="RequestIcon"/> 在后台线程取回后填入，失败保留占位。
    /// </summary>
    void Rebuild(bool restyle = false)
    {
        // 显示方式 / 是否显示图标会改变行形状（列表行 vs 图块），旧容器不能复用。
        // 这里只丢行对象、不清 Items：让 SyncRows 的差分把旧行换成新行，
        // 它会在移除前记下选中项，切换显示方式后选中态不会丢（旧实现是保存后手动恢复）。
        string signature = StationDisplayMode.Normalize(_station.View) + "|" + (_station.ShowIcons ? "1" : "0");
        if (!string.Equals(signature, _rowSignature, StringComparison.Ordinal))
        {
            _rowSignature = signature;
            _rows.Clear();
        }

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

        SyncRows();

        _list.SetEmptyText(EmptyText());
        _list.RefreshEmpty();
        UpdateHeader();

        // 换肤需要整棵树重套描边 / 行高；普通重建只在本轮确实新建了行时套一次，
        // 搜索每敲一个键都遍历整棵视觉树是旧实现里另一处隐性开销。
        if (restyle || _rowsCreated)
        {
            CardTextStyle.ApplyRichText(_settings.Style, _list);
            _rowsCreated = false;
        }
    }

    /// <summary>
    /// 把 <see cref="_items"/> 的可见序列同步到列表：按「有序子序列」差分——
    /// 先移除已不该出现的行，再在对不齐的位置插入缺失行。搜索逐字输入时通常只动几行，
    /// 清空搜索时只把被挡掉的行按原顺序插回，未变化的行容器一次都不重建。
    /// </summary>
    void SyncRows()
    {
        var desired = new List<FrameworkElement>(_items.Count);
        foreach (StationItem item in _items)
        {
            if (!Matches(item)) continue;
            desired.Add(RowFor(item).Root);
        }

        // 条目已从 _items 移除（删除 / 清空 / 自动移除 / 截图样张替换）：丢弃对应行对象。
        // 被搜索条件挡掉的行仍留在 _rows 里，清空搜索时可以直接复用。
        var alive = new HashSet<StationItem>(_items);
        var dead = new List<StationItem>();
        foreach (StationItem cached in _rows.Keys)
            if (!alive.Contains(cached)) dead.Add(cached);
        foreach (StationItem cached in dead)
        {
            _rows.Remove(cached);
            // 探针代次表也按条目引用持有：条目都删了就别留着它，避免字典无限增长。
            _probeLatest.Remove(cached);
        }

        var wanted = new HashSet<FrameworkElement>(desired);

        // 先记下选中项：移除 / 插入都会让 ListBox 丢掉被移动行的选中态，后面按条目引用补回。
        // 必须放在移除之前，否则切换显示方式（整批行换形状）时选中态会丢。
        List<StationItem> selected = _selection.SelectedItems();
        var restored = new List<FrameworkElement>();

        // 命中 / 失配都是引用比较：只有集合真的变了才动 ItemCollection。
        int index = 0;
        while (index < _list.Items.Count)
        {
            if (_list.Items[index] is FrameworkElement row && wanted.Contains(row))
            {
                index++;
                continue;
            }
            _list.Items.RemoveAt(index);
        }

        index = 0;
        foreach (FrameworkElement row in desired)
        {
            if (index < _list.Items.Count && ReferenceEquals(_list.Items[index], row))
            {
                index++;
                continue;
            }
            _list.Items.Insert(index, row);
            restored.Add(row);
            index++;
        }

        if (selected.Count == 0) return;
        foreach (FrameworkElement row in restored)
        {
            if (row.Tag is not StationItem item) continue;
            if (!selected.Contains(item) || _list.Inner.SelectedItems.Contains(row)) continue;
            _list.Inner.SelectedItems.Add(row);
        }
    }

    /// <summary>取（必要时新建）一条目的行容器；存量行只刷新文本与颜色，不动容器与图标槽。</summary>
    StationRowHandle RowFor(StationItem item)
    {
        if (_rows.TryGetValue(item, out StationRowHandle cached))
        {
            cached.Refresh(item, _foreground);
            return cached;
        }

        StationRowHandle handle = StationRow.Create(item, _station, _foreground, _station.View);
        _rows[item] = handle;
        _rowsCreated = true;
        RequestIcon(handle);
        return handle;
    }

    // ==================== 图标（P-17：异步 + 缓存） ====================

    /// <summary>
    /// 为一个行请求图标：命中缓存立即填，同一键的等待合并成一次后台提取，
    /// 失败（Shell 拿不到 / 目录图标不可用）缓存成 null 并保留占位，不反复重试。
    /// </summary>
    void RequestIcon(StationRowHandle handle)
    {
        if (_host is null || !_station.ShowIcons) return;

        string key = IconKeyOf(handle);
        handle.IconKey = key;

        if (_iconCache.TryGetValue(key, out ImageSource cached))
        {
            handle.SetIcon(cached);
            return;
        }

        // 已知取不到（例如目录路径的 Shell 图标在这套 ShellIcons 实现里必然为空）：
        // 直接留在占位，别为每一行再排队一次后台失败。
        if (_iconFailed.Contains(key))
        {
            handle.SetIcon(null);
            return;
        }

        if (_iconWaiters.TryGetValue(key, out List<StationRowHandle> waiters))
        {
            waiters.Add(handle);
            return;
        }

        _iconWaiters[key] = new List<StationRowHandle> { handle };

        string path = handle.IconSourcePath;
        bool extensionOnly = handle.IconExtensionOnly;
        int size = (int)handle.IconSize;

        // 用 Task.Run 而不是在这里直接 await：LoadIconWorker 在信号量立即可用时会同步
        // 跑到 GetFileIcon，那就又回到 UI 线程取图标了。
        _ = Task.Run(() => LoadIconWorkerAsync(key, path, size, extensionOnly));
    }

    string IconKeyOf(StationRowHandle handle)
    {
        StationItem item = handle.Item;
        // 文件图标按扩展名取（ShellIcons 的 SHGFI_USEFILEATTRIBUTES 口径，不碰磁盘）；
        // 目录要真实图标只能按路径取，可能落在网络盘上，所以必须走后台并限并发。
        string scope = item.IsDirectory
            ? "dir|" + item.DisplayPath
            : "file|" + Path.GetExtension(item.Name);
        return scope + "|" + (int)handle.IconSize;
    }

    /// <summary>
    /// 后台线程：有界并发取图标并把结果 Freeze（跨线程交给 UI 需要冻结）。
    /// 用 <see cref="SemaphoreSlim.WaitAsync"/> 而不是 Wait：500 个目录请求时只有 4 个真正
    /// 在跑，其余挂起不占线程池线程，避免「异步化之后又把线程池占满」。
    /// </summary>
    async Task LoadIconWorkerAsync(string key, string path, int size, bool extensionOnly)
    {
        ImageSource image = null;
        await _iconGate.WaitAsync().ConfigureAwait(false);
        try
        {
            image = _host.Shell.GetFileIcon(path, size, extensionOnly);
            if (image is not null && image.CanFreeze && !image.IsFrozen) image.Freeze();
        }
        catch (Exception ex)
        {
            // 图标拿不到不是致命错误，但必须留痕：网络盘 / 损坏关联 / Shell 拒绝都要可诊断。
            _iconThrottle.Failure($"{path}: {ex.Message}");
        }
        finally
        {
            _iconGate.Release();
        }

        // 图标拿不到也算失败路径：目录图标、损坏的文件关联都会返回 null，
        // 没有占位会是一片空白且无迹可查。LogThrottle 保证 500 条时也只记一条。
        if (image is null) _iconThrottle.Failure($"{path}: Shell 未返回图标，改用首字占位");
        else _iconThrottle.Success();

        _ = Dispatcher.BeginInvoke(new Action(() => ApplyIcon(key, image)));
    }

    /// <summary>UI 线程：写缓存并回填所有等待同一键的行（含 null = 失败占位）。</summary>
    void ApplyIcon(string key, ImageSource image)
    {
        if (_disposed) return;

        _iconCache[key] = image;
        if (image is null) _iconFailed.Add(key);

        if (!_iconWaiters.Remove(key, out List<StationRowHandle> waiters)) return;
        foreach (StationRowHandle handle in waiters)
        {
            // 行可能已被复用给别的条目（改名 / 换路径）或已随筛除丢弃，键不一致就不写。
            if (!string.Equals(handle.IconKey, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (!_rows.TryGetValue(handle.Item, out StationRowHandle live) || !ReferenceEquals(live, handle)) continue;
            handle.SetIcon(image);
        }
    }

    // ==================== 存在性后台回归（P-11） ====================

    /// <summary>
    /// 启动一代存在性检查。不取消上一代：不同代次各自持有结果，
    /// 应用时按「该条目最近一次探针」的代次丢弃旧结果，避免新加的条目被旧一代覆盖。
    /// </summary>
    void StartProbe(IReadOnlyList<StationItem> targets, bool reportStatus)
    {
        if (_disposed || targets is null || targets.Count == 0) return;

        int generation = ++_probeGeneration;
        for (int i = 0; i < targets.Count; i++) _probeLatest[targets[i]] = generation;

        var cts = new CancellationTokenSource();
        lock (_probeSync) _probeCancels.Add(cts);

        var snapshot = new List<StationItem>(targets);
        _ = Task.Run(() => ProbeAsync(snapshot, generation, reportStatus, cts));
    }

    /// <summary>
    /// 后台有界并发检查：上限 <see cref="ProbeWorkers"/> 条同时探测。
    /// 单次 <see cref="StationStore.Probe"/> 在断开的网络盘上仍可能阻塞数秒，
    /// 但只有 4 个后台线程被拖住，UI 线程不受影响（真正的阻塞中断需真机验证）。
    /// </summary>
    async Task ProbeAsync(List<StationItem> snapshot, int generation, bool reportStatus, CancellationTokenSource cts)
    {
        var results = new List<(StationItem Item, StationStore.StationPresence Presence, bool IsDirectory)>();
        var sync = new object();

        try
        {
            var queue = new ConcurrentQueue<StationItem>(snapshot);
            int workers = Math.Min(ProbeWorkers, snapshot.Count);
            var tasks = new Task[workers];

            for (int i = 0; i < workers; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    while (queue.TryDequeue(out StationItem item))
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        try
                        {
                            StationStore.StationPresence presence =
                                StationStore.Probe(item.DisplayPath, out bool isDirectory, out string error);

                            // 只有「不可判定」才需要日志节流：缺失是正常结果，不该刷日志。
                            if (presence == StationStore.StationPresence.Unknown)
                                _probeThrottle.Failure($"{item.DisplayPath}: {error}");

                            lock (sync) results.Add((item, presence, isDirectory));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // 属性读取之外还有极少数壳层异常（例如路径规范化）：算「状态未知」，
                            // 记一条节流日志后继续，绝不让单条异常把整轮检查做成未观察的任务异常。
                            _probeThrottle.Failure($"{item.DisplayPath}: {ex.Message}");
                            lock (sync) results.Add((item, StationStore.StationPresence.Unknown, item.IsDirectory));
                        }
                    }
                }, cts.Token);
            }

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // 视图销毁或本轮被取消：属正常收尾，不记日志、不写任何状态。
            return;
        }
        catch (Exception ex)
        {
            // 兜底：调度层（Dispatchers / 线程池）异常也不能静默，否则卡片只剩“没有状态”的假象。
            Log.Warn($"中转站存在性检查异常: {ex.Message}");
        }
        finally
        {
            lock (_probeSync) _probeCancels.Remove(cts);
            cts.Dispose();
        }

        // 后台线程收尾：不 await 这次投递（UI 队列与检查结果无关，视图销毁后由 _disposed 丢弃）。
        _ = Dispatcher.BeginInvoke(new Action(() => ApplyProbeResults(results, generation, reportStatus)));
    }

    /// <summary>
    /// UI 线程应用探针结果：只改「存在」状态与行颜色；自动移除只针对本轮**明确缺失**的条目，
    /// 网络断开的 Unknown 一律不动，避免把暂时不可达的条目从列表里删掉。
    /// </summary>
    void ApplyProbeResults(List<(StationItem Item, StationStore.StationPresence Presence, bool IsDirectory)> results,
                           int generation, bool reportStatus)
    {
        if (_disposed) return;

        int missing = 0;
        int unknown = 0;
        var nowMissing = new List<StationItem>();

        foreach ((StationItem item, StationStore.StationPresence presence, bool isDirectory) in results)
        {
            if (!_probeLatest.TryGetValue(item, out int latest) || latest != generation) continue;
            if (!_items.Contains(item))
            {
                _probeLatest.Remove(item);
                continue;
            }

            if (presence == StationStore.StationPresence.Present)
            {
                item.Missing = false;
                // 顺手纠正 items.json 里过期的目录标记（原本在 Load 里同步做 Directory.Exists）。
                if (!item.IsDirectory && isDirectory) item.IsDirectory = true;
            }
            else if (presence == StationStore.StationPresence.Missing)
            {
                item.Missing = true;
                missing++;
                nowMissing.Add(item);
            }
            else
            {
                unknown++;
            }
        }

        RefreshRowStates();

        if (nowMissing.Count > 0 && _station.AutoRemoveMissing)
        {
            int removed = 0;
            foreach (StationItem item in nowMissing)
            {
                _probeLatest.Remove(item);
                if (_items.Remove(item)) removed++;
            }
            if (removed > 0)
            {
                Rebuild();
                Persist();
                SetStatus($"已自动移除 {removed} 个丢失项", CardStatusKind.Success);
                return;
            }
        }

        if (!reportStatus) return;
        if (unknown > 0)
            SetStatus($"{unknown} 项状态未确认（路径暂时不可用），未按丢失处理", CardStatusKind.Warning);
        else if (missing > 0)
            SetStatus($"{missing} 个条目已丢失（未自动移除）", CardStatusKind.Warning);
    }

    /// <summary>存在性 / 名称变化后就地刷新所有行（不重建容器、不重取图标）。</summary>
    void RefreshRowStates()
    {
        foreach (StationRowHandle handle in _rows.Values) handle.Refresh(handle.Item, _foreground);
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
        HudMenuStyle.Apply(menu);

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

        // 先取消在途复制，避免“列表里已删、磁盘上还在写”（P-3 的竞态）。
        if (_copy.IsBusy) CancelCopies();
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

        // 清空列表必须先停在途复制：否则目标文件会继续落盘，而列表里已经没有记录。
        if (_copy.IsBusy) CancelCopies();
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
    /// 截图专用样张：不读 items.json，也不写盘；只让四种显示方式在同一张图里有内容可看。
    /// 由 <see cref="DropStationCard.PrepareScreenshot"/> 调用，正常运行路径永远不会走到这里。
    /// </summary>
    internal void LoadScreenshotSamples()
    {
        _items.Clear();
        _items.Add(Sample("设计稿.zip", 12.4 * 1024 * 1024, false));
        _items.Add(Sample("截图 2026-11-11.png", 2.1 * 1024 * 1024, false));
        _items.Add(Sample("说明文档.txt", 8 * 1024, false));
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _items.Add(Sample("项目资料", 0, true,
                          string.IsNullOrEmpty(folder) ? Environment.SystemDirectory : folder));
        _items.Add(Sample("演示视频.mp4", 248.0 * 1024 * 1024, false));
        _filter = "";
        _selection.Anchor = null;
        Rebuild();
        SetStatus("就绪", CardStatusKind.Normal);
        // 截图里留一条选中行，用来验收 M3 左缘条；真实卡片不会走这个入口。
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    static StationItem Sample(string name, double size, bool directory, string path = null) => new()
    {
        Id = StationFiles.NewId(),
        Path = path ?? @"C:\Screenshots\" + name,
        Name = name,
        Size = (long)size,
        AddedUtc = StationFiles.UtcNow(),
        IsDirectory = directory
    };

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
