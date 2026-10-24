using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Media
{
    /// <summary>一个视频的基本信息（ffmpeg 探测出来）。</summary>
    internal sealed class VideoInfo
    {
        public double Duration;
        public int Width;
        public int Height;
        public double Fps;
    }

    /// <summary>
    /// 用外部 ffmpeg 解码视频，逐帧交给 WPF 显示。
    ///
    /// 为什么需要它：卡片里的视频默认走 Windows 的 Media Foundation（MediaElement），
    /// mp4 / wmv 一般能放，mkv / webm / av1 / 部分 hevc 就放不了，而装解码包也不一定
    /// 对 Media Foundation 生效。这里改成直接问 ffmpeg 要 BGRA 原始帧：
    ///
    ///   ffmpeg -re -i 文件 -an -vf scale=W:H -r 帧率 -f rawvideo -pix_fmt bgra pipe:1
    ///
    /// 自己读 stdout，每 W×H×4 字节包成一张 BitmapSource。暂停 = 不再读管道
    /// （管道写满后 ffmpeg 自己会停下），继续 = 接着读；循环 = 读完从头再来。
    /// 代价是没有声音（HUD 场景默认静音），播放速度固定为 1×。
    /// </summary>
    internal sealed class FfmpegPlayer : IDisposable
    {
        public event Action<BitmapSource> FrameReady;
        public event Action<string> Failed;
        public event Action Completed;

        readonly ManualResetEventSlim _resume = new ManualResetEventSlim(true);

        Process _process;
        Thread _reader;
        string _ffmpeg = "";
        string _path = "";
        int _width;
        int _height;
        double _fps = 25;
        double _duration;
        long _frames;
        bool _loop;
        volatile bool _stop;
        volatile bool _startPaused;
        bool _disposed;

        public double Duration { get { return _duration; } }
        public double Position { get { return _fps > 0 ? _frames / _fps : 0; } }
        public bool IsRunning { get { return _process != null && !_stop; } }

        // ==================== 播放 ====================

        /// <summary>开始（或换一个文件）播放；参数里的尺寸是解码尺寸。</summary>
        public void Start(string ffmpeg, string path, int width, int height,
                          double fps, bool loop, double duration, bool autoplay)
        {
            StopInternal();

            _ffmpeg = ffmpeg;
            _path = path;
            _width = Math.Max(16, width - width % 2);
            _height = Math.Max(16, height - height % 2);
            _fps = fps >= 1 && fps <= 121 ? fps : 25;
            _duration = duration;
            _loop = loop;
            _frames = 0;
            _stop = false;
            _startPaused = !autoplay;
            _resume.Set();

            try
            {
                Launch();
            }
            catch (Exception ex)
            {
                Log.Warn("启动 ffmpeg 失败: " + ex.Message);
                RaiseFailed(ex.Message);
                return;
            }

            _reader = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "MirrorpondHUD.ffmpeg"
            };
            _reader.Start();
        }

        void Launch()
        {
            var psi = new ProcessStartInfo(_ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-re");            // 按片源速度读，画面才不会一秒钟播完
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(_path);
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:v:0");
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-sn");
            psi.ArgumentList.Add("-dn");
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add("scale=" + _width + ":" + _height +
                                 ":force_original_aspect_ratio=decrease");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(_fps.ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("rawvideo");
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("bgra");
            psi.ArgumentList.Add("pipe:1");

            Process process = Process.Start(psi);
            if (process == null) throw new InvalidOperationException("ffmpeg 没有启动");

            process.ErrorDataReceived += (s, e) =>
            {
                if (e != null && !string.IsNullOrWhiteSpace(e.Data))
                    Log.Warn("ffmpeg: " + e.Data.Trim());
            };
            process.BeginErrorReadLine();

            _process = process;
        }

        void ReadLoop()
        {
            try
            {
                int size = _width * _height * 4;
                var buffer = new byte[size];
                long framesSinceLaunch = 0;

                while (!_stop)
                {
                    _resume.Wait();
                    if (_stop) break;

                    Process process = _process;
                    if (process == null) break;

                    Stream stream = process.StandardOutput.BaseStream;
                    if (!ReadFull(stream, buffer))
                    {
                        if (_stop) break;

                        // 一帧都没解出来 = 文件打不开 / 格式不支持，再重开也是死循环。
                        if (framesSinceLaunch == 0)
                        {
                            RaiseFailed("ffmpeg 没有解出画面");
                            break;
                        }

                        if (_loop)
                        {
                            Restart();
                            framesSinceLaunch = 0;
                            continue;
                        }

                        Action completed = Completed;
                        if (completed != null)
                        {
                            try { completed(); } catch { }
                        }
                        break;
                    }

                    framesSinceLaunch++;

                    var frame = BitmapSource.Create(_width, _height, 96, 96,
                        PixelFormats.Bgra32, null, buffer, _width * 4);
                    frame.Freeze();

                    Interlocked.Increment(ref _frames);

                    Action<BitmapSource> handler = FrameReady;
                    if (handler != null)
                    {
                        try { handler(frame); }
                        catch (Exception ex) { Log.Warn("显示视频帧失败: " + ex.Message); }
                    }

                    if (_startPaused)
                    {
                        _startPaused = false;
                        _resume.Reset();
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_stop)
                {
                    Log.Warn("ffmpeg 解码线程结束: " + ex.Message);
                    RaiseFailed(ex.Message);
                }
            }
        }

        void Restart()
        {
            try
            {
                KillProcess();
                _frames = 0;
                Launch();
            }
            catch (Exception ex)
            {
                Log.Warn("ffmpeg 重新打开失败: " + ex.Message);
                RaiseFailed(ex.Message);
            }
        }

        static bool ReadFull(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        public void Pause()
        {
            _resume.Reset();
        }

        public void Resume()
        {
            if (_disposed || _stop) return;
            _resume.Set();
        }

        void RaiseFailed(string message)
        {
            Action<string> handler = Failed;
            if (handler != null)
            {
                try { handler(message); } catch { }
            }
        }

        // ==================== 探测 ====================

        /// <summary>
        /// 问 ffmpeg 要时长 / 分辨率 / 帧率：`ffmpeg -i 文件`（不带输出）会把信息打到 stderr
        /// 然后报「没有输出文件」退出，正好拿来读。
        /// </summary>
        public static VideoInfo Probe(string ffmpeg, string path)
        {
            var info = new VideoInfo { Fps = 25, Width = 0, Height = 0, Duration = 0 };

            try
            {
                var psi = new ProcessStartInfo(ffmpeg)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(path);

                using (var process = Process.Start(psi))
                {
                    if (process == null) return info;

                    string error = process.StandardError.ReadToEnd();
                    string output = process.StandardOutput.ReadToEnd();

                    if (!process.WaitForExit(4000))
                    {
                        try { process.Kill(); } catch { }
                    }

                    info = Parse(error.Length > 0 ? error : output);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("ffmpeg 探测失败: " + ex.Message);
            }

            return info;
        }

        static readonly Regex DurationRegex = new Regex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)",
            RegexOptions.CultureInvariant);

        static readonly Regex SizeRegex = new Regex(@"(\d{2,5})x(\d{2,5})",
            RegexOptions.CultureInvariant);

        static readonly Regex FpsRegex = new Regex(@"(\d+(?:\.\d+)?)\s*fps",
            RegexOptions.CultureInvariant);

        static VideoInfo Parse(string text)
        {
            var info = new VideoInfo { Fps = 25 };

            if (string.IsNullOrEmpty(text)) return info;

            Match duration = DurationRegex.Match(text);
            if (duration.Success)
            {
                double hours = double.Parse(duration.Groups[1].Value, CultureInfo.InvariantCulture);
                double minutes = double.Parse(duration.Groups[2].Value, CultureInfo.InvariantCulture);
                double seconds = double.Parse(duration.Groups[3].Value, CultureInfo.InvariantCulture);
                info.Duration = hours * 3600 + minutes * 60 + seconds;
            }

            foreach (string line in text.Split('\n'))
            {
                if (line.IndexOf("Video:", StringComparison.OrdinalIgnoreCase) < 0) continue;

                Match size = SizeRegex.Match(line);
                if (size.Success && info.Width == 0)
                {
                    info.Width = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
                    info.Height = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
                }

                Match fps = FpsRegex.Match(line);
                if (fps.Success)
                {
                    double value;
                    if (double.TryParse(fps.Groups[1].Value, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out value) &&
                        value >= 1 && value <= 121)
                        info.Fps = value;
                }

                if (info.Width > 0) break;
            }

            return info;
        }

        /// <summary>
        /// 用 ffmpeg 把一个「WPF 解不开」的图片（webp / avif / heic …）转成一张位图。
        /// 失败返回 null，调用方照常显示「无法解码」。
        /// </summary>
        public static BitmapSource ExtractImage(string ffmpeg, string path, double seconds = 0)
        {
            try
            {
                var psi = new ProcessStartInfo(ffmpeg)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-loglevel");
                psi.ArgumentList.Add("error");
                if (seconds > 0.01)
                {
                    psi.ArgumentList.Add("-ss");
                    psi.ArgumentList.Add(seconds.ToString("0.###", CultureInfo.InvariantCulture));
                }
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(path);
                psi.ArgumentList.Add("-frames:v");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("image2pipe");
                psi.ArgumentList.Add("-vcodec");
                psi.ArgumentList.Add("png");
                psi.ArgumentList.Add("pipe:1");

                using (var process = Process.Start(psi))
                {
                    if (process == null) return null;

                    using (var memory = new MemoryStream())
                    {
                        // 先读 stdout（PNG 可能几 MB，管道写满会把 ffmpeg 卡住），再读 stderr。
                        process.StandardOutput.BaseStream.CopyTo(memory);
                        process.StandardError.ReadToEnd();

                        if (!process.WaitForExit(6000))
                        {
                            try { process.Kill(); } catch { }
                        }

                        if (memory.Length == 0) return null;

                        memory.Position = 0;
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = memory;
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("ffmpeg 解图失败: " + ex.Message);
                return null;
            }
        }

        // ==================== 释放 ====================

        void KillProcess()
        {
            Process process = _process;
            _process = null;
            if (process == null) return;

            try { if (!process.HasExited) process.Kill(); } catch { }
            try { process.Dispose(); } catch { }
        }

        void StopInternal()
        {
            _stop = true;
            _resume.Set();

            KillProcess();

            Thread reader = _reader;
            _reader = null;
            if (reader != null)
            {
                try { reader.Join(1500); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopInternal();
        }
    }
}
