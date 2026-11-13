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

    /// <summary>S-16：复制一个 HICON，供兜底托盘图标使用（副本由本类销毁，不是共享实例）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr CopyIcon(IntPtr hIcon);

    /// <summary>气泡正文缓冲 256 字符：自行截断并补省略号，避免 ByValTStr 静默吃字。</summary>
    const int BalloonTextMax = 200;

    /// <summary>气泡标题缓冲 64 字符。</summary>
    const int BalloonTitleMax = 60;

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

    /// <summary>
    /// S-16：句柄是否归本类销毁。只有从共享 HICON 复制出来的兜底图标为 true ——
    /// <c>Icon.FromHandle</c> 不接管句柄所有权，必须自己 DestroyIcon。
    /// </summary>
    readonly bool _ownsIconHandle;

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
        (Icon icon, bool ownsHandle) loaded = LoadIcon();
        _icon = loaded.icon;
        _ownsIconHandle = loaded.ownsHandle;

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
        catch (Exception ex)
        {
            // 这是一条「图标到底进没进托盘」的诊断探针：失败等价于“读不到位置”，
            // 上层只少一条 INFO，不改图标状态，因此按 Debug 记录即可。
            Log.Debug($"Shell_NotifyIconGetRect 读取托盘位置失败: {ex.Message}");
            return false;
        }
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
        // S-17：删除失败时图标会一直残留在托盘区直到 Explorer 重启，必须留痕。
        if (!Shell_NotifyIcon(NIM_DELETE, ref data))
            Log.Warn($"Shell_NotifyIcon(NIM_DELETE) 失败 err={Marshal.GetLastWin32Error()}，图标可能残留在托盘区");
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
        if (_disposed) return;
        if (!_added)
        {
            // 托盘被用户关掉 / 添加失败时气泡确实无处可显示：这是状态而不是发送异常，
            // 但也不能静默 —— 热键冲突提示主要靠它，必须留一条可归因的记录。
            Log.Info("托盘气泡未显示：托盘图标当前不在托盘区（用户关闭了托盘或添加失败）");
            return;
        }

        string safeTitle = ClampBalloon(title, BalloonTitleMax, "标题");
        string safeText = ClampBalloon(text, BalloonTextMax, "正文");

        try
        {
            var data = NewData();
            data.uFlags = NIF_INFO;
            data.szInfoTitle = safeTitle;
            data.szInfo = safeText;
            data.dwInfoFlags = NIIF_INFO;
            data.uVersion = 0;
            if (!Shell_NotifyIcon(NIM_MODIFY, ref data))
            {
                // S-17：专注助手 / 通知策略关闭时 Shell 直接返回 false，用户什么都看不到。
                Log.Warn($"托盘气泡通知发送失败（Shell_NotifyIcon 返回 false）err={Marshal.GetLastWin32Error()}，"
                         + $"用户可能没看到「{safeTitle}」这条提示");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"托盘气泡通知异常: {ex.Message}（标题：{safeTitle}）");
        }
    }

    /// <summary>
    /// S-17：szInfo / szInfoTitle 是定长 ByValTStr，超长会被封送层静默截断成半句话。
    /// 这里主动截到上限并补省略号，让用户知道内容没有显示全（同时避免封送层行为依赖）。
    /// </summary>
    static string ClampBalloon(string value, int max, string what)
    {
        string text = value ?? "";
        if (text.Length <= max) return text;

        Log.Info($"托盘气泡{what}超过 {max} 字符，已截断补省略号（原长 {text.Length}）");
        return text.Substring(0, max - 1) + "…";
    }

    static (Icon Icon, bool OwnsHandle) LoadIcon()
    {
        // 深色任务栏用浅色版本，浅色任务栏用深色版本
        // 2.45.5：亮色任务栏用 DeepYellow 品牌图标；暗色任务栏仍用白色线稿，避免黄底发闷。
        string name = IsLightTaskbar() ? "assets/MirrorpondHUDLogoDeepYellow.ico" : "assets/logo-light.ico";
        try
        {
            System.Windows.Resources.StreamResourceInfo info =
                System.Windows.Application.GetResourceStream(new Uri(name, UriKind.Relative));
            if (info is not null)
            {
                var icon = new Icon(info.Stream, new System.Drawing.Size(32, 32));
                info.Stream.Dispose();
                // 资源图标自己拥有句柄，Dispose 时由 Icon 自己销毁。
                return (icon, false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"加载托盘图标失败，改用系统图标: {ex.Message}");
        }

        // S-16：SystemIcons.Application 是进程级共享实例，直接 Dispose 会销毁整个进程共用的 HICON，
        // 之后任何再取 SystemIcons.* 的代码都会拿到失效句柄。兜底图标必须是自有副本：
        // CopyIcon 得到独立句柄 → Icon.FromHandle（不接管所有权）→ Dispose 时显式 DestroyIcon。
        IntPtr copy = CopyIcon(SystemIcons.Application.Handle);
        if (copy != IntPtr.Zero) return (Icon.FromHandle(copy), true);

        // 连复制都失败（几乎不可能）：退回 Clone —— 它同样会复制句柄，可安全 Dispose。
        Log.Warn($"复制系统兜底图标失败 err={Marshal.GetLastWin32Error()}，改用 Clone 副本");
        return ((Icon)SystemIcons.Application.Clone(), false);
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

        SafeRelease.Run(RemoveIcon, "托盘图标移除");
        SafeRelease.Run(_window.Destroy, "托盘宿主窗口");

        SafeRelease.Run(() =>
        {
            // S-16：只有 CopyIcon 出来的兜底句柄需要显式销毁；
            // Icon.FromHandle 的 Dispose 不会释放 HICON，而资源图标由 Icon 自己负责。
            if (_ownsIconHandle) DestroyIcon(_icon.Handle);
            _icon.Dispose();
        }, "托盘图标");
    }
}
