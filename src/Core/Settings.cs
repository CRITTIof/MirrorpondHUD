using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    /// <summary>全部可持久化的设置。字段名即 JSON 键名。</summary>
    public sealed class Settings
    {
        /// <summary>当前配置结构版本。0/1 是旧的单窗口配置，加载时由 SettingsMigration 升级。</summary>
        public const int CurrentSchemaVersion = 2;
        public int SchemaVersion = 0;

        /// <summary>系统信息卡的默认内容：CPU / 内存 / GPU / 网络 / 磁盘，前置 ReiconFont 图标。</summary>
        public const string DefaultMetricsContent =
            "{cpu_icon} {cpu:0.0}%   {mem_icon} {mem_pct:0}%   {gpu_icon} {gpu:0.0}%\n" +
            "{net_down_icon} {net_down}   {net_up_icon} {net_up}   {disk_icon} {disk:0.0}%";

        /// <summary>时钟卡的默认内容：时间、日期、星期、运行时长。</summary>
        public const string DefaultClockContent =
            "{time}  {date}\n{weekday}  {uptime}";

        /// <summary>按键卡的默认内容：最近按键（键帽字形）。</summary>
        public const string DefaultKeysContent = "KEY {keys}";

        /// <summary>文本卡的默认内容：计算机名与用户名。</summary>
        public const string DefaultTextContent = "{host} · {user}";

        /// <summary>叠层锚点：所有卡片从坐标 (X, Y) 开始，按顺序向下堆叠。</summary>
        public OverlaySettings Overlay = new OverlaySettings();
        public StyleSettings Style = new StyleSettings();
        public DataSettings Data = new DataSettings();
        public BehaviorSettings Behavior = new BehaviorSettings();
        public HotkeySettings Hotkeys = new HotkeySettings();
        public CommandSettings Command = new CommandSettings();

        /// <summary>卡片列表，顺序即叠层上的显示顺序。</summary>
        public List<CardSettings> Cards = new List<CardSettings>();

        public void Normalize()
        {
            SchemaVersion = CurrentSchemaVersion;

            Style.FontSize = Clamp(Style.FontSize, 6, 96);
            Style.BackgroundOpacity = Clamp(Style.BackgroundOpacity, 0, 1);
            Style.Padding = Clamp(Style.Padding, 0, 80);
            Style.LineHeight = Clamp(Style.LineHeight, 0, 120);
            Style.FontFamily = string.IsNullOrWhiteSpace(Style.FontFamily) ? "Microsoft YaHei UI" : Style.FontFamily.Trim();
            Style.OutlineThickness = Clamp(Style.OutlineThickness, 0.2, 10);
            Style.FixedWidth = Clamp(Style.FixedWidth, 0, 4000);
            Style.ImageOpacity = Clamp(Style.ImageOpacity, 0, 1);
            Style.AcrylicBlur = Clamp(Style.AcrylicBlur, 0, 60);
            if (string.IsNullOrWhiteSpace(Style.BackgroundImage)) Style.BackgroundImage = "";
            Style.ImageStretch = NormalizeStretch(Style.ImageStretch);

            Overlay.Monitor = Math.Max(0, Overlay.Monitor);
            Overlay.CornerRadius = Clamp(Overlay.CornerRadius, 0, 60);
            Overlay.X = Clamp(Overlay.X, -20000, 20000);
            Overlay.Y = Clamp(Overlay.Y, -20000, 20000);

            Data.KeyHistoryCount = (int)Clamp(Data.KeyHistoryCount, 1, 64);
            Data.MaxKeyChars = (int)Clamp(Data.MaxKeyChars, 4, 200);
            Data.RepeatThreshold = (int)Clamp(Data.RepeatThreshold, 1, 20);
            Data.NvidiaIntervalMs = (int)Clamp(Data.NvidiaIntervalMs, 500, 10000);
            if (Data.DiskDrives == null) Data.DiskDrives = new List<string>();
            if (Data.DiskDrives.Count > 0) Data.DiskDrives = NormalizeDrives(Data.DiskDrives);
            if (Data.NetworkAdapter == null) Data.NetworkAdapter = "";

            Behavior.RefreshMs = (int)Clamp(Behavior.RefreshMs, 100, 5000);
            Behavior.TypeSpeedMs = (int)Clamp(Behavior.TypeSpeedMs, 8, 300);
            Behavior.CursorStyle = NormalizeCursor(Behavior.CursorStyle);
            if (Behavior.WelcomeText == null) Behavior.WelcomeText = "Welcome Back";
            if (Behavior.WelcomeText.Length > 120) Behavior.WelcomeText = Behavior.WelcomeText.Substring(0, 120);

            Hotkeys.ToggleOverlay = (Hotkeys.ToggleOverlay ?? "").Trim();
            Hotkeys.OpenSettings = NormalizeHotkey(Hotkeys.OpenSettings, "F10");
            Hotkeys.ToggleLock = NormalizeHotkey(Hotkeys.ToggleLock, "F8");

            // 命令行热键不再是全局热键：它属于「命令行」卡片（见 CardSettings.Hotkey）。

            Command.Shell = NormalizeShell(Command.Shell);
            Command.CustomShell = string.IsNullOrWhiteSpace(Command.CustomShell)
                ? "cmd.exe /c {cmd}"
                : Command.CustomShell.Trim();
            Command.WorkingDirectory = (Command.WorkingDirectory ?? "").Trim();
            Command.MaxLineChars = (int)Clamp(Command.MaxLineChars, 20, 4000);
            Command.MaxLines = (int)Clamp(Command.MaxLines, 1, 500);
            Command.TimeoutSeconds = (int)Clamp(Command.TimeoutSeconds, 1, 300);

            if (!IsHexColor(Style.Foreground)) Style.Foreground = "#E6EDF3";
            if (!IsHexColor(Style.Background)) Style.Background = "#0D1117";

            // 卡片列表的结构由 CardModel.Normalize 处理（需要卡片注册表里的元数据）。
        }

        static string NormalizeHotkey(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary>背景图片拉伸方式：UniformToFill / Uniform / Fill / None。</summary>
        public static string NormalizeStretch(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "uniformtofill": return "UniformToFill";
                case "uniform": return "Uniform";
                case "fill": return "Fill";
                case "none": return "None";
                default: return "UniformToFill";
            }
        }

        /// <summary>启动动画里的光标样式：None / Block / Bar / Underline / Outline。</summary>
        public static string NormalizeCursor(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "none": return "None";
                case "block": return "Block";
                case "bar": return "Bar";
                case "underline": return "Underline";
                case "outline": return "Outline";
                default: return "Block";
            }
        }

        /// <summary>命令行工具预设：cmd / powershell / pwsh / custom。</summary>
        public static string NormalizeShell(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "powershell":
                case "ps":
                case "windows powershell":
                    return "powershell";
                case "pwsh":
                case "powershell7":
                case "powershell 7":
                    return "pwsh";
                case "custom":
                    return "custom";
                default:
                    return "cmd";
            }
        }

        static List<string> NormalizeDrives(List<string> drives)
        {
            var result = new List<string>(drives.Count);
            foreach (string raw in drives)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string s = raw.Trim();

                if (s.Length == 2 && s[1] == ':') s += "\\";
                else if (!s.EndsWith("\\")) s += "\\";

                if (!result.Contains(s)) result.Add(s);
            }
            return result;
        }

        public static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static bool IsHexColor(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s[0] != '#') return false;
            if (s.Length != 7) return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }
    }

    public sealed class OverlaySettings
    {
        /// <summary>叠层锚点坐标（DIP，相对所在显示器工作区左上角），第一张卡片放在这里。</summary>
        public double X = 48;
        public double Y = 48;
        /// <summary>0-based 显示器序号。</summary>
        public int Monitor = 0;
        /// <summary>true = 鼠标穿透（锁定），false = 可拖动。</summary>
        public bool Locked = true;
        public bool Topmost = true;
        public double CornerRadius = 10;
        public bool Shadow = true;
    }

    public sealed class StyleSettings
    {
        public string FontFamily = "Microsoft YaHei UI";
        public double FontSize = 15;
        public bool Bold = false;
        public string Foreground = "#E6EDF3";
        public string Background = "#0D1117";
        /// <summary>背景不透明度 0~1（窗口本身始终不透明，避免文字发虚）。</summary>
        public double BackgroundOpacity = 0.86;
        public double Padding = 14;
        public double LineHeight = 0;
        public string TextAlignment = "Left";

        /// <summary>文字描边，深色/浅色背景都能看清。</summary>
        public bool TextOutline = false;
        public string OutlineColor = "#000000";
        public double OutlineThickness = 1.2;

        /// <summary>自定义背景图片路径；空 = 只用背景色。</summary>
        public string BackgroundImage = "";
        public double ImageOpacity = 1.0;
        /// <summary>UniformToFill / Uniform / Fill / None。</summary>
        public string ImageStretch = "UniformToFill";

        /// <summary>固定内容宽度（DIP），0 = 自适应但只增不减（避免数值变化时整块跳动）。</summary>
        public double FixedWidth = 0;

        /// <summary>毛玻璃背景：抓取窗口后面的桌面并模糊。背景不透明度决定玻璃厚度。</summary>
        public bool Acrylic = false;

        /// <summary>毛玻璃模糊强度（像素）。</summary>
        public double AcrylicBlur = 12;
    }

    public sealed class DataSettings
    {
        public bool ShowCpu = true;
        public bool ShowGpu = true;
        /// <summary>-1 = 汇总所有 GPU；>=0 = 指定 LUID 序号。</summary>
        public int GpuIndex = -1;
        public bool ShowMemory = true;
        public bool ShowDisk = true;
        /// <summary>要统计剩余空间的分区，如 ["C:", "D:"]；空 = 全部固定磁盘。</summary>
        public List<string> DiskDrives = new List<string>();
        public bool ShowNetwork = true;
        /// <summary>网卡 PDH 实例名；空 = 自动选择流量最大的网卡。</summary>
        public string NetworkAdapter = "";
        public bool ShowKeyboard = true;
        public int KeyHistoryCount = 14;
        /// <summary>按键串的最大显示字符数（空格与分隔符都算），默认 18。</summary>
        public int MaxKeyChars = 18;
        /// <summary>连续相同按键超过该次数时折叠为 字符×N。</summary>
        public int RepeatThreshold = 3;
        public bool ShowNvidia = true;
        public int NvidiaIntervalMs = 2000;
    }

    public sealed class BehaviorSettings
    {
        public int RefreshMs = 500;
        public bool AutoStart = false;
        public bool StartMinimized = false;
        public bool TrayEnabled = true;

        /// <summary>启动时播放"打字 / 退格"欢迎动画。</summary>
        public bool IntroEnabled = true;
        /// <summary>欢迎语，默认 Welcome Back。</summary>
        public string WelcomeText = "Welcome Back";
        /// <summary>每字符的动画间隔（毫秒），越小越快。</summary>
        public int TypeSpeedMs = 28;

        /// <summary>模拟输入时显示的光标：None / Block / Bar / Underline / Outline。</summary>
        public string CursorStyle = "Block";
        public bool CursorBlink = true;
    }

    public sealed class HotkeySettings
    {
        /// <summary>留空 = 不启用该热键。</summary>
        public string ToggleOverlay = "";
        public string OpenSettings = "F10";
        public string ToggleLock = "F8";
    }

    public sealed class CommandSettings
    {
        /// <summary>cmd / powershell / pwsh / custom。</summary>
        public string Shell = "cmd";
        /// <summary>自定义命令行模板，{cmd} 会被替换成用户输入；不含 {cmd} 时追加到末尾。</summary>
        public string CustomShell = "cmd.exe /c {cmd}";
        /// <summary>工作目录；空 = 用户主目录。</summary>
        public string WorkingDirectory = "";
        /// <summary>单行过长时截断前面、保留后面。</summary>
        public bool Truncate = true;
        /// <summary>截断后单行保留的字符数。</summary>
        public int MaxLineChars = 120;
        /// <summary>命令行区域最多保留多少行返回内容。</summary>
        public int MaxLines = 30;
        public int TimeoutSeconds = 20;
    }

    /// <summary>
    /// 一张卡片（一个独立透明小窗口）的配置。
    /// 位置不再单独保存：卡片按列表顺序自上而下堆叠，Spacing 是与上一张可见卡片之间的距离。
    /// </summary>
    public sealed class CardSettings
    {
        /// <summary>稳定标识，删除 / 重排都不变，热键动作也挂在它上面。</summary>
        public string Id = "";
        /// <summary>卡片种类（CardRegistry 里的 Kind）。</summary>
        public string Kind = "metrics";
        /// <summary>显示名称，允许重名。</summary>
        public string Name = "";
        /// <summary>常驻：叠层显示时一直在屏幕上；关闭后需要用热键呼出。</summary>
        public bool Resident = true;
        /// <summary>呼出热键（仅非常驻卡片使用）。</summary>
        public string Hotkey = "";
        /// <summary>与上一张可见卡片的间距（DIP）。</summary>
        public double Spacing = 12;
        /// <summary>内容模板；空 = 使用卡片自带的默认内容。</summary>
        public string Content = "";
    }
}
