using System;
using System.Diagnostics;
using Microsoft.Win32;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>HKCU\...\Run 开机自启。</summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "MirrorpondHUD";
    /// <summary>改名前的注册表值，设置自启时顺手清掉，避免旧 exe 路径残留。</summary>
    const string LegacyValueName = "WindowOverlay";

    /// <summary>S-06：最近一次 Set / IsEnabled 的失败原因，供设置窗显示。</summary>
    public static string LastError { get; private set; }

    public static bool IsEnabled()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                if (key is null) return false;
                return key.GetValue(ValueName) is not null;
            }
        }
        catch (Exception ex)
        {
            // S-06：读注册表失败不能当成“未开启”静默处理，否则后续 Set 的判断依据也是错的。
            LastError = ex.Message;
            Log.Warn($"读取开机自启状态失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>清掉改名前的旧自启值，避免指向已不存在的 exe。</summary>
    public static void CleanLegacy()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key is not null && key.GetValue(LegacyValueName) is not null)
                {
                    key.DeleteValue(LegacyValueName, false);
                    Log.Info($"已清理旧的开机自启项: {LegacyValueName}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"清理旧自启项失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 写注册表。返回 false 时 <see cref="LastError"/> 是失败原因（S-06：
    /// 调用方必须把它透传到设置窗，不能只落日志然后照常提示“已保存并应用”）。
    /// </summary>
    public static bool Set(bool enabled, bool startMinimized)
    {
        LastError = null;
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key is null)
                {
                    LastError = @"无法打开 HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
                    Log.Warn("无法打开 Run 注册表项");
                    return false;
                }

                if (!enabled)
                {
                    key.DeleteValue(ValueName, false);
                    key.DeleteValue(LegacyValueName, false);
                    Log.Info("已关闭开机自启");
                    return true;
                }

                string exe = Process.GetCurrentProcess().MainModule.FileName;
                // S-07：自启尊重“启动后最小化”，由 --minimized 告知 OverlayController。
                string command = startMinimized
                    ? $"\"{exe}\" --autostart --minimized"
                    : $"\"{exe}\" --autostart";
                key.SetValue(ValueName, command);
                key.DeleteValue(LegacyValueName, false);
                Log.Info($"已开启开机自启: {command}");
                return true;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error("设置开机自启失败", ex);
            return false;
        }
    }
}
