using System;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace WindowOverlay.Ui
{
    internal static class Native
    {
        public const int GwlExStyle = -20;

        public const int WsExTransparent = 0x00000020;
        public const int WsExToolWindow = 0x00000080;
        public const int WsExNoActivate = 0x08000000;
        public const int WsExLayered = 0x00080000;

        public static readonly IntPtr HwndTopmost = new IntPtr(-1);

        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpNoOwnerZorder = 0x0200;
        public const uint SwpFrameChanged = 0x0020;

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        /// <summary>
        /// 把窗口强行切到前台并拿到键盘焦点。热键触发的程序通常可以直接 SetForegroundWindow，
        /// 失败时用 AttachThreadInput 挂到前台线程上再试一次。
        /// </summary>
        public static bool ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            try
            {
                if (GetForegroundWindow() == hwnd) return true;

                uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out uint _);
                uint ourThread = GetCurrentThreadId();
                bool attached = false;

                if (foregroundThread != 0 && foregroundThread != ourThread)
                    attached = AttachThreadInput(ourThread, foregroundThread, true);

                try
                {
                    SetForegroundWindow(hwnd);
                    SetFocus(hwnd);
                }
                finally
                {
                    if (attached) AttachThreadInput(ourThread, foregroundThread, false);
                }

                return GetForegroundWindow() == hwnd;
            }
            catch
            {
                return false;
            }
        }

        public static int GetExStyle(IntPtr hwnd)
        {
            if (IntPtr.Size == 8)
            {
                IntPtr value = GetWindowLongPtr64(hwnd, GwlExStyle);
                return unchecked((int)value.ToInt64());
            }
            return GetWindowLong(hwnd, GwlExStyle);
        }

        public static void SetExStyle(IntPtr hwnd, int value)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(value));
                return;
            }
            SetWindowLong(hwnd, GwlExStyle, value);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

        // ==================== 毛玻璃（抓屏时的窗口排除） ====================

        const uint WdaNone = 0x00000000;
        const uint WdaExcludeFromCapture = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        /// <summary>抓屏 + 模糊需要 WDA_EXCLUDEFROMCAPTURE（Windows 10 2004 / build 19041 起）。</summary>
        public static bool IsAcrylicSupported
        {
            get
            {
                try { return Environment.OSVersion.Version.Build >= 19041; }
                catch { return false; }
            }
        }

        /// <summary>让窗口在截屏 / 抓屏时不可见（用户仍然看得到），用于抓取叠层后面的桌面。</summary>
        public static void SetCaptureExclusion(IntPtr hwnd, bool exclude)
        {
            if (hwnd == IntPtr.Zero) return;
            try { SetWindowDisplayAffinity(hwnd, exclude ? WdaExcludeFromCapture : WdaNone); }
            catch { }
        }

    }
}
