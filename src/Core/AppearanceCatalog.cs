using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core;

/// <summary>
/// 2.46.0：外观包（前期脚手架）。
///
/// 一个外观包 = 一套主题令牌 + 一组卡片 chrome 配方。现阶段主题令牌直接复用
/// <see cref="ThemeDocument"/>（.mptheme），chrome 复用 <see cref="EndfieldStyleSettings"/> 的
/// 开关集合；后续新增风格只需要在 <see cref="AppearanceCatalog"/> 里登记包并指向新的主题文档，
/// 不需要改卡片渲染代码。设置窗的组件配方（settings.xaml）留到下一阶段接入。
/// </summary>
public sealed class AppearancePack
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public string ThemeId = "";
    public EndfieldStyleSettings Chrome = new();
}

/// <summary>
/// 内置外观包目录。主题文档仍由 <see cref="ThemeCatalog"/> 提供，这里只做“外观包 → 主题 + chrome”的映射。
/// 找不到对应主题的包会被跳过并留 Warn，避免选到一个空外观。
/// </summary>
public static class AppearanceCatalog
{
    static List<AppearancePack> _cache;

    public static List<AppearancePack> Load()
    {
        List<AppearancePack> cached = _cache;
        if (cached is not null) return new List<AppearancePack>(cached);

        List<ThemePreset> themes = ThemeCatalog.Load();
        var list = new List<AppearancePack>
        {
            Make("endfield-ink", "终末地 · 墨", "暗色工业基线：切角、选中条与系统页脚全开。", "endfield-ink", true),
            Make("endfield-paper", "终末地 · 纸", "亮色纸张基线：与墨版共用几何与 chrome，只换令牌。", "endfield-paper", true)
        };

        var valid = new List<AppearancePack>();
        foreach (AppearancePack pack in list)
        {
            bool exists = themes.Exists(t => string.Equals(t.Id, pack.ThemeId, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                Log.Warn($"外观包 {pack.Id} 指向的主题 {pack.ThemeId} 不存在，已跳过");
                continue;
            }
            valid.Add(pack);
        }

        _cache = valid;
        return new List<AppearancePack>(valid);
    }

    public static AppearancePack Find(string id)
    {
        List<AppearancePack> packs = Load();
        foreach (AppearancePack pack in packs)
        {
            if (string.Equals(pack.Id, id, StringComparison.OrdinalIgnoreCase)) return pack;
        }
        return packs.Count > 0 ? packs[0] : null;
    }

    static AppearancePack Make(string id, string name, string description, string themeId, bool chrome) =>
        new AppearancePack
        {
            Id = id,
            Name = name,
            Description = description,
            ThemeId = themeId,
            Chrome = new EndfieldStyleSettings
            {
                Shadow = chrome,
                SelectionBar = chrome,
                Buttons = chrome,
                SegmentProgress = chrome,
                Meta = chrome,
                CornerBrackets = chrome,
                CutCorners = chrome
            }
        };
}

/// <summary>
/// 应用外观包：调用方必须传入**克隆后的** Settings；这里先套主题令牌，再落 chrome 与身份字段。
/// 返回 false 表示包/主题不可用，调用方保留原设置并显示错误。
/// </summary>
public static class AppearanceRuntime
{
    public static bool TryApply(Settings settings, AppearancePack pack, out string error)
    {
        error = null;
        if (settings is null || settings.Style is null)
        {
            error = "设置树为空";
            return false;
        }
        if (pack is null)
        {
            error = "外观包不存在";
            return false;
        }

        ThemePreset theme = ThemeCatalog.Load().Find(
            t => string.Equals(t.Id, pack.ThemeId, StringComparison.OrdinalIgnoreCase));
        if (theme is null || theme.Document is null)
        {
            error = $"外观包 {pack.Id} 的主题 {pack.ThemeId} 不可用";
            return false;
        }

        theme.Document.ApplyTo(settings);
        EndfieldStyleSettings src = pack.Chrome ?? new EndfieldStyleSettings();
        settings.Style.Endfield = new EndfieldStyleSettings
        {
            Shadow = src.Shadow,
            SelectionBar = src.SelectionBar,
            Buttons = src.Buttons,
            SegmentProgress = src.SegmentProgress,
            Meta = src.Meta,
            CornerBrackets = src.CornerBrackets,
            CutCorners = src.CutCorners,
            FooterText = src.FooterText
        };
        settings.Style.AppearanceId = pack.Id;
        settings.Style.ThemeName = pack.Name;
        return true;
    }
}
