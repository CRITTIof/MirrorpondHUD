using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 旧版（单窗口 / 初版多卡片）配置 → 当前配置结构。
    /// 旧文件没有 SchemaVersion，字段包括全局 Content 和带 X/Y/Visible 的 Cards。
    /// 迁移只做一次：ConfigStore 升级后立即写回新结构。
    /// </summary>
    internal static class SettingsMigration
    {
        public static Settings Load(string json, out bool needsSave)
        {
            needsSave = false;

            int version = ReadVersion(json);
            if (version >= Settings.CurrentSchemaVersion)
            {
                Settings current = null;
                try { current = Json.Deserialize<Settings>(json); }
                catch (Exception ex) { Log.Warn("读取设置失败，使用默认值: " + ex.Message); }
                return current ?? new Settings();
            }

            needsSave = true;
            LegacySettings legacy = null;
            try { legacy = Json.Deserialize<LegacySettings>(json); }
            catch (Exception ex) { Log.Warn("迁移旧设置失败，使用默认值: " + ex.Message); }

            Settings migrated = Migrate(legacy ?? new LegacySettings());
            Log.Info("旧设置已升级到 SchemaVersion " + Settings.CurrentSchemaVersion +
                     "（卡片 " + migrated.Cards.Count + " 张）");
            return migrated;
        }

        static int ReadVersion(string json)
        {
            try
            {
                var map = Json.Parse(json) as Dictionary<string, object>;
                if (map == null) return 0;

                object raw;
                if (!map.TryGetValue("SchemaVersion", out raw) || raw == null) return 0;
                return (int)Math.Round(Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
                return 0;
            }
        }

        public static Settings Migrate(LegacySettings legacy)
        {
            var s = new Settings
            {
                Overlay = legacy.Overlay ?? new OverlaySettings(),
                Style = legacy.Style ?? new StyleSettings(),
                Data = legacy.Data ?? new DataSettings(),
                Behavior = legacy.Behavior ?? new BehaviorSettings(),
                Command = legacy.Command ?? new CommandSettings(),
                Hotkeys = new HotkeySettings
                {
                    ToggleOverlay = legacy.Hotkeys == null ? "" : (legacy.Hotkeys.ToggleOverlay ?? ""),
                    OpenSettings = legacy.Hotkeys == null ? "F10" : legacy.Hotkeys.OpenSettings,
                    ToggleLock = legacy.Hotkeys == null ? "F8" : legacy.Hotkeys.ToggleLock
                }
            };

            string commandHotkey = legacy.Hotkeys == null || string.IsNullOrWhiteSpace(legacy.Hotkeys.CommandLine)
                ? "F9"
                : legacy.Hotkeys.CommandLine.Trim();

            s.Cards = new List<CardSettings>();

            if (legacy.Cards != null && legacy.Cards.Count > 0)
            {
                foreach (LegacyCard old in legacy.Cards)
                {
                    if (old == null) continue;

                    string kind = NormalizeKind(old.Kind);
                    bool resident = kind == "command" ? false : old.Visible;

                    var card = new CardSettings
                    {
                        Kind = kind,
                        Resident = resident,
                        Hotkey = kind == "command" ? commandHotkey : "",
                        Spacing = s.Cards.Count == 0 ? 0 : 16,
                        Content = old.Content ?? ""
                    };

                    if (kind == "metrics" && string.IsNullOrWhiteSpace(card.Content))
                        card.Content = legacy.Content ?? "";

                    s.Cards.Add(card);
                }
            }

            bool hasMetrics = s.Cards.Exists(c => c.Kind == "metrics");
            bool hasCommand = s.Cards.Exists(c => c.Kind == "command");

            if (!hasMetrics)
            {
                s.Cards.Insert(0, new CardSettings
                {
                    Kind = "metrics",
                    Resident = true,
                    Spacing = 0,
                    Content = legacy.Content ?? ""
                });
            }

            if (!hasCommand)
            {
                s.Cards.Add(new CardSettings
                {
                    Kind = "command",
                    Resident = false,
                    Hotkey = commandHotkey,
                    Spacing = 16
                });
            }

            if (string.IsNullOrWhiteSpace(legacy.Content))
            {
                foreach (CardSettings card in s.Cards)
                {
                    if (card.Kind == "metrics" && string.IsNullOrWhiteSpace(card.Content))
                        card.Content = Settings.DefaultMetricsContent;
                }
            }

            s.Cards[0].Spacing = 0;
            return s;
        }

        static string NormalizeKind(string kind)
        {
            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "clock": return "clock";
                case "keys": return "keys";
                case "text": return "text";
                case "command": return "command";
                default: return "metrics";
            }
        }
    }

    /// <summary>旧配置的完整镜像，只用于迁移。</summary>
    public sealed class LegacySettings
    {
        public OverlaySettings Overlay = new OverlaySettings();
        public StyleSettings Style = new StyleSettings();
        public DataSettings Data = new DataSettings();
        public BehaviorSettings Behavior = new BehaviorSettings();
        public LegacyHotkeys Hotkeys = new LegacyHotkeys();
        public CommandSettings Command = new CommandSettings();
        public string Content = "";
        public List<LegacyCard> Cards = new List<LegacyCard>();
    }

    public sealed class LegacyHotkeys
    {
        public string ToggleOverlay = "";
        public string OpenSettings = "F10";
        public string ToggleLock = "F8";
        public string CommandLine = "F9";
    }

    public sealed class LegacyCard
    {
        public string Kind = "metrics";
        public bool Visible = true;
        public double X = 0;
        public double Y = 0;
        public int Monitor = 0;
        public string Content = "";
    }
}
