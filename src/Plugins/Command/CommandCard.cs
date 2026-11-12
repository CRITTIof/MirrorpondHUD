using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 命令行卡：外观与「AI 对话」卡同一套（<see cref="CliPanelView"/>），
/// 只是提交之后不是发请求，而是跑一条 shell 命令。
///
/// 和 AI 对话卡一样声明 <see cref="NeedsMouseInput"/>：输入框要收键盘，
/// 所以热键唤起它的时候宿主会把窗口提到前台、把焦点交给输入行。
/// </summary>
public sealed class CommandCard : RichCardModuleBase, ICardSettingsModule, ICardEditorSection,
                                  ICardSettingsValidator, ICardSummary, ICardStyleAware, ICardActivation,
                                  ICardSelfTest, ICardDefaultProvider
{
    /// <summary>命令行设置存在 <see cref="CardSettings.Extras"/> 里的键名。</summary>
    public const string SettingsKey = "command";

    public override string Kind => "command";
    public override string DisplayName => "命令行";
    public override string Icon => Icons.Terminal;
    public override bool SupportsContent => false;
    public override bool IsInteractive => true;
    public override bool DefaultResident => false;
    public override bool NeedsMouseInput => true;
    public override string DefaultHotkey => "F9";
    public override int Order => 4;

    /// <summary>默认配置里命令行排在信息卡之后、媒体之前。</summary>
    public int DefaultOrder => 10;

    /// <summary>全新配置自带一张按 F9 呼出的命令行卡（M-1：由插件自己声明）。</summary>
    public IList<CardSettings> CreateDefaults() => new List<CardSettings>
        {
            new() {
                Kind = Kind,
                Name = DisplayName,
                Resident = false,
                Spacing = 12,
                Hotkey = DefaultHotkey,
                Content = ""
            }
        };

    public override FrameworkElement CreateContent(CardRenderContext context)
    {
        Settings settings = context is null ? null : context.Settings;
        CardSettings config = context is null ? null : context.Config;

        var view = new CommandView(settings, CommandOf(config));
        view.SetMeta(CardMeta.Header(settings, config), CardMeta.Footer(settings, config));
        return view;
    }

    // ==================== 模块自己的设置 ====================

    /// <summary>
    /// 这张卡片自己的命令行设置。设置住在 <see cref="CardSettings.Extras"/> 里，
    /// 缺省 / 解析失败都回落到默认值，并立刻写回配置（保存后就有）。
    /// </summary>
    public static CommandSettings CommandOf(CardSettings config)
    {
        if (config is null) return new CommandSettings();

        CommandSettings command = config.ExtrasAs<CommandSettings>(SettingsKey);
        command.Normalize();
        config.SetExtras(SettingsKey, command);
        return command;
    }

    /// <inheritdoc />
    public string OnActivated(CardSettings config)
    {
        CommandSettings command = CommandOf(config);
        return $"工具={CommandRunner.ToolLabel(command)}，目录={CommandRunner.ResolveWorkingDirectory(command)}";
    }

    /// <inheritdoc />
    public void OnDeactivated(CardSettings config)
    {
        // 面板自己通过 ICliFocusTarget.Cancel 收尾（取消还在跑的命令），这里没有额外状态。
    }

    // ==================== 模块契约（P1-1 / P1-2） ====================

    /// <inheritdoc />
    public void InitializeCard(CardSettings config)
    {
        if (config is null) return;
        CommandOf(config);
    }

    /// <inheritdoc />
    public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged) => CommandCardEditor.Create(config, onChanged);

    /// <inheritdoc />
    public string Validate(CardSettings config)
    {
        // 这里读原始值，不走 CommandOf：它会 Normalize，把空的模板补成默认值，校验就永远不报了。
        CommandSettings command = config is null
            ? new CommandSettings()
            : config.ExtrasAs<CommandSettings>(SettingsKey);

        if (string.Equals(Settings.NormalizeShell(command.Shell), "custom", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(command.CustomShell))
            return "选择了自定义命令行，但命令模板是空的。";

        return null;
    }

    /// <inheritdoc />
    public string Summary(CardSettings config)
    {
        CommandSettings command = CommandOf(config);

        string dir = (command.WorkingDirectory ?? "").Trim();
        if (dir.Length == 0) dir = "用户主目录";
        if (dir.Length > 28) dir = $"…{dir.Substring(dir.Length - 27)}";

        return $"{CommandRunner.ToolLabel(command)} · {dir}";
    }

    public string RunSelfTest()
    {
        var settings = new CommandSettings();
        settings.Shell = "powershell";
        settings.Normalize();
        if (CommandRunner.ToolLabel(settings) != "PWSH") return "PowerShell 工具名不是 PWSH";
        if (!CommandRunner.BuildPrompt(settings).StartsWith("[PWSH] [", StringComparison.Ordinal))
            return "命令行提示符格式不对";
        var result = new CommandResult { Output = "a\nb\nc", ExitCode = 0 };
        if (CommandRunner.FormatOutput(result, new CommandSettings { MaxLines = 2 }) != "b\nc")
            return "命令行输出尾部截断不对";

        // B-5：空自定义模板不能被 Normalize 静默补成默认值，否则 Validate 永远不报错。
        var custom = new CommandSettings { Shell = "custom", CustomShell = "   " };
        custom.Normalize();
        if (custom.CustomShell.Length != 0) return "空自定义模板被 Normalize 补成了默认值";
        var config = new CardSettings { Id = "command", Kind = "command" };
        config.SetExtras(SettingsKey, custom);
        if (string.IsNullOrEmpty(Validate(config))) return "空自定义模板应当校验失败";
        return null;
    }

    /// <summary>只换外观：面板就地重套样式，命令记录不丢。</summary>
    public void ApplyStyle(FrameworkElement content, CardRenderContext context)
    {
        var view = content as CommandView;
        if (view is null || context is null || context.Settings is null) return;

        view.ApplyStyle(context.Settings);
        // 只改页脚模板 / M6 开关时宿主不会重建内容，换肤后必须重算铭牌。
        view.SetMeta(CardMeta.Header(context.Settings, context.Config),
                     CardMeta.Footer(context.Settings, context.Config));
        CardTextStyle.ApplyOutline(context.Settings.Style, content);
    }
}

/// <summary>
/// 命令行卡片的面板：标题行（工具名 + 状态）、记录区（**每条命令一块内部小面板**，各带自己的滑块）、
/// 提示符输入行、状态行。命令跑在后台线程，结果回填到那条命令自己的小面板里。
/// </summary>
internal sealed class CommandView : CliPanelView
{
    readonly CommandSettings _command;

    CancellationTokenSource _cancel;
    bool _running;
    int _session;             // 一次性会话（关闭命令行卡片）后，旧的回填不要再往界面上写
    readonly List<string> _history = new();
    int _historyIndex = -1;

    public CommandView(Settings settings, CommandSettings command)
        : base(settings, settings is null ? new CliSettings() : settings.Cli)
    {
        _command = command ?? new CommandSettings();
        _command.Normalize();

        SetPrompt(">");
        DisableIme();          // 命令要发给 shell，回车不能被输入法吃掉
        RefreshHeader();
        SetStatus(IdleHint(), false);
    }

    void RefreshHeader()
    {
        SetTitle(CommandRunner.ToolLabel(_command));
        SetBadge(_running ? "[运行中]" : "[就绪]", WorkingDirectory());
    }

    string WorkingDirectory() => CommandRunner.ResolveWorkingDirectory(_command);

    string IdleHint() => $"● 就绪 · 输入命令后回车 · Esc 关闭 · {WorkingDirectory()}";

    /// <summary>热键唤起：清空上一轮的记录（焦点由窗口在激活之后交给输入行）。</summary>
    public override void Begin()
    {
        _session++;
        ClearEntries();
        _running = false;
        CancelRunning();
        Input.Text = "";
        SetInputReadOnly(false);
        _historyIndex = -1;
        RefreshHeader();
        SetStatus(IdleHint(), false);
    }

    protected override void SubmitInput() => RunCommand();

    /// <summary>↑/↓ 召回最近命令；输入新内容后重置召回位置。</summary>
    protected override bool HandleInputKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return false;

        if (e.Key == Key.Up)
        {
            if (_history.Count == 0) return true;
            _historyIndex = Math.Min(_historyIndex + 1, _history.Count - 1);
            Input.Text = _history[_history.Count - 1 - _historyIndex];
            Input.CaretIndex = Input.Text.Length;
            return true;
        }

        if (e.Key == Key.Down)
        {
            if (_historyIndex < 0) return true;
            _historyIndex--;
            Input.Text = _historyIndex < 0 ? "" : _history[_history.Count - 1 - _historyIndex];
            Input.CaretIndex = Input.Text.Length;
            return true;
        }

        _historyIndex = -1;
        return false;
    }

    void RememberHistory(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        if (_history.Count == 0 ||
            !string.Equals(_history[_history.Count - 1], command, StringComparison.Ordinal))
            _history.Add(command);

        // 只保留最近 50 条，避免长期开着命令行卡无限长。
        while (_history.Count > 50) _history.RemoveAt(0);
        _historyIndex = -1;
    }

    async void RunCommand()
    {
        // 顶层兜底：命令面板的建立过程（AddEntry / SetStatus 等）一旦抛异常，
        // async void 会把它交给 Dispatcher，默认行为是结束进程。
        try
        {
            await RunCommandCore();
        }
        catch (Exception ex)
        {
            _running = false;
            try { if (_cancel is not null) _cancel.Cancel(); } catch { }
            try { SetInputReadOnly(false); } catch { }
            SetStatus($"! {ex.Message}", true);
            Log.Error("命令行界面异常", ex);
        }
    }

    async Task RunCommandCore()
    {
        if (_running) return;

        string command = (Input.Text ?? "").Trim();
        if (command.Length == 0) return;

        RememberHistory(command);

        int session = _session;
        string prompt = CommandRunner.BuildPrompt(_command);

        // 一条命令 = 一块内部小面板：提示符 + 命令暗一点，输出亮一点，超出高度上限走它自己的滑块。
        CliEntryPanel entry = AddEntry();
        entry.AddText(prompt, 0.45, false, 0, 1);
        entry.AddText(command, 0.95, false, 0, 3);
        TextBlock output = entry.AddText("(运行中…)", 0.55, false, 0, 0);

        Input.Text = "";
        TrimEntries(Cli.MaxEntries);
        entry.ScrollToEnd();
        ScrollRecordsToEnd();

        _running = true;
        SetInputReadOnly(true);
        SetBadge("[运行中]", WorkingDirectory());
        SetStatus($"… 正在执行：{command}", false);

        // P2-6：完整命令可能带 Authorization / token，不写进日志；只记工具、目录、长度和脱敏摘要。
        Log.Info($"命令行执行（{CommandRunner.ToolLabel(_command)}，目录 {WorkingDirectory()}，命令 {command.Length} 字）：{CommandRunner.DescribeCommand(command)}");

        var cts = new CancellationTokenSource();
        _cancel = cts;
        CancellationToken token = cts.Token;

        // 命令跑在后台线程，用发起这一刻的卡片设置做快照（跑的过程中改设置不影响这一条）。
        CommandSettings snapshot = _command.Clone();

        try
        {
            CommandResult result = await Task.Run(() => CommandRunner.Run(snapshot, command, token));
            if (session != _session) return;

            output.Text = CommandRunner.FormatOutput(result, snapshot);
            bool truncated = result.OutputTruncated || result.ErrorTruncated;
            SetStatus(result.Canceled
                          ? "已取消"
                          : $"● 完成 · 输入下一条命令{(truncated ? " · 输出已截断" : "")}", false);

            // P1-5：取消 / 超时 / 失败不能和正常完成混在一条 INFO 里，失败要带上 stderr 内容。
            if (result.Canceled)
            {
                Log.Info("命令行已取消（用户取消或卡片销毁）");
            }
            else if (result.Failed)
            {
                Log.Warn($"命令行失败：退出码 {result.ExitCode}{(truncated ? "（输出已截断）" : "")}{(string.IsNullOrWhiteSpace(result.Error) ? "" : $"  stderr={ClipForLog(result.Error)}")}");
            }
            else if (result.TimedOut)
            {
                // 超时不是「失败态」但值得单独 Warn：命令被强杀，输出可能不完整。
                Log.Warn($"命令行超时被终止：退出码 {result.ExitCode}{(string.IsNullOrWhiteSpace(result.Error) ? "" : $"  stderr={ClipForLog(result.Error)}")}");
            }
            else
            {
                Log.Info($"命令行完成：退出码 {result.ExitCode}，输出 {((output.Text ?? "").Length)} 字{(truncated ? "（输出已截断）" : "")}");
            }
        }
        catch (Exception ex)
        {
            if (session != _session) return;
            output.Text = $"! {ex.Message}";
            SetStatus($"执行失败：{ex.Message}", true);
            // P1-5：卡片侧的执行失败也要进日志（原来只有状态行有这条信息）。
            Log.Warn($"命令行执行失败: {ex.Message}");
        }
        finally
        {
            if (session == _session)
            {
                _running = false;
                SetInputReadOnly(false);
                RefreshHeader();
                entry.ScrollToEnd();
                ScrollRecordsToEnd();
                FocusInput();
            }

            // 只收自己这一代的 CTS：用户取消后立刻开新命令时，旧 continuation
            // 晚归不能把新命令的 _cancel Dispose / 清空（B-3）。
            try { cts.Dispose(); }
            catch { }
            if (ReferenceEquals(_cancel, cts)) _cancel = null;
        }
    }

    /// <summary>把 stderr 压成单行并截短，给日志用（避免多行刷屏 / 超长内容塞爆日志）。</summary>
    static string ClipForLog(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (flat.Length <= 200) return flat;
        return $"{flat.Substring(0, 200)}…";
    }

    /// <summary>卡片被收起 / 重建：把还在跑的命令掐掉。</summary>
    public override void Cancel()
    {
        _session++;
        _running = false;
        CancelRunning();
    }

    void CancelRunning()
    {
        try { if (_cancel is not null) _cancel.Cancel(); }
        catch { }
    }
}
