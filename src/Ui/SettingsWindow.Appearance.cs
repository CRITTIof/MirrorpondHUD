using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

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

    /// <summary>
    /// S-18：字体下拉的筛选视图。整个窗口生命周期只建一次，筛选时只 Refresh，
    /// 不再每次键入都换 ItemsSource（换源会让 ComboBox 重建全部项容器，几百项时输入明显发顿）。
    /// </summary>
    ICollectionView _fontView;

    /// <summary>当前筛选词；后台加载完成回填时要按它重新过滤，不能丢用户输入。</summary>
    string _fontKeyword = "";

    /// <summary>正在程序化套用字体列表 / 选中项：此时 SelectedItem 变化不是用户操作，不能标脏。</summary>
    bool _applyingFonts;

    /// <summary>
    /// S-18：系统字体表是进程级数据，后台枚举一次、所有设置窗实例共享。
    /// 旧实现在 UI 线程首次打开设置窗时全量枚举（还要取本地化名），中文字体多的机器会停顿数百毫秒。
    /// </summary>
    static readonly object FontGate = new();
    static List<FontItem> _fontCache;
    static Task<List<FontItem>> _fontLoad;

    // ==================== 数字框绑定 ====================

    /// <summary>滑块旁的数字框：可点击直接输入，回车/失焦生效。</summary>
    void BindNumberBoxes()
    {
        BindNumberBox(FontSizeValue, FontSizeSlider, "0");
        BindNumberBox(BgOpacityValue, BgOpacitySlider, "0.00");
        BindNumberBox(PaddingValue, PaddingSlider, "0");
        BindNumberBox(LineHeightValue, LineHeightSlider, "0");
        BindNumberBox(CornerValue, CornerSlider, "0");
        BindNumberBox(EdgeBarWidthValue, EdgeBarWidthSlider, "0");
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
    /// S-19：四个 CLI 尺寸框逐个校验、逐个提交。
    ///
    /// 旧实现把解析失败静默换回旧值，然后无条件重写四个框：既吃掉用户输入，又会覆盖另一个
    /// 框里尚未失焦的半截内容，而且每次失焦都触发一次全量 UiChanged（标脏 + 重建预览）。
    /// 现在：非法 → 就地标红 + ToolTip（保留原文让用户改）；超范围 → 按区间钳制并在框上说明；
    /// 只有设置值真的变化才提交，只有真的要规范化时才回写那一个框。
    /// </summary>
    void ApplyCliBoxesTo(Settings target, bool writeBack)
    {
        if (target is null || _suppress) return;

        CliSettings cli = target.Cli ?? (target.Cli = new CliSettings());
        bool changed = false;

        // markUi 跟随 writeBack：判脏用的探针副本不该在界面上闪红框。
        changed |= CommitDoubleBox(CliWidthBox, 240, 1600, writeBack,
                                   () => cli.MaxWidth, v => cli.MaxWidth = v, "面板宽度");
        changed |= CommitDoubleBox(CliHeightBox, 140, 3000, writeBack,
                                   () => cli.MaxHeight, v => cli.MaxHeight = v, "面板最大高度");
        changed |= CommitDoubleBox(CliEntryBox, 60, 2000, writeBack,
                                   () => cli.EntryMaxHeight, v => cli.EntryMaxHeight = v, "单条记录最大高度");
        changed |= CommitIntBox(CliEntriesBox, 1, 100, writeBack,
                                () => cli.MaxEntries, v => cli.MaxEntries = v, "最多保留条数");

        // 两条区间互相约束（与 CliSettings.Normalize 同义）：单条高度不能超过面板最大高度。
        // 只有真的越界才回写这一个框，并说明原因。
        if (cli.EntryMaxHeight > cli.MaxHeight)
        {
            cli.EntryMaxHeight = cli.MaxHeight;
            changed = true;
            if (writeBack)
            {
                CliEntryBox.Text = FormatCliValue(cli.EntryMaxHeight);
                MarkFieldHint(CliEntryBox, $"不能大于面板最大高度，已按 {FormatCliValue(cli.MaxHeight)} 生效");
            }
        }

        if (writeBack && changed) UiChanged();
    }

    /// <summary>读一个浮点尺寸框；返回设置值是否真的变化。</summary>
    bool CommitDoubleBox(TextBox box, double min, double max, bool markUi,
                         Func<double> get, Action<double> set, string what)
    {
        string text = (box.Text ?? "").Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed))
        {
            if (markUi) MarkFieldInvalid(box, $"{what}需要数字（{FormatCliValue(min)}–{FormatCliValue(max)}）");
            return false;
        }

        double value = Math.Max(min, Math.Min(max, parsed));
        if (markUi)
        {
            if (Math.Abs(value - parsed) > 0.001)
            {
                // 超范围不是格式错误：提交钳制值，并把「实际生效值」写回这个框。
                box.Text = FormatCliValue(value);
                MarkFieldHint(box, $"{what}超出范围，按 {FormatCliValue(value)} 生效（{FormatCliValue(min)}–{FormatCliValue(max)}）");
            }
            else
            {
                ClearFieldValidation(box);
            }
        }

        if (Math.Abs(get() - value) < 0.0001) return false;
        set(value);
        return true;
    }

    /// <summary>读一个整数尺寸框；返回设置值是否真的变化。</summary>
    bool CommitIntBox(TextBox box, int min, int max, bool markUi,
                      Func<int> get, Action<int> set, string what)
    {
        string text = (box.Text ?? "").Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed))
        {
            if (markUi) MarkFieldInvalid(box, $"{what}需要整数（{min}–{max}）");
            return false;
        }

        int value = Math.Max(min, Math.Min(max, parsed));
        if (markUi)
        {
            if (value != parsed)
            {
                box.Text = value.ToString(CultureInfo.CurrentCulture);
                MarkFieldHint(box, $"{what}超出范围，按 {value} 生效（{min}–{max}）");
            }
            else
            {
                ClearFieldValidation(box);
            }
        }

        if (get() == value) return false;
        set(value);
        return true;
    }

    /// <summary>就地标记非法输入：红框 + ToolTip（沿用 ValidateColorBox 的视觉语言）。</summary>
    void MarkFieldInvalid(TextBox box, string message)
    {
        box.BorderBrush = FindResource("Danger") as Brush;
        box.ToolTip = message;
    }

    /// <summary>值合法但被钳制：不标红（值会生效），只用 ToolTip 说明真实生效值。</summary>
    void MarkFieldHint(TextBox box, string message)
    {
        box.ClearValue(Control.BorderBrushProperty);
        box.ToolTip = message;
    }

    void ClearFieldValidation(TextBox box)
    {
        box.ClearValue(Control.BorderBrushProperty);
        box.ToolTip = null;
    }

    static string FormatCliValue(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

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

    /// <summary>启动时预热（宿主构造 OverlayController 时调用）：后台枚举字体，首次打开设置窗直接命中缓存。</summary>
    public static void WarmFontCache() => _ = EnsureFontCacheAsync();

    static Task<List<FontItem>> EnsureFontCacheAsync()
    {
        lock (FontGate)
        {
            if (_fontCache is not null) return Task.FromResult(_fontCache);

            return _fontLoad ??= Task.Factory.StartNew(() =>
            {
                List<FontItem> fonts = LoadSystemFonts();
                lock (FontGate)
                {
                    // 空表代表枚举失败：不写入缓存，下次打开设置窗再试一次（失败原因已进日志）。
                    if (fonts.Count > 0) _fontCache = fonts;
                    _fontLoad = null;
                }
                return fonts;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    static bool TryGetFontCache(out List<FontItem> fonts)
    {
        lock (FontGate)
        {
            fonts = _fontCache;
            return fonts is not null;
        }
    }

    /// <summary>
    /// 独立 STA 线程枚举系统字体：WPF 的字体表 API 在部分系统上要求 STA，
    /// 直接扔进线程池（MTA）有踩坑风险；这里用一次性线程，读完只返回纯字符串数据，
    /// 不把任何 WPF 对象带出后台线程。
    /// </summary>
    static List<FontItem> LoadSystemFonts()
    {
        try
        {
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
                catch (Exception ex)
                {
                    // 个别字体族读不到本地化名：退回英文名即可，不影响列表可用性。
                    Log.Debug($"读取字体本地化名失败({name}): {ex.Message}");
                }

                bool chinese = !string.IsNullOrWhiteSpace(local)
                               && !string.Equals(local, name, StringComparison.OrdinalIgnoreCase)
                               && HasCjk(local);

                string display = chinese ? $"{local} ({name})" : name;
                items.Add(new FontItem { Family = name, Display = display, Chinese = chinese });
            }

            // 2.45.6：嵌入的思源黑体不在 SystemFontFamilies 里，手动放在第一项；
            // 否则默认值 "Noto Sans SC" 在字体下拉中选不中，看起来像“空字体”。
            items.Insert(0, new FontItem
            {
                Family = HudFont.DefaultName,
                Display = HudFont.DisplayName,
                Chinese = true
            });

            List<FontItem> sorted = items
                .OrderBy(f => f.Chinese ? 0 : 1)
                .ThenBy(f => f.Display, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Log.Info($"系统字体枚举完成：{sorted.Count} 项（后台线程）");
            return sorted;
        }
        catch (Exception ex)
        {
            // 字体表读不到属于环境故障：界面显示空列表并带提示，原因必须进日志。
            Log.Warn($"系统字体枚举失败: {ex.Message}");
            return new List<FontItem>();
        }
    }

    /// <summary>
    /// 把字体列表装进界面。缓存命中时同步完成；冷启动时先禁用下拉框，
    /// 后台枚举完成后回到 UI 线程回填 —— UI 线程不再做全量枚举（S-18）。
    /// </summary>
    void LoadFontsIntoUi(string family)
    {
        _fontKeyword = "";

        if (TryGetFontCache(out var cached))
        {
            ApplyFontList(cached, family);
            return;
        }

        _applyingFonts = true;
        try
        {
            FontCombo.ItemsSource = null;
            FontCombo.SelectedItem = null;
            FontCombo.IsEnabled = false;
            FontCombo.ToolTip = "正在后台加载系统字体…";
        }
        finally
        {
            _applyingFonts = false;
        }

        _ = EnsureFontCacheAsync().ContinueWith(task =>
        {
            if (task.Status != TaskStatus.RanToCompletion) return;
            Dispatcher.BeginInvoke(new Action(() => ApplyFontList(task.Result, family)), DispatcherPriority.Background);
        }, TaskScheduler.Default);
    }

    void ApplyFontList(List<FontItem> fonts, string family)
    {
        // 窗口可能已经真正关闭（后台枚举还没回来）：不再往可视树上写。
        if (_reallyClose) return;

        // 缓存列表是共享的，视图必须按窗口实例建；Reload 时复用同一个视图，保留筛选状态。
        if (!ReferenceEquals(_allFonts, fonts) || _fontView is null)
        {
            _allFonts = fonts;
            _fontView = new ListCollectionView(fonts) { Filter = FontPassesFilter };
            FontCombo.ItemsSource = _fontView;
        }

        _selectedFont = fonts.FirstOrDefault(
                            f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase))
                        ?? fonts.FirstOrDefault(
                            f => string.Equals(f.Family, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
                        ?? fonts.FirstOrDefault();

        FontCombo.IsEnabled = true;
        FontCombo.ToolTip = fonts.Count == 0 ? "系统字体枚举失败（详情见日志）" : null;

        _applyingFonts = true;
        try
        {
            FontFilterBox.Text = _fontKeyword;
            _fontView.Refresh();
            FontCombo.SelectedItem = _selectedFont;
        }
        finally
        {
            _applyingFonts = false;
        }
    }

    static bool HasCjk(string text)
    {
        foreach (char c in text)
            if (c >= 0x3400 && c <= 0x9FFF) return true;
        return false;
    }

    void FilterFonts(string keyword)
    {
        _fontKeyword = (keyword ?? "").Trim();

        // 字体还在后台枚举：加载完成后 ApplyFontList 会按这个关键字刷新视图。
        if (_fontView is null) return;

        // S-18：只刷新同一个视图的过滤结果，不换 ItemsSource、不重建 List。
        _applyingFonts = true;
        try
        {
            _fontView.Refresh();
            FontCombo.SelectedItem = _selectedFont;
        }
        finally
        {
            _applyingFonts = false;
        }
    }

    bool FontPassesFilter(object item)
    {
        if (_fontKeyword.Length == 0) return true;
        if (item is not FontItem font) return false;

        return font.Family.IndexOf(_fontKeyword, StringComparison.CurrentCultureIgnoreCase) >= 0
               || font.Display.IndexOf(_fontKeyword, StringComparison.CurrentCultureIgnoreCase) >= 0;
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
                try
                {
                    dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current);
                }
                catch (Exception ex)
                {
                    // 当前路径是相对路径 / 非法字符时选不出初始目录：对话框仍会打开，
                    // 只是从默认位置开始，不影响用户选择。
                    Log.Debug($"设置背景图片对话框初始目录失败: {ex.Message}");
                }
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
