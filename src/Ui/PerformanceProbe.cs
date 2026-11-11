using System;
using System.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 性能采样开关与计数器（2.27.0）。只在 <c>--perf</c> 下开启，正常运行时所有计数方法
/// 都直接 return，不改变生产路径。
///
/// 目的：在优化 TextCanvas / GlassFeed 之前先拿到同一台机器上的真实基线：
/// · TextCanvas 每次 Measure / OnRender 重建了多少行、多少 FormattedText；
/// · GlassFeed 每帧新分配多少像素缓冲、其中多少会进 LOH；
/// · 采样窗口内的 GC 分配量与 Gen0/1/2 次数。
/// </summary>
internal static class PerformanceProbe
{
    static volatile bool _enabled;
    static DateTime _startUtc;
    static long _allocStart;
    static int _gen0Start;
    static int _gen1Start;
    static int _gen2Start;

    static long _textMeasures;
    static long _textRenders;
    static long _textLines;
    static long _textRuns;

    static long _glassFrames;
    static long _glassBuffers;
    static long _glassBufferBytes;
    static long _glassLoh;
    static long _glassBitmaps;

    public static bool Enabled => _enabled;

    public static void Begin()
    {
        _startUtc = DateTime.UtcNow;
        _allocStart = GC.GetTotalAllocatedBytes(false);
        _gen0Start = GC.CollectionCount(0);
        _gen1Start = GC.CollectionCount(1);
        _gen2Start = GC.CollectionCount(2);

        Interlocked.Exchange(ref _textMeasures, 0);
        Interlocked.Exchange(ref _textRenders, 0);
        Interlocked.Exchange(ref _textLines, 0);
        Interlocked.Exchange(ref _textRuns, 0);
        Interlocked.Exchange(ref _glassFrames, 0);
        Interlocked.Exchange(ref _glassBuffers, 0);
        Interlocked.Exchange(ref _glassBufferBytes, 0);
        Interlocked.Exchange(ref _glassLoh, 0);
        Interlocked.Exchange(ref _glassBitmaps, 0);

        _enabled = true;
        Log.Info("性能采样开始（仅 --perf 模式启用计数器）");
    }

    public static void End()
    {
        if (!_enabled) return;
        _enabled = false;

        double seconds = Math.Max(0.001, (DateTime.UtcNow - _startUtc).TotalSeconds);
        long allocated = Math.Max(0, GC.GetTotalAllocatedBytes(false) - _allocStart);
        long buffers = Interlocked.Read(ref _glassBuffers);
        long bufferBytes = Interlocked.Read(ref _glassBufferBytes);

        Log.Info($"=== 性能采样结果（{seconds:0.0} 秒）===");
        Log.Info($"TextCanvas: Measure={Interlocked.Read(ref _textMeasures)}，OnRender={Interlocked.Read(ref _textRenders)}，BuildLine={Interlocked.Read(ref _textLines)}，FormattedText run={Interlocked.Read(ref _textRuns)}");
        Log.Info($"GlassFeed: 取样轮={Interlocked.Read(ref _glassFrames)}，像素缓冲分配={buffers}（{bufferBytes / 1024.0 / 1024.0:0.00} MB，平均 {(buffers == 0 ? "0" : (bufferBytes / buffers / 1024.0).ToString("0.0"))} KB/次），LOH(≥85KB)={Interlocked.Read(ref _glassLoh)}，BitmapSource={Interlocked.Read(ref _glassBitmaps)}");
        Log.Info($"GC: 分配={allocated / 1024.0 / 1024.0:0.00} MB，Gen0={(GC.CollectionCount(0) - _gen0Start)}，Gen1={(GC.CollectionCount(1) - _gen1Start)}，Gen2={(GC.CollectionCount(2) - _gen2Start)}，WorkingSet={Environment.WorkingSet / 1024.0 / 1024.0:0.0} MB");
        Log.Info("性能采样结束");
    }

    internal static void TextMeasure()
    {
        if (_enabled) Interlocked.Increment(ref _textMeasures);
    }

    internal static void TextRender()
    {
        if (_enabled) Interlocked.Increment(ref _textRenders);
    }

    internal static void TextLine()
    {
        if (_enabled) Interlocked.Increment(ref _textLines);
    }

    internal static void TextRun()
    {
        if (_enabled) Interlocked.Increment(ref _textRuns);
    }

    internal static void GlassFrame()
    {
        if (_enabled) Interlocked.Increment(ref _glassFrames);
    }

    internal static void GlassBuffer(int bytes)
    {
        if (!_enabled) return;
        Interlocked.Increment(ref _glassBuffers);
        Interlocked.Add(ref _glassBufferBytes, bytes);
        if (bytes >= 85 * 1024) Interlocked.Increment(ref _glassLoh);
    }

    internal static void GlassBitmap()
    {
        if (_enabled) Interlocked.Increment(ref _glassBitmaps);
    }
}
