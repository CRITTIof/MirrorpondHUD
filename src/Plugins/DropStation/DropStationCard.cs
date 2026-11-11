using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

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
    ICardSummary, ICardStyleAware, ICardDropTarget, ICardDropFeedback, ICardSelfTest,
    ICardScreenshotProvider
{
    public const string SettingsKey = "dropstation";

    public override string Kind => "dropstation";
    public override string DisplayName => "文件中转站";
    public override string Icon => Icons.Folder;
    public override bool SupportsContent => false;
    public override bool NeedsMouseInput => true;
    public override bool DefaultResident => true;
    public override int Order => 60;

    /// <summary>这张卡片的设置，缺省 / 解析失败都回落到默认值并写回配置。</summary>
    public static StationSettings StationOf(CardSettings config)
    {
        if (config is null) return new StationSettings();

        StationSettings station = config.ExtrasAs<StationSettings>(SettingsKey);
        station.Normalize();
        config.SetExtras(SettingsKey, station);
        return station;
    }

    public void InitializeCard(CardSettings config)
    {
        if (config is not null) StationOf(config);
    }

    public override FrameworkElement CreateContent(CardRenderContext context) => new StationView(context.Settings, context.Config, context.Host);

    public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged) => new StationEditor(config, onChanged);

    public string Validate(CardSettings config)
    {
        StationSettings station = StationOf(config);
        if (station.Mode == "Reference") return null;

        if (string.IsNullOrWhiteSpace(station.Directory)) return "还没选接收目录。";

        try
        {
            if (!Directory.Exists(station.Directory)) return $"接收目录不存在：{station.Directory}";
        }
        catch (Exception ex)
        {
            return $"接收目录不可用：{ex.Message}";
        }
        return null;
    }

    public string Summary(CardSettings config)
    {
        StationSettings station = StationOf(config);
        if (station.Mode == "Reference") return "引用";

        string directory = (station.Directory ?? "").Trim();
        string prefix = station.Mode == "Move" ? "移动" : "收纳";
        if (directory.Length == 0) return $"{prefix} · 未选目录";
        try { return $"{prefix} · {Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}"; }
        catch { return prefix; }
    }

    public void ApplyStyle(FrameworkElement content, CardRenderContext context)
    {
        var view = content as StationView;
        if (view is not null && context is not null) view.ApplyStyle(context.Settings);
    }

    public string RunSelfTest()
    {
        var config = new CardSettings { Id = "dropstation", Kind = "dropstation" };
        var station = StationOf(config);
        station.Mode = "Copy";
        station.MaxEntries = 1;
        station.Normalize();
        if (station.Mode != "Copy" || station.MaxEntries != 1) return "中转站设置归一化失败";
        if (string.IsNullOrEmpty(Validate(config))) return "空接收目录应当校验失败";

        var viewStation = new StationSettings { View = "medium" };
        viewStation.Normalize();
        if (viewStation.View != StationDisplayMode.MediumIcon) return "中转站显示方式没有归一化到中图标";
        if (!StationDisplayMode.IsTiles(viewStation.View)) return "中图标没有识别为网格视图";
        viewStation.View = "unknown";
        viewStation.Normalize();
        if (viewStation.View != StationDisplayMode.List) return "未知显示方式没有回落到列表";

        // B-1：盘符根不能被当成普通条目的文件名，否则目标会组合到接收目录外。
        string root = Path.GetPathRoot(Environment.SystemDirectory);
        if (!StationFiles.IsRootPath(root)) return $"盘符根没有被识别: {root}";
        try
        {
            StationFiles.UniqueTarget(root, root, true);
            return "盘符根作为条目名没有被拒绝";
        }
        catch (IOException)
        {
            // 预期：绝对路径 / 非法文件名被拒绝。
        }

        return null;
    }

    public bool CanDrop(IDataObject data)
    {
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop)) return false;

        var paths = data.GetData(DataFormats.FileDrop) as string[];
        return paths is not null && paths.Length > 0;
    }

    public void Drop(IDataObject data, FrameworkElement content)
    {
        var view = content as StationView;
        if (view is null) return;

        var paths = data is null ? null : data.GetData(DataFormats.FileDrop) as string[];
        view.HandleDrop(paths);
    }

    public void OnDropStateChanged(FrameworkElement content, bool over, IDataObject data)
    {
        var view = content as StationView;
        if (view is null) return;

        int count = 0;
        if (over && data is not null && data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = data.GetData(DataFormats.FileDrop) as string[];
            if (paths is not null) count = paths.Length;
        }
        view.ShowDropHint(over, count);
    }

    /// <summary>截图专用：给中转站塞一组样张，不写入 items.json，也不影响真实卡片。</summary>
    public void PrepareScreenshot(FrameworkElement content) => (content as StationView)?.LoadScreenshotSamples();
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

    /// <summary>列表显示方式：LargeIcon / MediumIcon / SmallIcon / List。</summary>
    public string View = "List";

    /// <summary>加载状态时自动移除已经不存在的列表项（只清列表，不删文件）。</summary>
    public bool AutoRemoveMissing = false;

    public void Normalize()
    {
        string mode = (Mode ?? "").Trim();
        if (string.Equals(mode, "Copy", StringComparison.OrdinalIgnoreCase)) Mode = "Copy";
        else if (string.Equals(mode, "Move", StringComparison.OrdinalIgnoreCase)) Mode = "Move";
        else Mode = "Reference";

        Directory = (Directory ?? "").Trim();
        if (Directory.Length > 0)
        {
            try { Directory = Path.GetFullPath(Directory); }
            catch (Exception ex) { Log.Warn($"中转站接收目录规范化失败，保留原值: {ex.Message}"); }
        }
        MaxEntries = (int)Settings.Clamp(MaxEntries, 1, 500);
        View = StationDisplayMode.Normalize(View);
    }

    public StationSettings Clone() => new StationSettings
    {
        Mode = Mode,
        Directory = Directory,
        MaxEntries = MaxEntries,
        ShowIcons = ShowIcons,
        View = View,
        AutoRemoveMissing = AutoRemoveMissing
    };
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

    public string DisplayPath => string.IsNullOrEmpty(StoredPath) ? Path : StoredPath;

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
    /// <summary>当前代码能读写的 items.json 格式版本；新写盘固定用这个值。</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// 列表格式版本。审阅 §5.2 / §6.13：以前只写不校验，是未来迁移的空壳；现在由
    /// <see cref="StationStore"/> 在读取时校验——高于 <see cref="CurrentFormatVersion"/> 的文件
    /// 拒绝加载并保持原文件不动。字段默认当前版本，缺失该字段的旧文件按当前版本处理。
    /// </summary>
    public int FormatVersion = CurrentFormatVersion;

    public List<StationItem> Items = new();
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
    readonly ComboBox _view;
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

        var viewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        viewRow.Children.Add(new TextBlock { Text = "显示方式", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        _view = new ComboBox { Width = 180 };
        _view.Items.Add(new ComboBoxItem { Content = "大图标", Tag = StationDisplayMode.LargeIcon });
        _view.Items.Add(new ComboBoxItem { Content = "中图标", Tag = StationDisplayMode.MediumIcon });
        _view.Items.Add(new ComboBoxItem { Content = "小图标", Tag = StationDisplayMode.SmallIcon });
        _view.Items.Add(new ComboBoxItem { Content = "列表", Tag = StationDisplayMode.List });
        _view.SelectedIndex = station.View == StationDisplayMode.LargeIcon ? 0
            : station.View == StationDisplayMode.MediumIcon ? 1
            : station.View == StationDisplayMode.SmallIcon ? 2 : 3;
        _view.SelectionChanged += (s, e) => Commit();
        viewRow.Children.Add(_view);
        viewRow.Children.Add(new TextBlock
        {
            Text = "大 / 中图标会换成网格；小图标与列表仍是单行",
            FontSize = 12,
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        });
        Children.Add(viewRow);

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
        var viewItem = _view.SelectedItem as ComboBoxItem;
        station.View = StationDisplayMode.Normalize(viewItem is null ? null : viewItem.Tag as string);
        station.ShowIcons = _showIcons.IsChecked == true;
        station.AutoRemoveMissing = _autoRemove.IsChecked == true;
        station.Normalize();
        _config.SetExtras(DropStationCard.SettingsKey, station);

        UpdateEnabled(station.Mode);

        Action handler = _onChanged;
        if (handler is not null) handler();
    }

    void UpdateEnabled(string mode)
    {
        bool copy = !string.Equals(mode, "Reference", StringComparison.OrdinalIgnoreCase);
        _directory.IsEnabled = copy;
        _browse.IsEnabled = copy;
    }

    static int ParseInt(string text, int fallback) => int.TryParse((text ?? "").Trim(), out var value) ? value : fallback;
}
