using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core;

/// <summary>全部可持久化的设置。字段名即 JSON 键名。</summary>
public sealed class Settings
{
    /// <summary>
    /// 当前配置结构版本。2.14.7 起不再迁移历史 Schema；旧文件按损坏备份后用默认设置。
    /// 2.24.0 是唯一例外：为合并玻璃模糊字段、把 GlassMode 的 Dwm 更名 DesktopFeed、
    /// 移除 Chat 模式历史别名，提供 v6→v7 一次性迁移（见 SettingsLoader）。
    /// </summary>
    public const int CurrentSchemaVersion = 7;
    public int SchemaVersion = 0;


    /// <summary>叠层锚点：所有卡片从坐标 (X, Y) 开始，按顺序向下堆叠。</summary>
    public OverlaySettings Overlay = new();
    public StyleSettings Style = new();
    public CliSettings Cli = new();
    public DataSettings Data = new();
    public BehaviorSettings Behavior = new();
    public HotkeySettings Hotkeys = new();

    /// <summary>卡片列表，顺序即叠层上的显示顺序。</summary>
    public List<CardSettings> Cards = new();

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;

        // 手改 / 损坏的 JSON 会把整块写成 null。先补回空对象，后面的 Clamp 才不会 NRE。
        if (Style is null) Style = new StyleSettings();
        if (Overlay is null) Overlay = new OverlaySettings();
        if (Cli is null) Cli = new CliSettings();
        if (Data is null) Data = new DataSettings();
        if (Behavior is null) Behavior = new BehaviorSettings();
        if (Hotkeys is null) Hotkeys = new HotkeySettings();
        if (Cards is null) Cards = new List<CardSettings>();

        Style.FontSize = Clamp(Style.FontSize, 6, 96);
        Style.BackgroundOpacity = Clamp(Style.BackgroundOpacity, 0, 1);
        Style.Padding = Clamp(Style.Padding, 0, 80);
        Style.LineHeight = Clamp(Style.LineHeight, 0, 120);
        Style.FontFamily = string.IsNullOrWhiteSpace(Style.FontFamily) ? "Microsoft YaHei UI" : Style.FontFamily.Trim();
        Style.OutlineThickness = Clamp(Style.OutlineThickness, 0.2, 10);
        Style.FixedWidth = Clamp(Style.FixedWidth, 0, 4000);
        Style.ImageOpacity = Clamp(Style.ImageOpacity, 0, 1);
        Style.BlurRadius = (int)Clamp(Style.BlurRadius, 0, 60);
        Style.BlurFps = (int)Clamp(Style.BlurFps, 2, 30);
        Style.IconScale = Clamp(Style.IconScale, 0.5, 3);
        Style.CardAlign = NormalizeCardAlign(Style.CardAlign);
        Style.TextAlignment = NormalizeTextAlign(Style.TextAlignment);
        Style.GlassMode = NormalizeGlassMode(Style.GlassMode);
        Style.GlassSource = NormalizeGlassSource(Style.GlassSource);
        if (string.IsNullOrWhiteSpace(Style.BackgroundImage)) Style.BackgroundImage = "";
        Style.ImageStretch = NormalizeStretch(Style.ImageStretch);

        Cli.Normalize();


        Overlay.Monitor = Math.Max(0, Overlay.Monitor);
        Overlay.CornerRadius = Clamp(Overlay.CornerRadius, 0, 60);
        Overlay.X = Clamp(Overlay.X, -20000, 20000);
        Overlay.Y = Clamp(Overlay.Y, -20000, 20000);

        Data.KeyHistoryCount = (int)Clamp(Data.KeyHistoryCount, 1, 64);
        Data.MaxKeyChars = (int)Clamp(Data.MaxKeyChars, 4, 200);
        Data.RepeatThreshold = (int)Clamp(Data.RepeatThreshold, 1, 20);
        Data.NvidiaIntervalMs = (int)Clamp(Data.NvidiaIntervalMs, 500, 10000);
        if (Data.DiskDrives is null) Data.DiskDrives = new List<string>();
        if (Data.DiskDrives.Count > 0) Data.DiskDrives = NormalizeDrives(Data.DiskDrives);
        if (Data.NetworkAdapter is null) Data.NetworkAdapter = "";

        Behavior.RefreshMs = (int)Clamp(Behavior.RefreshMs, 100, 5000);
        Behavior.TypeSpeedMs = (int)Clamp(Behavior.TypeSpeedMs, 8, 300);
        Behavior.CursorStyle = NormalizeCursor(Behavior.CursorStyle);
        Behavior.CardSlide = NormalizeCardSlide(Behavior.CardSlide);
        Behavior.SettingsTheme = NormalizeSettingsTheme(Behavior.SettingsTheme);
        if (Behavior.WelcomeText is null) Behavior.WelcomeText = "Welcome Back";
        if (Behavior.WelcomeText.Length > 120) Behavior.WelcomeText = Behavior.WelcomeText.Substring(0, 120);

        Hotkeys.ToggleOverlay = (Hotkeys.ToggleOverlay ?? "").Trim();
        Hotkeys.OpenSettings = NormalizeHotkey(Hotkeys.OpenSettings, "F10");
        Hotkeys.ToggleLock = NormalizeHotkey(Hotkeys.ToggleLock, "F8");
        Hotkeys.NormalizeCardActions();

        // 命令行工具 / 工作目录不再全局保存：它们属于每张「命令行」卡片（CardSettings.Extras 里的 command），
        // 这样同时放 cmd、pwsh、自定义工具的多张命令行卡片可以各用各的。

        if (!IsHexColor(Style.Foreground))
        {
            // P1-3：静默回落会让用户不知道为什么前景色没变成设置里写的值，必须留日志。
            Log.Warn($"样式 foreground 不合法（\"{(Style.Foreground ?? "")}\"），已回落默认 #E6EDF3（只接受 7 位 #RRGGBB）");
            Style.Foreground = "#E6EDF3";
        }
        if (!IsHexColor(Style.Background))
        {
            Log.Warn($"样式 background 不合法（\"{(Style.Background ?? "")}\"），已回落默认 #0D1117（只接受 7 位 #RRGGBB，透明度请用 backgroundOpacity）");
            Style.Background = "#0D1117";
        }

        // 外观令牌：非法值回落到默认 / 派生，避免主题文件写坏后整块外观失效。
        Style.Accent = NormalizeColorToken(Style.Accent, "#2F6FEB");
        Style.Danger = NormalizeColorToken(Style.Danger, "#D14343");
        Style.Success = NormalizeColorToken(Style.Success, "");
        Style.Warning = NormalizeColorToken(Style.Warning, "");
        Style.Muted = NormalizeColorToken(Style.Muted, "");
        Style.Border = NormalizeColorToken(Style.Border, "");
        Style.Panel = NormalizeColorToken(Style.Panel, "");
        Style.Scrollbar = NormalizeColorToken(Style.Scrollbar, "");
        Style.ScrollbarHot = NormalizeColorToken(Style.ScrollbarHot, "");
        Style.MonoFont = (Style.MonoFont ?? "").Trim();
        Style.Decoration = NormalizeDecoration(Style.Decoration);
        Style.DecorationDensity = Clamp(Style.DecorationDensity, 0, 2);
        Style.ContourLevels = (int)Clamp(Style.ContourLevels, 3, 48);
        Style.ContourEvening = Clamp(Style.ContourEvening, 0, 1);
        Style.ContourRelief = Clamp(Style.ContourRelief, 0.2, 4.0);
        Style.ContourSmooth = (int)Clamp(Style.ContourSmooth, 0, 6);
        Style.ContourMinLoopArea = Clamp(Style.ContourMinLoopArea, 0, 5000);
        Style.ContourSpeed = Clamp(Style.ContourSpeed, 0.1, 4.0);
        Style.MotionProfile = NormalizeMotionProfile(Style.MotionProfile);
        Style.LoopEffect = NormalizeLoopEffect(Style.LoopEffect, Style.LoopMotion);
        Style.LoopMotion = Style.LoopEffect != "None";
        Style.LoopDurationMs = (int)Clamp(Style.LoopDurationMs, 600, 20000);
        Style.LoopAmplitude = Clamp(Style.LoopAmplitude, 0.02, 0.8);
        Style.LoopPulseThickness = (int)Clamp(Style.LoopPulseThickness, 1, 8);
        Style.LoopScanlineThickness = (int)Clamp(Style.LoopScanlineThickness, 1, 10);
        Style.ThemeName = (Style.ThemeName ?? "").Trim();
        if (Style.Endfield is null) Style.Endfield = new EndfieldStyleSettings();
        Style.Endfield.Normalize();

        // 卡片列表的结构由 CardModel.Normalize 处理（需要卡片注册表里的元数据）。
    }

    static string NormalizeHotkey(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>颜色令牌：#RRGGBB 或 #AARRGGBB；空串合法（= 按前景派生）；其它一律回落。</summary>
    public static bool IsColorToken(string value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0) return true;

        return ColorCode.TryParse(text, out var a, out var r, out var g, out var b);
    }

    static string NormalizeColorToken(string value, string fallback)
    {
        string text = (value ?? "").Trim();
        return IsColorToken(text) ? text : fallback;
    }


    /// <summary>
    /// 文字对齐：Left / Center / Right / Justify / Card。
    /// Card = 跟随卡片对齐方式：卡片左对齐时文字左对齐，右对齐时文字右对齐，
    /// 自动对齐时按卡片在屏幕上的位置决定。未知值回落 Left。
    /// </summary>
    public static string NormalizeTextAlign(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "center":
                return "Center";
            case "right":
                return "Right";
            case "justify":
                return "Justify";
            case "card":
            case "follow":
            case "followcard":
                return "Card";
            default:
                return "Left";
        }
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

    /// <summary>
    /// M6 装饰纹样：None / Grid（网格） / Scanline（扫描线） / Hatch（45° 斜纹） /
    /// Halftone（半调网点） / Contour（等高线）。未知值回落 None，绝不因为主题写错而画不出来。
    /// </summary>
    public static string NormalizeDecoration(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "grid":
                return "Grid";
            case "scanline":
            case "scanlines":
                return "Scanline";
            case "hatch":
            case "diagonal":
                return "Hatch";
            case "halftone":
                return "Halftone";
            case "contour":
                return "Contour";
            default:
                return "None";
        }
    }

    /// <summary>主题文件 / 设置页校验用：只接受归一化后的六个值。</summary>
    public static bool IsDecoration(string value)
    {
        string text = (value ?? "").Trim();
        return text.Length == 0
            || string.Equals(text, NormalizeDecoration(text), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "None", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>M7 动效档位：Current / Cut / Industrial / Off；未知值回落 Current。</summary>
    public static string NormalizeMotionProfile(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "elegant":
                return "Current";
            case "standard":
                return "Industrial";
            case "brief":
            case "short":
                return "Cut";
            case "cut":
                return "Cut";
            case "industrial":
                return "Industrial";
            case "off":
            case "none":
                return "Off";
            default:
                return "Current";
        }
    }

    public static bool IsMotionProfile(string value)
    {
        string text = (value ?? "").Trim();
        return text.Length == 0
            || string.Equals(text, NormalizeMotionProfile(text), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Current", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>M7 循环类型：None / Breath / Scanline / Pulse。</summary>
    public static string NormalizeLoopEffect(string value, bool legacyLoopMotion)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "breath":
            case "breathe":
                return "Breath";
            case "scanline":
            case "scan":
                return "Scanline";
            case "pulse":
            case "border":
                return "Pulse";
            case "contour":
            case "drift":
                return "Contour";
            case "none":
                return "None";
            default:
                return legacyLoopMotion ? "Breath" : "None";
        }
    }

    public static bool IsLoopEffect(string value)
    {
        string text = (value ?? "").Trim();
        return text.Length == 0
            || string.Equals(text, NormalizeLoopEffect(text, false), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 毛玻璃实现方式：System（accent 亚克力底板） / DesktopFeed（DWM 合成 + 桌面取样模糊） / Compat（截屏取样）。
    /// 2.24.0 把桌面模式从 Dwm 更名为 DesktopFeed；dwm / acrylic / desktop / multiwindow 等旧值只在
    /// v6→v7 迁移兜底时出现（见 SettingsLoader），运行期统一归一为 DesktopFeed。
    /// </summary>
    public static string NormalizeGlassMode(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "desktopfeed":
            case "desktop-feed":
            case "dwm":
            case "acrylic":
            case "desktop":
            case "multiwindow":
            case "multi-window":
                return "DesktopFeed";
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
    /// 桌面取样的取图后端：Auto（DXGI 能用就用） / Gpu（强制 DXGI 桌面复制） / Cpu（强制 GDI 读屏）。
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

    /// <summary>卡片滑入滑出方向：None / Left / Right；未知值回落 None。</summary>
    public static string NormalizeCardSlide(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "left":
            case "fromleft":
            case "左":
            case "左侧":
                return "Left";
            case "right":
            case "fromright":
            case "右":
            case "右侧":
                return "Right";
            default:
                return "None";
        }
    }

    public static bool IsCardSlide(string value)
    {
        string text = (value ?? "").Trim();
        return text.Length == 0
            || string.Equals(text, NormalizeCardSlide(text), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>设置窗口主题：Dark / Light；未知值回落 Dark。</summary>
    public static string NormalizeSettingsTheme(string value) =>
        string.Equals((value ?? "").Trim(), "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

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

    /// <summary>
    /// 文字对齐：Left / Center / Right / Justify / Card。
    /// Card = 跟随卡片对齐方式（CardAlign）：显式左 / 右直接跟随，Auto 时按卡片位置决定。
    /// </summary>
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
    /// 2.26.0 起图标改用 Lucide；线条图标视觉重量比实心字形轻，保留 1.15 让图标略大一圈。
    /// 垂直方向按墨迹中心对齐正文，不再靠倍率去凑基线。
    /// </summary>
    public double IconScale = 1.15;

    /// <summary>毛玻璃背景：抓取窗口后面的桌面并模糊。背景不透明度决定玻璃厚度。</summary>
    public bool Acrylic = false;

    /// <summary>
    /// 毛玻璃模糊强度（像素，按原始分辨率算；取样图缩到 1/2 后再按 1/2 半径模糊）。
    /// 兼容实现（截屏取样）与桌面取样共用这一个值；2.24.0 起由 v6 的 AcrylicBlur / GlassBlur 合并而来。
    /// </summary>
    public int BlurRadius = 18;

    /// <summary>毛玻璃刷新帧率（帧/秒）：兼容实现与桌面取样共用，每帧读一次屏 + 重新模糊。</summary>
    public int BlurFps = 10;

    /// <summary>
    /// 毛玻璃实现方式：
    /// System = 卡片下面的独立亚克力底板窗口（DWM 合成，进程侧零开销，模糊半径由系统决定）；
    /// DesktopFeed = 桌面取样 + 自己模糊，画进卡片内容层（见 Ui.GlassFeed，可调半径 / 帧率 / 取图后端）；
    /// Compat = 截屏取样 + CPU 模糊（v2.1 的 PrintWindow 路线，可调半径 / 帧率）。
    /// 系统不支持（远程桌面 / 接口失效）时自动退回 Compat。
    /// </summary>
    public string GlassMode = "System";

    /// <summary>
    /// 桌面取样（<see cref="GlassMode"/> = DesktopFeed）取屏幕像素的后端：
    /// Auto = DXGI 桌面复制可用就用它，不可用退回 GDI 读屏；
    /// Gpu = 强制 DXGI 桌面复制（拿不到就还是退回 GDI，不会没画面）；
    /// Cpu = 强制 GDI BitBlt 读屏（兼容性最好，一轮约 18 ms）。
    /// </summary>
    public string GlassSource = "Auto";

    // ==================== 外观令牌（v2.9.0） ====================
    // 颜色为空 = 按前景色派生（派生值等于旧代码里的 alpha 常量，所以默认观感不变）。
    // 软色（Muted / Border / Panel / Scrollbar / ScrollbarHot）要半透明时写 #AARRGGBB。

    /// <summary>强调色：主操作按钮、选中态、滑块热区。</summary>
    public string Accent = "#2F6FEB";

    /// <summary>危险色：危险按钮 / 错误文字。</summary>
    public string Danger = "#D14343";

    /// <summary>
    /// 成功色（M3）：CardStatus.Success / 进度条。空 = 沿用强调色（默认观感不变）。
    /// 只建议用在「一个点 / 一条线」上，不做大面积色块。
    /// </summary>
    public string Success = "";

    /// <summary>
    /// 警告色（M3）：CardStatus.Warning。空 = 前景色 0.9（默认观感不变）。
    /// </summary>
    public string Warning = "";

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

    /// <summary>
    /// 终末地视觉语言（M2–M8）的开关与页脚模板。独立成对象是为了让每项都能单选，
    /// 而不是在渲染层散落一串布尔判断；旧配置缺这一块时按默认全开来加载。
    /// </summary>
    public EndfieldStyleSettings Endfield = new();

    /// <summary>
    /// M7：动效档位。Current = 现有 160/180ms ease-out；Cut = 更短、零位移；
    /// Industrial = 稍短、2 DIP 位移；Off = 瞬时。
    /// </summary>
    public string MotionProfile = "Current";

    /// <summary>
    /// M7 循环动效类型：None / Breath（整卡呼吸） / Scanline（横线扫描） / Pulse（描边脉冲）。
    /// 旧配置只有 LoopMotion 布尔时，true 会归一成 Breath。
    /// </summary>
    public string LoopEffect = "None";

    /// <summary>循环一次的时长（毫秒），600–20000；默认 2600。</summary>
    public int LoopDurationMs = 2600;

    /// <summary>循环幅度（叠加层最大不透明度），0.02–0.8；默认 0.10。</summary>
    public double LoopAmplitude = 0.10;

    /// <summary>Pulse 循环的描边粗细（1–8px）；只在循环效果为 Pulse 时使用。</summary>
    public int LoopPulseThickness = 2;

    /// <summary>Scanline 循环的横线粗细（1–10px）；只在循环效果为 Scanline 时使用。</summary>
    public int LoopScanlineThickness = 2;

    /// <summary>
    /// 2.31 的旧开关，仅用于兼容旧配置；读入后由 <see cref="LoopEffect"/> 派生。
    /// </summary>
    public bool LoopMotion = false;

    // ==================== 工业风格选项（2.30.0，全部默认关闭） ====================

    /// <summary>
    /// M4：读数使用等宽数字。true 时 TextCanvas 把数字串单独用 MonoFont 排版，
    /// 指标刷新时小数点 / 数字宽度不横跳。默认 false = 维持原来的正文字体。
    /// </summary>
    public bool MonoReadouts = false;

    /// <summary>
    /// M6：卡片装饰纹样。None / Grid / Scanline / Hatch / Halftone / Contour。
    /// 默认 None；只作为低透明度点缀，不参与命中测试，也不改变排版。
    /// </summary>
    public string Decoration = "None";

    /// <summary>
    /// 非等高线纹样的密度：0 = 最疏，2 = 最密；0.5 = 原来的间距。
    /// 等高线用下面五个专用参数，不受这个值影响。
    /// </summary>
    public double DecorationDensity = 0.5;

    /// <summary>等高线级数（3–48）。</summary>
    public int ContourLevels = 12;

    /// <summary>疏密均衡 0–1：0 完全按真实坡度，1 在画面上彻底摊匀。</summary>
    public double ContourEvening = 0.72;

    /// <summary>地形起伏 0.2–4.0：越大山体越大、等高线越舒展。</summary>
    public double ContourRelief = 1.0;

    /// <summary>Chaikin 平滑轮数 0–6。</summary>
    public int ContourSmooth = 2;

    /// <summary>最小闭合环面积（平方像素，0–5000）；小于它的闭环直接不画。</summary>
    public double ContourMinLoopArea = 26.0;

    /// <summary>等高线原地形变速度（0.1–4.0）；1.0 约 4 秒走完一个形变循环。</summary>
    public double ContourSpeed = 1.0;

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

    public CliSettings Clone() => new CliSettings
    {
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        EntryMaxHeight = EntryMaxHeight,
        MaxEntries = MaxEntries
    };

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
    public List<string> DiskDrives = new();
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
    /// <summary>减少动效：关闭卡片呼出 / 消失 / 补位动画（无障碍 / 远程桌面偏好）。</summary>
    public bool ReduceMotion = false;

    /// <summary>
    /// 显示 / 隐藏全部时是否播放整体动效。关闭后全局切换瞬时完成；
    /// 单张非常驻卡的呼出 / 收起仍按自己的动效设置播放。
    /// </summary>
    public bool OverallMotion = true;

    /// <summary>卡片滑入 / 滑出方向：None / Left / Right；None = 沿用原来的上下位移。</summary>
    public string CardSlide = "None";

    /// <summary>设置窗口明暗主题：Dark / Light；只影响设置窗口，不影响桌面叠层。</summary>
    public string SettingsTheme = "Dark";
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
    public List<CardActionHotkey> CardActions = new();

    public string GetCardAction(string action)
    {
        if (CardActions is null || string.IsNullOrWhiteSpace(action)) return "";

        foreach (CardActionHotkey entry in CardActions)
        {
            if (entry is not null && string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase))
                return entry.Hotkey ?? "";
        }

        return "";
    }

    public void SetCardAction(string action, string hotkey)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        if (CardActions is null) CardActions = new List<CardActionHotkey>();

        foreach (CardActionHotkey entry in CardActions)
        {
            if (entry is not null && string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase))
            {
                entry.Hotkey = hotkey ?? "";
                return;
            }
        }

        CardActions.Add(new CardActionHotkey { Action = action, Hotkey = hotkey ?? "" });
    }

    public void NormalizeCardActions()
    {
        if (CardActions is null)
        {
            CardActions = new List<CardActionHotkey>();
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<CardActionHotkey>();

        foreach (CardActionHotkey entry in CardActions)
        {
            if (entry is null) continue;

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

/// <summary>卡片配置（宿主通用模型；插件卡片同样使用）。</summary>
public sealed class CardSettings
{
    /// <summary>稳定标识，删除 / 重排都不变，热键动作也挂在它上面。</summary>
    public string Id = "";
    /// <summary>卡片种类（CardRegistry 里的 Kind）。</summary>
    public string Kind = "infotext";
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
    public Dictionary<string, object> Extras = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 读模块自己的设置；缺省或解析失败都回落到 <typeparamref name="T"/> 的默认值，调用方不用判空。
    /// 从磁盘读进来时值是原始 JSON 节点，这里做一次强类型绑定；返回的可能是副本，
    /// 改完要用 <see cref="SetExtras{T}"/> 写回。
    /// </summary>
    public T ExtrasAs<T>(string key) where T : class, new()
    {
        object raw = null;
        if (Extras is not null && !string.IsNullOrEmpty(key)) Extras.TryGetValue(key, out raw);
        if (raw is null) return new T();

        var typed = raw as T;
        if (typed is not null) return typed;

        try
        {
            T parsed = Json.BindNode<T>(raw);
            return parsed ?? new T();
        }
        catch (Exception ex)
        {
            Log.Warn($"卡片设置 {key} 解析失败: {ex.Message}");
            return new T();
        }
    }

    /// <summary>写回模块自己的设置；传 null 等于删掉这一项，回到模块默认值。</summary>
    public void SetExtras<T>(string key, T value) where T : class
    {
        if (string.IsNullOrEmpty(key)) return;
        if (Extras is null) Extras = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (value is null) Extras.Remove(key);
        else Extras[key] = value;
    }
}
