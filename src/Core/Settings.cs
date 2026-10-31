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
        /// 4：图标倍率按「墨迹高度 / 文字高度」重新标定，旧的 1.45 偏大（图标比文字高 45%）。
        /// 5：卡片自己的设置搬进 <see cref="CardSettings.Extras"/>（先迁命令行卡，旧的 Command 字段不再写盘）。
        /// 6：媒体卡与 AI 对话卡的设置也搬进 Extras（Media / Chat 字段不再写盘）。
        /// </summary>
        public const int CurrentSchemaVersion = 6;
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
        public CliSettings Cli = new CliSettings();
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
            Style.CardAlign = NormalizeCardAlign(Style.CardAlign);
            Style.GlassMode = NormalizeGlassMode(Style.GlassMode);
            Style.GlassSource = NormalizeGlassSource(Style.GlassSource);
            Style.GlassBlur = (int)Clamp(Style.GlassBlur, 0, 60);
            Style.GlassFps = (int)Clamp(Style.GlassFps, 2, 30);
            if (string.IsNullOrWhiteSpace(Style.BackgroundImage)) Style.BackgroundImage = "";
            Style.ImageStretch = NormalizeStretch(Style.ImageStretch);

            if (Cli == null) Cli = new CliSettings();
            Cli.Normalize();

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

            // 命令行工具 / 工作目录不再全局保存：它们属于每张「命令行」卡片（CardSettings.Extras 里的 command），
            // 这样同时放 cmd、pwsh、自定义工具的多张命令行卡片可以各用各的。

            if (!IsHexColor(Style.Foreground)) Style.Foreground = "#E6EDF3";
            if (!IsHexColor(Style.Background)) Style.Background = "#0D1117";

            // 外观令牌：非法值回落到默认 / 派生，避免主题文件写坏后整块外观失效。
            Style.Accent = NormalizeColorToken(Style.Accent, "#2F6FEB");
            Style.Danger = NormalizeColorToken(Style.Danger, "#D14343");
            Style.Muted = NormalizeColorToken(Style.Muted, "");
            Style.Border = NormalizeColorToken(Style.Border, "");
            Style.Panel = NormalizeColorToken(Style.Panel, "");
            Style.Scrollbar = NormalizeColorToken(Style.Scrollbar, "");
            Style.ScrollbarHot = NormalizeColorToken(Style.ScrollbarHot, "");
            Style.MonoFont = (Style.MonoFont ?? "").Trim();
            Style.ThemeName = (Style.ThemeName ?? "").Trim();

            // 卡片列表的结构由 CardModel.Normalize 处理（需要卡片注册表里的元数据）。
        }

        static string NormalizeHotkey(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary>颜色令牌：#RRGGBB 或 #AARRGGBB；空串合法（= 按前景派生）；其它一律回落。</summary>
        public static bool IsColorToken(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return true;
            if (text.Length != 7 && text.Length != 9) return false;
            if (text[0] != '#') return false;

            for (int i = 1; i < text.Length; i++)
            {
                if (!Uri.IsHexDigit(text[i])) return false;
            }

            return true;
        }

        static string NormalizeColorToken(string value, string fallback)
        {
            string text = (value ?? "").Trim();
            return IsColorToken(text) ? text : fallback;
        }


        /// <summary>卡片对齐：Auto / Left / Right。</summary>
        public static string NormalizeCardAlign(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "left":
                    return "Left";
                case "right":
                    return "Right";
                default:
                    return "Auto";
            }
        }

        /// <summary>毛玻璃实现方式：System（accent 亚克力底板） / Dwm（DWM 桌面亚克力底板） / Compat（截屏取样）。</summary>
        public static string NormalizeGlassMode(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "dwm":
                case "acrylic":
                case "desktop":
                case "multiwindow":
                case "multi-window":
                    return "Dwm";
                case "compat":
                case "compatible":
                case "compatibility":
                case "printwindow":
                    return "Compat";
                default:
                    return "System";
            }
        }

        /// <summary>
        /// 桌面亚克力的取图后端：Auto（DXGI 能用就用） / Gpu（强制 DXGI 桌面复制） / Cpu（强制 GDI 读屏）。
        /// </summary>
        public static string NormalizeGlassSource(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "gpu":
                case "dxgi":
                case "dup":
                case "duplication":
                    return "Gpu";
                case "cpu":
                case "gdi":
                case "bitblt":
                    return "Cpu";
                default:
                    return "Auto";
            }
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

        /// <summary>卡片对齐：Left = 所有卡片左边缘共线，Right = 右边缘共线；Auto = 按整叠卡片在屏幕左 / 右半边决定。</summary>
        public string CardAlign = "Auto";

        /// <summary>
        /// 图标大小倍率：图标字形按「正文同字号」绘制，再乘这个倍率（1.0 = 图标与文字等高）。
        /// 实测 Reicon 方框字形墨迹约 0.74 em、数字约 0.72 em，所以 1.15 就是「图标略大一圈」；
        /// 垂直方向按墨迹中心对齐正文，不再靠倍率去凑基线。
        /// </summary>
        public double IconScale = 1.15;

        /// <summary>毛玻璃背景：抓取窗口后面的桌面并模糊。背景不透明度决定玻璃厚度。</summary>
        public bool Acrylic = false;

        /// <summary>毛玻璃模糊强度（像素）。仅兼容实现（截屏取样）使用。</summary>
        public double AcrylicBlur = 12;

        /// <summary>毛玻璃刷新率（帧/秒）。仅兼容实现（截屏取样）使用。</summary>
        public int AcrylicFps = 15;

        /// <summary>
        /// 毛玻璃实现方式：
        /// System = 卡片下面的独立亚克力底板窗口（DWM 合成，进程侧零开销，模糊半径由系统决定）；
        /// Dwm = 截屏 + 自己模糊，画进卡片内容层（见 Ui.GlassFeed，可调半径 / 帧率 / 取图后端）；
        /// Compat = 截屏取样 + CPU 模糊（v2.1 的 PrintWindow 路线，可调半径 / 帧率）。
        /// 系统不支持（远程桌面 / 接口失效）时自动退回 Compat。
        /// </summary>
        public string GlassMode = "System";

        /// <summary>
        /// 桌面亚克力（<see cref="GlassMode"/> = Dwm）取屏幕像素的后端：
        /// Auto = DXGI 桌面复制可用就用它，不可用退回 GDI 读屏；
        /// Gpu = 强制 DXGI 桌面复制（拿不到就还是退回 GDI，不会没画面）；
        /// Cpu = 强制 GDI BitBlt 读屏（兼容性最好，一轮约 18 ms）。
        /// </summary>
        public string GlassSource = "Auto";

        /// <summary>桌面亚克力的模糊强度（像素，按原始分辨率算；取样图缩到 1/2 后再按 1/2 半径模糊）。</summary>
        public int GlassBlur = 18;

        /// <summary>桌面亚克力的刷新帧率（帧/秒）：每帧读一次屏 + 重新模糊。</summary>
        public int GlassFps = 10;

        // ==================== 外观令牌（v2.9.0） ====================
        // 颜色为空 = 按前景色派生（派生值等于旧代码里的 alpha 常量，所以默认观感不变）。
        // 软色（Muted / Border / Panel / Scrollbar / ScrollbarHot）要半透明时写 #AARRGGBB。

        /// <summary>强调色：主操作按钮、选中态、滑块热区。</summary>
        public string Accent = "#2F6FEB";

        /// <summary>危险色：危险按钮 / 错误文字。</summary>
        public string Danger = "#D14343";

        /// <summary>次要文字（时间戳 / 提示）。空 = 前景 0.45。</summary>
        public string Muted = "";

        /// <summary>面板 / 输入框描边。空 = 前景 0.10。</summary>
        public string Border = "";

        /// <summary>内部小面板底色。空 = 前景 0.045。</summary>
        public string Panel = "";

        /// <summary>滚动条常态。空 = 前景 0.18。</summary>
        public string Scrollbar = "";

        /// <summary>滚动条悬停 / 拖动。空 = 前景 0.40。</summary>
        public string ScrollbarHot = "";

        /// <summary>CLI 面板的等宽字体。空 = Cascadia Mono / Consolas。</summary>
        public string MonoFont = "";

        /// <summary>当前主题名（导入 / 选中内置主题时写入；手动改外观后清空）。空 = 自定义。</summary>
        public string ThemeName = "";

    }

    /// <summary>
    /// 「CLI 交互面板」的公共尺寸：AI 对话与命令行是同一类面板（终端样式 + 内部小面板记录），
    /// 所以宽度、整块高度上限、单条记录的高度上限都放在一起，两个面板共用一套参数。
    /// </summary>
    public sealed class CliSettings
    {
        /// <summary>面板宽度（DIP）：也是宽度上限，卡片不会再按文字长度自己变宽。</summary>
        public double MaxWidth = 460;

        /// <summary>整块面板的最大高度（DIP）：记录多了之后整块不再长高，改由外层滑块滚动。</summary>
        public double MaxHeight = 420;

        /// <summary>单个内部小面板（一轮问答 / 一条命令）的最大高度（DIP），超出走它自己的滑块。</summary>
        public double EntryMaxHeight = 140;

        /// <summary>最多保留几条记录（轮次 / 命令数），超过就把最老的整块扔掉。</summary>
        public int MaxEntries = 12;

        public CliSettings Clone()
        {
            return new CliSettings
            {
                MaxWidth = MaxWidth,
                MaxHeight = MaxHeight,
                EntryMaxHeight = EntryMaxHeight,
                MaxEntries = MaxEntries
            };
        }

        public void Normalize()
        {
            MaxWidth = Settings.Clamp(MaxWidth, 240, 1600);
            MaxHeight = Settings.Clamp(MaxHeight, 140, 3000);
            EntryMaxHeight = Settings.Clamp(EntryMaxHeight, 60, 2000);
            if (EntryMaxHeight > MaxHeight) EntryMaxHeight = MaxHeight;
            MaxEntries = (int)Settings.Clamp(MaxEntries, 1, 100);
        }
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
        /// <summary>
        /// 旧版「超长单行截断」开关。命令行面板改成会换行的内部小面板之后已经没有设置项了，
        /// 默认关掉（换行比截断更可读）；字段留着只是为了旧配置能读进来。
        /// </summary>
        public bool Truncate = false;
        /// <summary>截断后单行保留的字符数（只在 <see cref="Truncate"/> 为 true 时生效）。</summary>
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

        /// <summary>「信息与文本」卡的预设：metrics / clock / keys / text。</summary>
        public string Preset = "metrics";

        /// <summary>
        /// 卡片模块自己的设置（命令行卡是 <c>command</c>，以后媒体卡是 <c>media</c> 等）。
        /// 键由模块自己定，宿主不解释内容，只负责原样读写 JSON。
        ///
        /// 为什么是字典而不是继续加字段：CardSettings 是宿主的通用模型，
        /// 每多一种带设置的卡片就往这里长一个具体子对象，等于逼宿主认识每个模块（见评审文档 P1-1）。
        /// </summary>
        public Dictionary<string, object> Extras = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 读模块自己的设置；缺省或解析失败都回落到 <typeparamref name="T"/> 的默认值，调用方不用判空。
        /// 从磁盘读进来时值是原始 JSON 节点，这里做一次强类型绑定；返回的可能是副本，
        /// 改完要用 <see cref="SetExtras{T}"/> 写回。
        /// </summary>
        public T ExtrasAs<T>(string key) where T : class, new()
        {
            object raw = null;
            if (Extras != null && !string.IsNullOrEmpty(key)) Extras.TryGetValue(key, out raw);
            if (raw == null) return new T();

            var typed = raw as T;
            if (typed != null) return typed;

            try
            {
                T parsed = Json.BindNode<T>(raw);
                return parsed ?? new T();
            }
            catch (Exception ex)
            {
                Log.Warn("卡片设置 " + key + " 解析失败: " + ex.Message);
                return new T();
            }
        }

        /// <summary>写回模块自己的设置；传 null 等于删掉这一项，回到模块默认值。</summary>
        public void SetExtras<T>(string key, T value) where T : class
        {
            if (string.IsNullOrEmpty(key)) return;
            if (Extras == null) Extras = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (value == null) Extras.Remove(key);
            else Extras[key] = value;
        }
    }

    /// <summary>
    /// 「AI 对话」卡片的设置：一张卡片一套模型与接口，所以可以同时放两个不同模型。
    /// 走 OpenAI 兼容的 <c>POST {BaseUrl}/chat/completions</c>，
    /// 本地 vLLM / LM Studio / Ollama 的兼容层也能用（BaseUrl 填到 /v1 为止）。
    /// </summary>
    public sealed class ChatSettings
    {
        /// <summary>设置页里「已经存了 Key」时输入框里的提示（不回显密钥本身）。</summary>
        public const string KeyPlaceholder = "已填写 API Key，重新输入以更换";

        /// <summary>接口根地址，例如 https://api.openai.com/v1（末尾的 / 会自动去掉）。</summary>
        public string BaseUrl = "https://api.openai.com/v1";

        /// <summary>模型名，显示在卡片第一行。</summary>
        public string Model = "";

        /// <summary>
        /// API Key 的落盘形式：<c>dpapi:&lt;base64&gt;</c>，用 Windows DPAPI（当前用户）加密，
        /// 只有同一台机器、同一个 Windows 用户能解开。空 = 没填（本地服务可以留空）。
        /// 请求时用 <see cref="PlainApiKey"/> 取明文，明文只在内存里。
        /// </summary>
        public string ApiKey = "";

        /// <summary>Single = 每次提问都不带历史；Context = 保留上下文连续对话（轮询）。</summary>
        public string Mode = "Single";

        /// <summary>系统提示词；空 = 不发送。</summary>
        public string SystemPrompt = "";

        /// <summary>请求超时（秒）。</summary>
        public int TimeoutSeconds = 60;

        /// <summary>
        /// 卡片宽度（DIP）。2.4.5 起由「设置 → 外观 → CLI 交互面板」的宽度统一控制，
        /// 这个字段只为旧配置能读进来而保留。
        /// </summary>
        public double Width = 380;

        /// <summary>答案区最多显示多少行。2.4.5 起由 CLI 面板的「内部小面板最大高度」控制。</summary>
        public int AnswerLines = 8;

        public ChatSettings Clone()
        {
            return new ChatSettings
            {
                BaseUrl = BaseUrl, Model = Model, ApiKey = ApiKey, Mode = Mode,
                SystemPrompt = SystemPrompt, TimeoutSeconds = TimeoutSeconds,
                Width = Width, AnswerLines = AnswerLines
            };
        }

        string _plainKeyCache;
        string _plainKeySource;

        /// <summary>
        /// 解出来的明文 Key（只在内存里，不落盘）；空 = 没填，或者解不开（换了 Windows 用户 / 机器）。
        /// 请求头用它，不要直接读 <see cref="ApiKey"/>。
        /// </summary>
        public string PlainApiKey
        {
            get
            {
                if (_plainKeyCache != null && string.Equals(_plainKeySource, ApiKey, StringComparison.Ordinal))
                    return _plainKeyCache;

                string stored = ApiKey ?? "";
                string plain;
                if (stored.Length == 0) plain = "";                                  // 没填
                else if (!SecretStore.IsProtected(stored)) plain = stored;            // 旧配置里的明文
                else if (!SecretStore.TryUnprotect(stored, out plain)) plain = "";    // 解不开

                _plainKeySource = ApiKey;
                _plainKeyCache = plain;
                return plain;
            }
        }

        /// <summary>有可用的 Key（能解开、非空）。</summary>
        public bool HasApiKey
        {
            get { return PlainApiKey.Length > 0; }
        }

        /// <summary>settings.json 里存了 Key，但在这台机器 / 这个用户下解不开。</summary>
        public bool ApiKeyUnreadable
        {
            get
            {
                return !string.IsNullOrEmpty(ApiKey) && SecretStore.IsProtected(ApiKey)
                    && PlainApiKey.Length == 0;
            }
        }

        /// <summary>填 / 换 Key：进内存前先加密，落盘就只有密文。</summary>
        public void SetApiKey(string plain)
        {
            plain = (plain ?? "").Trim();
            ApiKey = plain.Length == 0 ? "" : SecretStore.Protect(plain);
            _plainKeySource = null;
            _plainKeyCache = null;
        }

        /// <summary>忘记 Key（清空）。</summary>
        public void ClearApiKey()
        {
            ApiKey = "";
            _plainKeySource = null;
            _plainKeyCache = null;
        }

        public void Normalize()
        {
            BaseUrl = (BaseUrl ?? "").Trim().TrimEnd('/');
            Model = (Model ?? "").Trim();

            ApiKey = (ApiKey ?? "").Trim();
            // 旧版本是明文存的：读进来就加密，下一次保存（ConfigStore.Load 会立刻回写）就只剩密文。
            if (ApiKey.Length > 0 && !SecretStore.IsProtected(ApiKey))
            {
                Log.Info("检测到明文 API Key，转为 DPAPI 加密存储");
                ApiKey = SecretStore.Protect(ApiKey);
                _plainKeySource = null;
                _plainKeyCache = null;
            }

            Mode = NormalizeMode(Mode);
            SystemPrompt = SystemPrompt ?? "";
            TimeoutSeconds = (int)Settings.Clamp(TimeoutSeconds, 5, 300);
            Width = Settings.Clamp(Width, 220, 1200);
            AnswerLines = (int)Settings.Clamp(AnswerLines, 2, 40);
        }

        /// <summary>对话模式：Single（单次） / Context（轮询，保留上下文）。</summary>
        public static string NormalizeMode(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "context":
                case "continuous":
                case "poll":
                case "polling":
                    return "Context";
                default:
                    return "Single";
            }
        }
    }
}
