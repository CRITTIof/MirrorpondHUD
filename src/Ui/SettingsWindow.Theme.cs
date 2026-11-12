using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗「外观」页的主题区：主题列表、套用、导入 / 导出、还原默认、缩略预览与色板取色。
/// 线程模型（P2-7）：导入时文件读取 + JSON 解析在后台线程完成（TryImport 内有 1 MB 硬上限），
/// 回到 UI 线程后先校验设置副本没被换过，再套用；过期结果只提示不写界面。主题预览走
/// OverlaySurface 离屏渲染，失败只记 Warn，不挡设置窗。
/// </summary>
public partial class SettingsWindow
{
    readonly List<ThemePreset> _themes = new();
    bool _applyingTheme;
    bool _importingTheme;

    // ==================== 主题 ====================

    void BuildThemeList()
    {
        _themes.Clear();
        _themes.AddRange(ThemeCatalog.Load());

        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add("自定义（当前外观）");
        foreach (ThemePreset preset in _themes) ThemeCombo.Items.Add(preset.Name);
        if (ThemeCombo.SelectedIndex < 0) ThemeCombo.SelectedIndex = 0;
    }

    int ThemeIndexOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        for (int i = 0; i < _themes.Count; i++)
            if (string.Equals(_themes[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    /// <summary>手动改外观后主题名失效（显示「自定义」），避免显示成某个主题却已经不一致。</summary>
    void ClearThemeName()
    {
        // ReadUi 会调 ApplyCliBoxes → UiChanged；保存不是“用户手动改外观”，
        // 这里清掉 ThemeName 会让刚套用的主题在每次保存时丢失（L-1）。
        if (_readingUi || _applyingTheme) return;
        if (_working is null || string.IsNullOrEmpty(_working.Style.ThemeName)) return;

        _working.Style.ThemeName = "";
        ThemeNameText.Text = "自定义";
        if (ThemeCombo.SelectedIndex != 0) ThemeCombo.SelectedIndex = 0;
    }

    void ThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _applyingTheme || _working is null) return;

        int index = ThemeCombo.SelectedIndex;
        if (index <= 0 || index - 1 >= _themes.Count) return;

        ThemePreset preset = _themes[index - 1];
        ApplyThemeDocument(preset.Document, preset.Name);
    }

    void ApplyThemeDocument(ThemeDocument document, string name)
    {
        if (document is null || _working is null) return;

        _applyingTheme = true;
        try
        {
            if (!TryClone(_working, out var next, out var error))
            {
                ThemeStatusText.Text = $"套用主题失败：{error}";
                return;
            }

            document.ApplyTo(next);
            next.Style.ThemeName = name ?? "";
            next.Normalize();
            CardModel.Normalize(next);
            LoadUi(next);
            ThemeStatusText.Text = $"已套用「{(string.IsNullOrEmpty(name) ? next.Style.ThemeName : name)}」，点“保存并应用”生效";
            MarkDirty();
        }
        finally
        {
            _applyingTheme = false;
        }
    }

    async void ImportThemeClick(object sender, RoutedEventArgs e)
    {
        if (_working is null || _importingTheme) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入主题",
            Filter = "主题文件 (*.mptheme;*.json)|*.mptheme;*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        // P2-7：文件读取 + JSON 解析放到后台线程（TryImport 内部还有 1 MB 硬上限，不会读无限流），
        // 慢盘 / 命名管道不再卡住 UI 线程；await 之后回到 UI 线程再套用。
        Settings source = _working;
        string path = dialog.FileName;

        if (!TryClone(source, out var snapshot, out var cloneError))
        {
            ThemeStatusText.Text = $"导入失败：复制当前设置失败：{cloneError}";
            return;
        }

        _importingTheme = true;
        ThemeStatusText.Text = "正在导入主题…";
        try
        {
            ThemeImportOutcome outcome = await Task.Run(() =>
            {
                bool ok = ThemeFile.TryImport(path, snapshot, out var imported, out var importError);
                return new ThemeImportOutcome { Ok = ok, Applied = imported, Error = importError };
            });

            if (!outcome.Ok)
            {
                ThemeStatusText.Text = $"导入失败：{outcome.Error}";
                return;
            }

            // 后台读取期间窗口可能被重新加载/关闭，过期结果不要再写回界面。
            if (!ReferenceEquals(_working, source) || _loadFailed)
            {
                ThemeStatusText.Text = "导入已取消：设置已变化，请重新导入";
                return;
            }

            Settings applied = outcome.Applied;
            applied.Style.ThemeName = Path.GetFileNameWithoutExtension(path);

            _applyingTheme = true;
            try
            {
                CardModel.Normalize(applied);
                LoadUi(applied);
                ThemeStatusText.Text = $"已导入「{applied.Style.ThemeName}」，点“保存并应用”生效";
            }
            finally
            {
                _applyingTheme = false;
            }
        }
        catch (Exception ex)
        {
            ThemeStatusText.Text = $"导入失败：{ex.Message}";
            Log.Error("导入主题异常", ex);
        }
        finally
        {
            _importingTheme = false;
        }
    }

    void ExportThemeClick(object sender, RoutedEventArgs e)
    {
        if (_working is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出当前外观",
            Filter = "主题文件 (*.mptheme)|*.mptheme",
            DefaultExt = ".mptheme",
            FileName = string.IsNullOrWhiteSpace(_working.Style.ThemeName)
                ? "mirrorpond" : _working.Style.ThemeName
        };
        if (dialog.ShowDialog(this) != true) return;

        Settings current;
        try
        {
            current = ReadUi();   // 把还没失焦的输入收进来
        }
        catch (Exception ex)
        {
            ThemeStatusText.Text = $"导出失败：{ex.Message}";
            return;
        }

        if (!ThemeFile.TryExport(current, dialog.FileName, out var error))
        {
            ThemeStatusText.Text = $"导出失败：{error}";
            return;
        }

        ThemeStatusText.Text = $"已导出到 {dialog.FileName}";
    }

    void ResetThemeClick(object sender, RoutedEventArgs e)
    {
        if (_working is null) return;

        _applyingTheme = true;
        try
        {
            if (!TryClone(_working, out var next, out var error))
            {
                ThemeStatusText.Text = $"还原失败：{error}";
                return;
            }

            var defaults = new Settings();
            next.Style = defaults.Style;
            next.Overlay.CornerRadius = defaults.Overlay.CornerRadius;
            next.Overlay.Shadow = defaults.Overlay.Shadow;
            next.Cli = defaults.Cli;
            next.Normalize();
            CardModel.Normalize(next);
            LoadUi(next);
            ThemeStatusText.Text = "已还原默认外观，点“保存并应用”生效";
        }
        finally
        {
            _applyingTheme = false;
        }
    }

    // ==================== 预览 ====================

    /// <summary>把外观控件的当前值抄进 _working，预览才能跟手（不触发卡片校验 / 加密等副作用）。</summary>
    void SyncPreviewStyle()
    {
        if (_working is null) return;

        StyleSettings style = _working.Style;
        FontItem font = FontCombo.SelectedItem as FontItem ?? _selectedFont;
        if (font is not null && !string.IsNullOrWhiteSpace(font.Family)) style.FontFamily = font.Family;

        style.FontSize = Math.Round(FontSizeSlider.Value);
        style.IconScale = Math.Round(IconScaleSlider.Value, 2);
        style.Bold = BoldCheck.IsChecked == true;
        style.Foreground = (FgBox.Text ?? "").Trim();
        style.Background = (BgBox.Text ?? "").Trim();
        style.BackgroundOpacity = BgOpacitySlider.Value;
        style.TextAlignment = AlignmentTag();
        style.Padding = Math.Round(PaddingSlider.Value);
        style.LineHeight = Math.Round(LineHeightSlider.Value);
        style.TextOutline = OutlineCheck.IsChecked == true;
        style.OutlineColor = (OutlineColorBox.Text ?? "").Trim();
        style.OutlineThickness = Math.Round(OutlineSlider.Value, 2);

        _working.Overlay.CornerRadius = Math.Round(CornerSlider.Value);
        _working.Overlay.Shadow = ShadowCheck.IsChecked == true;
    }

    /// <summary>
    /// S-10：主题预览防抖。拖动滑块会连续触发 UiChanged，过去每一次都整块离屏渲染
    /// （OverlaySurface + 2× RenderTargetBitmap），保存/关闭读回控件时也会白渲染一次。
    /// 这里合并到一次 Background 优先级的渲染；读取控件期间（_readingUi）直接跳过。
    /// </summary>
    void RequestThemePreview()
    {
        if (_suppress || _readingUi || _working is null) return;

        if (_previewTimer is null)
        {
            _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(160)
            };
            _previewTimer.Tick += (s, e) =>
            {
                _previewTimer.Stop();
                RenderThemePreview();
            };
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    /// <summary>用 OverlaySurface 离屏渲染一张缩略卡：颜色、字体、圆角、投影、内边距都跟着主题走。</summary>
    void RenderThemePreview()
    {
        if (ThemePreviewImage is null || _working is null) return;

        try
        {
            SyncPreviewStyle();

            const double width = 280;
            var surface = new OverlaySurface();
            surface.Apply(_working, "12:34  CPU 12.4%\nRAM 8.2G / 31.9G\n↓ 1.8 MB/s   ↑ 96 KB/s");
            surface.Width = width;
            surface.Measure(new Size(width, double.PositiveInfinity));
            double height = Math.Max(60, surface.DesiredSize.Height);
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();

            const double scale = 2;
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            ThemePreviewImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            Log.Warn($"主题预览渲染失败: {ex.Message}");
        }
    }

    // ==================== 色板 / 取色 ====================

    void ThemePreviewBgChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _readingUi || _working is null) return;

        string text = (ThemePreviewBgBox.Text ?? "").Trim();
        ValidateColorBox(ThemePreviewBgBox, false);
        if (!Settings.IsHexColor(text)) return;
        if (string.Equals(_previewBg, text, StringComparison.OrdinalIgnoreCase)) return;

        _previewBg = text;
        _working.Behavior.SettingsPreviewBg = text;
        SettingsTheme.Apply(_settingsDark, _previewBg);
        MarkDirty();
    }

    void PickThemePreviewBgClick(object sender, RoutedEventArgs e)
    {
        string picked = PickColor(ThemePreviewBgBox.Text);
        if (picked is not null) ThemePreviewBgBox.Text = picked;
    }

    void ResetThemePreviewBgClick(object sender, RoutedEventArgs e) =>
        ThemePreviewBgBox.Text = "#0B1020";

    void PickForegroundClick(object sender, RoutedEventArgs e)
    {
        string picked = PickColor(FgBox.Text);
        if (picked is not null) FgBox.Text = picked;
    }

    void PickBackgroundClick(object sender, RoutedEventArgs e)
    {
        string picked = PickColor(BgBox.Text);
        if (picked is not null) BgBox.Text = picked;
    }

    void PickOutlineColorClick(object sender, RoutedEventArgs e)
    {
        string picked = PickColor(OutlineColorBox.Text);
        if (picked is not null) OutlineColorBox.Text = picked;
    }

    void PickSuccessColorClick(object sender, RoutedEventArgs e)
    {
        string picked = PickColor(SuccessColorBox.Text);
        if (picked is not null) SuccessColorBox.Text = picked;
    }

    void PickWarningColorClick(object sender, RoutedEventArgs e)
    {
        string picked = PickColor(WarningColorBox.Text);
        if (picked is not null) WarningColorBox.Text = picked;
    }

    // ==================== 卡片内部：AI 对话 ====================
    // （历史分区注释保留：那一节的输入框加密逻辑已迁到 CardEditor.cs。）

    /// <summary>输入框失焦：把新填的 Key 加密收下来，之后框里只剩「已填写」提示。</summary>
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
                return $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"取色失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>P2-7：后台线程读取/解析主题文件的结果，回到 UI 线程再决定是否套用。</summary>
    sealed class ThemeImportOutcome
    {
        public bool Ok;
        public Settings Applied;
        public string Error;
    }
}
