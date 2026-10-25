using System;
using System.Collections.Generic;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>卡片列表的创建、补全与校验（放在模块层，配置文件本身只存数据）。</summary>
    public static class CardModel
    {
        /// <summary>
        /// 补全卡片列表：空列表生成默认卡片，未知 Kind 归一到已注册模块，
        /// 补 Id / 名称、清掉不适用的字段、夹紧间距。
        /// </summary>
        public static void Normalize(Settings settings)
        {
            if (settings == null) return;

            if (settings.Cards == null) settings.Cards = new List<CardSettings>();
            if (settings.Cards.Count == 0) settings.Cards = CreateDefaultCards();

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < settings.Cards.Count; i++)
            {
                CardSettings card = settings.Cards[i];
                if (card == null)
                {
                    settings.Cards.RemoveAt(i);
                    i--;
                    continue;
                }

                // v2.1.1 起四种文字卡合并成「信息与文本」：旧 Kind 迁移成预设。
                string legacy = (card.Kind ?? "").Trim().ToLowerInvariant();
                if (legacy == "metrics" || legacy == "clock" || legacy == "keys" || legacy == "text")
                {
                    if (string.IsNullOrWhiteSpace(card.Preset)) card.Preset = legacy;
                    card.Kind = "infotext";
                }

                card.Kind = CardRegistry.ResolveKind(card.Kind);
                ICardModule module = CardRegistry.Get(card.Kind);
                if (module is InfoTextCard)
                    card.Preset = InfoTextCard.InferPreset(card.Preset, card.Content);

                if (string.IsNullOrWhiteSpace(card.Id) || ids.Contains(card.Id))
                    card.Id = NewId();
                ids.Add(card.Id);

                card.Name = string.IsNullOrWhiteSpace(card.Name) ? module.DisplayName : card.Name.Trim();
                card.Content = card.Content ?? "";
                if (!module.SupportsContent) card.Content = "";

                card.Hotkey = (card.Hotkey ?? "").Trim();
                if (module.IsInteractive) card.Resident = false;

                card.Spacing = Math.Round(Settings.Clamp(card.Spacing, 0, 300));

                if (card.Command == null) card.Command = new CommandSettings();
                card.Command.Normalize();

                if (card.Media == null) card.Media = new MediaSettings();
                card.Media.Normalize();

                if (card.Chat == null) card.Chat = new ChatSettings();
                card.Chat.Normalize();
            }

            if (settings.Cards.Count == 0) settings.Cards = CreateDefaultCards();
        }

        /// <summary>全新配置的默认卡片：系统信息、时钟、按键（常驻）+ 命令行（F9）、媒体（F11）。</summary>
        public static List<CardSettings> CreateDefaultCards()
        {
            return new List<CardSettings>
            {
                new CardSettings
                {
                    Id = NewId(), Kind = "infotext", Preset = "metrics",
                    Name = InfoTextCard.LabelFor("metrics"),
                    Resident = true, Spacing = 0, Content = Settings.DefaultMetricsContent
                },
                new CardSettings
                {
                    Id = NewId(), Kind = "infotext", Preset = "clock",
                    Name = InfoTextCard.LabelFor("clock"),
                    Resident = true, Spacing = 12, Content = Settings.DefaultClockContent
                },
                new CardSettings
                {
                    Id = NewId(), Kind = "infotext", Preset = "keys",
                    Name = InfoTextCard.LabelFor("keys"),
                    Resident = true, Spacing = 12, Content = Settings.DefaultKeysContent
                },
                new CardSettings
                {
                    Id = NewId(), Kind = "command", Name = CardRegistry.Get("command").DisplayName,
                    Resident = false, Spacing = 12, Hotkey = "F9", Content = ""
                },
                new CardSettings
                {
                    Id = NewId(), Kind = "media", Name = CardRegistry.Get("media").DisplayName,
                    Resident = false, Spacing = 12, Hotkey = "F11", Content = ""
                }
            };
        }

        /// <summary>按模块元数据新建一张卡片（设置页「添加卡片」用）。</summary>
        public static CardSettings Create(ICardModule module, IList<CardSettings> existing)
        {
            if (module == null) return null;

            var card = new CardSettings
            {
                Id = NewId(),
                Kind = module.Kind,
                Name = UniqueName(module.DisplayName, existing),
                Resident = module.IsInteractive ? false : module.DefaultResident,
                Hotkey = module.DefaultHotkey,
                Spacing = existing == null || existing.Count == 0 ? 0 : 12,
                Content = ""
            };

            if (module.IsInteractive) card.Resident = false;
            return card;
        }

        /// <summary>同名卡片允许存在，这里只给新卡片加一个可读的后缀。</summary>
        public static string UniqueName(string baseName, IList<CardSettings> existing)
        {
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "卡片";
            if (existing == null || existing.Count == 0) return baseName;

            var used = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (CardSettings card in existing)
                if (card != null) used.Add(card.Name ?? "");

            if (!used.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (!used.Contains(candidate)) return candidate;
            }
            return baseName;
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
