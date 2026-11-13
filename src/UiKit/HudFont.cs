using System;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 字体解析：应用内只认一套“逻辑字体名”，由这里落到真正的 FontFamily。
///
/// 为什么需要它：思源黑体（Noto Sans SC，SIL OFL 1.1）作为 Resource 编进程序集，
/// 不能像系统字体那样直接 <c>new FontFamily("Noto Sans SC")</c> 解析；
/// 必须带 pack URI 指向 assets/fonts 才会加载嵌入副本。其它字体仍按系统字体解析，
/// 解析失败回退嵌入字体，避免又回到微软雅黑。
/// </summary>
public static class HudFont
{
    public const string DefaultName = "Noto Sans SC";

    /// <summary>界面展示名（设置页字体下拉）。</summary>
    public const string DisplayName = "思源黑体 / Noto Sans SC";

    static readonly FontFamily Embedded = CreateEmbedded();

    /// <summary>嵌入的思源黑体（等价 Noto Sans SC）。</summary>
    public static FontFamily Default => Embedded;

    /// <summary>
    /// 把设置里的字体名解析成 FontFamily：思源黑体走嵌入副本，其它走系统字体；
    /// 空值 / 非法值回退嵌入字体，日志由调用方按需记录。
    /// </summary>
    public static FontFamily Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Embedded;

        string trimmed = name.Trim();
        if (string.Equals(trimmed, DefaultName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "思源黑体", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "Source Han Sans SC", StringComparison.OrdinalIgnoreCase))
        {
            return Embedded;
        }

        try { return new FontFamily(trimmed); }
        catch { return Embedded; }
    }

    static FontFamily CreateEmbedded()
    {
        try
        {
            // 与键帽 / Lucide 字体同样的 pack URI 形式；字体族名取 TTF 内部的 Noto Sans SC。
            return new FontFamily(new Uri("pack://application:,,,/"),
                                  "./assets/fonts/#Noto Sans SC");
        }
        catch (Exception ex)
        {
            // 字体资源缺失是发布配置错误；这里不能抛到 UI 初始化里，回退系统黑体并留日志。
            Log.Warn($"嵌入思源黑体加载失败，回退系统字体: {ex.Message}");
            return new FontFamily("Microsoft YaHei UI");
        }
    }
}
