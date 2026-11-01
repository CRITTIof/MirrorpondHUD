using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MirrorpondHUD.Core
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

        /// <summary>stdout 超过上限、只保留了尾部时为 true（显示层据此提示“输出已截断”）。</summary>
        public bool OutputTruncated;
        /// <summary>stderr 超过上限、只保留了尾部时为 true。</summary>
        public bool ErrorTruncated;
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

        // ==================== 输出上限 ====================

        /// <summary>
        /// 每个流（stdout / stderr）最多保留的字节数。超出后丢掉前面的数据、保留尾部（和现有的
        /// 行/总行数截断一样是“留最新的”），并置 <see cref="CommandResult.OutputTruncated"/> /
        /// <see cref="CommandResult.ErrorTruncated"/>。达到上限后仍然继续把管道读空再丢弃，
        /// 否则子进程会因管道写满而卡住。
        /// </summary>
        public const int MaxCaptureBytes = 1024 * 1024;

        /// <summary>有界读取的结果：最多 <see cref="MaxCaptureBytes"/> 字节，超出时只保留尾部。</summary>
        sealed class BoundedOutput
        {
            public byte[] Bytes = new byte[0];
            public bool Truncated;
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

        public static CommandResult Run(CommandSettings commandSettings, string command, CancellationToken token)
        {
            var result = new CommandResult { Command = command ?? "" };
            CommandSettings command_settings = commandSettings == null ? new CommandSettings() : commandSettings;

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

                    Task<BoundedOutput> stdout = ReadBoundedAsync(process.StandardOutput.BaseStream, MaxCaptureBytes);
                    Task<BoundedOutput> stderr = ReadBoundedAsync(process.StandardError.BaseStream, MaxCaptureBytes);

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

                    BoundedOutput stdoutBytes = WaitOutput(stdout);
                    BoundedOutput stderrBytes = WaitOutput(stderr);
                    result.Output = Decode(stdoutBytes.Bytes, stdoutBytes.Truncated);
                    result.Error = Decode(stderrBytes.Bytes, stderrBytes.Truncated);
                    result.OutputTruncated = stdoutBytes.Truncated;
                    result.ErrorTruncated = stderrBytes.Truncated;
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

        /// <summary>
        /// 有界读取：环形缓冲只保留最新 <paramref name="limit"/> 字节，更早的数据直接被覆盖并标截断。
        /// 读满上限后仍然继续把流读空再丢弃——否则子进程会因 stdout/stderr 管道写满而卡死。
        /// </summary>
        static async Task<BoundedOutput> ReadBoundedAsync(Stream stream, int limit)
        {
            var output = new BoundedOutput();
            if (stream == null || limit <= 0) return output;

            byte[] buffer = new byte[81920];
            byte[] ring = new byte[limit];
            long total = 0;
            int write = 0;   // 环形缓冲的下一个写入位置；total > limit 时它正好是最旧字节所在位置

            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;

                    total += read;

                    int copy = Math.Min(read, limit);
                    int source = read - copy;

                    int first = Math.Min(copy, limit - write);
                    Buffer.BlockCopy(buffer, source, ring, write, first);
                    if (copy > first)
                        Buffer.BlockCopy(buffer, source + first, ring, 0, copy - first);
                    write = (write + copy) % limit;
                }
            }
            catch
            {
                // 进程被结束 / 管道断开：已经读到的部分照常返回（超时与取消语义由调用方处理）。
            }

            if (total <= limit)
            {
                output.Bytes = new byte[(int)total];
                if (total > 0) Buffer.BlockCopy(ring, 0, output.Bytes, 0, (int)total);
                return output;
            }

            // 只保留尾部：从最旧字节的位置绕环形缓冲读满一圈。
            output.Truncated = true;
            output.Bytes = new byte[limit];
            Buffer.BlockCopy(ring, write, output.Bytes, 0, limit - write);
            if (write > 0) Buffer.BlockCopy(ring, 0, output.Bytes, limit - write, write);
            return output;
        }

        static BoundedOutput WaitOutput(Task<BoundedOutput> task)
        {
            try
            {
                // 保留原来的 5 秒等待：子进程被 Kill 后，个别孙进程可能还攥着管道，
                // 这时宁可返回已读到的部分（空数组），也不要无限等。
                if (task.Wait(5000)) return task.Result;
            }
            catch { }
            return new BoundedOutput();
        }

        static string Decode(byte[] bytes, bool truncatedTail = false)
        {
            if (bytes == null || bytes.Length == 0) return "";

            int start = 0;
            if (truncatedTail)
            {
                // 截断点可能落在 UTF-8 多字节字符中间：跳过开头的续字节，
                // 否则整段严格解码失败会退化成 OEM 代码页，把好好的 UTF-8 输出解成乱码。
                while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start++;
                if (start >= bytes.Length) return "";
            }

            int count = bytes.Length - start;
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes, start, count);
            }
            catch (DecoderFallbackException) { }
            catch { }

            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                return Encoding.GetEncoding(codePage).GetString(bytes, start, count);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes, start, count);
            }
        }

        // ==================== 日志脱敏 ====================

        /// <summary>日志里命令摘要的最大字数（完整命令不进日志）。</summary>
        public const int LogSummaryChars = 80;

        static readonly Regex AuthorizationPattern = new Regex(
            @"(?i)(authorization\s*[:=]\s*)(?:bearer\s+)?[^\s""']+", RegexOptions.Compiled);

        static readonly Regex BearerPattern = new Regex(
            @"(?i)(\bbearer\s+)[^\s""']+", RegexOptions.Compiled);

        static readonly Regex SecretValuePattern = new Regex(
            @"(?i)((?:--?|/)?(?:access[_-]?token|refresh[_-]?token|token|password|passwd|pwd|secret|api[_-]?key|access[_-]?key|apikey|auth(?:orization)?|bearer|user(?:name)?|user-credentials)\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s""']+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 空白分隔的密钥参数：<see cref="SecretValuePattern"/> 要求 <c>=</c> / <c>:</c>，抓不到
        /// <c>curl --token sk-xxx</c>、<c>--password secret</c>、<c>--api-key sk-abc</c>、
        /// <c>curl -u user:pass</c> 这类写法（审阅报告已用探针复现）。这里只匹配「选项名 + 空白 + 值」，
        /// 把值整体替换成 ***；长选项名与上面的规则保持一致，另加 curl 的 -u / --user / --user-credentials。
        /// </summary>
        static readonly Regex SecretOptionPattern = new Regex(
            @"(?i)((?:^|\s)(?:-u|--user(?:name)?|--user-credentials|(?:--?|/)(?:access[_-]?token|refresh[_-]?token|token|password|passwd|pwd|secret|api[_-]?key|access[_-]?key|apikey|auth(?:orization)?|bearer|private[_-]?key|client[_-]?secret))\s+)(?:""[^""]*""|'[^']*'|[^\s""']+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 给日志用的命令摘要：绝不回传完整命令。先把常见密文写法（Authorization / Bearer /
        /// token= / --password= / --token value / -u user:pass 等）替换成 ***，再截到
        /// <see cref="LogSummaryChars"/> 字。完整命令只留在界面上给用户自己看。
        /// </summary>
        public static string DescribeCommand(string command)
        {
            string text = (command ?? "").Trim();
            if (text.Length == 0) return "(空命令)";

            text = RedactSecrets(text);
            if (text.Length > LogSummaryChars)
                text = text.Substring(0, LogSummaryChars) + "…";
            return text;
        }

        static string RedactSecrets(string text)
        {
            text = AuthorizationPattern.Replace(text, "$1***");
            text = BearerPattern.Replace(text, "$1***");
            text = SecretOptionPattern.Replace(text, "$1***");   // 空白分隔：--token value / -u user:pass
            text = SecretValuePattern.Replace(text, "$1***");    // 现有的 = / : 分隔规则
            return text;
        }

        // ==================== 显示格式 ====================

        /// <summary>把返回内容整理成要显示的多行文本：单行截断 + 限制总行数（大输出只保留尾部）。</summary>
        public static string FormatOutput(CommandResult result, CommandSettings settings)
        {
            settings = settings ?? new CommandSettings();
            int maxLines = Math.Max(1, settings.MaxLines);
            var lines = new List<string>(Math.Min(maxLines, 256));

            if (result == null)
            {
                lines.Add("(无输出)");
                return string.Join("\n", lines);
            }

            // 每个流只切出尾部 maxLines 行：1 MB 输出可能有上百万行，先全量 Split 再裁剪会把 List 撑爆。
            AddLines(lines, result.Output, maxLines);
            AddLines(lines, result.Error, maxLines);

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

            if (lines.Count > maxLines)
                lines = lines.GetRange(lines.Count - maxLines, maxLines);

            // 截断提示放在最后，避免像普通输出行一样被行数上限裁掉——显示层要能看出“输出已截断”。
            if (result.OutputTruncated || result.ErrorTruncated)
                lines.Add(TruncationNote(result));

            return string.Join("\n", lines);
        }

        static string TruncationNote(CommandResult result)
        {
            string which;
            if (result.OutputTruncated && result.ErrorTruncated) which = "输出与错误输出";
            else if (result.OutputTruncated) which = "输出";
            else which = "错误输出";

            return "(输出已截断：" + which + "超过 " + FormatBytes(MaxCaptureBytes) + "，仅保留尾部)";
        }

        static string FormatBytes(int bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)) + " MB";
            if (bytes >= 1024) return (bytes / 1024) + " KB";
            return bytes + " 字节";
        }

        /// <summary>把文本尾部最多 limit 行追加到 target（只切尾部，避免全量 Split 大输出）。</summary>
        static void AddLines(List<string> target, string text, int limit)
        {
            if (string.IsNullOrEmpty(text) || limit <= 0) return;

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            if (normalized.Length == 0) return;

            // 从后往前数 limit 个换行，只切出尾部这些行；行数不够时 start 保持 0，整段都是尾部。
            int start = 0;
            int seen = 0;
            for (int i = normalized.Length - 1; i >= 0; i--)
            {
                if (normalized[i] != '\n') continue;
                seen++;
                if (seen >= limit)
                {
                    start = i + 1;
                    break;
                }
            }

            string tail = normalized.Substring(start);
            string[] parts = tail.Split('\n');
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
