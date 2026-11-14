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

namespace MirrorpondHUD.Ui.Controls.Media;

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
/// <summary>
/// K-16：一帧原始 BGRA 数据。由解码线程从帧池租出、装进 <see cref="FfmpegPlayer"/> 的
/// "最新一帧"槽位；UI 复制进 WriteableBitmap 后必须调用 <see cref="FfmpegPlayer.ReleaseFrame"/>
/// 把缓冲还回池子（池子上限 3 块，天然有界）。用这种"租借"而不是每帧 BitmapSource，
/// 是为了让 60/120fps 视频不再每秒分配几百 MB 托管位图。
/// </summary>
internal sealed class VideoFrame
{
    internal byte[] Buffer;
    internal int Width;
    internal int Height;
    internal int Run;

    int _released;

    /// <summary>只允许归还一次（UI 可能在旧回调里重复归还）。</summary>
    internal bool TryMarkReleased() => Interlocked.Exchange(ref _released, 1) == 0;
}

internal sealed class FfmpegPlayer : IDisposable
{
    /// <summary>K-16：新帧可取的合并通知（同一时刻最多排一次）。UI 收到后调用
    /// <see cref="TakeFrame"/> 取"最新一帧"，复制完 <see cref="ReleaseFrame"/>。</summary>
    public event Action FrameAvailable;
    public event Action<string> Failed;
    public event Action Completed;
    /// <summary>P1-6：音频启动失败（WaveOut 打不开 / 解码进程起不来）时触发，
    /// 内容是原因描述；视频照常播，UI 可以用它在状态行如实标注「音频不可用」。</summary>
    public event Action<string> AudioUnavailable;
    /// <summary>K-6：运行期音频死亡（USB 耳机被拔、驱动重置、waveOutWrite 失败）。
    /// 与 <see cref="AudioUnavailable"/> 分开：那是"没起来"，这是"播着播着没了"。</summary>
    public event Action<string> AudioFailed;
    /// <summary>
    /// K-22：ffmpeg 只能按原生速率读包（-readrate 回退到 -re）时，把实际生效的倍速报给上层。
    /// 卡片收到后必须把显示的倍速回滚成这个值并说明原因，否则界面写着 2×、画面却是 1×。
    /// 可能在读帧线程上触发，订阅方自己负责切回 UI 线程。
    /// </summary>
    public event Action<double> SpeedForced;

    readonly ManualResetEventSlim _resume = new(true);

    // P2-18：_stop 原先兼作“重启代号”。RestartAt 的 Join(1500) 超时后旧读线程晚归，
    // 会看到 _stop 已被清掉继续跑，还可能 Kill 掉新一代的进程、或对新一代报假失败。
    // 现在每次 Start / RestartAt 都 ++_run；读线程全程用自己捕获的 run 校验，
    // 所有进程 / 音频播放器的安装与 Kill 都在 _sync 里校验代次，旧代晚归直接作废。
    readonly object _sync = new();
    int _run;

    Process _process;
    Process _audioProcess;
    Thread _reader;
    WaveOutPlayer _audio;

    // K-16 帧管线：解码线程 → "最新一帧"槽位（_pendingFrame）→ UI 复制进 WriteableBitmap。
    // 池子只放 3 块 BGRA 缓冲，超过就主动丢帧而不是无限增长；_frameNotify 保证
    // Dispatcher 队列里最多只有一个待处理通知（Render 优先级），高帧率不会堆积。
    readonly object _frameSync = new();
    readonly List<byte[]> _freeFrameBuffers = new();
    VideoFrame _pendingFrame;
    int _frameBufferSize;
    int _frameAllocated;
    int _frameNotify;
    const int MaxFrameBuffers = 3;

    string _ffmpeg = "";
    string _path = "";
    /// <summary>最近一次视频解码启动的实际 argv 摘要，只在失败时写日志（审阅 §7.2：失败原因要可行动）。</summary>
    string _lastVideoArgs = "";
    int _width;
    int _height;
    double _fps = 25;
    /// <summary>
    /// K-16：实际输出给 UI 的帧率（= min(源帧率, 30)）。ffmpeg 的 -r 用它，
    /// Position 也用它把帧数换算回秒——两者必须同一个值，否则进度会按倍率跑偏。
    /// </summary>
    double _outputFps = 25;
    double _duration;
    double _speed = 1;
    double _startSeconds;
    int _volume = 100;
    long _frames;
    bool _loop;
    bool _audioEnabled;
    volatile bool _stop;
    volatile bool _startPaused;
    /// <summary>
    /// K-7：非循环视频读到 EOF 后的"已播完"状态。和 <see cref="_stop"/> 分开：
    /// _stop 是"用户/宿主停止"（Seek 等操作要拒绝），_ended 是"自然播完"
    /// （Resume 应该从头重开，位置显示在末尾）。旧实现不分这两种状态，
    /// 于是 EOF 后 IsRunning 恒 true、播放键按下去毫无反应。
    /// </summary>
    bool _ended;
    bool _disposed;
    /// <summary>
    /// ffmpeg 不认 <c>-readrate</c> 的回退标记（P1-6）：一旦某代 0 帧输出，就改用经典的
    /// <c>-re</c>（按原生速率读包）并只以 1× 播放。这是二进制能力问题，与文件无关，
    /// 所以一旦置位就黏住，后续换文件 / 循环重开都不再试 -readrate。
    /// </summary>
    volatile bool _useRe;

    public double Duration => _duration;

    public double Position
    {
        get
        {
            double start;
            long frames;
            bool ended;
            lock (_sync)
            {
                start = _startSeconds;
                frames = _frames;
                ended = _ended;
            }

            // K-7：播完后位置就该停在末尾，不能再按帧数推算（末帧可能差半帧）。
            if (ended && _duration > 0.01) return _duration;

            double position = start + (_outputFps > 0 ? frames / _outputFps : 0);
            return _duration > 0.01 ? Math.Min(_duration, position) : position;
        }
    }

    public bool IsRunning
    {
        get { lock (_sync) { return _process is not null && !_stop && !_ended; } }
    }

    /// <summary>K-7：非循环视频是否已经自然播完（UI 用它显示"已结束"而不是"运行中"）。</summary>
    public bool IsEnded
    {
        get { lock (_sync) { return _ended; } }
    }
    public bool IsPaused => !_resume.IsSet;
    public double Speed => _speed;

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
        // K-16：媒体卡在 1280 宽以内，30fps 视觉足够；把输出帧率封顶能同时
        // 压掉解码 CPU、帧池压力和 Dispatcher 通知频率。Position 用 _outputFps 换算。
        _outputFps = Math.Min(_fps, 30);
        _ended = false;
        _duration = duration;
        _loop = loop;
        // K-22：_re 回退模式下 ffmpeg 以原生速率读包，没有倍速语义。这里必须把 _speed 归到 1，
        // 否则 LaunchVideo 走 -re（不读倍速）、LaunchAudio 却按 atempo 变速，音画各走各的；
        // 同时把"请求倍速没生效"上报给卡片，让界面回滚显示值而不是继续写着 2×。
        double requestedSpeed = ClampSpeed(speed);
        bool speedForced = _useRe && Math.Abs(requestedSpeed - 1) > 0.001;
        _speed = speedForced ? 1 : requestedSpeed;
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
            Log.Warn($"启动 ffmpeg 失败: {ex.Message}");
            RaiseFailed(ex.Message);
            return;
        }

        StartReader(run);

        if (speedForced)
        {
            Log.Warn($"-re 回退模式不支持倍速，已按 1× 播放（请求 {requestedSpeed:0.##}×）");
            RaiseSpeedForced(1);
        }
    }

    /// <summary>开始一代新播放：递增代次，重置本代的起始位置 / 帧计数 / 起播暂停标记。</summary>
    int BeginRun(double startSeconds, bool paused)
    {
        int run;
        lock (_sync)
        {
            _run++;
            run = _run;
            _startSeconds = Math.Max(0, startSeconds);
            _frames = 0;
            _startPaused = paused;
            // K-7：新代开始必须清掉"已播完"，否则 Seek 重播后 IsRunning 仍然为 false。
            _ended = false;
        }

        // K-16：上一代可能还有没被 UI 取走的帧；代次校验能挡住旧帧，但缓冲要回收。
        ResetFramePipeline();
        return run;
    }

    bool IsCurrent(int run) => run == Volatile.Read(ref _run);

    void StartReader(int run)
    {
        Thread reader = new(() => ReadLoop(run))
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
        if (_useRe)
        {
            // P1-6：老版 ffmpeg 不认 -readrate，回退经典 -re（按原生速率读包）。
            // 原生速率意味着没有倍速语义，所以 _speed 已经被固定成 1，保持音画同步。
            psi.ArgumentList.Add("-re");
        }
        else
        {
            psi.ArgumentList.Add("-readrate");
            psi.ArgumentList.Add(_speed.ToString("0.###", CultureInfo.InvariantCulture));
        }
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(_path);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:v:0");
        psi.ArgumentList.Add("-an");
        psi.ArgumentList.Add("-sn");
        psi.ArgumentList.Add("-dn");
        psi.ArgumentList.Add("-vf");
        // force_original_aspect_ratio=decrease 只保证“装进 W×H 盒子”，不保证输出正好 W×H；
        // 读帧线程却按 _width*_height*4 切帧，宽高比稍有差异整段画面就会错位。
        // pad 把输出强制补到 W×H（多出的部分是黑边），保证每帧字节数固定。
        psi.ArgumentList.Add(
            $"scale={_width}:{_height}:force_original_aspect_ratio=decrease," +
            $"pad={_width}:{_height}:(ow-iw)/2:(oh-ih)/2:color=black");
        psi.ArgumentList.Add("-r");
        // K-16：输出帧率封顶 30fps（见 _outputFps 注释），与 Position 的换算保持一致。
        psi.ArgumentList.Add(_outputFps.ToString("0.###", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("bgra");
        psi.ArgumentList.Add("pipe:1");

        // 先记下实际参数，再启动：Process.Start / 首帧失败时日志能直接看到是 -readrate 还是 -re 分支。
        _lastVideoArgs = DescribeArguments(psi);

        Process process = Process.Start(psi);
        if (process is null) throw new InvalidOperationException("ffmpeg 没有启动");

        process.ErrorDataReceived += (s, e) =>
        {
            if (e is not null && !string.IsNullOrWhiteSpace(e.Data))
                Log.Warn($"ffmpeg: {e.Data.Trim()}");
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
            if (tempo is not null)
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
            if (process is null)
            {
                // P1-6：Process.Start 返回 null 属于异常情况（资源耗尽 / 拦截），不能静默跳过。
                Log.Warn("音频解码进程没有启动（Process.Start 返回 null），继续静音播放");
                RaiseAudioUnavailable("音频解码进程没有启动");
                return;
            }

            process.ErrorDataReceived += (s, e) =>
            {
                if (e is not null && !string.IsNullOrWhiteSpace(e.Data))
                    Log.Warn($"ffmpeg(音频): {e.Data.Trim()}");
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
                // P1-6：失败原因要能给到 UI（状态行如实标注「音频不可用」），只写日志还不够。
                Log.Warn($"音频输出启动失败，继续静音播放: {player.LastErrorName}");
                RaiseAudioUnavailable(player.LastErrorName);
                // 释放路径统一走 SafeRelease（X-9）：Dispose 内部只会停线程/关句柄，
                // 失败记 Debug/Warn，不再空 catch。
                SafeRelease.Run(() => player.Dispose(), "音频播放器");
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
                // 释放路径：Stop/Dispose 失败没有可补救的动作，句柄交给下一次收尾；
                // 统一走 SafeRelease 留痕（X-9）。
                SafeRelease.Run(() => player.Stop(), "音频播放器");
                SafeRelease.Run(() => player.Dispose(), "音频播放器");
                DisposeProcess(process);
                return;
            }

            DisposeAudio(replacedAudio);

            // K-6：运行期音频死亡（设备热拔 / 写失败）转成事件上报，卡片状态行据此
            // 从"正常播放"改成"音频中断"。代次校验保证老播放器的晚归事件不污染新代。
            player.AudioFailed += reason =>
            {
                if (!IsCurrent(run)) return;
                RaiseAudioFailed(reason);
            };

            Log.Info($"音频输出已启动（48 kHz 立体声，音量 {_volume}%）");
        }
        catch (Exception ex)
        {
            Log.Warn($"启动音频解码失败: {ex.Message}");
            RaiseAudioUnavailable($"音频解码失败：{ex.Message}");
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

        parts.Add($"atempo={value.ToString("0.###", CultureInfo.InvariantCulture)}");
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
            // 池子被 UI 占满时的兜底缓冲：读掉字节保证 ffmpeg 管道不被堵死。
            // 每代最多一块，内存仍然有界（3 + 1 块）。
            byte[] scratch = null;
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
                if (process is null) break;

                Stream stream = process.StandardOutput.BaseStream;

                // K-16：先租一块帧缓冲再读；池子见底时短暂等 UI 消费，仍没有就丢这一帧。
                byte[] buffer = RentFrameBuffer(size);
                if (buffer is null)
                {
                    for (int i = 0; i < 125 && buffer is null && !_stop && IsCurrent(run); i++)
                    {
                        Thread.Sleep(2);
                        buffer = RentFrameBuffer(size);
                    }

                    if (buffer is null)
                    {
                        scratch ??= new byte[size];
                        buffer = scratch;
                    }
                }

                // scratch 是本代专用的丢弃缓冲，不能进入发布管道（否则内容会被覆盖）。
                bool reusable = !ReferenceEquals(buffer, scratch);

                bool frameRead;
                try
                {
                    frameRead = ReadFull(stream, buffer);
                }
                catch (Exception)
                {
                    // 进程被 Stop 杀掉时管道会抛 / 返回 0：老代不算错误。
                    if (reusable) ReturnFrameBuffer(buffer);
                    if (!IsCurrent(run) || _stop) break;
                    throw;
                }

                if (!frameRead)
                {
                    // 读失败/被停止：这块缓冲还在我们手里，能还就还（scratch 不还，
                    // 它是本代专用的丢弃缓冲，不能进入池子被当成可发布帧）。
                    if (reusable) ReturnFrameBuffer(buffer);

                    // 老代晚归：直接退出，不碰新代状态、不报假失败（P2-18）。
                    if (!IsCurrent(run) || _stop) break;

                    // 一帧都没解出来：可能是老版本 ffmpeg 不认 -readrate。
                    // P1-6：真回退 —— 置 -re 标记并把速度固定成 1×（原生速率读包，
                    // 倍速会让音画走叉），重开本代再验证一次。
                    bool switched = false;
                    if (framesSinceLaunch == 0)
                    {
                        lock (_sync)
                        {
                            if (run != _run) break;
                            if (!_useRe)
                            {
                                _useRe = true;
                                _speed = 1;
                                switched = true;
                            }
                        }
                        if (switched)
                        {
                            Log.Warn($"ffmpeg 不支持 -readrate（0 帧输出），已改用 -re 回退，以 1× 播放  {DescribeLaunch()}");
                            // K-22：卡片可能还显示着用户设置 / 热键给出的倍速，这里必须如实上报实际值。
                            RaiseSpeedForced(1);
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

                    // K-7：非循环视频自然播完：必须推进状态机（清进程 / 置 _ended /
                    // 停音频 / 触发 Completed），否则 IsRunning 骗人、播放键失效。
                    EndOfStream(run);
                    break;
                }

                framesSinceLaunch++;

                // 出帧期间可能已被重启：老代的帧直接丢掉，不写进当前代的计数 / 画面。
                if (!IsCurrent(run)) break;

                Interlocked.Increment(ref _frames);

                // K-16：把缓冲交给"最新一帧"槽位并合并通知；中间帧被覆盖即丢弃。
                if (reusable) PublishFrame(buffer, width, height, run);

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
                Log.Warn($"ffmpeg 解码线程结束: {ex.Message}");
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

    // ==================== 帧池 / 合并通知（K-16） ====================

    /// <summary>
    /// UI 线程取走"最新一帧"。高帧率下解码线程会不断用新帧覆盖槽位，
    /// 所以这里拿到的可能是最新帧而中间帧已被丢弃——这正是我们要的合并语义。
    /// </summary>
    public VideoFrame TakeFrame()
    {
        lock (_frameSync)
        {
            VideoFrame frame = _pendingFrame;
            _pendingFrame = null;
            return frame;
        }
    }

    /// <summary>UI 复制完一帧后归还缓冲（并允许下一次通知）。重复调用安全。</summary>
    public void ReleaseFrame(VideoFrame frame)
    {
        if (frame is null || !frame.TryMarkReleased()) return;
        ReturnFrameBuffer(frame.Buffer);
        Interlocked.Exchange(ref _frameNotify, 0);
    }

    /// <summary>UI 丢弃这一帧（例如视图已释放）时也要复位通知标志，否则再也不会有通知。</summary>
    public void SkipFrame() => Interlocked.Exchange(ref _frameNotify, 0);

    byte[] RentFrameBuffer(int size)
    {
        lock (_frameSync)
        {
            if (size != _frameBufferSize)
            {
                // 帧尺寸变了：旧尺寸的缓冲不能给 WritePixels 用（会越界），整池作废。
                _freeFrameBuffers.Clear();
                _frameBufferSize = size;
                _frameAllocated = 0;
            }

            if (_freeFrameBuffers.Count > 0)
            {
                int last = _freeFrameBuffers.Count - 1;
                byte[] buffer = _freeFrameBuffers[last];
                _freeFrameBuffers.RemoveAt(last);
                return buffer;
            }

            // 池子上限 3 块：1 块在"待取"槽位、1 块在 UI 手里、1 块给读线程写。
            // 再多就变成无界内存；没有可用缓冲时由调用方决定等待/丢帧。
            if (_frameAllocated >= MaxFrameBuffers) return null;
            _frameAllocated++;
            return new byte[size];
        }
    }

    void ReturnFrameBuffer(byte[] buffer)
    {
        if (buffer is null) return;

        lock (_frameSync)
        {
            if (buffer.Length != _frameBufferSize) return;   // 旧尺寸缓冲：直接让 GC 收走
            if (_freeFrameBuffers.Count >= MaxFrameBuffers) return;
            _freeFrameBuffers.Add(buffer);
        }
    }

    /// <summary>
    /// 把新帧放进"待取"槽位；被顶掉的旧帧立即回收。
    /// 通知只在 <see cref="_frameNotify"/> 从 0 变 1 时发出一次——UI 处理不过来时
    /// Dispatcher 队列里始终只有一个回调，不会堆积帧。
    /// </summary>
    void PublishFrame(byte[] buffer, int width, int height, int run)
    {
        // 代次校验放在发布前：读线程刚通过 IsCurrent 检查就被重启时，
        // 这一帧属于旧代，直接回收，不能贴到新代画面上（P2-18）。
        lock (_sync)
        {
            if (run != _run)
            {
                ReturnFrameBuffer(buffer);
                return;
            }
        }

        VideoFrame previous;
        lock (_frameSync)
        {
            previous = _pendingFrame;
            _pendingFrame = new VideoFrame { Buffer = buffer, Width = width, Height = height, Run = run };
        }

        if (previous is not null) ReturnFrameBuffer(previous.Buffer);

        if (Interlocked.CompareExchange(ref _frameNotify, 1, 0) != 0) return;

        Action handler = FrameAvailable;
        if (handler is null)
        {
            Interlocked.Exchange(ref _frameNotify, 0);
            return;
        }

        try { handler(); }
        catch (Exception ex)
        {
            // 通知方（卡片视图）异常不能拖垮解码线程；复位标志让后续帧还能通知。
            Log.Warn($"通知视频帧失败: {ex.Message}");
            Interlocked.Exchange(ref _frameNotify, 0);
        }
    }

    /// <summary>换代 / 停止时清掉待取帧并复位通知；已在 UI 手里的帧由 ReleaseFrame 归还。</summary>
    void ResetFramePipeline()
    {
        VideoFrame pending;
        lock (_frameSync)
        {
            pending = _pendingFrame;
            _pendingFrame = null;
        }

        if (pending is not null) ReturnFrameBuffer(pending.Buffer);
        Interlocked.Exchange(ref _frameNotify, 0);
    }

    /// <summary>
    /// K-7：非循环视频读到 EOF。旧实现只 break，_process 仍挂着、_stop 仍 false，
    /// IsRunning 骗人、Resume() 只是置事件（读线程已经退出），播放键完全失效。
    /// 这里把状态推进到"已播完"：清进程、置 _ended、停音频，再触发 Completed。
    /// 不置 _stop —— "停止"与"播完"是两种状态，Seek / 再次播放仍要通过 _stop 守卫。
    /// </summary>
    void EndOfStream(int run)
    {
        Process process = null;
        bool endedNow = false;

        lock (_sync)
        {
            if (run != _run) return;

            process = _process;
            _process = null;
            if (!_ended)
            {
                _ended = true;
                endedNow = true;
            }
        }

        DisposeProcess(process);
        StopAudio(run);

        // 帧池里可能还留着一帧没被 UI 取走：保留它（就是最后一帧），只清通知标志。
        if (!endedNow) return;

        Log.Info("ffmpeg 视频已播放到结尾");
        Action completed = Completed;
        if (completed is null) return;

        try { completed(); }
        catch (Exception ex) { Log.Warn($"视频结束事件处理异常: {ex.Message}"); }
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
                Log.Warn($"ffmpeg 重新打开失败: {ex.Message}");
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
                Log.Warn($"ffmpeg 重新打开失败: {ex.Message}");
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

    /// <summary>
    /// 设置播放速度。
    /// K-22：返回 false = 这个请求没能生效（<c>-re</c> 回退模式只支持 1×；播放器已停止 / 已释放）。
    /// 调用方（卡片）必须据此把显示的倍速回滚成实际值，不能"界面写 2×、画面走 1×"。
    /// 返回 true 表示已按请求重开解码，真正的启动失败仍走 <see cref="Failed"/> 事件。
    /// </summary>
    public bool SetSpeed(double speed)
    {
        if (_disposed || _stop) return false;

        double next = ClampSpeed(speed);
        if (Math.Abs(next - _speed) < 0.001) return true;   // 已经是这个速度，等价于成功

        // P1-6：-re 回退模式下 ffmpeg 以原生速率读包，没有倍速语义；
        // 强行变速会让视频原速、音频 atempo 各走各的，所以保持 1×。
        if (_useRe && Math.Abs(next - 1) > 0.001)
        {
            Log.Warn("-re 回退模式不支持倍速，保持 1× 播放");
            return false;
        }

        double position = Position;
        _speed = next;

        // K-7：播完之后改速度只需记住值，下一次从头播放时生效；不要为此重开解码进程。
        if (IsEnded)
        {
            Log.Info($"ffmpeg 播放速度已记为 {next:0.##}×（视频已播完，下次播放生效）");
            return true;
        }

        Log.Info($"ffmpeg 播放速度：{next:0.##}×");
        RestartAt(position);
        return true;
    }

    /// <summary>运行期切换静音：停掉音频进程 / 播放器，恢复时从当前位置重新拉起一条。</summary>
    public void SetMuted(bool muted)
    {
        if (_disposed || _stop) return;

        // K-7：播完后不要再为"取消静音"拉一条新音频进程——它只会立刻读到 EOF。
        if (IsEnded)
        {
            _audioEnabled = !muted;
            return;
        }

        if (muted)
        {
            if (!_audioEnabled && _audio is null && _audioProcess is null) return;
            _audioEnabled = false;
            StopAudio();
            Log.Info("ffmpeg 已静音（音频进程已停止）");
            return;
        }

        if (_audioEnabled && _audio is not null) return;
        _audioEnabled = true;

        int run = Volatile.Read(ref _run);
        if (run <= 0) return;

        // 从当前播放位置重新起一条音频；视频进程不动，音画仍然按同一时间轴对齐。
        LaunchAudio(run, Position);
        // K-8：暂停状态下去掉静音不应该立刻出声（视频还冻着，会音画脱节）。
        if (IsPaused) PauseAudio();
        Log.Info("ffmpeg 已取消静音（从当前进度重启音频输出）");
    }

    /// <summary>运行期切换循环。只影响之后读到 EOF 的行为，不重启视频。</summary>
    public void SetLoop(bool loop) => _loop = loop;

    public void Pause()
    {
        _resume.Reset();
        PauseAudio();
    }

    public void Resume()
    {
        if (_disposed || _stop) return;

        // K-7：自然播完后读线程已经退出，只置事件不会让画面动起来；
        // 这里真的从头重开一代（UI 的播放键因此恢复可用）。
        if (IsEnded)
        {
            Log.Info("视频已播完，从头重新播放");
            RestartAt(0);
            return;
        }

        _resume.Set();
        ResumeAudio();
    }

    void PauseAudio()
    {
        // X-9：暂停失败会导致"画面停住、声音继续"，不能吞。
        try { if (_audio is not null) _audio.Pause(); }
        catch (Exception ex) { Log.Warn($"暂停音频输出失败: {ex.Message}"); }
    }

    void ResumeAudio()
    {
        try { if (_audio is not null) _audio.Resume(); }
        catch (Exception ex) { Log.Warn($"恢复音频输出失败: {ex.Message}"); }
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
        if (process is null) return;
        // 释放路径：进程可能已退出/句柄已被系统回收，Kill/Dispose 的异常没有可补救动作，
        // 但只要能杀就杀——这里不允许影响调用方的状态机。统一走 SafeRelease 留痕（X-9）。
        SafeRelease.Run(() => { if (!process.HasExited) process.Kill(); }, "ffmpeg 进程");
        SafeRelease.Run(() => process.Dispose(), "ffmpeg 进程");
    }

    static void DisposeAudio(WaveOutPlayer audio)
    {
        if (audio is null) return;
        // 审阅 §20：Dispose 内部已经会 Stop()（Join 已改成后台有界等待），
        // 原来 Stop + Dispose 会把同一个音频线程 Join 两遍，这里只留一次。
        try { audio.Dispose(); }
        catch (Exception ex) { Log.Warn($"释放音频播放器失败: {ex.Message}"); }
    }

    void RaiseFailed(string message)
    {
        // 审阅 §7.2：只报「没有解出画面」不够排障，必须带上实际 argv 与进程退出码。
        Log.Warn($"ffmpeg 失败: {message}  {DescribeLaunch()}");

        Action<string> handler = Failed;
        if (handler is not null)
        {
            try { handler(message); }
            catch (Exception ex) { Log.Warn($"失败事件处理异常: {ex.Message}"); }
        }
    }

    /// <summary>给日志用的 argv 摘要：参数本身不含密钥，仍截断到 400 字避免刷屏。</summary>
    static string DescribeArguments(ProcessStartInfo psi)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(psi.FileName);
        foreach (string arg in psi.ArgumentList)
        {
            sb.Append(' ');
            if (arg.IndexOf(' ') >= 0 || arg.IndexOf('"') >= 0)
            {
                sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }
        if (sb.Length > 400) return $"{sb.ToString(0, 400)}…";
        return sb.ToString();
    }

    /// <summary>
    /// 失败时补一行可行动诊断：最近一次启动的 argv + 当前进程退出码。
    /// 进程可能已经被 Stop / Dispose 回收，读退出码失败属于良性竞态，如实标注即可。
    /// </summary>
    string DescribeLaunch()
    {
        string args = _lastVideoArgs;
        Process process;
        lock (_sync) { process = _process; }

        string state;
        if (process is null)
        {
            state = "进程=null";
        }
        else
        {
            try
            {
                state = process.HasExited ? $"退出码={process.ExitCode}" : "进程仍在运行";
            }
            catch (Exception ex)
            {
                // 句柄已被 Dispose / 回收：拿不到退出码，但失败本身仍要报。
                state = $"退出码不可读({ex.GetType().Name})";
            }
        }

        return $"argv={(string.IsNullOrEmpty(args) ? "(未记录)" : args)}  {state}";
    }

    /// <summary>P1-6：音频不可用的通知；处理器异常不影响播放线程。</summary>
    void RaiseAudioUnavailable(string message)
    {
        Action<string> handler = AudioUnavailable;
        if (handler is not null)
        {
            try { handler(message); } catch (Exception ex) { Log.Warn($"音频不可用事件处理异常: {ex.Message}"); }
        }
    }

    /// <summary>K-6：运行期音频死亡的通知（与"音频不可用"区分：这是播着播着断了）。</summary>
    void RaiseAudioFailed(string message)
    {
        Log.Warn($"音频输出中断: {message}");
        Action<string> handler = AudioFailed;
        if (handler is not null)
        {
            try { handler(message); } catch (Exception ex) { Log.Warn($"音频中断事件处理异常: {ex.Message}"); }
        }
    }

    /// <summary>K-22：把"倍速被强制成这个值"的通知交给卡片（可能来自读帧线程）。</summary>
    void RaiseSpeedForced(double actualSpeed)
    {
        Action<double> handler = SpeedForced;
        if (handler is null) return;

        try { handler(actualSpeed); }
        catch (Exception ex)
        {
            // 订阅方（卡片视图）异常不能反噬解码线程；日志留痕即可。
            Log.Warn($"倍速回退事件处理异常: {ex.Message}");
        }
    }

    // ==================== 探测 ====================

    /// <summary>探测整体超时（从进程启动算起，到点 Kill(true)）。</summary>
    const int ProbeTimeoutMs = 4000;

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
                Log.Warn($"ffmpeg 探测失败: 无法启动 {ffmpeg}");
                return info;
            }

            if (result.TimedOut)
                Log.Warn($"ffmpeg 探测超时（{timeoutMs}ms），已结束进程：{path}");

            string error = result.StdErr;
            string output = result.StdOutText;
            info = Parse(error.Length > 0 ? error : output);
        }
        catch (Exception ex)
        {
            Log.Warn($"ffmpeg 探测失败: {ex.Message}");
        }

        return info;
    }

    static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)",
        RegexOptions.CultureInvariant);

    static readonly Regex SizeRegex = new(@"(\d{2,5})x(\d{2,5})",
        RegexOptions.CultureInvariant);

    static readonly Regex FpsRegex = new(@"(\d+(?:\.\d+)?)\s*fps",
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
                if (double.TryParse(fps.Groups[1].Value, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var value) &&
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
    public static BitmapSource ExtractImage(string ffmpeg, string path, double seconds = 0) => ExtractImageAsync(ffmpeg, path, seconds).GetAwaiter().GetResult();

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
                Log.Warn($"ffmpeg 解图超时（{timeoutMs}ms），已结束进程：{path}");
            if (result.StdOut is null || result.StdOut.Length == 0) return null;

            return DecodeImageBytes(result.StdOut);
        }
        catch (Exception ex)
        {
            Log.Warn($"ffmpeg 解图失败: {ex.Message}");
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

        // 作废当前代：之后旧读线程无论从 ReadFull / 帧发布 / LoopRestart 哪一步回来，
        // 都会发现代次不符并立即退出，既不会 Kill 新进程，也不会对新一代报假失败（P2-18）。
        lock (_sync)
        {
            _run++;
            // 停止不是"播完"：下一次 Start/RestartAt 会重新置 false，这里提前清掉，
            // 免得 IsEnded 在停止与重开之间给出误导性的 true。
            _ended = false;
        }

        // K-16：把待取帧回收、复位通知标志；UI 若还持有帧，ReleaseFrame 会安全归还。
        ResetFramePipeline();

        StopAudio();
        KillProcess();

        Thread reader = _reader;
        _reader = null;

        // 审阅 §20：这里原来是 reader.Join(1500)，而 ReleaseVideo 经常在 UI 线程上调用
        // （换文件 / 删卡 / 改解码器），最坏要冻 UI 1.5s。signal + Kill 已经让老读线程
        // 立即失效（代次校验会丢掉它晚归的帧 / 失败），Join 只是收尾确认，交给后台有界等待。
        DetachJoin(reader);
    }

    /// <summary>把读线程的 Join 交给后台（最多等 2s，只写日志），调用线程不阻塞。</summary>
    static void DetachJoin(Thread thread)
    {
        if (thread is null) return;

        try
        {
            // 绝大多数情况进程刚被杀，读线程已经退出；先做一次零等待，避免无谓地开后台任务。
            if (thread.Join(0)) return;

            Task.Run(() =>
            {
                try
                {
                    if (!thread.Join(2000))
                        Log.Warn("ffmpeg 读线程 2s 内未退出（已脱离，不阻塞 UI）");
                }
                catch (ThreadStateException)
                {
                    // 良性竞态：线程已终止且句柄被回收时 Join 会抛，无需再等。
                }
            });
        }
        catch (ThreadStateException)
        {
            // 良性竞态：线程已经结束（信号 + Kill 后立即退出），不需要再 Join。
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();
    }
}
