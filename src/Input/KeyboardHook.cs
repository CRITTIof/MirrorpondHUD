using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowOverlay.Core
{
    /// <summary>WH_KEYBOARD_LL 全局键盘钩子，独立线程带消息循环。</summary>
    public sealed class KeyboardHook : IDisposable
    {
        const int WhKeyboardLl = 13;
        const int WmKeyDown = 0x0100;
        const int WmSysKeyDown = 0x0104;
        const int WmQuit = 0x0012;
        const uint LlkhfInjected = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        struct KbdLlHookStruct
        {
            public uint VkCode;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PointX;
            public int PointY;
        }

        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref Msg msg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref Msg msg);

        [DllImport("user32.dll")]
        static extern int GetMessage(out Msg msg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        // 必须是静态字段，否则委托会被 GC 回收导致钩子回调崩溃
        static LowLevelKeyboardProc _callback;

        readonly KeyHistory _history;
        readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        Thread _thread;
        volatile bool _running;
        uint _threadId;
        IntPtr _hook = IntPtr.Zero;
        int _captured;

        public KeyboardHook(KeyHistory history)
        {
            _history = history;
        }

        /// <summary>仅自检使用：允许接收注入按键，便于自动化验证钩子链路。</summary>
        public bool AllowInjected { get; set; }

        /// <summary>快捷命令行输入时暂停记录，避免命令内容把键盘行刷掉。</summary>
        public volatile bool Suppress;

        public bool IsRunning { get { return _running; } }

        public bool Start()
        {
            if (_thread != null) return _hook != IntPtr.Zero;

            _running = true;
            _ready.Reset();
            _thread = new Thread(Pump) { IsBackground = true, Name = "WindowOverlay.KeyboardHook" };
            _thread.Start();
            _ready.Wait(2000);

            return _hook != IntPtr.Zero;
        }

        void Pump()
        {
            _threadId = GetCurrentThreadId();
            _callback = Callback;

            _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                Log.Warn("键盘钩子安装失败，错误码 " + Marshal.GetLastWin32Error());
            else
                Log.Info("键盘钩子已安装 handle=" + _hook);

            _ready.Set();

            Msg msg;
            while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            Log.Info("键盘钩子线程退出（运行中=" + _running + "）");
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                if (message == WmKeyDown || message == WmSysKeyDown)
                {
                    var info = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
                    bool injected = (info.Flags & LlkhfInjected) != 0;
                    if ((!injected || AllowInjected) && !Suppress)
                    {
                        try
                        {
                            string name = KeyNames.Of((int)info.VkCode);
                            if (Interlocked.Increment(ref _captured) == 1)
                                Log.Info("键盘钩子：已捕获首个按键 " + name);
                            _history.Push(name);
                        }
                        catch (Exception ex) { Log.Warn("按键处理失败: " + ex.Message); }
                    }
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (!_running && _thread == null) return;

            _running = false;
            if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

            Thread thread = _thread;
            _thread = null;
            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(1500); } catch { }
            }

            try { _ready.Dispose(); } catch { }
        }
    }
}
