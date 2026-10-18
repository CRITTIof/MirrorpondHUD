using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowOverlay.Core
{
    public sealed class CommandResult
    {
        public string Command = "";
        public string Output = "";
        public string Error = "";
        public int ExitCode;
        public bool TimedOut;
        public bool Failed;
        public bool Canceled;
    }

    /// <summary>
    /// 快捷命令行：把一行命令交给 CMD / PowerShell 执行，把输出按原样取回来。
    /// 输出编码先按 UTF-8 严格解码，失败再退回系统 OEM 代码页（中文系统是 GBK），
    /// 这样既照顾中文的 cmd 输出，也不至于把现代工具的 UTF-8 输出解成乱码。
    /// </summary>
    public static class CommandRunner
    {
        static CommandRunner()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        // ==================== 工具与提示符 ====================

        public static string ResolveExecutable(CommandSettings settings)
        {
            settings = settings ?? new CommandSettings();
            switch (Settings.NormalizeShell(settings.Shell))
            {
                case "powershell": return "powershell.exe";
                case "pwsh": return "pwsh.exe";
                case "custom":
                    string customExe, customArgs;
                    SplitCommandLine(ResolveTemplate(settings), out customExe, out customArgs);
                    return string.IsNullOrWhiteSpace(customExe) ? "cmd.exe" : customExe;
                default: return "cmd.exe";
            }
        }

        /// <summary>提示符里的工具名：大写；PowerShell 统一显示 PWSH。</summary>
        public static string ToolLabel(CommandSettings settings)
        {
            string exe = ResolveExecutable(settings);
            string name;
            try { name = Path.GetFileNameWithoutExtension(exe); }
            catch { name = exe; }

            if (string.IsNullOrWhiteSpace(name)) return "CMD";
            name = name.ToUpperInvariant();
            if (name == "POWERSHELL" || name == "PWSH") return "PWSH";
            return name;
        }

        public static string ResolveWorkingDirectory(CommandSettings settings)
        {
            string dir = settings == null ? "" : (settings.WorkingDirectory ?? "").Trim();
            if (dir.Length == 0)
                dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            catch { }

            try { return AppContext.BaseDirectory; } catch { }
            return "C:\\";
        }

        /// <summary>命令行提示符：[工具] [工作目录] &gt;。</summary>
        public static string BuildPrompt(CommandSettings settings)
        {
            return "[" + ToolLabel(settings) + "] [" + ResolveWorkingDirectory(settings) + "] >";
        }

        static string ResolveTemplate(CommandSettings settings)
        {
            switch (Settings.NormalizeShell(settings.Shell))
            {
                case "powershell":
                    return "powershell.exe -NoLogo -NoProfile -Command {cmd}";
                case "pwsh":
                    return "pwsh.exe -NoLogo -NoProfile -Command {cmd}";
                case "custom":
                    return string.IsNullOrWhiteSpace(settings.CustomShell)
                        ? "cmd.exe /c {cmd}"
                        : settings.CustomShell.Trim();
                default:
                    return "cmd.exe /c {cmd}";
            }
        }

        public static void BuildProcess(CommandSettings settings, string command,
                                        out string executable, out string arguments)
        {
            settings = settings ?? new CommandSettings();
            string template = ResolveTemplate(settings);
            string full = template.IndexOf("{cmd}", StringComparison.Ordinal) >= 0
                ? template.Replace("{cmd}", command ?? "")
                : template + " " + (command ?? "");

            SplitCommandLine(full, out executable, out arguments);
        }

        static void SplitCommandLine(string line, out string executable, out string arguments)
        {
            executable = "";
            arguments = "";
            line = (line ?? "").Trim();
            if (line.Length == 0) return;

            if (line[0] == '"')
            {
                int close = line.IndexOf('"', 1);
                if (close > 0)
                {
                    executable = line.Substring(1, close - 1).Trim();
                    arguments = line.Substring(close + 1).Trim();
                    return;
                }
            }

            int space = line.IndexOf(' ');
            if (space < 0)
            {
                executable = line;
                return;
            }

            executable = line.Substring(0, space).Trim();
            arguments = line.Substring(space + 1).Trim();
        }

        // ==================== 执行 ====================

        public static CommandResult Run(Settings settings, string command, CancellationToken token)
        {
            var result = new CommandResult { Command = command ?? "" };
            CommandSettings command_settings = settings == null ? new CommandSettings() : settings.Command;

            string executable, arguments;
            BuildProcess(command_settings, command, out executable, out arguments);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = ResolveWorkingDirectory(command_settings),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                    {
                        result.Failed = true;
                        result.Error = "无法启动 " + executable;
                        return result;
                    }

                    Task<byte[]> stdout = ReadAllBytesAsync(process.StandardOutput.BaseStream);
                    Task<byte[]> stderr = ReadAllBytesAsync(process.StandardError.BaseStream);

                    int timeoutMs = Math.Max(1, command_settings.TimeoutSeconds) * 1000;
                    using (token.Register(() => { try { process.Kill(true); } catch { } }))
                    {
                        if (!process.WaitForExit(timeoutMs))
                        {
                            result.TimedOut = true;
                            try { process.Kill(true); } catch { }
                            try { process.WaitForExit(2000); } catch { }
                        }
                    }

                    result.Output = Decode(WaitBytes(stdout));
                    result.Error = Decode(WaitBytes(stderr));
                    result.Canceled = token.IsCancellationRequested;

                    try { result.ExitCode = process.HasExited ? process.ExitCode : -1; }
                    catch { result.ExitCode = -1; }
                }
            }
            catch (Exception ex)
            {
                result.Failed = true;
                result.Error = ex.Message;
            }

            return result;
        }

        static async Task<byte[]> ReadAllBytesAsync(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        static byte[] WaitBytes(Task<byte[]> task)
        {
            try
            {
                if (task.Wait(5000)) return task.Result;
            }
            catch { }
            return new byte[0];
        }

        static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException) { }
            catch { }

            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                return Encoding.GetEncoding(codePage).GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }

        // ==================== 显示格式 ====================

        /// <summary>把返回内容整理成要显示的多行文本：单行截断 + 限制总行数。</summary>
        public static string FormatOutput(CommandResult result, CommandSettings settings)
        {
            settings = settings ?? new CommandSettings();
            var lines = new List<string>(64);

            if (result == null)
            {
                lines.Add("(无输出)");
                return string.Join("\n", lines);
            }

            AddLines(lines, result.Output);
            AddLines(lines, result.Error);

            if (result.TimedOut)
                lines.Add("(超时 " + Math.Max(1, settings.TimeoutSeconds) + " 秒，已终止)");
            if (result.Failed && lines.Count == 0)
                lines.Add("(启动失败：" + (string.IsNullOrEmpty(result.Error) ? "未知错误" : result.Error) + ")");
            if (result.Canceled)
                lines.Add("(已取消)");

            if (lines.Count == 0)
                lines.Add("(无输出)");
            else if (!result.Failed && !result.TimedOut && result.ExitCode != 0)
                lines.Add("(退出码 " + result.ExitCode + ")");

            for (int i = 0; i < lines.Count; i++)
                lines[i] = TruncateLine(lines[i], settings);

            int maxLines = Math.Max(1, settings.MaxLines);
            if (lines.Count > maxLines)
                lines = lines.GetRange(lines.Count - maxLines, maxLines);

            return string.Join("\n", lines);
        }

        static void AddLines(List<string> target, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            if (normalized.Length == 0) return;

            string[] parts = normalized.Split('\n');
            for (int i = 0; i < parts.Length; i++)
                target.Add(parts[i].TrimEnd('\r'));
        }

        /// <summary>超长行丢掉前面、保留后面，并加一个省略号提示。</summary>
        public static string TruncateLine(string line, CommandSettings settings)
        {
            if (line == null) return "";
            settings = settings ?? new CommandSettings();
            if (!settings.Truncate) return line;

            int limit = Math.Max(1, settings.MaxLineChars);
            if (line.Length <= limit) return line;

            int keep = Math.Min(limit, line.Length);
            return "…" + line.Substring(line.Length - keep);
        }
    }
}
