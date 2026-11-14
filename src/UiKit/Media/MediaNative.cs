using System;
using System.Runtime.InteropServices;

namespace MirrorpondHUD.Ui.Controls.Media;

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
    /// <summary>K-10：分层窗口样式（配合 SetLayeredWindowAttributes 做整窗不透明度）。</summary>
    public const int WsExLayered = 0x00080000;
    /// <summary>K-20：置顶样式。用来判断卡片 / 锚点所在 z-order 带，决定视频窗怎么插。</summary>
    public const int WsExTopmost = 0x00000008;

    /// <summary>SetLayeredWindowAttributes 的 LWA_ALPHA：用源 alpha 混合整个窗口。</summary>
    public const int LwaAlpha = 0x00000002;

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>
    /// K-19：读取扩展样式并报告是否真的读到。旧实现直接返回 int，读失败（句柄失效）时
    /// 返回 0，调用方会把"0"当成真实样式再取反写回，等于清掉窗口上其它扩展样式。
    /// </summary>
    public static bool TryGetExStyle(IntPtr hwnd, out int value)
    {
        value = 0;
        if (hwnd == IntPtr.Zero) return false;

        Marshal.SetLastPInvokeError(0);
        IntPtr result;
        if (IntPtr.Size == 8)
        {
            result = GetWindowLongPtr64(hwnd, GwlExStyle);
        }
        else
        {
            result = new IntPtr(GetWindowLong(hwnd, GwlExStyle));
        }

        // 旧样式本来就是 0 也是合法的，只有"返回 0 且 LastError 非 0"才算读取失败。
        if (result == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0) return false;

        value = unchecked((int)result.ToInt64());
        return true;
    }

    /// <summary>
    /// K-19：设置扩展样式并返回是否成功。SetWindowLong 的返回值是"旧值"，可能本来就是 0，
    /// 所以同样用 LastError 判失败，而不是把 old == 0 当成错误。
    /// </summary>
    public static bool SetExStyle(IntPtr hwnd, int value)
    {
        if (hwnd == IntPtr.Zero) return false;

        Marshal.SetLastPInvokeError(0);
        IntPtr result = IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, GwlExStyle, new IntPtr(value))
            : new IntPtr(SetWindowLong(hwnd, GwlExStyle, value));

        return result != IntPtr.Zero || Marshal.GetLastPInvokeError() == 0;
    }

    /// <summary>
    /// K-10：设置整窗 alpha。失败（远程桌面 / 老驱动不支持分层窗口）时返回 false，
    /// 让调用方走"WPF 合成路径"的降级逻辑，而不是悄悄忽略不透明度。
    /// </summary>
    public static bool SetWindowAlpha(IntPtr hwnd, byte alpha)
    {
        if (hwnd == IntPtr.Zero) return false;
        return SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha);
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, int flags);
}
