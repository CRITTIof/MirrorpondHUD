using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    public partial class SettingsWindow : Window
    {
        static readonly int[] RefreshOptions = { 200, 300, 500, 800, 1000, 2000 };
        static readonly int[] NvidiaOptions = { 1000, 2000, 3000, 5000 };

        readonly Settings _live;
        readonly Action<Settings> _onApply;
        readonly Action<double, double, int> _onPosition;
        readonly Action _onClearKeys;

        readonly List<CardEditor> _editors = new List<CardEditor>();
        readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        readonly List<KeyValuePair<string, TextBox>> _cardHotkeyBoxes = new List<KeyValuePair<string, TextBox>>();

        Settings _working;
        bool _suppress;
        bool _reallyClose;

        public SettingsWindow(Settings live, Action<Settings> onApply,
                              Action<double, double, int> onPosition, Action onClearKeys)
        {
            InitializeComponent();

            _live = live;
            _onApply = onApply;
            _onPosition = onPosition;
            _onClearKeys = onClearKeys;
            try { VersionText.Text = "v" + typeof(SettingsWindow).Assembly.GetName().Version; } catch { }

            HookEvents();
            BindNumberBoxes();
            LoadUi(live);
        }

        // ==================== 初始化 ====================

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

        /// <summary>字体下拉项：显示中文名（有的话）并用自己的字体渲染。</summary>
        public sealed class FontItem
        {
            public string Family { get; set; }
            public string Display { get; set; }
            public bool Chinese { get; set; }

            public override string ToString() { return Display; }
        }

        List<FontItem> _allFonts;
        FontItem _selectedFont;

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

        void HookEvents()
        {
            FontCombo.SelectionChanged += (s, e) =>
            {
                FontItem item = FontCombo.SelectedItem as FontItem;
                if (item != null) _selectedFont = item;
                UiChanged();
            };
            FontSizeSlider.ValueChanged += (s, e) => UiChanged();
            BoldCheck.Checked += (s, e) => UiChanged();
            BoldCheck.Unchecked += (s, e) => UiChanged();
            FgBox.TextChanged += (s, e) => UiChanged();
            BgBox.TextChanged += (s, e) => UiChanged();
            BgOpacitySlider.ValueChanged += (s, e) => UiChanged();
            AcrylicCheck.Checked += (s, e) => UiChanged();
            AcrylicCheck.Unchecked += (s, e) => UiChanged();
            GlassModeCombo.SelectionChanged += (s, e) => { UiChanged(); UpdateGlassUiState(); };
            GlassSourceCombo.SelectionChanged += (s, e) => { UiChanged(); UpdateGlassUiState(); };
            AcrylicBlurSlider.ValueChanged += (s, e) => UiChanged();
            AcrylicFpsSlider.ValueChanged += (s, e) => UiChanged();
            GlassBlurSlider.ValueChanged += (s, e) => UiChanged();
            GlassFpsSlider.ValueChanged += (s, e) => UiChanged();
            IconScaleSlider.ValueChanged += (s, e) => UiChanged();
            FontFilterBox.TextChanged += (s, e) => FilterFonts(FontFilterBox.Text);
            AlignCombo.SelectionChanged += (s, e) => UiChanged();
            PaddingSlider.ValueChanged += (s, e) => UiChanged();
            LineHeightSlider.ValueChanged += (s, e) => UiChanged();
            CornerSlider.ValueChanged += (s, e) => UiChanged();
            ShadowCheck.Checked += (s, e) => UiChanged();
            ShadowCheck.Unchecked += (s, e) => UiChanged();

            OutlineCheck.Checked += (s, e) => UiChanged();
            OutlineCheck.Unchecked += (s, e) => UiChanged();
            OutlineColorBox.TextChanged += (s, e) => UiChanged();
            OutlineSlider.ValueChanged += (s, e) => UiChanged();
            ImagePathBox.TextChanged += (s, e) => UiChanged();
            ImageOpacitySlider.ValueChanged += (s, e) => UiChanged();
            StretchCombo.SelectionChanged += (s, e) => UiChanged();
            FixedWidthSlider.ValueChanged += (s, e) => UiChanged();
            CursorStyleCombo.SelectionChanged += (s, e) => UiChanged();
            CursorBlinkCheck.Checked += (s, e) => UiChanged();
            CursorBlinkCheck.Unchecked += (s, e) => UiChanged();
        }

        public void Reload(Settings live)
        {
            LoadUi(live);
        }

        void LoadUi(Settings s)
        {
            _suppress = true;
            try
            {
                _working = Clone(s);
                CardModel.Normalize(_working);

                BuildFontList();
                _selectedFont = _allFonts.FirstOrDefault(
                                    f => string.Equals(f.Family, s.Style.FontFamily, StringComparison.OrdinalIgnoreCase))
                                ?? _allFonts.FirstOrDefault(
                                    f => string.Equals(f.Family, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
                                ?? _allFonts.FirstOrDefault();
                FontFilterBox.Text = "";
                FilterFonts("");
                FontCombo.SelectedItem = _selectedFont;

                // 毛玻璃有三条实现：accent 底板（系统）/ DWM 桌面亚克力底板 / 截屏取样（兼容）。
                string align = Settings.NormalizeCardAlign(s.Style.CardAlign);
                CardAlignCombo.SelectedIndex = align == "Left" ? 1 : align == "Right" ? 2 : 0;

                string glassMode = Settings.NormalizeGlassMode(s.Style.GlassMode);
                GlassModeCombo.SelectedIndex = glassMode == "Compat" ? 2 : glassMode == "Dwm" ? 1 : 0;
                GlassSourceCombo.SelectedIndex = GlassSourceIndex(s.Style.GlassSource);
                AcrylicCheck.IsEnabled = true;
                AcrylicCheck.IsChecked = s.Style.Acrylic;
                AcrylicBlurSlider.Value = s.Style.AcrylicBlur;
                AcrylicFpsSlider.Value = s.Style.AcrylicFps;
                GlassBlurSlider.Value = s.Style.GlassBlur;
                GlassFpsSlider.Value = s.Style.GlassFps;
                UpdateGlassUiState();

                FontSizeSlider.Value = s.Style.FontSize;
                IconScaleSlider.Value = s.Style.IconScale;
                BoldCheck.IsChecked = s.Style.Bold;
                FgBox.Text = s.Style.Foreground;
                BgBox.Text = s.Style.Background;
                BgOpacitySlider.Value = s.Style.BackgroundOpacity;
                AlignCombo.SelectedIndex = AlignmentIndex(s.Style.TextAlignment);
                PaddingSlider.Value = s.Style.Padding;
                LineHeightSlider.Value = s.Style.LineHeight;
                CornerSlider.Value = s.Overlay.CornerRadius;
                ShadowCheck.IsChecked = s.Overlay.Shadow;

                OutlineCheck.IsChecked = s.Style.TextOutline;
                OutlineColorBox.Text = s.Style.OutlineColor;
                OutlineSlider.Value = s.Style.OutlineThickness;
                ImagePathBox.Text = s.Style.BackgroundImage ?? "";
                ImageOpacitySlider.Value = s.Style.ImageOpacity;
                StretchCombo.SelectedIndex = StretchIndex(s.Style.ImageStretch);
                FixedWidthSlider.Value = s.Style.FixedWidth;

                CpuCheck.IsChecked = s.Data.ShowCpu;
                GpuCheck.IsChecked = s.Data.ShowGpu;
                MemCheck.IsChecked = s.Data.ShowMemory;
                DiskCheck.IsChecked = s.Data.ShowDisk;
                NetCheck.IsChecked = s.Data.ShowNetwork;
                KeysCheck.IsChecked = s.Data.ShowKeyboard;
                KeyCountBox.Text = s.Data.KeyHistoryCount.ToString(CultureInfo.InvariantCulture);
                RepeatBox.Text = s.Data.RepeatThreshold.ToString(CultureInfo.InvariantCulture);
                NvidiaCheck.IsChecked = s.Data.ShowNvidia;

                FillGpuCombo(s.Data.GpuIndex);
                FillRefreshCombo(s.Behavior.RefreshMs);
                FillNvidiaCombo(s.Data.NvidiaIntervalMs);
                FillAdapterCombo(s.Data.NetworkAdapter);
                FillDrives(s.Data.DiskDrives);
                FillMonitors(s.Overlay.Monitor);

                TopmostCheck.IsChecked = s.Overlay.Topmost;
                AutoStartCheck.IsChecked = s.Behavior.AutoStart;
                StartMinCheck.IsChecked = s.Behavior.StartMinimized;
                TrayCheck.IsChecked = s.Behavior.TrayEnabled;
                IntroCheck.IsChecked = s.Behavior.IntroEnabled;
                WelcomeBox.Text = string.IsNullOrEmpty(s.Behavior.WelcomeText) ? "Welcome Back" : s.Behavior.WelcomeText;
                TypeSpeedBox.Text = s.Behavior.TypeSpeedMs.ToString(CultureInfo.InvariantCulture);
                CursorStyleCombo.SelectedIndex = CursorIndex(s.Behavior.CursorStyle);
                CursorBlinkCheck.IsChecked = s.Behavior.CursorBlink;
                MaxKeyCharsBox.Text = s.Data.MaxKeyChars.ToString(CultureInfo.InvariantCulture);

                XBox.Text = s.Overlay.X.ToString("0", CultureInfo.InvariantCulture);
                YBox.Text = s.Overlay.Y.ToString("0", CultureInfo.InvariantCulture);

                HotkeyToggleBox.Text = s.Hotkeys.ToggleOverlay;
                HotkeySettingsBox.Text = s.Hotkeys.OpenSettings;
                HotkeyLockBox.Text = s.Hotkeys.ToggleLock;
                BuildCardHotkeyPanel();

                PathText.Text = ConfigStore.Path;
                StatusText.Text = "";
            }
            finally
            {
                _suppress = false;
            }

            UpdateSwatches();
            RebuildCardList();
        }

        static int AlignmentIndex(string value)
        {
            if (string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        static int StretchIndex(string value)
        {
            switch (Settings.NormalizeStretch(value))
            {
                case "Uniform": return 1;
                case "Fill": return 2;
                case "None": return 3;
                default: return 0;
            }
        }

        string StretchTag()
        {
            var item = StretchCombo.SelectedItem as ComboBoxItem;
            return item == null ? "UniformToFill" : Settings.NormalizeStretch(item.Tag as string);
        }

        string GlassModeTag()
        {
            var item = GlassModeCombo == null ? null : GlassModeCombo.SelectedItem as ComboBoxItem;
            return Settings.NormalizeGlassMode(item == null ? null : item.Tag as string);
        }

        string GlassSourceTag()
        {
            var item = GlassSourceCombo == null ? null : GlassSourceCombo.SelectedItem as ComboBoxItem;
            return Settings.NormalizeGlassSource(item == null ? null : item.Tag as string);
        }

        static int GlassSourceIndex(string value)
        {
            switch (Settings.NormalizeGlassSource(value))
            {
                case "Gpu": return 1;
                case "Cpu": return 2;
                default: return 0;
            }
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

        static int CursorIndex(string value)
        {
            switch (Settings.NormalizeCursor(value))
            {
                case "None": return 0;
                case "Bar": return 2;
                case "Underline": return 3;
                case "Outline": return 4;
                default: return 1;
            }
        }

        string CursorTag()
        {
            var item = CursorStyleCombo.SelectedItem as ComboBoxItem;
            return item == null ? "Block" : Settings.NormalizeCursor(item.Tag as string);
        }

        // ==================== 下拉框数据 ====================

        void FillGpuCombo(int selected)
        {
            int count = 0;
            try { count = MetricEngine.GetGpuCount(); } catch { }

            var items = new List<string> { "汇总（所有 GPU）" };
            for (int i = 0; i < count; i++) items.Add("GPU " + i + "（独立统计）");

            GpuCombo.ItemsSource = items;
            int index = selected + 1;
            GpuCombo.SelectedIndex = index >= 0 && index < items.Count ? index : 0;
        }

        void FillRefreshCombo(int current)
        {
            var items = RefreshOptions.Select(ms => ms + " 毫秒").ToList();
            RefreshCombo.ItemsSource = items;
            int index = Array.IndexOf(RefreshOptions, current);
            RefreshCombo.SelectedIndex = index >= 0 ? index : Array.IndexOf(RefreshOptions, 500);
        }

        void FillNvidiaCombo(int current)
        {
            var items = NvidiaOptions.Select(ms => (ms / 1000.0).ToString("0.0") + " 秒").ToList();
            NvidiaIntervalCombo.ItemsSource = items;
            int index = Array.IndexOf(NvidiaOptions, current);
            NvidiaIntervalCombo.SelectedIndex = index >= 0 ? index : 1;
        }

        void FillAdapterCombo(string current)
        {
            var items = new List<string> { "自动选择（流量最大的网卡）" };
            try { items.AddRange(MetricEngine.GetNetworkAdapters()); }
            catch { }

            AdapterCombo.ItemsSource = items;
            int index = 0;
            if (!string.IsNullOrEmpty(current))
            {
                int found = items.IndexOf(current);
                if (found >= 0) index = found;
            }
            AdapterCombo.SelectedIndex = index;
        }

        void FillDrives(List<string> whitelist)
        {
            DrivesPanel.Children.Clear();
            string[] drives;
            try { drives = MetricEngine.GetFixedDrives(); }
            catch { drives = new string[0]; }

            bool all = whitelist == null || whitelist.Count == 0;

            foreach (string drive in drives)
            {
                var box = new CheckBox
                {
                    Content = drive,
                    Tag = drive,
                    Margin = new Thickness(0, 0, 20, 8),
                    IsChecked = all || whitelist.Contains(drive)
                };
                DrivesPanel.Children.Add(box);
            }

            if (drives.Length == 0)
                DrivesPanel.Children.Add(new TextBlock { Text = "未检测到固定磁盘" });
        }

        void FillMonitors(int current)
        {
            var items = new List<string>();
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                    items.Add("显示器 " + (i + 1) + "（" + screens[i].Bounds.Width + "×" + screens[i].Bounds.Height + "）");
            }
            catch { }

            if (items.Count == 0) items.Add("显示器 1");
            MonitorCombo.ItemsSource = items;
            MonitorCombo.SelectedIndex = current >= 0 && current < items.Count ? current : 0;
        }

        // ==================== 卡片列表 ====================

        void RebuildCardList()
        {
            _editors.Clear();

            foreach (CardSettings config in _working.Cards)
            {
                bool expanded;
                if (!_expanded.TryGetValue(config.Id, out expanded)) expanded = true;

                var editor = new CardEditor(config, CardRegistry.Get(config.Kind), expanded);
                string id = config.Id;
                editor.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "IsExpanded") _expanded[id] = editor.IsExpanded;
                };
                _editors.Add(editor);
            }

            for (int i = 0; i < _editors.Count; i++)
                _editors[i].SpacingEnabled = i > 0;

            ApplyCardFilter();
        }

        void ApplyCardFilter()
        {
            string keyword = (SearchBox.Text ?? "").Trim();

            // 每次都换一个新 List：ItemsControl 不对 List<T> 的增删做通知，
            // 复用同一个实例会导致增删 / 移动后界面不刷新。
            List<CardEditor> filtered;
            if (keyword.Length == 0)
            {
                filtered = new List<CardEditor>(_editors);
            }
            else
            {
                filtered = _editors.Where(e =>
                    e.DisplayName.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    e.KindLabel.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            }

            CardList.ItemsSource = filtered;
            EmptyHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Text = keyword.Length == 0
                ? "还没有卡片，点左上角「添加卡片」新建一张"
                : "没有匹配「" + keyword + "」的卡片";
        }

        void SearchBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_working != null) ApplyCardFilter();
        }

        void AddCardClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            foreach (ICardModule module in CardRegistry.Modules)
            {
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                header.Children.Add(new TextBlock
                {
                    Text = module.Icon,
                    FontFamily = (FontFamily)FindResource("ReiconFont"),
                    FontSize = 15,
                    Foreground = (Brush)FindResource("Accent"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                header.Children.Add(new TextBlock
                {
                    Text = module.DisplayName,
                    Margin = new Thickness(9, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var item = new MenuItem { Header = header, Tag = module };
                item.Click += (s, a) => AddCard((ICardModule)((MenuItem)s).Tag);
                menu.Items.Add(item);
            }

            menu.PlacementTarget = AddCardButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        void AddCard(ICardModule module)
        {
            if (module == null) return;

            CardSettings card = CardModel.Create(module, _working.Cards);
            if (card == null) return;

            _working.Cards.Add(card);
            _expanded[card.Id] = true;

            SearchBox.Text = "";
            RebuildCardList();
            StatusText.Text = "已添加卡片「" + card.Name + "」，保存并应用后生效";

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { CardScroll.ScrollToEnd(); } catch { }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        void DeleteCardClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = (sender as FrameworkElement)?.Tag as CardEditor;
            if (editor == null) return;

            if (_working.Cards.Count <= 1)
            {
                StatusText.Text = "至少保留一张卡片";
                return;
            }

            MessageBoxResult answer = MessageBox.Show(this,
                "删除卡片「" + editor.DisplayName + "」？\n保存并应用后生效。",
                "镜潭一览", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            _working.Cards.Remove(editor.Config);
            _expanded.Remove(editor.Config.Id);
            RebuildCardList();
            StatusText.Text = "已删除卡片「" + editor.DisplayName + "」，保存并应用后生效";
        }

        void MoveCardClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = (sender as FrameworkElement)?.Tag as CardEditor;
            if (editor == null) return;

            int index = _working.Cards.IndexOf(editor.Config);

            var menu = new ContextMenu();
            var up = new MenuItem { Header = "上移", IsEnabled = index > 0 };
            up.Click += (s, a) => MoveCard(editor, -1);
            var down = new MenuItem { Header = "下移", IsEnabled = index >= 0 && index < _working.Cards.Count - 1 };
            down.Click += (s, a) => MoveCard(editor, 1);
            menu.Items.Add(up);
            menu.Items.Add(down);

            menu.PlacementTarget = sender as UIElement;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        void MoveCard(CardEditor editor, int delta)
        {
            int index = _working.Cards.IndexOf(editor.Config);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _working.Cards.Count) return;

            _working.Cards.RemoveAt(index);
            _working.Cards.Insert(target, editor.Config);

            // 移到非首位时给一个可见的最小间距，避免和上一张卡片贴在一起。
            if (target > 0 && editor.Config.Spacing < 1)
                editor.Config.Spacing = 12;

            RebuildCardList();
            StatusText.Text = "卡片「" + editor.DisplayName + "」已移动到第 " + (target + 1) + " 位";
        }

        void ToggleCardClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = (sender as FrameworkElement)?.Tag as CardEditor;
            if (editor != null) editor.IsExpanded = !editor.IsExpanded;
        }

        void CardHeaderClick(object sender, MouseButtonEventArgs e)
        {
            CardEditor editor = (sender as FrameworkElement)?.DataContext as CardEditor;
            if (editor != null) editor.IsExpanded = !editor.IsExpanded;
        }

        void CardHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = sender as TextBox;
            CardEditor editor = box == null ? null : box.DataContext as CardEditor;
            if (editor == null) return;

            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                editor.Hotkey = "";
                return;
            }

            if (IsModifierKey(key)) return;

            editor.Hotkey = HotkeyManager.Format(key, Keyboard.Modifiers);
        }

        void InsertTokenClick(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;

            var token = combo.SelectedItem as TokenInfo;
            if (token == null) return;

            var box = combo.Tag as TextBox;
            if (box != null)
            {
                string insert = "{" + token.Name + "}";
                string text = box.Text ?? "";
                int caret = Math.Min(Math.Max(0, box.CaretIndex), text.Length);
                box.Text = text.Insert(caret, insert);
                box.CaretIndex = caret + insert.Length;
                box.Focus();
            }

            combo.SelectedIndex = -1;
        }

        // ==================== 读取 ====================

        Settings ReadUi()
        {
            // 输入框里可能还有没失焦的 API Key（直接点了「确定」）：先加密收下来，别丢。
            foreach (CardEditor editor in _editors) editor.CommitChatApiKey();

            Settings s = _working;

            FontItem font = FontCombo.SelectedItem as FontItem ?? _selectedFont;
            if (font != null && !string.IsNullOrWhiteSpace(font.Family)) s.Style.FontFamily = font.Family;
            s.Style.FontSize = Math.Round(FontSizeSlider.Value);
            s.Style.IconScale = Math.Round(IconScaleSlider.Value, 2);
            s.Style.Bold = BoldCheck.IsChecked == true;
            s.Style.Foreground = (FgBox.Text ?? "").Trim();
            s.Style.Background = (BgBox.Text ?? "").Trim();
            s.Style.BackgroundOpacity = BgOpacitySlider.Value;
            s.Style.Acrylic = AcrylicCheck.IsChecked == true;
            s.Style.GlassMode = GlassModeTag();
            s.Style.GlassSource = GlassSourceTag();
            s.Style.AcrylicBlur = Math.Round(AcrylicBlurSlider.Value);
            s.Style.AcrylicFps = (int)Math.Round(AcrylicFpsSlider.Value);
            s.Style.GlassBlur = (int)Math.Round(GlassBlurSlider.Value);
            s.Style.GlassFps = (int)Math.Round(GlassFpsSlider.Value);
            s.Style.TextAlignment = AlignmentTag();
            s.Style.Padding = Math.Round(PaddingSlider.Value);
            s.Style.LineHeight = Math.Round(LineHeightSlider.Value);
            s.Overlay.CornerRadius = Math.Round(CornerSlider.Value);
            s.Overlay.Shadow = ShadowCheck.IsChecked == true;

            s.Style.TextOutline = OutlineCheck.IsChecked == true;
            s.Style.OutlineColor = (OutlineColorBox.Text ?? "").Trim();
            s.Style.OutlineThickness = Math.Round(OutlineSlider.Value, 2);
            s.Style.BackgroundImage = (ImagePathBox.Text ?? "").Trim();
            s.Style.ImageOpacity = ImageOpacitySlider.Value;
            s.Style.ImageStretch = StretchTag();
            s.Style.FixedWidth = Math.Round(FixedWidthSlider.Value);

            s.Data.ShowCpu = CpuCheck.IsChecked == true;
            s.Data.ShowGpu = GpuCheck.IsChecked == true;
            s.Data.ShowMemory = MemCheck.IsChecked == true;
            s.Data.ShowDisk = DiskCheck.IsChecked == true;
            s.Data.ShowNetwork = NetCheck.IsChecked == true;
            s.Data.ShowKeyboard = KeysCheck.IsChecked == true;
            s.Data.KeyHistoryCount = Math.Max(1, ParseInt(KeyCountBox.Text, s.Data.KeyHistoryCount));
            s.Data.MaxKeyChars = Math.Max(4, ParseInt(MaxKeyCharsBox.Text, s.Data.MaxKeyChars));
            s.Data.RepeatThreshold = Math.Max(1, ParseInt(RepeatBox.Text, s.Data.RepeatThreshold));
            s.Data.ShowNvidia = NvidiaCheck.IsChecked == true;
            s.Data.GpuIndex = Math.Max(0, GpuCombo.SelectedIndex) - 1;

            if (NvidiaIntervalCombo.SelectedIndex >= 0 &&
                NvidiaIntervalCombo.SelectedIndex < NvidiaOptions.Length)
                s.Data.NvidiaIntervalMs = NvidiaOptions[NvidiaIntervalCombo.SelectedIndex];

            s.Data.NetworkAdapter = AdapterCombo.SelectedIndex <= 0
                ? ""
                : (AdapterCombo.SelectedItem as string ?? "");

            var selectedDrives = new List<string>();
            var allDrives = new List<string>();
            foreach (object child in DrivesPanel.Children)
            {
                var box = child as CheckBox;
                if (box == null) continue;
                allDrives.Add((string)box.Tag);
                if (box.IsChecked == true) selectedDrives.Add((string)box.Tag);
            }
            s.Data.DiskDrives = selectedDrives.Count == allDrives.Count ? new List<string>() : selectedDrives;

            if (RefreshCombo.SelectedIndex >= 0 && RefreshCombo.SelectedIndex < RefreshOptions.Length)
                s.Behavior.RefreshMs = RefreshOptions[RefreshCombo.SelectedIndex];

            s.Overlay.Topmost = TopmostCheck.IsChecked == true;
            s.Behavior.AutoStart = AutoStartCheck.IsChecked == true;
            s.Behavior.StartMinimized = StartMinCheck.IsChecked == true;
            s.Behavior.TrayEnabled = TrayCheck.IsChecked == true;
            s.Behavior.IntroEnabled = IntroCheck.IsChecked == true;
            s.Behavior.WelcomeText = (WelcomeBox.Text ?? "").Trim();
            if (s.Behavior.WelcomeText.Length == 0) s.Behavior.WelcomeText = "Welcome Back";
            s.Behavior.TypeSpeedMs = Math.Max(8, ParseInt(TypeSpeedBox.Text, s.Behavior.TypeSpeedMs));
            s.Behavior.CursorStyle = CursorTag();
            s.Behavior.CursorBlink = CursorBlinkCheck.IsChecked == true;

            s.Overlay.Monitor = Math.Max(0, MonitorCombo.SelectedIndex);
            s.Overlay.X = ParseDouble(XBox.Text, s.Overlay.X);
            s.Overlay.Y = ParseDouble(YBox.Text, s.Overlay.Y);

            s.Style.CardAlign = CardAlignCombo.SelectedIndex == 1 ? "Left"
                               : CardAlignCombo.SelectedIndex == 2 ? "Right" : "Auto";

            s.Hotkeys.ToggleOverlay = (HotkeyToggleBox.Text ?? "").Trim();
            s.Hotkeys.OpenSettings = (HotkeySettingsBox.Text ?? "").Trim();
            s.Hotkeys.ToggleLock = (HotkeyLockBox.Text ?? "").Trim();


            foreach (KeyValuePair<string, TextBox> pair in _cardHotkeyBoxes)
                s.Hotkeys.SetCardAction(pair.Key, (pair.Value.Text ?? "").Trim());

            s.Normalize();
            CardModel.Normalize(s);
            return s;
        }

        string AlignmentTag()
        {
            var item = AlignCombo.SelectedItem as ComboBoxItem;
            return item == null ? "Left" : (item.Tag as string ?? "Left");
        }

        static int ParseInt(string text, int fallback)
        {
            int v;
            return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v) ? v : fallback;
        }

        static double ParseDouble(string text, double fallback)
        {
            double v;
            return double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v) ? v : fallback;
        }

        // ==================== 状态 ====================

        void UiChanged()
        {
            if (_suppress) return;
            UpdateSwatches();
        }

        void UpdateSwatches()
        {
            if (FgSwatch == null) return;
            FgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(FgBox.Text));
            BgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(BgBox.Text));
            OutlineSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(OutlineColorBox.Text));
        }

        // ==================== 事件 ====================

        void PickForegroundClick(object sender, RoutedEventArgs e)
        {
            string picked = PickColor(FgBox.Text);
            if (picked != null) FgBox.Text = picked;
        }

        void PickBackgroundClick(object sender, RoutedEventArgs e)
        {
            string picked = PickColor(BgBox.Text);
            if (picked != null) BgBox.Text = picked;
        }

        void PickOutlineColorClick(object sender, RoutedEventArgs e)
        {
            string picked = PickColor(OutlineColorBox.Text);
            if (picked != null) OutlineColorBox.Text = picked;
        }

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

        // ==================== 卡片内部：命令行 / 媒体 ====================

        static CardEditor EditorOf(object sender)
        {
            var element = sender as FrameworkElement;
            if (element == null) return null;
            return element.Tag as CardEditor ?? element.DataContext as CardEditor;
        }

        void PickCardWorkDirClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;

            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "选择这张命令行卡片的工作目录";
                    dialog.ShowNewFolderButton = true;

                    string current = (editor.WorkingDirectory ?? "").Trim();
                    if (current.Length > 0 && System.IO.Directory.Exists(current))
                        dialog.SelectedPath = current;

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        editor.WorkingDirectory = dialog.SelectedPath;
                        editor.RefreshSummary();
                        StatusText.Text = "卡片「" + editor.DisplayName + "」的工作目录已修改";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("选择工作目录失败: " + ex.Message);
            }
        }

        void ClearCardWorkDirClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;
            editor.WorkingDirectory = "";
            editor.RefreshSummary();
        }

        void PickMediaFileClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择图片 / 动图 / 视频",
                    Filter = "媒体文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv;*.webm|" +
                             "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico|" +
                             "视频|*.mp4;*.m4v;*.wmv;*.avi;*.mov;*.mkv;*.webm|所有文件|*.*",
                    CheckFileExists = true
                };

                string current = (editor.MediaPath ?? "").Trim();
                if (current.Length > 0)
                {
                    try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current); }
                    catch { }
                }

                if (dialog.ShowDialog(this) == true)
                {
                    editor.MediaPath = dialog.FileName;
                    editor.RefreshSummary();
                    StatusText.Text = "已选择媒体文件，点击“保存并应用”后显示";
                }
            }
            catch (Exception ex)
            {
                Log.Warn("选择媒体文件失败: " + ex.Message);
            }
        }

        void ClearMediaFileClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;
            editor.MediaPath = "";
            editor.RefreshSummary();
        }

        void PickDecoderClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择 ffmpeg.exe",
                    Filter = "ffmpeg|ffmpeg.exe|可执行文件|*.exe|所有文件|*.*",
                    CheckFileExists = true
                };

                string current = (editor.MediaDecoderPath ?? "").Trim();
                if (current.Length > 0)
                {
                    try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current); }
                    catch { }
                }

                if (dialog.ShowDialog(this) == true)
                {
                    editor.MediaDecoderPath = dialog.FileName;
                    StatusText.Text = "解码器已设为 " + System.IO.Path.GetFileName(dialog.FileName) +
                                      "，点击“保存并应用”后生效";
                }
            }
            catch (Exception ex)
            {
                Log.Warn("选择解码器失败: " + ex.Message);
            }
        }

        void DetectDecodersClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;

            try
            {
                editor.RefreshDecoders();
                StatusText.Text = editor.DecoderHint;
            }
            catch (Exception ex)
            {
                Log.Warn("检测解码器失败: " + ex.Message);
            }
        }

        // ==================== 卡片内部：AI 对话 ====================

        /// <summary>输入框失焦：把新填的 Key 加密收下来，之后框里只剩「已填写」提示。</summary>
        void ChatApiKeyBoxLostFocus(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;

            string before = editor.Config.Chat.ApiKey;
            editor.CommitChatApiKey();
            if (editor.Config.Chat.ApiKey == before) return;

            StatusText.Text = "卡片「" + editor.DisplayName + "」的 API Key 已加密保存（同一 Windows 用户可解）";
            Log.Info("AI Key 已更新（dpapi 加密，长度 " + editor.Config.Chat.ApiKey.Length + "）");
        }

        void ClearChatApiKeyClick(object sender, RoutedEventArgs e)
        {
            CardEditor editor = EditorOf(sender);
            if (editor == null) return;

            editor.ClearChatApiKey();
            StatusText.Text = "卡片「" + editor.DisplayName + "」的 API Key 已清除";
        }

        static string PickColor(string current)
        {
            try
            {
                using (var dialog = new System.Windows.Forms.ColorDialog())
                {
                    Color color = OverlaySurface.ParseColor(current);
                    dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
                    dialog.FullOpen = true;
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                    return "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("取色失败: " + ex.Message);
                return null;
            }
        }

        void RefreshAdaptersClick(object sender, RoutedEventArgs e)
        {
            string current = AdapterCombo.SelectedIndex > 0 ? AdapterCombo.SelectedItem as string : "";
            FillAdapterCombo(current);
        }

        void ClearKeysClick(object sender, RoutedEventArgs e)
        {
            if (_onClearKeys != null) _onClearKeys();
            StatusText.Text = "按键记录已清空";
        }

        void ApplyPositionClick(object sender, RoutedEventArgs e)
        {
            double x = ParseDouble(XBox.Text, 48);
            double y = ParseDouble(YBox.Text, 48);
            int monitor = Math.Max(0, MonitorCombo.SelectedIndex);
            if (_onPosition != null) _onPosition(x, y, monitor);
            StatusText.Text = "叠层已移动到 X=" + x + " Y=" + y;
        }

        /// <summary>
        /// 把叠层的实时锚点同步到「显示器 / X / Y」输入框。
        /// 拖动叠层和保存设置之后都会调用，保证对话框里显示的永远是真实位置。
        /// </summary>
        public void SyncAnchor(double x, double y, int monitor)
        {
            bool old = _suppress;
            _suppress = true;
            try
            {
                XBox.Text = x.ToString("0", CultureInfo.InvariantCulture);
                YBox.Text = y.ToString("0", CultureInfo.InvariantCulture);
                if (MonitorCombo.Items.Count > 0)
                    MonitorCombo.SelectedIndex = Math.Max(0, Math.Min(monitor, MonitorCombo.Items.Count - 1));
            }
            finally
            {
                _suppress = old;
            }
        }

        /// <summary>
        /// 按「有这种卡片」的模块生成热键分组：每组前面一条分割线 + 标题，
        /// 组内的动作由模块自己声明（目前是媒体卡的播放 / 暂停、前进、后退、加速、减速）。
        /// </summary>
        void BuildCardHotkeyPanel()
        {
            if (CardHotkeyPanel == null) return;

            CardHotkeyPanel.Children.Clear();
            _cardHotkeyBoxes.Clear();

            if (_working == null) return;

            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardSettings card in _working.Cards)
            {
                if (card != null && !string.IsNullOrWhiteSpace(card.Kind)) kinds.Add(card.Kind);
            }

            Brush line = null;
            try { line = FindResource("BorderSoft") as Brush; } catch { }

            Style sectionTitle = null;
            try { sectionTitle = FindResource("SectionTitle") as Style; } catch { }

            foreach (ICardModule module in CardRegistry.Modules)
            {
                var source = module as ICardHotkeySource;
                if (source == null || source.HotkeyActions == null || source.HotkeyActions.Count == 0) continue;
                if (!kinds.Contains(module.Kind)) continue;

                var section = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };

                section.Children.Add(new Border
                {
                    Height = 1,
                    Background = line ?? Brushes.Gray,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                section.Children.Add(new TextBlock
                {
                    Text = module.DisplayName + "卡片",
                    Style = sectionTitle,
                    Margin = new Thickness(0, 0, 0, 8)
                });

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
                        ToolTip = "清除这个热键（留空 = 不启用）"
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

                section.Children.Add(grid);
                CardHotkeyPanel.Children.Add(section);
            }
        }

        void HotkeyBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;

            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                box.Text = "";
                return;
            }

            if (IsModifierKey(key)) return;

            ModifierKeys mods = Keyboard.Modifiers;
            box.Text = HotkeyManager.Format(key, mods);
            box.CaretIndex = box.Text.Length;
        }

        /// <summary>「清空」按钮：按钮的 Tag 指向要清空的热键输入框。</summary>
        void ClearHotkeyBoxClick(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var box = button == null ? null : button.Tag as TextBox;
            if (box == null) return;

            box.Text = "";
            box.CaretIndex = 0;
        }

        /// <summary>「内容」页每张卡片的呼出热键清空。</summary>
        void ClearCardHotkeyClick(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            CardEditor editor = element == null ? null : element.DataContext as CardEditor;
            if (editor == null) return;

            editor.Hotkey = "";
        }

        static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LWin || key == Key.RWin;
        }

        /// <summary>留空表示禁用；返回 null 表示全部合法。</summary>
        static string ValidateSettings(Settings s)
        {
            uint mods, vk;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.ToggleOverlay) &&
                !HotkeyManager.TryParse(s.Hotkeys.ToggleOverlay, out mods, out vk))
                return "显示/隐藏热键无效：" + s.Hotkeys.ToggleOverlay;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.OpenSettings) &&
                !HotkeyManager.TryParse(s.Hotkeys.OpenSettings, out mods, out vk))
                return "设置热键无效：" + s.Hotkeys.OpenSettings;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.ToggleLock) &&
                !HotkeyManager.TryParse(s.Hotkeys.ToggleLock, out mods, out vk))
                return "锁定热键无效：" + s.Hotkeys.ToggleLock;

            foreach (CardSettings card in s.Cards)
            {
                if (card == null) continue;

                if (card.Command != null &&
                    string.Equals(Settings.NormalizeShell(card.Command.Shell), "custom", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(card.Command.CustomShell))
                    return "卡片「" + card.Name + "」选择了自定义命令行，但命令模板是空的。";

                if (card.Media != null &&
                    !string.IsNullOrWhiteSpace(card.Media.Path) &&
                    !System.IO.File.Exists(card.Media.Path))
                    return "卡片「" + card.Name + "」的媒体文件不存在：" + card.Media.Path;

                if (card.Resident) continue;
                if (CardRegistry.Get(card.Kind).IsInteractive) continue;

                if (string.IsNullOrWhiteSpace(card.Hotkey))
                    return "卡片「" + card.Name + "」没有呼出热键：勾选「常驻」，或给它设一个组合键。";

                if (!HotkeyManager.TryParse(card.Hotkey, out mods, out vk))
                    return "卡片「" + card.Name + "」的呼出热键无效：" + card.Hotkey;
            }

            return null;
        }

        void SaveClick(object sender, RoutedEventArgs e)
        {
            Settings s;
            try
            {
                s = ReadUi();
            }
            catch (Exception ex)
            {
                StatusText.Text = "读取设置失败：" + ex.Message;
                return;
            }

            string hotkeyError = ValidateSettings(s);
            if (hotkeyError != null)
            {
                StatusText.Text = hotkeyError;
                return;
            }

            if (_onApply != null) _onApply(s);

            // 控制器会把实时锚点带回来，同步到界面，避免下次保存又写回旧坐标。
            SyncAnchor(s.Overlay.X, s.Overlay.Y, s.Overlay.Monitor);

            _working = Clone(s);
            CardModel.Normalize(_working);
            RebuildCardList();

            StatusText.Text = "已保存并应用（" + DateTime.Now.ToString("HH:mm:ss") + "）";
        }

        void ResetClick(object sender, RoutedEventArgs e)
        {
            var fresh = new Settings();
            fresh.Normalize();
            CardModel.Normalize(fresh);
            LoadUi(fresh);
            StatusText.Text = "已恢复默认值，点击“保存并应用”生效";
        }

        void CloseClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        void OpenConfigFolderClick(object sender, RoutedEventArgs e)
        {
            OpenWithExplorer("", ConfigStore.Path);
        }

        void OpenLogClick(object sender, RoutedEventArgs e)
        {
            OpenWithExplorer("/select,", Log.FilePath);
        }

        static void OpenWithExplorer(string arguments, string path)
        {
            try
            {
                if (arguments.Length == 0)
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + System.IO.Path.GetDirectoryName(path) + "\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", arguments + "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("打开资源管理器失败: " + ex.Message);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        public void CloseForReal()
        {
            _reallyClose = true;
            Close();
        }

        static Settings Clone(Settings source)
        {
            try { return Json.Deserialize<Settings>(Json.Serialize(source)); }
            catch { return new Settings(); }
        }
    }
}
