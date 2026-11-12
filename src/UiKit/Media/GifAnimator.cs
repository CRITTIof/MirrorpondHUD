using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls.Media;

/// <summary>
/// 动图（GIF）播放器：用 GDI+ 逐帧取出已经合成好的画面（WPF 自带的解码器对局部帧处理不完整），
/// 再用 DispatcherTimer 按每帧自带的延时切换。只在卡片可见时运行，释放时停表、断引用。
/// </summary>
public sealed class GifAnimator : IDisposable
{
    Image _target;
    readonly List<BitmapSource> _frames = new();
    readonly List<int> _delays = new();
    DispatcherTimer _timer;
    readonly bool _loop;
    int _index;

    /// <summary>最近一次因为预算被跳过时的原因；UI 用它在卡片状态行提示。</summary>
    public static string LastSkipReason { get; private set; } = "";

    /// <summary>单张动图允许常驻的解码帧总预算（RGBA 估算）。</summary>
    const long MaxDecodedBytes = 64L * 1024 * 1024;

    /// <summary>降采样下限：低于它说明源图大得离谱，直接拒绝并提示，别再浪费解码时间。</summary>
    const double MinScale = 0.1;

    GifAnimator(bool loop)
    {
        _loop = loop;
    }

    public int FrameCount => _frames.Count;
    public Size CanvasSize { get; private set; }

    /// <summary>
    /// 绑定要显示的 Image。必须在 UI 线程调用：计时器在第一次 Play 时才创建，
    /// 这样后台解码（<see cref="TryLoad"/>）不会把 DispatcherTimer 建在线程池线程上。
    /// </summary>
    public void AttachTarget(Image target) => _target = target;

    /// <summary>读取动图。文件不是多帧 GIF 或读取失败时返回 null。可在后台线程调用。</summary>
    public static GifAnimator TryCreate(string path, Image target, bool loop)
    {
        if (target is null) return null;
        GifAnimator animator = TryLoad(path, loop);
        if (animator is not null) animator.AttachTarget(target);
        return animator;
    }

    /// <summary>
    /// K-12：只解一次 —— 旧实现先在 UI 线程用 BitmapDecoder 解首帧、再用 GDI+ 解全部帧，
    /// 等于两遍全尺寸解码。这里直接解帧，并按 64MB 预算降采样（而不是耗尽内存或直接失败）。
    /// 只允许在任意线程调用；返回的帧都已 Freeze，可安全跨线程。
    /// </summary>
    public static GifAnimator TryLoad(string path, bool loop)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        LastSkipReason = "";

        GifAnimator animator = null;
        System.Drawing.Bitmap source = null;
        try
        {
            if (!File.Exists(path)) return null;
            source = new System.Drawing.Bitmap(path);

            Guid[] dimensions = source.FrameDimensionsList;
            if (dimensions is null || dimensions.Length == 0) return null;

            var dimension = new FrameDimension(dimensions[0]);
            int count = source.GetFrameCount(dimension);
            if (count <= 1) return null;

            // 像素/内存预算：estimated = w × h × 4 × 帧数（解码后常驻的 RGBA 大小）。
            // 超预算时先算缩放比例，缩完再解，避免"解到一半 OOM"。
            double scale = 1.0;
            long estimated = (long)source.Width * source.Height * 4 * count;
            if (estimated > MaxDecodedBytes)
            {
                scale = Math.Sqrt((double)MaxDecodedBytes / estimated);
                if (scale < MinScale)
                {
                    LastSkipReason = $"动图约 {count} 帧 / {estimated / 1024.0 / 1024.0:0} MB，超过 64 MB 预算且需要缩到 {scale:0.###} 倍";
                    Log.Warn($"动图帧缓存超预算，退回只显示首帧: {LastSkipReason}");
                    return null;
                }

                Log.Warn($"动图约 {count} 帧 / {estimated / 1024.0 / 1024.0:0} MB，超过 64 MB 预算，已按 {scale:0.##} 比例降采样");
            }

            animator = new GifAnimator(loop);
            animator.CanvasSize = new Size(Math.Max(1, source.Width * scale), Math.Max(1, source.Height * scale));
            var delays = ReadDelays(source);

            for (int i = 0; i < count; i++)
            {
                source.SelectActiveFrame(dimension, i);
                BitmapSource frame = ToBitmapSource(source);
                if (frame is null) continue;

                if (scale < 0.999)
                {
                    // TransformedBitmap 延迟到渲染时才真正缩放，先 Freeze 固定下来，
                    // 否则后台线程持有的是 GDI+ 位图的视图。
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    scaled.Freeze();
                    frame = scaled;
                }

                animator._frames.Add(frame);
                animator._delays.Add(i < delays.Count ? delays[i] : 100);
            }

            if (animator._frames.Count <= 1)
            {
                animator.Dispose();
                return null;
            }

            return animator;
        }
        catch (Exception ex)
        {
            Log.Warn($"读取动图失败: {ex.Message}");
            if (animator is not null) animator.Dispose();
            return null;
        }
        finally
        {
            if (source is not null) source.Dispose();
        }
    }

    /// <summary>每帧延时（毫秒），来自 GIF 的 0x5100 属性（单位 1/100 秒）。</summary>
    static List<int> ReadDelays(System.Drawing.Bitmap bitmap)
    {
        var result = new List<int>();
        try
        {
            PropertyItem item = bitmap.GetPropertyItem(0x5100);
            if (item is not null && item.Value is not null)
            {
                for (int i = 0; i + 3 < item.Value.Length; i += 4)
                {
                    int hundredths = BitConverter.ToInt32(item.Value, i);
                    result.Add(Math.Max(20, hundredths * 10));
                }
            }
        }
        catch (Exception ex)
        {
            // 没有延时属性时按默认 100ms/帧；但读取失败原因要留痕（X-9）。
            Log.Warn($"读取动图帧延时失败，按默认 100ms/帧: {ex.Message}");
        }
        return result;
    }

    static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        IntPtr handle = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    public void Play()
    {
        if (_frames.Count == 0) return;
        if (_target is null) return;   // 还没绑定显示控件（TryLoad 出来的实例要先 AttachTarget）
        _index = 0;
        _target.Source = _frames[0];
        Schedule();
    }

    public void Stop() => _timer?.Stop();

    /// <summary>计时器在 UI 线程懒建：TryLoad 可能在后台线程完成，不能在那里 new DispatcherTimer。</summary>
    DispatcherTimer EnsureTimer()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render);
            _timer.Tick += (s, e) => Advance();
        }
        return _timer;
    }

    void Schedule()
    {
        if (_frames.Count == 0) return;
        int delay = _delays[Math.Max(0, Math.Min(_index, _delays.Count - 1))];
        DispatcherTimer timer = EnsureTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, delay));
        timer.Start();
    }

    void Advance()
    {
        if (_frames.Count == 0)
        {
            _timer?.Stop();
            return;
        }

        if (_target is null)
        {
            _timer?.Stop();
            return;
        }

        _index++;
        if (_index >= _frames.Count)
        {
            if (!_loop)
            {
                _timer?.Stop();
                _index = _frames.Count - 1;
                return;
            }
            _index = 0;
        }

        _target.Source = _frames[_index];
        Schedule();
    }

    public void Dispose()
    {
        // 释放路径：停表失败没有可补救动作，下面照样清空帧列表。
        try { _timer?.Stop(); } catch { }
        _frames.Clear();
        _delays.Clear();
        _target = null;
    }
}
