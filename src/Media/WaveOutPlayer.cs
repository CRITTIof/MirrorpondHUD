using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Media
{
    /// <summary>
    /// 极简 PCM 播放器：给 ffmpeg 解码出来的音频用。
    ///
    /// 用 winmm 的 waveOut（系统自带的波形输出），不引第三方音频库：
    ///  · 固定 48 kHz / 16 bit / 立体声，和 ffmpeg 的 `-f s16le -ac 2 -ar 48000` 对齐；
    ///  · 后台线程从管道读 PCM，轮流写进 4 个 WAVEHDR 缓冲；
    ///  · 暂停用 waveOutPause + 暂停读管道（管道写满后 ffmpeg 自己会停）；
    ///  · 音量：默认 100% 时**不碰系统音量**；只有显式设成 &lt;100 才临时改设备音量，
    ///    并在收尾时恢复原值（P2-11：对 WAVE_MAPPER 调 waveOutSetVolume 是设备级的）。
    /// 声音本身由声卡按采样率播放，所以这里不需要自己做节拍。
    /// </summary>
    internal sealed class WaveOutPlayer : IDisposable
    {
        const int BufferCount = 4;
        const int BufferBytes = 48 * 2 * 2 * 32;   // 48kHz × 立体声 × 16bit × 32ms

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormatEx
        {
            public ushort FormatTag;
            public ushort Channels;
            public uint SamplesPerSec;
            public uint AvgBytesPerSec;
            public ushort BlockAlign;
            public ushort BitsPerSample;
            public ushort Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHeader
        {
            public IntPtr Data;
            public uint BufferLength;
            public uint BytesRecorded;
            public IntPtr User;
            public uint Flags;
            public uint Loops;
            public IntPtr Next;
            public IntPtr Reserved;
        }

        [DllImport("winmm.dll")]
        static extern int waveOutOpen(out IntPtr handle, int device, ref WaveFormatEx format,
                                      IntPtr callback, IntPtr instance, uint flags);

        [DllImport("winmm.dll")]
        static extern int waveOutClose(IntPtr handle);

        [DllImport("winmm.dll")]
        static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

        [DllImport("winmm.dll")]
        static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

        [DllImport("winmm.dll")]
        static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);

        [DllImport("winmm.dll")]
        static extern int waveOutReset(IntPtr handle);

        [DllImport("winmm.dll")]
        static extern int waveOutPause(IntPtr handle);

        [DllImport("winmm.dll")]
        static extern int waveOutRestart(IntPtr handle);

        [DllImport("winmm.dll")]
        static extern int waveOutSetVolume(IntPtr handle, uint volume);

        [DllImport("winmm.dll")]
        static extern int waveOutGetVolume(IntPtr handle, out uint volume);

        const uint WhdrDone = 0x00000001;

        readonly ManualResetEventSlim _resume = new ManualResetEventSlim(true);
        readonly IntPtr[] _headers = new IntPtr[BufferCount];
        readonly IntPtr[] _buffers = new IntPtr[BufferCount];
        readonly bool[] _queued = new bool[BufferCount];

        // Stop 与 Feed 线程之间交接 buffer 所有权的握手机制：
        //  · _sync 只保护字段交换，不保护 winmm 调用（那些可能阻塞）；
        //  · _resources：1 = header/buffer/handle 还活着，0 = 已收尾；
        //  · _cleanupClaimed：谁先抢到谁负责 Unprepare/Free/Close，保证只收尾一次。
        readonly object _sync = new object();
        int _resources;
        int _cleanupClaimed;

        IntPtr _handle;
        Thread _thread;
        // Join 超时后把收尾权交给 Feed 线程时记下它：之后的 Stop() 只能继续等它，
        // 绝不能再自己释放（它可能还在 Marshal.Copy / waveOutWrite）。
        Thread _handoffThread;
        volatile bool _stop;
        int _next;
        bool _disposed;
        int _volume = 100;

        // P2-11：显式 <100 音量时改过设备音量的话，记下用户原值，收尾时恢复。
        // 单独一把锁，只保护这两个字段 + 对应的 winmm 调用；持有它期间不再获取其他锁。
        readonly object _volumeSync = new object();
        bool _deviceVolumeSaved;
        uint _deviceVolumeSavedValue;

        public bool IsOpen { get { return _handle != IntPtr.Zero; } }
        public bool IsPaused { get { return !_resume.IsSet; } }

        /// <summary>0–100；0 = 静音。</summary>
        public int Volume
        {
            get { return _volume; }
            set
            {
                _volume = Math.Max(0, Math.Min(100, value));
                ApplyVolume();
            }
        }

        /// <summary>打开声卡并开始从 PCM 流播放（流结束时自动停）。</summary>
        public bool Start(Stream pcm)
        {
            if (pcm == null) return false;

            lock (_sync)
            {
                if (_disposed || _thread != null) return false;

                // 上一次 Stop 若是 Join 超时、把收尾权交给了 Feed 线程：它还活着就不能重开。
                if (_handoffThread != null)
                {
                    if (_handoffThread.IsAlive) return false;
                    _handoffThread = null;
                }

                // 它已经退出（应当已经自己收尾）。若资源还没释放完，等下一次再开。
                // 这时分配新 buffer 会覆盖旧指针，旧线程照旧往旧地址写 → 堆破坏。
                if (Volatile.Read(ref _resources) != 0) return false;

                var format = new WaveFormatEx
                {
                    FormatTag = 1,               // WAVE_FORMAT_PCM
                    Channels = 2,
                    SamplesPerSec = 48000,
                    AvgBytesPerSec = 48000 * 2 * 2,
                    BlockAlign = 4,
                    BitsPerSample = 16,
                    Size = 0
                };

                IntPtr handle;
                int code = waveOutOpen(out handle, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0);
                if (code != 0)
                {
                    Log.Warn("打开音频设备失败: " + code);
                    return false;
                }

                _handle = handle;
                Volatile.Write(ref _cleanupClaimed, 0);

                int headerSize = Marshal.SizeOf(typeof(WaveHeader));
                for (int i = 0; i < BufferCount; i++)
                {
                    _buffers[i] = Marshal.AllocHGlobal(BufferBytes);
                    IntPtr header = Marshal.AllocHGlobal(headerSize);
                    var value = new WaveHeader { Data = _buffers[i], BufferLength = BufferBytes };
                    Marshal.StructureToPtr(value, header, false);
                    _headers[i] = header;

                    if (waveOutPrepareHeader(_handle, header, headerSize) != 0)
                        Log.Warn("准备音频缓冲失败: " + i);
                }

                Volatile.Write(ref _resources, 1);
                ApplyVolume();

                _stop = false;
                _resume.Set();
                _thread = new Thread(() => Feed(pcm, handle))
                {
                    IsBackground = true,
                    Name = "MirrorpondHUD.audio"
                };
                _thread.Start();
            }

            return true;
        }

        /// <summary>
        /// 应用音量。P2-11：waveOutOpen(device: -1 = WAVE_MAPPER) 上的 waveOutSetVolume
        /// 是**设备级（全局）**音量，不是应用私有音量 —— 播放一个视频就把用户自己调低的
        /// 系统波形音量拉到 100% 是明显的越权。所以：
        ///  · 默认 / 满音量（100）时完全不写系统音量；
        ///  · 只有显式设成 &lt;100 时才临时改，并先保存原值，收尾时恢复。
        /// 应用内“静音”由上层不启动音频进程实现，不依赖设备音量。
        /// </summary>
        void ApplyVolume()
        {
            IntPtr handle = _handle;
            if (handle == IntPtr.Zero) return;

            if (_volume >= 100)
            {
                // 之前可能因显式 <100 音量改过设备音量：恢复用户原值后不再写。
                RestoreDeviceVolume(handle);
                return;
            }

            lock (_volumeSync)
            {
                if (!_deviceVolumeSaved)
                {
                    uint original;
                    if (waveOutGetVolume(handle, out original) != 0)
                    {
                        // 连原音量都读不到，就更不该覆盖用户的系统设置。
                        Log.Warn("读取系统波形音量失败，保持系统音量不变");
                        return;
                    }
                    _deviceVolumeSaved = true;
                    _deviceVolumeSavedValue = original;
                }

                uint level = (uint)Math.Round(_volume / 100.0 * 0xFFFF);
                try { waveOutSetVolume(handle, level | (level << 16)); } catch { }
            }
        }

        /// <summary>把设备音量恢复成打开时读到的用户原值（没改过就什么都不做）。</summary>
        void RestoreDeviceVolume(IntPtr handle)
        {
            lock (_volumeSync)
            {
                if (!_deviceVolumeSaved) return;

                _deviceVolumeSaved = false;
                if (handle == IntPtr.Zero) return;
                try { waveOutSetVolume(handle, _deviceVolumeSavedValue); } catch { }
            }
        }

        void Feed(Stream pcm, IntPtr handle)
        {
            int headerSize = Marshal.SizeOf(typeof(WaveHeader));
            var buffer = new byte[BufferBytes];

            try
            {
                while (!_stop)
                {
                    _resume.Wait();
                    if (_stop) break;

                    int read = pcm.Read(buffer, 0, buffer.Length);

                    // Read 会因为 ffmpeg 管道堵塞而阻塞很久；返回时 Stop 可能已经置了
                    // _stop 并把收尾权交给本线程。这里必须立刻复检，否则下面会往已经
                    // 被（别人或自己）释放的 HGlobal 里写 → 访问违例 / 堆破坏。
                    if (_stop) break;
                    if (read <= 0) break;
                    if (Volatile.Read(ref _resources) == 0) break;   // 防御：缓冲已收尾
                    if (read < buffer.Length) Array.Clear(buffer, read, buffer.Length - read);

                    int slot = _next;
                    if (!WaitForSlot(slot, headerSize)) break;
                    if (_stop) break;                                // 等待期间可能已经停了

                    IntPtr target = _buffers[slot];
                    if (target == IntPtr.Zero) break;                // 防御：不再写 IntPtr.Zero

                    // WAVEHDR 必须保持 waveOutPrepareHeader 设好的 WHDR_PREPARED 位，
                    // 所以这里只更新数据，不改头结构。
                    Marshal.Copy(buffer, 0, target, buffer.Length);

                    IntPtr header = _headers[slot];
                    if (header == IntPtr.Zero) break;                // 防御：header 已收尾

                    if (waveOutWrite(handle, header, headerSize) != 0)
                    {
                        Log.Warn("写音频缓冲失败");
                        break;
                    }

                    _queued[slot] = true;
                    _next = (slot + 1) % BufferCount;
                }
            }
            catch (Exception ex)
            {
                if (!_stop) Log.Warn("音频播放线程结束: " + ex.Message);
            }
            finally
            {
                // 正常路径：Stop() 里 Join 成功，由 Stop 收尾。
                // Join 超时（本线程还卡在 Read / waveOutWrite 上）时，Stop 已经返回并把
                // buffer/header/handle 的所有权交给本线程，必须在这里自己释放，
                // 否则没人敢释放（Stop 释放会与本线程并发写冲突）。
                if (_stop) ClaimCleanup(handle);
                else ReleaseResourcesOnEof(handle);
            }
        }

        bool WaitForSlot(int slot, int headerSize)
        {
            if (!_queued[slot]) return true;

            while (!_stop)
            {
                IntPtr headerPtr = _headers[slot];
                if (headerPtr == IntPtr.Zero) return false;   // 防御：header 已被收尾

                var header = (WaveHeader)Marshal.PtrToStructure(headerPtr, typeof(WaveHeader));
                if ((header.Flags & WhdrDone) != 0) return true;
                Thread.Sleep(2);
            }

            return false;
        }

        public void Pause()
        {
            if (_handle == IntPtr.Zero) return;
            _resume.Reset();
            try { waveOutPause(_handle); } catch { }
        }

        public void Resume()
        {
            if (_handle == IntPtr.Zero || _stop) return;
            try { waveOutRestart(_handle); } catch { }
            _resume.Set();
        }

        public void Stop()
        {
            Thread thread;
            IntPtr handle;

            lock (_sync)
            {
                _stop = true;
                _resume.Set();

                thread = _thread;
                _thread = null;
                handle = _handle;
                _handle = IntPtr.Zero;

                // 资源已经收尾（或从来没分配过）。
                if (Volatile.Read(ref _resources) == 0) return;

                // 句柄已经被另一条收尾路径（上一次 Stop 的超时交接 / Feed 自然 EOF）拿走：
                // 收尾权在别人手里，这里绝不能抢——抢了会用空句柄跳过 Unprepare/Close。
                if (handle == IntPtr.Zero) return;

                // 上一次 Stop 已经把收尾权交给 Feed 线程：这次只能继续等它，
                // 绝不自己释放（它可能还在往 buffer 写）。
                if (thread == null) thread = _handoffThread;
            }

            try { waveOutReset(handle); } catch { }

            if (thread == null)
            {
                // 没有 Feed 线程可等（从未启动 / 已退出）：当场收尾，等价于原来 Join 成功的路径。
                ClaimCleanup(handle);
                return;
            }

            // 先做一次零等待：绝大多数 Stop 时 Feed 已经因为 _stop / 管道关闭退出了。
            bool joined;
            try { joined = thread.Join(0); } catch { joined = false; }

            if (!joined)
            {
                // 审阅 §20：原来在调用方（通常是 UI）线程 Join(1000)，换文件 / 删卡最多冻 1s。
                // 现在把 Join + 收尾交给后台；收尾权先记到 _handoffThread，后续 Stop 只会继续
                // 等它，不会重复释放（ClaimCleanup 的 CAS 再兜一层）。
                lock (_sync) { _handoffThread = thread; }
                QueueFinish(thread, handle);
                return;
            }

            lock (_sync)
            {
                if (ReferenceEquals(_handoffThread, thread)) _handoffThread = null;
            }

            ClaimCleanup(handle);
        }

        /// <summary>后台等 Feed 线程退出（最多 1s）后收尾；调用方（UI 线程）不再为 Join 阻塞。</summary>
        void QueueFinish(Thread thread, IntPtr handle)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool joined = false;
                try { joined = thread.Join(1000); } catch { }

                if (!joined)
                {
                    // 线程还卡在 pcm.Read / waveOutWrite 里：所有权交回 Feed 线程，
                    // 它退出时在 finally 里认领收尾（ClaimCleanup）。
                    Log.Warn("音频线程 1s 内未退出，缓冲/句柄改由音频线程自行释放");
                    return;
                }

                lock (_sync)
                {
                    if (ReferenceEquals(_handoffThread, thread)) _handoffThread = null;
                }

                ClaimCleanup(handle);
            });
        }

        /// <summary>
        /// 审阅 §21：非循环视频自然放完（EOF）时没有人调 Stop，_stop 仍是 false；
        /// 原来 finally 只处理“被 Stop”的收尾，设备句柄 + 4 个 HGlobal 会一直留到卡片重绑。
        /// 这里由 Feed 线程自己把状态推进到“已停止”并收尾。
        /// </summary>
        void ReleaseResourcesOnEof(IntPtr handle)
        {
            lock (_sync)
            {
                if (_stop || Volatile.Read(ref _resources) == 0) return;   // Stop 已经在收尾

                _stop = true;
                _resume.Set();
                if (ReferenceEquals(_thread, Thread.CurrentThread)) _thread = null;

                // 句柄已被 Stop 拿走 = 收尾权在 Stop 的 Join / 后台路径，不抢。
                if (_handle == IntPtr.Zero) return;
                _handle = IntPtr.Zero;
            }

            ClaimCleanup(handle);
            Log.Info("音频自然播放结束，已释放音频设备与缓冲");
        }

        /// <summary>
        /// 收尾：Unprepare + Free + Close。只允许一个线程执行（_cleanupClaimed CAS），
        /// 触发者要么是 Join 成功的 Stop，要么是 Join 超时后自行退出的 Feed 线程。
        /// </summary>
        void ClaimCleanup(IntPtr handle)
        {
            if (Interlocked.CompareExchange(ref _cleanupClaimed, 1, 0) != 0) return;

            // P2-11：关闭设备前把用户原来的系统波形音量恢复回去
            // （只有我们显式设过 <100 音量、改过设备音量时才会真的写）。
            if (handle != IntPtr.Zero) RestoreDeviceVolume(handle);

            try
            {
                int headerSize = Marshal.SizeOf(typeof(WaveHeader));
                for (int i = 0; i < BufferCount; i++)
                {
                    IntPtr header = _headers[i];
                    if (header != IntPtr.Zero)
                    {
                        // 只有拿到有效设备句柄时才能 Unprepare；句柄已经关掉时
                        // header 只能直接释放（进程即将退出，winmm 会跟着回收）。
                        if (handle != IntPtr.Zero)
                        {
                            try { waveOutUnprepareHeader(handle, header, headerSize); } catch { }
                        }
                        _headers[i] = IntPtr.Zero;
                        try { Marshal.FreeHGlobal(header); } catch { }
                    }

                    IntPtr buffer = _buffers[i];
                    if (buffer != IntPtr.Zero)
                    {
                        _buffers[i] = IntPtr.Zero;
                        try { Marshal.FreeHGlobal(buffer); } catch { }
                    }

                    _queued[i] = false;
                }

                _next = 0;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    try { waveOutClose(handle); } catch { }
                }

                Volatile.Write(ref _resources, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
