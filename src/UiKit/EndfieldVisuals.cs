using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 终末地 M2–M8 开关的唯一读取入口。
///
/// 控件只问“这一项开没开”，不直接摸 <see cref="StyleSettings.Endfield"/>；
/// 缺省、旧配置、null 场景统一在这里回落为开启（= 2.35.0 观感），
/// 避免每个控件各写一份 null 判断导致同卡不同调。
/// </summary>
public static class EndfieldVisuals
{
    static EndfieldStyleSettings Flags(StyleSettings style) =>
        style is null ? null : style.Endfield;

    public static bool Shadow(StyleSettings style) => Flags(style)?.Shadow ?? true;

    public static bool SelectionBar(StyleSettings style) => Flags(style)?.SelectionBar ?? true;

    public static bool Buttons(StyleSettings style) => Flags(style)?.Buttons ?? true;

    public static bool SegmentProgress(StyleSettings style) => Flags(style)?.SegmentProgress ?? true;

    public static bool Meta(StyleSettings style) => Flags(style)?.Meta ?? true;

    public static bool CornerBrackets(StyleSettings style) => Flags(style)?.CornerBrackets ?? true;

    public static bool CutCorners(StyleSettings style) => Flags(style)?.CutCorners ?? true;

    public static string FooterText(StyleSettings style) => Flags(style)?.FooterText ?? "";
}
