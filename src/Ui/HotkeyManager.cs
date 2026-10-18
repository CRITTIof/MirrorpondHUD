using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    /// <summary>
    /// 全局热键。用一个隐藏的 message-only 窗口接收 WM_HOTKEY，
    /// 不依赖主窗口是否可见，也不依赖 WPF 的 Dispatcher 消息过滤。
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        const int WmHotkey = 0x0312;

        const uint ModAlt = 0x0001;
        const uint ModCtrl = 0x0002;
        const uint ModShift = 0x0004;
        const uint ModWin = 0x0008;
        const uint ModNoRepeat = 0x4000;

        public const string ActionToggleOverlay = "toggle-overlay";
        public const string ActionOpenSettings = "open-settings";
        public const string ActionToggleLock = "toggle-lock";

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        static readonly Dictionary<string, int> ActionIds = new Dictionary<string, int>
        {
            { ActionToggleOverlay, 1 },
            { ActionOpenSettings, 2 },
            { ActionToggleLock, 3 }
        };

        sealed class HotkeyWindow : NativeWindow
        {
            public event Action<int> Hotkey;

            public HotkeyWindow()
            {
                CreateHandle(new CreateParams
                {
                    Caption = "WindowOverlay.Hotkeys",
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
                if (m.Msg == WmHotkey)
                {
                    Action<int> handler = Hotkey;
                    if (handler != null) handler(m.WParam.ToInt32());
                }
                base.WndProc(ref m);
            }

            public void Destroy()
            {
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }

        readonly Dictionary<int, string> _idToAction = new Dictionary<int, string>();
        HotkeyWindow _window;
        bool _disposed;

        public event Action<string> HotkeyPressed;

        public HotkeyManager()
        {
            _window = new HotkeyWindow();
            _window.Hotkey += OnHotkeyMessage;
            Log.Info("热键宿主窗口已创建 handle=" + _window.Handle);
        }

        void OnHotkeyMessage(int id)
        {
            if (_idToAction.TryGetValue(id, out string action))
            {
                Log.Info("热键触发: " + action);
                Action<string> handler = HotkeyPressed;
                if (handler != null) handler(action);
            }
        }

        public bool Register(string action, string combination)
        {
            if (_disposed || _window == null) return false;
            if (!ActionIds.TryGetValue(action, out int id)) return false;

            UnregisterHotKey(_window.Handle, id);

            uint mods;
            uint vk;
            if (!TryParse(combination, out mods, out vk))
            {
                Log.Warn("无法识别的热键: " + combination);
                return false;
            }

            _idToAction[id] = action;
            bool ok = RegisterHotKey(_window.Handle, id, mods | ModNoRepeat, vk);
            if (!ok)
                Log.Warn("热键注册失败（多半被其它程序占用）: " + combination +
                         " err=" + Marshal.GetLastWin32Error());
            return ok;
        }

        public void UnregisterAll()
        {
            if (_window == null) return;
            foreach (int id in ActionIds.Values)
            {
                try { UnregisterHotKey(_window.Handle, id); } catch { }
            }
        }

        public static bool TryParse(string combination, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(combination)) return false;

            string[] parts = combination.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;

                string lower = part.ToLowerInvariant();
                if (lower == "ctrl" || lower == "control") { modifiers |= ModCtrl; continue; }
                if (lower == "alt") { modifiers |= ModAlt; continue; }
                if (lower == "shift") { modifiers |= ModShift; continue; }
                if (lower == "win" || lower == "windows") { modifiers |= ModWin; continue; }

                if (!Enum.TryParse(part, true, out Key key)) return false;
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                if (vk == 0) return false;
            }

            return vk != 0;
        }

        public static string Format(Key key, ModifierKeys mods)
        {
            var parts = new List<string>(4);
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { UnregisterAll(); } catch { }

            if (_window != null)
            {
                try { _window.Hotkey -= OnHotkeyMessage; } catch { }
                try { _window.Destroy(); } catch { }
                _window = null;
            }
        }
    }
}
