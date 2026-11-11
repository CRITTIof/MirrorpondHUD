using System;
using System.Reflection;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 卡片铭牌 / 系统页脚的唯一数据源：
/// 编号按设置里 Cards 的顺序（01 起），页脚只写真实版本与显示器序号，不编造延迟。
/// </summary>
public static class CardMeta
{
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
        string code = Code(settings, config);
        return code.Length == 0 ? "" : $"// {code} / ";
    }

    public static string Footer(Settings settings)
    {
        int monitor = settings is null ? 0 : Math.Max(0, settings.Overlay is null ? 0 : settings.Overlay.Monitor);
        Version version = Assembly.GetEntryAssembly()?.GetName().Version
                          ?? typeof(CardMeta).Assembly.GetName().Version;
        string text = version is null ? "v?" : "v" + version.ToString(3);
        return text + "  ·  MON " + (monitor + 1).ToString("00");
    }
}
