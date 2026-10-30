using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 命令行卡：外观与「AI 对话」卡同一套（<see cref="CliPanelView"/>），
    /// 只是提交之后不是发请求，而是跑一条 shell 命令。
    ///
    /// 和 AI 对话卡一样声明 <see cref="NeedsMouseInput"/>：输入框要收键盘，
    /// 所以热键唤起它的时候宿主会把窗口提到前台、把焦点交给输入行。
    /// </summary>
    public sealed class CommandCard : RichCardModuleBase
    {
        public override string Kind { get { return "command"; } }
        public override string DisplayName { get { return "命令行"; } }
        public override string Icon { get { return Icons.Terminal; } }
        public override bool SupportsContent { get { return false; } }
        public override bool IsInteractive { get { return true; } }
        public override bool DefaultResident { get { return false; } }
        public override bool NeedsMouseInput { get { return true; } }
        public override string DefaultHotkey { get { return "F9"; } }
        public override int Order { get { return 4; } }

        public override FrameworkElement CreateContent(CardRenderContext context)
        {
            Settings settings = context == null ? null : context.Settings;
            CardSettings config = context == null ? null : context.Config;
            CommandSettings command = config == null ? new CommandSettings() : config.Command;
            if (command == null)
            {
                command = new CommandSettings();
                if (config != null) config.Command = command;
            }
            command.Normalize();

            return new CommandView(settings, command);
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

        public CommandView(Settings settings, CommandSettings command)
            : base(settings, settings == null ? new CliSettings() : settings.Cli)
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

        string WorkingDirectory()
        {
            return CommandRunner.ResolveWorkingDirectory(_command);
        }

        string IdleHint()
        {
            return "● 就绪 · 输入命令后回车 · Esc 关闭 · " + WorkingDirectory();
        }

        /// <summary>热键唤起：清空上一轮的记录（焦点由窗口在激活之后交给输入行）。</summary>
        public override void Begin()
        {
            _session++;
            ClearEntries();
            _running = false;
            CancelRunning();
            Input.Text = "";
            SetInputReadOnly(false);
            RefreshHeader();
            SetStatus(IdleHint(), false);
        }

        protected override void SubmitInput()
        {
            RunCommand();
        }

        async void RunCommand()
        {
            if (_running) return;

            string command = (Input.Text ?? "").Trim();
            if (command.Length == 0) return;

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
            SetStatus("… 正在执行：" + command, false);

            Log.Info("命令行执行（" + CommandRunner.ToolLabel(_command) + "，目录 " + WorkingDirectory() + "）：" + command);

            _cancel = new CancellationTokenSource();
            CancellationToken token = _cancel.Token;

            // 命令跑在后台线程，用发起这一刻的卡片设置做快照（跑的过程中改设置不影响这一条）。
            CommandSettings snapshot = _command.Clone();

            try
            {
                CommandResult result = await Task.Run(() => CommandRunner.Run(snapshot, command, token));
                if (session != _session) return;

                output.Text = CommandRunner.FormatOutput(result, snapshot);
                SetStatus(result.Canceled ? "已取消" : "● 完成 · 输入下一条命令", false);

                Log.Info("命令行完成：退出码 " + result.ExitCode + "，输出 " +
                         ((output.Text ?? "").Length) + " 字" + (result.TimedOut ? "（超时）" : ""));
            }
            catch (Exception ex)
            {
                if (session != _session) return;
                output.Text = "! " + ex.Message;
                SetStatus("执行失败：" + ex.Message, true);
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

                try { if (_cancel != null) { _cancel.Dispose(); _cancel = null; } }
                catch { }
            }
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
            try { if (_cancel != null) _cancel.Cancel(); }
            catch { }
        }
    }
}
