using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MirrorpondHUD.Core;

/// <summary>
/// 后台跑外部进程并收集 stdout / stderr 的小工具。
///
/// P1-8 的背景：原来的探测都是「先 ReadToEnd、再 WaitForExit」——只要 ffmpeg /
/// nvidia-smi 卡住不退出（离线网络盘、损坏容器、杀软拦截），ReadToEnd 就永久阻塞，
/// 后面的 WaitForExit 超时形同虚设，UI 线程或采样线程会一直冻住。这里改成：
///  · stdout / stderr 并行异步读，任何一边都不会把另一边卡住；
///  · 超时是「从 Start 之后算起的整体超时」，到点 Kill(true) 结束整棵进程树；
///  · 方法本身是 async，调用方在自己的后台线程 await，UI 线程 / 采样线程立刻返回。
///
/// 放在 Core 里是因为媒体层（FfmpegPlayer / Decoders）和采样层（NvidiaSmi）都要用；
/// 命名空间与目录不一致是这个项目的历史问题（评审 P2-13），这里不额外制造第二种风格。
/// </summary>
public static class AsyncProcess
{
    public sealed class Result
    {
        public bool Started;
        public bool TimedOut;
        public int ExitCode = -1;
        public byte[] StdOut = Empty;
        public string StdErr = "";
        public string Error = "";
        // K-14：stdout 超过上限时只保留前 N 字节并置位，调用方据此判失败而不是解半张图。
        public bool StdOutTruncated;

        public string StdOutText => StdOut is null || StdOut.Length == 0 ? "" : Encoding.UTF8.GetString(StdOut);
    }

    static readonly byte[] Empty = new byte[0];

    /// <summary>Kill 之后等读取任务收尾的上限；正常几十毫秒内就结束。</summary>
    const int DrainMs = 2000;

    /// <summary>K-14：单次 stdout 的默认上限（8 MB）。超出后继续读空但丢弃，避免无界内存。</summary>
    public const int DefaultMaxStdOutBytes = 8 * 1024 * 1024;

    /// <summary>
    /// 启动 exe 并等它退出。timeoutMs 是整体超时，超时返回 TimedOut = true 并已 Kill(true)。
    /// 注意：这是 async，不要在 UI 线程上同步 GetResult()——那样又会冻 UI。
    /// </summary>
    public static async Task<Result> RunAsync(string exe, IList<string> arguments, int timeoutMs,
                                              int maxStdOutBytes = DefaultMaxStdOutBytes)
    {
        var result = new Result();

        ProcessStartInfo psi;
        try
        {
            psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (arguments is not null)
            {
                for (int i = 0; i < arguments.Count; i++) psi.ArgumentList.Add(arguments[i]);
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }

        var process = new Process { StartInfo = psi };
        try
        {
            try { process.Start(); }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }

            result.Started = true;

            // stdout / stderr 必须同时读：只读一边，另一边管道写满会把进程卡死，
            // 于是「读的那一边」也就永远等不到 EOF —— 这正是原实现的死法。
            Task<(byte[] Bytes, bool Truncated)> outTask =
                ReadAllBytesAsync(process.StandardOutput.BaseStream, Math.Max(1, maxStdOutBytes));
            Task<string> errTask = process.StandardError.ReadToEndAsync();

            Task exitTask = process.WaitForExitAsync();
            Task first = await Task.WhenAny(exitTask, Task.Delay(Math.Max(1, timeoutMs))).ConfigureAwait(false);

            if (first != exitTask)
            {
                result.TimedOut = true;
                try { process.Kill(true); } catch { }
                // Kill 不是保证会立刻退出（驱动 / 杀软可能卡在句柄上）；这里再给 2 秒收尾，
                // 不能因为 WaitForExitAsync 永不完成而让调用方跟着永久等待。
                try
                {
                    await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(DrainMs))
                              .ConfigureAwait(false);
                }
                catch { }
            }

            Task all = Task.WhenAll(new Task[] { outTask, errTask });
            await Task.WhenAny(all, Task.Delay(DrainMs)).ConfigureAwait(false);

            if (all.IsCompleted && !all.IsFaulted)
            {
                if (outTask.Status == TaskStatus.RanToCompletion)
                {
                    result.StdOut = outTask.Result.Bytes;
                    result.StdOutTruncated = outTask.Result.Truncated;
                }
                if (errTask.Status == TaskStatus.RanToCompletion) result.StdErr = errTask.Result;
            }
            else
            {
                // 极端情况（驱动卡在管道上）：不再等，异常观察掉，避免 TaskScheduler
                // 记一条未观察异常把进程日志弄脏。
                _ = all.ContinueWith(t => { _ = t.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }

            try { result.ExitCode = process.ExitCode; } catch { }
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
        finally
        {
            try { process.Dispose(); } catch { }
        }
    }

    static async Task<(byte[] Bytes, bool Truncated)> ReadAllBytesAsync(Stream stream, int limit)
    {
        using (var memory = new MemoryStream())
        {
            byte[] buffer = new byte[81920];
            int total = 0;
            bool truncated = false;
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) break;

                int keep = Math.Min(read, limit - total);
                if (keep > 0)
                {
                    memory.Write(buffer, 0, keep);
                    total += keep;
                }
                if (keep < read) truncated = true;
            }
            if (truncated) Log.Warn($"进程 stdout 超过上限 {limit} 字节，已截断（继续读空以免子进程阻塞）");
            return (memory.ToArray(), truncated);
        }
    }
}
