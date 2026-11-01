using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 命令行卡片的设置区块：工具 / 自定义命令 / 工作目录 / 返回内容上限 / 提示符预览。
    ///
    /// 以前这 8 个属性住在 <c>CardEditor</c>、面板住在 <c>SettingsWindow.xaml</c>，
    /// 新增一种带设置的卡片就要同时改那两个大文件；现在布局与读写都在模块里，
    /// 宿主只提供一个 <see cref="ContentControl"/> 来摆这块内容。
    /// </summary>
    internal static class CommandCardEditor
    {
        const double LabelWidth = 88;

        public static FrameworkElement Create(CardSettings config, Action onChanged)
        {
            CommandSettings command = CommandCard.CommandOf(config);
            var root = new StackPanel();

            var preview = new TextBlock
            {
                Margin = new Thickness(8, 0, 0, 0),
                FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
                FontSize = 12,
                Foreground = ResourceBrush("TextSecondary", Brushes.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Action commit = () =>
            {
                config.SetExtras(CommandCard.SettingsKey, command);
                preview.Text = PromptOf(command);
                if (onChanged != null) onChanged();
            };

            root.Children.Add(Title("命令行工具"));

            // ---------- 工具 ----------
            var shell = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            string[] shellLabels = { "命令提示符（CMD）", "Windows PowerShell（PWSH）", "PowerShell 7（PWSH7）", "自定义" };
            foreach (string label in shellLabels) shell.Items.Add(label);
            shell.SelectedIndex = ShellIndex(command.Shell);

            var customShell = new TextBox
            {
                Text = command.CustomShell ?? "",
                IsEnabled = Settings.NormalizeShell(command.Shell) == "custom",
                ToolTip = "用 {cmd} 代表输入的命令，例如 pwsh.exe -NoProfile -Command {cmd}"
            };

            shell.SelectionChanged += (s, e) =>
            {
                command.Shell = ShellFromIndex(shell.SelectedIndex);
                customShell.IsEnabled = Settings.NormalizeShell(command.Shell) == "custom";
                commit();
            };

            customShell.TextChanged += (s, e) =>
            {
                command.CustomShell = customShell.Text ?? "";
                commit();
            };

            root.Children.Add(Row("工具", shell));
            root.Children.Add(Row("自定义命令", customShell));

            // ---------- 工作目录 ----------
            var workDir = new TextBox
            {
                Text = command.WorkingDirectory ?? "",
                ToolTip = "留空 = 用户主目录"
            };

            var pickDir = new Button { Content = "选择…" };
            var clearDir = new Button { Content = "默认", Margin = new Thickness(6, 0, 0, 0) };

            var dirRow = new Grid();
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dirButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            dirButtons.Children.Add(pickDir);
            dirButtons.Children.Add(clearDir);

            Grid.SetColumn(dirButtons, 1);
            dirRow.Children.Add(workDir);
            dirRow.Children.Add(dirButtons);

            workDir.TextChanged += (s, e) =>
            {
                command.WorkingDirectory = workDir.Text ?? "";
                commit();
            };

            pickDir.Click += (s, e) =>
            {
                try
                {
                    using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        dialog.Description = "选择这张命令行卡片的工作目录";
                        dialog.ShowNewFolderButton = true;

                        string current = (command.WorkingDirectory ?? "").Trim();
                        if (current.Length > 0 && System.IO.Directory.Exists(current))
                            dialog.SelectedPath = current;

                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                            workDir.Text = dialog.SelectedPath;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("选择卡片工作目录失败: " + ex.Message);
                }
            };

            clearDir.Click += (s, e) => workDir.Text = "";

            root.Children.Add(Row("工作目录", dirRow));

            // ---------- 返回内容上限 ----------
            var maxLines = NumberBox(command.MaxLines.ToString("0"), 60,
                "单条命令的输出超过这个行数就只留最后几行；面板本身可以滚动，这个值只是兜底上限");
            var timeout = NumberBox(command.TimeoutSeconds.ToString("0"), 56, "命令超过这个秒数就中止");

            BindInt(maxLines, () => command.MaxLines, v => { command.MaxLines = v; commit(); });
            BindInt(timeout, () => command.TimeoutSeconds, v => { command.TimeoutSeconds = v; commit(); });

            var limits = new WrapPanel { Orientation = Orientation.Horizontal };
            limits.Children.Add(new TextBlock { Text = "输出最多保留", VerticalAlignment = VerticalAlignment.Center });
            limits.Children.Add(maxLines);
            limits.Children.Add(new TextBlock
            {
                Text = "行 · 超时",
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            limits.Children.Add(timeout);
            limits.Children.Add(new TextBlock { Text = "秒", VerticalAlignment = VerticalAlignment.Center });

            root.Children.Add(Row("返回内容", limits));

            // ---------- 提示符预览 ----------
            var promptRow = new StackPanel { Orientation = Orientation.Horizontal };
            promptRow.Children.Add(Hint("提示符", new Thickness(0)));
            promptRow.Children.Add(preview);
            root.Children.Add(promptRow);

            preview.Text = PromptOf(command);
            return root;
        }

        static string PromptOf(CommandSettings command)
        {
            try { return CommandRunner.BuildPrompt(command); }
            catch (Exception ex) { return "(预览失败: " + ex.Message + ")"; }
        }

        static int ShellIndex(string shell)
        {
            switch (Settings.NormalizeShell(shell))
            {
                case "powershell": return 1;
                case "pwsh": return 2;
                case "custom": return 3;
                default: return 0;
            }
        }

        static string ShellFromIndex(int index)
        {
            return index == 1 ? "powershell" : index == 2 ? "pwsh" : index == 3 ? "custom" : "cmd";
        }

        /// <summary>数字框：回车或失焦时提交；填的不是数字就还原成当前值。</summary>
        static void BindInt(TextBox box, Func<int> get, Action<int> set)
        {
            Action apply = () =>
            {
                int value;
                if (int.TryParse((box.Text ?? "").Trim(), out value)) set(value);
                else box.Text = get().ToString("0");
            };

            box.LostFocus += (s, e) => apply();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) apply(); };
        }

        static TextBox NumberBox(string text, double width, string tooltip)
        {
            return new TextBox
            {
                Text = text,
                Width = width,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip
            };
        }

        static Grid Row(string label, UIElement content)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };

            Grid.SetColumn(text, 0);
            Grid.SetColumn(content, 1);
            grid.Children.Add(text);
            grid.Children.Add(content);
            return grid;
        }

        static TextBlock Title(string text)
        {
            var block = new TextBlock { Text = text, Margin = new Thickness(0, 6, 0, 6) };
            Style style = TryStyle("SectionTitle");
            if (style != null) block.Style = style;
            return block;
        }

        static TextBlock Hint(string text, Thickness margin)
        {
            var block = new TextBlock { Text = text, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
            Style style = TryStyle("Hint");
            if (style != null) block.Style = style;
            return block;
        }

        static Style TryStyle(string key)
        {
            try
            {
                if (Application.Current == null) return null;
                return Application.Current.TryFindResource(key) as Style;
            }
            catch { return null; }
        }

        static Brush ResourceBrush(string key, Brush fallback)
        {
            try
            {
                if (Application.Current == null) return fallback;
                return (Application.Current.TryFindResource(key) as Brush) ?? fallback;
            }
            catch { return fallback; }
        }
    }
}
