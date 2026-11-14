using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗「热键」页：三个全局热键的录制 / 清空，以及卡片模块自己声明动作的热键面板。
/// 每张卡片自己的「呼出热键」属于「内容」页，见 <c>SettingsWindow.Cards.cs</c>。
/// </summary>
public partial class SettingsWindow
{
    readonly List<KeyValuePair<string, TextBox>> _cardHotkeyBoxes = new();

    /// <summary>
    /// 按「有这种卡片」的模块生成热键分组：每组前面一条分割线 + 标题，
    /// 组内的动作由模块自己声明（目前是媒体卡的播放 / 暂停、前进、后退、加速、减速）。
    /// </summary>
    void BuildCardHotkeyPanel()
    {
        if (CardHotkeyPanel is null) return;

        MoveCardHotkeyPanelOutOfGlobalGroup();
        CardHotkeyPanel.Children.Clear();
        _cardHotkeyBoxes.Clear();

        if (_working is null) return;

        var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CardSettings card in _working.Cards)
        {
            if (card is not null && !string.IsNullOrWhiteSpace(card.Kind)) kinds.Add(card.Kind);
        }

        Style sectionStyle = null;
        try
        {
            sectionStyle = FindResource("SettingsSection") as Style;
        }
        catch (Exception ex)
        {
            // 资源字典还没合并（极早的热键面板重建）：区块退化为默认样式，内容仍然可见。
            Log.Debug($"读取 SettingsSection 样式失败，使用默认样式: {ex.Message}");
        }

        bool any = false;
        foreach (ICardModule module in CardRegistry.Modules)
        {
            var source = module as ICardHotkeySource;
            if (source is null || source.HotkeyActions is null || source.HotkeyActions.Count == 0) continue;
            if (!kinds.Contains(module.Kind)) continue;

            if (!any)
            {
                CardHotkeyPanel.Children.Add(new TextBlock
                {
                    Text = "卡片定义的热键",
                    Style = FindResource("Hint") as Style,
                    Margin = new Thickness(0, 10, 0, 4)
                });
                any = true;
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            int row = 0;
            foreach (CardHotkeyAction action in source.HotkeyActions)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = action.Label,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var box = new TextBox
                {
                    IsReadOnly = true,
                    Text = _working.Hotkeys.GetCardAction(action.Action),
                    Tag = action.Action,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                box.PreviewKeyDown += HotkeyBoxPreviewKeyDown;

                Grid.SetRow(label, row);
                Grid.SetColumn(label, 0);
                Grid.SetRow(box, row);
                Grid.SetColumn(box, 1);

                var clear = new Button
                {
                    Content = "清空",
                    Tag = box,
                    Padding = new Thickness(10, 2, 10, 2),
                    Margin = new Thickness(8, 0, 0, 10),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "清除这个热键"
                };
                clear.Click += ClearHotkeyBoxClick;

                grid.Children.Add(label);
                grid.Children.Add(box);
                grid.Children.Add(clear);

                Grid.SetRow(clear, row);
                Grid.SetColumn(clear, 2);

                _cardHotkeyBoxes.Add(new KeyValuePair<string, TextBox>(action.Action, box));
                row++;
            }

            var section = new Expander
            {
                Header = $"{module.DisplayName}卡片",
                Content = grid,
                Style = sectionStyle,
                IsExpanded = true
            };
            CardHotkeyPanel.Children.Add(section);
        }
    }

    /// <summary>
    /// XAML 里 CardHotkeyPanel 暂放在“全局”组的内容里；第一次构建时把它提到页面级，
    /// 让每张卡片的热键成为与“全局”同级的独立折叠组。
    /// </summary>
    void MoveCardHotkeyPanelOutOfGlobalGroup()
    {
        if (CardHotkeyPanel.Parent is not Panel body) return;

        Expander global = null;
        DependencyObject current = body;
        while (current is not null)
        {
            if (current is Expander expander)
            {
                global = expander;
                break;
            }
            current = LogicalTreeHelper.GetParent(current);
        }

        if (global?.Parent is not Panel host) return;

        body.Children.Remove(CardHotkeyPanel);
        int index = host.Children.IndexOf(global);
        if (index < 0) index = host.Children.Count - 1;
        host.Children.Insert(index + 1, CardHotkeyPanel);
        CardHotkeyPanel.Margin = new Thickness(0, 8, 0, 0);
    }

    void HotkeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = sender as TextBox;
        if (box is null) return;

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            box.Text = "";
            MarkDirty();
            ValidateHotkeyBox(box);
            return;
        }

        if (IsModifierKey(key)) return;

        ModifierKeys mods = Keyboard.Modifiers;
        box.Text = HotkeyManager.Format(key, mods);
        box.CaretIndex = box.Text.Length;
        MarkDirty();
        ValidateHotkeyBox(box);
    }

    /// <summary>「清空」按钮：按钮的 Tag 指向要清空的热键输入框。</summary>
    void ClearHotkeyBoxClick(object sender, RoutedEventArgs e)
    {
        var button = sender as FrameworkElement;
        var box = button is null ? null : button.Tag as TextBox;
        if (box is null) return;

        box.Text = "";
        box.CaretIndex = 0;
        MarkDirty();
        ValidateHotkeyBox(box);
    }

    /// <summary>「内容」页每张卡片的呼出热键清空。</summary>
    void ClearCardHotkeyClick(object sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        CardEditor editor = element is null ? null : element.DataContext as CardEditor;
        if (editor is null) return;

        editor.Hotkey = "";
    }

    static bool IsModifierKey(Key key) => key == Key.LeftCtrl || key == Key.RightCtrl
            || key == Key.LeftAlt || key == Key.RightAlt
            || key == Key.LeftShift || key == Key.RightShift
            || key == Key.LWin || key == Key.RWin;
}
