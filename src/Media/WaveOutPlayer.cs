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
    ///  · 音量直接 waveOutSetVolume，静音就是 0。
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

        const uint WhdrDone = 0x00000001;

        readonly ManualResetEventSlim _resume = new ManualResetEventSlim(true);
        readonly IntPtr[] _headers = new IntPtr[BufferCount];
        readonly IntPtr[] _buffers = new IntPtr[BufferCount];
        readonly bool[] _queued = new bool[BufferCount];

        IntPtr _handle;
        Thread _thread;
        volatile bool _stop;
        int _next;
        bool _disposed;
        int _volume = 100;

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
            if (_disposed || _handle != IntPtr.Zero) return false;
            if (pcm == null) return false;

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

            ApplyVolume();

            _stop = false;
            _resume.Set();
            _thread = new Thread(() => Feed(pcm)) { IsBackground = true, Name = "MirrorpondHUD.audio" };
            _thread.Start();
            return true;
        }

        void ApplyVolume()
        {
            if (_handle == IntPtr.Zero) return;

            uint level = (uint)Math.Round(_volume / 100.0 * 0xFFFF);
            try { waveOutSetVolume(_handle, level | (level << 16)); } catch { }
        }

        void Feed(Stream pcm)
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
                    if (read <= 0) break;
                    if (read < buffer.Length) Array.Clear(buffer, read, buffer.Length - read);

                    int slot = _next;
                    if (!WaitForSlot(slot, headerSize)) break;

                    // WAVEHDR 必须保持 waveOutPrepareHeader 设好的 WHDR_PREPARED 位，
                    // 所以这里只更新数据，不改头结构。
                    Marshal.Copy(buffer, 0, _buffers[slot], buffer.Length);

                    if (waveOutWrite(_handle, _headers[slot], headerSize) != 0)
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
        }

        bool WaitForSlot(int slot, int headerSize)
        {
            if (!_queued[slot]) return true;

            while (!_stop)
            {
                var header = (WaveHeader)Marshal.PtrToStructure(_headers[slot], typeof(WaveHeader));
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
            _stop = true;
            _resume.Set();

            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            if (handle == IntPtr.Zero) return;

            try { waveOutReset(handle); } catch { }

            Thread thread = _thread;
            _thread = null;
            if (thread != null)
            {
                try { thread.Join(1000); } catch { }
            }

            int headerSize = Marshal.SizeOf(typeof(WaveHeader));
            for (int i = 0; i < BufferCount; i++)
            {
                if (_headers[i] != IntPtr.Zero)
                {
                    try { waveOutUnprepareHeader(handle, _headers[i], headerSize); } catch { }
                    Marshal.FreeHGlobal(_headers[i]);
                    _headers[i] = IntPtr.Zero;
                }

                if (_buffers[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_buffers[i]);
                    _buffers[i] = IntPtr.Zero;
                }

                _queued[i] = false;
            }

            _next = 0;
            try { waveOutClose(handle); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
