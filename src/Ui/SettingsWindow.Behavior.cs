using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗「行为」页里独立成事件处理器的那部分：光标样式取值、清除按键记录、
/// 应用锚点位置、打开配置目录 / 日志文件。
///
/// 说明：开机自启、启动最小化、托盘、启动引导、打字速度、光标闪烁等开关没有独立方法，
/// 它们的装载与读取内联在 LoadUi / ReadUi 里（顺序与语义必须保持不变），所以不在本文件。
/// </summary>
public partial class SettingsWindow
{
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

    static int CardSlideIndex(string value)
    {
        switch (Settings.NormalizeCardSlide(value))
        {
            case "Left": return 1;
            case "Right": return 2;
            case "Follow": return 3;
            default: return 0;
        }
    }

    string CardSlideTag()
    {
        var item = CardSlideCombo.SelectedItem as ComboBoxItem;
        return Settings.NormalizeCardSlide(item is null ? null : item.Tag as string);
    }

    string CursorTag()
    {
        var item = CursorStyleCombo.SelectedItem as ComboBoxItem;
        return item is null ? "Block" : Settings.NormalizeCursor(item.Tag as string);
    }

    void ClearKeysClick(object sender, RoutedEventArgs e)
    {
        if (_onClearKeys is not null) _onClearKeys();
        StatusText.Text = "按键记录已清空";
    }

    void ApplyPositionClick(object sender, RoutedEventArgs e)
    {
        double x = ParseDouble(XBox.Text, 48);
        double y = ParseDouble(YBox.Text, 48);
        int monitor = Math.Max(0, MonitorCombo.SelectedIndex);
        if (_onPosition is not null) _onPosition(x, y, monitor);
        StatusText.Text = $"叠层已移动到 X={x} Y={y}";
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
            {
                // G-14：序号越界不能静默钳到最后一台（那是另一台显示器）。这里只是显示同步，
                // 回退到主屏并在状态行之外留一条日志，持久身份仍由设备名决定。
                if (monitor < 0 || monitor >= MonitorCombo.Items.Count)
                {
                    Log.Warn($"同步显示器下拉框时序号 {monitor} 越界（共 {MonitorCombo.Items.Count} 台），显示为主屏");
                    monitor = 0;
                }
                MonitorCombo.SelectedIndex = monitor;
            }
        }
        finally
        {
            _suppress = old;
        }
    }

    void OpenConfigFolderClick(object sender, RoutedEventArgs e) => OpenWithExplorer("", ConfigStore.Path);

    void OpenLogClick(object sender, RoutedEventArgs e) => OpenWithExplorer("/select,", Log.FilePath);

    static void OpenWithExplorer(string arguments, string path)
    {
        try
        {
            if (arguments.Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{System.IO.Path.GetDirectoryName(path)}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"{arguments}\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"打开资源管理器失败: {ex.Message}");
        }
    }
}
