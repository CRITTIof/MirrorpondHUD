using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MirrorpondHUD.Core
{
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
