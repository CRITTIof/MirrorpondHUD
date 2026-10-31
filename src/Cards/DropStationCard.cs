using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 文件中转站：把文件从资源管理器拖进卡片，暂存成列表，之后打开 / 定位 / 复制路径 / 移除。
    ///
    /// 两种模式：
    /// - 引用（默认）：只记录路径，不动文件；
    /// - 复制：把文件收进「接收目录」，重名自动改成 <c>名字 (2).扩展名</c>，复制在后台串行执行。
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
            if (station.Mode != "Copy") return null;

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
            if (station.Mode != "Copy") return "引用";

            string directory = (station.Directory ?? "").Trim();
            if (directory.Length == 0) return "收纳 · 未选目录";
            try { return "收纳 · " + Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
            catch { return "收纳"; }
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
        /// <summary>Reference = 只记路径；Copy = 收进接收目录。</summary>
        public string Mode = "Reference";

        /// <summary>接收目录（Copy 模式必填）。</summary>
        public string Directory = "";

        /// <summary>列表最多保留多少条（超出从最旧开始丢列表项，不删文件）。</summary>
        public int MaxEntries = 50;

        public bool ShowIcons = true;

        public void Normalize()
        {
            Mode = string.Equals((Mode ?? "").Trim(), "Copy", StringComparison.OrdinalIgnoreCase) ? "Copy" : "Reference";
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
                ShowIcons = ShowIcons
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

        public string DisplayPath
        {
            get { return string.IsNullOrEmpty(StoredPath) ? Path : StoredPath; }
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
        readonly CardList _list;
        readonly CardStatus _status;
        readonly CardDropOverlay _dropOverlay;
        readonly CardJobQueue _jobs = new CardJobQueue(1);
        readonly List<StationItem> _items = new List<StationItem>();

        Color _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
        int _copyTotal;
        int _copyDone;

        public StationView(Settings settings, CardSettings config, ICardHost host)
        {
            _settings = settings ?? new Settings();
            _config = config ?? new CardSettings { Id = "dropstation", Kind = "dropstation" };
            _station = DropStationCard.StationOf(_config).Clone();
            _host = host;

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _header = new CardHeader("文件中转站");
            _openDirectory = _header.Actions.Add("打开目录", OpenDirectory, CardButtonTone.Normal, "在资源管理器中打开接收目录");
            _header.Actions.Add("清空", ClearAll, CardButtonTone.Danger, "清空列表（不删除文件）");
            Children.Add(_header);

            _list = new CardList
            {
                MinHeight = 72,
                MaxHeight = Math.Max(140, (_settings.Cli ?? new CliSettings()).MaxHeight - 70)
            };
            _list.SetEmptyText("把文件拖进来");
            _list.ItemActivated += row => OpenItem(ItemOf(row));
            _list.Inner.ContextMenu = BuildMenu();

            _dropOverlay = new CardDropOverlay();

            var body = new Grid();
            body.Children.Add(_list);
            body.Children.Add(_dropOverlay);
            SetRow(body, 1);
            Children.Add(body);

            _status = new CardStatus { Margin = new Thickness(0, 4, 0, 0) };
            SetRow(_status, 2);
            Children.Add(_status);

            ApplyStyle(_settings);
            Load();
            Rebuild();
            SetStatus(_items.Count == 0 ? "把文件拖进来，或从资源管理器拖入。" : "就绪", CardStatusKind.Normal);
        }

        public void ApplyStyle(Settings settings)
        {
            _settings = settings ?? _settings;
            _foreground = OverlaySurface.ParseColor(_settings.Style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            CliSettings cli = _settings.Cli ?? new CliSettings();
            Width = Math.Max(240, cli.MaxWidth);

            _header.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
            _list.ApplyStyle(_settings.Style, cli);
            _status.ApplyStyle(_settings.Style);
            _dropOverlay.ApplyStyle(_settings.Style, _settings.Overlay.CornerRadius);
            _list.SetEmptyText(EmptyText());
            _list.MaxHeight = Math.Max(140, cli.MaxHeight - 70);
            Rebuild();
        }

        /// <summary>自检用：当前列表条数。</summary>
        internal int ItemCountForTest { get { return _items.Count; } }

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
            if (paths == null || paths.Length == 0) return;

            if (_station.Mode == "Copy" && string.IsNullOrWhiteSpace(_station.Directory))
            {
                SetStatus("先到设置里选接收目录。", CardStatusKind.Error);
                return;
            }

            int added = 0;
            foreach (string raw in paths)
            {
                string source = FullPath(raw);
                if (source == null || Contains(source)) continue;

                if (Directory.Exists(source))
                {
                    SetStatus("暂不支持直接拖文件夹。", CardStatusKind.Warning);
                    continue;
                }

                if (_station.Mode == "Reference")
                {
                    if (!File.Exists(source))
                    {
                        SetStatus("文件不存在：" + Path.GetFileName(source), CardStatusKind.Error);
                        continue;
                    }

                    _items.Add(new StationItem
                    {
                        Id = NewId(),
                        Path = source,
                        Name = Path.GetFileName(source),
                        Size = SafeSize(source),
                        AddedUtc = UtcNow()
                    });
                    added++;
                }
                else
                {
                    var item = new StationItem
                    {
                        Id = NewId(),
                        Path = source,
                        Name = Path.GetFileName(source),
                        AddedUtc = UtcNow()
                    };
                    _items.Add(item);
                    EnqueueCopy(item);
                    added++;
                }
            }

            if (added == 0) return;

            Trim();
            Persist();
            Rebuild();
            SetStatus(added + " 个文件已加入", CardStatusKind.Success);
        }

        // ==================== 列表 ====================

        void Rebuild()
        {
            StationItem selected = Selected();
            _list.Items.Clear();

            foreach (StationItem item in _items)
            {
                FrameworkElement row = BuildRow(item);
                _list.Items.Add(row);
                if (ReferenceEquals(item, selected)) _list.SelectedItem = row;
            }

            _list.RefreshEmpty();
            UpdateHeader();
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
            if (_station.ShowIcons) icon.Source = ShellIcons.GetIcon(item.Name, 16);
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

            string meta = FormatSize(item.Size);
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

        StationItem Selected()
        {
            return ItemOf(_list.SelectedItem);
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
            copy.Click += (s, e) => CopyItemPath(Selected());
            menu.Items.Add(copy);

            menu.Items.Add(new Separator());

            var remove = new MenuItem { Header = "从列表移除" };
            remove.Click += (s, e) => RemoveItem(Selected());
            menu.Items.Add(remove);

            var delete = new MenuItem { Header = "删除文件…" };
            delete.Click += (s, e) => DeleteStoredFile(Selected());
            menu.Items.Add(delete);

            menu.Opened += (s, e) =>
            {
                StationItem item = Selected();
                bool canDelete = item != null && !string.IsNullOrEmpty(item.StoredPath) &&
                                 IsInside(_station.Directory, item.StoredPath);
                delete.IsEnabled = canDelete;
                open.IsEnabled = item != null;
                reveal.IsEnabled = item != null;
                copy.IsEnabled = item != null;
                remove.IsEnabled = item != null;
            };

            return menu;
        }

        // ==================== 操作 ====================

        void OpenItem(StationItem item)
        {
            if (item == null) return;

            string path = item.DisplayPath;
            if (!File.Exists(path))
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
            if (!File.Exists(path)) return;

            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch (Exception ex) { SetStatus("定位失败：" + ex.Message, CardStatusKind.Error); }
        }

        void CopyItemPath(StationItem item)
        {
            if (item == null) return;

            bool ok = ClipboardService.TrySetText(item.DisplayPath);
            SetStatus(ok ? "路径已复制" : "复制路径失败（剪贴板被占用）",
                      ok ? CardStatusKind.Success : CardStatusKind.Error);
        }

        void RemoveItem(StationItem item)
        {
            if (item == null) return;

            _items.Remove(item);
            Persist();
            Rebuild();
            SetStatus("已从列表移除", CardStatusKind.Normal);
        }

        void ClearAll()
        {
            if (_items.Count == 0) return;

            _items.Clear();
            Persist();
            Rebuild();
            SetStatus("列表已清空（文件未删除）", CardStatusKind.Normal);
        }

        void DeleteStoredFile(StationItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.StoredPath)) return;
            if (!IsInside(_station.Directory, item.StoredPath))
            {
                SetStatus("只允许删除接收目录里的文件。", CardStatusKind.Error);
                return;
            }
            if (!File.Exists(item.StoredPath)) return;

            MessageBoxResult answer;
            Window owner = Window.GetWindow(this);
            string message = "确定删除这个文件？\n\n" + item.Name;
            if (owner == null)
                answer = MessageBox.Show(message, "文件中转站", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            else
                answer = MessageBox.Show(owner, message, "文件中转站", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            try
            {
                File.Delete(item.StoredPath);
                _items.Remove(item);
                Persist();
                Rebuild();
                SetStatus("已删除文件", CardStatusKind.Normal);
            }
            catch (Exception ex)
            {
                SetStatus("删除失败：" + ex.Message, CardStatusKind.Error);
            }
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

        // ==================== 复制 ====================

        void EnqueueCopy(StationItem item)
        {
            string source = item.Path;
            string directory = _station.Directory;
            string fileName = Path.GetFileName(source);
            string target = null;

            _copyTotal++;
            SetStatus("正在收纳 " + _copyDone + "/" + _copyTotal + "…", CardStatusKind.Busy);
            _status.SetProgress(_copyTotal == 0 ? 0 : (double)_copyDone / _copyTotal);

            CardJob job = _jobs.Enqueue(fileName, (token, progress) =>
            {
                Directory.CreateDirectory(directory);
                target = UniqueTarget(directory, fileName);

                try
                {
                    using (var input = File.OpenRead(source))
                    using (var output = File.Create(target))
                    {
                        var buffer = new byte[1024 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                        }
                    }
                }
                catch
                {
                    // 失败 / 取消：把写了一半的目标文件删掉，别留半成品。
                    try { if (!string.IsNullOrEmpty(target) && File.Exists(target)) File.Delete(target); }
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

            if (job.State == CardJobState.Succeeded && !string.IsNullOrEmpty(target))
            {
                item.StoredPath = target;
                item.Name = Path.GetFileName(target);
                item.Size = SafeSize(target);
                item.Missing = false;
                SetStatus("已收纳 " + item.Name, CardStatusKind.Success);
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
            UpdateCopyProgress();
        }

        void UpdateCopyProgress()
        {
            if (_copyDone < _copyTotal)
            {
                SetStatus("正在收纳 " + _copyDone + "/" + _copyTotal + "…", CardStatusKind.Busy);
                _status.SetProgress(_copyTotal == 0 ? 0 : (double)_copyDone / _copyTotal);
            }
            else
            {
                _status.SetBusy(false);
                _status.SetProgress(null);
            }
        }

        static string UniqueTarget(string directory, string fileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            string candidate = Path.Combine(directory, fileName);

            int index = 2;
            while (File.Exists(candidate) && index < 1000)
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
                    item.Missing = !File.Exists(item.DisplayPath);
                    _items.Add(item);
                }

                Trim();
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
            _header.Badge = _items.Count + " 个文件";
            bool canOpen = _station.Mode == "Copy" && !string.IsNullOrWhiteSpace(_station.Directory);
            _openDirectory.Visibility = canOpen ? Visibility.Visible : Visibility.Collapsed;
            _openDirectory.IsEnabled = canOpen;
        }

        string EmptyText()
        {
            if (_station.Mode == "Copy" && !string.IsNullOrWhiteSpace(_station.Directory))
                return "把文件拖进来，收纳到 " + _station.Directory;

            return "把文件拖进来（只记录路径，不移动文件）";
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

        public StationEditor(CardSettings config, Action onChanged)
        {
            _config = config;
            _onChanged = onChanged;

            Orientation = Orientation.Vertical;
            Margin = new Thickness(0, 4, 0, 6);

            StationSettings station = DropStationCard.StationOf(config);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            modeRow.Children.Add(new TextBlock { Text = "收纳方式", Width = 90, VerticalAlignment = VerticalAlignment.Center });
            _mode = new ComboBox { Width = 220 };
            _mode.Items.Add(new ComboBoxItem { Content = "引用（只记录路径，不动文件）" });
            _mode.Items.Add(new ComboBoxItem { Content = "复制到接收目录" });
            _mode.SelectedIndex = station.Mode == "Copy" ? 1 : 0;
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

            station.Mode = _mode.SelectedIndex == 1 ? "Copy" : "Reference";
            station.Directory = _directory.Text;
            station.MaxEntries = ParseInt(_maxEntries.Text, station.MaxEntries);
            station.Normalize();
            _config.SetExtras(DropStationCard.SettingsKey, station);

            UpdateEnabled(station.Mode);

            Action handler = _onChanged;
            if (handler != null) handler();
        }

        void UpdateEnabled(string mode)
        {
            bool copy = string.Equals(mode, "Copy", StringComparison.OrdinalIgnoreCase);
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
