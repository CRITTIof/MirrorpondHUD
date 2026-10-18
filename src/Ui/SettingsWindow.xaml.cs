using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    public partial class SettingsWindow : Window
    {
        static readonly int[] RefreshOptions = { 200, 300, 500, 800, 1000, 2000 };
        static readonly int[] NvidiaOptions = { 1000, 2000, 3000, 5000 };

        readonly Settings _live;
        readonly Action<Settings> _onApply;
        readonly Action<double, double, int> _onPosition;
        readonly Action _onClearKeys;
        readonly DispatcherTimer _previewTimer;
        readonly MetricsSnapshot _demo;

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
            _demo = Screenshots.DemoSnapshot();
            try { VersionText.Text = "v" + typeof(SettingsWindow).Assembly.GetName().Version; } catch { }

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); UpdatePreview(); };

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
            ContentBox.TextChanged += (s, e) => UiChanged();
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
            AcrylicBlurSlider.ValueChanged += (s, e) => UiChanged();
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

            ShellCombo.SelectionChanged += (s, e) => { UpdateCommandUi(); UiChanged(); };
            CustomShellBox.TextChanged += (s, e) => { UpdateCommandUi(); UiChanged(); };
            WorkDirBox.TextChanged += (s, e) => { UpdateCommandUi(); UiChanged(); };
            TruncateCheck.Checked += (s, e) => { UpdateCommandUi(); UiChanged(); };
            TruncateCheck.Unchecked += (s, e) => { UpdateCommandUi(); UiChanged(); };

            TokenList.MouseDoubleClick += (s, e) =>
            {
                var token = TokenList.SelectedItem as TokenInfo;
                if (token != null) InsertToken(token.Name);
            };
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

                ContentBox.Text = s.Content ?? "";

                BuildFontList();
                _selectedFont = _allFonts.FirstOrDefault(
                                    f => string.Equals(f.Family, s.Style.FontFamily, StringComparison.OrdinalIgnoreCase))
                                ?? _allFonts.FirstOrDefault(
                                    f => string.Equals(f.Family, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
                                ?? _allFonts.FirstOrDefault();
                FontFilterBox.Text = "";
                FilterFonts("");
                FontCombo.SelectedItem = _selectedFont;

                bool acrylicSupported = Native.IsAcrylicSupported;
                AcrylicCheck.IsEnabled = acrylicSupported;
                AcrylicCheck.IsChecked = s.Style.Acrylic && acrylicSupported;
                AcrylicBlurSlider.Value = s.Style.AcrylicBlur;
                AcrylicBlurSlider.IsEnabled = acrylicSupported;
                AcrylicHint.Text = acrylicSupported
                    ? "实验功能：抓取叠层后面的桌面并模糊。把「背景不透明度」调到 0.5 左右更容易看出效果；模糊强度控制模糊半径。"
                    : "当前系统不支持抓屏排除（需要 Windows 10 2004 / build 19041 及以上）。";

                FontSizeSlider.Value = s.Style.FontSize;
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
                HotkeyCommandBox.Text = s.Hotkeys.CommandLine;
                HotkeySettingsBox.Text = s.Hotkeys.OpenSettings;
                HotkeyLockBox.Text = s.Hotkeys.ToggleLock;

                ShellCombo.SelectedIndex = ShellIndex(s.Command.Shell);
                CustomShellBox.Text = s.Command.CustomShell;
                WorkDirBox.Text = s.Command.WorkingDirectory;
                TruncateCheck.IsChecked = s.Command.Truncate;
                MaxLineCharsBox.Text = s.Command.MaxLineChars.ToString(CultureInfo.InvariantCulture);
                MaxLinesBox.Text = s.Command.MaxLines.ToString(CultureInfo.InvariantCulture);
                TimeoutBox.Text = s.Command.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);

                TokenList.ItemsSource = Catalog.Tokens;
                PresetCombo.ItemsSource = Catalog.Presets;
                PresetCombo.SelectedIndex = 0;

                PathText.Text = ConfigStore.Path;
                StatusText.Text = "";
            }
            finally
            {
                _suppress = false;
            }

            UpdateSwatches();
            UpdateCommandUi();
            UpdatePreview();
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

        static int ShellIndex(string shell)
        {
            switch (Settings.NormalizeShell(shell))
            {
                case "powershell": return 1;
                case "pwsh": return 2;
                case "custom": return 3;
                default: return 0;
            }
        }

        string ShellTag()
        {
            var item = ShellCombo.SelectedItem as ComboBoxItem;
            return item == null ? "cmd" : Settings.NormalizeShell(item.Tag as string);
        }

        CommandSettings BuildCommandSettings()
        {
            CommandSettings command = _working == null ? new CommandSettings() : _working.Command;
            command.Shell = ShellTag();
            command.CustomShell = (CustomShellBox.Text ?? "").Trim();
            command.WorkingDirectory = (WorkDirBox.Text ?? "").Trim();
            command.Truncate = TruncateCheck.IsChecked == true;
            return command;
        }

        void UpdateCommandUi()
        {
            if (ShellCombo == null || CustomShellBox == null || PromptPreview == null) return;

            CustomShellBox.IsEnabled = string.Equals(ShellTag(), "custom", StringComparison.OrdinalIgnoreCase);
            MaxLineCharsBox.IsEnabled = TruncateCheck.IsChecked == true;

            try
            {
                PromptPreview.Text = CommandRunner.BuildPrompt(BuildCommandSettings());
            }
            catch (Exception ex)
            {
                PromptPreview.Text = "(预览失败: " + ex.Message + ")";
            }
        }

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

        // ==================== 读取 ====================

        Settings ReadUi()
        {
            Settings s = _working;

            s.Content = ContentBox.Text ?? "";

            FontItem font = FontCombo.SelectedItem as FontItem ?? _selectedFont;
            if (font != null && !string.IsNullOrWhiteSpace(font.Family)) s.Style.FontFamily = font.Family;
            s.Style.FontSize = Math.Round(FontSizeSlider.Value);
            s.Style.Bold = BoldCheck.IsChecked == true;
            s.Style.Foreground = (FgBox.Text ?? "").Trim();
            s.Style.Background = (BgBox.Text ?? "").Trim();
            s.Style.BackgroundOpacity = BgOpacitySlider.Value;
            s.Style.Acrylic = AcrylicCheck.IsChecked == true;
            s.Style.AcrylicBlur = Math.Round(AcrylicBlurSlider.Value);
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

            s.Hotkeys.ToggleOverlay = (HotkeyToggleBox.Text ?? "").Trim();
            s.Hotkeys.CommandLine = (HotkeyCommandBox.Text ?? "").Trim();
            s.Hotkeys.OpenSettings = (HotkeySettingsBox.Text ?? "").Trim();
            s.Hotkeys.ToggleLock = (HotkeyLockBox.Text ?? "").Trim();

            s.Command.Shell = ShellTag();
            s.Command.CustomShell = (CustomShellBox.Text ?? "").Trim();
            s.Command.WorkingDirectory = (WorkDirBox.Text ?? "").Trim();
            s.Command.Truncate = TruncateCheck.IsChecked == true;
            s.Command.MaxLineChars = Math.Max(20, ParseInt(MaxLineCharsBox.Text, s.Command.MaxLineChars));
            s.Command.MaxLines = Math.Max(1, ParseInt(MaxLinesBox.Text, s.Command.MaxLines));
            s.Command.TimeoutSeconds = Math.Max(1, ParseInt(TimeoutBox.Text, s.Command.TimeoutSeconds));

            s.Normalize();
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

        // ==================== 预览 ====================

        void UiChanged()
        {
            if (_suppress) return;
            UpdateSwatches();
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        void UpdateSwatches()
        {
            FgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(FgBox.Text));
            BgSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(BgBox.Text));
            OutlineSwatch.Background = new SolidColorBrush(OverlaySurface.ParseColor(OutlineColorBox.Text));
        }

        void UpdatePreview()
        {
            try
            {
                Settings s = ReadUi();
                var keys = new KeyHistory();
                keys.Configure(s.Data.KeyHistoryCount, s.Data.RepeatThreshold, s.Data.MaxKeyChars);
                foreach (string k in new[] { "Ctrl", "C", "Ctrl", "V", "A", "A", "A", "A", "A", "A", "Enter" })
                    keys.Push(k);

                Dictionary<string, object> vars = Variables.Build(_demo, s, keys);
                List<string> unknown;
                string text = TemplateEngine.Render(s.Content, vars, out unknown);

                PreviewSurface.Apply(s, text, s.Behavior.IntroEnabled);

                UnknownHint.Text = unknown.Count == 0
                    ? ""
                    : "未知占位符: " + string.Join("、", unknown);
            }
            catch (Exception ex)
            {
                Log.Warn("预览失败: " + ex.Message);
            }
        }

        void InsertToken(string name)
        {
            string insert = "{" + name + "}";
            string text = ContentBox.Text ?? "";
            int caret = Math.Min(Math.Max(0, ContentBox.CaretIndex), text.Length);
            ContentBox.Text = text.Insert(caret, insert);
            ContentBox.CaretIndex = caret + insert.Length;
            ContentBox.Focus();
        }

        // ==================== 事件 ====================

        void ApplyPresetClick(object sender, RoutedEventArgs e)
        {
            var preset = PresetCombo.SelectedItem as PresetInfo;
            if (preset == null) return;
            ContentBox.Text = preset.Content;
            ContentBox.CaretIndex = ContentBox.Text.Length;
        }

        void InsertTokenClick(object sender, RoutedEventArgs e)
        {
            var token = TokenList.SelectedItem as TokenInfo;
            if (token != null) InsertToken(token.Name);
        }

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

        void PickWorkDirClick(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "选择快捷命令行的工作目录";
                    dialog.ShowNewFolderButton = true;

                    string current = (WorkDirBox.Text ?? "").Trim();
                    if (current.Length > 0 && System.IO.Directory.Exists(current))
                        dialog.SelectedPath = current;

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        WorkDirBox.Text = dialog.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("选择工作目录失败: " + ex.Message);
            }
        }

        void ResetWorkDirClick(object sender, RoutedEventArgs e)
        {
            WorkDirBox.Text = "";
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
            StatusText.Text = "已移动到 X=" + x + " Y=" + y;
        }

        /// <summary>
        /// 把叠层的实时坐标同步到「显示器 / X / Y」输入框。
        /// 拖动叠层和保存设置之后都会调用，保证对话框里显示的永远是叠层的真实位置。
        /// </summary>
        public void SyncPosition(double x, double y, int monitor)
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

        static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LWin || key == Key.RWin;
        }

        /// <summary>留空表示禁用；返回 null 表示全部合法。</summary>
        static string ValidateHotkeys(Settings s, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.ToggleOverlay) &&
                !HotkeyManager.TryParse(s.Hotkeys.ToggleOverlay, out mods, out vk))
                return "显示/隐藏热键无效：" + s.Hotkeys.ToggleOverlay;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.CommandLine) &&
                !HotkeyManager.TryParse(s.Hotkeys.CommandLine, out mods, out vk))
                return "快捷命令行热键无效：" + s.Hotkeys.CommandLine;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.OpenSettings) &&
                !HotkeyManager.TryParse(s.Hotkeys.OpenSettings, out mods, out vk))
                return "设置热键无效：" + s.Hotkeys.OpenSettings;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.ToggleLock) &&
                !HotkeyManager.TryParse(s.Hotkeys.ToggleLock, out mods, out vk))
                return "锁定热键无效：" + s.Hotkeys.ToggleLock;

            if (!string.IsNullOrWhiteSpace(s.Hotkeys.ToggleOverlay) &&
                !string.IsNullOrWhiteSpace(s.Hotkeys.CommandLine) &&
                string.Equals(s.Hotkeys.ToggleOverlay, s.Hotkeys.CommandLine, StringComparison.OrdinalIgnoreCase))
                return "「显示/隐藏」和「快捷命令行」不能共用同一个热键";

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

            uint mods, vk;
            string hotkeyError = ValidateHotkeys(s, out mods, out vk);
            if (hotkeyError != null)
            {
                StatusText.Text = hotkeyError;
                return;
            }

            if (_onApply != null) _onApply(s);

            // 控制器把位置 / 锁定换成了实时值，同步回界面，避免下次保存又写回旧坐标。
            SyncPosition(s.Overlay.X, s.Overlay.Y, s.Overlay.Monitor);

            _working = Clone(s);
            StatusText.Text = "已保存并应用（" + DateTime.Now.ToString("HH:mm:ss") + "）";
        }

        void ResetClick(object sender, RoutedEventArgs e)
        {
            LoadUi(new Settings());
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
