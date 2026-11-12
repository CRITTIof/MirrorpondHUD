using System;

namespace MirrorpondHUD.Core;

/// <summary>
/// X-9：释放 / 收尾路径的统一写法。
///
/// 本仓库大量空 catch 里，Dispose 收尾（“对象已经不可用”）是合法的；真正危险的是
/// 业务路径上吞掉失败。这个辅助只用于前者：把“预期内的释放失败”降为 Debug，
/// 其它异常一律 Warn（失败可见），调用方不再需要写 <c>catch { }</c>。
/// </summary>
public static class SafeRelease
{
    public static void Run(Action action, string what)
    {
        if (action is null) return;

        try
        {
            action();
        }
        catch (ObjectDisposedException)
        {
            // 释放路径上对象已被别的线程释放：预期竞态，不算故障。
            Log.Debug($"释放 {what} 时对象已释放（预期竞态）");
        }
        catch (InvalidOperationException ex)
        {
            // 句柄 / 窗口在收尾阶段已经失效（WPF/Win32 常见），保留 Debug 便于排查。
            Log.Debug($"释放 {what} 时状态已失效: {ex.Message}");
        }
        catch (Exception ex)
        {
            // 释放失败本身可能意味着资源泄漏，必须留痕。
            Log.Warn($"释放 {what} 失败: {ex.Message}");
        }
    }
}
