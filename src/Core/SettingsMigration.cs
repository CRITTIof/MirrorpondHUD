using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 设置 JSON 读取。2.14.8 起项目还在开发阶段，**不再迁移历史 Schema**：
    /// 只接受当前 <see cref="Settings.CurrentSchemaVersion"/>（以及缺版本号的当前格式）；
    /// 更旧的版本按损坏处理，由 ConfigStore 备份后使用默认设置。
    /// 更高版本只读加载，避免降级覆盖。
    /// </summary>
    internal static class SettingsMigration
    {
        public static Settings Load(string json, out bool needsSave, out bool corrupted)
        {
            bool future;
            return Load(json, out needsSave, out corrupted, out future);
        }

        public static Settings Load(string json, out bool needsSave, out bool corrupted, out bool futureVersion)
        {
            needsSave = false;
            corrupted = false;
            futureVersion = false;

            Dictionary<string, object> root;
            int version;
            bool hasVersion;
            if (!TryReadRoot(json, out root) ||
                !TryReadVersion(root, out version, out hasVersion))
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
                    if (future == null) throw new InvalidOperationException("设置根对象为空");
                    futureVersion = true;
                    Log.Warn("设置来自更高 SchemaVersion " + version + "，只读加载，拒绝覆盖");
                    return future;
                }

                if (hasVersion && version < Settings.CurrentSchemaVersion)
                {
                    // 不再做 0/1/2/3/4/5 → 6 的迁移；没有用户，旧文件直接备份后用默认设置。
                    corrupted = true;
                    Log.Warn("旧 SchemaVersion " + version + " 已不再支持（当前 " +
                             Settings.CurrentSchemaVersion + "），按损坏处理");
                    return new Settings();
                }

                Settings current = Json.Deserialize<Settings>(json);
                if (current == null) throw new InvalidOperationException("设置根对象为空");

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
                Log.Warn("设置解析失败，按损坏处理: " + ex.Message);
                return new Settings();
            }
        }

        static bool TryReadRoot(string json, out Dictionary<string, object> root)
        {
            root = null;
            try
            {
                root = Json.Parse(json) as Dictionary<string, object>;
                return root != null;
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

            object raw;
            if (root == null || !root.TryGetValue("SchemaVersion", out raw) || raw == null)
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
}
