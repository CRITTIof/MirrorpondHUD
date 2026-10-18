using System;
using System.Runtime.InteropServices;

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
    }
}
