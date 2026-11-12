using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

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
    public static string CardAction(string cardId) => $"card:{(cardId ?? "")}";

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
                if (handler is not null) handler(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }

        public void Destroy()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    readonly Dictionary<int, string> _idToKey = new();
    readonly Dictionary<int, List<string>> _idToActions = new();
    readonly Dictionary<string, int> _keyToId = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _actionToId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>RegisterHotKey 的应用 id 合法上界；0xC000 以上由 GlobalAddAtom 占用。</summary>
    const int MaxHotkeyId = 0xBFFF;

    int _nextId = 10;

    /// <summary>
    /// S-13：已注销 / 注册失败的 id 回收池。
    /// 旧实现只增不减，每次改键都会消耗一个新 id；逼近 0xBFFF 后 RegisterHotKey 必然失败，
    /// 日志却把原因写成「多半被其它程序占用」，用户看到的是「所有热键突然不灵」且只能重启。
    /// </summary>
    readonly Stack<int> _freeIds = new();

    HotkeyWindow _window;
    bool _disposed;

    public event Action<string> HotkeyPressed;

    public HotkeyManager()
    {
        _window = new HotkeyWindow();
        _window.Hotkey += OnHotkeyMessage;
        Log.Info($"热键宿主窗口已创建 handle={_window.Handle}");
    }

    void OnHotkeyMessage(int id)
    {
        if (!_idToActions.TryGetValue(id, out var actions) || actions.Count == 0) return;

        // 复制一份：处理过程中可能有卡片被删除并重新注册热键。
        string[] snapshot = actions.ToArray();
        foreach (string action in snapshot)
        {
            Log.Info($"热键触发: {action}");
            Action<string> handler = HotkeyPressed;
            if (handler is null) continue;

            // S-21：这条调用链位于 WinForms 窗口过程（WM_HOTKEY）里，异常从这里逸出不会经过
            // WPF 的 DispatcherUnhandledException，可能直接终止进程。动作失败只损失这一次触发，
            // 必须就地隔离并留痕，不能让它穿出 WndProc。
            try
            {
                handler(action);
            }
            catch (Exception ex)
            {
                Log.Error($"热键动作失败: {action}", ex);
            }
        }
    }

    /// <summary>
    /// 取一个可用的热键 id：优先复用回收池；池空且仍在合法区间内才自增。
    /// 返回 0 表示没有可用 id（调用方必须按「id 耗尽」记录，不能混同于「组合键被占用」）。
    /// </summary>
    int TakeId()
    {
        while (_freeIds.Count > 0)
        {
            int recycled = _freeIds.Pop();
            // 防御：池里若混进仍在使用的 id（重复归还 / 池未清空），宁可丢弃也不能让两个动作共用。
            if (!_idToActions.ContainsKey(recycled) && !_idToKey.ContainsKey(recycled)) return recycled;
        }

        if (_nextId > MaxHotkeyId) return 0;
        return _nextId++;
    }

    /// <summary>
    /// 注销成功后才把 id 还给回收池（S-13）。注册失败的 id 也要还：它从未被 OS 占用，
    /// 不回池就等于永久漏掉一个 id。位于区间外的 id 不回收（防止池被异常值污染）。
    /// </summary>
    void ReleaseId(int id)
    {
        if (id < 10 || id > MaxHotkeyId) return;
        _freeIds.Push(id);
    }

    /// <summary>
    /// 把动作绑定到组合键。空白组合键 = 解除绑定。
    /// 返回 true 表示该动作已经能被触发（包括与其它动作共用同一个组合键）。
    /// </summary>
    public bool Register(string action, string combination)
    {
        if (_disposed || _window is null || string.IsNullOrEmpty(action)) return false;

        if (string.IsNullOrWhiteSpace(combination))
        {
            Detach(action);
            Log.Info($"热键未设置（已禁用）: {action}");
            return false;
        }

        if (!TryParse(combination, out var mods, out var vk))
        {
            // P2-3：不能先 Detach 再解析。解析失败 / 新键被占用时必须保留旧绑定。
            Log.Warn($"无法识别的热键: {combination}（{action}），保留原绑定");
            return false;
        }

        string key = KeyOf(mods, vk);
        _actionToId.TryGetValue(action, out var oldId);

        if (_keyToId.TryGetValue(key, out var existingId))
        {
            if (!_idToActions.TryGetValue(existingId, out var shared))
            {
                shared = new List<string>();
                _idToActions[existingId] = shared;
            }

            bool already = false;
            for (int i = 0; i < shared.Count; i++)
            {
                if (string.Equals(shared[i], action, StringComparison.OrdinalIgnoreCase))
                {
                    already = true;
                    break;
                }
            }

            if (!already) shared.Add(action);
            _actionToId[action] = existingId;

            // 新组合键已经有注册项：不需要动 OS；把旧 id 上的这个动作摘掉即可。
            if (oldId != 0 && oldId != existingId) RemoveActionFromId(action, oldId);

            if (!already) Log.Info($"热键 {combination} 已合并（同一组合键共 {shared.Count} 个动作）");
            return true;
        }

        int newId = TakeId();
        if (newId == 0)
        {
            // 与「组合键被占用」分开记录：这两种失败对用户的意义完全不同。
            Log.Error($"热键 id 已全部占用（0x10–0x{MaxHotkeyId:X4}），无法注册 {combination}（{action}）；重启程序可回收 id");
            return false;
        }

        if (!RegisterHotKey(_window.Handle, newId, mods | ModNoRepeat, vk))
        {
            // 注册失败：旧绑定保持原样（这是 P2-3 的核心）。
            // id 从未被 OS 占用，立刻还回池里，避免改键失败几次就把 id 空间耗掉。
            ReleaseId(newId);
            Log.Warn($"热键注册失败（组合键 {combination} 多半已被其它程序占用）err={Marshal.GetLastWin32Error()}，保留原绑定");
            return false;
        }

        _keyToId[key] = newId;
        _idToKey[newId] = key;
        _idToActions[newId] = new List<string> { action };

        if (oldId != 0 && oldId != newId) RemoveActionFromId(action, oldId);
        _actionToId[action] = newId;
        return true;
    }

    /// <summary>只把一个动作从指定 id 上摘掉；id 上还有别的动作时保留组合键注册（P2-3）。</summary>
    void RemoveActionFromId(string action, int id)
    {
        if (!_idToActions.TryGetValue(id, out var actions)) return;

        actions.RemoveAll(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));
        if (actions.Count > 0) return;

        _idToActions.Remove(id);
        if (_window is not null)
        {
            if (UnregisterHotKey(_window.Handle, id))
            {
                // S-13：注销成功的 id 立刻回池，下一次改键复用而不是继续自增。
                ReleaseId(id);
            }
            else
            {
                // 注销失败时 id 可能仍被 OS 占着：不回池，避免下次注册到同一个 id 后
                // 两个动作互相顶掉。窗口销毁时 OS 会兜底回收。
                Log.Warn($"注销热键失败（id={id}）err={Marshal.GetLastWin32Error()}，该 id 不复用");
            }
        }

        if (_idToKey.TryGetValue(id, out var key))
        {
            _idToKey.Remove(id);
            _keyToId.Remove(key);
        }
    }

    /// <summary>解除一个动作的绑定；组合键上还有别的动作时保持注册。</summary>
    public void Detach(string action)
    {
        if (!_actionToId.TryGetValue(action, out var id)) return;
        _actionToId.Remove(action);

        if (!_idToActions.TryGetValue(id, out var actions)) return;

        actions.RemoveAll(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));
        if (actions.Count > 0) return;

        _idToActions.Remove(id);
        if (_window is not null)
        {
            if (UnregisterHotKey(_window.Handle, id))
            {
                // S-13：注销成功的 id 立刻回池，下一次改键复用而不是继续自增。
                ReleaseId(id);
            }
            else
            {
                // 注销失败时 id 可能仍被 OS 占着：不回池，避免下次注册到同一个 id 后
                // 两个动作互相顶掉。窗口销毁时 OS 会兜底回收。
                Log.Warn($"注销热键失败（id={id}）err={Marshal.GetLastWin32Error()}，该 id 不复用");
            }
        }

        if (_idToKey.TryGetValue(id, out var key))
        {
            _idToKey.Remove(id);
            _keyToId.Remove(key);
        }
    }

    public void UnregisterAll()
    {
        if (_window is null) return;

        int failed = 0;
        int lastError = 0;
        foreach (int id in new List<int>(_idToActions.Keys))
        {
            // UnregisterHotKey 返回 BOOL 而不是抛异常，catch 在这里本来就兜不住（G-18）。
            if (!UnregisterHotKey(_window.Handle, id))
            {
                failed++;
                lastError = Marshal.GetLastWin32Error();
            }
        }

        if (failed > 0)
        {
            Log.Warn($"批量注销热键有 {failed} 个失败（err={lastError}），这些 id 不再回收；窗口销毁时由系统兜底");
        }

        _idToActions.Clear();
        _idToKey.Clear();
        _keyToId.Clear();
        _actionToId.Clear();

        // S-13：全部注销后 id 空间回到起点。id 复用理论上可能残留旧的 WM_HOTKEY，
        // 但注册 / 注销都在同一条 UI 线程的消息泵里，残留消息最多早于新注册一拍，
        // 实际风险远低于「id 耗尽后所有热键永久失效」。
        _nextId = 10;
        _freeIds.Clear();
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

        // S-09：无修饰键时只允许功能键（VK_F1–VK_F24）。F6/F8/F9 这类是项目默认热键，
        // 必须兼容；但单字母 / 数字 / 符号会吃掉整个系统的该按键，直接判非法。
        if (modifiers == 0 && (vk < 0x70 || vk > 0x87)) return false;

        return vk != 0;
    }

    static string KeyOf(uint modifiers, uint vk) => $"{modifiers:X4}:{vk:X4}";

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

        // 释放路径统一走 SafeRelease.Run（X-9）：注销 / 销毁失败必须是 Debug/Warn，
        // 不能再写成空 catch。
        SafeRelease.Run(UnregisterAll, "全局热键注销");

        if (_window is not null)
        {
            HotkeyWindow window = _window;
            _window = null;
            SafeRelease.Run(() => window.Hotkey -= OnHotkeyMessage, "热键事件退订");
            SafeRelease.Run(window.Destroy, "热键宿主窗口");
        }
    }
}
