using System;
using System.Diagnostics;
using Microsoft.Win32;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    /// <summary>HKCU\...\Run 开机自启。</summary>
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "WindowOverlay";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool enabled, bool startMinimized)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        Log.Warn("无法打开 Run 注册表项");
                        return;
                    }

                    if (!enabled)
                    {
                        key.DeleteValue(ValueName, false);
                        Log.Info("已关闭开机自启");
                        return;
                    }

                    string exe = Process.GetCurrentProcess().MainModule.FileName;
                    string command = "\"" + exe + "\"" + (startMinimized ? " --minimized" : "");
                    key.SetValue(ValueName, command);
                    Log.Info("已开启开机自启: " + command);
                }
            }
            catch (Exception ex)
            {
                Log.Error("设置开机自启失败", ex);
            }
        }
    }
}
