using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MirrorpondHUD.Core
{
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
    internal static class AsyncProcess
    {
        internal sealed class Result
        {
            public bool Started;
            public bool TimedOut;
            public int ExitCode = -1;
            public byte[] StdOut = Empty;
            public string StdErr = "";
            public string Error = "";

            public string StdOutText
            {
                get { return StdOut == null || StdOut.Length == 0 ? "" : Encoding.UTF8.GetString(StdOut); }
            }
        }

        static readonly byte[] Empty = new byte[0];

        /// <summary>Kill 之后等读取任务收尾的上限；正常几十毫秒内就结束。</summary>
        const int DrainMs = 2000;

        /// <summary>
        /// 启动 exe 并等它退出。timeoutMs 是整体超时，超时返回 TimedOut = true 并已 Kill(true)。
        /// 注意：这是 async，不要在 UI 线程上同步 GetResult()——那样又会冻 UI。
        /// </summary>
        public static async Task<Result> RunAsync(string exe, IList<string> arguments, int timeoutMs)
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
                if (arguments != null)
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
                Task<byte[]> outTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
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
                    if (outTask.Status == TaskStatus.RanToCompletion) result.StdOut = outTask.Result;
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

        static async Task<byte[]> ReadAllBytesAsync(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory).ConfigureAwait(false);
                return memory.ToArray();
            }
        }
    }

    /// <summary>通过 nvidia-smi 读取温度 / 显存 / 功耗。没有 N 卡时静默降级。</summary>
    internal sealed class NvidiaSmi
    {
        static readonly string[] QueryArgs =
        {
            "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw",
            "--format=csv,noheader,nounits"
        };

        // nvidia-smi 冷启动要几百毫秒到几秒；整体超时 2.5s，到点 Kill(true)。
        const int ProbeTimeoutMs = 2500;

        readonly object _gate = new object();
        string _exe;
        bool _searched;
        bool _reportedMissing;
        // 第一次调用 nvidia-smi 常见的要好几秒（冷启动 + 驱动初始化）。
        // 放到启动 6 秒后再跑，避免把采样线程的首帧拖住，CPU / 内存 / GPU 先出数据。
        DateTime _nextTryUtc = DateTime.UtcNow.AddSeconds(6);
        NvidiaSample _current = new NvidiaSample();
        Task _probe;

        public NvidiaSample Current
        {
            get { lock (_gate) { return _current; } }
        }

        /// <summary>
        /// 采样线程每轮调用一次，必须立刻返回：这里只做节流和投递，
        /// 真正启动 nvidia-smi 放到后台任务（P1-8：nvidia-smi 卡住不能拖死整个采样线程）。
        /// </summary>
        public void Sample(int intervalMs)
        {
            lock (_gate)
            {
                if (DateTime.UtcNow < _nextTryUtc) return;
                _nextTryUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(500, intervalMs));

                // 上一轮还没回来（进程慢 / 已超时等待 Kill）就不叠加新进程。
                if (_probe != null && !_probe.IsCompleted) return;
                _probe = Task.Run(ProbeOnce);
            }
        }

        async Task ProbeOnce()
        {
            try
            {
                string exe = FindExe();
                if (exe == null)
                {
                    lock (_gate)
                    {
                        if (_current.Available)
                        {
                            _current = new NvidiaSample();
                            Log.Info("nvidia-smi 不再可用");
                        }
                    }
                    return;
                }

                AsyncProcess.Result result =
                    await AsyncProcess.RunAsync(exe, QueryArgs, ProbeTimeoutMs).ConfigureAwait(false);

                if (!result.Started) return;
                if (result.TimedOut)
                {
                    Log.Warn("nvidia-smi 超时（" + ProbeTimeoutMs + "ms），已结束进程");
                    return;
                }
                if (result.ExitCode != 0) return;

                NvidiaSample sample = Parse(result.StdOutText);
                if (sample != null)
                {
                    lock (_gate) { _current = sample; }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("nvidia-smi 调用失败: " + ex.Message);
            }
        }

        NvidiaSample Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string line = text.Split('\n')[0].Trim().Trim('\r');
            if (line.Length == 0) return null;

            string[] parts = line.Split(',');
            if (parts.Length < 3) return null;

            var sample = new NvidiaSample { Available = true, Name = parts[0].Trim() };
            if (parts.Length > 1) sample.Utilization = Num(parts[1]);
            if (parts.Length > 2) sample.VramUsedMb = Num(parts[2]);
            if (parts.Length > 3) sample.VramTotalMb = Num(parts[3]);
            if (parts.Length > 4) sample.TempC = Num(parts[4]);
            if (parts.Length > 5) sample.PowerW = Num(parts[5]);
            return sample;
        }

        static double Num(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return double.NaN;
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return double.NaN; // "[N/A]" 之类
        }

        string FindExe()
        {
            if (_searched) return _exe;
            _searched = true;

            try
            {
                string system32 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
                if (File.Exists(system32)) { _exe = system32; return _exe; }

                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in pathVar.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                    if (File.Exists(candidate)) { _exe = candidate; return _exe; }
                }
            }
            catch { }

            if (!_reportedMissing)
            {
                _reportedMissing = true;
                Log.Info("未找到 nvidia-smi，GPU 温度/显存/功耗不可用");
            }
            return null;
        }
    }
}
