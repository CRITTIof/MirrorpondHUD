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

namespace MirrorpondHUD.Ui
{
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
            BindNumberBox(AcrylicBlurValue, AcrylicBlurSlider, "0");
            BindNumberBox(AcrylicFpsValue, AcrylicFpsSlider, "0");
            BindNumberBox(GlassBlurValue, GlassBlurSlider, "0");
            BindNumberBox(GlassFpsValue, GlassFpsSlider, "0");
            BindNumberBox(IconScaleValue, IconScaleSlider, "0.00");
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

        void ApplyCliBoxes()
        {
            if (_working == null || _suppress) return;

            CliSettings cli = _working.Cli ?? (_working.Cli = new CliSettings());
            cli.MaxWidth = ParseBox(CliWidthBox, cli.MaxWidth);
            cli.MaxHeight = ParseBox(CliHeightBox, cli.MaxHeight);
            cli.EntryMaxHeight = ParseBox(CliEntryBox, cli.EntryMaxHeight);
            cli.MaxEntries = (int)ParseBox(CliEntriesBox, cli.MaxEntries);
            cli.Normalize();

            CliWidthBox.Text = cli.MaxWidth.ToString("0", CultureInfo.InvariantCulture);
            CliHeightBox.Text = cli.MaxHeight.ToString("0", CultureInfo.InvariantCulture);
            CliEntryBox.Text = cli.EntryMaxHeight.ToString("0", CultureInfo.InvariantCulture);
            CliEntriesBox.Text = cli.MaxEntries.ToString(CultureInfo.InvariantCulture);

            UiChanged();
        }

        static double ParseBox(TextBox box, double fallback)
        {
            double value;
            if (!double.TryParse((box.Text ?? "").Trim(), NumberStyles.Float,
                    CultureInfo.CurrentCulture, out value))
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
                double value;
                if (!double.TryParse((box.Text ?? "").Trim(), NumberStyles.Float,
                        CultureInfo.CurrentCulture, out value))
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

            public override string ToString() { return Display; }
        }

        void BuildFontList()
        {
            if (_allFonts != null) return;

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

                string display = chinese ? local + " (" + name + ")" : name;
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
        /// 三种实现各有各的参数：系统亚克力由 DWM 决定（没有可调项）；
        /// 兼容实现用「模糊强度 / 刷新帧率」；桌面亚克力用自己那一组（取样图缩 1/2 后再模糊，
        /// 所以同样的观感需要更大的「模糊强度」数字）。
        /// </summary>
        void UpdateGlassUiState()
        {
            if (AcrylicHint == null) return;

            string mode = GlassModeTag();
            bool system = mode == "System";
            bool dwm = mode == "Dwm";

            // 兼容实现那一组
            AcrylicBlurRow.Visibility = system || dwm ? Visibility.Collapsed : Visibility.Visible;
            AcrylicFpsRow.Visibility = system || dwm ? Visibility.Collapsed : Visibility.Visible;
            AcrylicFpsHint.Visibility = AcrylicFpsRow.Visibility;

            // 桌面亚克力那一组
            GlassBlurRow.Visibility = dwm ? Visibility.Visible : Visibility.Collapsed;
            GlassFpsRow.Visibility = dwm ? Visibility.Visible : Visibility.Collapsed;
            GlassFpsHint.Visibility = GlassFpsRow.Visibility;
            GlassSourceRow.Visibility = GlassFpsRow.Visibility;

            if (dwm)
            {
                AcrylicHint.Text = "抓取卡片后面的画面、模糊后画进卡片自己的内容层（圆角和卡片一致）。"
                    + "为了让截屏不拍到自己，卡片会被排除在截屏之外 —— 这个模式下截图 / 录屏里看不到卡片。";
            }
            else if (system)
            {
                AcrylicHint.Text = "由 DWM 在卡片下面的独立底板上画实时亚克力：模糊在 GPU 上完成、进程侧不抓屏，"
                    + "「模糊强度 / 刷新帧率」由系统决定（这里不可调）。关闭系统透明效果或远程桌面时会自动退回兼容实现。";
            }
            else
            {
                AcrylicHint.Text = "抓取卡片后面的画面并模糊，当作卡片底色；压在桌面上时直接用壁纸取样（图标不参与）。"
                    + "把「背景不透明度」调到 0.5 左右更容易看出效果。";
            }

            // 「取图方式」只对桌面亚克力有意义：系统亚克力不抓屏，兼容实现走的是自己的拼图路线。
            if (GlassSourceCombo != null) GlassSourceCombo.IsEnabled = dwm;
            if (GlassSourceHint != null)
            {
                GlassSourceHint.Visibility = dwm ? Visibility.Visible : Visibility.Collapsed;
                if (dwm)
                {
                    string source = Settings.NormalizeGlassSource(GlassSourceTag());
                    if (source == "Gpu")
                        GlassSourceHint.Text = "DXGI 桌面复制：一轮取图约 1 ms（GDI 读屏要 18 ms），"
                            + "所以刷新帧率可以放心开高。独占全屏 / 远程桌面 / 换显卡时会自动退回 CPU 读屏，画面不会断。";
                    else if (source == "Cpu")
                        GlassSourceHint.Text = "GDI BitBlt 读屏：一轮约 18 ms 且占着 UI 线程，"
                            + "刷新帧率别超过 8；兼容性最好 —— 桌面复制在你这台机器上出问题时选这个。";
                    else
                        GlassSourceHint.Text = "能拿到桌面复制就走 GPU 加速（约 1 ms/轮），"
                            + "拿不到（独占全屏 / 远程桌面 / 设备被占）自动退回 CPU 读屏（约 18 ms/轮）。";
                }
            }
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
                Log.Warn("选择背景图片失败: " + ex.Message);
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
            if (FgSwatch == null) return;
            FgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(FgBox.Text));
            BgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(BgBox.Text));
            OutlineSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(OutlineColorBox.Text));
        }
    }
}
