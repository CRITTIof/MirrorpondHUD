using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        void HookEvents()
        {
            ContentBox.TextChanged += (s, e) => UiChanged();
            FontCombo.SelectionChanged += (s, e) => UiChanged();
            FontSizeSlider.ValueChanged += (s, e) => UiChanged();
            BoldCheck.Checked += (s, e) => UiChanged();
            BoldCheck.Unchecked += (s, e) => UiChanged();
            FgBox.TextChanged += (s, e) => UiChanged();
            BgBox.TextChanged += (s, e) => UiChanged();
            BgOpacitySlider.ValueChanged += (s, e) => UiChanged();
            AlignCombo.SelectionChanged += (s, e) => UiChanged();
            PaddingSlider.ValueChanged += (s, e) => UiChanged();
            LineHeightSlider.ValueChanged += (s, e) => UiChanged();
            CornerSlider.ValueChanged += (s, e) => UiChanged();
            ShadowCheck.Checked += (s, e) => UiChanged();
            ShadowCheck.Unchecked += (s, e) => UiChanged();

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

                List<string> fonts = Fonts.SystemFontFamilies
                    .Select(f => f.Source)
                    .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                FontCombo.ItemsSource = fonts;
                FontCombo.SelectedItem = fonts.FirstOrDefault(
                    f => string.Equals(f, s.Style.FontFamily, StringComparison.OrdinalIgnoreCase)) ?? "Microsoft YaHei UI";

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
                LockedCheck.IsChecked = s.Overlay.Locked;
                AutoStartCheck.IsChecked = s.Behavior.AutoStart;
                StartMinCheck.IsChecked = s.Behavior.StartMinimized;
                TrayCheck.IsChecked = s.Behavior.TrayEnabled;
                IntroCheck.IsChecked = s.Behavior.IntroEnabled;
                WelcomeBox.Text = string.IsNullOrEmpty(s.Behavior.WelcomeText) ? "Welcome Back" : s.Behavior.WelcomeText;
                TypeSpeedBox.Text = s.Behavior.TypeSpeedMs.ToString(CultureInfo.InvariantCulture);

                XBox.Text = s.Overlay.X.ToString("0", CultureInfo.InvariantCulture);
                YBox.Text = s.Overlay.Y.ToString("0", CultureInfo.InvariantCulture);

                HotkeyToggleBox.Text = s.Hotkeys.ToggleOverlay;
                HotkeySettingsBox.Text = s.Hotkeys.OpenSettings;
                HotkeyLockBox.Text = s.Hotkeys.ToggleLock;

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
            UpdatePreview();
        }

        static int AlignmentIndex(string value)
        {
            if (string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
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

            string font = FontCombo.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(font)) s.Style.FontFamily = font;
            s.Style.FontSize = Math.Round(FontSizeSlider.Value);
            s.Style.Bold = BoldCheck.IsChecked == true;
            s.Style.Foreground = (FgBox.Text ?? "").Trim();
            s.Style.Background = (BgBox.Text ?? "").Trim();
            s.Style.BackgroundOpacity = BgOpacitySlider.Value;
            s.Style.TextAlignment = AlignmentTag();
            s.Style.Padding = Math.Round(PaddingSlider.Value);
            s.Style.LineHeight = Math.Round(LineHeightSlider.Value);
            s.Overlay.CornerRadius = Math.Round(CornerSlider.Value);
            s.Overlay.Shadow = ShadowCheck.IsChecked == true;

            s.Data.ShowCpu = CpuCheck.IsChecked == true;
            s.Data.ShowGpu = GpuCheck.IsChecked == true;
            s.Data.ShowMemory = MemCheck.IsChecked == true;
            s.Data.ShowDisk = DiskCheck.IsChecked == true;
            s.Data.ShowNetwork = NetCheck.IsChecked == true;
            s.Data.ShowKeyboard = KeysCheck.IsChecked == true;
            s.Data.KeyHistoryCount = Math.Max(1, ParseInt(KeyCountBox.Text, s.Data.KeyHistoryCount));
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
            s.Overlay.Locked = LockedCheck.IsChecked == true;
            s.Behavior.AutoStart = AutoStartCheck.IsChecked == true;
            s.Behavior.StartMinimized = StartMinCheck.IsChecked == true;
            s.Behavior.TrayEnabled = TrayCheck.IsChecked == true;
            s.Behavior.IntroEnabled = IntroCheck.IsChecked == true;
            s.Behavior.WelcomeText = (WelcomeBox.Text ?? "").Trim();
            if (s.Behavior.WelcomeText.Length == 0) s.Behavior.WelcomeText = "Welcome Back";
            s.Behavior.TypeSpeedMs = Math.Max(8, ParseInt(TypeSpeedBox.Text, s.Behavior.TypeSpeedMs));

            s.Overlay.Monitor = Math.Max(0, MonitorCombo.SelectedIndex);
            s.Overlay.X = ParseDouble(XBox.Text, s.Overlay.X);
            s.Overlay.Y = ParseDouble(YBox.Text, s.Overlay.Y);

            s.Hotkeys.ToggleOverlay = (HotkeyToggleBox.Text ?? "").Trim();
            s.Hotkeys.OpenSettings = (HotkeySettingsBox.Text ?? "").Trim();
            s.Hotkeys.ToggleLock = (HotkeyLockBox.Text ?? "").Trim();

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
        }

        void UpdatePreview()
        {
            try
            {
                Settings s = ReadUi();
                var keys = new KeyHistory();
                keys.Configure(s.Data.KeyHistoryCount, s.Data.RepeatThreshold);
                foreach (string k in new[] { "Ctrl", "C", "Ctrl", "V", "A", "A", "A", "A", "A", "A", "Enter" })
                    keys.Push(k);

                Dictionary<string, object> vars = Variables.Build(_demo, s, keys);
                List<string> unknown;
                string text = TemplateEngine.Render(s.Content, vars, out unknown);

                PreviewSurface.Apply(s, text);

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
            if (!HotkeyManager.TryParse(s.Hotkeys.ToggleOverlay, out mods, out vk))
            {
                StatusText.Text = "显示/隐藏热键无效：" + s.Hotkeys.ToggleOverlay;
                return;
            }
            if (!HotkeyManager.TryParse(s.Hotkeys.OpenSettings, out mods, out vk))
            {
                StatusText.Text = "设置热键无效：" + s.Hotkeys.OpenSettings;
                return;
            }
            if (!HotkeyManager.TryParse(s.Hotkeys.ToggleLock, out mods, out vk))
            {
                StatusText.Text = "锁定热键无效：" + s.Hotkeys.ToggleLock;
                return;
            }

            if (_onApply != null) _onApply(s);

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
