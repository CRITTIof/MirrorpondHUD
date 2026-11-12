using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗「外观」页：字体列表 / 筛选、字号 / 图标 / 对齐 / 内边距 / 行高 / 描边、
/// CLI 面板尺寸、背景图片，以及滑块旁数字框的绑定。
///
/// 主题列表、导入导出、预览与色板在 <c>SettingsWindow.Theme.cs</c>；
/// 下拉框里的「取图方式 / 玻璃实现 / 拉伸」取值辅助方法也在这里（它们只服务外观页）。
/// </summary>
public partial class SettingsWindow
{
    List<FontItem> _allFonts;
    FontItem _selectedFont;

    // ==================== 数字框绑定 ====================

    /// <summary>滑块旁的数字框：可点击直接输入，回车/失焦生效。</summary>
    void BindNumberBoxes()
    {
        BindNumberBox(FontSizeValue, FontSizeSlider, "0");
        BindNumberBox(BgOpacityValue, BgOpacitySlider, "0.00");
        BindNumberBox(PaddingValue, PaddingSlider, "0");
        BindNumberBox(LineHeightValue, LineHeightSlider, "0");
        BindNumberBox(CornerValue, CornerSlider, "0");
        BindNumberBox(OutlineValue, OutlineSlider, "0.0");
        BindNumberBox(ImageOpacityValue, ImageOpacitySlider, "0.00");
        BindNumberBox(FixedWidthValue, FixedWidthSlider, "0");
        BindNumberBox(GlassBlurValue, GlassBlurSlider, "0");
        BindNumberBox(GlassFpsValue, GlassFpsSlider, "0");
        BindNumberBox(IconScaleValue, IconScaleSlider, "0.00");
        BindNumberBox(LoopDurationValue, LoopDurationSlider, "0");
        BindNumberBox(LoopAmplitudeValue, LoopAmplitudeSlider, "0.00");
        BindNumberBox(PulseThicknessValue, PulseThicknessSlider, "0");
        BindNumberBox(ScanlineThicknessValue, ScanlineThicknessSlider, "0");
        BindNumberBox(DecorationDensityValue, DecorationDensitySlider, "0.00");
        BindNumberBox(ContourLevelsValue, ContourLevelsSlider, "0");
        BindNumberBox(ContourEveningValue, ContourEveningSlider, "0.00");
        BindNumberBox(ContourReliefValue, ContourReliefSlider, "0.00");
        BindNumberBox(ContourSmoothValue, ContourSmoothSlider, "0");
        BindNumberBox(ContourMinLoopValue, ContourMinLoopSlider, "0");
        BindNumberBox(ContourSpeedValue, ContourSpeedSlider, "0.00");
        BindNumberBox(TextParticleSizeValue, TextParticleSizeSlider, "0.0");
        BindNumberBox(TextParticleCountValue, TextParticleCountSlider, "0");
        BindNumberBox(TextParticleSpeedValue, TextParticleSpeedSlider, "0.0");
        BindCliBoxes();
    }

    /// <summary>CLI 面板的三个尺寸框：失焦或回车时提交（边打字边重建预览会抖）。</summary>
    void BindCliBoxes()
    {
        TextBox[] boxes = { CliWidthBox, CliHeightBox, CliEntryBox, CliEntriesBox };
        foreach (TextBox box in boxes)
        {
            TextBox captured = box;
            captured.LostFocus += (s, e) => ApplyCliBoxes();
            captured.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                ApplyCliBoxes();
            };
        }
    }

    void ApplyCliBoxes() => ApplyCliBoxesTo(_working, true);

    /// <summary>
    /// S-11：判脏时把 CLI 输入框读进副本，不回写控件、不触发 UiChanged。
    /// </summary>
    void ApplyCliBoxesTo(Settings target, bool writeBack)
    {
        if (target is null || _suppress) return;

        CliSettings cli = target.Cli ?? (target.Cli = new CliSettings());
        cli.MaxWidth = ParseBox(CliWidthBox, cli.MaxWidth);
        cli.MaxHeight = ParseBox(CliHeightBox, cli.MaxHeight);
        cli.EntryMaxHeight = ParseBox(CliEntryBox, cli.EntryMaxHeight);
        cli.MaxEntries = (int)ParseBox(CliEntriesBox, cli.MaxEntries);
        cli.Normalize();

        if (!writeBack) return;

        CliWidthBox.Text = cli.MaxWidth.ToString("0", CultureInfo.InvariantCulture);
        CliHeightBox.Text = cli.MaxHeight.ToString("0", CultureInfo.InvariantCulture);
        CliEntryBox.Text = cli.EntryMaxHeight.ToString("0", CultureInfo.InvariantCulture);
        CliEntriesBox.Text = cli.MaxEntries.ToString(CultureInfo.InvariantCulture);

        UiChanged();
    }

    static double ParseBox(TextBox box, double fallback)
    {
        if (!double.TryParse((box.Text ?? "").Trim(), NumberStyles.Float,
                CultureInfo.CurrentCulture, out var value))
            return fallback;
        return value;
    }

    void BindNumberBox(TextBox box, Slider slider, string format)
    {
        bool updating = false;

        slider.ValueChanged += (s, e) =>
        {
            if (updating) return;
            if (box.IsKeyboardFocusWithin) return;
            updating = true;
            box.Text = slider.Value.ToString(format, CultureInfo.InvariantCulture);
            updating = false;
        };

        Action commit = () =>
        {
            if (updating) return;
            if (!double.TryParse((box.Text ?? "").Trim(), NumberStyles.Float,
                    CultureInfo.CurrentCulture, out var value))
            {
                box.Text = slider.Value.ToString(format, CultureInfo.InvariantCulture);
                return;
            }

            value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            updating = true;
            slider.Value = value;
            box.Text = slider.Value.ToString(format, CultureInfo.InvariantCulture);
            updating = false;
            UiChanged();
        };

        box.LostFocus += (s, e) => commit();
        box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                commit();
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };
        box.GotFocus += (s, e) => box.SelectAll();

        box.Text = slider.Value.ToString(format, CultureInfo.InvariantCulture);
    }

    // ==================== 字体 ====================

    /// <summary>字体下拉项：显示中文名（有的话）并用自己的字体渲染。</summary>
    public sealed class FontItem
    {
        public string Family { get; set; }
        public string Display { get; set; }
        public bool Chinese { get; set; }

        public override string ToString() => Display;
    }

    void BuildFontList()
    {
        if (_allFonts is not null) return;

        XmlLanguage zh = XmlLanguage.GetLanguage("zh-CN");
        var items = new List<FontItem>();

        foreach (FontFamily family in Fonts.SystemFontFamilies)
        {
            string name = family.Source;
            if (string.IsNullOrWhiteSpace(name)) continue;

            string local = null;
            try
            {
                if (family.FamilyNames.ContainsKey(zh)) local = family.FamilyNames[zh];
            }
            catch { }

            bool chinese = !string.IsNullOrWhiteSpace(local)
                           && !string.Equals(local, name, StringComparison.OrdinalIgnoreCase)
                           && HasCjk(local);

            string display = chinese ? $"{local} ({name})" : name;
            items.Add(new FontItem { Family = name, Display = display, Chinese = chinese });
        }

        _allFonts = items
            .OrderBy(f => f.Chinese ? 0 : 1)
            .ThenBy(f => f.Display, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    static bool HasCjk(string text)
    {
        foreach (char c in text)
            if (c >= 0x3400 && c <= 0x9FFF) return true;
        return false;
    }

    void FilterFonts(string keyword)
    {
        BuildFontList();

        keyword = (keyword ?? "").Trim();
        IEnumerable<FontItem> filtered = _allFonts;
        if (keyword.Length > 0)
        {
            filtered = _allFonts.Where(f =>
                f.Family.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                f.Display.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        FontCombo.ItemsSource = filtered.ToList();
        FontCombo.SelectedItem = _selectedFont;
    }

    /// <summary>
    /// 三种实现共用一组「模糊强度 / 刷新帧率」（Schema 7 起 Acrylic / Glass 双套字段已合并成
    /// <c>BlurRadius</c> / <c>BlurFps</c>）：系统亚克力由系统决定，另外两种可调；
    /// 「取图方式」只对桌面取样（DesktopFeed）有意义。
    /// 末尾追加运行时状态里的「当前实际」，用户才看得出自己选的实现有没有被降级。
    /// </summary>
    void UpdateGlassUiState()
    {
        if (AcrylicHint is null) return;

        string mode = GlassModeTag();
        bool system = mode == "System";
        bool desktop = mode == "DesktopFeed";

        // 系统亚克力的模糊半径 / 帧率由系统决定，不显示可调项；兼容与桌面取样共用同一组参数。
        GlassBlurRow.Visibility = system ? Visibility.Collapsed : Visibility.Visible;
        GlassFpsRow.Visibility = system ? Visibility.Collapsed : Visibility.Visible;
        GlassFpsHint.Visibility = GlassFpsRow.Visibility;

        // 「取图方式」只对桌面取样有意义：系统亚克力不抓屏，兼容实现走的是自己的拼图路线。
        GlassSourceRow.Visibility = desktop ? Visibility.Visible : Visibility.Collapsed;
        if (GlassSourceCombo is not null) GlassSourceCombo.IsEnabled = desktop;
        if (GlassSourceHint is not null)
        {
            GlassSourceHint.Visibility = desktop ? Visibility.Visible : Visibility.Collapsed;
            if (desktop)
            {
                string source = Settings.NormalizeGlassSource(GlassSourceTag());
                if (source == "Gpu")
                    GlassSourceHint.Text = $"DXGI 桌面复制：一轮取图约 1 ms（GDI 读屏要 18 ms），所以刷新帧率可以放心开高。独占全屏 / 远程桌面 / 换显卡时会自动退回 CPU 读屏，画面不会断。";
                else if (source == "Cpu")
                    GlassSourceHint.Text = $"GDI BitBlt 读屏：一轮约 18 ms 且占着 UI 线程，刷新帧率别超过 8；兼容性最好 —— 桌面复制在你这台机器上出问题时选这个。";
                else
                    GlassSourceHint.Text = $"能拿到桌面复制就走 GPU 加速（约 1 ms/轮），拿不到（独占全屏 / 远程桌面 / 设备被占）自动退回 CPU 读屏（约 18 ms/轮）。";
            }
        }

        // 每次从基础文案重建，再按需追加「当前实际」——切来切去时不会把同一句重复叠上去。
        string hint;
        if (desktop)
        {
            hint = $"抓取卡片后面的画面、模糊后画进卡片自己的内容层（圆角和卡片一致）。为了让截屏不拍到自己，卡片会被排除在截屏之外 —— 这个模式下截图 / 录屏里看不到卡片。";
        }
        else if (system)
        {
            hint = $"由 DWM 在卡片下面的独立底板上画实时亚克力：模糊在 GPU 上完成、进程侧不抓屏，「模糊强度 / 刷新帧率」由系统决定（这里不可调）。关闭系统透明效果或远程桌面时会自动退回兼容实现。";
        }
        else
        {
            hint = $"抓取卡片后面的画面并模糊，当作卡片底色；压在桌面上时直接用壁纸取样（图标不参与）。把「背景不透明度」调到 0.5 左右更容易看出效果。";
        }

        GlassRuntimeState state = _glassState is null ? null : _glassState();
        if (state is not null && state.Kind != GlassBackdropKind.None)
            hint += $" 当前实际：{state.Describe()}";

        AcrylicHint.Text = hint;
    }

    // ==================== 背景图片 ====================

    void PickBackgroundImageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*",
                CheckFileExists = true
            };

            string current = (ImagePathBox.Text ?? "").Trim();
            if (current.Length > 0)
            {
                try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current); }
                catch { }
            }

            if (dialog.ShowDialog(this) == true)
            {
                ImagePathBox.Text = dialog.FileName;
                StatusText.Text = "已选择背景图片，点击“保存并应用”生效";
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"选择背景图片失败: {ex.Message}");
        }
    }

    void ClearBackgroundImageClick(object sender, RoutedEventArgs e)
    {
        ImagePathBox.Text = "";
        StatusText.Text = "已清除背景图片";
    }

    // ==================== 色板 ====================

    void UpdateSwatches()
    {
        if (FgSwatch is null) return;
        FgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(FgBox.Text));
        BgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(BgBox.Text));
        OutlineSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(OutlineColorBox.Text));

        // 状态色允许留空表示“沿用派生值”，所以预览必须走 ThemeResolver，
        // 不能直接 ParseColor(空串)（那会显示成白色，和卡片上的实际颜色不符）。
        // 用控件里的即时值做一份轻量副本：用户还没保存时预览也要跟手。
        var preview = new StyleSettings
        {
            Foreground = (FgBox.Text ?? "").Trim(),
            Accent = _working is null ? new StyleSettings().Accent : _working.Style.Accent,
            Success = (SuccessColorBox.Text ?? "").Trim(),
            Warning = (WarningColorBox.Text ?? "").Trim()
        };
        ThemeTokens tokens = ThemeResolver.Resolve(preview);
        if (SuccessSwatch is not null)
        {
            SuccessSwatch.Background = new SolidColorBrush(tokens.Success);
            SuccessSwatch.ToolTip = string.IsNullOrWhiteSpace(preview.Success)
                ? $"空 = 沿用强调色（{tokens.Success}）"
                : $"成功色预览（{tokens.Success}）";
        }
        if (WarningSwatch is not null)
        {
            WarningSwatch.Background = new SolidColorBrush(tokens.Warning);
            WarningSwatch.ToolTip = string.IsNullOrWhiteSpace(preview.Warning)
                ? $"空 = 前景色 90%（{tokens.Warning}）"
                : $"警告色预览（{tokens.Warning}）";
        }
    }
}
