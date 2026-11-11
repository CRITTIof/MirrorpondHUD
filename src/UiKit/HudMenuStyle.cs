using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 托盘与卡片右键菜单共用的现代暗色样式。
/// 两处菜单都只保留四个入口；样式集中在这里，避免一个亮色默认菜单配一个暗色菜单。
/// </summary>
public static class HudMenuStyle
{
    public static void Apply(ContextMenu menu)
    {
        if (menu is null) return;

        var style = Application.Current?.TryFindResource("HudContextMenu") as Style;
        var itemStyle = Application.Current?.TryFindResource("HudMenuItem") as Style;
        if (style is null || itemStyle is null)
        {
            Log.Warn("右键菜单样式缺失，本次使用系统默认菜单");
            return;
        }

        menu.Style = style;
        menu.ItemContainerStyle = itemStyle;
    }
}
