using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 设置窗的「装载」与「读取」两个总入口：LoadUi 把设置树灌进控件，ReadUi 把控件收回设置树。
    ///
    /// 这两个方法的**顺序与语义是硬契约**（报告 §4.2 / 第三批第 13 项）：
    /// 顺序决定了「下拉框先填选项、再选中值」「_suppress 期间不触发 UiChanged」等依赖，
    /// 拆分时逐行照搬，没有合并 / 重排任何一步。往设置树里写回时的数字钳制与文案也保持原样。
    /// </summary>
    public partial class SettingsWindow
    {
        bool LoadUi(Settings s)
        {
            _suppress = true;
            try
            {
                Settings working;
                string cloneError;
                if (!TryClone(s, out working, out cloneError))
                {
                    // 绝不能用 new Settings() 顶上：那会让用户看到全默认值，一点保存就把默认值写盘。
                    _loadFailed = true;
                    StatusText.Text = "读取当前设置失败：" + cloneError + "；已禁止保存，避免覆盖 settings.json";
                    return false;
                }

                _loadFailed = false;
                _working = working;
                CardModel.Normalize(_working);
                CaptureRuntimeBaseline();

                ThemeCombo.SelectedIndex = ThemeIndexOf(_working.Style.ThemeName);
                ThemeNameText.Text = string.IsNullOrWhiteSpace(_working.Style.ThemeName)
                    ? "自定义" : _working.Style.ThemeName;

                BuildFontList();
                _selectedFont = _allFonts.FirstOrDefault(
                                    f => string.Equals(f.Family, s.Style.FontFamily, StringComparison.OrdinalIgnoreCase))
                                ?? _allFonts.FirstOrDefault(
                                    f => string.Equals(f.Family, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
                                ?? _allFonts.FirstOrDefault();
                FontFilterBox.Text = "";
                FilterFonts("");
                FontCombo.SelectedItem = _selectedFont;

                // 毛玻璃有三条实现：accent 底板（系统）/ 桌面取样（DesktopFeed）/ 兼容取样（Compat）。
                string align = Settings.NormalizeCardAlign(s.Style.CardAlign);
                CardAlignCombo.SelectedIndex = align == "Left" ? 1 : align == "Right" ? 2 : 0;

                string glassMode = Settings.NormalizeGlassMode(s.Style.GlassMode);
                GlassModeCombo.SelectedIndex = glassMode == "Compat" ? 2 : glassMode == "DesktopFeed" ? 1 : 0;
                GlassSourceCombo.SelectedIndex = GlassSourceIndex(s.Style.GlassSource);
                AcrylicCheck.IsEnabled = true;
                AcrylicCheck.IsChecked = s.Style.Acrylic;
                GlassBlurSlider.Value = s.Style.BlurRadius;
                GlassFpsSlider.Value = s.Style.BlurFps;
                UpdateGlassUiState();

                CliWidthBox.Text = s.Cli.MaxWidth.ToString("0", CultureInfo.InvariantCulture);
                CliHeightBox.Text = s.Cli.MaxHeight.ToString("0", CultureInfo.InvariantCulture);
                CliEntryBox.Text = s.Cli.EntryMaxHeight.ToString("0", CultureInfo.InvariantCulture);
                CliEntriesBox.Text = s.Cli.MaxEntries.ToString(CultureInfo.InvariantCulture);

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
                MonoReadoutsCheck.IsChecked = s.Style.MonoReadouts;
                IndustrialHeaderCheck.IsChecked = s.Style.IndustrialHeader;
                DecorationCombo.SelectedIndex = DecorationIndex(s.Style.Decoration);
                MotionProfileCombo.SelectedIndex = MotionProfileIndex(s.Style.MotionProfile);
                LoopEffectCombo.SelectedIndex = LoopEffectIndex(s.Style.LoopEffect);
                LoopDurationSlider.Value = s.Style.LoopDurationMs;
                LoopAmplitudeSlider.Value = s.Style.LoopAmplitude;
                SuccessColorBox.Text = s.Style.Success ?? "";
                WarningColorBox.Text = s.Style.Warning ?? "";

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
                ReduceMotionCheck.IsChecked = s.Behavior.ReduceMotion;
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
                ValidateColorBox(FgBox, false);
                ValidateColorBox(BgBox, false);
                ValidateColorBox(OutlineColorBox, true);
                ValidateColorBox(SuccessColorBox, true);
                ValidateColorBox(WarningColorBox, true);
                ValidateHotkeyBox(HotkeyToggleBox);
                ValidateHotkeyBox(HotkeySettingsBox);
                ValidateHotkeyBox(HotkeyLockBox);
                foreach (KeyValuePair<string, TextBox> pair in _cardHotkeyBoxes)
                    ValidateHotkeyBox(pair.Value);

                PathText.Text = ConfigStore.Path;
                StatusText.Text = "";
            }
            finally
            {
                _suppress = false;
            }

            UpdateSwatches();
            RebuildCardList();
            RenderThemePreview();
            ClearDirty();
            CaptureCleanSnapshot();
            return true;
        }

        // ==================== 下拉框取值辅助（LoadUi / ReadUi 使用） ====================

        static int AlignmentIndex(string value)
        {
            if (string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        string AlignmentTag()
        {
            var item = AlignCombo.SelectedItem as ComboBoxItem;
            return item == null ? "Left" : (item.Tag as string ?? "Left");
        }

        static int DecorationIndex(string value)
        {
            switch (Settings.NormalizeDecoration(value))
            {
                case "Grid": return 1;
                case "Scanline": return 2;
                case "Hatch": return 3;
                case "Halftone": return 4;
                case "Contour": return 5;
                default: return 0;
            }
        }

        string DecorationTag()
        {
            var item = DecorationCombo == null ? null : DecorationCombo.SelectedItem as ComboBoxItem;
            return Settings.NormalizeDecoration(item == null ? null : item.Tag as string);
        }

        static int MotionProfileIndex(string value)
        {
            switch (Settings.NormalizeMotionProfile(value))
            {
                case "Cut": return 1;
                case "Industrial": return 2;
                case "Off": return 3;
                default: return 0;
            }
        }

        string MotionProfileTag()
        {
            var item = MotionProfileCombo == null ? null : MotionProfileCombo.SelectedItem as ComboBoxItem;
            return Settings.NormalizeMotionProfile(item == null ? null : item.Tag as string);
        }

        static int LoopEffectIndex(string value)
        {
            switch (Settings.NormalizeLoopEffect(value, false))
            {
                case "Breath": return 1;
                case "Scanline": return 2;
                case "Pulse": return 3;
                default: return 0;
            }
        }

        string LoopEffectTag()
        {
            var item = LoopEffectCombo == null ? null : LoopEffectCombo.SelectedItem as ComboBoxItem;
            return Settings.NormalizeLoopEffect(item == null ? null : item.Tag as string, false);
        }

        static int StretchIndex(string value)        {
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

        // ==================== 读取 ====================

        Settings ReadUi()
        {
            _readingUi = true;
            try
            {
                // 输入框里可能还有没失焦的 API Key（直接点了「确定」）：先加密收下来，别丢。
                foreach (CardEditor editor in _editors) editor.CommitPendingEdits();

                Settings s = _working;
                ApplyCliBoxes();   // 输入框可能还没失焦（直接点了「确定」）

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
                // Schema 7：Acrylic / Glass 双套模糊字段合并为 BlurRadius / BlurFps，三种实现共用。
                s.Style.BlurRadius = (int)Math.Round(GlassBlurSlider.Value);
                s.Style.BlurFps = (int)Math.Round(GlassFpsSlider.Value);
                s.Style.TextAlignment = AlignmentTag();
                s.Style.Padding = Math.Round(PaddingSlider.Value);
                s.Style.LineHeight = Math.Round(LineHeightSlider.Value);
                s.Overlay.CornerRadius = Math.Round(CornerSlider.Value);
                s.Overlay.Shadow = ShadowCheck.IsChecked == true;
                s.Style.MonoReadouts = MonoReadoutsCheck.IsChecked == true;
                s.Style.IndustrialHeader = IndustrialHeaderCheck.IsChecked == true;
                s.Style.Decoration = DecorationTag();
                s.Style.MotionProfile = MotionProfileTag();
                s.Style.LoopEffect = LoopEffectTag();
                s.Style.LoopDurationMs = (int)Math.Round(LoopDurationSlider.Value);
                s.Style.LoopAmplitude = Math.Round(LoopAmplitudeSlider.Value, 2);
                s.Style.Success = (SuccessColorBox.Text ?? "").Trim();
                s.Style.Warning = (WarningColorBox.Text ?? "").Trim();

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
                    NvidiaIntervalCombo.SelectedIndex < SettingsDeviceCatalog.NvidiaOptions.Length)
                    s.Data.NvidiaIntervalMs = SettingsDeviceCatalog.NvidiaOptions[NvidiaIntervalCombo.SelectedIndex];

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

                if (RefreshCombo.SelectedIndex >= 0 && RefreshCombo.SelectedIndex < SettingsDeviceCatalog.RefreshOptions.Length)
                    s.Behavior.RefreshMs = SettingsDeviceCatalog.RefreshOptions[RefreshCombo.SelectedIndex];

                s.Overlay.Topmost = TopmostCheck.IsChecked == true;
                s.Behavior.AutoStart = AutoStartCheck.IsChecked == true;
                s.Behavior.StartMinimized = StartMinCheck.IsChecked == true;
                s.Behavior.TrayEnabled = TrayCheck.IsChecked == true;
                s.Behavior.IntroEnabled = IntroCheck.IsChecked == true;
                s.Behavior.ReduceMotion = ReduceMotionCheck.IsChecked == true;
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
            finally
            {
                _readingUi = false;
            }
        }
    }
}
