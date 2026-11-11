using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MirrorpondHUD.Ui;

/// <summary>一台显示器的物理几何与缩放。坐标一律是物理像素（PerMonitorV2）。</summary>
internal sealed class ScreenInfo
{
    public int Index;
    public string DeviceName;
    public bool Primary;

    public int Left;
    public int Top;
    public int Width;
    public int Height;

    public int WorkLeft;
    public int WorkTop;
    public int WorkWidth;
    public int WorkHeight;

    public int DpiX = 96;
    public int DpiY = 96;

    public double ScaleX = 1.0;
    public double ScaleY = 1.0;

    public bool Contains(int x, int y) => x >= Left && x < Left + Width && y >= Top && y < Top + Height;
}

/// <summary>
/// 显示器几何的唯一来源（L-13）：用 Win32 物理坐标 + 每屏 DPI，
/// 不再用「主屏缩放比例」去换算整块虚拟屏。
///
/// WPF 的 <c>Window.Left/Top</c> 在 PerMonitorV2 下会按目标屏 DPI 再乘一次，
/// 混合 DPI 时无法用 DIP 可靠跨屏（4K@300% 上 Left=1920 会被放到物理 5760）。
/// 因此定位一律走 <see cref="Native.SetWindowPos"/> 物理坐标；锚点仍是 DIP，
/// 用本类按目标屏缩放换算。
///
/// 缓存只在首次访问 / <see cref="Invalidate"/> 后重建；DPI 变化由窗口的
/// DpiChanged 事件触发失效，避免每帧枚举显示器。
/// </summary>
internal static class ScreenGeometry
{
    [StructLayout(LayoutKind.Sequential)]
    struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point32 pt, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    const uint MonitorDefaultToNearest = 2;
    const int MdtEffectiveDpi = 0;

    static readonly object Sync = new();
    static List<ScreenInfo> _screens;
    static int _signature;

    /// <summary>按 <see cref="Screen.AllScreens"/> 的顺序返回（设置里的 Monitor 就是这个序号）。</summary>
    public static IList<ScreenInfo> All
    {
        get
        {
            Ensure();
            return _screens;
        }
    }

    public static void Invalidate()
    {
        lock (Sync)
        {
            _screens = null;
            _signature = 0;
        }
    }

    public static ScreenInfo Get(int index)
    {
        Ensure();
        if (_screens.Count == 0) return null;
        return _screens[Math.Max(0, Math.Min(index, _screens.Count - 1))];
    }

    /// <summary>物理点所在显示器；不在任何屏上时取最近的一台。</summary>
    public static ScreenInfo ForPoint(int x, int y)
    {
        Ensure();
        if (_screens.Count == 0) return null;

        foreach (ScreenInfo screen in _screens)
        {
            if (screen.Contains(x, y)) return screen;
        }

        ScreenInfo best = _screens[0];
        long bestDistance = long.MaxValue;
        foreach (ScreenInfo screen in _screens)
        {
            long cx = screen.Left + screen.Width / 2;
            long cy = screen.Top + screen.Height / 2;
            long dx = x - cx;
            long dy = y - cy;
            long distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = screen;
            }
        }
        return best;
    }

    public static int IndexForPoint(int x, int y)
    {
        ScreenInfo screen = ForPoint(x, y);
        return screen is null ? 0 : screen.Index;
    }

    static void Ensure()
    {
        // 显示器数量 / 矩形变了（插拔、改分辨率、换主屏）就重建；DPI 单独变化由
        // 窗口的 DpiChanged 调 Invalidate。这里只枚举 Screen（便宜），不重复调
        // GetDpiForMonitor —— 后者才是需要缓存的 P/Invoke。
        Screen[] screens = Screen.AllScreens;
        int signature = Signature(screens);

        if (_screens is not null && _signature == signature) return;

        lock (Sync)
        {
            if (_screens is not null && _signature == signature) return;
            _screens = Build(screens);
            _signature = signature;
        }
    }

    /// <summary>只覆盖几何变化的轻量签名：数量 + 每屏 bounds / work / 主屏标记。</summary>
    static int Signature(Screen[] screens)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + screens.Length;
            for (int i = 0; i < screens.Length; i++)
            {
                Screen screen = screens[i];
                Rectangle b = screen.Bounds;
                Rectangle w = screen.WorkingArea;
                hash = hash * 31 + b.Left;
                hash = hash * 31 + b.Top;
                hash = hash * 31 + b.Width;
                hash = hash * 31 + b.Height;
                hash = hash * 31 + w.Left;
                hash = hash * 31 + w.Top;
                hash = hash * 31 + w.Width;
                hash = hash * 31 + w.Height;
                hash = hash * 31 + (screen.Primary ? 1 : 0);
            }
            return hash;
        }
    }

    static List<ScreenInfo> Build(Screen[] screens)
    {
        var result = new List<ScreenInfo>();

        for (int i = 0; i < screens.Length; i++)
        {
            Screen screen = screens[i];
            Rectangle bounds = screen.Bounds;
            Rectangle work = screen.WorkingArea;

            var info = new ScreenInfo
            {
                Index = i,
                DeviceName = screen.DeviceName,
                Primary = screen.Primary,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                WorkLeft = work.Left,
                WorkTop = work.Top,
                WorkWidth = work.Width,
                WorkHeight = work.Height
            };


            var center = new Point32
            {
                X = bounds.Left + bounds.Width / 2,
                Y = bounds.Top + bounds.Height / 2
            };

            IntPtr monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out var dpiY) == 0)
            {
                if (dpiX < 48 || dpiX > 960) dpiX = 96;
                if (dpiY < 48 || dpiY > 960) dpiY = 96;
            }
            else
            {
                dpiX = 96;
                dpiY = 96;
            }

            info.DpiX = (int)dpiX;
            info.DpiY = (int)dpiY;
            info.ScaleX = dpiX / 96.0;
            info.ScaleY = dpiY / 96.0;

            result.Add(info);
        }

        return result;
    }
}
