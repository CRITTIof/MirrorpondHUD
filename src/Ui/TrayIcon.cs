using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
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
        const int WM_CALLBACK = WM_APP + 101;
        const int NIN_SELECT = WM_APP + 400;
        const int NIN_KEYSELECT = WM_APP + 401;
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

        sealed class TrayHostWindow : NativeWindow
        {
            readonly uint _taskbarCreated;
            readonly Action<int, int> _callback;
            readonly Action _taskbarRebuilt;

            public TrayHostWindow(Action<int, int> callback, Action taskbarRebuilt)
            {
                _callback = callback;
                _taskbarRebuilt = taskbarRebuilt;
                _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

                CreateHandle(new CreateParams
                {
                    Caption = "WindowOverlay.TrayHost",
                    Style = 0,
                    ExStyle = 0,
                    X = 0,
                    Y = 0,
                    Width = 0,
                    Height = 0
                });
            }

            protected override void WndProc(ref Message m)
            {
                if (_taskbarCreated != 0 && m.Msg == (int)_taskbarCreated)
                {
                    _taskbarRebuilt();
                    return;
                }

                if (m.Msg == WM_CALLBACK)
                {
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
        readonly ContextMenuStrip _menu;
        readonly ToolStripMenuItem _visibilityItem;
        readonly ToolStripMenuItem _commandItem;
        readonly ToolStripMenuItem _lockItem;
        readonly ToolStripMenuItem _autoStartItem;
        readonly Icon _icon;
        readonly IntPtr _iconHandle;

        bool _added;
        bool _disposed;

        public event Action ToggleVisibility;
        public event Action OpenSettings;
        public event Action ToggleLock;
        public event Action ToggleAutoStart;
        public event Action CommandLine;
        public event Action Exit;

        public TrayIcon()
        {
            _icon = CreateIcon(out _iconHandle);

            _menu = new ContextMenuStrip();

            _visibilityItem = new ToolStripMenuItem("隐藏叠层");
            _visibilityItem.Click += (s, e) => { ToggleVisibility?.Invoke(); };
            _menu.Items.Add(_visibilityItem);

            _commandItem = new ToolStripMenuItem("快捷命令行");
            _commandItem.Click += (s, e) => { CommandLine?.Invoke(); };
            _menu.Items.Add(_commandItem);

            var settings = new ToolStripMenuItem("打开设置…");
            settings.Font = new Font(settings.Font, System.Drawing.FontStyle.Bold);
            settings.Click += (s, e) => { OpenSettings?.Invoke(); };
            _menu.Items.Add(settings);

            _lockItem = new ToolStripMenuItem("锁定叠层");
            _lockItem.Click += (s, e) => { ToggleLock?.Invoke(); };
            _menu.Items.Add(_lockItem);

            _menu.Items.Add(new ToolStripSeparator());

            _autoStartItem = new ToolStripMenuItem("开机自动启动");
            _autoStartItem.Click += (s, e) => { ToggleAutoStart?.Invoke(); };
            _menu.Items.Add(_autoStartItem);

            _menu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("退出");
            exit.Click += (s, e) => { Exit?.Invoke(); };
            _menu.Items.Add(exit);

            _window = new TrayHostWindow(OnTrayMessage, OnTaskbarRebuilt);
        }

        public bool Visible
        {
            get { return _added; }
            set
            {
                if (_disposed) return;
                if (value) AddIcon();
                else RemoveIcon();
            }
        }

        /// <summary>是否真的显示在托盘里（用 Shell_NotifyIconGetRect 验证）。</summary>
        public bool IsPresentInTray
        {
            get
            {
                int left, top, right, bottom;
                return TryGetRect(out left, out top, out right, out bottom);
            }
        }

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
                Rect rect;
                int hr = Shell_NotifyIconGetRect(ref id, out rect);
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
                Log.Warn("Shell_NotifyIcon(NIM_ADD) 失败，err=" + Marshal.GetLastWin32Error());
                return;
            }

            data.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref data);

            _added = true;

            if (IsPresentInTray)
            {
                Log.Info("托盘图标已进入托盘区");
            }
            else
            {
                Log.Warn("托盘图标已添加，但 Shell_NotifyIconGetRect 未返回位置" +
                         "（Win11 新图标默认收在“隐藏图标”里，需要手动固定）");
            }
        }

        void RemoveIcon()
        {
            if (!_added) return;
            var data = NewData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        NotifyIconData NewData()
        {
            return new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
                hWnd = _window.Handle,
                uID = TRAY_ICON_ID,
                uCallbackMessage = WM_CALLBACK,
                hIcon = _iconHandle,
                szTip = "桌面叠层"
            };
        }

        void OnTrayMessage(int message, int iconId)
        {
            if (iconId != 0 && iconId != TRAY_ICON_ID) return;

            switch (message)
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    OpenSettings?.Invoke();
                    break;

                case WM_CONTEXTMENU:
                    try
                    {
                        _menu.Show(Cursor.Position);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("显示托盘菜单失败: " + ex.Message);
                    }
                    break;
            }
        }

        void OnTaskbarRebuilt()
        {
            Log.Info("检测到任务栏重建（Explorer 重启），重新添加托盘图标");
            _added = false;
            AddIcon();
        }

        public void Update(bool overlayVisible, bool locked, bool autoStart)
        {
            _visibilityItem.Text = overlayVisible ? "隐藏叠层" : "显示叠层";
            _lockItem.Text = locked ? "解锁（可拖动）" : "锁定（鼠标穿透）";
            _autoStartItem.Checked = autoStart;
        }

        public void SetCommandHotkey(string hotkey)
        {
            _commandItem.Text = string.IsNullOrWhiteSpace(hotkey)
                ? "快捷命令行"
                : "快捷命令行 (" + hotkey + ")";
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
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
            catch { }
        }

        static Icon CreateIcon(out IntPtr hicon)
        {
            hicon = IntPtr.Zero;
            const int size = 32;

            try
            {
                using (var bitmap = new Bitmap(size, size))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        using (var back = new SolidBrush(Color.FromArgb(255, 24, 32, 44)))
                            g.FillRectangle(back, 2, 4, 28, 20);
                        using (var screen = new SolidBrush(Color.FromArgb(255, 47, 111, 235)))
                            g.FillRectangle(screen, 4, 6, 24, 16);

                        using (var bar = new SolidBrush(Color.White))
                        {
                            g.FillRectangle(bar, 7, 9, 10, 2);
                            g.FillRectangle(bar, 7, 13, 16, 2);
                            g.FillRectangle(bar, 7, 17, 7, 2);
                        }

                        using (var stand = new SolidBrush(Color.FromArgb(255, 24, 32, 44)))
                        {
                            g.FillRectangle(stand, 14, 24, 4, 3);
                            g.FillRectangle(stand, 10, 27, 12, 2);
                        }
                    }

                    hicon = bitmap.GetHicon();
                    return Icon.FromHandle(hicon);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("生成托盘图标失败，改用系统图标: " + ex.Message);
                return SystemIcons.Application;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { RemoveIcon(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _window.Destroy(); } catch { }

            if (_iconHandle != IntPtr.Zero)
            {
                try { DestroyIcon(_iconHandle); } catch { }
            }

            try { _icon.Dispose(); } catch { }
        }
    }
}
