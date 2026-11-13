using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core;

/// <summary>
/// 2.46.1：设置页外观包（Settings Appearance）。
///
/// 与卡片外观包分开：设置页有自己的明暗两套调色板、几何令牌和组件 recipe。
/// 当前 recipe 只覆盖导航项 / 分组标题 / 主按钮圆角，后续可继续加 key，不改设置窗结构。
/// palette 用“覆盖默认 Endfield 调色板的键值”表示，缺省键自动回退，避免每套风格抄 30 个颜色。
/// </summary>
public sealed class SettingsAppearancePack
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public string FontFamily = "Noto Sans SC";

    public double WindowRadius = 22;
    public double InnerRadius = 7;
    public double CardRadius = 8;
    public double NavWidth = 220;
    public double NavItemHeight = 46;
    public double NavItemRadius = 7;
    public double NavBarWidth = 3;
    public double DividerThickness = 2;
    public double ButtonRadius = 4;

    /// <summary>导航项 / 分组标题 / 主按钮的 recipe 资源键；内置包对应 Theme.xaml 中的 Style。</summary>
    public string NavItemStyle = "SettingsNavItem";
    public string SectionTitleStyle = "SectionTitle";

    public Dictionary<string, string> Dark = new();
    public Dictionary<string, string> Light = new();
}

/// <summary>
/// 内置设置页外观目录。默认 Endfield 使用现有 SettingsTheme 调色板与 22px 圆角；
/// Arknights 使用硬边、琥珀强调与更窄的导航——用来证明 recipe 层可替换，而不是只能换色。
/// </summary>
public static class SettingsAppearanceCatalog
{
    static List<SettingsAppearancePack> _cache;

    public static List<SettingsAppearancePack> Load()
    {
        List<SettingsAppearancePack> cached = _cache;
        if (cached is not null) return new List<SettingsAppearancePack>(cached);

        var list = new List<SettingsAppearancePack>
        {
            new()
            {
                Id = "endfield",
                Name = "终末地",
                Description = "22px 圆角、信号黄、3px 竖条的现有基线。"
            },
            new()
            {
                Id = "arknights",
                Name = "明日方舟 · 硬边",
                Description = "硬边窗口、琥珀强调、窄导航与方形分组标记。",
                WindowRadius = 4,
                InnerRadius = 2,
                CardRadius = 2,
                NavWidth = 210,
                NavItemHeight = 44,
                NavItemRadius = 0,
                NavBarWidth = 4,
                DividerThickness = 1,
                ButtonRadius = 0,
                NavItemStyle = "SettingsNavItemArknights",
                SectionTitleStyle = "SectionTitleArknights",
                Dark = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Accent"] = "#FFB400",
                    ["AccentSoft"] = "#2A2110",
                    ["AccentText"] = "#111111",
                    ["WindowBg"] = "#0B0B0C",
                    ["CardBg"] = "#151516",
                    ["BorderSoft"] = "#333338",
                    ["Divider"] = "#3A3A40",
                    ["TextPrimary"] = "#F2F2F2",
                    ["TextSecondary"] = "#9A9AA0",
                    ["PanelBg"] = "#1B1B1D",
                    ["HeaderBg"] = "#18181A",
                    ["HeaderBorder"] = "#303036",
                    ["BadgeBg"] = "#25252A",
                    ["HoverBg"] = "#232328",
                    ["PressedBg"] = "#2B2B31",
                    ["AccentHover"] = "#FFC333",
                    ["AccentPressed"] = "#D99A00",
                    ["DisabledBg"] = "#141416",
                    ["DisabledText"] = "#66666E",
                    ["SelectedBg"] = "#2A2110",
                    ["SelectedStrongBg"] = "#3A2D14",
                    ["InputBg"] = "#101012",
                    ["PopupBg"] = "#17171A",
                    ["PrimaryBg"] = "#FFB400",
                    ["PrimaryText"] = "#111111",
                    ["PrimaryBorder"] = "#FFB400",
                    ["PrimaryHover"] = "#FFC333",
                    ["PrimaryPressed"] = "#D99A00",
                    ["SliderTrack"] = "#333338",
                    ["Scrollbar"] = "#3A3A40",
                    ["ScrollbarHot"] = "#606068"
                },
                Light = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Accent"] = "#C47A00",
                    ["AccentSoft"] = "#F7E8CC",
                    ["AccentText"] = "#111111",
                    ["WindowBg"] = "#F4F4F0",
                    ["CardBg"] = "#FFFFFF",
                    ["BorderSoft"] = "#D8D8D2",
                    ["Divider"] = "#E4E4DE",
                    ["TextPrimary"] = "#111111",
                    ["TextSecondary"] = "#66665E",
                    ["PanelBg"] = "#F7F7F2",
                    ["HeaderBg"] = "#FAFAF6",
                    ["HeaderBorder"] = "#E5E5DE",
                    ["BadgeBg"] = "#EFEFE7",
                    ["HoverBg"] = "#EEEEE8",
                    ["PressedBg"] = "#E2E2DA",
                    ["AccentHover"] = "#D98A00",
                    ["AccentPressed"] = "#A86400",
                    ["DisabledBg"] = "#EDEDE6",
                    ["DisabledText"] = "#9A9A90",
                    ["SelectedBg"] = "#F7E8CC",
                    ["SelectedStrongBg"] = "#EFD8AC",
                    ["InputBg"] = "#FFFFFF",
                    ["PopupBg"] = "#FFFFFF",
                    ["PrimaryBg"] = "#FFB400",
                    ["PrimaryText"] = "#111111",
                    ["PrimaryBorder"] = "#D99A00",
                    ["PrimaryHover"] = "#FFC333",
                    ["PrimaryPressed"] = "#D99A00",
                    ["SliderTrack"] = "#D8D8D2",
                    ["Scrollbar"] = "#C9C9C0",
                    ["ScrollbarHot"] = "#9C9C91"
                }
            }
        };

        _cache = list;
        return new List<SettingsAppearancePack>(list);
    }

    public static SettingsAppearancePack Find(string id)
    {
        List<SettingsAppearancePack> packs = Load();
        foreach (SettingsAppearancePack pack in packs)
            if (string.Equals(pack.Id, id, StringComparison.OrdinalIgnoreCase)) return pack;
        return packs[0];
    }
}
