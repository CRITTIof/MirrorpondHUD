using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>对话框的按钮组合。</summary>
public enum HudDialogButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel
}

/// <summary>对话框左侧的方形状态标记；None 时不占位。</summary>
public enum HudDialogIcon
{
    None,
    Info,
    Warning,
    Error,
    Question
}

/// <summary>
/// 对话框结果，与 <c>MessageBoxResult</c> 一一对应：
/// Ok / Yes / No / Cancel；<see cref="None"/> 只表示“还没有结果”。
/// </summary>
public enum HudDialogResult
{
    None,
    Ok,
    Yes,
    No,
    Cancel
}

/// <summary>
/// 宿主与插件共用的自绘对话框：22px 圆角「浮起面板」，不用系统 MessageBox。
///
/// 为什么不用 <see cref="MessageBox"/>：系统弹窗不受主题控制（白底、直角、系统图标），
/// 在深色卡片与 Endfield 主题下像一块外来面板；插件里也无从取得与本项目一致的层级与色彩。
///
/// 视觉规格（见 docs/UI/endfield.md §3.2/§3.3/§3.5 + 设置窗 SettingsTheme）：
/// - 形状与设置主界面同一档圆角：外层 22px 圆角 + 1px 线，内容按圆角裁切；
/// - 暗色 #141414 底 / #F5F5F0 字 / #FFFA00 信号黄；明亮 #F2F2EC 底 / #FFFFFF 面板 /
///   #151515 字 / #B7A400 暗金强调（亮黄在纸白上不可见，不能当文字/竖条色）；
/// - 层级只靠标题字号 / 字重 + 3px 强调竖条；信号色只作填充 + 墨字，不做黄字；
/// - 按钮 6px 圆角（与设置窗控件同档）：主行动黄底墨字、次级透明 + 1px 线、危险红描边红字；
/// - 状态色（危险 / 提醒 / 信息）只用在设置窗内的方形小标记上，且配字形，不单靠颜色。
///
/// 用法：
/// <code>
/// HudDialogResult answer = HudDialog.Show(this, "删除卡片", "删除后保存并应用才生效。",
///                                        HudDialogButtons.YesNo, HudDialogIcon.Warning,
///                                        _working.Style);
/// if (answer == HudDialogResult.Yes) { ... }
/// </code>
/// </summary>
public static class HudDialog
{
    /// <summary>对话框宽度：规范要求 460–520，取中值保证中文正文每行 34–38 字。</summary>
    public const double DefaultWidth = 480;

    /// <summary>
    /// 显示模态对话框并返回用户选择。owner 为 null（或不可见）时置顶居中于屏幕；
    /// 否则成为 owner 的模态子窗口，居中于 owner。
    /// </summary>
    /// <param name="okText">主行动文案；null 按按钮组合取默认（确定 / 是）。</param>
    /// <param name="cancelText">取消 / 否的文字；null 取默认（取消 / 否）。YesNoCancel 的“否”固定为“否”。</param>
    /// <param name="danger">
    /// true 时主行动改成红描边 + 红字（危险确认），与次级按钮共用同一套形态。
    /// 不传 = 普通主行动（信号黄实心 + 墨字，6px 圆角）。
    /// </param>
    /// <param name="dark">
    /// 明暗：false（默认）= 外部明亮，true = 暗色。
    /// 为什么默认明亮：设置窗之外的弹窗浮在任意桌面 / 卡片上，纸白面板与用户对系统确认框的心智一致；
    /// 设置窗内的弹窗由调用方传 <c>dark: _settingsDark</c>，跟着设置窗一起切，不出现“暗窗里跳出白框”。
    /// </param>
    /// <param name="showIcon">
    /// 是否画左侧方形状态标记。默认 false：外部弹窗只要一句结论，图标位会把正文挤右、也与卡片无关；
    /// 设置窗内传 true，保留「方形色标 + 字形」这套状态语义。
    /// </param>
    public static HudDialogResult Show(Window owner, string title, string message,
        HudDialogButtons buttons = HudDialogButtons.Ok,
        HudDialogIcon icon = HudDialogIcon.None,
        StyleSettings style = null,
        string okText = null,
        string cancelText = null,
        bool danger = false,
        bool dark = false,
        bool showIcon = false)
    {
        HudDialogWindow window = null;
        // 收起（Hide）或已关闭的设置窗不能当 Owner：WPF 会抛 InvalidOperationException，
        // 而调用方此时只想看到一个提示框，不该为宿主窗口状态买单。不可见时按“无 owner”处理。
        bool owned = owner is not null && owner.IsVisible;

        try
        {
            window = new HudDialogWindow(title, message, buttons, icon, style, okText, cancelText, danger,
                                         dark, showIcon);
            if (owned)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.Topmost = true;
            }

            window.ShowDialog();
            return window.Result;
        }
        catch (Exception ex)
        {
            // 失败必须可见：自绘失败不能让提示消失（那等于把错误藏起来），
            // 退回系统窗口继续把话说给用户听，同时把技术原因写进日志。
            Log.Error($"自绘对话框显示失败（标题：{title ?? ""}；按钮：{buttons}），回退系统窗口", ex);
            if (window is not null) SafeRelease.Run(window.Close, "自绘对话框窗口");
            return ShowFallback(owner, title, message, buttons);
        }
    }

    /// <summary>错误提示（Error 图标 + 确定）。</summary>
    public static HudDialogResult ShowError(Window owner, string title, string message, StyleSettings style = null) =>
        Show(owner, title, message, HudDialogButtons.Ok, HudDialogIcon.Error, style, "确定");

    /// <summary>警告提示（Warning 图标 + 确定）。</summary>
    public static HudDialogResult ShowWarning(Window owner, string title, string message, StyleSettings style = null) =>
        Show(owner, title, message, HudDialogButtons.Ok, HudDialogIcon.Warning, style, "确定");

    /// <summary>
    /// 是 / 否确认。默认不带危险色：确认不一定具有破坏性，
    /// 删除一类操作由调用方显式传 <c>danger: true</c>。
    /// </summary>
    public static bool ShowConfirm(Window owner, string title, string message, StyleSettings style = null,
        string yesText = null, string noText = null, bool danger = false) =>
        Show(owner, title, message, HudDialogButtons.YesNo, HudDialogIcon.Question, style, yesText, noText, danger)
            == HudDialogResult.Yes;

    /// <summary>
    /// Esc / 右上关闭 / Alt+F4 的返回值：有 Cancel 就取消，YesNo 视为“否”，只有确定键时等于确认。
    /// 调用所有收尾路径共用它，避免系统关闭留下 None 让调用方自己猜。
    /// </summary>
    internal static HudDialogResult DismissResult(HudDialogButtons buttons) => buttons switch
    {
        HudDialogButtons.OkCancel => HudDialogResult.Cancel,
        HudDialogButtons.YesNo => HudDialogResult.No,
        HudDialogButtons.YesNoCancel => HudDialogResult.Cancel,
        _ => HudDialogResult.Ok
    };

    /// <summary>
    /// 自绘窗口彻底建不起来时的最小回退：普通系统窗口 + 原生按钮。
    /// 这里刻意不用任何自定义样式 —— 它能显示出来，就说明用户至少看到了失败。
    /// </summary>
    static HudDialogResult ShowFallback(Window owner, string title, string message, HudDialogButtons buttons)
    {
        Window window = null;
        var result = HudDialogResult.None;
        try
        {
            List<HudDialogButtonSpec> specs = BuildSpecs(buttons, null, null);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var text = new TextBlock
            {
                Text = message ?? "",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            };

            foreach (HudDialogButtonSpec spec in specs)
            {
                var button = new Button
                {
                    Content = spec.Text,
                    MinWidth = 76,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(12, 5, 12, 5),
                    IsDefault = spec.Primary,
                    IsCancel = spec.Cancel
                };
                HudDialogResult captured = spec.Result;
                button.Click += (s, e) =>
                {
                    result = captured;
                    window?.Close();
                };
                row.Children.Add(button);
            }

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(text);
            panel.Children.Add(row);

            window = new Window
            {
                Title = string.IsNullOrWhiteSpace(title) ? "镜潭一览" : title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                Content = panel
            };

            if (owner is not null && owner.IsVisible)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Topmost = false;
            }

            window.ShowDialog();
            return result == HudDialogResult.None ? DismissResult(buttons) : result;
        }
        catch (Exception ex)
        {
            // 连系统窗口都建不起来（例如 UI 线程已经在撕裂状态）：只能力求“取消”/“否”，
            // 但必须把原因落日志，不能静默返回。
            Log.Error("对话框回退窗口也失败，按取消语义返回", ex);
            return DismissResult(buttons);
        }
        finally
        {
            if (window is not null) SafeRelease.Run(window.Close, "对话框回退窗口");
        }
    }

    /// <summary>
    /// 按钮顺序固定为「取消 / 否 → 否 → 主行动」：主行动始终在最右，
    /// 与设置窗、卡片按钮的排布一致；文字与结果由同一张表派生，面板与回退窗口不会各写一套。
    /// </summary>
    internal static List<HudDialogButtonSpec> BuildSpecs(HudDialogButtons buttons, string okText, string cancelText)
    {
        string ok = string.IsNullOrWhiteSpace(okText) ? null : okText.Trim();
        string cancel = string.IsNullOrWhiteSpace(cancelText) ? null : cancelText.Trim();

        var specs = new List<HudDialogButtonSpec>();
        switch (buttons)
        {
            case HudDialogButtons.OkCancel:
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.Cancel, Text = cancel ?? "取消", Cancel = true });
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.Ok, Text = ok ?? "确定", Primary = true });
                break;

            case HudDialogButtons.YesNo:
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.No, Text = cancel ?? "否", Cancel = true });
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.Yes, Text = ok ?? "是", Primary = true });
                break;

            case HudDialogButtons.YesNoCancel:
                // 三个按钮时“取消”与“否”语义不同：取消 = 继续编辑，否 = 放弃修改。
                // 因此 cancelText 只改取消，“否”保持固定文案，避免两个按钮同名。
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.Cancel, Text = cancel ?? "取消", Cancel = true });
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.No, Text = "否" });
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.Yes, Text = ok ?? "是", Primary = true });
                break;

            default:
                // 只有确定键：Esc / Alt+F4 也等于确认，否则调用方会收到一个不存在的“取消”。
                specs.Add(new HudDialogButtonSpec { Result = HudDialogResult.Ok, Text = ok ?? "确定", Primary = true, Cancel = true });
                break;
        }
        return specs;
    }

    /// <summary>按钮表的一条：结果、文案、是否主行动、是否承接 Esc。</summary>
    internal sealed class HudDialogButtonSpec
    {
        internal HudDialogResult Result;
        internal string Text;
        internal bool Primary;
        internal bool Cancel;
    }
}

/// <summary>
/// 对话框窗口本身只负责模态、定位、键盘与结果收尾；
/// 视觉全部在 <see cref="HudDialogPanel"/> 里，截图模式可以脱离窗口单独渲染它。
/// </summary>
internal sealed class HudDialogWindow : Window
{
    readonly HudDialogButtons _buttons;
    readonly HudDialogPanel _panel;
    bool _finished;

    internal HudDialogResult Result { get; private set; } = HudDialogResult.None;

    internal HudDialogWindow(string title, string message, HudDialogButtons buttons, HudDialogIcon icon,
                             StyleSettings style, string okText, string cancelText, bool danger,
                             bool dark, bool showIcon)
    {
        _buttons = buttons;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        // Title 不显示（WindowStyle=None），但任务栏 / 辅助技术仍能读到；空标题给个宿主名。
        Title = string.IsNullOrWhiteSpace(title) ? "镜潭一览" : title;

        _panel = new HudDialogPanel(title, message, buttons, icon, style, okText, cancelText, danger,
                                    dark, showIcon);
        _panel.ResultSelected = Finish;
        _panel.CloseRequested = () => Finish(HudDialog.DismissResult(buttons));
        _panel.DragRequested = DragByTitle;
        Content = _panel;

        Loaded += (s, e) => _panel.FocusPrimary();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    /// <summary>
    /// Esc 统一在窗口层处理：不等某个按钮自己响应，这样 YesNo（没有 Cancel 键）
    /// 也能稳定映射成“否”，与 MessageBox 的语义一致。
    /// </summary>
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Finish(HudDialog.DismissResult(_buttons));
    }

    /// <summary>标题区拖动：窗口没有系统边框，只剩这一条可拖动的区域。</summary>
    void DragByTitle()
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException ex)
        {
            // DragMove 要求按下鼠标左键；快速点击 / 合成输入会落在这里，不是故障但值得留痕。
            Log.Warn($"对话框拖动未生效（鼠标状态已变化）: {ex.Message}");
        }
    }

    void OnClosing(object sender, CancelEventArgs e)
    {
        if (_finished) return;
        // Alt+F4 / 程序化 Close：没有走按钮时必须给出与 Esc 一致的结果，不能留下 None。
        _finished = true;
        Result = HudDialog.DismissResult(_buttons);
    }

    void Finish(HudDialogResult result)
    {
        if (_finished) return;
        _finished = true;
        Result = result;
        Close();
    }
}

/// <summary>
/// 对话框的视觉面板：离屏渲染（--screenshot-dialog）与真实窗口共用同一份布局，
/// 避免“截图里的样子”和“用户看到的样子”各写一套后慢慢分叉。
/// </summary>
internal sealed class HudDialogPanel : Grid
{
    internal Action DragRequested;
    internal Action<HudDialogResult> ResultSelected;
    internal Action CloseRequested;

    internal Button PrimaryButton { get; private set; }

    internal HudDialogPanel(string title, string message, HudDialogButtons buttons, HudDialogIcon icon,
                            StyleSettings style, string okText, string cancelText, bool danger,
                            bool dark = false, bool showIcon = false)
    {
        HudDialogTheme theme = HudDialogTheme.Resolve(style, dark);
        List<HudDialog.HudDialogButtonSpec> specs = HudDialog.BuildSpecs(buttons, okText, cancelText);

        Width = HudDialog.DefaultWidth;
        Background = Brushes.Transparent;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        // 对话框内 Tab / Shift+Tab 只在自己的按钮间循环，不能把焦点放回背后的卡片或设置窗。
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid header = BuildHeader(theme, title, buttons);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        // 外部弹窗不画图标：showIcon=false 时按 None 处理，图标列宽度归零，正文与标题都不偏移。
        Grid body = BuildBody(theme, message, showIcon ? icon : HudDialogIcon.None);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        Grid footer = BuildFooter(theme, buttons, specs, danger);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        // 与设置主界面同一档圆角：22px 表面 + 1px 线，内容按圆角裁切。
        // 上下各留 20px，3px 强调竖条落在弧线之内，不会被圆角吃掉。
        Children.Add(HudDialogSurface.Wrap(layout, theme.Surface, theme.Line, new Thickness(24, 20, 24, 20)));
    }

    /// <summary>标题行：3px 信号黄竖条 + 14 SemiBold 标题 + 可选关闭键；整行是拖动区。</summary>
    Grid BuildHeader(HudDialogTheme theme, string title, HudDialogButtons buttons)
    {
        var header = new Grid { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border
        {
            Width = 3,
            Height = 16,
            Background = CardPalette.Brush(theme.AccentOnSurface),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        header.Children.Add(bar);

        var titleBlock = new TextBlock
        {
            Text = title ?? "",
            FontFamily = theme.Font,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = CardPalette.Brush(theme.Text),
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(titleBlock, 1);
        header.Children.Add(titleBlock);

        // 只有确定键的提示不需要关闭键（Esc 就是确定）；有多选项时才给右上角小 ×。
        if (buttons != HudDialogButtons.Ok)
        {
            var close = new HudDialogButton("×", HudDialogButtonTone.Ghost, theme)
            {
                Width = 24,
                Height = 24,
                MinWidth = 24,
                MinHeight = 24,
                Padding = new Thickness(0),
                FontSize = 14,
                IsTabStop = false,
                ToolTip = "关闭（Esc）",
                Margin = new Thickness(8, 0, 0, 0)
            };
            close.Click += (s, e) => CloseRequested?.Invoke();
            Grid.SetColumn(close, 2);
            header.Children.Add(close);
        }

        header.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragRequested?.Invoke();
        };
        return header;
    }

    /// <summary>正文行：方形状态标记（颜色 + 字形）+ 12.5 正文，行高 1.5。</summary>
    Grid BuildBody(HudDialogTheme theme, string message, HudDialogIcon icon)
    {
        var body = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (icon != HudDialogIcon.None)
        {
            (Color role, string glyph) = HudDialogTheme.IconOf(icon);
            var marker = new Border
            {
                Width = 24,
                Height = 24,
                BorderThickness = new Thickness(1),
                BorderBrush = CardPalette.Brush(role),
                Background = CardPalette.Brush(role, 0.14),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 12, 0),
                // 方形标记：状态不能只靠颜色，字形与方框一起表达“这是什么状态”。
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = theme.Font,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = CardPalette.Brush(role),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(marker, 0);
            body.Children.Add(marker);
        }

        var text = new TextBlock
        {
            Text = message ?? "",
            FontFamily = theme.Font,
            FontSize = 12.5,
            Foreground = CardPalette.Brush(theme.Text),
            LineHeight = 18.75,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(text, 1);
        body.Children.Add(text);
        return body;
    }

    /// <summary>底部：左侧快捷键提示（小号次级字），右侧按钮组；主行动永远在最右。</summary>
    Grid BuildFooter(HudDialogTheme theme, HudDialogButtons buttons,
                     List<HudDialog.HudDialogButtonSpec> specs, bool danger)
    {
        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = HintOf(buttons),
            FontFamily = theme.Font,
            FontSize = 11,
            Foreground = CardPalette.Brush(theme.Secondary),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0)
        };
        footer.Children.Add(hint);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(row, 1);

        for (int i = 0; i < specs.Count; i++)
        {
            HudDialog.HudDialogButtonSpec spec = specs[i];
            HudDialogButtonTone tone = spec.Primary
                ? (danger ? HudDialogButtonTone.Danger : HudDialogButtonTone.Primary)
                : HudDialogButtonTone.Secondary;

            var button = new HudDialogButton(spec.Text, tone, theme)
            {
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = spec.Primary,
                IsCancel = spec.Cancel
            };

            HudDialogResult captured = spec.Result;
            button.Click += (s, e) => ResultSelected?.Invoke(captured);
            if (spec.Primary) PrimaryButton = button;
            row.Children.Add(button);
        }

        footer.Children.Add(row);
        return footer;
    }

    internal void FocusPrimary()
    {
        if (PrimaryButton is not null) PrimaryButton.Focus();
    }

    static string HintOf(HudDialogButtons buttons) => buttons switch
    {
        HudDialogButtons.Ok => "Enter / Esc 关闭",
        HudDialogButtons.YesNo => "Enter 确认 · Esc 否",
        _ => "Enter 确认 · Esc 取消"
    };
}

/// <summary>对话框按钮的形态：主行动、次级行动、危险确认、无框关闭键。</summary>
internal enum HudDialogButtonTone
{
    Primary,
    Secondary,
    Danger,
    Ghost
}

/// <summary>
/// 自绘按钮：模板只留内容呈现，底形 / 描边 / 圆角 / 悬停 / 焦点环全部在 OnRender 里画。
/// 这样按钮不会被 WPF 默认模板再套一层灰色边框，也不用为每种语气拼一套 XAML。
/// </summary>
internal sealed class HudDialogButton : Button
{
    // 6px 圆角：与设置窗控件（Theme.xaml 按钮 4–6px、色块 4px）同档。
    // 32px 高的按钮用 6px 才和 22px 圆角的面板同调；继续用切角会像上一个版本的残留。
    const double Radius = 6;
    const double MinHeightValue = 32;

    readonly HudDialogTheme _theme;
    readonly HudDialogButtonTone _tone;

    internal HudDialogButton(string text, HudDialogButtonTone tone, HudDialogTheme theme)
    {
        _theme = theme;
        _tone = tone;

        Color textColor = tone switch
        {
            HudDialogButtonTone.Primary => theme.AccentInk,
            HudDialogButtonTone.Danger => theme.DangerColor,
            HudDialogButtonTone.Ghost => theme.Secondary,
            _ => theme.Text
        };

        Content = new TextBlock
        {
            Text = text ?? "",
            FontFamily = theme.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.Normal,
            Foreground = CardPalette.Brush(textColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        FontFamily = theme.Font;
        FontSize = 12.5;
        MinHeight = MinHeightValue;
        MinWidth = 68;
        Padding = new Thickness(14, 5, 14, 5);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        Template = SharedTemplate;
    }

    static ControlTemplate SharedTemplate =>
        CardTemplates.Get("HudDialog.Button", CreateTemplate, "对话框按钮模板");

    static ControlTemplate CreateTemplate() =>
        (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate TargetType=\"Button\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<ContentPresenter Margin=\"{TemplateBinding Padding}\" " +
            "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" RecognizesAccessKey=\"True\"/>" +
            "</ControlTemplate>");

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w > 0 && h > 0)
        {
            Color fill;
            Color border;

            switch (_tone)
            {
                case HudDialogButtonTone.Primary:
                    // 信号黄只作填充，字是墨色：对比 16.6:1（endfield.md §3.2）。
                    fill = IsPressed
                        ? Mix(_theme.AccentColor, Colors.Black, 0.12)
                        : IsMouseOver ? Mix(_theme.AccentColor, Colors.White, 0.12) : _theme.AccentColor;
                    border = Colors.Transparent;
                    break;

                case HudDialogButtonTone.Danger:
                    // 危险确认统一“红描边 + 红字”，不做红填充：与主行动的实心黄保持两套清晰语气。
                    fill = IsMouseOver ? CardPalette.WithAlpha(_theme.DangerColor, 0.12) : Colors.Transparent;
                    border = _theme.DangerColor;
                    break;

                case HudDialogButtonTone.Ghost:
                    fill = IsMouseOver ? _theme.Panel : Colors.Transparent;
                    border = Colors.Transparent;
                    break;

                default:
                    // 次级行动：透明底 + 1px 线 + 主文字，悬停只抬一档中性底。
                    fill = IsMouseOver ? _theme.Panel : Colors.Transparent;
                    border = _theme.Line;
                    break;
            }

            Geometry shape = BuildShape(new Rect(0, 0, w, h), Radius);
            Pen pen = null;
            if (border.A > 0)
            {
                pen = new Pen(CardPalette.Brush(border), 1);
                pen.Freeze();
            }
            dc.DrawGeometry(CardPalette.Brush(fill), pen, shape);

            if (IsKeyboardFocused)
            {
                // 2.45.3 修复：旧环画在按钮外侧 2px，颜色按“按钮填充”取墨色——
                // 暗色下外侧是深色表面，墨色环与背景同色；外侧绘制还可能被布局裁切。
                // 改为在按钮内侧留 1.5px 画环：任何宿主都不会裁掉，且按“环所在的底色”取对比色。
                double inset = 1.5;
                double rw = Math.Max(1, w - inset * 2);
                double rh = Math.Max(1, h - inset * 2);
                Geometry ring = BuildShape(new Rect(inset, inset, rw, rh), Math.Max(2, Radius - 1));
                Color ringColor = _tone == HudDialogButtonTone.Primary
                    ? _theme.AccentInk          // 环压在黄色填充上
                    : _theme.AccentOnSurface;   // 环压在面板底色上
                var ringPen = new Pen(CardPalette.Brush(ringColor), 2);
                ringPen.Freeze();
                dc.DrawGeometry(null, ringPen, ring);
            }
        }

        base.OnRender(dc);
    }

    /// <summary>6px 圆角矩形；半径按较短边折半收口，避免小按钮（关闭键 24px）画成胶囊。</summary>
    static Geometry BuildShape(Rect rect, double radius)
    {
        double r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        var geometry = new RectangleGeometry(rect, r, r);
        geometry.Freeze();
        return geometry;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    static Color Mix(Color from, Color to, double amount)
    {
        double t = Math.Max(0, Math.Min(1, amount));
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }
}

/// <summary>
/// 对话框表面：22px 圆角 + 1px 线的「浮起面板」，与设置主界面同一档圆角。
///
/// 为什么必须自己裁切：<see cref="Border"/> 的 CornerRadius 只影响背景与描边，
/// 不会剪掉子元素 —— 悬停底、色块、输入框都会在弧线外露出直角，圆角就只“画”了一半。
/// </summary>
internal static class HudDialogSurface
{
    internal const double Radius = 22;

    /// <summary>把内容放进圆角表面；内容按缩进后的矩形裁切，圆角本身由 Border 画。</summary>
    internal static Border Wrap(FrameworkElement content, Color background, Color line, Thickness padding)
    {
        var surface = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(1),
            BorderBrush = CardPalette.Brush(line),
            Background = CardPalette.Brush(background),
            Padding = padding
        };

        // 内容区被 padding 缩进到弧线之内（缩进 ≥ 圆角半径时内层无需再倒角），
        // 内层半径按「外圆角 − 缩进」计算：padding 变小也不会突然露出直角。
        var clip = new RectangleGeometry();
        content.Clip = clip;
        content.SizeChanged += (s, e) =>
        {
            clip.Rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            double inset = Math.Min(padding.Left, padding.Top);
            clip.RadiusX = clip.RadiusY = Math.Max(0, Radius - inset);
        };

        surface.Child = content;
        return surface;
    }
}

/// <summary>
/// 对话框用到的全部颜色与字体：明暗**不再**从背景亮度推断，而由调用方显式给 <c>dark</c>。
///
/// 为什么底色不吃 <see cref="StyleSettings"/> 的背景：卡片底色可能半透明、带花纹或极亮，
/// 系统级确认面浮在桌面上时正文会读不清；对话框固定跟设置窗的两套 Endfield 令牌，
/// StyleSettings 只贡献字体（用户选的字体要跟着走）。
/// </summary>
internal sealed class HudDialogTheme
{
    internal Color Surface;
    internal Color Panel;
    internal Color Field;
    internal Color Text;
    internal Color Secondary;
    internal Color Line;
    internal Color AccentColor;
    // 浅色行动面上的强调色：亮黄在浅底几乎不可见，按 paper 版换成暗金 #B7A400。
    internal Color AccentOnSurface;
    internal Color AccentInk;
    internal Color DangerColor;
    internal FontFamily Font;

    internal static HudDialogTheme Resolve(StyleSettings style, bool dark)
    {
        string font = style is not null && !string.IsNullOrWhiteSpace(style.FontFamily)
            ? style.FontFamily
            : "Microsoft YaHei UI";

        // 两套令牌与 SettingsTheme 一一对应：暗色是 Endfield 墨版 #141414/#F5F5F0/#FFFA00，
        // 明亮是 paper 版 #F2F2EC 底 / #FFFFFF 面板 / #151515 字 / #626262 次级 / #E6E6E6 线。
        // 强调色在浅底上用暗金 #B7A400（亮黄当文字/竖条几乎不可见），主按钮仍保留黄底墨字。
        Color accent = dark ? Color.FromRgb(0xFF, 0xFA, 0x00) : Color.FromRgb(0xF2, 0xE9, 0x00);

        return new HudDialogTheme
        {
            Surface = dark ? Color.FromRgb(0x14, 0x14, 0x14) : Color.FromRgb(0xF2, 0xF2, 0xEC),
            // Panel 只用于悬停底 / 关闭键 / 取色器面板，不参与层级（层级靠字号字重 + 3px 竖条）。
            Panel = dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xFF, 0xFF, 0xFF),
            // Field 是输入框底：浅色用纯白，暗色比表面再沉一档，输入区才有“凹陷”的可读边界。
            Field = dark ? Color.FromRgb(0x0E, 0x11, 0x13) : Color.FromRgb(0xFF, 0xFF, 0xFF),
            Text = dark ? Color.FromRgb(0xF5, 0xF5, 0xF0) : Color.FromRgb(0x15, 0x15, 0x15),
            Secondary = dark ? Color.FromRgb(0x8A, 0x8A, 0x8A) : Color.FromRgb(0x62, 0x62, 0x62),
            Line = dark ? Color.FromRgb(0x38, 0x38, 0x38) : Color.FromRgb(0xE6, 0xE6, 0xE6),
            AccentColor = accent,
            AccentOnSurface = dark ? accent : Color.FromRgb(0xB7, 0xA4, 0x00),
            AccentInk = dark ? Color.FromRgb(0x14, 0x14, 0x14) : Color.FromRgb(0x11, 0x11, 0x11),
            // 危险色与设置窗同值：暗 #F62A2A / 亮 #B83A2E（浅底上的纯红会发飘）。
            DangerColor = dark ? Color.FromRgb(0xF6, 0x2A, 0x2A) : Color.FromRgb(0xB8, 0x3A, 0x2E),
            Font = CardPalette.ResolveFont(font)
        };
    }

    internal static (Color Role, string Glyph) IconOf(HudDialogIcon icon) => icon switch
    {
        HudDialogIcon.Info => (Color.FromRgb(0x2A, 0x84, 0xB4), "i"),
        HudDialogIcon.Warning => (Color.FromRgb(0xFC, 0x6C, 0x00), "!"),
        HudDialogIcon.Error => (Color.FromRgb(0xF6, 0x2A, 0x2A), "×"),
        HudDialogIcon.Question => (Color.FromRgb(0x2A, 0x84, 0xB4), "?"),
        _ => (Colors.Transparent, "")
    };
}
