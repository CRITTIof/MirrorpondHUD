using System;
using System.Collections.Generic;
using System.Globalization;

namespace MirrorpondHUD.Core;

/// <summary>
/// 设置 JSON 读取。2.14.7 起不再按版本链迁移历史 Schema：
/// 只接受当前 <see cref="Settings.CurrentSchemaVersion"/>（以及缺版本号的当前格式）；
/// 唯一例外是 2.24.0 的 v6→v7 一次性迁移（玻璃模糊字段合并 / GlassMode 更名 DesktopFeed /
/// 移除 Chat 模式别名，见 <see cref="MigrateV6ToV7"/>）。
/// 更旧的版本按损坏处理，由 ConfigStore 备份后使用默认设置；更高版本只读加载，避免降级覆盖。
/// </summary>
internal static class SettingsLoader
{
    public static Settings Load(string json, out bool needsSave, out bool corrupted) => Load(json, out needsSave, out corrupted, out bool future);

    public static Settings Load(string json, out bool needsSave, out bool corrupted, out bool futureVersion)
    {
        needsSave = false;
        corrupted = false;
        futureVersion = false;

        if (!TryReadRoot(json, out Dictionary<string, object> root) ||
            !TryReadVersion(root, out int version, out bool hasVersion))
        {
            corrupted = true;
            Log.Warn("设置文件不是合法的 JSON 对象，按损坏处理");
            return new Settings();
        }

        try
        {
            if (hasVersion && version > Settings.CurrentSchemaVersion)
            {
                Settings future = Json.Deserialize<Settings>(json);
                if (future is null) throw new InvalidOperationException("设置根对象为空");
                futureVersion = true;
                Log.Warn($"设置来自更高 SchemaVersion {version}，只读加载，拒绝覆盖");
                return future;
            }

            if (hasVersion && version < Settings.CurrentSchemaVersion)
            {
                // 2.24.0 的唯一例外：v6 → v7 一次性迁移（字段合并 + 更名 + Chat 别名）。
                // 必须先把迁移后的 root 重新序列化再反序列化——原 json 里的旧键仍然存在，
                // 直接用原 json 会让被删掉的字段继续参与绑定（多套模糊参数并存）。
                if (version == 6)
                {
                    string migratedJson = MigrateV6ToV7(root);
                    Settings migrated = Json.Deserialize<Settings>(migratedJson);
                    if (migrated is null) throw new InvalidOperationException("设置根对象为空");

                    needsSave = true;
                    Log.Info($"设置已从 SchemaVersion 6 迁移到 7：玻璃模糊合并为 BlurRadius / BlurFps，GlassMode 归一为 DesktopFeed / Compat / System，Chat 模式别名改为 Context");
                    return migrated;
                }

                // 不再做 0/1/2/3/4/5 → 6/7 的迁移；旧文件直接备份后用默认设置。
                corrupted = true;
                Log.Warn($"旧 SchemaVersion {version} 已不再支持（当前 {Settings.CurrentSchemaVersion}），按损坏处理");
                return new Settings();
            }

            Settings current = Json.Deserialize<Settings>(json);
            if (current is null) throw new InvalidOperationException("设置根对象为空");

            if (!hasVersion)
            {
                needsSave = true;
                Log.Warn("设置缺少 SchemaVersion，按当前格式补写版本号");
            }

            return current;
        }
        catch (Exception ex)
        {
            corrupted = true;
            needsSave = false;
            futureVersion = false;
            Log.Warn($"设置解析失败，按损坏处理: {ex.Message}");
            return new Settings();
        }
    }

    /// <summary>
    /// v6 → v7 一次性迁移（2.24.0）。返回迁移后的 JSON 文本，调用方必须用它重新反序列化。
    ///
    /// 1) 玻璃模糊：v6 有 AcrylicBlur / AcrylicFps（兼容取样）与 GlassBlur / GlassFps（桌面取样）两套，
    ///    按旧 GlassMode 选一套合并到 BlurRadius / BlurFps（桌面模式缺省 18 / 10，其余 12 / 15）；
    /// 2) GlassMode：Dwm / Acrylic 等旧值归一为 DesktopFeed，Compat 族保持 Compat，其余回落 System；
    /// 3) Chat：Cards[].Extras.chat.Mode 的 continuous / poll / polling 改写成 Context，其余值原样保留；
    /// 4) 删掉 Style 里的四个旧模糊键，SchemaVersion 写成当前版本。
    /// </summary>
    static string MigrateV6ToV7(Dictionary<string, object> root)
    {
        Dictionary<string, object> style = GetMap(root, "Style");
        if (style is not null)
        {
            string oldMode = GetString(style, "GlassMode");
            bool desktop = IsDesktopGlassMode(oldMode);

            style["BlurRadius"] = ReadInt(style, desktop ? "GlassBlur" : "AcrylicBlur", desktop ? 18 : 12);
            style["BlurFps"] = ReadInt(style, desktop ? "GlassFps" : "AcrylicFps", desktop ? 10 : 15);
            style["GlassMode"] = Settings.NormalizeGlassMode(oldMode);

            RemoveKey(style, "AcrylicBlur");
            RemoveKey(style, "AcrylicFps");
            RemoveKey(style, "GlassBlur");
            RemoveKey(style, "GlassFps");
        }

        List<object> cards = GetList(root, "Cards");
        if (cards is not null)
        {
            foreach (object node in cards)
            {
                Dictionary<string, object> card = node as Dictionary<string, object>;
                if (card is null) continue;

                Dictionary<string, object> extras = GetMap(card, "Extras");
                if (extras is null) continue;

                string chatKey = FindKey(extras, "chat");
                if (chatKey is null) continue;

                Dictionary<string, object> chat = extras[chatKey] as Dictionary<string, object>;
                if (chat is null) continue;

                string modeKey = FindKey(chat, "Mode");
                if (modeKey is null) continue;

                string mode = chat[modeKey] as string;
                switch ((mode ?? "").Trim().ToLowerInvariant())
                {
                    case "continuous":
                    case "poll":
                    case "polling":
                        // 2.24.0 起运行期只认 Context；旧别名在这里一次性改写，
                        // 之后 ChatSettings.NormalizeMode 不再兼容这些值。
                        chat[modeKey] = "Context";
                        break;
                }
            }
        }

        string versionKey = FindKey(root, "SchemaVersion");
        root[versionKey is null ? "SchemaVersion" : versionKey] = Settings.CurrentSchemaVersion;

        return Json.Serialize(root);
    }

    /// <summary>v6 的桌面取样模式（Dwm 及其更早别名）；System / Compat / 缺省都按兼容取样那一套取值。</summary>
    static bool IsDesktopGlassMode(string mode)
    {
        switch ((mode ?? "").Trim().ToLowerInvariant())
        {
            case "dwm":
            case "acrylic":
            case "desktop":
            case "multiwindow":
            case "multi-window":
            case "desktopfeed":
            case "desktop-feed":
                return true;
            default:
                return false;
        }
    }

    /// <summary>按字段名找键：先精确匹配，再忽略大小写（手改配置可能改了大小写）。</summary>
    static string FindKey(Dictionary<string, object> map, string key)
    {
        if (map is null || key is null) return null;
        if (map.ContainsKey(key)) return key;

        foreach (string existing in map.Keys)
        {
            if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)) return existing;
        }
        return null;
    }

    static void RemoveKey(Dictionary<string, object> map, string key)
    {
        string actual = FindKey(map, key);
        if (actual is not null) map.Remove(actual);
    }

    static Dictionary<string, object> GetMap(Dictionary<string, object> map, string key)
    {
        string actual = FindKey(map, key);
        if (actual is null) return null;
        return map[actual] as Dictionary<string, object>;
    }

    static List<object> GetList(Dictionary<string, object> map, string key)
    {
        string actual = FindKey(map, key);
        if (actual is null) return null;
        return map[actual] as List<object>;
    }

    static string GetString(Dictionary<string, object> map, string key)
    {
        string actual = FindKey(map, key);
        if (actual is null) return "";
        string value = map[actual] as string;
        return value is null ? "" : value;
    }

    /// <summary>读旧配置里的整数（JSON 数字解析成 double，手改也可能是字符串）；缺失 / 非法回落默认值。</summary>
    static int ReadInt(Dictionary<string, object> map, string key, int fallback)
    {
        string actual = FindKey(map, key);
        if (actual is null) return fallback;

        object raw = map[actual];
        double value;
        if (raw is double) value = (double)raw;
        else if (raw is int) value = (int)raw;
        else if (raw is long) value = (long)raw;
        else if (raw is float) value = (float)raw;
        else if (raw is string)
        {
            if (!double.TryParse((string)raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return fallback;
        }
        else
        {
            return fallback;
        }

        // v6 的 GlassBlur / GlassFps 是 int，手改偶尔会写小数或超大数；夹到 int 范围后再四舍五入。
        if (double.IsNaN(value) || double.IsInfinity(value) ||
            value < int.MinValue || value > int.MaxValue)
            return fallback;

        return (int)Math.Round(value);
    }

    static bool TryReadRoot(string json, out Dictionary<string, object> root)
    {
        root = null;
        try
        {
            root = Json.Parse(json) as Dictionary<string, object>;
            return root is not null;
        }
        catch
        {
            return false;
        }
    }

    static bool TryReadVersion(Dictionary<string, object> root, out int version, out bool hasVersion)
    {
        version = 0;
        hasVersion = false;

        if (root is null || !root.TryGetValue("SchemaVersion", out object raw) || raw is null)
            return true;

        hasVersion = true;
        try
        {
            double value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value) ||
                value < 0 || value > 1_000_000)
                return false;

            version = (int)Math.Round(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
