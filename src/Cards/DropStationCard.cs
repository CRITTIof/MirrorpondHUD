using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 文件中转站：把文件从资源管理器拖进卡片，暂存成列表，之后打开 / 定位 / 复制路径 / 移除。
    ///
    /// 三种模式：
    /// - 引用（默认）：只记录路径，不动文件；
    /// - 复制：把文件收进「接收目录」，重名自动改成 <c>名字 (2).扩展名</c>，复制在后台串行执行；
    /// - 移动：先复制到接收目录，成功后再删除源文件（跨盘也走复制 + 删除）。
    ///
    /// 列表持久化在每卡状态目录的 <c>items.json</c>，不塞进 settings.json。
    /// </summary>
    public sealed class DropStationCard : RichCardModuleBase,
        ICardSettingsModule, ICardEditorSection, ICardSettingsValidator,
        ICardSummary, ICardStyleAware, ICardDropTarget, ICardDropFeedback
    {
        public const string SettingsKey = "dropstation";

        public override string Kind { get { return "dropstation"; } }
        public override string DisplayName { get { return "文件中转站"; } }
        public override string Icon { get { return Icons.Folder; } }
        public override bool SupportsContent { get { return false; } }
        public override bool NeedsMouseInput { get { return true; } }
        public override bool DefaultResident { get { return true; } }
        public override int Order { get { return 60; } }

        /// <summary>这张卡片的设置，缺省 / 解析失败都回落到默认值并写回配置。</summary>
        public static StationSettings StationOf(CardSettings config)
        {
            if (config == null) return new StationSettings();

            StationSettings station = config.ExtrasAs<StationSettings>(SettingsKey);
            station.Normalize();
            config.SetExtras(SettingsKey, station);
            return station;
        }

        public void InitializeCard(CardSettings config)
        {
            if (config != null) StationOf(config);
        }

        public override FrameworkElement CreateContent(CardRenderContext context)
        {
            return new StationView(context.Settings, context.Config, context.Host);
        }

        public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged)
        {
            return new StationEditor(config, onChanged);
        }

        public string Validate(CardSettings config)
        {
            StationSettings station = StationOf(config);
            if (station.Mode == "Reference") return null;

            if (string.IsNullOrWhiteSpace(station.Directory)) return "还没选接收目录。";

            try
            {
                if (!Directory.Exists(station.Directory)) return "接收目录不存在：" + station.Directory;
            }
            catch (Exception ex)
            {
                return "接收目录不可用：" + ex.Message;
            }
            return null;
        }

        public string Summary(CardSettings config)
        {
            StationSettings station = StationOf(config);
            if (station.Mode == "Reference") return "引用";

            string directory = (station.Directory ?? "").Trim();
            string prefix = station.Mode == "Move" ? "移动" : "收纳";
            if (directory.Length == 0) return prefix + " · 未选目录";
            try { return prefix + " · " + Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
            catch { return prefix; }
        }

        public void ApplyStyle(FrameworkElement content, CardRenderContext context)
        {
            var view = content as StationView;
            if (view != null && context != null) view.ApplyStyle(context.Settings);
        }

        public bool CanDrop(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;

            var paths = data.GetData(DataFormats.FileDrop) as string[];
            return paths != null && paths.Length > 0;
        }

        public void Drop(IDataObject data, FrameworkElement content)
        {
            var view = content as StationView;
            if (view == null) return;

            var paths = data == null ? null : data.GetData(DataFormats.FileDrop) as string[];
            view.HandleDrop(paths);
        }

        public void OnDropStateChanged(FrameworkElement content, bool over, IDataObject data)
        {
            var view = content as StationView;
            if (view == null) return;

            int count = 0;
            if (over && data != null && data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null) count = paths.Length;
            }
            view.ShowDropHint(over, count);
        }
    }

    /// <summary>中转站的设置（住在 CardSettings.Extras）。</summary>
    public sealed class StationSettings
    {
        /// <summary>Reference = 只记路径；Copy = 收进接收目录；Move = 复制成功后删除源文件。</summary>
        public string Mode = "Reference";

        /// <summary>接收目录（Copy / Move 模式必填）。</summary>
        public string Directory = "";

        /// <summary>列表最多保留多少条（超出从最旧开始丢列表项，不删文件）。</summary>
        public int MaxEntries = 50;

        public bool ShowIcons = true;

        /// <summary>加载状态时自动移除已经不存在的列表项（只清列表，不删文件）。</summary>
        public bool AutoRemoveMissing = false;

        public void Normalize()
        {
            string mode = (Mode ?? "").Trim();
            if (string.Equals(mode, "Copy", StringComparison.OrdinalIgnoreCase)) Mode = "Copy";
            else if (string.Equals(mode, "Move", StringComparison.OrdinalIgnoreCase)) Mode = "Move";
            else Mode = "Reference";

            Directory = (Directory ?? "").Trim();
            MaxEntries = (int)Settings.Clamp(MaxEntries, 1, 500);
        }

        public StationSettings Clone()
        {
            return new StationSettings
            {
                Mode = Mode,
                Directory = Directory,
                MaxEntries = MaxEntries,
                ShowIcons = ShowIcons,
                AutoRemoveMissing = AutoRemoveMissing
            };
        }
    }

    public sealed class StationItem
    {
        public string Id = "";
        public string Path = "";
        public string StoredPath = "";
        public string Name = "";
        public long Size = 0;
        public string AddedUtc = "";
        public bool Missing = false;
        public bool IsDirectory = false;

        public string DisplayPath
        {
            get { return string.IsNullOrEmpty(StoredPath) ? Path : StoredPath; }
        }

        /// <summary>源 / 目标可能是文件也可能是文件夹；缺失检查不能只看 File.Exists。</summary>
        public bool PathExists()
        {
            string path = DisplayPath;
            if (string.IsNullOrEmpty(path)) return false;

            try { return File.Exists(path) || Directory.Exists(path); }
            catch { return false; }
        }
    }

    public sealed class StationState
    {
        public int FormatVersion = 1;
        public List<StationItem> Items = new List<StationItem>();
    }

    /// <summary>中转站的内容控件：标题、列表、拖入高亮、状态行。</summary>
    internal sealed class StationView : Grid
    {
        const string StateFile = "items.json";

        Settings _settings;
        readonly CardSettings _config;
        readonly StationSettings _station;
        readonly ICardHost _host;
        readonly CardHeader _header;
        readonly CardButton _openDirectory;
        readonly CardButton _clear;
        readonly CardButton _cancelCopy;
        readonly CardInput _search;
        readonly CardList _list;
        readonly CardStatus _status;
        readonly CardDropOverlay _dropOverlay;
        readonly CardJobQueue _jobs = new CardJobQueue(1);
        readonly List<StationItem> _items = new List<StationItem>();

        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        int _copyTotal;
        int _copyDone;
        string _activeName;
        double _activePercent;
        double _activeSpeed;
        bool _cancelRequested;
        int _autoRemoved;
        string _filter = "";
        bool _syncingFilter;
        StationItem _dragItem;
        Point _dragOrigin;
        StationItem _selectionAnchor;

        public StationView(Settings settings, CardSettings config, ICardHost host)
        {
            _settings = settings ?? new Settings();
            _config = config ?? new CardSettings { Id = "dropstation", Kind = "dropstation" };
            _station = DropStationCard.StationOf(_config).Clone();
            _host = host;

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _header = new CardHeader("文件中转站") { Margin = new Thickness(0, 0, 0, 8) };
            _openDirectory = _header.Actions.Add("打开目录", OpenDirectory, CardButtonTone.Normal, "在资源管理器中打开接收目录");
            _header.Actions.Add("粘贴", PasteFromClipboard, CardButtonTone.Normal, "从剪贴板添加文件");
            _clear = _header.Actions.Add("清空", ClearAll, CardButtonTone.Normal, "清空列表（不删除文件）");
            Children.Add(_header);

            _search = new CardInput
            {
                Placeholder = "搜索文件名…",
                Margin = new Thickness(0, 0, 0, 6),
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
            Children.Add(_search);

            _list = new CardList
            {
                MinHeight = 72,
                MaxHeight = Math.Max(120, (_settings.Cli ?? new CliSettings()).MaxHeight - 104),
                SelectionMode = SelectionMode.Extended
            };
            _list.SetEmptyText("把文件拖进来");
            _list.ItemActivated += row => OpenItem(ItemOf(row));
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
            Children.Add(body);

            _status = new CardStatus { Margin = new Thickness(0, 4, 0, 0) };

            var statusRow = new Grid();
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusRow.Children.Add(_status);

            _cancelCopy = new CardButton("取消", CardButtonTone.Normal)
            {
                Margin = new Thickness(8, 6, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed
            };
            _cancelCopy.ToolTip = "取消排队和进行中的收纳";
            _cancelCopy.Click += (s, e) => CancelCopies();
            Grid.SetColumn(_cancelCopy, 1);
            statusRow.Children.Add(_cancelCopy);

            SetRow(statusRow, 3);
            Children.Add(statusRow);

            ApplyStyle(_settings);
            Load();
            Rebuild();
            if (_autoRemoved > 0) SetStatus("已自动移除 " + _autoRemoved + " 个丢失项", CardStatusKind.Success);
            else SetStatus(_items.Count == 0 ? "把文件拖进来，或从资源管理器拖入。" : "就绪", CardStatusKind.Normal);
        }

        public void ApplyStyle(Settings settings)
        {
            _settings = settings ?? _settings;
            _foreground = OverlaySurface.ParseColor(_settings.Style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            CliSettings cli = _settings.Cli ?? new CliSettings();
            Width = Math.Max(240, cli.MaxWidth);

            _header.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
            _search.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
            _list.ApplyStyle(_settings.Style, cli);
            _status.ApplyStyle(_settings.Style);
            _cancelCopy.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
            _dropOverlay.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
            _list.SetEmptyText(EmptyText());
            _list.MaxHeight = Math.Max(120, cli.MaxHeight - 104);
            Rebuild();
        }

        /// <summary>自检用：当前列表条数。</summary>
        internal int ItemCountForTest { get { return _items.Count; } }

        /// <summary>自检用：复制 / 移动队列是否还在跑。</summary>
        internal bool CopyBusyForTest { get { return _copyTotal > 0 && _copyDone < _copyTotal; } }

        /// <summary>自检用：第一条已收纳文件的目标路径（没有则为 null）。</summary>
        internal string FirstStoredPathForTest
        {
            get { return _items.Count == 0 ? null : _items[0].StoredPath; }
        }

        internal bool FirstIsDirectoryForTest
        {
            get { return _items.Count > 0 && _items[0].IsDirectory; }
        }

        /// <summary>自检用：设置搜索词，走真实的 TextChanged → 过滤 → Rebuild 链路。</summary>
        internal void SetSearchForTest(string text)
        {
            _search.Text = text ?? "";
        }

        internal int VisibleCountForTest { get { return _list.Items.Count; } }

        internal string BadgeForTest { get { return _header.Badge; } }

        internal int SelectedCountForTest { get { return _list.Inner.SelectedItems.Count; } }

        internal void SelectAllForTest()
        {
            _list.Inner.SelectedItems.Clear();
            foreach (object row in _list.Items) _list.Inner.SelectedItems.Add(row);
        }

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

        /// <summary>从剪贴板取文件列表；没有文件时给出提示，不弹对话框。</summary>
        void PasteFromClipboard()
        {
            string[] paths;
            if (!ClipboardService.TryGetFiles(out paths) || paths == null || paths.Length == 0)
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
            if (paths == null || paths.Length == 0) return 0;

            if (_station.Mode != "Reference" && string.IsNullOrWhiteSpace(_station.Directory))
            {
                SetStatus("先到设置里选接收目录。", CardStatusKind.Error);
                return 0;
            }

            int added = 0;
            foreach (string raw in paths)
            {
                string source = FullPath(raw);
                if (source == null || Contains(source)) continue;

                bool isDirectory = Directory.Exists(source);
                if (!isDirectory && !File.Exists(source))
                {
                    SetStatus("文件不存在：" + Path.GetFileName(source), CardStatusKind.Error);
                    continue;
                }

                if (isDirectory && FolderWouldContainReceiveDirectory(source))
                {
                    SetStatus("接收目录不能放在要收纳的文件夹里。", CardStatusKind.Error);
                    continue;
                }

                string name = ItemName(source, isDirectory);
                if (_station.Mode == "Reference")
                {
                    _items.Add(new StationItem
                    {
                        Id = NewId(),
                        Path = source,
                        Name = name,
                        Size = isDirectory ? 0 : SafeSize(source),
                        AddedUtc = UtcNow(),
                        IsDirectory = isDirectory
                    });
                    added++;
                }
                else
                {
                    var item = new StationItem
                    {
                        Id = NewId(),
                        Path = source,
                        Name = name,
                        AddedUtc = UtcNow(),
                        IsDirectory = isDirectory
                    };
                    _items.Add(item);
                    EnqueueCopy(item);
                    added++;
                }
            }

            if (added == 0) return 0;

            Trim();
            Persist();
            Rebuild();
            if (_station.Mode == "Reference") SetStatus(added + " 项已加入", CardStatusKind.Success);
            else RefreshCopyStatus();
            return added;
        }

        /// <summary>复制 / 移动文件夹时不能把接收目录放进源文件夹内部，否则递归会自我嵌套。</summary>
        bool FolderWouldContainReceiveDirectory(string source)
        {
            if (_station.Mode == "Reference" || string.IsNullOrWhiteSpace(_station.Directory)) return false;

            try
            {
                string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string targetRoot = Path.GetFullPath(_station.Directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase)) return true;
                return targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || targetRoot.StartsWith(sourceRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static string ItemName(string path, bool isDirectory)
        {
            if (!isDirectory) return Path.GetFileName(path);
            try
            {
                string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string name = Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch { return path; }
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

            List<StationItem> selectedItems = SelectedItems();
            _list.Items.Clear();

            foreach (StationItem item in _items)
            {
                if (!Matches(item)) continue;

                FrameworkElement row = BuildRow(item);
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
            _selectionAnchor = null;
            Rebuild();
        }

        void ClearSearch()
        {
            _filter = "";
            _selectionAnchor = null;
            if (!string.IsNullOrEmpty(_search.Text))
            {
                _syncingFilter = true;
                _search.Text = "";
                _syncingFilter = false;
            }
            Rebuild();
        }

        FrameworkElement BuildRow(StationItem item)
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
            if (_station.ShowIcons)
                icon.Source = ShellIcons.GetIcon(item.IsDirectory ? item.DisplayPath : item.Name, 16, !item.IsDirectory);
            grid.Children.Add(icon);

            string name = string.IsNullOrEmpty(item.Name) ? item.DisplayPath : item.Name;
            if (item.Missing) name += "（已丢失）";

            var title = new TextBlock
            {
                Text = name,
                Foreground = CardPalette.Brush(_foreground, item.Missing ? 0.40 : 0.92),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            string meta = item.IsDirectory ? "文件夹" : FormatSize(item.Size);
            if (!string.IsNullOrEmpty(item.StoredPath)) meta = string.IsNullOrEmpty(meta) ? "已收纳" : meta + " · 已收纳";

            var detail = new TextBlock
            {
                Text = meta,
                Foreground = CardPalette.Brush(_foreground, item.Missing ? 0.30 : 0.45),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(detail, 2);
            grid.Children.Add(detail);

            return grid;
        }

        StationItem ItemOf(object row)
        {
            var element = row as FrameworkElement;
            return element == null ? null : element.Tag as StationItem;
        }

        ListBoxItem ContainerAt(Point point)
        {
            DependencyObject hit = _list.Inner.InputHitTest(point) as DependencyObject;
            while (hit != null && !(hit is ListBoxItem))
                hit = VisualTreeHelper.GetParent(hit);
            return hit as ListBoxItem;
        }

        StationItem ItemAt(Point point, out FrameworkElement row)
        {
            row = null;
            ListBoxItem container = ContainerAt(point);
            if (container == null) return null;

            row = container.Content as FrameworkElement;
            return ItemOf(row);
        }

        /// <summary>
        /// 行内左键按下：手动选中并标记 Handled。整卡拖动在窗口的冒泡处理里，
        /// Preview 阶段吃掉左键后，从行上开始拖动就不会把整张卡片拖走。
        /// </summary>
        void OnRowMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            Point point = e.GetPosition(_list.Inner);
            FrameworkElement row;
            StationItem item = ItemAt(point, out row);
            if (item == null) { _dragItem = null; return; }

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (shift && !ctrl && _selectionAnchor != null)
            {
                SelectRange(_selectionAnchor, item);
            }
            else if (ctrl)
            {
                if (_list.Inner.SelectedItems.Contains(row)) _list.Inner.SelectedItems.Remove(row);
                else _list.Inner.SelectedItems.Add(row);
                _selectionAnchor = item;
            }
            else
            {
                _list.Inner.SelectedItem = row;
                _selectionAnchor = item;
            }

            _list.Inner.Focus();
            _dragOrigin = point;
            _dragItem = _list.Inner.SelectedItems.Contains(row) && item.PathExists()
                ? item : null;
            e.Handled = true;

            // 行内按下已被吃掉，CardList 的 MouseDoubleClick 不会再触发；
            // 这里按 ClickCount 自己处理双击打开，避免和拖出阈值冲突。
            if (e.ClickCount > 1)
            {
                _dragItem = null;
                OpenItem(item);
            }
        }

        /// <summary>Shift 连选：在可见（过滤后）列表里从锚点到目标整段选中。</summary>
        void SelectRange(StationItem anchor, StationItem target)
        {
            var visible = new List<StationItem>();
            foreach (StationItem item in _items)
                if (Matches(item)) visible.Add(item);

            int from = visible.IndexOf(anchor);
            int to = visible.IndexOf(target);
            if (from < 0 || to < 0)
            {
                FrameworkElement row = RowOf(target);
                if (row != null) _list.Inner.SelectedItem = row;
                return;
            }

            if (from > to) { int swap = from; from = to; to = swap; }

            _list.Inner.SelectedItems.Clear();
            foreach (object rowObject in _list.Items)
            {
                StationItem item = ItemOf(rowObject);
                int index = visible.IndexOf(item);
                if (index >= from && index <= to) _list.Inner.SelectedItems.Add(rowObject);
            }
        }

        FrameworkElement RowOf(StationItem item)
        {
            foreach (object rowObject in _list.Items)
            {
                var row = rowObject as FrameworkElement;
                if (row != null && ReferenceEquals(row.Tag, item)) return row;
            }
            return null;
        }

        /// <summary>行内移动超过阈值才拖出；只允许 Copy，拖完不移除列表项。</summary>
        void OnRowMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed) return;

            Point point = e.GetPosition(_list.Inner);
            if (!DragSource.ShouldStart(_dragOrigin, point)) return;

            StationItem item = _dragItem;
            _dragItem = null;

            List<StationItem> selected = SelectedItems();
            if (!selected.Contains(item)) selected = new List<StationItem> { item };

            var paths = new List<string>();
            foreach (StationItem candidate in selected)
            {
                string candidatePath = candidate.DisplayPath;
                if (candidate.PathExists()) paths.Add(candidatePath);
            }

            if (paths.Count == 0)
            {
                SetStatus("文件已不存在：" + item.DisplayPath, CardStatusKind.Error);
                return;
            }

            e.Handled = true;
            DragDropEffects effect = DragSource.BeginFiles(_list, paths, DragDropEffects.Copy);
            if (effect == DragDropEffects.None)
                SetStatus("拖出已取消", CardStatusKind.Normal);
            else if (paths.Count == 1)
                SetStatus("已拖出：" + (string.IsNullOrEmpty(item.Name) ? Path.GetFileName(paths[0]) : item.Name),
                          CardStatusKind.Success);
            else
                SetStatus("已拖出 " + paths.Count + " 个文件", CardStatusKind.Success);
        }

        void OnRowRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement row;
            StationItem item = ItemAt(e.GetPosition(_list.Inner), out row);
            if (item == null) return;

            // 右键点在已选中的行上保留多选，批量操作才有意义；点未选中的行则只选它。
            if (!_list.Inner.SelectedItems.Contains(row))
            {
                _list.Inner.SelectedItem = row;
                _selectionAnchor = item;
            }
        }

        List<StationItem> SelectedItems()
        {
            var result = new List<StationItem>();
            foreach (object row in _list.Inner.SelectedItems)
            {
                StationItem item = ItemOf(row);
                if (item != null && !result.Contains(item)) result.Add(item);
            }
            return result;
        }

        StationItem Selected()
        {
            List<StationItem> selected = SelectedItems();
            return selected.Count == 0 ? null : selected[0];
        }

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();

            var open = new MenuItem { Header = "打开" };
            open.Click += (s, e) => OpenItem(Selected());
            menu.Items.Add(open);

            var reveal = new MenuItem { Header = "在资源管理器中显示" };
            reveal.Click += (s, e) => RevealItem(Selected());
            menu.Items.Add(reveal);

            var copy = new MenuItem { Header = "复制路径" };
            copy.Click += (s, e) => CopySelectedPaths();
            menu.Items.Add(copy);

            menu.Items.Add(new Separator());

            var remove = new MenuItem { Header = "从列表移除" };
            remove.Click += (s, e) => RemoveSelected();
            menu.Items.Add(remove);

            var delete = new MenuItem { Header = "删除文件…" };
            delete.Click += (s, e) => DeleteSelectedFiles();
            menu.Items.Add(delete);

            menu.Items.Add(new Separator());

            var missing = new MenuItem { Header = "移除丢失项" };
            missing.Click += (s, e) => RemoveMissing();
            menu.Items.Add(missing);

            menu.Opened += (s, e) =>
            {
                List<StationItem> selected = SelectedItems();
                StationItem item = selected.Count == 0 ? null : selected[0];
                bool canDelete = false;
                foreach (StationItem candidate in selected)
                {
                    if (string.IsNullOrEmpty(candidate.StoredPath)) continue;
                    if (!IsInside(_station.Directory, candidate.StoredPath)) continue;
                    if (candidate.PathExists()) { canDelete = true; break; }
                }

                delete.IsEnabled = canDelete;
                open.IsEnabled = item != null;
                reveal.IsEnabled = item != null;
                copy.IsEnabled = item != null;
                remove.IsEnabled = item != null;

                remove.Header = selected.Count > 1 ? "从列表移除 " + selected.Count + " 项" : "从列表移除";
                delete.Header = selected.Count > 1 ? "删除选中项…" : "删除文件 / 文件夹…";

                int missingCount = 0;
                foreach (StationItem candidate in _items)
                    if (candidate.Missing || !candidate.PathExists()) missingCount++;
                missing.Header = missingCount == 0 ? "移除丢失项" : "移除 " + missingCount + " 个丢失项";
                missing.IsEnabled = missingCount > 0;
            };

            return menu;
        }

        // ==================== 操作 ====================

        internal void RemoveMissing()
        {
            int before = _items.Count;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                StationItem item = _items[i];
                if (item.Missing || !item.PathExists()) _items.RemoveAt(i);
            }

            int removed = before - _items.Count;
            if (removed == 0)
            {
                SetStatus("没有丢失的文件。", CardStatusKind.Normal);
                return;
            }

            _selectionAnchor = null;
            Persist();
            Rebuild();
            SetStatus("已移除 " + removed + " 个丢失项", CardStatusKind.Success);
        }

        void OpenItem(StationItem item)
        {
            if (item == null) return;

            string path = item.DisplayPath;
            if (!item.PathExists())
            {
                item.Missing = true;
                Persist();
                Rebuild();
                SetStatus("文件已不存在：" + path, CardStatusKind.Error);
                return;
            }

            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("打开失败：" + ex.Message, CardStatusKind.Error); }
        }

        void RevealItem(StationItem item)
        {
            if (item == null) return;

            string path = item.DisplayPath;
            if (!item.PathExists()) return;

            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch (Exception ex) { SetStatus("定位失败：" + ex.Message, CardStatusKind.Error); }
        }

        void CopySelectedPaths()
        {
            List<StationItem> selected = SelectedItems();
            if (selected.Count == 0) return;

            var lines = new List<string>();
            foreach (StationItem item in selected) lines.Add(item.DisplayPath);

            bool ok = ClipboardService.TrySetText(string.Join(Environment.NewLine, lines));
            SetStatus(ok
                    ? (selected.Count > 1 ? selected.Count + " 条路径已复制" : "路径已复制")
                    : "复制路径失败（剪贴板被占用）",
                      ok ? CardStatusKind.Success : CardStatusKind.Error);
        }

        void RemoveSelected()
        {
            List<StationItem> selected = SelectedItems();
            if (selected.Count == 0) return;

            foreach (StationItem item in selected) _items.Remove(item);
            _selectionAnchor = null;
            Persist();
            Rebuild();
            SetStatus(selected.Count > 1 ? "已从列表移除 " + selected.Count + " 项" : "已从列表移除",
                      CardStatusKind.Normal);
        }

        void ClearAll()
        {
            if (_items.Count == 0) return;

            _items.Clear();
            _selectionAnchor = null;
            Persist();
            Rebuild();
            SetStatus("列表已清空（文件未删除）", CardStatusKind.Normal);
        }

        void DeleteSelectedFiles()
        {
            List<StationItem> selected = SelectedItems();
            var deletable = new List<StationItem>();
            foreach (StationItem item in selected)
            {
                if (string.IsNullOrEmpty(item.StoredPath)) continue;
                if (!IsInside(_station.Directory, item.StoredPath)) continue;
                if (item.PathExists()) deletable.Add(item);
            }

            if (deletable.Count == 0)
            {
                SetStatus("选中的条目没有可删除的接收目录文件。", CardStatusKind.Warning);
                return;
            }

            MessageBoxResult answer;
            Window owner = Window.GetWindow(this);
            string message;
            if (deletable.Count == 1)
            {
                message = deletable[0].IsDirectory
                    ? "确定删除这个文件夹及其内容？\n\n" + deletable[0].Name
                    : "确定删除这个文件？\n\n" + deletable[0].Name;
            }
            else
            {
                message = "确定删除选中的 " + deletable.Count + " 项？\n\n文件夹会连同内容一起删除。";
            }
            if (owner == null)
                answer = MessageBox.Show(message, "文件中转站", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            else
                answer = MessageBox.Show(owner, message, "文件中转站", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            int deleted = 0;
            int failed = 0;
            foreach (StationItem item in deletable)
            {
                try
                {
                    if (item.IsDirectory || Directory.Exists(item.StoredPath))
                        Directory.Delete(item.StoredPath, true);
                    else
                        File.Delete(item.StoredPath);
                    _items.Remove(item);
                    deleted++;
                }
                catch
                {
                    failed++;
                }
            }

            if (deleted > 0)
            {
                _selectionAnchor = null;
                Persist();
                Rebuild();
            }

            if (failed == 0)
                SetStatus(deleted > 1 ? "已删除 " + deleted + " 项" : "已删除", CardStatusKind.Normal);
            else
                SetStatus("已删除 " + deleted + " 项，失败 " + failed + " 项", CardStatusKind.Warning);
        }

        void OpenDirectory()
        {
            string directory = _station.Directory;
            if (string.IsNullOrWhiteSpace(directory)) return;

            try
            {
                Directory.CreateDirectory(directory);
                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception ex)
            {
                SetStatus("打开目录失败：" + ex.Message, CardStatusKind.Error);
            }
        }

        // ==================== 复制 / 移动 ====================

        void EnqueueCopy(StationItem item)
        {
            string source = item.Path;
            string directory = _station.Directory;
            string fileName = string.IsNullOrEmpty(item.Name) ? Path.GetFileName(source) : item.Name;
            bool isDirectory = item.IsDirectory;
            string target = null;

            _copyTotal++;
            _cancelRequested = false;
            _cancelCopy.Visibility = Visibility.Visible;
            _cancelCopy.IsEnabled = true;
            RefreshCopyStatus();

            CardJob job = _jobs.Enqueue(fileName, (token, progress) =>
            {
                Directory.CreateDirectory(directory);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_cancelRequested || _copyDone >= _copyTotal) return;
                    _activeName = fileName;
                    _activePercent = 0;
                    _activeSpeed = 0;
                    RefreshCopyStatus();
                }));

                var plan = new List<KeyValuePair<string, string>>();
                long total = 0;

                try
                {
                    if (isDirectory)
                    {
                        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("暂不支持收纳符号链接文件夹。");

                        target = UniqueTarget(directory, fileName, true);
                        Directory.CreateDirectory(target);

                        var options = new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            AttributesToSkip = FileAttributes.ReparsePoint,
                            IgnoreInaccessible = false
                        };

                        foreach (string dir in Directory.EnumerateDirectories(source, "*", options))
                        {
                            token.ThrowIfCancellationRequested();
                            string relative = Path.GetRelativePath(source, dir);
                            Directory.CreateDirectory(Path.Combine(target, relative));
                        }

                        foreach (string file in Directory.EnumerateFiles(source, "*", options))
                        {
                            token.ThrowIfCancellationRequested();
                            string relative = Path.GetRelativePath(source, file);
                            plan.Add(new KeyValuePair<string, string>(file, Path.Combine(target, relative)));
                            total += SafeSize(file);
                        }
                    }
                    else
                    {
                        target = UniqueTarget(directory, fileName);
                        plan.Add(new KeyValuePair<string, string>(source, target));
                        total = SafeSize(source);
                    }

                    long copied = 0;
                    var watch = Stopwatch.StartNew();
                    DateTime lastReport = DateTime.MinValue;

                    foreach (KeyValuePair<string, string> pair in plan)
                    {
                        token.ThrowIfCancellationRequested();

                        string parent = Path.GetDirectoryName(pair.Value);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                        using (var input = File.OpenRead(pair.Key))
                        using (var output = File.Create(pair.Value))
                        {
                            var buffer = new byte[1024 * 1024];
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, read);
                                copied += read;

                                double ratio = total > 0 ? Math.Min(1.0, (double)copied / total) : 1.0;
                                progress.Report(ratio);

                                DateTime now = DateTime.UtcNow;
                                if ((now - lastReport).TotalMilliseconds < 120) continue;
                                lastReport = now;

                                double speed = watch.Elapsed.TotalSeconds > 0
                                    ? copied / (1024.0 * 1024.0) / watch.Elapsed.TotalSeconds
                                    : 0;
                                double uiRatio = ratio;
                                double uiSpeed = speed;
                                Dispatcher.BeginInvoke(new Action(() => UpdateActiveCopy(fileName, uiRatio, uiSpeed)));
                            }
                        }
                    }

                    if (total == 0) progress.Report(1.0);
                }
                catch
                {
                    // 失败 / 取消：把写了一半的目标文件 / 文件夹删掉，别留半成品。
                    try
                    {
                        if (!string.IsNullOrEmpty(target))
                        {
                            if (Directory.Exists(target)) Directory.Delete(target, true);
                            else if (File.Exists(target)) File.Delete(target);
                        }
                    }
                    catch { }
                    throw;
                }

                return Task.CompletedTask;
            });

            job.Task.ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(new Action(() => CompleteCopy(item, job, target)));
            });
        }

        void CompleteCopy(StationItem item, CardJob job, string target)
        {
            _copyDone++;
            _activeName = null;
            _activePercent = 0;
            _activeSpeed = 0;

            if (job.State == CardJobState.Succeeded && !string.IsNullOrEmpty(target))
            {
                item.StoredPath = target;
                item.Name = Path.GetFileName(target);
                item.Size = item.IsDirectory ? 0 : SafeSize(target);
                item.Missing = false;

                if (_station.Mode == "Move")
                {
                    try
                    {
                        if (item.IsDirectory) Directory.Delete(item.Path, true);
                        else File.Delete(item.Path);
                        SetStatus("已移动 " + item.Name, CardStatusKind.Success);
                    }
                    catch (Exception ex)
                    {
                        // 复制已经成功，目标文件 / 文件夹保留；源删不掉只提示，不回滚目标。
                        SetStatus("已收纳，但删除源失败：" + ex.Message, CardStatusKind.Warning);
                    }
                }
                else
                {
                    SetStatus("已收纳 " + item.Name, CardStatusKind.Success);
                }
            }
            else if (job.State == CardJobState.Failed)
            {
                _items.Remove(item);
                SetStatus("收纳失败：" + (job.Error ?? "未知错误"), CardStatusKind.Error);
            }
            else
            {
                _items.Remove(item);
                SetStatus("收纳已取消", CardStatusKind.Warning);
            }

            Persist();
            Rebuild();

            if (_copyDone >= _copyTotal)
            {
                _copyTotal = 0;
                _copyDone = 0;
                _cancelRequested = false;
            }

            RefreshCopyStatus();
        }

        internal void CancelCopies()
        {
            if (_copyTotal == 0 || _copyDone >= _copyTotal) return;

            _cancelRequested = true;
            _cancelCopy.IsEnabled = false;
            _jobs.CancelAll();
            RefreshCopyStatus();
        }

        void UpdateActiveCopy(string name, double percent, double speed)
        {
            if (_cancelRequested || _copyTotal == 0 || _copyDone >= _copyTotal) return;

            _activeName = name;
            _activePercent = Math.Max(0, Math.Min(1, percent));
            _activeSpeed = Math.Max(0, speed);
            RefreshCopyStatus();
        }

        /// <summary>复制期间的状态行：当前文件 + 百分比 + 速度；取消中显示进度；结束后清掉进度条。</summary>
        void RefreshCopyStatus()
        {
            bool busy = _copyTotal > 0 && _copyDone < _copyTotal;
            _cancelCopy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (!busy)
            {
                _status.SetBusy(false);
                _status.SetProgress(null);
                return;
            }

            if (_cancelRequested)
            {
                _status.SetText("正在取消收纳 " + _copyDone + "/" + _copyTotal + "…", CardStatusKind.Busy);
                _status.SetProgress(_copyTotal == 0 ? 0 : (double)_copyDone / _copyTotal);
                return;
            }

            int current = Math.Min(_copyTotal, _copyDone + 1);
            string text = "正在收纳 " + current + "/" + _copyTotal;
            if (!string.IsNullOrEmpty(_activeName))
            {
                text += " · " + _activeName;
                if (_activePercent > 0) text += " " + (int)Math.Round(_activePercent * 100) + "%";
                if (_activeSpeed > 0) text += " · " + FormatSpeed(_activeSpeed);
            }
            else
            {
                text += "…";
            }

            _status.SetText(text, CardStatusKind.Busy);
            _status.SetProgress(string.IsNullOrEmpty(_activeName)
                ? (_copyTotal == 0 ? 0 : (double)_copyDone / _copyTotal)
                : _activePercent);
        }

        static string FormatSpeed(double mbPerSecond)
        {
            if (mbPerSecond >= 1) return mbPerSecond.ToString("0.0") + " MB/s";
            return (mbPerSecond * 1024).ToString("0") + " KB/s";
        }

        internal static string UniqueTarget(string directory, string fileName, bool isDirectory = false)
        {
            string baseName = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);
            string extension = isDirectory ? "" : Path.GetExtension(fileName);
            string candidate = Path.Combine(directory, fileName);

            int index = 2;
            while ((File.Exists(candidate) || Directory.Exists(candidate)) && index < 1000)
            {
                candidate = Path.Combine(directory, baseName + " (" + index + ")" + extension);
                index++;
            }
            return candidate;
        }

        // ==================== 状态 ====================

        void Load()
        {
            string json = _host != null
                ? _host.ReadState(StateFile)
                : CardStateStore.Read(_config.Id, StateFile);

            if (string.IsNullOrEmpty(json)) return;

            try
            {
                StationState state = Json.Deserialize<StationState>(json);
                if (state == null || state.Items == null) return;

                foreach (StationItem item in state.Items)
                {
                    if (item == null || string.IsNullOrEmpty(item.DisplayPath)) continue;
                    if (!item.IsDirectory && Directory.Exists(item.DisplayPath)) item.IsDirectory = true;
                    item.Missing = !item.PathExists();
                    _items.Add(item);
                }

                Trim();

                if (_station.AutoRemoveMissing)
                {
                    for (int i = _items.Count - 1; i >= 0; i--)
                    {
                        if (!_items[i].Missing) continue;
                        _items.RemoveAt(i);
                        _autoRemoved++;
                    }
                    if (_autoRemoved > 0) Persist();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("中转站状态解析失败: " + ex.Message);
            }
        }

        void Persist()
        {
            try
            {
                string json = Json.Serialize(new StationState { Items = _items });
                if (_host != null) _host.WriteState(StateFile, json);
                else CardStateStore.Write(_config.Id, StateFile, json);
            }
            catch (Exception ex)
            {
                Log.Warn("中转站状态写入失败: " + ex.Message);
            }
        }

        void Trim()
        {
            int max = Math.Max(1, _station.MaxEntries);
            while (_items.Count > max) _items.RemoveAt(0);
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
                : visible + "/" + _items.Count + unit;

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
                    ? "把文件 / 文件夹拖进来，移动到 " + _station.Directory
                    : "把文件 / 文件夹拖进来，收纳到 " + _station.Directory;

            return "把文件 / 文件夹拖进来（只记录路径，不移动文件）";
        }

        void SetStatus(string text, CardStatusKind kind)
        {
            _status.SetText(text, kind);
            if (kind != CardStatusKind.Busy) _status.SetBusy(false);
        }

        // ==================== 工具 ====================

        static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try { return Path.GetFullPath(path.Trim()); }
            catch { return null; }
        }

        bool Contains(string path)
        {
            foreach (StationItem item in _items)
            {
                if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(item.StoredPath, path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static long SafeSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        static string FormatSize(long size)
        {
            if (size <= 0) return "";

            const double unit = 1024.0;
            if (size < unit) return size + " B";
            if (size < unit * unit) return (size / unit).ToString("0.0") + " KB";
            if (size < unit * unit * unit) return (size / unit / unit).ToString("0.0") + " MB";
            return (size / unit / unit / unit).ToString("0.0") + " GB";
        }

        static bool IsInside(string directory, string path)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(path);
                return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o");
        }
    }

    /// <summary>中转站的设置区块：模式 / 接收目录 / 最多保留。</summary>
    internal sealed class StationEditor : StackPanel
    {
        readonly CardSettings _config;
        readonly Action _onChanged;
        readonly ComboBox _mode;
        readonly TextBox _directory;
        readonly Button _browse;
        readonly TextBox _maxEntries;
        readonly CheckBox _showIcons;
        readonly CheckBox _autoRemove;

        public StationEditor(CardSettings config, Action onChanged)
        {
            _config = config;
            _onChanged = onChanged;

            Orientation = Orientation.Vertical;
            Margin = new Thickness(0, 4, 0, 6);

            StationSettings station = DropStationCard.StationOf(config);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            modeRow.Children.Add(new TextBlock { Text = "收纳方式", Width = 90, VerticalAlignment = VerticalAlignment.Center });
            _mode = new ComboBox { Width = 320 };
            _mode.Items.Add(new ComboBoxItem { Content = "引用（只记录路径，不动文件）" });
            _mode.Items.Add(new ComboBoxItem { Content = "复制到接收目录" });
            _mode.Items.Add(new ComboBoxItem { Content = "移动到接收目录（先复制，成功后删源）" });
            _mode.SelectedIndex = station.Mode == "Copy" ? 1 : station.Mode == "Move" ? 2 : 0;
            _mode.SelectionChanged += (s, e) => Commit();
            modeRow.Children.Add(_mode);
            Children.Add(modeRow);

            var directoryRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            directoryRow.Children.Add(new TextBlock { Text = "接收目录", Width = 90, VerticalAlignment = VerticalAlignment.Center });
            _directory = new TextBox
            {
                Width = 280,
                Text = station.Directory,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _directory.TextChanged += (s, e) => Commit();
            directoryRow.Children.Add(_directory);

            _browse = new Button { Content = "选择…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            _browse.Click += (s, e) => Browse();
            directoryRow.Children.Add(_browse);
            Children.Add(directoryRow);

            var maxRow = new StackPanel { Orientation = Orientation.Horizontal };
            maxRow.Children.Add(new TextBlock { Text = "最多保留", Width = 90, VerticalAlignment = VerticalAlignment.Center });
            _maxEntries = new TextBox
            {
                Width = 60,
                Text = station.MaxEntries.ToString(),
                VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            _maxEntries.TextChanged += (s, e) => Commit();
            maxRow.Children.Add(_maxEntries);
            maxRow.Children.Add(new TextBlock { Text = " 条（超出丢最旧的列表项，不删文件）", VerticalAlignment = VerticalAlignment.Center });
            Children.Add(maxRow);

            var optionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            _showIcons = new CheckBox
            {
                Content = "显示文件图标",
                IsChecked = station.ShowIcons,
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _showIcons.Checked += (s, e) => Commit();
            _showIcons.Unchecked += (s, e) => Commit();
            optionRow.Children.Add(_showIcons);

            _autoRemove = new CheckBox
            {
                Content = "加载列表时移除丢失项（只清列表）",
                IsChecked = station.AutoRemoveMissing,
                VerticalAlignment = VerticalAlignment.Center
            };
            _autoRemove.Checked += (s, e) => Commit();
            _autoRemove.Unchecked += (s, e) => Commit();
            optionRow.Children.Add(_autoRemove);
            Children.Add(optionRow);

            UpdateEnabled(station.Mode);
        }

        void Browse()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择中转站的接收目录",
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _directory.Text = dialog.SelectedPath;
            }
        }

        void Commit()
        {
            StationSettings station = DropStationCard.StationOf(_config);

            station.Mode = _mode.SelectedIndex == 1 ? "Copy" : _mode.SelectedIndex == 2 ? "Move" : "Reference";
            station.Directory = _directory.Text;
            station.MaxEntries = ParseInt(_maxEntries.Text, station.MaxEntries);
            station.ShowIcons = _showIcons.IsChecked == true;
            station.AutoRemoveMissing = _autoRemove.IsChecked == true;
            station.Normalize();
            _config.SetExtras(DropStationCard.SettingsKey, station);

            UpdateEnabled(station.Mode);

            Action handler = _onChanged;
            if (handler != null) handler();
        }

        void UpdateEnabled(string mode)
        {
            bool copy = !string.Equals(mode, "Reference", StringComparison.OrdinalIgnoreCase);
            _directory.IsEnabled = copy;
            _browse.IsEnabled = copy;
        }

        static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse((text ?? "").Trim(), out value) ? value : fallback;
        }
    }
}
