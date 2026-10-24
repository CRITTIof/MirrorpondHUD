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
    ///   ffmpeg -readrate 1 -i 文件 -map 0:v:0 -an -vf scale=W:H -r 帧率 -f rawvideo -pix_fmt bgra pipe:1
    ///
    /// 自己读 stdout，每 W×H×4 字节包成一张 BitmapSource。暂停 = 不再读管道
    /// （管道写满后 ffmpeg 自己会停下），继续 = 接着读；循环 = 读完从头再来。
    ///
    /// 声音走第二条 ffmpeg 进程：只取音频、转成 48 kHz 立体声 16 bit PCM，
    /// 交给 <see cref="WaveOutPlayer"/>（winmm waveOut）播放。`-readrate` 控制视频速度，
    /// `atempo` 控制音频速度，两边同时重启即可实现快进 / 快退 / 变速。
    /// </summary>
    internal sealed class FfmpegPlayer : IDisposable
    {
        public event Action<BitmapSource> FrameReady;
        public event Action<string> Failed;
        public event Action Completed;

        readonly ManualResetEventSlim _resume = new ManualResetEventSlim(true);

        Process _process;
        Process _audioProcess;
        Thread _reader;
        WaveOutPlayer _audio;

        string _ffmpeg = "";
        string _path = "";
        int _width;
        int _height;
        double _fps = 25;
        double _duration;
        double _speed = 1;
        double _startSeconds;
        int _volume = 100;
        long _frames;
        bool _loop;
        bool _audioEnabled;
        volatile bool _stop;
        volatile bool _startPaused;
        bool _disposed;
        bool _readrateRetried;

        public double Duration { get { return _duration; } }

        public double Position
        {
            get
            {
                double position = _startSeconds + (_fps > 0 ? _frames / _fps : 0);
                return _duration > 0.01 ? Math.Min(_duration, position) : position;
            }
        }

        public bool IsRunning { get { return _process != null && !_stop; } }
        public bool IsPaused { get { return !_resume.IsSet; } }
        public double Speed { get { return _speed; } }

        // ==================== 播放 ====================

        /// <summary>开始（或换一个文件）播放；参数里的尺寸是解码尺寸。</summary>
        public void Start(string ffmpeg, string path, int width, int height,
                          double fps, bool loop, double duration, bool autoplay,
                          double speed, double startSeconds, bool audio, int volume)
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
            _readrateRetried = false;
            _speed = ClampSpeed(speed);
            _startSeconds = Math.Max(0, startSeconds);
            _audioEnabled = audio;
            _volume = Math.Max(0, Math.Min(100, volume));
            _startPaused = !autoplay;
            _resume.Set();

            try
            {
                LaunchAll();
            }
            catch (Exception ex)
            {
                Log.Warn("启动 ffmpeg 失败: " + ex.Message);
                RaiseFailed(ex.Message);
                return;
            }

            StartReader();
        }

        void StartReader()
        {
            _reader = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "MirrorpondHUD.ffmpeg"
            };
            _reader.Start();
        }

        void LaunchAll()
        {
            LaunchVideo();
            LaunchAudio();
        }

        void LaunchVideo()
        {
            var psi = new ProcessStartInfo(_ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            AddInput(psi);
            psi.ArgumentList.Add("-readrate");
            psi.ArgumentList.Add(_speed.ToString("0.###", CultureInfo.InvariantCulture));
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

        void LaunchAudio()
        {
            if (!_audioEnabled || _volume <= 0) return;

            try
            {
                var psi = new ProcessStartInfo(_ffmpeg)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                AddInput(psi);
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(_path);
                psi.ArgumentList.Add("-vn");
                psi.ArgumentList.Add("-sn");
                psi.ArgumentList.Add("-dn");

                string tempo = Atempo(_speed);
                if (tempo != null)
                {
                    psi.ArgumentList.Add("-af");
                    psi.ArgumentList.Add(tempo);
                }

                psi.ArgumentList.Add("-ac");
                psi.ArgumentList.Add("2");
                psi.ArgumentList.Add("-ar");
                psi.ArgumentList.Add("48000");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("s16le");
                psi.ArgumentList.Add("pipe:1");

                Process process = Process.Start(psi);
                if (process == null) return;

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e != null && !string.IsNullOrWhiteSpace(e.Data))
                        Log.Warn("ffmpeg(音频): " + e.Data.Trim());
                };
                process.BeginErrorReadLine();

                _audioProcess = process;

                _audio = new WaveOutPlayer { Volume = _volume };
                if (!_audio.Start(process.StandardOutput.BaseStream))
                {
                    Log.Warn("音频输出启动失败，继续静音播放");
                    StopAudio();
                }
                else
                {
                    Log.Info("音频输出已启动（48 kHz 立体声，音量 " + _volume + "%）");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("启动音频解码失败: " + ex.Message);
                StopAudio();
            }
        }

        void AddInput(ProcessStartInfo psi)
        {
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-nostdin");

            if (_startSeconds > 0.01)
            {
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(_startSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>把倍速拆成 atempo 链（单次 atempo 只支持 0.5–2.0）。</summary>
        static string Atempo(double speed)
        {
            if (Math.Abs(speed - 1) < 0.001) return null;

            var parts = new System.Collections.Generic.List<string>();
            double value = speed;

            while (value > 2.0001)
            {
                parts.Add("atempo=2.0");
                value /= 2.0;
            }
            while (value < 0.4999)
            {
                parts.Add("atempo=0.5");
                value /= 0.5;
            }

            parts.Add("atempo=" + value.ToString("0.###", CultureInfo.InvariantCulture));
            return string.Join(",", parts);
        }

        static double ClampSpeed(double speed)
        {
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0) return 1;
            return Math.Max(0.25, Math.Min(4.0, speed));
        }

        // ==================== 读帧 ====================

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

                        // 一帧都没解出来：可能是老版本 ffmpeg 不认 -readrate，退回 -re 再试一次。
                        if (framesSinceLaunch == 0 && !_readrateRetried)
                        {
                            _readrateRetried = true;
                            if (Math.Abs(_speed - 1) > 0.001)
                            {
                                Log.Warn("ffmpeg 不支持 -readrate，改用 -re 并以 1× 播放");
                                _speed = 1;
                                LoopRestart(0);
                                continue;
                            }
                        }

                        // 一帧都没解出来 = 文件打不开 / 格式不支持，再重开也是死循环。
                        if (framesSinceLaunch == 0)
                        {
                            RaiseFailed("ffmpeg 没有解出画面");
                            break;
                        }

                        if (_loop)
                        {
                            LoopRestart(0);
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
                        PauseAudio();
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

        /// <summary>读线程内部的重开：不 join 自己，只换进程。</summary>
        void LoopRestart(double seconds)
        {
            StopAudio();
            KillProcess();

            _startSeconds = Math.Max(0, seconds);
            _frames = 0;

            try
            {
                LaunchVideo();
                LaunchAudio();
            }
            catch (Exception ex)
            {
                Log.Warn("ffmpeg 重新打开失败: " + ex.Message);
                RaiseFailed(ex.Message);
            }
        }

        /// <summary>从指定秒数重新开始（快进 / 快退 / 变速用）。</summary>
        void RestartAt(double seconds)
        {
            bool paused = IsPaused;

            StopInternal();

            _stop = false;
            _startSeconds = Math.Max(0, seconds);
            _frames = 0;
            _startPaused = paused;
            _resume.Set();

            try
            {
                LaunchAll();
            }
            catch (Exception ex)
            {
                Log.Warn("ffmpeg 重新打开失败: " + ex.Message);
                RaiseFailed(ex.Message);
                return;
            }

            StartReader();
        }

        // ==================== 控制 ====================

        public void Seek(double seconds)
        {
            if (_disposed || _stop) return;

            double target = Math.Max(0, seconds);
            if (_duration > 0.01) target = Math.Min(_duration - 0.05, target);
            RestartAt(target);
        }

        /// <summary>相对当前位置快进 / 快退（负数后退）。</summary>
        public void SeekBy(double seconds)
        {
            if (_disposed || _stop) return;
            Seek(Position + seconds);
        }

        public void SetSpeed(double speed)
        {
            if (_disposed || _stop) return;

            double next = ClampSpeed(speed);
            if (Math.Abs(next - _speed) < 0.001) return;

            double position = Position;
            _speed = next;
            Log.Info("ffmpeg 播放速度：" + next.ToString("0.##") + "×");
            RestartAt(position);
        }

        public void Pause()
        {
            _resume.Reset();
            PauseAudio();
        }

        public void Resume()
        {
            if (_disposed || _stop) return;
            _resume.Set();
            ResumeAudio();
        }

        void PauseAudio()
        {
            try { if (_audio != null) _audio.Pause(); } catch { }
        }

        void ResumeAudio()
        {
            try { if (_audio != null) _audio.Resume(); } catch { }
        }

        void StopAudio()
        {
            Process process = _audioProcess;
            _audioProcess = null;
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
            }

            WaveOutPlayer audio = _audio;
            _audio = null;
            if (audio != null)
            {
                try { audio.Stop(); } catch { }
                try { audio.Dispose(); } catch { }
            }
        }

        void KillProcess()
        {
            Process process = _process;
            _process = null;
            if (process == null) return;

            try { if (!process.HasExited) process.Kill(); } catch { }
            try { process.Dispose(); } catch { }
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

        void StopInternal()
        {
            _stop = true;
            _resume.Set();

            StopAudio();
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
