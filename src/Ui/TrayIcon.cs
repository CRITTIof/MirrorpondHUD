using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinForms = System.Windows.Forms;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 原生 Shell_NotifyIcon 托盘图标：
/// · 左键单击呼出设置，右键弹出菜单
/// · 监听 TaskbarCreated，Explorer 重启后自动恢复
/// · 自己验证图标是否真的进了托盘（Shell_NotifyIconGetRect）
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const uint NIM_ADD = 0x00000000;
    const uint NIM_MODIFY = 0x00000001;
    const uint NIM_DELETE = 0x00000002;
    const uint NIM_SETVERSION = 0x00000004;

    const uint NIF_MESSAGE = 0x00000001;
    const uint NIF_ICON = 0x00000002;
    const uint NIF_TIP = 0x00000004;
    const uint NIF_INFO = 0x00000010;
    const uint NIF_SHOWTIP = 0x00000080;

    const uint NOTIFYICON_VERSION_4 = 4;
    const uint NIIF_INFO = 0x00000001;

    const int WM_APP = 0x8000;
    const int WM_USER = 0x0400;
    const int WM_CALLBACK = WM_APP + 101;
    // NOTIFYICON_VERSION_4：NIN_SELECT = WM_USER + 0、NIN_KEYSELECT = WM_USER + 1。
    // 之前误写成 WM_APP + 400/401（0x8190/0x8191），switch 永不命中，托盘左键单击完全失效。
    const int NIN_SELECT = WM_USER + 0;
    const int NIN_KEYSELECT = WM_USER + 1;
    // NIM_SETVERSION 失败退回 v3 时，Shell 仍发经典鼠标消息；v3 的 iconId 在 wParam。
    const int WM_LBUTTONUP = 0x0202;
    const int WM_RBUTTONUP = 0x0205;
    const int WM_CONTEXTMENU = 0x007B;

    const uint TRAY_ICON_ID = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NotifyIconIdentifier
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll")]
    static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);

    sealed class TrayHostWindow : WinForms.NativeWindow
    {
        readonly uint _taskbarCreated;
        readonly Action<int, int> _callback;
        readonly Action _taskbarRebuilt;

        public TrayHostWindow(Action<int, int> callback, Action taskbarRebuilt)
        {
            _callback = callback;
            _taskbarRebuilt = taskbarRebuilt;
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

            CreateHandle(new WinForms.CreateParams
            {
                Caption = "MirrorpondHUD.TrayHost",
                Style = 0,
                ExStyle = 0,
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0
            });
        }

        protected override void WndProc(ref WinForms.Message m)
        {
            if (_taskbarCreated != 0 && m.Msg == (int)_taskbarCreated)
            {
                _taskbarRebuilt();
                return;
            }

            if (m.Msg == WM_CALLBACK)
            {
                // v3 回退：wParam 是鼠标消息本身（iconId 也放在 wParam），lParam 是坐标。
                int wparam = m.WParam.ToInt32();
                if (wparam == WM_LBUTTONUP || wparam == WM_RBUTTONUP)
                {
                    _callback(wparam, (int)TRAY_ICON_ID);
                    return;
                }

                int lparam = m.LParam.ToInt32();
                int message = lparam & 0xFFFF;
                int iconId = (lparam >> 16) & 0xFFFF;
                _callback(message, iconId);
                return;
            }

            base.WndProc(ref m);
        }

        public void Destroy()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    readonly TrayHostWindow _window;
    readonly ContextMenu _menu;
    readonly MenuItem _visibilityItem;
    readonly MenuItem _settingsItem;
    readonly MenuItem _lockItem;
    readonly Icon _icon;

    bool _added;
    bool _addFailed;
    bool _disposed;
    // S-12：用户是否希望显示托盘。Explorer 重启后只能按“期望”重加，
    // 不能无条件 AddIcon——否则用户关掉的托盘会在任务栏重启后自己回来。
    bool _wanted;

    public event Action ToggleVisibility;
    public event Action OpenSettings;
    public event Action ToggleLock;
    public event Action Exit;

    public TrayIcon()
    {
        _icon = LoadIcon();

        // 用 WPF 的 ContextMenu：它本身是工具窗口，不会在任务栏里留下一个窗口。
        _menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            StaysOpen = false
        };
        HudMenuStyle.Apply(_menu);

        _visibilityItem = new MenuItem { Header = "隐藏叠层" };
        _visibilityItem.Click += (s, e) => { ToggleVisibility?.Invoke(); };
        _menu.Items.Add(_visibilityItem);

        _settingsItem = new MenuItem { Header = "打开设置" };
        _settingsItem.Click += (s, e) => { OpenSettings?.Invoke(); };
        _menu.Items.Add(_settingsItem);

        _lockItem = new MenuItem { Header = "解锁" };
        _lockItem.Click += (s, e) => { ToggleLock?.Invoke(); };
        _menu.Items.Add(_lockItem);

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (s, e) => { Exit?.Invoke(); };
        _menu.Items.Add(exit);

        _window = new TrayHostWindow(OnTrayMessage, OnTaskbarRebuilt);
    }

    public bool Visible
    {
        get => _added;
        set
        {
            if (_disposed) return;
            _wanted = value;
            if (value) AddIcon();
            else RemoveIcon();
        }
    }

    /// <summary>是否真的显示在托盘里（用 Shell_NotifyIconGetRect 验证）。</summary>
    public bool IsPresentInTray => TryGetRect(out var left, out var top, out var right, out var bottom);

    /// <summary>
    /// 最近一次 <see cref="Visible"/>=true 请求后图标是否真的加进了托盘。
    /// 与 <see cref="Visible"/>（getter = <c>_added</c>）分开：设置里的开关关掉托盘时
    /// <see cref="Visible"/> 也是 false，但那不叫「不可用」（P0-1）。
    /// </summary>
    public bool AddFailed => _addFailed;

    public bool TryGetRect(out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        try
        {
            var id = new NotifyIconIdentifier
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NotifyIconIdentifier)),
                hWnd = _window.Handle,
                uID = TRAY_ICON_ID
            };
            int hr = Shell_NotifyIconGetRect(ref id, out var rect);
            if (hr != 0) return false;
            left = rect.Left;
            top = rect.Top;
            right = rect.Right;
            bottom = rect.Bottom;
            return true;
        }
        catch { return false; }
    }

    void AddIcon()
    {
        if (_added) return;

        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;

        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            // 上游 Visible 属性无法区分「用户关了托盘」和「添加失败」——
            // 这里把添加失败暴露出去，调用方才能做正确的兜底（P0-1）。
            _addFailed = true;
            Log.Warn($"Shell_NotifyIcon(NIM_ADD) 失败，err={Marshal.GetLastWin32Error()}");
            return;
        }

        _addFailed = false;

        data.uVersion = NOTIFYICON_VERSION_4;
        if (!Shell_NotifyIcon(NIM_SETVERSION, ref data))
        {
            // 版本协商失败只影响 v4 回调语义（仍按 v0 处理），图标照常可用，不算添加失败。
            Log.Warn($"Shell_NotifyIcon(NIM_SETVERSION) 失败，err={Marshal.GetLastWin32Error()}");
        }

        _added = true;

        if (IsPresentInTray)
        {
            Log.Info("托盘图标已进入托盘区");
        }
        else
        {
            Log.Warn($"托盘图标已添加，但 Shell_NotifyIconGetRect 未返回位置（Win11 新图标默认收在“隐藏图标”里，需要手动固定）");
        }
    }

    void RemoveIcon()
    {
        if (!_added) return;
        var data = NewData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
    }

    NotifyIconData NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
        hWnd = _window.Handle,
        uID = TRAY_ICON_ID,
        uCallbackMessage = WM_CALLBACK,
        hIcon = _icon.Handle,
        szTip = "镜潭一览"
    };

    void OnTrayMessage(int message, int iconId)
    {
        if (iconId != 0 && iconId != TRAY_ICON_ID) return;

        switch (message)
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
            case WM_LBUTTONUP:
                OpenSettings?.Invoke();
                break;

            case WM_CONTEXTMENU:
            case WM_RBUTTONUP:
                try
                {
                    _menu.Placement = PlacementMode.MousePoint;
                    _menu.PlacementTarget = null;
                    _menu.IsOpen = true;
                }
                catch (Exception ex)
                {
                    Log.Warn($"显示托盘菜单失败: {ex.Message}");
                }
                break;
        }
    }

    void OnTaskbarRebuilt()
    {
        Log.Info("检测到任务栏重建（Explorer 重启），重新添加托盘图标");
        _added = false;
        _addFailed = false;
        if (!_wanted)
        {
            Log.Info("托盘已被用户关闭，任务栏重建后不再重新添加");
            return;
        }
        AddIcon();
    }

    public void Update(bool overlayVisible, bool locked,
                       string toggleHotkey, string settingsHotkey, string lockHotkey)
    {
        _visibilityItem.Header = overlayVisible ? "隐藏叠层" : "显示叠层";
        _visibilityItem.InputGestureText = toggleHotkey ?? "";

        _settingsItem.InputGestureText = settingsHotkey ?? "";

        _lockItem.Header = locked ? "解锁" : "锁定";
        _lockItem.InputGestureText = lockHotkey ?? "";
    }

    public void ShowBalloon(string title, string text)
    {
        if (_disposed || !_added) return;

        try
        {
            var data = NewData();
            data.uFlags = NIF_INFO;
            data.szInfoTitle = title;
            data.szInfo = text;
            data.dwInfoFlags = NIIF_INFO;
            data.uVersion = 0;
            if (!Shell_NotifyIcon(NIM_MODIFY, ref data))
                Log.Warn("托盘气泡通知发送失败（Shell_NotifyIcon 返回 false）");
        }
        catch (Exception ex)
        {
            Log.Warn($"托盘气泡通知异常: {ex.Message}");
        }
    }

    static Icon LoadIcon()
    {
        // 深色任务栏用浅色版本，浅色任务栏用深色版本
        string name = IsLightTaskbar() ? "assets/logo.ico" : "assets/logo-light.ico";
        try
        {
            System.Windows.Resources.StreamResourceInfo info =
                System.Windows.Application.GetResourceStream(new Uri(name, UriKind.Relative));
            if (info is not null)
            {
                var icon = new Icon(info.Stream, new System.Drawing.Size(32, 32));
                info.Stream.Dispose();
                return icon;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"加载托盘图标失败，改用系统图标: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    static bool IsLightTaskbar()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object value = key is null ? null : key.GetValue("SystemUsesLightTheme");
                if (value is int) return (int)value != 0;
            }
        }
        catch (Exception ex)
        {
            // X-9：读不到系统主题时仍按深色任务栏处理，但要把原因记下来。
            Log.Warn($"读取任务栏明暗主题失败，按深色处理: {ex.Message}");
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { RemoveIcon(); } catch { }
        try { _window.Destroy(); } catch { }

        try { _icon.Dispose(); } catch { }
    }
}
