using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 全局热键。用一个隐藏的 message-only 窗口接收 WM_HOTKEY，
    /// 不依赖主窗口是否可见，也不依赖 WPF 的 Dispatcher 消息过滤。
    /// 动作名是动态的（包括每张卡片的 "card:&lt;id&gt;"），同一个组合键可以挂多个动作：
    /// 两张卡片设置同一个热键时，按下会同时呼出两张卡片。
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

        /// <summary>卡片热键的动作名。</summary>
        public static string CardAction(string cardId)
        {
            return "card:" + (cardId ?? "");
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        sealed class HotkeyWindow : NativeWindow
        {
            public event Action<int> Hotkey;

            public HotkeyWindow()
            {
                CreateHandle(new CreateParams
                {
                    Caption = "MirrorpondHUD.Hotkeys",
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

        readonly Dictionary<int, string> _idToKey = new Dictionary<int, string>();
        readonly Dictionary<int, List<string>> _idToActions = new Dictionary<int, List<string>>();
        readonly Dictionary<string, int> _keyToId = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> _actionToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int _nextId = 10;
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
            List<string> actions;
            if (!_idToActions.TryGetValue(id, out actions) || actions.Count == 0) return;

            // 复制一份：处理过程中可能有卡片被删除并重新注册热键。
            string[] snapshot = actions.ToArray();
            foreach (string action in snapshot)
            {
                Log.Info("热键触发: " + action);
                Action<string> handler = HotkeyPressed;
                if (handler != null) handler(action);
            }
        }

        /// <summary>
        /// 把动作绑定到组合键。空白组合键 = 解除绑定。
        /// 返回 true 表示该动作已经能被触发（包括与其它动作共用同一个组合键）。
        /// </summary>
        public bool Register(string action, string combination)
        {
            if (_disposed || _window == null || string.IsNullOrEmpty(action)) return false;

            Detach(action);

            if (string.IsNullOrWhiteSpace(combination))
            {
                Log.Info("热键未设置（已禁用）: " + action);
                return false;
            }

            uint mods, vk;
            if (!TryParse(combination, out mods, out vk))
            {
                Log.Warn("无法识别的热键: " + combination + "（" + action + "）");
                return false;
            }

            string key = KeyOf(mods, vk);
            int id;
            if (_keyToId.TryGetValue(key, out id))
            {
                List<string> shared;
                if (!_idToActions.TryGetValue(id, out shared))
                {
                    shared = new List<string>();
                    _idToActions[id] = shared;
                }
                shared.Add(action);
                _actionToId[action] = id;
                Log.Info("热键 " + combination + " 已合并（同一组合键共 " + shared.Count + " 个动作）");
                return true;
            }

            int newId = _nextId++;
            if (!RegisterHotKey(_window.Handle, newId, mods | ModNoRepeat, vk))
            {
                Log.Warn("热键注册失败（多半被其它程序占用）: " + combination +
                         " err=" + Marshal.GetLastWin32Error());
                return false;
            }

            _keyToId[key] = newId;
            _idToKey[newId] = key;
            _idToActions[newId] = new List<string> { action };
            _actionToId[action] = newId;
            return true;
        }

        /// <summary>解除一个动作的绑定；组合键上还有别的动作时保持注册。</summary>
        public void Detach(string action)
        {
            int id;
            if (!_actionToId.TryGetValue(action, out id)) return;
            _actionToId.Remove(action);

            List<string> actions;
            if (!_idToActions.TryGetValue(id, out actions)) return;

            actions.RemoveAll(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));
            if (actions.Count > 0) return;

            _idToActions.Remove(id);
            if (_window != null) UnregisterHotKey(_window.Handle, id);

            string key;
            if (_idToKey.TryGetValue(id, out key))
            {
                _idToKey.Remove(id);
                _keyToId.Remove(key);
            }
        }

        public void UnregisterAll()
        {
            if (_window == null) return;
            foreach (int id in new List<int>(_idToActions.Keys))
            {
                try { UnregisterHotKey(_window.Handle, id); } catch { }
            }
            _idToActions.Clear();
            _idToKey.Clear();
            _keyToId.Clear();
            _actionToId.Clear();
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

        static string KeyOf(uint modifiers, uint vk)
        {
            return modifiers.ToString("X4") + ":" + vk.ToString("X4");
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
