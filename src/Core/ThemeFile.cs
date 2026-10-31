using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MirrorpondHUD.Core
{
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

        public bool IsSet { get { return Alpha.HasValue || !string.IsNullOrEmpty(Color); } }
    }

    public sealed class ThemeColors
    {
        public string Foreground;
        public string Background;
        public double? BackgroundOpacity;
        public string Accent;
        public string Danger;
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
    /// 一份 `.mptheme` 的内容。字段为 null = 文件里没写，套用时保留当前值（增量套用）；
    /// 只有 <see cref="ThemeFile.Export(MirrorpondHUD.Core.Settings, string)"/> 会写出全部已知字段。
    /// </summary>
    public sealed class ThemeDocument
    {
        public int Schema = ThemeFile.SupportedSchema;
        public string Name = "";
        public string Author = "";
        public string Description = "";

        public ThemeColors Colors = new ThemeColors();
        public ThemeTypography Typography = new ThemeTypography();
        public ThemeShape Shape = new ThemeShape();
        public ThemeOutline Outline = new ThemeOutline();
        public ThemeCliPanel CliPanel = new ThemeCliPanel();

        /// <summary>
        /// 把主题写进设置（只覆盖主题里出现过的字段）。调用方**必须**先克隆 Settings，
        /// 再对克隆调用本方法；校验失败时原设置不会被碰。
        /// </summary>
        public void ApplyTo(Settings settings)
        {
            if (settings == null) return;

            StyleSettings style = settings.Style ?? (settings.Style = new StyleSettings());

            // 1) 基础颜色先落，软色的「按前景派生」要用到新的前景色。
            if (Colors.Foreground != null) style.Foreground = Colors.Foreground;
            if (Colors.Background != null) style.Background = Colors.Background;
            if (Colors.BackgroundOpacity.HasValue) style.BackgroundOpacity = Colors.BackgroundOpacity.Value;
            if (Colors.Accent != null) style.Accent = Colors.Accent;
            if (Colors.Danger != null) style.Danger = Colors.Danger;

            int a, r, g, b;
            bool hasForeground = ThemeFile.TryParseColor(style.Foreground, out a, out r, out g, out b);
            ApplySoft(Colors.Muted, hasForeground, a, r, g, b, ref style.Muted);
            ApplySoft(Colors.Border, hasForeground, a, r, g, b, ref style.Border);
            ApplySoft(Colors.Panel, hasForeground, a, r, g, b, ref style.Panel);
            ApplySoft(Colors.Scrollbar, hasForeground, a, r, g, b, ref style.Scrollbar);
            ApplySoft(Colors.ScrollbarHot, hasForeground, a, r, g, b, ref style.ScrollbarHot);

            if (Typography.FontFamily != null) style.FontFamily = Typography.FontFamily;
            if (Typography.MonoFont != null) style.MonoFont = Typography.MonoFont;
            if (Typography.FontSize.HasValue) style.FontSize = Typography.FontSize.Value;
            if (Typography.Bold.HasValue) style.Bold = Typography.Bold.Value;
            if (Typography.IconScale.HasValue) style.IconScale = Typography.IconScale.Value;
            if (Typography.LineHeight.HasValue) style.LineHeight = Typography.LineHeight.Value;
            if (Typography.TextAlign != null) style.TextAlignment = Typography.TextAlign;

            if (Shape.Padding.HasValue) style.Padding = Shape.Padding.Value;

            OverlaySettings overlay = settings.Overlay ?? (settings.Overlay = new OverlaySettings());
            if (Shape.CornerRadius.HasValue) overlay.CornerRadius = Shape.CornerRadius.Value;
            if (Shape.Shadow.HasValue) overlay.Shadow = Shape.Shadow.Value;

            if (Outline.Enabled.HasValue) style.TextOutline = Outline.Enabled.Value;
            if (Outline.Color != null) style.OutlineColor = Outline.Color;
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
    /// 格式见 `docs/theme-system.md` §3；与 `settings.json` 的 SchemaVersion 无关。
    /// 只认识外观字段，卡片 / 热键 / 毛玻璃 / 位置一律不碰（导入只往 Style / Overlay / Cli 写值）。
    /// </summary>
    public static class ThemeFile
    {
        public const int SupportedSchema = 1;

        /// <summary>读取并套用到 source 的副本。失败时 applied 为 null，source 原样不动。</summary>
        public static bool TryImport(string path, Settings source, out Settings applied, out string error)
        {
            applied = null;
            error = null;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                error = "读取主题文件失败：" + ex.Message;
                return false;
            }

            ThemeDocument document;
            string parseError;
            if (!TryParse(json, out document, out parseError))
            {
                error = parseError;
                return false;
            }

            Settings duplicate;
            try
            {
                duplicate = Json.Deserialize<Settings>(Json.Serialize(source ?? new Settings()));
            }
            catch (Exception ex)
            {
                // 克隆失败就报错，绝不退回「全默认值」——那会把当前外观悄悄换成出厂设置。
                error = "复制当前设置失败：" + ex.Message;
                return false;
            }

            try
            {
                document.ApplyTo(duplicate);
                duplicate.Normalize();
            }
            catch (Exception ex)
            {
                error = "套用主题失败：" + ex.Message;
                return false;
            }

            applied = duplicate;
            return true;
        }

        /// <summary>把当前外观写成 `.mptheme`（UTF-8 无 BOM）。失败返回 false。</summary>
        public static bool TryExport(Settings settings, string path, out string error)
        {
            error = null;
            try
            {
                File.WriteAllText(path, Export(settings), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                error = "写出主题文件失败：" + ex.Message;
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

            var appearance = new Dictionary<string, object>();
            appearance["colors"] = colors;
            appearance["typography"] = typography;
            appearance["shape"] = shape;
            appearance["outline"] = outline;
            appearance["cliPanel"] = cliPanel;

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

            if (json != null && json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
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
                error = "JSON 解析失败：" + ex.Message;
                return false;
            }

            var rootMap = node as Dictionary<string, object>;
            if (rootMap == null)
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
            if (root.Error != null)
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
                    error = "主题文件版本 " + schema.Value + " 高于当前支持的 " + SupportedSchema + "，请升级 MirrorpondHUD";
                    return false;
                }
                doc.Schema = schema.Value;
            }

            doc.Name = root.String("name") ?? "";
            doc.Author = root.String("author") ?? "";
            doc.Description = root.String("description") ?? "";

            var appearance = root.Child("appearance");
            if (root.Error != null)
            {
                error = root.Error;
                return false;
            }
            if (appearance == null)
            {
                error = "缺少 appearance 对象";
                return false;
            }

            var colors = appearance.Child("colors");
            if (colors != null)
            {
                doc.Colors.Foreground = colors.Color("foreground");
                doc.Colors.Background = colors.Color("background");
                doc.Colors.BackgroundOpacity = colors.Number("backgroundOpacity");
                colors.Range(doc.Colors.BackgroundOpacity, "backgroundOpacity", 0, 1);
                doc.Colors.Accent = colors.Color("accent");
                doc.Colors.Danger = colors.Color("danger");

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
            if (typography != null)
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
                if (align != null)
                {
                    doc.Typography.TextAlign = CanonicalAlign(align);
                    if (doc.Typography.TextAlign == null)
                        typography.Fail("textAlign", "必须是 Left / Center / Right / Justify");
                }
            }

            var shape = appearance.Child("shape");
            if (shape != null)
            {
                doc.Shape.CornerRadius = shape.Number("cornerRadius");
                shape.Range(doc.Shape.CornerRadius, "cornerRadius", 0, 60);
                doc.Shape.Padding = shape.Number("padding");
                shape.Range(doc.Shape.Padding, "padding", 0, 80);
                doc.Shape.Shadow = shape.Bool("shadow");
            }

            var outline = appearance.Child("outline");
            if (outline != null)
            {
                doc.Outline.Enabled = outline.Bool("enabled");
                doc.Outline.Color = outline.Color("color");
                doc.Outline.Thickness = outline.Number("thickness");
                outline.Range(doc.Outline.Thickness, "thickness", 0.2, 10);
            }

            var cliPanel = appearance.Child("cliPanel");
            if (cliPanel != null)
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

            string first = root.Error
                ?? appearance.Error
                ?? (colors != null ? colors.Error : null)
                ?? (typography != null ? typography.Error : null)
                ?? (shape != null ? shape.Error : null)
                ?? (outline != null ? outline.Error : null)
                ?? (cliPanel != null ? cliPanel.Error : null);

            if (first != null)
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
                default: return null;
            }
        }

        /// <summary>#RRGGBB / #AARRGGBB → ARGB。失败返回 false（前景色非法时软色就不派生）。</summary>
        internal static bool TryParseColor(string hex, out int a, out int r, out int g, out int b)
        {
            a = 255; r = 0; g = 0; b = 0;
            string text = (hex ?? "").Trim();
            if (!Settings.IsColorToken(text) || text.Length == 0) return false;

            try
            {
                int offset = text.Length == 9 ? 1 : 0;
                if (text.Length == 9) a = Hex(text, 1);
                r = Hex(text, 1 + offset);
                g = Hex(text, 3 + offset);
                b = Hex(text, 5 + offset);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static int Hex(string text, int index)
        {
            return int.Parse(text.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
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

            public bool Has(string key)
            {
                object node;
                return _map.TryGetValue(key, out node) && node != null;
            }

            public ThemeObject Child(string key)
            {
                object node;
                if (!_map.TryGetValue(key, out node) || node == null) return null;

                var map = node as Dictionary<string, object>;
                if (map == null)
                {
                    Fail(key, "必须是对象");
                    return null;
                }
                return new ThemeObject(map, Join(key));
            }

            public string String(string key)
            {
                object node;
                if (!_map.TryGetValue(key, out node) || node == null) return null;
                if (!(node is string))
                {
                    Fail(key, "必须是字符串");
                    return null;
                }
                return ((string)node).Trim();
            }

            public double? Number(string key)
            {
                object node;
                if (!_map.TryGetValue(key, out node) || node == null) return null;
                if (!(node is double))
                {
                    Fail(key, "必须是数字");
                    return null;
                }
                return (double)node;
            }

            public bool? Bool(string key)
            {
                object node;
                if (!_map.TryGetValue(key, out node) || node == null) return null;
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
                if (text == null) return null;
                if (text.Length == 0 || !Settings.IsColorToken(text))
                {
                    Fail(key, "必须是 #RRGGBB 或 #AARRGGBB");
                    return null;
                }
                return text;
            }

            public ThemeSoftColor? Soft(string key)
            {
                object node;
                if (!_map.TryGetValue(key, out node) || node == null) return null;

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
                if (Error == null) Error = Join(key) + " " + message;
            }

            string Join(string key)
            {
                return _path.Length == 0 ? key : _path + "." + key;
            }
        }
    }
}
