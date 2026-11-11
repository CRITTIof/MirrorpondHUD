using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 镜潭一览的设置窗口（5 个页：内容 / 外观 / 数据源 / 行为 / 热键）。
    ///
    /// 本类是 partial，按职责拆在下列文件里（审阅报告 §4.2 / 第三批第 13 项）：
    /// <list type="bullet">
    /// <item><c>SettingsWindow.xaml.cs</c>：本文件 —— 构造、事件挂接、Reload、恢复默认、关闭与释放、克隆。</item>
    /// <item><c>SettingsWindow.Load.cs</c>：LoadUi / ReadUi 两个总入口（顺序与语义是硬契约）。</item>
    /// <item><c>SettingsWindow.Save.cs</c>：ValidateSettings / SaveClick / 运行时字段合并（P1-5、P0-2）。</item>
    /// <item><c>SettingsWindow.Cards.cs</c>：「内容」页卡片列表与呼出热键。</item>
    /// <item><c>SettingsWindow.Appearance.cs</c>：「外观」页字体 / 排版 / CLI / 背景图片 / 数字框。</item>
    /// <item><c>SettingsWindow.Theme.cs</c>：「外观」页主题列表、导入导出（P2-7）、预览与色板。</item>
    /// <item><c>SettingsWindow.Sources.cs</c>：「数据源」页下拉框填充（枚举在 SettingsDeviceCatalog）。</item>
    /// <item><c>SettingsWindow.Behavior.cs</c>：「行为」页的位置 / 清除按键 / 打开目录等事件。</item>
    /// <item><c>SettingsWindow.Hotkeys.cs</c>：「热键」页全局热键录制与卡片动作热键面板。</item>
    /// </list>
    /// </summary>
    public partial class SettingsWindow : Window
    {
        readonly Func<Settings, bool> _onApply;
        readonly Action<double, double, int> _onPosition;
        readonly Action _onClearKeys;
        /// <summary>读「当前实际玻璃实现」；无头截图等没有宿主的场景为 null。</summary>
        readonly Func<GlassRuntimeState> _glassState;

        Settings _working;
        /// <summary>
        /// 打开窗口 / 上次保存成功时的设置快照。保存前用它判断「用户有没有在本窗口改过」运行时字段
        /// （见 <see cref="MergeRuntimeFields"/>）：和基线不同就当用户编辑过，保留用户的值。
        /// </summary>
        Settings _runtimeBaseline;
        /// <summary>控制器当前持有的 Settings 对象；热键切 Chat 模式、热键被占用回退都直接改这一份。</summary>
        Settings _liveSettings;
        /// <summary>点了「恢复默认值」后以界面为准，不再把 live 的运行时字段合并回来。</summary>
        bool _skipRuntimeMerge;
        bool _suppress;
        /// <summary>正在把控件值读回设置树：读取期间不能因为「改外观」把刚套用的主题名清掉。</summary>
        bool _readingUi;
        /// <summary>有未保存编辑；关闭窗口时据此提示保存 / 放弃。</summary>
        bool _dirty;
        /// <summary>上次成功加载 / 保存时的设置快照，用来兜住没有接 UiChanged 的控件。</summary>
        string _cleanSnapshot;
        bool _reallyClose;
        bool _loadFailed;

        /// <summary>
        /// 第 5 个参数读「当前实际玻璃实现」（可为 null：无头截图等没有宿主窗口的场景）。
        /// 参数类型 GlassRuntimeState 是程序集内部类型，所以构造函数保持 internal ——
        /// 设置窗只由宿主（OverlayController）与无头截图（Screenshots）创建，没有跨程序集调用点。
        /// </summary>
        internal SettingsWindow(Settings live, Func<Settings, bool> onApply,
                                Action<double, double, int> onPosition, Action onClearKeys,
                                Func<GlassRuntimeState> glassState = null)
        {
            InitializeComponent();

            _onApply = onApply;
            _onPosition = onPosition;
            _onClearKeys = onClearKeys;
            _glassState = glassState;
            _liveSettings = live;
            try { VersionText.Text = "v" + typeof(SettingsWindow).Assembly.GetName().Version; } catch { }

            HookEvents();
            BindNumberBoxes();
            BuildThemeList();
            LoadUi(live);
        }

        // ==================== 初始化 ====================

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
            FgBox.TextChanged += (s, e) => ValidateColorBox(FgBox, false);
            BgBox.TextChanged += (s, e) => UiChanged();
            BgBox.TextChanged += (s, e) => ValidateColorBox(BgBox, false);
            BgOpacitySlider.ValueChanged += (s, e) => UiChanged();
            AcrylicCheck.Checked += (s, e) => { UiChanged(); UpdateGlassUiState(); UpdateContextUiState(); };
            AcrylicCheck.Unchecked += (s, e) => { UiChanged(); UpdateGlassUiState(); UpdateContextUiState(); };
            GlassModeCombo.SelectionChanged += (s, e) => { UiChanged(); UpdateGlassUiState(); UpdateContextUiState(); };
            GlassSourceCombo.SelectionChanged += (s, e) => { UiChanged(); UpdateGlassUiState(); UpdateContextUiState(); };
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
            IntroCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            IntroCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            TrayCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            TrayCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            GpuCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            GpuCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            DiskCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            DiskCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            NetCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            NetCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            KeysCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            KeysCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            NvidiaCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            NvidiaCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            MonoReadoutsCheck.Checked += (s, e) => UiChanged();
            MonoReadoutsCheck.Unchecked += (s, e) => UiChanged();
            DecorationCombo.SelectionChanged += (s, e) => { UiChanged(); UpdateDecorationUiState(); };
            DecorationDensitySlider.ValueChanged += (s, e) => UiChanged();
            ContourLevelsSlider.ValueChanged += (s, e) => UiChanged();
            ContourEveningSlider.ValueChanged += (s, e) => UiChanged();
            ContourReliefSlider.ValueChanged += (s, e) => UiChanged();
            ContourSmoothSlider.ValueChanged += (s, e) => UiChanged();
            ContourMinLoopSlider.ValueChanged += (s, e) => UiChanged();
            MotionProfileCombo.SelectionChanged += (s, e) => { UiChanged(); UpdateContextUiState(); };
            ReduceMotionCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            ReduceMotionCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            LoopEffectCombo.SelectionChanged += (s, e) => { UiChanged(); UpdateLoopUiState(); };
            LoopDurationSlider.ValueChanged += (s, e) => UiChanged();
            LoopAmplitudeSlider.ValueChanged += (s, e) => UiChanged();
            SuccessColorBox.TextChanged += (s, e) => UiChanged();
            SuccessColorBox.TextChanged += (s, e) => ValidateColorBox(SuccessColorBox, true);
            WarningColorBox.TextChanged += (s, e) => UiChanged();
            WarningColorBox.TextChanged += (s, e) => ValidateColorBox(WarningColorBox, true);

            OutlineCheck.Checked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            OutlineCheck.Unchecked += (s, e) => { UiChanged(); UpdateContextUiState(); };
            OutlineColorBox.TextChanged += (s, e) => UiChanged();
            OutlineColorBox.TextChanged += (s, e) => ValidateColorBox(OutlineColorBox, true);
            OutlineSlider.ValueChanged += (s, e) => UiChanged();
            ImagePathBox.TextChanged += (s, e) => { UiChanged(); UpdateContextUiState(); };
            ImageOpacitySlider.ValueChanged += (s, e) => UiChanged();
            StretchCombo.SelectionChanged += (s, e) => UiChanged();
            FixedWidthSlider.ValueChanged += (s, e) => UiChanged();
            CursorStyleCombo.SelectionChanged += (s, e) => UiChanged();
            CursorBlinkCheck.Checked += (s, e) => UiChanged();
            CursorBlinkCheck.Unchecked += (s, e) => UiChanged();
        }

        public bool Reload(Settings live)
        {
            if (!LoadUi(live)) return false;

            _liveSettings = live;
            _skipRuntimeMerge = false;
            return true;
        }

        // ==================== 状态 ====================

        void UiChanged()
        {
            if (_suppress) return;
            MarkDirty();
            if (!_applyingTheme) ClearThemeName();
            UpdateSwatches();
            RenderThemePreview();
        }

        /// <summary>用户改了任一设置：标题加 *，关闭时提示保存 / 放弃。</summary>
        void MarkDirty()
        {
            if (_suppress || _readingUi) return;
            _dirty = true;
            if (!Title.EndsWith(" *", StringComparison.Ordinal)) Title = Title + " *";
        }

        void ClearDirty()
        {
            _dirty = false;
            if (Title.EndsWith(" *", StringComparison.Ordinal))
                Title = Title.Substring(0, Title.Length - 2);
        }

        /// <summary>保存一份“干净”快照；没有显式 UiChanged 的控件也靠它判断是否改过。</summary>
        void CaptureCleanSnapshot()
        {
            try { _cleanSnapshot = _working == null ? null : Json.Serialize(_working); }
            catch (Exception ex)
            {
                _cleanSnapshot = null;
                Log.Warn("设置脏状态快照失败: " + ex.Message);
            }
        }

        /// <summary>关闭前判断是否有未保存编辑。先看显式脏标记，再看控件读回与干净快照。</summary>
        bool IsDirty()
        {
            if (_dirty) return true;
            if (_working == null || _cleanSnapshot == null) return false;

            try
            {
                Settings current = ReadUi();
                return !string.Equals(Json.Serialize(current), _cleanSnapshot, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Log.Warn("检查未保存修改失败，按有修改处理: " + ex.Message);
                return true;
            }
        }

        /// <summary>颜色框就地校验：非法值标红并给 ToolTip，不必等到保存才在底部报错。</summary>
        void ValidateColorBox(TextBox box, bool allowEmpty)
        {
            if (box == null) return;

            string text = (box.Text ?? "").Trim();
            bool ok = allowEmpty
                ? text.Length == 0 || Settings.IsColorToken(text)
                : Settings.IsHexColor(text);

            if (ok)
            {
                box.ClearValue(Control.BorderBrushProperty);
                box.ToolTip = null;
                return;
            }

            box.BorderBrush = FindResource("Danger") as Brush;
            box.ToolTip = allowEmpty
                ? "留空，或填 #RRGGBB / #AARRGGBB"
                : "需要 7 位 #RRGGBB（透明度请用背景不透明度）";
        }

        /// <summary>热键框就地校验；空串表示不启用，属于合法值。</summary>
        void ValidateHotkeyBox(TextBox box)
        {
            if (box == null) return;

            uint mods, vk;
            string text = (box.Text ?? "").Trim();
            bool ok = text.Length == 0 || HotkeyManager.TryParse(text, out mods, out vk);

            if (ok)
            {
                box.ClearValue(Control.BorderBrushProperty);
                box.ToolTip = null;
                return;
            }

            box.BorderBrush = FindResource("Danger") as Brush;
            box.ToolTip = "无法识别的组合键；支持 Ctrl / Alt / Shift / Win + 键";
        }

        // ==================== 恢复默认 / 关闭 ====================

        void ResetClick(object sender, RoutedEventArgs e)
        {
            var fresh = new Settings();
            fresh.Normalize();
            CardModel.Normalize(fresh);
            LoadUi(fresh);
            // 「恢复默认值」是用户的显式意图（含热键与 Chat 模式），保存时不要再被 live 的运行时字段盖回去。
            _skipRuntimeMerge = true;
            MarkDirty();
            StatusText.Text = "已恢复默认值，点击“保存并应用”生效";
        }

        void CloseClick(object sender, RoutedEventArgs e)
        {
            // 走 OnClosing 的“未保存”提示；不能直接 Hide，否则修改会被静默丢弃。
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClose)
            {
                if (IsDirty())
                {
                    MessageBoxResult answer = MessageBox.Show(this,
                        "设置还有未保存的修改。\n\n是 = 保存并应用后关闭；否 = 放弃修改；取消 = 继续编辑。",
                        "镜潭一览 · 未保存的修改",
                        MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (answer == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if (answer == MessageBoxResult.Yes && !TrySaveSettings())
                    {
                        e.Cancel = true;
                        return;
                    }

                    if (answer == MessageBoxResult.No) ClearDirty();
                }

                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        /// <summary>
        /// 窗口真正关闭时断开设置页对卡片模块的引用。
        /// WPF 的绑定引擎在窗口关闭后还可能短暂保留 ItemsControl/ListCollectionView；
        /// 只 Close 不摘 ItemsSource 会让 CardEditor 继续持有插件模块 / 内容。
        /// </summary>
        public void ReleaseCardEditors()
        {
            foreach (CardEditor editor in _editors)
            {
                try { editor.ReleaseModule(); } catch { }
            }
            try { CardList.ItemsSource = null; } catch { }
            _editors.Clear();
        }

        public void CloseForReal()
        {
            _reallyClose = true;
            ReleaseCardEditors();
            Close();
        }

        /// <summary>加载当前设置是否失败（失败时窗口不打开、保存被禁用）。</summary>
        public bool LoadFailed { get { return _loadFailed; } }

        // ==================== 小工具 ====================

        /// <summary>克隆当前设置；失败返回 false（clone 为 null）。调用方必须拒绝继续，不能拿默认值顶上。</summary>
        internal static bool TryClone(Settings source, out Settings clone, out string error)
        {
            clone = null;
            error = null;
            try
            {
                clone = Json.Deserialize<Settings>(Json.Serialize(source));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("设置副本克隆失败", ex);
            }
            return clone != null;
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
    }
}
