using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 命令行模式（--screenshot* / --selftest / --smoke）的解析与执行。
/// App.OnStartup 只留启动顺序与退出收尾，这里集中处理「跑完即退出」和「定时退出」两类参数。
///
/// 行为契约（与原先内联在 App 里逐字一致）：
/// - 截图 / 自检在插件加载之后、配置读取之前执行，仍位于 App 的启动 try 内，
///   未捕获异常走同一条初始化失败路径（记录 + 提示 + Shutdown(1)）。
/// - 分支顺序固定为 cards → overlay → settings → widgets → selftest，退出码原样带回。
/// - --smoke 不立即退出：摘掉自己的参数、改写 --smoke-visible / --minimized，
///   再由 DispatcherTimer 到点调用宿主传入的 shutdown(0)。
/// - 参数非法时的回退：--smoke 的秒数解析失败按 6 秒；未知参数原样留给 OverlayController。
/// </summary>
internal static class CommandLineMode
{
    /// <summary>
    /// 是否包含“跑完即退出”的参数（--selftest / --screenshot*）。
    /// App 在单实例检查里用它区分“普通第二实例”与“验证命令”，后者不能静默返回 0。
    /// </summary>
    internal static bool HasImmediateMode(IEnumerable<string> args)
    {
        if (args is null) return false;
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 尝试执行「跑完即退出」的模式。命中时 exitCode 为进程退出码并由调用方 Shutdown；
    /// 未命中返回 false，调用方继续正常启动。
    /// </summary>
    internal static bool TryRunImmediate(List<string> args, out int exitCode)
    {
        exitCode = 0;
        if (args is null) return false;

        // 离屏验收不能抓到淡入 / 补位的中间帧：截图与自检关闭卡片动效。
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                CardMotion.SuppressForCapture();
                break;
            }
        }

        // 顺序与旧实现一致：--screenshot-cards 必须先于 --screenshot 判断。
        // S-20：六种模式的参数都是「输出文件路径」，统一先做绝对路径 / 扩展名 / 目录可写校验；
        // 校验失败必须 Log.Error + 非 0 退出，不能把配置或任意文件当 PNG 覆盖掉。
        // --screenshot-dialog 先判：避免 --screenshot 的前缀匹配误收，也让它独占自己的 --dialog-light。
        if (TryRunDialogScreenshot(args, out exitCode)) return true;
        if (TryRunScreenshot(args, "--screenshot-cards", Screenshots.CaptureCards, out exitCode)) return true;
        if (TryRunScreenshot(args, "--screenshot", Screenshots.CaptureOverlay, out exitCode)) return true;
        if (TryRunScreenshot(args, "--screenshot-settings", Screenshots.CaptureSettings, out exitCode)) return true;
        if (TryRunScreenshot(args, "--screenshot-widgets", Screenshots.CaptureWidgets, out exitCode)) return true;
        if (TryRunScreenshot(args, "--screenshot-station", Screenshots.CaptureStationViews, out exitCode)) return true;

        if (args.Contains("--selftest"))
        {
            // C-17：--isolated 把配置 / 卡片状态重定向到临时目录，并跳过键盘注入与全局热键。
            bool isolated = args.Remove("--isolated");
            string isolatedDir = null;
            if (isolated)
            {
                isolatedDir = Path.Combine(Path.GetTempPath(), $"mhud-selftest-{Guid.NewGuid():N}");
                Directory.CreateDirectory(isolatedDir);
                ConfigStore.SetDirectoryOverride(isolatedDir);
                SelfTest.Isolated = true;
                Log.Info($"自检隔离模式：配置与卡片状态重定向到 {isolatedDir}");
            }

            // SelfTest 自己负责收集失败并返回失败数；临时目录在返回后清理。
            try { exitCode = SelfTest.Run(); }
            finally
            {
                SelfTest.Isolated = false;
                ConfigStore.ClearDirectoryOverride();
                if (isolatedDir is not null)
                {
                    try { Directory.Delete(isolatedDir, true); }
                    catch (Exception ex) { Log.Warn($"清理自检临时目录失败: {ex.Message}"); }
                }
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// --screenshot-dialog &lt;path&gt; [--dialog-light]：单张图渲染四种对话框变体。
    /// 路径校验复用 S-20（绝对路径 / .png / 目录可写），非法路径按失败退出，不落回正常启动。
    /// </summary>
    static bool TryRunDialogScreenshot(List<string> args, out int exitCode)
    {
        exitCode = 0;
        string raw = GetOption(args, "--screenshot-dialog");
        if (raw is null)
        {
            // 只写了选项没写路径：必须明确失败。否则会落回正常启动，
            // 用户以为“截图失败”，实际却常驻了一个 GUI 进程。
            bool present = args.Contains("--screenshot-dialog") ||
                           args.Exists(a => a.StartsWith("--screenshot-dialog=", StringComparison.OrdinalIgnoreCase));
            if (!present) return false;

            Log.Error("--screenshot-dialog 缺少输出路径");
            exitCode = 1;
            return true;
        }

        bool light = args.Remove("--dialog-light");
        if (!Screenshots.TryResolveOutputPath(raw, out var path, out var error))
        {
            Log.Error($"--screenshot-dialog 输出路径非法：{error}（参数：{raw}）");
            exitCode = 1;
            return true;
        }

        Log.Info($"--screenshot-dialog 输出到 {path}（{(light ? "亮色" : "暗色")}）");
        exitCode = Screenshots.CaptureDialogs(path, light) ? 0 : 1;
        return true;
    }

    /// <summary>
    /// S-20：解析并执行某个截图选项。未命中该选项返回 false（调用方继续看下一个模式）；
    /// 命中时无论成败都返回 true 并写 <paramref name="exitCode"/> ——
    /// 非法路径是明确失败，不能被当成“没有设这个参数”而落到正常启动。
    /// </summary>
    static bool TryRunScreenshot(List<string> args, string option, Func<string, bool> capture, out int exitCode)
    {
        exitCode = 0;
        string raw = GetOption(args, option);
        if (raw is null) return false;

        if (!Screenshots.TryResolveOutputPath(raw, out var path, out var error))
        {
            Log.Error($"{option} 输出路径非法：{error}（参数：{raw}）");
            exitCode = 1;
            return true;
        }

        // 回显最终落点：相对路径按 cwd 展开后，用户才能确认图写到了哪。
        Log.Info($"{option} 输出到 {path}");
        exitCode = capture(path) ? 0 : 1;
        return true;
    }

    /// <summary>
    /// 解析并启动 --smoke[:秒]。摘参数与可见性改写在这里完成，计时器到点调用调用方的
    /// shutdown(0)（保留原先 DispatcherTimer + Shutdown(0) 的语义）。
    /// </summary>
    internal static void StartSmokeIfRequested(List<string> args, Action<int> shutdown)
    {
        if (args is null) return;

        // --perf：正常起界面，采样 N 秒后输出基线并退出。用于优化 TextCanvas / GlassFeed
        // 之前拿同一台机器上的分配与调用次数；不写设置、不改运行行为。
        if (args.Contains("--perf") || GetOption(args, "--perf") is not null)
        {
            int perfSeconds = 12;
            string perfValue = GetOption(args, "--perf");
            if (perfValue is not null && !perfValue.StartsWith("--", StringComparison.Ordinal))
            {
                if (int.TryParse(perfValue, out var parsed)) perfSeconds = Math.Max(3, Math.Min(120, parsed));
                args.Remove(perfValue);
            }
            args.Remove("--perf");
            if (!args.Contains("--minimized") && !args.Contains("--visible")) args.Add("--visible");

            PerformanceProbe.Begin();
            var perfTimer = new DispatcherTimer();
            perfTimer.Interval = TimeSpan.FromSeconds(perfSeconds);
            perfTimer.Tick += (s, a) =>
            {
                perfTimer.Stop();
                PerformanceProbe.End();
                if (shutdown is not null) shutdown(0);
            };
            perfTimer.Start();
            return;
        }

        string smoke = GetOption(args, "--smoke");
        if (smoke is null) return;

        // 旧实现的行为原样保留：连秒数一起摘掉；--smoke-visible 改写成 --visible，
        // 否则在没有 --minimized 时补上 --minimized（冒烟默认不弹全屏卡片）。
        args.Remove(smoke);
        args.Remove("--smoke");
        bool visible = args.Remove("--smoke-visible");
        if (visible) args.Add("--visible");
        else if (!args.Contains("--minimized")) args.Add("--minimized");

        if (!int.TryParse(smoke, out var seconds)) seconds = 6;

        var timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        timer.Tick += (s, a) =>
        {
            timer.Stop();
            Log.Info("冒烟测试结束，退出");
            if (shutdown is not null) shutdown(0);
        };
        timer.Start();
    }

    /// <summary>取「--name 值」或「--name=值」；两种写法都没有时返回 null。</summary>
    static string GetOption(List<string> args, string name)
    {
        int index = args.IndexOf(name);
        if (index >= 0 && index + 1 < args.Count) return args[index + 1];

        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
                return args[i].Substring(name.Length + 1);
        }
        return null;
    }
}
