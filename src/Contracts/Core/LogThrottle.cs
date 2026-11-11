using System;

namespace MirrorpondHUD.Core;

/// <summary>
/// 周期性循环里的失败日志节流器（P1-9 配套小工具）。
///
/// PDH 查询、nvidia-smi、桌面取图这类**每秒跑一次**的循环，失败时如果每轮都写日志，
/// 会把日志文件瞬间刷满（而且重复内容没有信息量）。这里提供两层节流：
///
///  · 状态转移：从正常 → 失败记一条 Warn，失败 → 恢复记一条 Info（带累计失败次数）；
///  · 最小间隔：同一个失败状态持续期间，每隔 <paramref name="minIntervalMs"/> 才再记
///    一条「仍失败」，保证长时间坏着也能周知、又不刷屏。
///
/// 线程安全：Failure / Success 可在任意线程打点（内部一把小锁）。
/// </summary>
public sealed class LogThrottle
{
    readonly string _label;
    readonly double _minIntervalMs;
    readonly object _sync = new();

    bool _failing;
    long _lastLogTicks;
    int _failuresSinceLog;

    /// <summary>打点周期：正常→失败时立即记录；持续失败期间以该间隔记录「仍失败」。</summary>
    public double MinIntervalMs => _minIntervalMs;

    public LogThrottle(string label, double minIntervalMs = 0)
    {
        _label = label ?? "节流日志";
        _minIntervalMs = Math.Max(0, minIntervalMs);
    }

    /// <summary>
    /// 循环里的失败打点：从正常转进失败记一条 WARN；失败持续期间每过最小间隔
    /// 再记一条「仍失败」（带已累计的周期数），避免长时间坏着却毫无声响。
    /// </summary>
    public void Failure(string reason)
    {
        long now = DateTime.UtcNow.Ticks;
        bool first;
        bool logNow;
        int count;

        lock (_sync)
        {
            first = !_failing;
            _failing = true;
            _failuresSinceLog++;
            count = _failuresSinceLog;

            logNow = first || MinIntervalElapsed(now);
            if (logNow) _lastLogTicks = now;
        }

        if (!logNow) return;
        if (first)
            Log.Warn($"{_label} 失败: {(reason ?? "")}");
        else
            Log.Warn($"{_label} 仍失败（已连续 {count} 个周期）: {(reason ?? "")}");
    }

    /// <summary>循环里的成功打点：从失败恢复时记一条 INFO，带失败期间累计的周期数。</summary>
    public void Success()
    {
        int count;
        lock (_sync)
        {
            if (!_failing) return;
            count = _failuresSinceLog;
            _failuresSinceLog = 0;
            _failing = false;
        }
        Log.Info($"{_label} 已恢复正常（恢复前失败 {count} 个周期）");
    }

    bool MinIntervalElapsed(long nowTicks) => _minIntervalMs <= 0 ||
               (nowTicks - _lastLogTicks) / TimeSpan.TicksPerMillisecond >= _minIntervalMs;
}
