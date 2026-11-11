using System;
using System.Windows;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 「外观 → 终末地视觉」的装载 / 读取 / 上下文显示。
/// 从 SettingsWindow.xaml.cs 拆出来，主文件只保留 Hook / Load / Read 的总入口，
/// 新增 M2–M8 开关时不再往总入口里塞第二组字段。
/// </summary>
public partial class SettingsWindow
{
    void HookEndfieldEvents()
    {
        EndfieldShadowCheck.Checked += (s, e) => UiChanged();
        EndfieldShadowCheck.Unchecked += (s, e) => UiChanged();
        EndfieldSelectionCheck.Checked += (s, e) => UiChanged();
        EndfieldSelectionCheck.Unchecked += (s, e) => UiChanged();
        EndfieldButtonCheck.Checked += (s, e) => UiChanged();
        EndfieldButtonCheck.Unchecked += (s, e) => UiChanged();
        EndfieldSegmentCheck.Checked += (s, e) => UiChanged();
        EndfieldSegmentCheck.Unchecked += (s, e) => UiChanged();
        EndfieldBracketCheck.Checked += (s, e) => UiChanged();
        EndfieldBracketCheck.Unchecked += (s, e) => UiChanged();
        EndfieldCutCheck.Checked += (s, e) => UiChanged();
        EndfieldCutCheck.Unchecked += (s, e) => UiChanged();
        EndfieldMetaCheck.Checked += (s, e) => { UiChanged(); UpdateEndfieldUiState(); };
        EndfieldMetaCheck.Unchecked += (s, e) => { UiChanged(); UpdateEndfieldUiState(); };
        FooterTextBox.TextChanged += (s, e) => UiChanged();
    }

    void LoadEndfieldUi(StyleSettings style)
    {
        EndfieldStyleSettings flags = style is null ? new EndfieldStyleSettings() : style.Endfield;
        if (flags is null) flags = new EndfieldStyleSettings();

        EndfieldShadowCheck.IsChecked = flags.Shadow;
        EndfieldSelectionCheck.IsChecked = flags.SelectionBar;
        EndfieldButtonCheck.IsChecked = flags.Buttons;
        EndfieldSegmentCheck.IsChecked = flags.SegmentProgress;
        EndfieldMetaCheck.IsChecked = flags.Meta;
        EndfieldBracketCheck.IsChecked = flags.CornerBrackets;
        EndfieldCutCheck.IsChecked = flags.CutCorners;
        FooterTextBox.Text = flags.FooterText ?? "";
        UpdateEndfieldUiState();
    }

    void ReadEndfieldUi(StyleSettings style)
    {
        if (style is null) return;

        style.Endfield ??= new EndfieldStyleSettings();
        EndfieldStyleSettings flags = style.Endfield;
        flags.Shadow = EndfieldShadowCheck.IsChecked == true;
        flags.SelectionBar = EndfieldSelectionCheck.IsChecked == true;
        flags.Buttons = EndfieldButtonCheck.IsChecked == true;
        flags.SegmentProgress = EndfieldSegmentCheck.IsChecked == true;
        flags.Meta = EndfieldMetaCheck.IsChecked == true;
        flags.CornerBrackets = EndfieldBracketCheck.IsChecked == true;
        flags.CutCorners = EndfieldCutCheck.IsChecked == true;
        flags.FooterText = (FooterTextBox.Text ?? "").Trim();
        flags.Normalize();
    }

    /// <summary>页脚模板只在「编号与页脚」开启时有意义，关闭时整行收起，保持设置页干净。</summary>
    void UpdateEndfieldUiState()
    {
        if (FooterTextBoxRow is null) return;
        bool meta = EndfieldMetaCheck is not null && EndfieldMetaCheck.IsChecked == true;
        FooterTextBoxRow.Visibility = meta ? Visibility.Visible : Visibility.Collapsed;
    }
}
