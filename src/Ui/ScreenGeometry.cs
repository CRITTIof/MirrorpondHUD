using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MirrorpondHUD.Core;

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
    static readonly LogThrottle _enumFailure = new("枚举显示器", 60000);
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

    static string _warnedDevice = "";
    static int _warnedIndex = int.MinValue;

    public static ScreenInfo Get(int index)
    {
        Ensure();
        if (_screens.Count == 0) return null;

        ScreenInfo screen = ByIndex(index);
        if (screen is not null) return screen;

        // G-14：越界不再静默钳制到最后一台（那是一台完全不同的显示器）。
        // 记一次 WARN 后回退主屏；用静态字段去重，避免每次布局 / 每帧刷日志。
        if (index != _warnedIndex)
        {
            _warnedIndex = index;
            Log.Warn($"显示器序号 {index} 超出当前 {_screens.Count} 台，已回退主屏");
        }
        return Primary();
    }

    /// <summary>
    /// 运行时按「设备名优先、序号兜底」取显示器（G-14）。热插拔 / 驱动重排后序号可能
    /// 已经指向别的屏幕，设备名仍能找回用户当初选的那台。
    /// </summary>
    public static ScreenInfo Get(int index, string deviceName)
    {
        Ensure();
        if (_screens.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            ScreenInfo match = ByDevice(deviceName);
            if (match is not null) return match;
        }

        return Get(index);
    }

    /// <summary>
    /// 解析并就地同步设置里的显示器身份（G-14）。宿主归一化钩子在 Load / Save 时都会调用：
    /// 设备名命中就把序号刷成当前枚举值；设备不存在则 Warn 并回退主屏；
    /// 旧配置（MonitorDevice 为空）按旧序号做一次性迁移并写入设备名。
    /// </summary>
    public static ScreenInfo Resolve(OverlaySettings overlay)
    {
        Ensure();
        if (overlay is null || _screens.Count == 0) return null;

        string device = (overlay.MonitorDevice ?? "").Trim();
        if (device.Length > 0)
        {
            ScreenInfo match = ByDevice(device);
            if (match is not null)
            {
                overlay.Monitor = match.Index;
                return match;
            }

            // 设备被拔掉 / 驱动改名：不能静默钳制，明确记录后回退主屏（与旧行为不同）。
            if (!string.Equals(_warnedDevice, device, StringComparison.OrdinalIgnoreCase))
            {
                _warnedDevice = device;
                Log.Warn($"设置里的显示器「{device}」当前不存在（已拔掉 / 驱动改名），已回退主屏");
            }
            return FallbackToPrimary(overlay);
        }

        ScreenInfo byIndex = ByIndex(overlay.Monitor);
        if (byIndex is null)
        {
            if (overlay.Monitor != _warnedIndex)
            {
                _warnedIndex = overlay.Monitor;
                Log.Warn($"旧配置的显示器序号 {overlay.Monitor} 超出当前 {_screens.Count} 台，已回退主屏并改存设备名");
            }
            return FallbackToPrimary(overlay);
        }

        overlay.Monitor = byIndex.Index;
        overlay.MonitorDevice = byIndex.DeviceName ?? "";
        return byIndex;
    }

    /// <summary>按设备名取显示器；名称不存在时返回 null（不抛、不钳制）。</summary>
    public static ScreenInfo ByDevice(string deviceName)
    {
        Ensure();
        if (_screens.Count == 0 || string.IsNullOrWhiteSpace(deviceName)) return null;

        string wanted = deviceName.Trim();
        foreach (ScreenInfo screen in _screens)
        {
            if (string.Equals(screen.DeviceName, wanted, StringComparison.OrdinalIgnoreCase)) return screen;
        }
        return null;
    }

    public static string DeviceNameOf(int index)
    {
        Ensure();
        // 这里刻意不经过 Get(index)：序号越界时 Get 会回退主屏，若把主屏设备名配着
        // 越界序号一起写回设置，保存后的身份与序号会短暂不一致。越界就返回空，
        // 由 Resolve 在下一次 Load / Save 时决定回退哪台。
        if (index < 0 || index >= _screens.Count) return "";
        return _screens[index].DeviceName ?? "";
    }

    static ScreenInfo ByIndex(int index)
    {
        if (index < 0 || index >= _screens.Count) return null;
        return _screens[index];
    }

    static ScreenInfo Primary()
    {
        foreach (ScreenInfo screen in _screens)
        {
            if (screen.Primary) return screen;
        }
        return _screens[0];
    }

    static ScreenInfo FallbackToPrimary(OverlaySettings overlay)
    {
        ScreenInfo primary = Primary();
        overlay.Monitor = primary.Index;
        overlay.MonitorDevice = primary.DeviceName ?? "";
        return primary;
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
        Screen[] screens;
        try
        {
            screens = Screen.AllScreens;
        }
        catch (Exception ex)
        {
            // G-14 起这个枚举会出现在设置 Load / Save 的归一化路径上：
            // 无桌面会话（服务 / CI）里枚举失败时绝不能抛给 ConfigStore，否则会被当成
            // “设置归一化失败”而备份并用默认设置覆盖。这里保留原设置（返回空屏列表），
            // 按节流记一条，下一次访问再重试。
            _enumFailure.Failure(ex.Message);
            if (_screens is null) _screens = new List<ScreenInfo>();
            return;
        }

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
