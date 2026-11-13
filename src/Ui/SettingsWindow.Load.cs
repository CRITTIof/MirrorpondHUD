using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

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
            if (!TryClone(s, out var working, out var cloneError))
            {
                // 绝不能用 new Settings() 顶上：那会让用户看到全默认值，一点保存就把默认值写盘。
                _loadFailed = true;
                StatusText.Text = $"读取当前设置失败：{cloneError}；已禁止保存，避免覆盖 settings.json";
                return false;
            }

            _loadFailed = false;
            _working = working;
            CardModel.Normalize(_working);
            // 2.45.3：设置树里的「减少动效」一票否决设置窗动效（与卡片动效同源，分组 / 切页都不播）。
            SettingsMotion.SyncReduceMotion(_working.Behavior is not null && _working.Behavior.ReduceMotion);
            UpdateSettingsTheme(_working);
            CaptureRuntimeBaseline();

            ThemeCombo.SelectedIndex = ThemeIndexOf(_working.Style.ThemeName);
            ThemeNameText.Text = string.IsNullOrWhiteSpace(_working.Style.ThemeName)
                ? "自定义" : _working.Style.ThemeName;
            _applyingSettingsAppearance = true;
            try
            {
                SettingsAppearanceCombo.SelectedIndex =
                    SettingsAppearanceIndexOf(_working.Behavior.SettingsAppearanceId);
            }
            finally { _applyingSettingsAppearance = false; }
            _applyingAppearance = true;
            try
            {
                int appearanceIndex = AppearanceIndexOf(_working.Style.AppearanceId);
                AppearanceCombo.SelectedIndex = appearanceIndex;
                // 离屏截图/首次布局时只改 SelectedIndex 偶尔不会刷新选中项文字；
                // 显式选回同一个 item，保证界面与持久化 id 一致。
                if (appearanceIndex >= 0 && appearanceIndex < AppearanceCombo.Items.Count)
                    AppearanceCombo.SelectedItem = AppearanceCombo.Items[appearanceIndex];
            }
            finally { _applyingAppearance = false; }
            ThemePreviewBgBox.Text = _working.Behavior.SettingsPreviewBg;

            // S-18：字体列表改成后台预热 + 缓存，UI 只负责装视图与选中项（不再在 UI 线程全量枚举）。
            LoadFontsIntoUi(s.Style.FontFamily);

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
            CornerStyleCombo.SelectedIndex = string.Equals(s.Style.CornerStyle, "Cut", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            EdgeBarCheck.IsChecked = s.Style.EdgeBar;
            EdgeBarWidthSlider.Value = s.Style.EdgeBarWidth;
            EdgeBarWidthValue.Text = s.Style.EdgeBarWidth.ToString("0", CultureInfo.InvariantCulture);
            EdgeBarColorBox.Text = s.Style.EdgeBarColor ?? "";
            ShadowCheck.IsChecked = s.Overlay.Shadow;
            MonoReadoutsCheck.IsChecked = s.Style.MonoReadouts;
            LoadEndfieldUi(s.Style);
            DecorationCombo.SelectedIndex = DecorationIndex(s.Style.Decoration);
            DecorationDensitySlider.Value = s.Style.DecorationDensity;
            ContourLevelsSlider.Value = s.Style.ContourLevels;
            ContourEveningSlider.Value = s.Style.ContourEvening;
            ContourReliefSlider.Value = s.Style.ContourRelief;
            ContourSmoothSlider.Value = s.Style.ContourSmooth;
            ContourMinLoopSlider.Value = s.Style.ContourMinLoopArea;
            MotionProfileCombo.SelectedIndex = MotionProfileIndex(s.Style.MotionProfile);
            TextEffectCombo.SelectedIndex = TextEffectIndex(s.Style.TextEffect);
            TextParticleSizeSlider.Value = s.Style.TextParticleSize;
            TextParticleCountSlider.Value = s.Style.TextParticleCount;
            TextParticleSpeedSlider.Value = s.Style.TextParticleSpeed;
            TextParticleNearestCheck.IsChecked = s.Style.TextParticleNearest;
            TextParticleGeometryCheck.IsChecked = s.Style.TextParticleGeometry;
            LoopEffectCombo.SelectedIndex = LoopEffectIndex(s.Style.LoopEffect);
            ContourSpeedSlider.Value = s.Style.ContourSpeed;
            LoopDurationSlider.Value = s.Style.LoopDurationMs;
            LoopAmplitudeSlider.Value = s.Style.LoopAmplitude;
            PulseThicknessSlider.Value = s.Style.LoopPulseThickness;
            ScanlineThicknessSlider.Value = s.Style.LoopScanlineThickness;
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

            int deviceGpuIndex = s.Data.GpuIndex;
            string deviceAdapter = s.Data.NetworkAdapter;
            SettingsDeviceCatalog.BeginDeviceEnumeration();
            FillGpuCombo(deviceGpuIndex);
            FillRefreshCombo(s.Behavior.RefreshMs);
            FillNvidiaCombo(s.Data.NvidiaIntervalMs);
            FillAdapterCombo(deviceAdapter);
            FillDrives(s.Data.DiskDrives);
            _deviceGpuIndex = deviceGpuIndex;
            _deviceAdapter = deviceAdapter;
            _devicesUnreliable = SettingsDeviceCatalog.DeviceEnumerationFailed;
            if (_devicesUnreliable)
                StatusText.Text = "设备列表暂不可用：本次保存会保留原来的 GPU / 网卡选择。";
            FillMonitors(s.Overlay.Monitor);

            DisplayModeCombo.SelectedIndex = s.Overlay.Topmost ? 1 : 0;
            AutoStartCheck.IsChecked = s.Behavior.AutoStart;
            StartMinCheck.IsChecked = s.Behavior.StartMinimized;
            TrayCheck.IsChecked = s.Behavior.TrayEnabled;
            LogLevelCombo.SelectedIndex = LogLevelIndex(s.Behavior.LogLevel);
            IntroCheck.IsChecked = s.Behavior.IntroEnabled;
            ReduceMotionCheck.IsChecked = s.Behavior.ReduceMotion;
            OverallMotionCheck.IsChecked = s.Behavior.OverallMotion;
            CardSlideCombo.SelectedIndex = CardSlideIndex(s.Behavior.CardSlide);
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
            ValidateColorBox(ThemePreviewBgBox, false);
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

            // P-9：插件加载失败必须有一个用户能看到的入口，而不是只在日志里。
            CardPluginLoadReport pluginReport = CardPluginLoader.LastLoadReport;
            if (pluginReport is not null && pluginReport.HasErrors)
            {
                PluginErrorBanner.Visibility = System.Windows.Visibility.Visible;
                string preview = "";
                int shown = 0;
                foreach (string error in pluginReport.Errors)
                {
                    if (shown >= 3) break;
                    preview += (shown == 0 ? "" : "；") + error;
                    shown++;
                }
                PluginErrorText.Text = $"有 {pluginReport.Errors.Count} 个卡片插件加载失败：{preview}（详见日志）";
            }
            else
            {
                PluginErrorBanner.Visibility = System.Windows.Visibility.Collapsed;
            }

            // 只读加载（更高版本 / 本次读取失败）时，整个会话内的保存都会被 ConfigStore 拒绝。
            // 必须在进入界面时就说明并禁用保存，否则用户会改完一整轮才发现全丢。
            if (ConfigStore.LastLoadReadOnly)
            {
                if (SaveButton is not null) SaveButton.IsEnabled = false;
                StatusText.Text = "配置为只读加载：本次会话内的保存都会被拒绝。请先处理设置文件，然后重启应用。";
            }
            else if (SaveButton is not null)
            {
                SaveButton.IsEnabled = true;
            }
        }
        finally
        {
            _suppress = false;
        }

        UpdateSwatches();
        UpdateDecorationUiState();
        UpdateLoopUiState();
        UpdateContextUiState();
        RebuildCardList();
        RenderThemePreview();
        ClearDirty();
        CaptureCleanSnapshot();
        return true;
    }

    // S-05：枚举失败时用于回写的原值（LoadUi 记录、ReadUi 使用）。
    int _deviceGpuIndex;
    string _deviceAdapter;
    bool _devicesUnreliable;

    // ==================== 下拉框取值辅助（LoadUi / ReadUi 使用） ====================

    static int AlignmentIndex(string value)
    {
        string mode = Settings.NormalizeTextAlign(value);
        if (mode == "Center") return 1;
        if (mode == "Right") return 2;
        if (mode == "Card") return 3;
        return 0;
    }

    static int LogLevelIndex(string value)
    {
        switch (Settings.NormalizeLogLevel(value))
        {
            case "Debug": return 0;
            case "Warn": return 2;
            case "Error": return 3;
            default: return 1;
        }
    }

    string LogLevelTag()
    {
        var item = LogLevelCombo is null ? null : LogLevelCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeLogLevel(item is null ? null : item.Tag as string);
    }

    string AlignmentTag()
    {
        var item = AlignCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeTextAlign(item is null ? "Left" : item.Tag as string);
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
        var item = DecorationCombo is null ? null : DecorationCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeDecoration(item is null ? null : item.Tag as string);
    }

    static int MotionProfileIndex(string value)
    {
        switch (Settings.NormalizeMotionProfile(value))
        {
            case "Industrial": return 1;
            case "Cut": return 2;
            case "Off": return 3;
            default: return 0;
        }
    }

    string MotionProfileTag()
    {
        var item = MotionProfileCombo is null ? null : MotionProfileCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeMotionProfile(item is null ? null : item.Tag as string);
    }

    static int LoopEffectIndex(string value)
    {
        switch (Settings.NormalizeLoopEffect(value, false))
        {
            case "Breath": return 1;
            case "Scanline": return 2;
            case "Pulse": return 3;
            case "Contour": return 4;
            default: return 0;
        }
    }

    static int TextEffectIndex(string value) =>
        string.Equals(Settings.NormalizeTextEffect(value), "Particle", StringComparison.Ordinal) ? 1 : 0;

    string TextEffectTag()
    {
        var item = TextEffectCombo is null ? null : TextEffectCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeTextEffect(item is null ? null : item.Tag as string);
    }

    bool ParticleTextSelected() =>
        string.Equals(TextEffectTag(), "Particle", StringComparison.OrdinalIgnoreCase);

    string LoopEffectTag()
    {
        var item = LoopEffectCombo is null ? null : LoopEffectCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeLoopEffect(item is null ? null : item.Tag as string, false);
    }

    /// <summary>只有选择“等高线”才展示五项等高线参数；否则展示纹样密度。</summary>
    void UpdateDecorationUiState()
    {
        if (DecorationDensityRow is null || ContourOptionsPanel is null) return;

        bool contour = string.Equals(DecorationTag(), "Contour", StringComparison.OrdinalIgnoreCase);
        DecorationDensityRow.Visibility = contour ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        ContourOptionsPanel.Visibility = contour ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    /// <summary>
    /// 外观 / 行为 / 数据源里所有“子设置只在父选项打开时才有意义”的显隐。
    /// 统一放在一处，避免每个事件各写一套、漏掉某个入口（LoadUi / 主题应用 / 手动切换）。
    ///
    /// 注意：这里只控制显示与可用性，不改值。隐藏项仍会被 ReadUi 读回原值，
    /// 所以关掉父选项再打开，用户之前的子设置不会丢。
    /// </summary>
    void UpdateContextUiState()
    {
        if (AcrylicCheck is null) return;

        bool acrylic = AcrylicCheck.IsChecked == true;
        if (GlassOptionsPanel is not null)
            GlassOptionsPanel.Visibility = acrylic ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // 系统亚克力的圆角由 DWM 底板决定；此时圆角滑块灰掉，避免用户以为改了会生效。
        bool systemAcrylic = acrylic && string.Equals(GlassModeTag(), "System", StringComparison.OrdinalIgnoreCase);
        if (CornerRow is not null)
        {
            CornerRow.IsEnabled = !systemAcrylic;
            CornerRow.Opacity = systemAcrylic ? 0.55 : 1.0;
        }
        if (CornerHint is not null)
            CornerHint.Visibility = systemAcrylic ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        if (OutlineOptionsRow is not null)
            OutlineOptionsRow.Visibility = OutlineCheck is not null && OutlineCheck.IsChecked == true
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        if (ImageOptionsRow is not null)
            ImageOptionsRow.Visibility = !string.IsNullOrWhiteSpace(ImagePathBox is null ? null : ImagePathBox.Text)
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // 粒子聚字与启动打字动画互斥：选了粒子就把打字动画入口整段收起，
        // 不留下“勾着却不生效”的控件；切回“无”后原值和入口都回来。
        bool reduceMotionNow = ReduceMotionCheck is not null && ReduceMotionCheck.IsChecked == true;
        bool motionOffNow = string.Equals(MotionProfileTag(), "Off", StringComparison.OrdinalIgnoreCase);
        bool particleText = ParticleTextSelected() && !reduceMotionNow && !motionOffNow;
        if (IntroCheck is not null)
            IntroCheck.Visibility = particleText
                ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        if (IntroOptionsPanel is not null)
            IntroOptionsPanel.Visibility = !particleText && IntroCheck is not null && IntroCheck.IsChecked == true
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // 托盘关闭时“启动后最小化到托盘”无处可去，直接禁用；值保留，重新开托盘即可复用。
        if (StartMinCheck is not null)
            StartMinCheck.IsEnabled = TrayCheck is not null && TrayCheck.IsChecked == true;

        if (GpuOptionsPanel is not null)
            GpuOptionsPanel.Visibility = GpuCheck is not null && GpuCheck.IsChecked == true
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (DrivesOptionsPanel is not null)
            DrivesOptionsPanel.Visibility = DiskCheck is not null && DiskCheck.IsChecked == true
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (NetworkOptionsPanel is not null)
            NetworkOptionsPanel.Visibility = NetCheck is not null && NetCheck.IsChecked == true
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (KeyboardOptionsPanel is not null)
            KeyboardOptionsPanel.Visibility = KeysCheck is not null && KeysCheck.IsChecked == true
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (NvidiaOptionsPanel is not null)
            NvidiaOptionsPanel.Visibility = NvidiaCheck is not null && NvidiaCheck.IsChecked == true
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // 减少动效 / 动效档位关闭时，循环效果不会运行：灰掉而不是隐藏，
        // 让用户看得到自己之前选的效果，关掉覆盖后还能原样恢复。
        bool reduceMotion = ReduceMotionCheck is not null && ReduceMotionCheck.IsChecked == true;
        bool motionOff = string.Equals(MotionProfileTag(), "Off", StringComparison.OrdinalIgnoreCase);
        bool loopUsable = !reduceMotion && !motionOff;
        if (MotionProfileCombo is not null) MotionProfileCombo.IsEnabled = !reduceMotion;
        if (LoopEffectCombo is not null) LoopEffectCombo.IsEnabled = loopUsable;
        if (LoopDurationRow is not null) LoopDurationRow.IsEnabled = loopUsable;
        if (LoopAmplitudeRow is not null) LoopAmplitudeRow.IsEnabled = loopUsable;
        if (ContourSpeedRow is not null) ContourSpeedRow.IsEnabled = loopUsable;

        bool motionUsable = !reduceMotion && !motionOff;
        if (OverallMotionCheck is not null) OverallMotionCheck.IsEnabled = motionUsable;
        if (CardSlideCombo is not null) CardSlideCombo.IsEnabled = motionUsable;
        if (TextEffectCombo is not null) TextEffectCombo.IsEnabled = motionUsable;
        UpdateTextEffectUiState();
        UpdateEndfieldUiState();
    }

    void UpdateTextEffectUiState()
    {
        if (TextEffectHint is null) return;
        bool particle = ParticleTextSelected();
        TextEffectHint.Visibility = particle
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        if (TextParticlePanel is not null)
        {
            TextParticlePanel.Visibility = particle
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            TextParticlePanel.IsEnabled = TextEffectCombo is null || TextEffectCombo.IsEnabled;
        }
    }

    /// <summary>
    /// 循环时长用于 Breath / Scanline / Pulse；呼吸幅度只在 Breath 出现；
    /// Contour 是原地形变，用“变化速度”而不是时长/幅度。
    /// </summary>
    void UpdateLoopUiState()
    {
        if (LoopDurationRow is null || LoopAmplitudeRow is null || ContourSpeedRow is null) return;

        string effect = LoopEffectTag();
        bool timed = effect == "Breath" || effect == "Scanline" || effect == "Pulse";
        LoopDurationRow.Visibility = timed ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        LoopAmplitudeRow.Visibility = effect == "Breath" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PulseThicknessRow.Visibility = effect == "Pulse" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ScanlineThicknessRow.Visibility = effect == "Scanline" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ContourSpeedRow.Visibility = effect == "Contour" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
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
        return item is null ? "UniformToFill" : Settings.NormalizeStretch(item.Tag as string);
    }

    string GlassModeTag()
    {
        var item = GlassModeCombo is null ? null : GlassModeCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeGlassMode(item is null ? null : item.Tag as string);
    }

    string GlassSourceTag()
    {
        var item = GlassSourceCombo is null ? null : GlassSourceCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeGlassSource(item is null ? null : item.Tag as string);
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

    Settings ReadUi() => ReadUiCore(_working, commitPending: true);

    /// <summary>
    /// S-11：把控件读进指定设置对象。判脏传副本且 commitPending=false，
    /// 这样“只是检查有没有改动”不会加密 API Key、也不会原地 Normalize 活配置。
    /// </summary>
    Settings ReadUiCore(Settings s, bool commitPending)
    {
        _readingUi = true;
        try
        {
            if (s is null) return null;

            // 输入框里可能还有没失焦的 API Key（直接点了「确定」）：先加密收下来，别丢。
            if (commitPending)
            {
                foreach (CardEditor editor in _editors) editor.CommitPendingEdits();
            }

            ApplyCliBoxesTo(s, writeBack: ReferenceEquals(s, _working));   // 输入框可能还没失焦（直接点了「确定」）

            FontItem font = FontCombo.SelectedItem as FontItem ?? _selectedFont;
            if (font is not null && !string.IsNullOrWhiteSpace(font.Family)) s.Style.FontFamily = font.Family;
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
            s.Style.CornerStyle = (CornerStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Round";
            s.Style.EdgeBar = EdgeBarCheck.IsChecked == true;
            s.Style.EdgeBarWidth = Math.Round(EdgeBarWidthSlider.Value);
            s.Style.EdgeBarColor = (EdgeBarColorBox.Text ?? "").Trim();
            s.Overlay.Shadow = ShadowCheck.IsChecked == true;
            s.Style.MonoReadouts = MonoReadoutsCheck.IsChecked == true;
            ReadEndfieldUi(s.Style);
            s.Style.Decoration = DecorationTag();
            s.Style.DecorationDensity = Math.Round(DecorationDensitySlider.Value, 2);
            s.Style.ContourLevels = (int)Math.Round(ContourLevelsSlider.Value);
            s.Style.ContourEvening = Math.Round(ContourEveningSlider.Value, 2);
            s.Style.ContourRelief = Math.Round(ContourReliefSlider.Value, 2);
            s.Style.ContourSmooth = (int)Math.Round(ContourSmoothSlider.Value);
            s.Style.ContourMinLoopArea = Math.Round(ContourMinLoopSlider.Value);
            s.Style.ContourSpeed = Math.Round(ContourSpeedSlider.Value, 2);
            s.Style.MotionProfile = MotionProfileTag();
            s.Style.TextEffect = TextEffectTag();
            s.Style.TextParticleSize = Math.Round(TextParticleSizeSlider.Value, 2);
            s.Style.TextParticleCount = (int)Math.Round(TextParticleCountSlider.Value);
            s.Style.TextParticleSpeed = Math.Round(TextParticleSpeedSlider.Value, 2);
            s.Style.TextParticleNearest = TextParticleNearestCheck.IsChecked == true;
            s.Style.TextParticleGeometry = TextParticleGeometryCheck.IsChecked == true;
            s.Style.LoopEffect = LoopEffectTag();
            s.Style.LoopDurationMs = (int)Math.Round(LoopDurationSlider.Value);
            s.Style.LoopAmplitude = Math.Round(LoopAmplitudeSlider.Value, 2);
            s.Style.LoopPulseThickness = (int)Math.Round(PulseThicknessSlider.Value);
            s.Style.LoopScanlineThickness = (int)Math.Round(ScanlineThicknessSlider.Value);
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
            s.Data.GpuIndex = _devicesUnreliable
                ? _deviceGpuIndex
                : Math.Max(0, GpuCombo.SelectedIndex) - 1;

            if (NvidiaIntervalCombo.SelectedIndex >= 0 &&
                NvidiaIntervalCombo.SelectedIndex < SettingsDeviceCatalog.NvidiaOptions.Length)
                s.Data.NvidiaIntervalMs = SettingsDeviceCatalog.NvidiaOptions[NvidiaIntervalCombo.SelectedIndex];

            s.Data.NetworkAdapter = _devicesUnreliable
                ? _deviceAdapter
                : (AdapterCombo.SelectedIndex <= 0
                    ? ""
                    : (AdapterCombo.SelectedItem as string ?? ""));

            var selectedDrives = new List<string>();
            var allDrives = new List<string>();
            foreach (object child in DrivesPanel.Children)
            {
                var box = child as CheckBox;
                if (box is null) continue;
                allDrives.Add((string)box.Tag);
                if (box.IsChecked == true) selectedDrives.Add((string)box.Tag);
            }
            s.Data.DiskDrives = selectedDrives.Count == allDrives.Count ? new List<string>() : selectedDrives;

            if (RefreshCombo.SelectedIndex >= 0 && RefreshCombo.SelectedIndex < SettingsDeviceCatalog.RefreshOptions.Length)
                s.Behavior.RefreshMs = SettingsDeviceCatalog.RefreshOptions[RefreshCombo.SelectedIndex];

            s.Overlay.Topmost = DisplayModeCombo.SelectedIndex == 1;
            s.Behavior.AutoStart = AutoStartCheck.IsChecked == true;
            s.Behavior.StartMinimized = StartMinCheck.IsChecked == true;
            s.Behavior.TrayEnabled = TrayCheck.IsChecked == true;
            s.Behavior.LogLevel = LogLevelTag();
            s.Behavior.IntroEnabled = IntroCheck.IsChecked == true;
            s.Behavior.ReduceMotion = ReduceMotionCheck.IsChecked == true;
            s.Behavior.OverallMotion = OverallMotionCheck.IsChecked == true;
            s.Behavior.CardSlide = CardSlideTag();
            s.Behavior.SettingsTheme = _settingsDark ? "Dark" : "Light";
            s.Behavior.SettingsPreviewBg = _previewBg;
            s.Behavior.WelcomeText = (WelcomeBox.Text ?? "").Trim();
            if (s.Behavior.WelcomeText.Length == 0) s.Behavior.WelcomeText = "Welcome Back";
            s.Behavior.TypeSpeedMs = Math.Max(8, ParseInt(TypeSpeedBox.Text, s.Behavior.TypeSpeedMs));
            s.Behavior.CursorStyle = CursorTag();
            s.Behavior.CursorBlink = CursorBlinkCheck.IsChecked == true;

            int monitor = Math.Max(0, MonitorCombo.SelectedIndex);
            s.Overlay.Monitor = monitor;
            // G-14：下拉框选的是当前枚举序号，落盘时同时记下设备名作为持久身份。
            s.Overlay.MonitorDevice = ScreenGeometry.DeviceNameOf(monitor);
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
