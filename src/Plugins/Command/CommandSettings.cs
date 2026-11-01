using System;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Core
{
    /// <summary>命令行卡自己的设置（存在 CardSettings.Extras["command"]）。</summary>
    public sealed class CommandSettings
    {
        /// <summary>cmd / powershell / pwsh / custom。</summary>
        public string Shell = "cmd";
        /// <summary>自定义命令行模板，{cmd} 会被替换成用户输入；不含 {cmd} 时追加到末尾。</summary>
        public string CustomShell = "cmd.exe /c {cmd}";
        /// <summary>工作目录；空 = 用户主目录。</summary>
        public string WorkingDirectory = "";
        /// <summary>超长单行截断开关。默认关（换行比截断更可读），开启后仍按 MaxLineChars 截断。</summary>
        public bool Truncate = false;
        /// <summary>截断后单行保留的字符数（只在 Truncate 为 true 时生效）。</summary>
        public int MaxLineChars = 120;
        /// <summary>单条命令的输出最多保留多少行（兜底上限，防止死循环刷屏把内存吃满）。</summary>
        public int MaxLines = 30;
        public int TimeoutSeconds = 20;

        public CommandSettings Clone()
        {
            return new CommandSettings
            {
                Shell = Shell,
                CustomShell = CustomShell,
                WorkingDirectory = WorkingDirectory,
                Truncate = Truncate,
                MaxLineChars = MaxLineChars,
                MaxLines = MaxLines,
                TimeoutSeconds = TimeoutSeconds
            };
        }

        public void Normalize()
        {
            Shell = Settings.NormalizeShell(Shell);
            CustomShell = string.IsNullOrWhiteSpace(CustomShell) ? "cmd.exe /c {cmd}" : CustomShell.Trim();
            WorkingDirectory = (WorkingDirectory ?? "").Trim();
            MaxLineChars = (int)Settings.Clamp(MaxLineChars, 20, 4000);
            MaxLines = (int)Settings.Clamp(MaxLines, 1, 500);
            TimeoutSeconds = (int)Settings.Clamp(TimeoutSeconds, 1, 300);
        }
    }
}
