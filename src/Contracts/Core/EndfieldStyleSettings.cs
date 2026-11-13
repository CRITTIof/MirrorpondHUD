using System;

namespace MirrorpondHUD.Core;

/// <summary>
/// 终末地视觉语言（2.35.0 的 M2–M8）的独立开关模块。
///
/// 这些效果在 2.35.0 里是写死的；2.36.0 把它们拆成可分别关闭的选项。
/// 默认全部开启 = 保持 2.35.0 的观感；关闭某一项时回落到该效果引入前的旧实现，
/// 而不是让控件失去样式。
/// </summary>
public sealed class EndfieldStyleSettings
{
    /// <summary>M2：零偏移环境阴影。关闭后回到从上方打光的旧投影。</summary>
    public bool Shadow = true;

    /// <summary>M3：列表 / 表格 / 树选中左缘条；大/中图标网格改用下缘条。关闭后只保留旧版强调色淡底。</summary>
    public bool SelectionBar = true;

    /// <summary>M4：工业按钮（实心主行动、深色次行动、4px 微圆角）。关闭后回到描边按钮。</summary>
    public bool Buttons = true;

    /// <summary>M5：直角分段进度。关闭后回到 3px 半圆端头进度条。</summary>
    public bool SegmentProgress = true;

    /// <summary>M6：卡片编号与系统页脚。关闭后所有卡片都不再显示这一行。</summary>
    public bool Meta = true;

    /// <summary>M7：错误 / 警告状态的角括号，以及媒体异常时的四角记号。</summary>
    public bool CornerBrackets = true;

    /// <summary>M8：主行动按钮与角标的 45° 切角。关闭后保留圆角矩形。</summary>
    public bool CutCorners = true;

    /// <summary>
    /// 2.46.2：卡片外轮廓。CutAngle = 右上/右下 45° 切角；RightAngle = 全直角；
    /// RoundAngle = 全圆角（默认，保持现有观感）。只影响卡片外形，不改内容布局。
    /// </summary>
    public string CardShape = "RoundAngle";

    /// <summary>2.46.2：左侧信号黄强调条（设计稿三套都有；默认关闭以保持旧观感）。</summary>
    public bool AccentBar = false;

    /// <summary>
    /// 页脚模板。空 = 默认 <c>v{version} · MON {monitor}</c>。
    /// 支持 <c>{version}</c> / <c>{monitor}</c> / <c>{code}</c> / <c>{name}</c>；
    /// 未知占位符原样保留，方便用户自己排查拼写。
    /// </summary>
    public string FooterText = "";

    public void Normalize()
    {
        FooterText = (FooterText ?? "").Trim();
        if (FooterText.Length > 160) FooterText = FooterText.Substring(0, 160);
        CardShape = Settings.NormalizeCardShape(CardShape);
    }

    public EndfieldStyleSettings Clone() => new()
    {
        Shadow = Shadow,
        SelectionBar = SelectionBar,
        Buttons = Buttons,
        SegmentProgress = SegmentProgress,
        Meta = Meta,
        CornerBrackets = CornerBrackets,
        CutCorners = CutCorners,
        CardShape = CardShape,
        AccentBar = AccentBar,
        FooterText = FooterText
    };
}
