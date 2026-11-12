using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MirrorpondHUD.Core;

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

    readonly object _gate = new();
    string _exe;
    bool _searched;
    bool _reportedMissing;
    // 第一次调用 nvidia-smi 常见的要好几秒（冷启动 + 驱动初始化）。
    // 放到启动 6 秒后再跑，避免把采样线程的首帧拖住，CPU / 内存 / GPU 先出数据。
    DateTime _nextTryUtc = DateTime.UtcNow.AddSeconds(6);
    NvidiaSample _current = new();
    Task _probe;
    // P-18：多卡时按设置里的 GpuIndex 选行；-1 = 汇总（保持旧行为，取第一张）。
    int _gpuIndex = -1;
    bool _indexWarned;

    // §6：失败日志节流（状态转移记一条 + 60s 最小间隔的「仍失败」，恢复时记一条）。
    readonly LogThrottle _probeThrottle = new("nvidia-smi 采样", 60000);

    public int GpuIndex
    {
        get { lock (_gate) { return _gpuIndex; } }
        set { lock (_gate) { _gpuIndex = value; _indexWarned = false; } }
    }

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
            if (_probe is not null && !_probe.IsCompleted) return;
            _probe = Task.Run(ProbeOnce);
        }
    }

    async Task ProbeOnce()
    {
        try
        {
            string exe = FindExe();
            if (exe is null)
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

            if (!result.Started)
            {
                // §6：以前完全静默——「为什么 GPU 指标一直是空的」无从排查。
                // 采样每 0.5s+ 一轮，经节流器只记状态转移与低频的「仍失败」。
                _probeThrottle.Failure($"nvidia-smi 没有启动（路径 {exe}）");
                return;
            }
            if (result.TimedOut)
            {
                Log.Warn($"nvidia-smi 超时（{ProbeTimeoutMs}ms），已结束进程");
                return;
            }
            if (result.ExitCode != 0)
            {
                _probeThrottle.Failure($"nvidia-smi 退出码 {result.ExitCode}{(string.IsNullOrEmpty(result.StdErr)
                                           ? ""
                                           : $"  stderr={ClipText(result.StdErr, 120)}")}");
                return;
            }

            NvidiaSample sample = Parse(result.StdOutText);
            if (sample is not null)
            {
                lock (_gate) { _current = sample; }
                // 有数据了：正常路径打点（失败→恢复会记一条 Info，平时无输出）。
                _probeThrottle.Success();
            }
            else
            {
                _probeThrottle.Failure($"nvidia-smi 输出无法解析: {ClipText(result.StdOutText, 120)}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"nvidia-smi 调用失败: {ex.Message}");
        }
    }

    NvidiaSample Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // P-18：按 GpuIndex 选择对应的 nvidia-smi 行；越界时回落第一行并告警一次。
        string[] lines = text.Split('\n');
        int index = _gpuIndex;
        if (index < 0 || index >= lines.Length)
        {
            if (index >= 0 && !_indexWarned)
            {
                _indexWarned = true;
                Log.Warn($"nvidia-smi 没有第 {index} 张卡（实际 {lines.Length} 行），已回落第一张");
            }
            index = 0;
        }

        string line = lines[index].Trim().Trim('\r');
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
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        return double.NaN; // "[N/A]" 之类
    }

    /// <summary>把进程输出压成单行并限长，留日志诊断用。</summary>
    static string ClipText(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (flat.Length <= max) return flat;
        return $"{flat.Substring(0, max)}…";
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
        catch (Exception ex)
        {
            // PATH 里没有 / 读不了只影响能否自动发现 nvidia-smi，仍要留下原因。
            Log.Warn($"扫描 PATH 查找 nvidia-smi 失败: {ex.Message}");
        }

        if (!_reportedMissing)
        {
            _reportedMissing = true;
            Log.Info("未找到 nvidia-smi，GPU 温度/显存/功耗不可用");
        }
        return null;
    }
}
