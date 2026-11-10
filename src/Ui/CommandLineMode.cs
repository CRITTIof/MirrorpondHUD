using System;
using System.Collections.Generic;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
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
        /// 尝试执行「跑完即退出」的模式。命中时 exitCode 为进程退出码并由调用方 Shutdown；
        /// 未命中返回 false，调用方继续正常启动。
        /// </summary>
        internal static bool TryRunImmediate(List<string> args, out int exitCode)
        {
            exitCode = 0;
            if (args == null) return false;

            // 离屏验收不能抓到淡入 / 补位的中间帧：截图与自检关闭卡片动效。
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    CardMotion.Enabled = false;
                    break;
                }
            }

            // 顺序与旧实现一致：--screenshot-cards 必须先于 --screenshot 判断。
            string shotCards = GetOption(args, "--screenshot-cards");
            if (shotCards != null)
            {
                Screenshots.CaptureCards(shotCards);
                return true;
            }

            string shot = GetOption(args, "--screenshot");
            if (shot != null)
            {
                Screenshots.CaptureOverlay(shot);
                return true;
            }

            string shotSettings = GetOption(args, "--screenshot-settings");
            if (shotSettings != null)
            {
                Screenshots.CaptureSettings(shotSettings);
                return true;
            }

            string shotWidgets = GetOption(args, "--screenshot-widgets");
            if (shotWidgets != null)
            {
                Screenshots.CaptureWidgets(shotWidgets);
                return true;
            }

            if (args.Contains("--selftest"))
            {
                // SelfTest 自己负责收集失败并返回失败数，不在这里包 finally：
                // 让它内部未捕获的异常走 App 的初始化失败路径。
                exitCode = SelfTest.Run();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 解析并启动 --smoke[:秒]。摘参数与可见性改写在这里完成，计时器到点调用调用方的
        /// shutdown(0)（保留原先 DispatcherTimer + Shutdown(0) 的语义）。
        /// </summary>
        internal static void StartSmokeIfRequested(List<string> args, Action<int> shutdown)
        {
            if (args == null) return;

            // --perf：正常起界面，采样 N 秒后输出基线并退出。用于优化 TextCanvas / GlassFeed
            // 之前拿同一台机器上的分配与调用次数；不写设置、不改运行行为。
            if (args.Contains("--perf") || GetOption(args, "--perf") != null)
            {
                int perfSeconds = 12;
                string perfValue = GetOption(args, "--perf");
                if (perfValue != null && !perfValue.StartsWith("--", StringComparison.Ordinal))
                {
                    int parsed;
                    if (int.TryParse(perfValue, out parsed)) perfSeconds = Math.Max(3, Math.Min(120, parsed));
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
                    if (shutdown != null) shutdown(0);
                };
                perfTimer.Start();
                return;
            }

            string smoke = GetOption(args, "--smoke");
            if (smoke == null) return;

            // 旧实现的行为原样保留：连秒数一起摘掉；--smoke-visible 改写成 --visible，
            // 否则在没有 --minimized 时补上 --minimized（冒烟默认不弹全屏卡片）。
            args.Remove(smoke);
            args.Remove("--smoke");
            bool visible = args.Remove("--smoke-visible");
            if (visible) args.Add("--visible");
            else if (!args.Contains("--minimized")) args.Add("--minimized");

            int seconds;
            if (!int.TryParse(smoke, out seconds)) seconds = 6;

            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
            timer.Tick += (s, a) =>
            {
                timer.Stop();
                Log.Info("冒烟测试结束，退出");
                if (shutdown != null) shutdown(0);
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
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(name.Length + 1);
            }
            return null;
        }
    }
}
