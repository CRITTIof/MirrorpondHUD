using System;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace MirrorpondHUD.Ui;

internal static class Native
{
    public const int GwlExStyle = -20;

    /// <summary>GetWindow 命令：直接压在目标窗口上面的那个窗口。</summary>
    public const int GwHwndPrev = 3;

    public const int WsExTopmost = 0x00000008;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExLayered = 0x00080000;

    public static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoZorder = 0x0004;
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

    // X-11：src/Ui 与 src/UiKit 是两个程序集，内部 P/Invoke（如 GetWindowLongPtr64）
    // 无法跨程序集共享；这里保留各自的声明，避免把 user32 依赖塞进 Contracts。
    /// <summary>
    /// G-18：返回完整位宽的扩展样式（nint），不再截成 int。
    /// 旧实现的 <c>ToInt64() → (int)</c> 会把高于第 31 位的 WS_EX_* 当成负数，
    /// 再经 <see cref="SetExStyle"/> 写回时等于把那些扩展样式抹掉；当前用到的常量恰好都低于 0x10000000，
    /// 但这是隐式契约，不能依赖。
    /// </summary>
    public static nint GetExStyle(IntPtr hwnd)
    {
        if (IntPtr.Size == 8) return GetWindowLongPtr64(hwnd, GwlExStyle);
        return GetWindowLong(hwnd, GwlExStyle);
    }

    /// <summary>
    /// 设置扩展样式；返回 false 表示这次没有写成功（GetLastError 可读）。
    /// SetWindowLongPtr 的「旧值」本身可能就是 0，必须先把 last error 清零才能区分成功与失败。
    /// </summary>
    public static bool SetExStyle(IntPtr hwnd, nint value)
    {
        Marshal.SetLastPInvokeError(0);

        if (IntPtr.Size == 8)
        {
            IntPtr previous = SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(value));
            return previous != IntPtr.Zero || Marshal.GetLastPInvokeError() == 0;
        }

        int previous32 = SetWindowLong(hwnd, GwlExStyle, unchecked((int)value));
        return previous32 != 0 || Marshal.GetLastPInvokeError() == 0;
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

    [StructLayout(LayoutKind.Sequential)]
    public struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int virtualKey);

}
