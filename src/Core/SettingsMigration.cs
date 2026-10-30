using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 旧版配置 → 当前配置结构。
    /// · 0/1：旧的单窗口配置（全局 Content + 带 X/Y/Visible 的 Cards），走 LegacySettings。
    /// · 2：初版卡片配置（命令行工具是全局设置），把 Settings.Command 拷进每张命令行卡片。
    /// · 4 → 5：每张命令行卡片的 Command 子对象搬进 CardSettings.Extras["command"]（以后的卡片设置也走这里）。
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

            if (version >= 3)
            {
                Settings modern = null;
                try { modern = Json.Deserialize<Settings>(json); }
                catch (Exception ex) { Log.Warn("读取 v" + version + " 设置失败: " + ex.Message); }
                modern = modern ?? new Settings();

                // v3 → v4：旧默认 1.45 是「字号倍率」时代的值，图标因此比文字高 45%；
                // 新标定下 1.15 才是「比文字略大一圈」，只在用户没改过默认值时替换。
                if (version == 3 && modern.Style != null && Math.Abs(modern.Style.IconScale - 1.45) < 0.005)
                {
                    modern.Style.IconScale = 1.15;
                    Log.Info("设置升级到 SchemaVersion 4：图标倍率 1.45 → 1.15（旧默认值偏大，图标比文字高 45%）");
                }

                UpgradeCommandToExtras(json, modern);
                Log.Info("设置已从 SchemaVersion " + version + " 升级到 " + Settings.CurrentSchemaVersion +
                         "（卡片自己的设置搬进 Extras）");
                return modern;
            }

            if (version == 2)
            {
                Settings two = null;
                try { two = Json.Deserialize<Settings>(json); }
                catch (Exception ex) { Log.Warn("读取 v2 设置失败: " + ex.Message); }
                two = two ?? new Settings();

                UpgradeV2Command(json, two);
                Log.Info("设置已从 SchemaVersion 2 升级到 " + Settings.CurrentSchemaVersion + "（命令行设置改为每张卡片独立）");
                return two;
            }

            LegacySettings legacy = null;
            try { legacy = Json.Deserialize<LegacySettings>(json); }
            catch (Exception ex) { Log.Warn("迁移旧设置失败，使用默认值: " + ex.Message); }

            Settings migrated = Migrate(legacy ?? new LegacySettings());
            Log.Info("旧设置已升级到 SchemaVersion " + Settings.CurrentSchemaVersion +
                     "（卡片 " + migrated.Cards.Count + " 张）");
            return migrated;
        }

        /// <summary>v2 的全局命令行设置 → 每张命令行卡片各一份。</summary>
        static void UpgradeV2Command(string json, Settings settings)
        {
            CommandSettings legacy = null;
            try
            {
                var map = Json.Parse(json) as Dictionary<string, object>;
                object raw;
                if (map != null && map.TryGetValue("Command", out raw) && raw != null)
                    legacy = Json.BindNode<CommandSettings>(raw);
            }
            catch (Exception ex)
            {
                Log.Warn("读取旧命令行设置失败: " + ex.Message);
            }

            if (legacy == null || settings.Cards == null) return;
            legacy.Normalize();

            foreach (CardSettings card in settings.Cards)
            {
                if (card == null) continue;
                if (!string.Equals(card.Kind, "command", StringComparison.OrdinalIgnoreCase)) continue;
                card.SetExtras("command", legacy.Clone());
            }
        }

        /// <summary>
        /// v4 及更早：每张命令行卡片的 <c>Command</c> 子对象 → <c>Extras["command"]</c>。
        /// 只搬命令行卡片，别的卡片以前也带着一份拷贝但从没被读过。
        /// </summary>
        static void UpgradeCommandToExtras(string json, Settings settings)
        {
            if (settings == null || settings.Cards == null || settings.Cards.Count == 0) return;

            List<object> rawCards = null;
            try
            {
                var root = Json.Parse(json) as Dictionary<string, object>;
                object raw;
                if (root != null && root.TryGetValue("Cards", out raw)) rawCards = raw as List<object>;
            }
            catch (Exception ex)
            {
                Log.Warn("读取旧卡片设置失败: " + ex.Message);
            }

            if (rawCards == null) return;

            int count = Math.Min(rawCards.Count, settings.Cards.Count);
            for (int i = 0; i < count; i++)
            {
                CardSettings card = settings.Cards[i];
                if (card == null) continue;
                if (!string.Equals(card.Kind, "command", StringComparison.OrdinalIgnoreCase)) continue;

                var map = rawCards[i] as Dictionary<string, object>;
                if (map == null) continue;

                object commandNode;
                if (!map.TryGetValue("Command", out commandNode) || commandNode == null) continue;

                try
                {
                    CommandSettings command = Json.BindNode<CommandSettings>(commandNode);
                    if (command == null) continue;
                    command.Normalize();
                    card.SetExtras("command", command);
                }
                catch (Exception ex)
                {
                    Log.Warn("迁移卡片命令行设置失败: " + ex.Message);
                }
            }
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
                Hotkeys = new HotkeySettings
                {
                    ToggleOverlay = legacy.Hotkeys == null ? "" : (legacy.Hotkeys.ToggleOverlay ?? ""),
                    OpenSettings = legacy.Hotkeys == null ? "F10" : legacy.Hotkeys.OpenSettings,
                    ToggleLock = legacy.Hotkeys == null ? "F8" : legacy.Hotkeys.ToggleLock
                }
            };

            CommandSettings legacyCommand = legacy.Command ?? new CommandSettings();
            legacyCommand.Normalize();

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
                    if (kind == "command") card.SetExtras("command", legacyCommand.Clone());

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
                var commandCard = new CardSettings
                {
                    Kind = "command",
                    Resident = false,
                    Hotkey = commandHotkey,
                    Spacing = 16
                };
                commandCard.SetExtras("command", legacyCommand.Clone());
                s.Cards.Add(commandCard);
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
