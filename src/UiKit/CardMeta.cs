using System;
using System.Reflection;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 卡片铭牌 / 系统页脚的唯一数据源：
/// 编号按设置里 Cards 的顺序（01 起），页脚内容来自 <see cref="EndfieldStyleSettings.FooterText"/>。
/// 信息与文本卡（infotext）是纯文字卡，不显示页头页脚；M6 总开关关闭时所有卡片都不显示。
/// </summary>
public static class CardMeta
{
    /// <summary>这张卡是否应该显示 M6 铭牌。信息 / 文本卡始终不显示。</summary>
    public static bool Enabled(Settings settings, CardSettings config)
    {
        if (settings is null || config is null) return false;
        if (!EndfieldVisuals.Meta(settings.Style)) return false;
        return !string.Equals(config.Kind, "infotext", StringComparison.OrdinalIgnoreCase);
    }

    public static string Code(Settings settings, CardSettings config)
    {
        if (settings is null || config is null || settings.Cards is null) return "";

        for (int i = 0; i < settings.Cards.Count; i++)
        {
            CardSettings item = settings.Cards[i];
            if (item is null) continue;
            if (ReferenceEquals(item, config)) return (i + 1).ToString("00");
            if (!string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(config.Id)
                && string.Equals(item.Id, config.Id, StringComparison.OrdinalIgnoreCase))
                return (i + 1).ToString("00");
        }
        return "";
    }

    public static string Header(Settings settings, CardSettings config)
    {
        if (!Enabled(settings, config)) return "";
        string code = Code(settings, config);
        return code.Length == 0 ? "" : $"// {code} / ";
    }

    /// <summary>
    /// 系统页脚。空模板 = <c>v{version} · MON {monitor}</c>。
    /// 支持 <c>{version}</c> / <c>{monitor}</c> / <c>{code}</c> / <c>{name}</c>，未知占位符原样保留。
    /// </summary>
    public static string Footer(Settings settings, CardSettings config = null)
    {
        if (settings is null || !EndfieldVisuals.Meta(settings.Style)) return "";

        string template = EndfieldVisuals.FooterText(settings.Style);
        if (string.IsNullOrWhiteSpace(template)) template = "v{version} · MON {monitor}";

        Version version = Assembly.GetEntryAssembly()?.GetName().Version
                          ?? typeof(CardMeta).Assembly.GetName().Version;
        string versionText = version is null ? "?" : version.ToString(3);
        int monitor = Math.Max(0, settings.Overlay is null ? 0 : settings.Overlay.Monitor);
        string code = config is null ? "" : Code(settings, config);

        return template
            .Replace("{version}", versionText)
            .Replace("{monitor}", (monitor + 1).ToString("00"))
            .Replace("{code}", code)
            .Replace("{name}", config is null ? "" : config.Name ?? "");
    }
}
