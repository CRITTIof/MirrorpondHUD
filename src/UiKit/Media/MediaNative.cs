using System;
using System.Runtime.InteropServices;

namespace MirrorpondHUD.Ui.Controls.Media
{
    /// <summary>
    /// 视频子窗口只需要扩展样式；不把宿主 Ui.Native 整个搬进 UiKit，
    /// 这里只保留媒体窗用到的三个常量 / 两个方法，避免 UiKit 依赖宿主程序集。
    /// </summary>
    internal static class MediaNative
    {
        const int GwlExStyle = -20;

        public const int WsExTransparent = 0x00000020;
        public const int WsExToolWindow = 0x00000080;
        public const int WsExNoActivate = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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
