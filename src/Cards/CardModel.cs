using System;
using System.Collections.Generic;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards;

/// <summary>卡片列表的创建、补全与校验（放在模块层，配置文件本身只存数据）。</summary>
public static class CardModel
{
    /// <summary>
    /// 补全卡片列表：空列表生成默认卡片，未知 Kind 归一到已注册模块，
    /// 补 Id / 名称、清掉不适用的字段、夹紧间距。
    /// </summary>
    public static void Normalize(Settings settings)
    {
        if (settings is null) return;

        if (settings.Cards is null) settings.Cards = new List<CardSettings>();
        if (settings.Cards.Count == 0) settings.Cards = CreateDefaultCards();

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < settings.Cards.Count; i++)
        {
            CardSettings card = settings.Cards[i];
            if (card is null)
            {
                settings.Cards.RemoveAt(i);
                i--;
                continue;
            }

            card.Kind = CardRegistry.ResolveKind(card.Kind);
            bool known = CardRegistry.TryGet(card.Kind, out var module);

            if (string.IsNullOrWhiteSpace(card.Id) || ids.Contains(card.Id))
                card.Id = NewId();
            ids.Add(card.Id);

            if (!known)
            {
                // 插件被卸载 / 加载失败：保留原 Kind 与 Extras，绝不能静默改成默认卡。
                // 名称、内容、间距仍做安全夹紧；等插件重新出现时原配置自动恢复。
                card.Kind = (card.Kind ?? "").Trim();
                if (string.IsNullOrWhiteSpace(card.Name)) card.Name = card.Kind;
                card.Content = card.Content ?? "";
                card.Hotkey = (card.Hotkey ?? "").Trim();
                card.Spacing = Math.Round(Settings.Clamp(card.Spacing, 0, 300));
                if (card.Extras is null)
                    card.Extras = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                Log.Warn($"卡片 Kind 未注册（插件缺失？），保留原配置: {card.Kind}");
                continue;
            }

            card.Name = string.IsNullOrWhiteSpace(card.Name) ? module.DisplayName : card.Name.Trim();
            card.Content = card.Content ?? "";
            if (!module.SupportsContent) card.Content = "";

            card.Hotkey = (card.Hotkey ?? "").Trim();
            if (module.IsInteractive) card.Resident = false;

            card.Spacing = Math.Round(Settings.Clamp(card.Spacing, 0, 300));

            // 卡片自己的设置（命令行工具 / 目录 / 超时……）由模块自己补默认值与夹紧，
            // 宿主不再认识 CommandSettings 这种具体类型。
            if (card.Extras is null)
                card.Extras = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var settingsModule = module as ICardSettingsModule;
            if (settingsModule is not null) settingsModule.InitializeCard(card);
        }

        if (settings.Cards.Count == 0) settings.Cards = CreateDefaultCards();

        int interactive = CountInteractive(settings.Cards);
        if (interactive > 1)
            Log.Warn($"有 {interactive} 张交互卡片（键盘独占），F9 / 托盘 / 卡片菜单只会打开第一张");
    }

    /// <summary>
    /// 数一数「键盘独占」的交互卡片（命令行；将来的画板 / 中转站若声明 IsInteractive 也算）。
    /// 宿主目前只认第一张——设置页据此拦住第二张。
    /// </summary>
    public static int CountInteractive(IEnumerable<CardSettings> cards)
    {
        int count = 0;
        if (cards is null) return 0;

        foreach (CardSettings card in cards)
        {
            if (card is null) continue;
            if (CardRegistry.TryGet(card.Kind, out var module) && module is not null && module.IsInteractive) count++;
        }
        return count;
    }

    /// <summary>
    /// 全新配置的默认卡片：由各模块通过 <see cref="ICardDefaultProvider"/> 自己提供，
    /// 宿主只按 <see cref="ICardDefaultProvider.DefaultOrder"/> 排序并补齐 Id / Extras / 归一化。
    ///
    /// M-1：以前这里写死 infotext / command / media 与三份模板，插件缺失时会落一张
    /// 「插件不可用」占位卡；现在没有模块提供默认卡就返回空列表，绝不伪造卡片。
    /// </summary>
    public static List<CardSettings> CreateDefaultCards()
    {
        var providers = new List<ICardDefaultProvider>();
        foreach (ICardModule module in CardRegistry.Modules)
        {
            var provider = module as ICardDefaultProvider;
            if (provider is not null) providers.Add(provider);
        }
        providers.Sort((a, b) => a.DefaultOrder.CompareTo(b.DefaultOrder));

        var cards = new List<CardSettings>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ICardDefaultProvider provider in providers)
        {
            IList<CardSettings> provided;
            try
            {
                provided = provider.CreateDefaults();
            }
            catch (Exception ex)
            {
                Log.Warn($"模块提供默认卡失败，已跳过: {ex.Message}");
                continue;
            }
            if (provided is null) continue;

            foreach (CardSettings card in provided)
            {
                CardSettings normalized = NormalizeDefaultCard(card, ids);
                if (normalized is not null) cards.Add(normalized);
            }
        }

        if (cards.Count == 0)
            Log.Warn("没有任何卡片模块提供默认卡（插件可能全部加载失败），本次配置将是空卡片列表");

        return cards;
    }

    /// <summary>把模块给的默认卡补成可直接落盘的形状；Kind 未注册 / 卡片为空则丢弃。</summary>
    static CardSettings NormalizeDefaultCard(CardSettings card, HashSet<string> ids)
    {
        if (card is null) return null;

        if (!CardRegistry.TryGet(card.Kind, out var module) || module is null)
        {
            Log.Warn($"默认卡的 Kind 未注册，已跳过: {(card.Kind ?? "")}");
            return null;
        }

        card.Kind = module.Kind;
        if (string.IsNullOrWhiteSpace(card.Id) || ids.Contains(card.Id))
            card.Id = NewId();
        ids.Add(card.Id);

        card.Name = string.IsNullOrWhiteSpace(card.Name) ? module.DisplayName : card.Name.Trim();
        card.Content = card.Content ?? "";
        if (!module.SupportsContent) card.Content = "";
        card.Hotkey = (card.Hotkey ?? "").Trim();
        if (module.IsInteractive) card.Resident = false;
        card.Spacing = Math.Round(Settings.Clamp(card.Spacing, 0, 300));

        if (card.Extras is null)
            card.Extras = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var settingsModule = module as ICardSettingsModule;
        if (settingsModule is not null) settingsModule.InitializeCard(card);

        return card;
    }

    /// <summary>按模块元数据新建一张卡片（设置页「添加卡片」用）。</summary>
    public static CardSettings Create(ICardModule module, IList<CardSettings> existing)
    {
        if (module is null) return null;

        var card = new CardSettings
        {
            Id = NewId(),
            Kind = module.Kind,
            Name = UniqueName(module.DisplayName, existing),
            Resident = module.IsInteractive ? false : module.DefaultResident,
            Hotkey = module.DefaultHotkey,
            Spacing = existing is null || existing.Count == 0 ? 0 : 12,
            Content = ""
        };

        if (module.IsInteractive) card.Resident = false;

        var settingsModule = module as ICardSettingsModule;
        if (settingsModule is not null) settingsModule.InitializeCard(card);

        return card;
    }

    /// <summary>同名卡片允许存在，这里只给新卡片加一个可读的后缀。</summary>
    public static string UniqueName(string baseName, IList<CardSettings> existing)
    {
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "卡片";
        if (existing is null || existing.Count == 0) return baseName;

        var used = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (CardSettings card in existing)
            if (card is not null) used.Add(card.Name ?? "");

        if (!used.Contains(baseName)) return baseName;
        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{baseName} {i}";
            if (!used.Contains(candidate)) return candidate;
        }
        return baseName;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");
}
