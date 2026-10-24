using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    /// <summary>全部可持久化的设置。字段名即 JSON 键名。</summary>
    public sealed class Settings
    {
        /// <summary>
        /// 当前配置结构版本。0/1 是旧的单窗口配置，2 是「命令行设置全局」的初版卡片配置，
        /// 都由 SettingsMigration 升级（3 起命令行工具、工作目录等属于每张命令行卡片）。
        /// </summary>
        public const int CurrentSchemaVersion = 3;
        public int SchemaVersion = 0;

        /// <summary>系统信息卡的默认内容：CPU / 内存 / GPU / 网络 / 磁盘，前置 ReiconFont 图标。</summary>
        public const string DefaultMetricsContent =
            "{cpu_icon} {cpu:0.0}%   {mem_icon} {mem_pct:0}%   {gpu_icon} {gpu:0.0}%\n" +
            "{net_down_icon} {net_down}   {net_up_icon} {net_up}   {disk_icon} {disk:0.0}%";

        /// <summary>时钟卡的默认内容：时间、日期、星期（中文）、运行时长。</summary>
        public const string DefaultClockContent =
            "{time}  {date}\n{weekday_zh}  {uptime}";

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
            Style.AcrylicFps = (int)Clamp(Style.AcrylicFps, 2, 30);
            Style.IconScale = Clamp(Style.IconScale, 0.5, 3);
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
            Hotkeys.NormalizeCardActions();

            // 命令行工具 / 工作目录不再全局保存：它们属于每张「命令行」卡片（CardSettings.Command），
            // 这样同时放 cmd、pwsh、自定义工具的多张命令行卡片可以各用各的。

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

        /// <summary>
        /// 图标字号倍率：Reicon 图标字形的实际字形比汉字小一圈，1.0 = 与正文同字号。
        /// 默认放大到 1.45，图标与文字的视觉高度就一致，不用把整行字号调大。
        /// </summary>
        public double IconScale = 1.45;

        /// <summary>毛玻璃背景：抓取窗口后面的桌面并模糊。背景不透明度决定玻璃厚度。</summary>
        public bool Acrylic = false;

        /// <summary>毛玻璃模糊强度（像素）。</summary>
        public double AcrylicBlur = 12;

        /// <summary>毛玻璃刷新率（帧/秒）。越高越跟手，抓屏与 GPU 开销也越大。</summary>
        public int AcrylicFps = 15;

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

        /// <summary>卡片模块提供的通用热键（如 media.play-pause），按动作名保存。</summary>
        public List<CardActionHotkey> CardActions = new List<CardActionHotkey>();

        public string GetCardAction(string action)
        {
            if (CardActions == null || string.IsNullOrWhiteSpace(action)) return "";

            foreach (CardActionHotkey entry in CardActions)
            {
                if (entry != null && string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase))
                    return entry.Hotkey ?? "";
            }

            return "";
        }

        public void SetCardAction(string action, string hotkey)
        {
            if (string.IsNullOrWhiteSpace(action)) return;
            if (CardActions == null) CardActions = new List<CardActionHotkey>();

            foreach (CardActionHotkey entry in CardActions)
            {
                if (entry != null && string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Hotkey = hotkey ?? "";
                    return;
                }
            }

            CardActions.Add(new CardActionHotkey { Action = action, Hotkey = hotkey ?? "" });
        }

        public void NormalizeCardActions()
        {
            if (CardActions == null)
            {
                CardActions = new List<CardActionHotkey>();
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<CardActionHotkey>();

            foreach (CardActionHotkey entry in CardActions)
            {
                if (entry == null) continue;

                entry.Action = (entry.Action ?? "").Trim();
                entry.Hotkey = (entry.Hotkey ?? "").Trim();
                if (entry.Action.Length == 0 || !seen.Add(entry.Action)) continue;

                cleaned.Add(entry);
            }

            CardActions = cleaned;
        }
    }

    /// <summary>卡片模块的一个通用热键：动作名 + 组合键。</summary>
    public sealed class CardActionHotkey
    {
        public string Action = "";
        public string Hotkey = "";
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

    /// <summary>媒体卡片（图片 / 动图 / 视频）的配置。</summary>
    public sealed class MediaSettings
    {
        /// <summary>文件路径；空 = 卡片显示占位提示。</summary>
        public string Path = "";
        /// <summary>缩放方式：Contain（完整显示）/ Cover（裁剪填满）/ Fill（拉伸）/ None（原始大小）。</summary>
        public string Fit = "Contain";
        /// <summary>卡片内容宽度（DIP）；0 = 用媒体原始尺寸（会被 MaxWidth 夹住）。</summary>
        public double Width = 320;
        /// <summary>卡片内容高度（DIP）；0 = 按媒体长宽比自动。</summary>
        public double Height = 0;
        /// <summary>媒体不透明度 0~1。</summary>
        public double Opacity = 1.0;
        /// <summary>视频 / 动图循环播放。</summary>
        public bool Loop = true;
        /// <summary>视频自动播放。</summary>
        public bool Autoplay = true;
        /// <summary>视频静音（默认静音，避免打扰）。</summary>
        public bool Muted = true;
        /// <summary>视频播放速度倍率 0.25~4。</summary>
        public double Speed = 1.0;
        /// <summary>按外观里的圆角把媒体的四个角裁圆。</summary>
        public bool Rounded = true;

        /// <summary>
        /// 视频 / 图片解码器：Auto = 先试 Windows 自带，失败再换 ffmpeg；
        /// BuiltIn = 只用 Media Foundation；FFmpeg = 只用 ffmpeg（不支持的格式也靠它）。
        /// </summary>
        public string Decoder = "Auto";

        /// <summary>自定义 ffmpeg.exe 路径；空 = 自动在 PATH 与常见安装目录里找。</summary>
        public string DecoderPath = "";

        public MediaSettings Clone()
        {
            return new MediaSettings
            {
                Path = Path, Fit = Fit, Width = Width, Height = Height, Opacity = Opacity,
                Loop = Loop, Autoplay = Autoplay, Muted = Muted, Speed = Speed, Rounded = Rounded,
                Decoder = Decoder, DecoderPath = DecoderPath
            };
        }

        public void Normalize()
        {
            Path = (Path ?? "").Trim().Trim('"');
            Fit = NormalizeFit(Fit);
            Width = Settings.Clamp(Width, 0, 4000);
            Height = Settings.Clamp(Height, 0, 4000);
            Opacity = Settings.Clamp(Opacity, 0, 1);
            Speed = Settings.Clamp(Speed, 0.25, 4);
            Decoder = NormalizeDecoder(Decoder);
            DecoderPath = (DecoderPath ?? "").Trim().Trim('"');
        }

        /// <summary>解码器：Auto / BuiltIn / FFmpeg（也接受 mediafoundation、external 之类写法）。</summary>
        public static string NormalizeDecoder(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "builtin":
                case "built-in":
                case "mediafoundation":
                case "mf":
                case "system":
                    return "BuiltIn";
                case "ffmpeg":
                case "ffmpeg.exe":
                case "external":
                    return "FFmpeg";
                default:
                    return "Auto";
            }
        }

        /// <summary>缩放方式：Contain / Cover / Fill / None（也接受 WPF 的 Stretch 名称）。</summary>
        public static string NormalizeFit(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "cover":
                case "uniformtofill":
                case "crop":
                    return "Cover";
                case "fill":
                case "stretch":
                    return "Fill";
                case "none":
                case "original":
                    return "None";
                default:
                    return "Contain";
            }
        }
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

        /// <summary>命令行卡片自己的工具 / 目录 / 超时设置；每张命令行卡片互不影响。</summary>
        public CommandSettings Command = new CommandSettings();

        /// <summary>媒体卡片自己的文件与缩放设置。</summary>
        public MediaSettings Media = new MediaSettings();
    }
}
