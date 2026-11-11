using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MirrorpondHUD.Core;

/// <summary>
/// 软色令牌（Muted / Border / Panel / Scrollbar / ScrollbarHot）在主题文件里的两种写法：
/// <list type="bullet">
/// <item>数字 0~1：按**前景色**派生透明度（示例主题 `docs/themes/*.mptheme` 的写法）；</item>
/// <item>字符串：直接写 <c>#RRGGBB</c> / <c>#AARRGGBB</c>（可以偏离前景色，浅色主题常用）。</item>
/// </list>
/// 两者都不写 = 保持当前值（增量套用）。
/// </summary>
public struct ThemeSoftColor
{
    public double? Alpha;
    public string Color;

    public bool IsSet => Alpha.HasValue || !string.IsNullOrEmpty(Color);
}

public sealed class ThemeColors
{
    public string Foreground;
    public string Background;
    public double? BackgroundOpacity;
    public string Accent;
    public string Danger;
    /// <summary>M3：空 / 缺省 = 沿用 Accent。</summary>
    public string Success;
    /// <summary>M3：空 / 缺省 = 前景 0.9。</summary>
    public string Warning;
    public ThemeSoftColor Muted;
    public ThemeSoftColor Border;
    public ThemeSoftColor Panel;
    public ThemeSoftColor Scrollbar;
    public ThemeSoftColor ScrollbarHot;
}

public sealed class ThemeTypography
{
    public string FontFamily;
    public string MonoFont;
    public double? FontSize;
    public bool? Bold;
    public double? IconScale;
    public double? LineHeight;
    public string TextAlign;
}

public sealed class ThemeShape
{
    public double? CornerRadius;
    public double? Padding;
    public bool? Shadow;
}

public sealed class ThemeOutline
{
    public bool? Enabled;
    public string Color;
    public double? Thickness;
}

public sealed class ThemeCliPanel
{
    public double? MaxWidth;
    public double? MaxHeight;
    public double? EntryMaxHeight;
    public int? MaxEntries;
}

/// <summary>
/// M7：动效数据。缺省不改；档位是有界枚举，主题不能写任意曲线。
/// </summary>
public sealed class ThemeMotion
{
    public string Profile;
    public bool? Loop;
    public string Effect;
    public double? DurationMs;
    public double? Amplitude;
    /// <summary>Contour 原地形变速度（0.1–4.0）。</summary>
    public double? Speed;
}

/// <summary>
/// M4–M6 的工业风格选项。全部可选；缺省 = 关（保持默认观感）。
/// </summary>
public sealed class ThemeIndustrial
{
    /// <summary>M4：读数使用等宽数字。</summary>
    public bool? MonoReadouts;
    /// <summary>M6：None / Grid / Scanline / Hatch / Halftone / Contour。</summary>
    public string Decoration;
    public double? DecorationDensity;
    public double? ContourLevels;
    public double? ContourEvening;
    public double? ContourRelief;
    public double? ContourSmooth;
    public double? ContourMinLoopArea;
}

/// <summary>
/// 一份 `.mptheme` 的内容。字段为 null = 文件里没写，套用时保留当前值（增量套用）；
/// 只有 <see cref="ThemeFile.Export(MirrorpondHUD.Core.Settings, string)"/> 会写出全部已知字段。
/// </summary>
public sealed class ThemeDocument
{
    public int Schema = ThemeFile.SupportedSchema;
    public string Name = "";
    public string Author = "";
    public string Description = "";

    public ThemeColors Colors = new();
    public ThemeTypography Typography = new();
    public ThemeShape Shape = new();
    public ThemeOutline Outline = new();
    public ThemeCliPanel CliPanel = new();
    public ThemeIndustrial Industrial = new();
    public ThemeMotion Motion = new();

    /// <summary>
    /// 把主题写进设置（只覆盖主题里出现过的字段）。调用方**必须**先克隆 Settings，
    /// 再对克隆调用本方法；校验失败时原设置不会被碰。
    /// </summary>
    public void ApplyTo(Settings settings)
    {
        if (settings is null) return;

        StyleSettings style = settings.Style ?? (settings.Style = new StyleSettings());

        // 1) 基础颜色先落，软色的「按前景派生」要用到新的前景色。
        if (Colors.Foreground is not null) style.Foreground = Colors.Foreground;
        if (Colors.Background is not null) style.Background = Colors.Background;
        if (Colors.BackgroundOpacity.HasValue) style.BackgroundOpacity = Colors.BackgroundOpacity.Value;
        if (Colors.Accent is not null) style.Accent = Colors.Accent;
        if (Colors.Danger is not null) style.Danger = Colors.Danger;
        if (Colors.Success is not null) style.Success = Colors.Success;
        if (Colors.Warning is not null) style.Warning = Colors.Warning;

        bool hasForeground = ColorCode.TryParse(style.Foreground, out byte a, out byte r, out byte g, out byte b);
        ApplySoft(Colors.Muted, hasForeground, a, r, g, b, ref style.Muted);
        ApplySoft(Colors.Border, hasForeground, a, r, g, b, ref style.Border);
        ApplySoft(Colors.Panel, hasForeground, a, r, g, b, ref style.Panel);
        ApplySoft(Colors.Scrollbar, hasForeground, a, r, g, b, ref style.Scrollbar);
        ApplySoft(Colors.ScrollbarHot, hasForeground, a, r, g, b, ref style.ScrollbarHot);

        if (Typography.FontFamily is not null) style.FontFamily = Typography.FontFamily;
        if (Typography.MonoFont is not null) style.MonoFont = Typography.MonoFont;
        if (Typography.FontSize.HasValue) style.FontSize = Typography.FontSize.Value;
        if (Typography.Bold.HasValue) style.Bold = Typography.Bold.Value;
        if (Typography.IconScale.HasValue) style.IconScale = Typography.IconScale.Value;
        if (Typography.LineHeight.HasValue) style.LineHeight = Typography.LineHeight.Value;
        if (Typography.TextAlign is not null) style.TextAlignment = Typography.TextAlign;

        if (Industrial.MonoReadouts.HasValue) style.MonoReadouts = Industrial.MonoReadouts.Value;
        if (Industrial.Decoration is not null) style.Decoration = Settings.NormalizeDecoration(Industrial.Decoration);
        if (Industrial.DecorationDensity.HasValue) style.DecorationDensity = Industrial.DecorationDensity.Value;
        if (Industrial.ContourLevels.HasValue) style.ContourLevels = (int)Industrial.ContourLevels.Value;
        if (Industrial.ContourEvening.HasValue) style.ContourEvening = Industrial.ContourEvening.Value;
        if (Industrial.ContourRelief.HasValue) style.ContourRelief = Industrial.ContourRelief.Value;
        if (Industrial.ContourSmooth.HasValue) style.ContourSmooth = (int)Industrial.ContourSmooth.Value;
        if (Industrial.ContourMinLoopArea.HasValue) style.ContourMinLoopArea = Industrial.ContourMinLoopArea.Value;

        if (Motion.Profile is not null) style.MotionProfile = Settings.NormalizeMotionProfile(Motion.Profile);
        if (Motion.Effect is not null)
            style.LoopEffect = Settings.NormalizeLoopEffect(Motion.Effect, false);
        else if (Motion.Loop.HasValue)
            style.LoopEffect = Motion.Loop.Value ? "Breath" : "None";
        if (Motion.DurationMs.HasValue) style.LoopDurationMs = (int)Motion.DurationMs.Value;
        if (Motion.Amplitude.HasValue) style.LoopAmplitude = Motion.Amplitude.Value;
        if (Motion.Speed.HasValue) style.ContourSpeed = Motion.Speed.Value;

        if (Shape.Padding.HasValue) style.Padding = Shape.Padding.Value;

        OverlaySettings overlay = settings.Overlay ?? (settings.Overlay = new OverlaySettings());
        if (Shape.CornerRadius.HasValue) overlay.CornerRadius = Shape.CornerRadius.Value;
        if (Shape.Shadow.HasValue) overlay.Shadow = Shape.Shadow.Value;

        if (Outline.Enabled.HasValue) style.TextOutline = Outline.Enabled.Value;
        if (Outline.Color is not null) style.OutlineColor = Outline.Color;
        if (Outline.Thickness.HasValue) style.OutlineThickness = Outline.Thickness.Value;

        CliSettings cli = settings.Cli ?? (settings.Cli = new CliSettings());
        if (CliPanel.MaxWidth.HasValue) cli.MaxWidth = CliPanel.MaxWidth.Value;
        if (CliPanel.MaxHeight.HasValue) cli.MaxHeight = CliPanel.MaxHeight.Value;
        if (CliPanel.EntryMaxHeight.HasValue) cli.EntryMaxHeight = CliPanel.EntryMaxHeight.Value;
        if (CliPanel.MaxEntries.HasValue) cli.MaxEntries = CliPanel.MaxEntries.Value;
    }

    static void ApplySoft(ThemeSoftColor soft, bool hasForeground, int a, int r, int g, int b, ref string target)
    {
        if (!soft.IsSet) return;

        if (!string.IsNullOrEmpty(soft.Color))
        {
            target = soft.Color;
            return;
        }

        if (soft.Alpha.HasValue && hasForeground)
            target = ThemeFile.AlphaToken(a, r, g, b, soft.Alpha.Value);
    }
}

/// <summary>
/// `.mptheme` 的读写：解析 + 校验 + 套用到副本 + 导出。
/// 格式见独立设计文档的格式章节；与 `settings.json` 的 SchemaVersion 无关。
/// 只认识外观字段，卡片 / 热键 / 毛玻璃 / 位置一律不碰（导入只往 Style / Overlay / Cli 写值）。
/// </summary>
public static class ThemeFile
{
    public const int SupportedSchema = 1;

    /// <summary>
    /// 导入主题文件的大小上限（1 MB）。超过直接拒绝：巨型文件 / 命名管道这类“读不完”的输入
    /// 不应该被整个读进内存，更不应该拖住调用线程。
    /// </summary>
    public const int MaxImportBytes = 1024 * 1024;

    /// <summary>
    /// 读取并套用到 source 的副本。失败时 applied 为 null，source 原样不动。
    /// 文件超过 <see cref="MaxImportBytes"/> 会被明确拒绝；读取本身有界，不会接受无限流。
    /// </summary>
    public static bool TryImport(string path, Settings source, out Settings applied, out string error)
    {
        if (TryImportCore(path, source, out applied, out string stage, out error))
            return true;

        // 失败必须可见（AGENTS §5）：设置窗只会显示一句 error，事后排障只能靠这条日志。
        // 只记路径、失败阶段与错误串，不记主题文件内容（可能含用户自定义文本）。
        Log.Warn($"主题导入失败  文件={(path ?? "(空)")}  阶段={stage}  原因={error}");
        return false;
    }

    /// <summary>导入的实际流程；stage 供公开入口在失败时写日志，error 文案与旧实现逐字一致。</summary>
    static bool TryImportCore(string path, Settings source, out Settings applied,
                              out string stage, out string error)
    {
        applied = null;
        error = null;
        stage = "读取主题文件";

        if (!TryReadTextBounded(path, out string json, out string readError))
        {
            error = readError;
            return false;
        }

        stage = "解析主题文件";
        if (!TryParse(json, out ThemeDocument document, out string parseError))
        {
            error = parseError;
            return false;
        }

        stage = "复制当前设置";
        Settings duplicate;
        try
        {
            duplicate = Json.Deserialize<Settings>(Json.Serialize(source ?? new Settings()));
        }
        catch (Exception ex)
        {
            // 克隆失败就报错，绝不退回「全默认值」——那会把当前外观悄悄换成出厂设置。
            error = $"复制当前设置失败：{ex.Message}";
            return false;
        }

        stage = "套用主题";
        try
        {
            document.ApplyTo(duplicate);
            duplicate.Normalize();
        }
        catch (Exception ex)
        {
            error = $"套用主题失败：{ex.Message}";
            return false;
        }

        applied = duplicate;
        return true;
    }

    /// <summary>
    /// 有界读取主题文本：命名管道 / 设备路径直接拒绝（它们可能永远读不完）；普通文件先按
    /// <see cref="FileInfo.Length"/> 快速拒绝超大文件，再按字节数上限读取兜底。
    /// </summary>
    static bool TryReadTextBounded(string path, out string text, out string error)
    {
        text = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "读取主题文件失败：路径为空";
            return false;
        }

        // 命名管道 / 设备路径不是主题文件，读操作可能永远不返回：直接拒绝。
        // （设置页已经把读取放到后台线程，但后台线程也不该被无限流挂住。）
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            error = "读取主题文件失败：不支持命名管道 / 设备路径";
            return false;
        }

        // 普通文件先看大小，避免白读 1 MB 才拒绝。长度探测只是「提前拒绝」的快速路径：
        // 目录 / 网络路径 / 权限问题在这里失败时，下面的有界读取仍会给出真实错误，
        // 所以这里只留 DEBUG 级痕迹，不阻断导入。
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxImportBytes)
            {
                error = $"主题文件过大：{info.Length} 字节，超过上限 {MaxImportBytes} 字节（1 MB）";
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"主题文件长度探测失败，继续有界读取: {ex.Message}");
        }

        try
        {
            byte[] buffer = new byte[81920];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete))
            using (var memory = new MemoryStream())
            {
                long total = 0;
                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    total += read;
                    if (total > MaxImportBytes)
                    {
                        error = $"主题文件过大：超过上限 {MaxImportBytes} 字节（1 MB），已拒绝导入";
                        return false;
                    }

                    memory.Write(buffer, 0, read);
                }

                text = DecodeThemeText(memory.ToArray());
                return true;
            }
        }
        catch (Exception ex)
        {
            error = $"读取主题文件失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>与 File.ReadAllText 的编码行为对齐：按 BOM 识别 UTF-8 / UTF-16，其余按 UTF-8。</summary>
    static string DecodeThemeText(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return "";

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // UTF-8 BOM 不解掉，留给 TryParse 统一去（它本来就处理 \uFEFF）。
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>把当前外观写成 `.mptheme`（UTF-8 无 BOM）。失败返回 false。</summary>
    public static bool TryExport(Settings settings, string path, out string error)
    {
        if (TryExportCore(settings, path, out string stage, out error))
            return true;

        // 失败必须可见（AGENTS §5）：设置窗只显示 error，排障时要靠日志区分
        // 「生成主题内容失败」与「写出主题文件失败」。
        Log.Warn($"主题导出失败  文件={(path ?? "(空)")}  阶段={stage}  原因={error}");
        return false;
    }

    /// <summary>导出的实际流程；拆成两步只为在失败日志里区分阶段，error 文案与旧实现一致。</summary>
    static bool TryExportCore(Settings settings, string path, out string stage, out string error)
    {
        error = null;
        stage = "生成主题内容";
        try
        {
            string text = Export(settings);
            stage = "写出主题文件";
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            error = $"写出主题文件失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 导出当前外观。字段顺序固定，便于 diff；空的软色令牌不写（导入时继续按前景派生）。
    /// </summary>
    public static string Export(Settings settings, string name = null)
    {
        Settings s = settings ?? new Settings();
        StyleSettings style = s.Style ?? new StyleSettings();
        OverlaySettings overlay = s.Overlay ?? new OverlaySettings();
        CliSettings cli = s.Cli ?? new CliSettings();

        var root = new Dictionary<string, object>();
        root["schema"] = SupportedSchema;
        root["name"] = string.IsNullOrWhiteSpace(name) ? "自定义外观" : name;
        root["author"] = "MirrorpondHUD";
        root["description"] = "由当前外观导出，可直接编辑后分享";

        var colors = new Dictionary<string, object>();
        colors["foreground"] = style.Foreground;
        colors["background"] = style.Background;
        colors["backgroundOpacity"] = style.BackgroundOpacity;
        colors["accent"] = style.Accent;
        colors["danger"] = style.Danger;
        // M3：空串是合法配置（= 沿用派生值），但主题文件里不写空颜色，避免解析器把它当非法。
        if (!string.IsNullOrWhiteSpace(style.Success)) colors["success"] = style.Success;
        if (!string.IsNullOrWhiteSpace(style.Warning)) colors["warning"] = style.Warning;
        AddSoft(colors, "muted", style.Muted);
        AddSoft(colors, "border", style.Border);
        AddSoft(colors, "panel", style.Panel);
        AddSoft(colors, "scrollbar", style.Scrollbar);
        AddSoft(colors, "scrollbarHot", style.ScrollbarHot);

        var typography = new Dictionary<string, object>();
        typography["fontFamily"] = style.FontFamily;
        typography["monoFont"] = style.MonoFont;
        typography["fontSize"] = style.FontSize;
        typography["bold"] = style.Bold;
        typography["iconScale"] = style.IconScale;
        typography["lineHeight"] = style.LineHeight;
        typography["textAlign"] = style.TextAlignment;

        var shape = new Dictionary<string, object>();
        shape["cornerRadius"] = overlay.CornerRadius;
        shape["padding"] = style.Padding;
        shape["shadow"] = overlay.Shadow;

        var outline = new Dictionary<string, object>();
        outline["enabled"] = style.TextOutline;
        outline["color"] = style.OutlineColor;
        outline["thickness"] = style.OutlineThickness;

        var cliPanel = new Dictionary<string, object>();
        cliPanel["maxWidth"] = cli.MaxWidth;
        cliPanel["maxHeight"] = cli.MaxHeight;
        cliPanel["entryMaxHeight"] = cli.EntryMaxHeight;
        cliPanel["maxEntries"] = cli.MaxEntries;

        var industrial = new Dictionary<string, object>();
        industrial["monoReadouts"] = style.MonoReadouts;
        industrial["decoration"] = Settings.NormalizeDecoration(style.Decoration);
        industrial["decorationDensity"] = style.DecorationDensity;
        industrial["contourLevels"] = style.ContourLevels;
        industrial["contourEvening"] = style.ContourEvening;
        industrial["contourRelief"] = style.ContourRelief;
        industrial["contourSmooth"] = style.ContourSmooth;
        industrial["contourMinLoopArea"] = style.ContourMinLoopArea;

        var motion = new Dictionary<string, object>();
        motion["profile"] = Settings.NormalizeMotionProfile(style.MotionProfile);
        motion["effect"] = Settings.NormalizeLoopEffect(style.LoopEffect, style.LoopMotion);
        motion["loop"] = style.LoopEffect != "None";
        motion["durationMs"] = style.LoopDurationMs;
        motion["amplitude"] = style.LoopAmplitude;
        motion["speed"] = style.ContourSpeed;

        var appearance = new Dictionary<string, object>();
        appearance["colors"] = colors;
        appearance["typography"] = typography;
        appearance["shape"] = shape;
        appearance["outline"] = outline;
        appearance["cliPanel"] = cliPanel;
        appearance["industrial"] = industrial;
        appearance["motion"] = motion;

        root["appearance"] = appearance;

        return Json.Serialize(root);
    }

    static void AddSoft(Dictionary<string, object> map, string key, string value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0) return;   // 空 = 派生，不写进文件
        if (!Settings.IsColorToken(text)) return;
        map[key] = text;
    }

    /// <summary>
    /// 解析 + 校验。未知键忽略；schema 高于 <see cref="SupportedSchema"/> 直接拒绝；
    /// 颜色 / 数值越界 / 文本对齐非法都返回可读原因。
    /// </summary>
    public static bool TryParse(string json, out ThemeDocument document, out string error)
    {
        document = null;
        error = null;

        if (json is not null && json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "主题文件为空";
            return false;
        }

        object node;
        try
        {
            node = Json.Parse(json);
        }
        catch (Exception ex)
        {
            error = $"JSON 解析失败：{ex.Message}";
            return false;
        }

        var rootMap = node as Dictionary<string, object>;
        if (rootMap is null)
        {
            error = "主题文件根节点必须是对象";
            return false;
        }

        var root = new ThemeObject(rootMap, "");
        var doc = new ThemeDocument();

        // schema / formatVersion：缺省按 1 处理（兼容旧草案），只支持 1。
        int? schema = null;
        if (root.Has("schema")) schema = root.Int("schema");
        else if (root.Has("formatVersion")) schema = root.Int("formatVersion");
        if (root.Error is not null)
        {
            error = root.Error;
            return false;
        }
        if (schema.HasValue)
        {
            if (schema.Value < 1)
            {
                error = "schema 必须是正整数";
                return false;
            }
            if (schema.Value > SupportedSchema)
            {
                error = $"主题文件版本 {schema.Value} 高于当前支持的 {SupportedSchema}，请升级 MirrorpondHUD";
                return false;
            }
            doc.Schema = schema.Value;
        }

        doc.Name = root.String("name") ?? "";
        doc.Author = root.String("author") ?? "";
        doc.Description = root.String("description") ?? "";

        var appearance = root.Child("appearance");
        if (root.Error is not null)
        {
            error = root.Error;
            return false;
        }
        if (appearance is null)
        {
            error = "缺少 appearance 对象";
            return false;
        }

        var colors = appearance.Child("colors");
        if (colors is not null)
        {
            doc.Colors.Foreground = colors.ColorStrict("foreground");
            doc.Colors.Background = colors.ColorStrict("background");
            doc.Colors.BackgroundOpacity = colors.Number("backgroundOpacity");
            colors.Range(doc.Colors.BackgroundOpacity, "backgroundOpacity", 0, 1);
            doc.Colors.Accent = colors.Color("accent");
            doc.Colors.Danger = colors.Color("danger");
            doc.Colors.Success = colors.Color("success");
            doc.Colors.Warning = colors.Color("warning");

            ThemeSoftColor? soft = colors.Soft("muted");
            if (soft.HasValue) doc.Colors.Muted = soft.Value;
            soft = colors.Soft("border");
            if (soft.HasValue) doc.Colors.Border = soft.Value;
            soft = colors.Soft("panel");
            if (soft.HasValue) doc.Colors.Panel = soft.Value;
            soft = colors.Soft("scrollbar");
            if (soft.HasValue) doc.Colors.Scrollbar = soft.Value;
            soft = colors.Soft("scrollbarHot");
            if (soft.HasValue) doc.Colors.ScrollbarHot = soft.Value;
        }

        var typography = appearance.Child("typography");
        if (typography is not null)
        {
            doc.Typography.FontFamily = typography.String("fontFamily");
            doc.Typography.MonoFont = typography.String("monoFont");
            doc.Typography.FontSize = typography.Number("fontSize");
            typography.Range(doc.Typography.FontSize, "fontSize", 6, 96);
            doc.Typography.Bold = typography.Bool("bold");
            doc.Typography.IconScale = typography.Number("iconScale");
            typography.Range(doc.Typography.IconScale, "iconScale", 0.5, 3);
            doc.Typography.LineHeight = typography.Number("lineHeight");
            typography.Range(doc.Typography.LineHeight, "lineHeight", 0, 120);

            string align = typography.String("textAlign");
            if (align is not null)
            {
                doc.Typography.TextAlign = CanonicalAlign(align);
                if (doc.Typography.TextAlign is null)
                    typography.Fail("textAlign", "必须是 Left / Center / Right / Justify / Card");
            }
        }

        var shape = appearance.Child("shape");
        if (shape is not null)
        {
            doc.Shape.CornerRadius = shape.Number("cornerRadius");
            shape.Range(doc.Shape.CornerRadius, "cornerRadius", 0, 60);
            doc.Shape.Padding = shape.Number("padding");
            shape.Range(doc.Shape.Padding, "padding", 0, 80);
            doc.Shape.Shadow = shape.Bool("shadow");
        }

        var outline = appearance.Child("outline");
        if (outline is not null)
        {
            doc.Outline.Enabled = outline.Bool("enabled");
            doc.Outline.Color = outline.Color("color");
            doc.Outline.Thickness = outline.Number("thickness");
            outline.Range(doc.Outline.Thickness, "thickness", 0.2, 10);
        }

        var cliPanel = appearance.Child("cliPanel");
        if (cliPanel is not null)
        {
            doc.CliPanel.MaxWidth = cliPanel.Number("maxWidth");
            cliPanel.Range(doc.CliPanel.MaxWidth, "maxWidth", 240, 1600);
            doc.CliPanel.MaxHeight = cliPanel.Number("maxHeight");
            cliPanel.Range(doc.CliPanel.MaxHeight, "maxHeight", 140, 3000);
            doc.CliPanel.EntryMaxHeight = cliPanel.Number("entryMaxHeight");
            cliPanel.Range(doc.CliPanel.EntryMaxHeight, "entryMaxHeight", 60, 2000);
            doc.CliPanel.MaxEntries = cliPanel.Int("maxEntries");
            if (doc.CliPanel.MaxEntries.HasValue)
                cliPanel.Range(doc.CliPanel.MaxEntries.Value, "maxEntries", 1, 100);
        }

        var industrial = appearance.Child("industrial");
        if (industrial is not null)
        {
            doc.Industrial.MonoReadouts = industrial.Bool("monoReadouts");

            string decoration = industrial.String("decoration");
            if (decoration is not null)
            {
                if (!Settings.IsDecoration(decoration))
                {
                    industrial.Fail("decoration",
                        "必须是 None / Grid / Scanline / Hatch / Halftone / Contour");
                }
                else
                {
                    doc.Industrial.Decoration = Settings.NormalizeDecoration(decoration);
                }
            }

            doc.Industrial.DecorationDensity = industrial.Number("decorationDensity");
            industrial.Range(doc.Industrial.DecorationDensity, "decorationDensity", 0, 1);
            doc.Industrial.ContourLevels = industrial.Number("contourLevels");
            industrial.Range(doc.Industrial.ContourLevels, "contourLevels", 4, 30);
            doc.Industrial.ContourEvening = industrial.Number("contourEvening");
            industrial.Range(doc.Industrial.ContourEvening, "contourEvening", 0, 1);
            doc.Industrial.ContourRelief = industrial.Number("contourRelief");
            industrial.Range(doc.Industrial.ContourRelief, "contourRelief", 0.5, 1.8);
            doc.Industrial.ContourSmooth = industrial.Number("contourSmooth");
            industrial.Range(doc.Industrial.ContourSmooth, "contourSmooth", 0, 3);
            doc.Industrial.ContourMinLoopArea = industrial.Number("contourMinLoopArea");
            industrial.Range(doc.Industrial.ContourMinLoopArea, "contourMinLoopArea", 0, 400);
        }

        var motion = appearance.Child("motion");
        if (motion is not null)
        {
            string profile = motion.String("profile");
            if (profile is not null)
            {
                if (!Settings.IsMotionProfile(profile))
                {
                    motion.Fail("profile", "必须是 Current / Cut / Industrial / Off");
                }
                else
                {
                    doc.Motion.Profile = Settings.NormalizeMotionProfile(profile);
                }
            }
            doc.Motion.Loop = motion.Bool("loop");

            string effect = motion.String("effect");
            if (effect is not null)
            {
                if (!Settings.IsLoopEffect(effect))
                {
                    motion.Fail("effect", "必须是 None / Breath / Scanline / Pulse");
                }
                else
                {
                    doc.Motion.Effect = Settings.NormalizeLoopEffect(effect, false);
                }
            }

            doc.Motion.DurationMs = motion.Number("durationMs");
            motion.Range(doc.Motion.DurationMs, "durationMs", 600, 20000);
            doc.Motion.Amplitude = motion.Number("amplitude");
            motion.Range(doc.Motion.Amplitude, "amplitude", 0.02, 0.8);
            doc.Motion.Speed = motion.Number("speed");
            motion.Range(doc.Motion.Speed, "speed", 0.1, 4.0);
        }

        string first = root.Error
            ?? appearance.Error
            ?? (colors is not null ? colors.Error : null)
            ?? (typography is not null ? typography.Error : null)
            ?? (shape is not null ? shape.Error : null)
            ?? (outline is not null ? outline.Error : null)
            ?? (cliPanel is not null ? cliPanel.Error : null)
            ?? (industrial is not null ? industrial.Error : null)
            ?? (motion is not null ? motion.Error : null);

        if (first is not null)
        {
            error = first;
            return false;
        }

        document = doc;
        return true;
    }

    static string CanonicalAlign(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "left": return "Left";
            case "center": return "Center";
            case "right": return "Right";
            case "justify": return "Justify";
            case "card":
            case "follow":
            case "followcard": return "Card";
            default: return null;
        }
    }

    internal static string AlphaToken(int a, int r, int g, int b, double alpha)
    {
        int value = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255.0);
        return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", value, r, g, b);
    }

    /// <summary>逐个键读取并记录第一个错误；错误信息带 JSON 路径（appearance.colors.muted 之类）。</summary>
    sealed class ThemeObject
    {
        readonly Dictionary<string, object> _map;
        readonly string _path;
        public string Error;

        public ThemeObject(Dictionary<string, object> map, string path)
        {
            _map = map;
            _path = path;
        }

        public bool Has(string key) => _map.TryGetValue(key, out object node) && node is not null;

        public ThemeObject Child(string key)
        {
            if (!_map.TryGetValue(key, out object node) || node is null) return null;

            var map = node as Dictionary<string, object>;
            if (map is null)
            {
                Fail(key, "必须是对象");
                return null;
            }
            return new ThemeObject(map, Join(key));
        }

        public string String(string key)
        {
            if (!_map.TryGetValue(key, out object node) || node is null) return null;
            if (!(node is string))
            {
                Fail(key, "必须是字符串");
                return null;
            }
            return ((string)node).Trim();
        }

        public double? Number(string key)
        {
            if (!_map.TryGetValue(key, out object node) || node is null) return null;
            if (!(node is double))
            {
                Fail(key, "必须是数字");
                return null;
            }
            return (double)node;
        }

        public bool? Bool(string key)
        {
            if (!_map.TryGetValue(key, out object node) || node is null) return null;
            if (!(node is bool))
            {
                Fail(key, "必须是 true / false");
                return null;
            }
            return (bool)node;
        }

        public int? Int(string key)
        {
            double? number = Number(key);
            if (!number.HasValue) return null;
            if (number.Value != Math.Floor(number.Value))
            {
                Fail(key, "必须是整数");
                return null;
            }
            return (int)number.Value;
        }

        public string Color(string key)
        {
            string text = String(key);
            if (text is null) return null;
            if (text.Length == 0 || !Settings.IsColorToken(text))
            {
                Fail(key, "必须是 #RRGGBB 或 #AARRGGBB");
                return null;
            }
            return text;
        }

        /// <summary>
        /// 严格 #RRGGBB（只接受 7 位）：foreground / background 专用。
        /// 带透明度的 #AARRGGBB 在前景色上会让文字发虚，背景的透明度必须走
        /// appearance.backgroundOpacity 表达，所以这里直接拒绝并给出明确错误。
        /// </summary>
        public string ColorStrict(string key)
        {
            string text = String(key);
            if (text is null) return null;

            if (text.Length == 9 && text[0] == '#')
            {
                Fail(key, "必须是 7 位 #RRGGBB（#AARRGGBB 的透明度写法在这里不允许，背景透明度请用 appearance.backgroundOpacity）");
                return null;
            }
            if (text.Length == 0 || !Settings.IsHexColor(text))
            {
                Fail(key, "必须是 7 位 #RRGGBB，例如 #E6EDF3");
                return null;
            }
            return text;
        }

        public ThemeSoftColor? Soft(string key)
        {
            if (!_map.TryGetValue(key, out object node) || node is null) return null;

            if (node is string)
            {
                string text = ((string)node).Trim();
                if (text.Length == 0 || !Settings.IsColorToken(text))
                {
                    Fail(key, "必须是 #RRGGBB / #AARRGGBB 或 0 ~ 1 的透明度");
                    return null;
                }
                return new ThemeSoftColor { Color = text };
            }

            if (node is double)
            {
                double alpha = (double)node;
                if (alpha < 0 || alpha > 1)
                {
                    Fail(key, "透明度必须在 0 ~ 1 之间");
                    return null;
                }
                return new ThemeSoftColor { Alpha = alpha };
            }

            Fail(key, "必须是颜色或 0 ~ 1 的透明度");
            return null;
        }

        public bool Range(double? value, string key, double min, double max)
        {
            if (!value.HasValue) return true;
            if (value.Value < min || value.Value > max)
            {
                Fail(key, string.Format(CultureInfo.InvariantCulture,
                    "必须在 {0} ~ {1} 之间（当前 {2}）", min, max, value.Value));
                return false;
            }
            return true;
        }

        public void Fail(string key, string message)
        {
            if (Error is null) Error = $"{Join(key)} {message}";
        }

        string Join(string key) => _path.Length == 0 ? key : $"{_path}.{key}";
    }
}
