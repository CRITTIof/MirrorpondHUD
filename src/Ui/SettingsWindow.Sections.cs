using System;
using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 把设置页现有的 SectionTitle + 后续控件自动包成可折叠的 SettingsSection。
/// 这样所有页 / 所有分组一次生效，不需要在 XAML 里为每个分组重复写容器；
/// 以后新增分组仍按原来的 SectionTitle 写法即可。
/// </summary>
public partial class SettingsWindow
{
    void BuildCollapsibleSections()
    {
        Style titleStyle = TryFindResource("SectionTitle") as Style;
        Style sectionStyle = TryFindResource("SettingsSection") as Style;
        if (titleStyle is null || sectionStyle is null)
        {
            Log.Warn("设置分组样式缺失，本轮不生成可折叠分组");
            return;
        }

        int count = 0;
        GroupSections(Tabs, titleStyle, sectionStyle, ref count);
        Log.Info($"设置页已生成 {count} 个可折叠分组");
    }

    static void GroupSections(DependencyObject root, Style titleStyle, Style sectionStyle, ref int count)
    {
        if (root is null) return;

        // 先递归子级：嵌套的“等高线”等分组会在自己的 StackPanel 里先包好，
        // 再作为父分组内容的一部分被搬走。
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependency) GroupSections(dependency, titleStyle, sectionStyle, ref count);
        }

        if (root is not StackPanel panel) return;

        int index = 0;
        while (index < panel.Children.Count)
        {
            if (panel.Children[index] is not TextBlock title || !ReferenceEquals(title.Style, titleStyle))
            {
                index++;
                continue;
            }

            var body = new StackPanel { Orientation = Orientation.Vertical };
            int next = index + 1;
            while (next < panel.Children.Count)
            {
                if (panel.Children[next] is TextBlock candidate && ReferenceEquals(candidate.Style, titleStyle))
                    break;

                UIElement element = panel.Children[next];
                panel.Children.RemoveAt(next);
                body.Children.Add(element);
            }

            var section = new Expander
            {
                Header = title.Text,
                Content = body,
                Style = sectionStyle,
                IsExpanded = true,
                Margin = new Thickness(0, title.Margin.Top, 0, 4)
            };

            panel.Children.RemoveAt(index);
            panel.Children.Insert(index, section);
            count++;
            index++;
        }
    }
}
