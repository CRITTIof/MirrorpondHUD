using System;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 2.46.1：把设置页外观包落到 WPF 资源。
///
/// 只改 Application.Resources 里的令牌/样式，不重建设置窗：
/// · 颜色交给 <see cref="SettingsTheme"/>（保留画刷引用，DynamicResource 立即刷新）；
/// · 几何/字体写资源（窗口圆角、导航宽、卡片圆角、按钮圆角）；
/// · recipe 只替换 Theme.xaml 中已存在的 Style，外部包不能注入 XAML。
/// </summary>
internal static class SettingsAppearanceRuntime
{
    public static void Apply(SettingsAppearancePack pack, bool dark, string previewBg)
    {
        if (pack is null) return;

        SettingsTheme.Apply(dark, previewBg, dark ? pack.Dark : pack.Light);

        ResourceDictionary resources = Application.Current?.Resources;
        if (resources is null) return;

        resources["SettingsWindowRadius"] = new CornerRadius(pack.WindowRadius);
        resources["SettingsInnerRadius"] = new CornerRadius(pack.InnerRadius);
        resources["SettingsCardRadius"] = new CornerRadius(pack.CardRadius);
        resources["SettingsNavItemRadius"] = new CornerRadius(pack.NavItemRadius);
        resources["SettingsPrimaryButtonRadius"] = new CornerRadius(pack.ButtonRadius);
        resources["SettingsNavWidth"] = new GridLength(pack.NavWidth);
        resources["SettingsNavItemHeight"] = pack.NavItemHeight;
        resources["SettingsNavBarWidth"] = pack.NavBarWidth;
        resources["SettingsDividerThickness"] = pack.DividerThickness;
        resources["SettingsDividerMargin"] = new Thickness(pack.NavWidth, 64, 0, 64);
        resources["AppFontFamily"] = HudFont.Resolve(pack.FontFamily);

        // recipe：分组标题样式按包替换；DynamicResource SectionTitle 的消费者会立即刷新。
        if (Application.Current.TryFindResource(pack.SectionTitleStyle) is Style section)
            resources["SectionTitle"] = section;
    }

    /// <summary>导航项不是全局样式（ListBox 显式指定），窗口拿到样式后自己赋给 NavList。</summary>
    public static Style ResolveNavItemStyle(SettingsAppearancePack pack)
    {
        if (pack is null) return null;
        return Application.Current?.TryFindResource(pack.NavItemStyle) as Style;
    }
}
