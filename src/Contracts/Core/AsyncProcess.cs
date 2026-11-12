using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
/// C-13（评审 P2）补齐了三件事：
///  · stdout 不再写死 UTF-8：先按严格 UTF-8 解，失败再按系统 OEM 代码页解（中文系统的
///    cmd 输出是 GBK，直接 UTF-8 解会整段乱码）；调用方也可以显式传 Encoding。
///  · 超时不再丢弃已经读到的输出：读取器写进共享收集器，超时后取快照并置
///    StdOutIncomplete，调用方据此知道「这是部分结果」。
///  · 超时用的 Task.Delay 现在由 CancellationTokenSource 取消，不再每次调用都留一个
///    活到超时点才退出的定时器任务。
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
        // C-13：超时 / 读取未收尾时 StdOut 只是已读到的部分，调用方不得当完整结果用。
        public bool StdOutIncomplete;
        // C-13：调用方显式指定 stdout 编码；null 表示自动（严格 UTF-8 → OEM 回退）。
        public Encoding StdOutEncoding;

        public string StdOutText => DecodeStdOut(StdOut, StdOutEncoding);
    }

    static readonly byte[] Empty = new byte[0];

    /// <summary>Kill 之后等读取任务收尾的上限；正常几十毫秒内就结束。</summary>
    const int DrainMs = 2000;

    /// <summary>K-14：单次 stdout 的默认上限（8 MB）。超出后继续读空但丢弃，避免无界内存。</summary>
    public const int DefaultMaxStdOutBytes = 8 * 1024 * 1024;

    /// <summary>严格模式：遇到非法 UTF-8 会抛 DecoderFallbackException，用于触发 OEM 回退。</summary>
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// 启动 exe 并等它退出。timeoutMs 是整体超时，超时返回 TimedOut = true 并已 Kill(true)。
    /// 注意：这是 async，不要在 UI 线程上同步 GetResult()——那样又会冻 UI。
    /// </summary>
    public static async Task<Result> RunAsync(string exe, IList<string> arguments, int timeoutMs,
                                              int maxStdOutBytes = DefaultMaxStdOutBytes,
                                              Encoding stdoutEncoding = null)
    {
        var result = new Result { StdOutEncoding = stdoutEncoding };

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
        var collector = new ByteCollector(Math.Max(1, maxStdOutBytes));
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
            Task outTask = ReadIntoAsync(process.StandardOutput.BaseStream, collector);
            Task<string> errTask = process.StandardError.ReadToEndAsync();

            // C-13：超时用的定时器必须可取消，否则每次调用都留一个活到 timeoutMs 的任务。
            using (var timeoutCts = new CancellationTokenSource())
            {
                Task exitTask = process.WaitForExitAsync();
                Task timeoutTask = Task.Delay(Math.Max(1, timeoutMs), timeoutCts.Token);
                Task first = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);

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

                timeoutCts.Cancel();
            }

            Task reads = Task.WhenAll(outTask, errTask);
            await Task.WhenAny(reads, Task.Delay(DrainMs)).ConfigureAwait(false);

            // C-13：即使读取没在收尾窗口内完成，也把已读到的部分交出去；
            // StdOutIncomplete 让调用方知道自己拿到的是半截数据。
            result.StdOut = collector.Snapshot();
            result.StdOutTruncated = collector.Truncated;
            result.StdOutIncomplete = !outTask.IsCompleted;

            if (errTask.Status == TaskStatus.RanToCompletion) result.StdErr = errTask.Result;
            else _ = errTask.ContinueWith(t => { _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

            if (!outTask.IsCompleted)
            {
                _ = outTask.ContinueWith(t => { _ = t.Exception; },
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

    /// <summary>
    /// stdout 解码。显式编码优先；未指定时先按严格 UTF-8（绝大多数工具输出），
    /// 失败再按系统 OEM 代码页（中文 cmd / GBK 工具）。两条都失败时返回替换字符，
    /// 不抛异常——调用方是在解析输出，不该因为一个坏字节把整条采样链路打断。
    /// </summary>
    public static string DecodeStdOut(byte[] bytes, Encoding explicitEncoding)
    {
        if (bytes is null || bytes.Length == 0) return "";
        if (explicitEncoding is not null) return explicitEncoding.GetString(bytes);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeOem(bytes);
        }
    }

    const uint CP_OEMCP = 1;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] bytes, int byteCount,
                                          StringBuilder chars, int charCount);

    /// <summary>
    /// 用 Win32 CP_OEMCP 解码，避免依赖 CodePagesEncodingProvider（宿主保持零 NuGet）。
    /// 只在 UTF-8 解码失败时走到这里。
    /// </summary>
    static string DecodeOem(byte[] bytes)
    {
        try
        {
            int need = MultiByteToWideChar(CP_OEMCP, 0, bytes, bytes.Length, null, 0);
            if (need <= 0) return Encoding.Latin1.GetString(bytes);

            var sb = new StringBuilder(need);
            int written = MultiByteToWideChar(CP_OEMCP, 0, bytes, bytes.Length, sb, need);
            return written > 0 ? sb.ToString() : Encoding.Latin1.GetString(bytes);
        }
        catch (Exception ex)
        {
            // 解码失败不该影响采样：降级为 Latin1（每个字节一个字符），至少不丢长度。
            Log.Warn($"stdout 按 OEM 代码页解码失败，降级 Latin1：{ex.Message}");
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>
    /// 读线程写、调用方超时后读快照的共享缓冲。锁只保护一次内存拷贝，
    /// 不做磁盘/管道 IO，不会把读取线程卡住。
    /// </summary>
    sealed class ByteCollector
    {
        readonly object _gate = new();
        readonly MemoryStream _buffer = new();
        readonly int _limit;
        int _total;
        bool _truncated;

        public ByteCollector(int limit) { _limit = limit; }

        public void Append(byte[] buffer, int count)
        {
            lock (_gate)
            {
                int keep = Math.Min(count, _limit - _total);
                if (keep > 0)
                {
                    _buffer.Write(buffer, 0, keep);
                    _total += keep;
                }
                if (keep < count) _truncated = true;
            }
        }

        public bool Truncated
        {
            get { lock (_gate) return _truncated; }
        }

        public byte[] Snapshot()
        {
            lock (_gate) return _buffer.ToArray();
        }
    }

    static async Task ReadIntoAsync(Stream stream, ByteCollector collector)
    {
        byte[] buffer = new byte[81920];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) break;
                collector.Append(buffer, read);
            }
            if (collector.Truncated)
            {
                Log.Warn($"进程 stdout 超过上限，已截断（继续读空以免子进程阻塞）");
            }
        }
        catch (Exception ex)
        {
            // 超时 Kill 会让管道句柄失效，这里读到异常是预期收尾；其它情况记一条 Warn。
            Log.Warn($"读取进程 stdout 中断：{ex.Message}");
        }
    }
}
