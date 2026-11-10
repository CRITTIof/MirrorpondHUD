using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 设置窗「数据源」「行为」两页的下拉框 / 勾选框填充。
    ///
    /// 设备枚举已下沉到 <see cref="SettingsDeviceCatalog"/>（纯数据、自己记日志）；
    /// 这里只做一件事：把返回的列表与建议下标填进控件。枚举失败时的可见占位
    /// （网卡的「自动选择」、磁盘的「未检测到固定磁盘」、显示器的「显示器 1」）
    /// 仍由本文件生成，行为与拆分前一致。
    /// </summary>
    public partial class SettingsWindow
    {
        // ==================== 下拉框数据 ====================

        void FillGpuCombo(int selected)
        {
            int index;
            GpuCombo.ItemsSource = SettingsDeviceCatalog.GetGpuOptions(selected, out index);
            GpuCombo.SelectedIndex = index;
        }

        void FillRefreshCombo(int current)
        {
            int index;
            RefreshCombo.ItemsSource = SettingsDeviceCatalog.GetRefreshOptions(current, out index);
            RefreshCombo.SelectedIndex = index;
        }

        void FillNvidiaCombo(int current)
        {
            int index;
            NvidiaIntervalCombo.ItemsSource = SettingsDeviceCatalog.GetNvidiaOptions(current, out index);
            NvidiaIntervalCombo.SelectedIndex = index;
        }

        void FillAdapterCombo(string current)
        {
            int index;
            AdapterCombo.ItemsSource = SettingsDeviceCatalog.GetAdapterOptions(current, out index);
            AdapterCombo.SelectedIndex = index;
        }

        void FillDrives(List<string> whitelist)
        {
            DrivesPanel.Children.Clear();

            string[] drives = SettingsDeviceCatalog.GetFixedDrives();
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
            int index;
            MonitorCombo.ItemsSource = SettingsDeviceCatalog.GetMonitorOptions(current, out index);
            MonitorCombo.SelectedIndex = index;
        }

        void RefreshAdaptersClick(object sender, RoutedEventArgs e)
        {
            string current = AdapterCombo.SelectedIndex > 0 ? AdapterCombo.SelectedItem as string : "";
            FillAdapterCombo(current);
        }
    }
}
