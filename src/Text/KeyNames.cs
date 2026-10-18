using System;
using System.Collections.Generic;
using System.Text;

namespace WindowOverlay.Core
{
    /// <summary>虚拟键码 → 显示名称。</summary>
    public static class KeyNames
    {
        static readonly Dictionary<int, string> Special = BuildSpecial();

        public static string Of(int vk)
        {
            if (Special.TryGetValue(vk, out string name)) return name;

            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();      // 0-9
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();      // A-Z
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);        // 小键盘数字
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);          // F1-F24

            return "VK" + vk.ToString("X2");
        }

        static Dictionary<int, string> BuildSpecial()
        {
            var map = new Dictionary<int, string>
            {
                { 0x08, "Back" },    // Backspace
                { 0x09, "Tab" },
                { 0x0D, "Enter" },
                { 0x13, "Pause" },
                { 0x14, "Cap" },     // CapsLock
                { 0x1B, "Esc" },
                { 0x20, "Space" },
                { 0x21, "PgUp" },
                { 0x22, "PgDn" },
                { 0x23, "End" },
                { 0x24, "Home" },
                { 0x25, "←" },
                { 0x26, "↑" },
                { 0x27, "→" },
                { 0x28, "↓" },
                { 0x2C, "PrtSc" },
                { 0x2D, "ins" },     // Insert
                { 0x2E, "Del" },
                { 0x5B, "Win" },
                { 0x5C, "Win" },
                { 0x5D, "Menu" },
                { 0x6A, "Num*" },
                { 0x6B, "Num+" },
                { 0x6D, "Num-" },
                { 0x6E, "Num." },
                { 0x6F, "Num/" },
                { 0x90, "NumLock" },
                { 0x91, "ScrollLock" },
                { 0xA0, "Shift" },
                { 0xA1, "Shift" },
                { 0xA2, "Ctrl" },
                { 0xA3, "Ctrl" },
                { 0xA4, "Alt" },
                { 0xA5, "Alt" },
                { 0xA6, "BrowserBack" },
                { 0xA7, "BrowserForward" },
                { 0xA8, "BrowserRefresh" },
                { 0xAD, "Mute" },
                { 0xAE, "Vol-" },
                { 0xAF, "Vol+" },
                { 0xB0, "NextTrack" },
                { 0xB1, "PrevTrack" },
                { 0xB2, "Stop" },
                { 0xB3, "Play" },
                { 0xBA, ";" },
                { 0xBB, "=" },
                { 0xBC, "," },
                { 0xBD, "-" },
                { 0xBE, "." },
                { 0xBF, "/" },
                { 0xC0, "`" },
                { 0xDB, "[" },
                { 0xDC, "\\" },
                { 0xDD, "]" },
                { 0xDE, "'" }
            };
            return map;
        }
    }

    /// <summary>按键历史：环形记录 + 连续重复折叠。</summary>
    public sealed class KeyHistory
    {
        struct Entry
        {
            public string Name;
            public int Count;
        }

        const int MaxEntries = 400;

        readonly object _gate = new object();
        readonly List<Entry> _entries = new List<Entry>(32);
        int _threshold = 3;
        int _visibleCount = 14;
        int _maxChars = 18;
        int _total;

        public int Total { get { lock (_gate) { return _total; } } }

        public void Configure(int visibleCount, int repeatThreshold)
        {
            Configure(visibleCount, repeatThreshold, _maxChars);
        }

        public void Configure(int visibleCount, int repeatThreshold, int maxChars)
        {
            lock (_gate)
            {
                _visibleCount = Math.Max(1, visibleCount);
                _threshold = Math.Max(1, repeatThreshold);
                _maxChars = Math.Max(1, maxChars);
            }
        }

        public void Push(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_gate)
            {
                _total++;
                int last = _entries.Count - 1;
                if (last >= 0 && _entries[last].Name == key)
                {
                    Entry e = _entries[last];
                    e.Count++;
                    _entries[last] = e;
                }
                else
                {
                    _entries.Add(new Entry { Name = key, Count = 1 });
                    if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _total = 0;
            }
        }

        public string Render()
        {
            lock (_gate)
            {
                var tokens = new List<string>(64);
                for (int i = 0; i < _entries.Count; i++)
                {
                    Entry e = _entries[i];
                    if (e.Count > _threshold)
                    {
                        tokens.Add(e.Name + "×" + e.Count);
                    }
                    else
                    {
                        for (int k = 0; k < e.Count; k++) tokens.Add(e.Name);
                    }
                }

                if (tokens.Count > _visibleCount)
                    tokens = tokens.GetRange(tokens.Count - _visibleCount, _visibleCount);

                if (tokens.Count == 0) return "";

                // 按显示字符数截断（空格、分隔符都计数）：从最旧的按键开始丢弃，
                // 保证最新的按键永远可见。单个按键本身就超长时保留它，避免显示空白。
                string joined = string.Join(" ", tokens);
                while (joined.Length > _maxChars && tokens.Count > 1)
                {
                    tokens.RemoveAt(0);
                    joined = string.Join(" ", tokens);
                }
                return joined;
            }
        }
    }
}
