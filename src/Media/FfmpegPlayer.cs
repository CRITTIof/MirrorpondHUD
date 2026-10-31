using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

        // P2-18：_stop 原先兼作“重启代号”。RestartAt 的 Join(1500) 超时后旧读线程晚归，
        // 会看到 _stop 已被清掉继续跑，还可能 Kill 掉新一代的进程、或对新一代报假失败。
        // 现在每次 Start / RestartAt 都 ++_run；读线程全程用自己捕获的 run 校验，
        // 所有进程 / 音频播放器的安装与 Kill 都在 _sync 里校验代次，旧代晚归直接作废。
        readonly object _sync = new object();
        int _run;

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
                double start;
                long frames;
                lock (_sync)
                {
                    start = _startSeconds;
                    frames = _frames;
                }

                double position = start + (_fps > 0 ? frames / _fps : 0);
                return _duration > 0.01 ? Math.Min(_duration, position) : position;
            }
        }

        public bool IsRunning
        {
            get { lock (_sync) { return _process != null && !_stop; } }
        }
        public bool IsPaused { get { return !_resume.IsSet; } }
        public double Speed { get { return _speed; } }

        // ==================== 播放 ====================

        /// <summary>开始（或换一个文件）播放；参数里的尺寸是解码尺寸。</summary>
        public void Start(string ffmpeg, string path, int width, int height,
                          double fps, bool loop, double duration, bool autoplay,
                          double speed, double startSeconds, bool audio, int volume)
        {
            StopInternal();

            double startAt = Math.Max(0, startSeconds);

            _ffmpeg = ffmpeg;
            _path = path;
            _width = Math.Max(16, width - width % 2);
            _height = Math.Max(16, height - height % 2);
            _fps = fps >= 1 && fps <= 121 ? fps : 25;
            _duration = duration;
            _loop = loop;
            _readrateRetried = false;
            _speed = ClampSpeed(speed);
            _audioEnabled = audio;
            _volume = Math.Max(0, Math.Min(100, volume));

            // 新一轮播放：++代次并重置本代状态（旧读线程 / 旧进程由此失效）。
            int run = BeginRun(startAt, !autoplay);
            _stop = false;
            _resume.Set();

            try
            {
                LaunchAll(run, startAt);
            }
            catch (Exception ex)
            {
                Log.Warn("启动 ffmpeg 失败: " + ex.Message);
                RaiseFailed(ex.Message);
                return;
            }

            StartReader(run);
        }

        /// <summary>开始一代新播放：递增代次，重置本代的起始位置 / 帧计数 / 起播暂停标记。</summary>
        int BeginRun(double startSeconds, bool paused)
        {
            lock (_sync)
            {
                _run++;
                _startSeconds = Math.Max(0, startSeconds);
                _frames = 0;
                _startPaused = paused;
                return _run;
            }
        }

        bool IsCurrent(int run)
        {
            return run == Volatile.Read(ref _run);
        }

        void StartReader(int run)
        {
            Thread reader = new Thread(() => ReadLoop(run))
            {
                IsBackground = true,
                Name = "MirrorpondHUD.ffmpeg"
            };
            _reader = reader;
            reader.Start();
        }

        void LaunchAll(int run, double startSeconds)
        {
            LaunchVideo(run, startSeconds);
            LaunchAudio(run, startSeconds);
        }

        void LaunchVideo(int run, double startSeconds)
        {
            var psi = new ProcessStartInfo(_ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            AddInput(psi, startSeconds);
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

            // 代次校验 + 换手都在 _sync 里：旧代的启动不能把新代进程顶掉（P2-18）。
            Process replaced = null;
            bool current;
            lock (_sync)
            {
                current = run == _run;
                if (current)
                {
                    replaced = _process;
                    _process = process;
                }
            }

            if (!current)
            {
                // 这一代在启动过程中已经被作废：新进程直接收掉，别污染当前状态。
                DisposeProcess(process);
                return;
            }

            DisposeProcess(replaced);
        }

        void LaunchAudio(int run, double startSeconds)
        {
            if (!_audioEnabled || _volume <= 0) return;

            Process process = null;
            WaveOutPlayer player = null;
            try
            {
                var psi = new ProcessStartInfo(_ffmpeg)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                AddInput(psi, startSeconds);
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

                process = Process.Start(psi);
                if (process == null) return;

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e != null && !string.IsNullOrWhiteSpace(e.Data))
                        Log.Warn("ffmpeg(音频): " + e.Data.Trim());
                };
                process.BeginErrorReadLine();

                // 安装音频解码进程（代次校验，P2-18）
                Process replacedProcess = null;
                bool current;
                lock (_sync)
                {
                    current = run == _run;
                    if (current)
                    {
                        replacedProcess = _audioProcess;
                        _audioProcess = process;
                    }
                }

                if (!current)
                {
                    DisposeProcess(process);
                    return;
                }
                DisposeProcess(replacedProcess);

                player = new WaveOutPlayer { Volume = _volume };
                if (!player.Start(process.StandardOutput.BaseStream))
                {
                    Log.Warn("音频输出启动失败，继续静音播放");
                    try { player.Dispose(); } catch { }
                    player = null;
                    StopAudio(run);
                    return;
                }

                // 安装播放器：启动期间可能又被重启 / 停止
                WaveOutPlayer replacedAudio = null;
                bool installed;
                lock (_sync)
                {
                    installed = run == _run;
                    if (installed)
                    {
                        replacedAudio = _audio;
                        _audio = player;
                    }
                }

                if (!installed)
                {
                    // 这一代已被作废：刚起来的播放器自己收掉，不污染当前状态。
                    try { player.Stop(); } catch { }
                    try { player.Dispose(); } catch { }
                    DisposeProcess(process);
                    return;
                }

                DisposeAudio(replacedAudio);
                Log.Info("音频输出已启动（48 kHz 立体声，音量 " + _volume + "%）");
            }
            catch (Exception ex)
            {
                Log.Warn("启动音频解码失败: " + ex.Message);
                DisposeProcess(process);
                DisposeAudio(player);
                StopAudio(run);
            }
        }

        void AddInput(ProcessStartInfo psi, double startSeconds)
        {
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-nostdin");

            if (startSeconds > 0.01)
            {
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(startSeconds.ToString("0.###", CultureInfo.InvariantCulture));
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

        void ReadLoop(int run)
        {
            try
            {
                // 尺寸在开播时固定；之后即使 UI 换了文件也不能用新的 _width/_height 去解释
                // 这一代的 buffer（否则会越界 / 抛异常）。
                int width = _width;
                int height = _height;
                int size = width * height * 4;
                var buffer = new byte[size];
                long framesSinceLaunch = 0;

                while (!_stop)
                {
                    if (!IsCurrent(run)) break;

                    _resume.Wait();
                    if (_stop || !IsCurrent(run)) break;

                    Process process;
                    lock (_sync)
                    {
                        if (run != _run) break;
                        process = _process;
                    }
                    if (process == null) break;

                    Stream stream = process.StandardOutput.BaseStream;
                    bool frameRead;
                    try
                    {
                        frameRead = ReadFull(stream, buffer);
                    }
                    catch (Exception)
                    {
                        // 进程被 Stop 杀掉时管道会抛 / 返回 0：老代不算错误。
                        if (!IsCurrent(run) || _stop) break;
                        throw;
                    }

                    if (!frameRead)
                    {
                        // 老代晚归：直接退出，不碰新代状态、不报假失败（P2-18）。
                        if (!IsCurrent(run) || _stop) break;

                        // 一帧都没解出来：可能是老版本 ffmpeg 不认 -readrate，退回 -re 再试一次。
                        if (framesSinceLaunch == 0)
                        {
                            bool retried = false;
                            double speed;
                            lock (_sync)
                            {
                                if (run != _run) break;
                                speed = _speed;
                                if (!_readrateRetried)
                                {
                                    _readrateRetried = true;
                                    retried = true;
                                }
                            }

                            if (retried && Math.Abs(speed - 1) > 0.001)
                            {
                                lock (_sync) { if (run == _run) _speed = 1; }
                                Log.Warn("ffmpeg 不支持 -readrate，改用 -re 并以 1× 播放");
                                LoopRestart(run, 0);
                                continue;
                            }
                        }

                        // 一帧都没解出来 = 文件打不开 / 格式不支持，再重开也是死循环。
                        if (framesSinceLaunch == 0)
                        {
                            if (IsCurrent(run)) RaiseFailed("ffmpeg 没有解出画面");
                            break;
                        }

                        if (_loop)
                        {
                            LoopRestart(run, 0);
                            framesSinceLaunch = 0;
                            continue;
                        }

                        if (IsCurrent(run))
                        {
                            Action completed = Completed;
                            if (completed != null)
                            {
                                try { completed(); } catch { }
                            }
                        }
                        break;
                    }

                    framesSinceLaunch++;

                    var frame = BitmapSource.Create(width, height, 96, 96,
                        PixelFormats.Bgra32, null, buffer, width * 4);
                    frame.Freeze();

                    // 出帧期间可能已被重启：老代的帧直接丢掉，不写进当前代的计数 / 画面。
                    if (!IsCurrent(run)) break;

                    Interlocked.Increment(ref _frames);

                    Action<BitmapSource> handler = FrameReady;
                    if (handler != null)
                    {
                        try { handler(frame); }
                        catch (Exception ex) { Log.Warn("显示视频帧失败: " + ex.Message); }
                    }

                    if (_startPaused)
                    {
                        bool current;
                        lock (_sync)
                        {
                            current = run == _run;
                            if (current) _startPaused = false;
                        }
                        if (!current) break;
                        _resume.Reset();
                        PauseAudio();
                    }
                }
            }
            catch (Exception ex)
            {
                // 只有当前代才汇报失败；老代晚归的异常静默丢弃（P2-18：不再报假失败）。
                if (IsCurrent(run) && !_stop)
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

        /// <summary>
        /// 读线程内部的重开：不 join 自己，只换进程（同一代内）。
        /// 每一步都做代次校验：如果 UI 在重开过程中又 Seek / Stop 了（代次变了），
        /// 这里就必须立刻收手，不能 Kill 掉新一代刚起的进程（P2-18）。
        /// </summary>
        void LoopRestart(int run, double seconds)
        {
            if (!IsCurrent(run)) return;

            double startAt = Math.Max(0, seconds);
            lock (_sync)
            {
                if (run != _run) return;
                _startSeconds = startAt;
                _frames = 0;
            }

            StopAudio(run);
            KillProcess(run);

            if (!IsCurrent(run)) return;   // 换进程期间可能已被 UI 重启 / 停止

            try
            {
                LaunchVideo(run, startAt);
                LaunchAudio(run, startAt);
            }
            catch (Exception ex)
            {
                if (IsCurrent(run))
                {
                    Log.Warn("ffmpeg 重新打开失败: " + ex.Message);
                    RaiseFailed(ex.Message);
                }
            }
        }

        /// <summary>从指定秒数重新开始（快进 / 快退 / 变速用）。</summary>
        void RestartAt(double seconds)
        {
            bool paused = IsPaused;
            double startAt = Math.Max(0, seconds);

            StopInternal();

            int run = BeginRun(startAt, paused);
            _stop = false;
            _resume.Set();

            try
            {
                LaunchAll(run, startAt);
            }
            catch (Exception ex)
            {
                if (IsCurrent(run))
                {
                    Log.Warn("ffmpeg 重新打开失败: " + ex.Message);
                    RaiseFailed(ex.Message);
                }
                return;
            }

            StartReader(run);
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

        /// <summary>管理路径（StopInternal / Dispose）用：无条件收掉当前音频进程与播放器。</summary>
        void StopAudio()
        {
            Process process;
            WaveOutPlayer audio;
            lock (_sync)
            {
                process = _audioProcess;
                _audioProcess = null;
                audio = _audio;
                _audio = null;
            }

            DisposeProcess(process);
            DisposeAudio(audio);
        }

        /// <summary>只有仍是当前代时才收音频；旧代晚归不能停掉新一代的声音（P2-18）。</summary>
        void StopAudio(int run)
        {
            Process process;
            WaveOutPlayer audio;
            lock (_sync)
            {
                if (run != _run) return;
                process = _audioProcess;
                _audioProcess = null;
                audio = _audio;
                _audio = null;
            }

            DisposeProcess(process);
            DisposeAudio(audio);
        }

        /// <summary>管理路径用：无条件杀掉当前视频解码进程。</summary>
        void KillProcess()
        {
            Process process;
            lock (_sync)
            {
                process = _process;
                _process = null;
            }
            DisposeProcess(process);
        }

        /// <summary>只有仍是当前代时才杀视频解码进程，旧代晚归不会误杀新进程（P2-18）。</summary>
        void KillProcess(int run)
        {
            Process process;
            lock (_sync)
            {
                if (run != _run) return;
                process = _process;
                _process = null;
            }
            DisposeProcess(process);
        }

        static void DisposeProcess(Process process)
        {
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); } catch { }
            try { process.Dispose(); } catch { }
        }

        static void DisposeAudio(WaveOutPlayer audio)
        {
            if (audio == null) return;
            try { audio.Stop(); } catch { }
            try { audio.Dispose(); } catch { }
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

        /// <summary>探测整体超时（从进程启动算起，到点 Kill(true)）。</summary>
        const int ProbeTimeoutMs = 4000;

        /// <summary>
        /// 问 ffmpeg 要时长 / 分辨率 / 帧率：`ffmpeg -i 文件`（不带输出）会把信息打到 stderr
        /// 然后报「没有输出文件」退出，正好拿来读。
        ///
        /// 同步版本只为保持旧签名 / 兼容调用方存在：**只允许在后台线程调用**。
        /// UI 线程请用 <see cref="ProbeAsync"/>，否则 ffmpeg 卡住时 UI 会一起冻住（P1-8）。
        /// </summary>
        public static VideoInfo Probe(string ffmpeg, string path)
        {
            return ProbeAsync(ffmpeg, path).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 异步探测。stdout / stderr 由 AsyncProcess 并行异步读，整体超时后 Kill(true)，
        /// 所以即使 ffmpeg 卡死（离线网络盘、损坏容器、杀软拦截）也会在 timeoutMs 内返回。
        /// </summary>
        public static async Task<VideoInfo> ProbeAsync(string ffmpeg, string path,
                                                      int timeoutMs = ProbeTimeoutMs)
        {
            var info = new VideoInfo { Fps = 25, Width = 0, Height = 0, Duration = 0 };

            try
            {
                AsyncProcess.Result result = await AsyncProcess.RunAsync(
                    ffmpeg, new[] { "-hide_banner", "-i", path }, timeoutMs).ConfigureAwait(false);

                if (!result.Started)
                {
                    Log.Warn("ffmpeg 探测失败: 无法启动 " + ffmpeg);
                    return info;
                }

                if (result.TimedOut)
                    Log.Warn("ffmpeg 探测超时（" + timeoutMs + "ms），已结束进程：" + path);

                string error = result.StdErr;
                string output = result.StdOutText;
                info = Parse(error.Length > 0 ? error : output);
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

        /// <summary>解图整体超时（从进程启动算起，到点 Kill(true)）。</summary>
        const int ExtractTimeoutMs = 6000;

        /// <summary>
        /// 用 ffmpeg 把一个「WPF 解不开」的图片（webp / avif / heic …）转成一张位图。
        /// 失败返回 null，调用方照常显示「无法解码」。
        ///
        /// 同步版本只为保持旧签名存在：**只允许在后台线程调用**（如离屏截图）。
        /// UI 线程请用 <see cref="ExtractImageAsync"/>（P1-8）。
        /// </summary>
        public static BitmapSource ExtractImage(string ffmpeg, string path, double seconds = 0)
        {
            return ExtractImageAsync(ffmpeg, path, seconds).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 异步解图：PNG 数据走 stdout，错误走 stderr，两边并行异步读（不互相卡）；
        /// 整体超时后 Kill(true)。返回值已 Freeze，可以安全地从后台线程交给 UI。
        /// </summary>
        public static async Task<BitmapSource> ExtractImageAsync(string ffmpeg, string path,
                                                                double seconds = 0,
                                                                int timeoutMs = ExtractTimeoutMs)
        {
            try
            {
                var args = new List<string> { "-hide_banner", "-loglevel", "error" };
                if (seconds > 0.01)
                {
                    args.Add("-ss");
                    args.Add(seconds.ToString("0.###", CultureInfo.InvariantCulture));
                }
                args.Add("-i");
                args.Add(path);
                args.Add("-frames:v");
                args.Add("1");
                args.Add("-f");
                args.Add("image2pipe");
                args.Add("-vcodec");
                args.Add("png");
                args.Add("pipe:1");

                AsyncProcess.Result result =
                    await AsyncProcess.RunAsync(ffmpeg, args, timeoutMs).ConfigureAwait(false);

                if (result.TimedOut)
                    Log.Warn("ffmpeg 解图超时（" + timeoutMs + "ms），已结束进程：" + path);
                if (result.StdOut == null || result.StdOut.Length == 0) return null;

                return DecodeImageBytes(result.StdOut);
            }
            catch (Exception ex)
            {
                Log.Warn("ffmpeg 解图失败: " + ex.Message);
                return null;
            }
        }

        static BitmapSource DecodeImageBytes(byte[] data)
        {
            using (var memory = new MemoryStream(data))
            {
                // ONLOAD + Freeze：解码器把像素读进内存，之后流可以关；
                // Freeze 之后位图不再有线程亲和性，可以从后台线程交给 UI 线程。
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = memory;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        // ==================== 释放 ====================

        void StopInternal()
        {
            _stop = true;
            _resume.Set();

            // 作废当前代：之后旧读线程无论从 ReadFull / FrameReady / LoopRestart 哪一步回来，
            // 都会发现代次不符并立即退出，既不会 Kill 新进程，也不会对新一代报假失败（P2-18）。
            lock (_sync) { _run++; }

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
