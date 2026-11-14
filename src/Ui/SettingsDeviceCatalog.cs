using System;
using System.Collections.Generic;
using System.Linq;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗「数据源 / 行为」两页用到的设备与候选项枚举。
///
/// 只负责拿纯数据：不接触任何 WPF 控件，也不决定界面上怎么摆；
/// 设置窗（SettingsWindow.Sources.cs）负责把返回的列表塞进 ComboBox / 生成磁盘勾选框。
///
/// 失败可见：以前这几处是 <c>catch { }</c> 静默吞掉（GPU / 网卡 / 磁盘 / 显示器），
/// 现在统一 Log.Warn 后返回空列表或兜底项，界面侧的可见占位文案保持不变
/// （例如磁盘为空时仍显示「未检测到固定磁盘」）。
/// </summary>
internal static class SettingsDeviceCatalog
{
    /// <summary>
    /// S-05：本轮设备枚举是否失败。失败时设置窗保留原选择，不能用回退项覆盖配置。
    /// 由 SettingsWindow.LoadUi 在 Fill* 之前清零、之后读取。
    /// </summary>
    internal static bool DeviceEnumerationFailed;

    internal static void BeginDeviceEnumeration() => DeviceEnumerationFailed = false;

    /// <summary>采样间隔候选（毫秒）；「行为」页下拉框与 ReadUi 的下标映射共用这一份。</summary>
    internal static readonly int[] RefreshOptions = { 200, 300, 500, 800, 1000, 2000 };

    /// <summary>nvidia-smi 采样间隔候选（毫秒）；「数据源」页下拉框与 ReadUi 的下标映射共用。</summary>
    internal static readonly int[] NvidiaOptions = { 1000, 2000, 3000, 5000 };

    /// <summary>
    /// GPU 下拉项：第 0 项是「汇总」。selected 是设置里的 GpuIndex（-1 = 汇总），
    /// comboIndex 可直接给 ComboBox.SelectedIndex；越界时退回汇总。
    /// </summary>
    internal static List<string> GetGpuOptions(int selected, out int comboIndex)
    {
        int count = 0;
        try
        {
            count = MetricEngine.GetGpuCount();
        }
        catch (Exception ex)
        {
            DeviceEnumerationFailed = true;
            Log.Warn($"枚举 GPU 数量失败，GPU 下拉框只显示汇总项: {ex.Message}");
        }
        // 0 张 GPU 在正常桌面上极罕见；按“枚举不可信”处理，避免把用户的独立显卡选择改回汇总。
        if (count <= 0) DeviceEnumerationFailed = true;

        var items = new List<string> { "汇总（所有 GPU）" };
        for (int i = 0; i < count; i++) items.Add($"GPU {i}（独立统计）");

        comboIndex = selected + 1;
        if (comboIndex < 0 || comboIndex >= items.Count) comboIndex = 0;
        return items;
    }

    /// <summary>刷新率下拉项（毫秒）；current 不在候选里时退回 500 毫秒。</summary>
    internal static List<string> GetRefreshOptions(int current, out int comboIndex)
    {
        var items = RefreshOptions.Select(ms => $"{ms} 毫秒").ToList();
        int index = Array.IndexOf(RefreshOptions, current);
        comboIndex = index >= 0 ? index : Array.IndexOf(RefreshOptions, 500);
        return items;
    }

    /// <summary>nvidia-smi 采样间隔下拉项（秒）；current 不在候选里时退回第二档（2 秒）。</summary>
    internal static List<string> GetNvidiaOptions(int current, out int comboIndex)
    {
        var items = NvidiaOptions.Select(ms => $"{ms / 1000.0:0.0} 秒").ToList();
        int index = Array.IndexOf(NvidiaOptions, current);
        comboIndex = index >= 0 ? index : 1;
        return items;
    }

    /// <summary>网卡下拉项：第 0 项是自动选择；current 不在列表里时退回自动选择。</summary>
    internal static List<string> GetAdapterOptions(string current, out int comboIndex)
    {
        var items = new List<string> { "自动选择（流量最大的网卡）" };
        try
        {
            items.AddRange(MetricEngine.GetNetworkAdapters());
        }
        catch (Exception ex)
        {
            DeviceEnumerationFailed = true;
            Log.Warn($"枚举网卡失败，网卡下拉框只显示自动选择: {ex.Message}");
        }
        // 只剩“自动选择”时同样视为不可信，保留用户原选择。
        if (items.Count <= 1) DeviceEnumerationFailed = true;

        comboIndex = 0;
        if (!string.IsNullOrEmpty(current))
        {
            int found = items.IndexOf(current);
            if (found >= 0) comboIndex = found;
        }
        return items;
    }

    /// <summary>固定磁盘分区名；枚举失败返回空数组，界面照旧显示「未检测到固定磁盘」。</summary>
    internal static string[] GetFixedDrives()
    {
        try
        {
            return MetricEngine.GetFixedDrives();
        }
        catch (Exception ex)
        {
            Log.Warn($"枚举固定磁盘失败: {ex.Message}");
            return new string[0];
        }
    }

    /// <summary>
    /// 显示器下拉项（带分辨率）；枚举失败退回只有「显示器 1」一项。
    /// current 是设置里的 Overlay.Monitor，越界时选第 0 项。
    /// </summary>
    internal static List<string> GetMonitorOptions(int current, out int comboIndex)
    {
        var items = new List<string>();
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
                items.Add($"显示器 {(i + 1)}（{screens[i].Bounds.Width}×{screens[i].Bounds.Height}）");
        }
        catch (Exception ex)
        {
            Log.Warn($"枚举显示器失败，锚点下拉框退回单显示器: {ex.Message}");
        }

        if (items.Count == 0) items.Add("显示器 1");
        comboIndex = current >= 0 && current < items.Count ? current : 0;
        return items;
    }
}
