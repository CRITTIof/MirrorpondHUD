using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 宿主与插件共用的自绘取色器：与 <see cref="HudDialog"/> 同一套 22px 圆角表面、1px 线、
/// 明暗两套令牌与黄底墨字主按钮，所以从设置窗点「取色」不会跳出一个系统配色面板。
///
/// 为什么不用 <c>System.Windows.Forms.ColorDialog</c>：它不受主题控制（白底、直角、系统字体），
/// 在暗色设置窗里像一块外来面板；而且 UiKit 的公开控件不应替调用方决定用哪个 UI 框架。
/// </summary>
public static class HudColorPicker
{
    /// <summary>当前值解析失败时的回退色：明确的蓝，用户一眼能看出“这不是我原来的颜色”。</summary>
    public const string FallbackColor = "#2F6FEB";

    /// <summary>
    /// 打开取色器并返回 <c>#RRGGBB</c>；取消（Esc / 关闭键 / 确定失败）返回 null。
    /// </summary>
    /// <param name="current">当前颜色值，接受 <c>#RRGGBB</c> 与 <c>#AARRGGBB</c>；非法 / 空值回退 <see cref="FallbackColor"/>。</param>
    /// <param name="dark">跟随宿主明暗：设置窗内传当前设置窗的明暗，窗外固定明亮。</param>
    /// <param name="style">只取字体，颜色固定走对话框令牌（见 <see cref="HudDialogTheme"/>）。</param>
    public static string Pick(Window owner, string current, bool dark, StyleSettings style = null)
    {
        ColorPickerWindow window = null;
        // 收起 / 已关闭的宿主窗口不能当 Owner（WPF 会抛 InvalidOperationException），
        // 调用方此时只想取个色，不该为宿主窗口状态买单。
        bool owned = owner is not null && owner.IsVisible;

        try
        {
            window = new ColorPickerWindow(current, dark, style);
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
            return window.Picked;
        }
        catch (Exception ex)
        {
            // 失败必须可见：取色器打不开要留日志，返回 null 等价“取消”，
            // 绝不把没确认过的颜色写回设置（那会让用户以为已经选好了）。
            Log.Error($"取色器显示失败（当前值：{current ?? ""}）", ex);
            if (window is not null) SafeRelease.Run(window.Close, "取色器窗口");
            return null;
        }
    }

    /// <summary>供截图模式离屏渲染：与真实窗口共用同一份面板布局。</summary>
    internal static ColorPickerPanel CreatePanel(string current, bool dark, StyleSettings style = null) =>
        new(current, dark, style);
}

/// <summary>
/// 取色器的窗口：只负责模态、定位、Enter / Esc 与结果收尾，视觉全部在 <see cref="ColorPickerPanel"/>。
/// </summary>
internal sealed class ColorPickerWindow : Window
{
    readonly ColorPickerPanel _panel;

    internal string Picked { get; private set; }

    internal ColorPickerWindow(string current, bool dark, StyleSettings style)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = HudDialog.DefaultWidth;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        // Title 不显示，但任务栏 / 辅助技术仍要能读到。
        Title = "取色";

        _panel = new ColorPickerPanel(current, dark, style);
        _panel.Confirmed = hex =>
        {
            Picked = hex;
            Close();
        };
        _panel.Cancelled = () =>
        {
            Picked = null;
            Close();
        };
        Content = _panel;

        Loaded += (s, e) => _panel.FocusInitial();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Esc / Enter 统一在窗口层处理：焦点在输入框里时也要能用，而且必须与按钮结果一致。
    /// 关窗（× / Alt+F4）不设 Picked，走取消语义，调用方拿到 null 不会误覆盖颜色。
    /// </summary>
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _panel.Cancel();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _panel.Confirm();
        }
    }
}

/// <summary>
/// 取色器面板：HSV 色板 + Hex + RGB + 当前 / 新色预览 + 确定 / 取消。
/// 离屏截图与真实窗口共用它，避免“图里的样子”和用户看到的样子各写一套。
/// </summary>
internal sealed class ColorPickerPanel : Grid
{
    internal Action<string> Confirmed;
    internal Action Cancelled;

    readonly HudDialogTheme _theme;
    readonly HsvPad _pad;
    readonly TextBox _hex;
    readonly TextBox _red;
    readonly TextBox _green;
    readonly TextBox _blue;
    readonly List<TextBox> _boxes = new();
    readonly Border _nextSwatch;
    readonly TextBlock _error;
    Color _current;
    bool _syncing;

    internal ColorPickerPanel(string current, bool dark, StyleSettings style)
    {
        _theme = HudDialogTheme.Resolve(style, dark);
        _current = ColorPickerMath.Parse(current);

        Width = HudDialog.DefaultWidth;
        Background = Brushes.Transparent;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        // 取色器里的 Tab 只在自身输入框与按钮间循环，不能把焦点放回背后的设置窗。
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid header = BuildHeader();
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        _pad = new HsvPad(_theme);
        _hex = CreateBox(7);
        _red = CreateBox(3);
        _green = CreateBox(3);
        _blue = CreateBox(3);
        _error = new TextBlock
        {
            FontFamily = _theme.Font,
            FontSize = 11,
            Foreground = CardPalette.Brush(_theme.DangerColor),
            Height = 16,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _nextSwatch = Swatch(_current);

        Grid body = BuildBody();
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        Grid footer = BuildFooter();
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        // 与对话框同一条圆角：22px 表面 + 1px 线，内容按圆角裁切。
        Children.Add(HudDialogSurface.Wrap(layout, _theme.Surface, _theme.Line, new Thickness(24, 20, 24, 20)));

        // 初始色相在灰色（饱和度为 0）时无处可推，用 0 起手；之后拖色板会保留上一帧色相。
        double hue = 0;
        ColorPickerMath.HsvFromColor(_current, ref hue, out double saturation, out double value);
        _pad.SetHsv(hue, saturation, value);
        SyncInputs(_current, updateHex: true);

        _pad.Changed += OnPadChanged;
        _hex.TextChanged += OnHexChanged;
        _red.TextChanged += OnChannelChanged;
        _green.TextChanged += OnChannelChanged;
        _blue.TextChanged += OnChannelChanged;
    }

    /// <summary>标题行：3px 强调竖条 + 14 SemiBold「取色」+ 右上关闭键；整行可拖动。</summary>
    Grid BuildHeader()
    {
        var header = new Grid { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border
        {
            Width = 3,
            Height = 16,
            Background = CardPalette.Brush(_theme.AccentOnSurface),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        header.Children.Add(bar);

        var title = new TextBlock
        {
            Text = "取色",
            FontFamily = _theme.Font,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = CardPalette.Brush(_theme.Text),
            LineHeight = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var close = new HudDialogButton("×", HudDialogButtonTone.Ghost, _theme)
        {
            Width = 24,
            Height = 24,
            MinWidth = 24,
            MinHeight = 24,
            Padding = new Thickness(0),
            FontSize = 14,
            IsTabStop = false,
            ToolTip = "关闭（Esc）"
        };
        close.Click += (s, e) => Cancel();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);

        header.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try
            {
                Window.GetWindow(this)?.DragMove();
            }
            catch (InvalidOperationException ex)
            {
                // DragMove 要求按下左键；快速点击 / 合成输入会落在这里，不是故障但值得留痕。
                Log.Warn($"取色器拖动未生效（鼠标状态已变化）: {ex.Message}");
            }
        };
        return header;
    }

    /// <summary>正文：左侧 HSV 色板，右侧预览 / Hex / RGB，底部错误提示。</summary>
    Grid BuildBody()
    {
        var body = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _pad.VerticalAlignment = VerticalAlignment.Top;
        body.Children.Add(_pad);

        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(right, 1);

        var preview = new Grid();
        preview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        preview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        preview.Children.Add(PreviewCell("当前", Swatch(_current), first: true));

        FrameworkElement newCell = PreviewCell("新色", _nextSwatch, first: false);
        Grid.SetColumn(newCell, 1);
        preview.Children.Add(newCell);
        right.Children.Add(preview);

        right.Children.Add(Label("Hex", 10));
        AddBox(right, _hex);
        _hex.ToolTip = "颜色值，#RRGGBB";

        right.Children.Add(Label("RGB", 10));
        var channels = new Grid();
        for (int i = 0; i < 3; i++)
            channels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBox[] boxes = { _red, _green, _blue };
        string[] names = { "R", "G", "B" };
        for (int i = 0; i < boxes.Length; i++)
        {
            TextBox box = boxes[i];
            box.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
            box.ToolTip = names[i] + " 通道，0–255";
            Grid.SetColumn(box, i);
            channels.Children.Add(box);
        }
        right.Children.Add(channels);

        right.Children.Add(_error);
        body.Children.Add(right);
        return body;
    }

    /// <summary>一个预览格：小号说明 + 色块；「当前」是初始色，「新色」由色板 / 输入实时刷新。</summary>
    FrameworkElement PreviewCell(string caption, Border swatch, bool first)
    {
        var cell = new StackPanel { Margin = new Thickness(first ? 0 : 8, 0, 0, 0) };
        cell.Children.Add(Label(caption, 0));
        swatch.Margin = new Thickness(0, 3, 0, 0);
        cell.Children.Add(swatch);
        return cell;
    }

    /// <summary>4px 圆角色块（与设置窗色板同档），1px 线保证浅色在浅底上也有边界。</summary>
    Border Swatch(Color color) => new()
    {
        Height = 26,
        CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1),
        BorderBrush = CardPalette.Brush(_theme.Line),
        Background = CardPalette.Brush(color)
    };

    TextBlock Label(string text, double top) => new()
    {
        Text = text,
        FontFamily = _theme.Font,
        FontSize = 10.5,
        Foreground = CardPalette.Brush(_theme.Secondary),
        Margin = new Thickness(0, top, 0, 0)
    };

    void AddBox(StackPanel host, TextBox box)
    {
        box.Margin = new Thickness(0, 3, 0, 0);
        host.Children.Add(box);
    }

    /// <summary>底部：左侧快捷键提示，右侧取消 / 确定；主行动永远在最右。</summary>
    Grid BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = "Enter 确定 · Esc 取消",
            FontFamily = _theme.Font,
            FontSize = 11,
            Foreground = CardPalette.Brush(_theme.Secondary),
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(hint);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(row, 1);

        var cancel = new HudDialogButton("取消", HudDialogButtonTone.Secondary, _theme) { IsCancel = true };
        cancel.Click += (s, e) => Cancel();
        row.Children.Add(cancel);

        var ok = new HudDialogButton("确定", HudDialogButtonTone.Primary, _theme)
        {
            IsDefault = true,
            Margin = new Thickness(8, 0, 0, 0)
        };
        ok.Click += (s, e) => Confirm();
        row.Children.Add(ok);

        footer.Children.Add(row);
        return footer;
    }

    /// <summary>
    /// 输入框：6px 圆角 + 1px 线（与设置窗控件同档），模板自己解析以便圆角与底/线都跟令牌走。
    /// TextBox 没有 CornerRadius，只能用模板里的 Border 画。
    /// </summary>
    TextBox CreateBox(int maxLength)
    {
        var box = new TextBox
        {
            FontFamily = _theme.Font,
            FontSize = 12,
            Height = 30,
            MaxLength = maxLength,
            CharacterCasing = CharacterCasing.Upper,
            Foreground = CardPalette.Brush(_theme.Text),
            Background = CardPalette.Brush(_theme.Field),
            BorderBrush = CardPalette.Brush(_theme.Line),
            BorderThickness = new Thickness(1),
            CaretBrush = CardPalette.Brush(_theme.Text),
            SelectionBrush = CardPalette.Brush(_theme.Text, 0.35),
            // 内边距只能交给模板里的 ScrollViewer.Margin：放在 Border.Padding 上时
            // 文本行高会按缩进后的可用高度再算一次，实测把字形底边裁掉一半。
            // 横向只留 6px：RGB 三格各约 55px，再宽的内边距会让 “111” 这类三位数被截。
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null
        };

        // 模板解析失败时 CardTemplates 已记 ERROR 并返回 null：此时绝不能把 Template 赋成 null
        // （本地值 null 会盖掉隐式样式，输入框整块变成空白），保留样式里的默认模板。
        ControlTemplate template = InputTemplate;
        if (template is not null) box.Template = template;

        _boxes.Add(box);
        return box;
    }

    static ControlTemplate InputTemplate =>
        CardTemplates.Get("HudColorPicker.Input", CreateInputTemplate, "取色器输入框模板");

    static ControlTemplate CreateInputTemplate() =>
        (ControlTemplate)XamlReader.Parse(
            // x 命名空间必须显式声明：x:Name="PART_ContentHost" 是 TextBox 的硬约定，
            // 缺 xmlns:x 会抛 XamlParseException，模板变成 null 后输入框会整块不渲染。
            "<ControlTemplate TargetType=\"TextBox\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Border CornerRadius=\"6\" Background=\"{TemplateBinding Background}\" " +
            "BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\">" +
            "<ScrollViewer x:Name=\"PART_ContentHost\" Margin=\"{TemplateBinding Padding}\" VerticalAlignment=\"Center\"/>" +
            "</Border>" +
            "</ControlTemplate>");

    // ==================== 同步与校验 ====================

    void OnPadChanged()
    {
        if (_syncing) return;
        // 色板给出的一定是合法颜色：清掉上一次的红框，输入框跟着色板走。
        SyncInputs(_pad.Color, updateHex: true);
        ClearError();
    }

    void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (ColorPickerMath.TryParseHex(_hex.Text, out Color color))
        {
            ApplyColor(color, updateHex: false);
            return;
        }

        // 非法输入不静默：红框 + 提示，颜色保持上一个合法值，确定键也会被拦下。
        ShowError("请输入 #RRGGBB", _hex);
    }

    void OnChannelChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        byte[] channels = new byte[3];
        TextBox[] boxes = { _red, _green, _blue };
        for (int i = 0; i < boxes.Length; i++)
        {
            if (!byte.TryParse(boxes[i].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[i]))
            {
                ShowError("RGB 取值 0–255", _red, _green, _blue);
                return;
            }
        }

        ApplyColor(Color.FromRgb(channels[0], channels[1], channels[2]), updateHex: true);
    }

    /// <summary>把颜色写回色板与三个输入框；<paramref name="updateHex"/> 控制是否也刷新 Hex 框。</summary>
    void ApplyColor(Color color, bool updateHex)
    {
        SyncInputs(color, updateHex);
        ClearError();
    }

    void SyncInputs(Color color, bool updateHex)
    {
        _syncing = true;
        try
        {
            double hue = _pad.Hue;
            // 灰色没有色相：把当前色相喂进去，滑到饱和度 0 时色条不会跳回红色。
            ColorPickerMath.HsvFromColor(color, ref hue, out double saturation, out double value);
            _pad.SetHsv(hue, saturation, value);

            if (updateHex) _hex.Text = ColorPickerMath.ToHex(color);
            _red.Text = color.R.ToString(CultureInfo.InvariantCulture);
            _green.Text = color.G.ToString(CultureInfo.InvariantCulture);
            _blue.Text = color.B.ToString(CultureInfo.InvariantCulture);
            _nextSwatch.Background = CardPalette.Brush(color);
        }
        finally
        {
            _syncing = false;
        }
    }

    void ShowError(string message, params TextBox[] culprits)
    {
        _error.Text = message;
        foreach (TextBox box in _boxes)
        {
            box.BorderBrush = CardPalette.Brush(_theme.Line);
            box.BorderThickness = new Thickness(1);
        }

        foreach (TextBox box in culprits)
        {
            box.BorderBrush = CardPalette.Brush(_theme.DangerColor);
            box.BorderThickness = new Thickness(2);
        }
    }

    void ClearError()
    {
        if (_error.Text.Length == 0 && _boxes.TrueForAll(b => b.BorderThickness.Left < 2)) return;

        _error.Text = "";
        foreach (TextBox box in _boxes)
        {
            box.BorderBrush = CardPalette.Brush(_theme.Line);
            box.BorderThickness = new Thickness(1);
        }
    }

    internal void FocusInitial()
    {
        _hex.Focus();
        _hex.SelectAll();
    }

    /// <summary>
    /// 确定：两个输入源都要复核。Hex 合法但 RGB 被改成越界值时，用户以为改的是 RGB，
    /// 所以不能只信 Hex 就把窗口关掉 —— 那种“看起来成功了”的静默覆盖最伤。
    /// </summary>
    internal void Confirm()
    {
        if (!ColorPickerMath.TryParseHex(_hex.Text, out Color fromHex))
        {
            ShowError("请输入 #RRGGBB", _hex);
            _hex.Focus();
            _hex.SelectAll();
            return;
        }

        if (!byte.TryParse(_red.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            !byte.TryParse(_green.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            !byte.TryParse(_blue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            ShowError("RGB 取值 0–255", _red, _green, _blue);
            _red.Focus();
            _red.SelectAll();
            return;
        }

        // 两个来源都合法时以 Hex 为准（它同时是界面上最精确的那一栏）；
        // 两者不一致只可能发生在某次非法编辑期间，此时上面已经拦下。
        Confirmed?.Invoke(ColorPickerMath.ToHex(fromHex));
    }

    internal void Cancel() => Cancelled?.Invoke();
}

/// <summary>
/// HSV 取色板：左侧饱和度 / 明度方块，右侧色相条；两者共用一个鼠标捕获，
/// 避免拖出色块边界时丢事件。
/// </summary>
internal sealed class HsvPad : FrameworkElement
{
    internal const double SvWidth = 208;
    // 220 会让右栏（预览 + Hex + RGB + 提示，约 175）下方空一大块；200 与右栏高度基本齐平。
    internal const double SvHeight = 200;
    const double Gap = 10;
    const double HueWidth = 18;

    readonly HudDialogTheme _theme;
    double _hue;
    double _saturation;
    double _value;
    bool _draggingSv;
    bool _draggingHue;

    internal event Action Changed;

    internal HsvPad(HudDialogTheme theme)
    {
        _theme = theme;
        Width = SvWidth + Gap + HueWidth;
        Height = SvHeight;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
        // 键盘焦点留给输入框与按钮：色板是纯鼠标操作面，抢焦点会让 Enter 落到一个没有焦点的控件上。
        Focusable = false;
    }

    internal double Hue => _hue;

    internal Color Color => ColorPickerMath.FromHsv(_hue, _saturation, _value);

    internal void SetHsv(double hue, double saturation, double value)
    {
        _hue = ColorPickerMath.NormalizeHue(hue);
        _saturation = ColorPickerMath.Clamp01(saturation);
        _value = ColorPickerMath.Clamp01(value);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var sv = new Rect(0, 0, SvWidth, SvHeight);
        var hueRect = new Rect(SvWidth + Gap, 0, HueWidth, SvHeight);

        DrawSaturationValue(dc, sv);
        DrawHue(dc, hueRect);

        Pen edge = EdgePen();
        dc.DrawRectangle(null, edge, sv);
        dc.DrawRectangle(null, edge, hueRect);

        DrawSvMarker(dc, new Point(_saturation * SvWidth, (1 - _value) * SvHeight));
        DrawHueMarker(dc, hueRect, _hue / 360.0 * SvHeight);
    }

    /// <summary>
    /// SV 方块：底色取当前色相的纯色，横向叠白→透明（饱和度），纵向叠透明→黑（明度）。
    /// 两层渐变只依赖色相，所以拖动色相条时才需要重建。
    /// </summary>
    void DrawSaturationValue(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(CardPalette.Brush(ColorPickerMath.FromHsv(_hue, 1, 1)), null, rect);

        var saturation = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0)
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        saturation.Freeze();
        dc.DrawRectangle(saturation, null, rect);

        var value = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90)
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        value.Freeze();
        dc.DrawRectangle(value, null, rect);
    }

    void DrawHue(DrawingContext dc, Rect rect)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        // 七个停靠点闭合色相环：红 → 黄 → 绿 → 青 → 蓝 → 品红 → 红。
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 1.0 / 6.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xFF, 0x00), 2.0 / 6.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xFF, 0xFF), 3.0 / 6.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x00, 0xFF), 4.0 / 6.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x00, 0xFF), 5.0 / 6.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 1.0));
        brush.Freeze();
        dc.DrawRectangle(brush, null, rect);
    }

    /// <summary>十字标记：黑环打底 + 白环，深浅底色上都能看清。</summary>
    void DrawSvMarker(DrawingContext dc, Point center)
    {
        var shadow = new Pen(CardPalette.Brush(Colors.Black, 0.55), 3);
        shadow.Freeze();
        dc.DrawEllipse(null, shadow, center, 6, 6);

        var ring = new Pen(Brushes.White, 1.5);
        ring.Freeze();
        dc.DrawEllipse(null, ring, center, 6, 6);
    }

    /// <summary>色相游标：横跨色相条的白色短杠 + 1px 暗描边，贴边时收在条内不溢出。</summary>
    void DrawHueMarker(DrawingContext dc, Rect hueRect, double offset)
    {
        double y = Math.Max(3, Math.Min(SvHeight - 3, offset));
        var marker = new Rect(hueRect.Left - 2, y - 2, hueRect.Width + 4, 4);

        var edge = new Pen(CardPalette.Brush(Colors.Black, 0.55), 1);
        edge.Freeze();
        dc.DrawRectangle(Brushes.White, edge, marker);
    }

    Pen EdgePen()
    {
        var pen = new Pen(CardPalette.Brush(_theme.Line), 1);
        pen.Freeze();
        return pen;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Point point = e.GetPosition(this);

        if (point.X <= SvWidth + Gap / 2)
        {
            _draggingSv = true;
            ApplySaturationValue(point);
        }
        else
        {
            _draggingHue = true;
            ApplyHue(point);
        }

        // 捕获鼠标：拖到方块外（甚至窗口外）仍要继续跟随，否则一松手颜色就停在边界上。
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_draggingSv && !_draggingHue) return;

        Point point = e.GetPosition(this);
        if (_draggingSv) ApplySaturationValue(point);
        else ApplyHue(point);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _draggingSv = false;
        _draggingHue = false;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // 捕获被别的控件抢走时也必须停止拖动，否则下一次鼠标移动会继续改色。
        _draggingSv = false;
        _draggingHue = false;
    }

    void ApplySaturationValue(Point point)
    {
        SetHsv(_hue,
               ColorPickerMath.Clamp01(point.X / SvWidth),
               ColorPickerMath.Clamp01(1 - point.Y / SvHeight));
        Changed?.Invoke();
    }

    void ApplyHue(Point point)
    {
        SetHsv(ColorPickerMath.Clamp01(point.Y / SvHeight) * 360.0, _saturation, _value);
        Changed?.Invoke();
    }
}

/// <summary>
/// 色值数学：HSV ↔ RGB、<c>#RRGGBB</c> 严格校验。
/// 抽出来是因为面板（Hex / RGB 输入）与色板（绘制、光标）必须用同一套换算，
/// 各写一份迟早会在边界色（灰、纯黑、360°）上分叉。
/// </summary>
internal static class ColorPickerMath
{
    internal static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    internal static double NormalizeHue(double hue)
    {
        double value = hue % 360.0;
        return value < 0 ? value + 360.0 : value;
    }

    /// <summary>严格 <c>#RRGGBB</c>：只接受 6 位十六进制，允许省略 #（用户手输常漏）。</summary>
    internal static bool TryParseHex(string text, out Color color)
    {
        color = Colors.Black;
        string value = (text ?? "").Trim();
        if (value.StartsWith("#", StringComparison.Ordinal)) value = value.Substring(1);
        if (value.Length != 6) return false;
        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;

        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    /// <summary>
    /// 解析设置里的当前值：接受 7 位与 9 位（#AARRGGBB 的 alpha 丢弃，取色器只输出不透明色）；
    /// 解析失败回退 <see cref="HudColorPicker.FallbackColor"/>，不静默变黑。
    /// </summary>
    internal static Color Parse(string text)
    {
        if (ColorCode.TryParse(text, out _, out byte r, out byte g, out byte b))
            return Color.FromRgb(r, g, b);

        TryParseHex(HudColorPicker.FallbackColor, out Color fallback);
        return fallback;
    }

    internal static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static Color FromHsv(double hue, double saturation, double value)
    {
        double h = NormalizeHue(hue) / 60.0;
        double s = Clamp01(saturation);
        double v = Clamp01(value);

        double f = h - Math.Floor(h);
        double p = v * (1 - s);
        double q = v * (1 - s * f);
        double t = v * (1 - s * (1 - f));

        (double r, double g, double b) = (int)Math.Floor(h) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q)
        };

        return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>
    /// RGB → HSV。色相是按引用进出：灰色（delta≈0）没有色相，保留调用方传进来的上一帧值，
    /// 否则把饱和度拖到 0 的瞬间色条会自己跳回红色。
    /// </summary>
    internal static void HsvFromColor(Color color, ref double hue, out double saturation, out double value)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        value = max;
        saturation = max <= 0 ? 0 : delta / max;
        if (delta <= 0.0001) return;

        double sector = max == r ? (g - b) / delta
                      : max == g ? (b - r) / delta + 2
                      : (r - g) / delta + 4;
        hue = NormalizeHue(sector * 60.0);
    }

    static byte ToByte(double value) => (byte)Math.Round(Clamp01(value) * 255.0);
}
