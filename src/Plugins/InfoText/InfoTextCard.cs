using System;
using System.Collections.Generic;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards;

/// <summary>「信息与文本」的四种预设。</summary>
public sealed class InfoTextPreset
{
    public InfoTextPreset(string id, string label, string defaultContent, string icon)
    {
        Id = id;
        Label = label;
        DefaultContent = defaultContent;
        Icon = icon;
    }

    public string Id { get; private set; }
    public string Label { get; private set; }
    public string DefaultContent { get; private set; }
    public string Icon { get; private set; }
}

/// <summary>
/// 信息与文本卡：把原来的系统信息 / 时钟 / 按键 / 文本四种文字卡合并成一种，
/// 用卡片自己的 `Preset` 选择预设。四种预设都是模板渲染，所以内容、占位符、
/// 图标倍率、描边等文字能力完全一致。
/// </summary>
public sealed class InfoTextCard : CardModuleBase, ICardSettingsModule,
                                ICardPresetSource, ICardSelfTest, ICardDefaultProvider
{
    static readonly List<InfoTextPreset> PresetList = new()
    {
        new InfoTextPreset("metrics", "系统信息",
            "{cpu_icon} {cpu:0.0}%   {mem_icon} {mem_pct:0}%   {gpu_icon} {gpu:0.0}%\n" +
            "{net_down_icon} {net_down}   {net_up_icon} {net_up}   {disk_icon} {disk:0.0}%",
            Icons.Cpu),
        new InfoTextPreset("clock", "时钟",
            "{time}  {date}\n{weekday_zh}  {uptime}",
            Icons.Clock),
        new InfoTextPreset("keys", "按键", "KEY {keys}", Icons.TabHotkey),
        new InfoTextPreset("text", "文本信息", "{host} · {user}", Icons.Txt)
    };

    public static IList<InfoTextPreset> AllPresets => PresetList;

    public override string Kind => "infotext";
    public override string DisplayName => "信息与文本";
    public override string Icon => Icons.Txt;
    public override string DefaultContent => DefaultFor("metrics");
    public override int Order => 0;

    // ==================== 默认卡（M-1） ====================

    /// <summary>默认配置里三张信息卡排在命令行 / 媒体之前。</summary>
    public int DefaultOrder => 0;

    /// <summary>
    /// 2.29.0 起默认卡由插件自己提供：系统信息 / 时钟 / 按键三张。
    /// 模板就是 <see cref="PresetList"/> 里那一份，宿主不再维护第二份 CardDefaults。
    /// </summary>
    public IList<CardSettings> CreateDefaults()
    {
        var cards = new List<CardSettings>();
        cards.Add(CreateDefaultCard("metrics", 0));
        cards.Add(CreateDefaultCard("clock", 12));
        cards.Add(CreateDefaultCard("keys", 12));
        return cards;
    }

    CardSettings CreateDefaultCard(string preset, double spacing)
    {
        InfoTextPreset item = Find(preset);
        return new CardSettings
        {
            Kind = Kind,
            Preset = item.Id,
            Name = $"{DisplayName} · {item.Label}",
            Resident = true,
            Spacing = spacing,
            Content = item.DefaultContent
        };
    }

    /// <summary>
    /// 按预设决定重画节奏：系统信息跟着采样走（每帧刷），时钟 / 文本每秒一次就够，
    /// 按键要跟手一点（250 ms）。省下的是模板渲染与字符串构造，不是「卡顿」。
    /// </summary>
    public override int RefreshIntervalMs(CardSettings config)
    {
        switch (NormalizePreset(config is null ? null : config.Preset))
        {
            case "keys": return 250;
            case "metrics": return 0;
            default: return 1000;
        }
    }

    // ==================== 模块自己的设置 ====================

    /// <summary>
    /// 预设归一化：内容恰好等于某个预设的默认内容时，把预设纠回那一个；
    /// 未知 / 空值归到系统信息。
    /// </summary>
    public void InitializeCard(CardSettings config)
    {
        if (config is null) return;
        config.Preset = InferPreset(config.Preset, config.Content);
    }

    public override string Render(CardRenderContext context)
    {
        string preset = NormalizePreset(context is null || context.Config is null ? null : context.Config.Preset);
        return context.RenderOrDefault(DefaultFor(preset));
    }

    // ==================== 预设契约 / 自检 ====================

    static readonly IList<CardPreset> ContractPresets = PresetList.ConvertAll(p =>
    {
        var preset = new CardPreset(p.Id, p.Label, p.DefaultContent, p.Icon);
        preset.EditableContent = p.Id != "keys";
        return preset;
    });

    public IList<CardPreset> Presets => ContractPresets;

    public string CurrentPreset(CardSettings config) => NormalizePreset(config is null ? null : config.Preset);

    public void ApplyPreset(CardSettings config, string presetId)
    {
        if (config is null) return;
        string id = NormalizePreset(presetId);
        config.Preset = id;
        config.Content = DefaultFor(id);
    }

    public string RunSelfTest()
    {
        if (NormalizePreset("nope") != "metrics") return "未知预设没有归一到系统信息";
        if (InferPreset("clock", DefaultFor("metrics")) != "metrics") return "默认内容没有纠正预设";
        if (DefaultFor("keys") != "KEY {keys}") return "按键预设默认内容不一致";
        if (ContractPresets.Count != 4) return "预设契约数量不是 4";
        return null;
    }

    /// <summary>未知 / 空的预设一律归到系统信息。</summary>
    public static string NormalizePreset(string value)
    {
        string key = (value ?? "").Trim().ToLowerInvariant();
        foreach (InfoTextPreset preset in PresetList)
        {
            if (preset.Id == key) return preset.Id;
        }
        return "metrics";
    }

    /// <summary>
    /// 预设归一化：内容恰好等于某个预设的默认内容时，把预设纠回那一个；
    /// 未知 / 空值归到系统信息。
    /// </summary>
    public static string InferPreset(string preset, string content)
    {
        string key = NormalizePreset(preset);
        string text = (content ?? "").Trim();
        if (text.Length == 0) return key;

        foreach (InfoTextPreset item in PresetList)
        {
            if (item.Id == key) continue;
            if (string.Equals(item.DefaultContent.Trim(), text, StringComparison.Ordinal))
                return item.Id;
        }
        return key;
    }

    public static InfoTextPreset Find(string value)
    {
        string key = NormalizePreset(value);
        foreach (InfoTextPreset preset in PresetList)
        {
            if (preset.Id == key) return preset;
        }
        return PresetList[0];
    }

    public static string DefaultFor(string preset) => Find(preset).DefaultContent;

    public static string LabelFor(string preset) => Find(preset).Label;

    public static string IconFor(string preset) => Find(preset).Icon;
}
