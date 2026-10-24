using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Media
{
    /// <summary>
    /// 动图（GIF）播放器：用 GDI+ 逐帧取出已经合成好的画面（WPF 自带的解码器对局部帧处理不完整），
    /// 再用 DispatcherTimer 按每帧自带的延时切换。只在卡片可见时运行，释放时停表、断引用。
    /// </summary>
    public sealed class GifAnimator : IDisposable
    {
        readonly Image _target;
        readonly List<BitmapSource> _frames = new List<BitmapSource>();
        readonly List<int> _delays = new List<int>();
        readonly DispatcherTimer _timer;
        readonly bool _loop;
        int _index;

        GifAnimator(Image target, bool loop)
        {
            _target = target;
            _loop = loop;
            _timer = new DispatcherTimer(DispatcherPriority.Render);
            _timer.Tick += (s, e) => Advance();
        }

        public int FrameCount { get { return _frames.Count; } }
        public Size CanvasSize { get; private set; }

        /// <summary>读取动图。文件不是多帧 GIF 或读取失败时返回 null。</summary>
        public static GifAnimator TryCreate(string path, Image target, bool loop)
        {
            if (string.IsNullOrWhiteSpace(path) || target == null) return null;

            GifAnimator animator = null;
            System.Drawing.Bitmap source = null;
            try
            {
                if (!File.Exists(path)) return null;
                source = new System.Drawing.Bitmap(path);

                Guid[] dimensions = source.FrameDimensionsList;
                if (dimensions == null || dimensions.Length == 0) return null;

                var dimension = new FrameDimension(dimensions[0]);
                int count = source.GetFrameCount(dimension);
                if (count <= 1) return null;

                animator = new GifAnimator(target, loop);
                animator.CanvasSize = new Size(source.Width, source.Height);
                var delays = ReadDelays(source);

                for (int i = 0; i < count; i++)
                {
                    source.SelectActiveFrame(dimension, i);
                    BitmapSource frame = ToBitmapSource(source);
                    if (frame == null) continue;
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
                Log.Warn("读取动图失败: " + ex.Message);
                if (animator != null) animator.Dispose();
                return null;
            }
            finally
            {
                if (source != null) source.Dispose();
            }
        }

        /// <summary>每帧延时（毫秒），来自 GIF 的 0x5100 属性（单位 1/100 秒）。</summary>
        static List<int> ReadDelays(System.Drawing.Bitmap bitmap)
        {
            var result = new List<int>();
            try
            {
                PropertyItem item = bitmap.GetPropertyItem(0x5100);
                if (item != null && item.Value != null)
                {
                    for (int i = 0; i + 3 < item.Value.Length; i += 4)
                    {
                        int hundredths = BitConverter.ToInt32(item.Value, i);
                        result.Add(Math.Max(20, hundredths * 10));
                    }
                }
            }
            catch { }
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
            _index = 0;
            _target.Source = _frames[0];
            Schedule();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        void Schedule()
        {
            if (_frames.Count == 0) return;
            int delay = _delays[Math.Max(0, Math.Min(_index, _delays.Count - 1))];
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, delay));
            _timer.Start();
        }

        void Advance()
        {
            if (_frames.Count == 0)
            {
                _timer.Stop();
                return;
            }

            _index++;
            if (_index >= _frames.Count)
            {
                if (!_loop)
                {
                    _timer.Stop();
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
            try { _timer.Stop(); } catch { }
            _frames.Clear();
            _delays.Clear();
        }
    }
}
