using System;
using System.Diagnostics;
using Microsoft.Win32;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>HKCU\...\Run 开机自启。</summary>
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "MirrorpondHUD";
        /// <summary>改名前的注册表值，设置自启时顺手清掉，避免旧 exe 路径残留。</summary>
        const string LegacyValueName = "WindowOverlay";

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

        /// <summary>清掉改名前的旧自启值，避免指向已不存在的 exe。</summary>
        public static void CleanLegacy()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null && key.GetValue(LegacyValueName) != null)
                    {
                        key.DeleteValue(LegacyValueName, false);
                        Log.Info("已清理旧的开机自启项: " + LegacyValueName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("清理旧自启项失败: " + ex.Message);
            }
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
                        key.DeleteValue(LegacyValueName, false);
                        Log.Info("已关闭开机自启");
                        return;
                    }

                    string exe = Process.GetCurrentProcess().MainModule.FileName;
                    // 开机自启一律带 --autostart：登录后直接显示卡片，不受「启动后最小化」影响。
                    string command = "\"" + exe + "\" --autostart";
                    key.SetValue(ValueName, command);
                    key.DeleteValue(LegacyValueName, false);
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
