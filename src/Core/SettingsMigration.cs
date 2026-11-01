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
            bool corrupted;
            bool future;
            return Load(json, out needsSave, out corrupted, out future);
        }

        public static Settings Load(string json, out bool needsSave, out bool corrupted)
        {
            bool future;
            return Load(json, out needsSave, out corrupted, out future);
        }

        /// <summary>
        /// 读设置。<paramref name="corrupted"/> = true 表示 JSON / 绑定失败，调用方应备份损坏文件并尝试 .bak，
        /// 不能把它当成“旧版本迁移”后静默保存默认值。
        /// <paramref name="futureVersion"/> = true 表示文件来自更高 SchemaVersion：只读，不能保存覆盖。
        /// </summary>
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
                // 没有 SchemaVersion 的当前格式（例如手改/旧工具导出）：不能当 legacy 迁移，
                // 否则 chat/media/dropstation 会被 NormalizeKind 降成 metrics 并丢 Extras。
                if (!hasVersion && LooksLikeCurrentFormat(root))
                {
                    Settings unversioned = Json.Deserialize<Settings>(json);
                    if (unversioned == null) throw new InvalidOperationException("设置根对象为空");
                    needsSave = true;
                    Log.Warn("设置缺少 SchemaVersion，按当前格式补写版本号");
                    return unversioned;
                }

                if (hasVersion && version > Settings.CurrentSchemaVersion)
                {
                    Settings future = Json.Deserialize<Settings>(json);
                    if (future == null) throw new InvalidOperationException("设置根对象为空");
                    futureVersion = true;
                    Log.Warn("设置来自更高 SchemaVersion " + version + "，只读加载，拒绝覆盖");
                    return future;
                }

                if (version >= Settings.CurrentSchemaVersion)
                {
                    Settings current = Json.Deserialize<Settings>(json);
                    if (current == null) throw new InvalidOperationException("设置根对象为空");
                    return current;
                }

                needsSave = true;

                if (version >= 3)
                {
                    Settings modern = Json.Deserialize<Settings>(json);
                    if (modern == null) throw new InvalidOperationException("设置根对象为空");

                    // v3 → v4：旧默认 1.45 是「字号倍率」时代的值，图标因此比文字高 45%；
                    // 新标定下 1.15 才是「比文字略大一圈」，只在用户没改过默认值时替换。
                    if (version == 3 && modern.Style != null && Math.Abs(modern.Style.IconScale - 1.45) < 0.005)
                    {
                        modern.Style.IconScale = 1.15;
                        Log.Info("设置升级到 SchemaVersion 4：图标倍率 1.45 → 1.15（旧默认值偏大，图标比文字高 45%）");
                    }

                    UpgradeCardExtras(json, modern);
                    Log.Info("设置已从 SchemaVersion " + version + " 升级到 " + Settings.CurrentSchemaVersion +
                             "（卡片自己的设置搬进 Extras）");
                    return modern;
                }

                if (version == 2)
                {
                    Settings two = Json.Deserialize<Settings>(json);
                    if (two == null) throw new InvalidOperationException("设置根对象为空");

                    UpgradeV2Command(json, two);
                    UpgradeCardExtras(json, two);
                    Log.Info("设置已从 SchemaVersion 2 升级到 " + Settings.CurrentSchemaVersion + "（命令行设置改为每张卡片独立）");
                    return two;
                }

                // 显式 SchemaVersion 0 + 当前卡片形状：也按当前格式补写，而不是 legacy 重建。
                if (LooksLikeCurrentFormat(root))
                {
                    Settings zero = Json.Deserialize<Settings>(json);
                    if (zero == null) throw new InvalidOperationException("设置根对象为空");
                    Log.Warn("设置 SchemaVersion=0 但卡片是当前格式，按当前格式补写");
                    return zero;
                }

                LegacySettings legacy = Json.Deserialize<LegacySettings>(json);
                if (legacy == null) throw new InvalidOperationException("旧设置根对象为空");

                Settings migrated = Migrate(legacy);
                Log.Info("旧设置已升级到 SchemaVersion " + Settings.CurrentSchemaVersion +
                         "（卡片 " + migrated.Cards.Count + " 张）");
                return migrated;
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
        /// v5 及更早：把每张卡片旧的子对象搬进 <c>Extras</c>——
        /// <c>Command</c> → <c>command</c>（命令行卡）、<c>Media</c> → <c>media</c>（媒体卡）、
        /// <c>Chat</c> → <c>chat</c>（对话卡）。只搬对应种类的卡片：其它卡片以前也各带一份拷贝但从没被读过。
        /// </summary>
        static void UpgradeCardExtras(string json, Settings settings)
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
                var map = rawCards[i] as Dictionary<string, object>;
                if (card == null || map == null) continue;

                switch ((card.Kind ?? "").Trim().ToLowerInvariant())
                {
                    case "command":
                        MoveExtra<CommandSettings>(map, "Command", card, "command");
                        break;
                    case "media":
                        MoveExtra<MediaSettings>(map, "Media", card, "media");
                        break;
                    case "chat":
                        MoveExtra<ChatSettings>(map, "Chat", card, "chat");
                        break;
                }
            }
        }

        /// <summary>把一个旧的子对象节点绑定成强类型，归一化后写进卡片的 Extras。</summary>
        static void MoveExtra<T>(Dictionary<string, object> map, string jsonKey, CardSettings card, string extrasKey)
            where T : class, new()
        {
            object node;
            if (!map.TryGetValue(jsonKey, out node) || node == null) return;

            try
            {
                T value = Json.BindNode<T>(node);
                if (value == null) return;

                var command = value as CommandSettings;
                if (command != null) command.Normalize();

                var media = value as MediaSettings;
                if (media != null) media.Normalize();

                var chat = value as ChatSettings;
                if (chat != null) chat.Normalize();

                card.SetExtras(extrasKey, value);
            }
            catch (Exception ex)
            {
                Log.Warn("迁移卡片设置失败(" + jsonKey + "): " + ex.Message);
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
                return true; // 缺省版本号：由调用方按卡片形状判断 legacy / 当前格式

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

        /// <summary>
        /// 没有版本号时区分“旧版 legacy”与“当前 CardSettings 形状”。legacy 卡片只有
        /// Kind/Visible/X/Y/Monitor/Content；当前卡片有 Id/Extras/Preset/Hotkey/Resident 等字段。
        /// </summary>
        static bool LooksLikeCurrentFormat(Dictionary<string, object> root)
        {
            if (root == null) return false;

            object cardsNode;
            if (!root.TryGetValue("Cards", out cardsNode) || cardsNode == null) return false;

            var cards = cardsNode as List<object>;
            if (cards == null) return false;

            foreach (object entry in cards)
            {
                var card = entry as Dictionary<string, object>;
                if (card == null) continue;

                if (card.ContainsKey("Id") || card.ContainsKey("Extras") ||
                    card.ContainsKey("Preset") || card.ContainsKey("Hotkey") ||
                    card.ContainsKey("Resident"))
                    return true;
            }

            return false;
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
