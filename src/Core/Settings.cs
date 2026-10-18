using System;
using System.Collections.Generic;
using System.IO;

namespace WindowOverlay.Core
{
    /// <summary>全部可持久化的设置。字段名即 JSON 键名。</summary>
    public sealed class Settings
    {
        public const string DefaultContent =
            "{time}  {date}\n" +
            "CPU {cpu:0.0}%   MEM {mem_pct:0}%   GPU {gpu:0.0}%\n" +
            "↓ {net_down}   ↑ {net_up}   DISK {disk:0.0}%\n" +
            "KEY {keys}";

        public OverlaySettings Overlay = new OverlaySettings();
        public StyleSettings Style = new StyleSettings();
        public DataSettings Data = new DataSettings();
        public BehaviorSettings Behavior = new BehaviorSettings();
        public HotkeySettings Hotkeys = new HotkeySettings();

        /// <summary>显示内容模板，支持 {cpu} 等占位符，换行即多行。</summary>
        public string Content = DefaultContent;

        public void Normalize()
        {
            if (Content == null) Content = DefaultContent;

            Style.FontSize = Clamp(Style.FontSize, 6, 96);
            Style.BackgroundOpacity = Clamp(Style.BackgroundOpacity, 0, 1);
            Style.Padding = Clamp(Style.Padding, 0, 80);
            Style.LineHeight = Clamp(Style.LineHeight, 0, 120);
            Style.FontFamily = string.IsNullOrWhiteSpace(Style.FontFamily) ? "Microsoft YaHei UI" : Style.FontFamily.Trim();

            Overlay.Monitor = Math.Max(0, Overlay.Monitor);
            Overlay.CornerRadius = Clamp(Overlay.CornerRadius, 0, 60);
            Overlay.X = Clamp(Overlay.X, -20000, 20000);
            Overlay.Y = Clamp(Overlay.Y, -20000, 20000);

            Data.KeyHistoryCount = (int)Clamp(Data.KeyHistoryCount, 1, 64);
            Data.RepeatThreshold = (int)Clamp(Data.RepeatThreshold, 1, 20);
            Data.NvidiaIntervalMs = (int)Clamp(Data.NvidiaIntervalMs, 500, 10000);
            if (Data.DiskDrives == null) Data.DiskDrives = new List<string>();
            if (Data.DiskDrives.Count > 0) Data.DiskDrives = NormalizeDrives(Data.DiskDrives);
            if (Data.NetworkAdapter == null) Data.NetworkAdapter = "";

            Behavior.RefreshMs = (int)Clamp(Behavior.RefreshMs, 100, 5000);
            Behavior.TypeSpeedMs = (int)Clamp(Behavior.TypeSpeedMs, 8, 300);
            if (Behavior.WelcomeText == null) Behavior.WelcomeText = "Welcome Back";
            if (Behavior.WelcomeText.Length > 120) Behavior.WelcomeText = Behavior.WelcomeText.Substring(0, 120);

            Hotkeys.ToggleOverlay = NormalizeHotkey(Hotkeys.ToggleOverlay, "F9");
            Hotkeys.OpenSettings = NormalizeHotkey(Hotkeys.OpenSettings, "F10");
            Hotkeys.ToggleLock = NormalizeHotkey(Hotkeys.ToggleLock, "F8");

            if (!IsHexColor(Style.Foreground)) Style.Foreground = "#E6EDF3";
            if (!IsHexColor(Style.Background)) Style.Background = "#0D1117";
        }

        static string NormalizeHotkey(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        static List<string> NormalizeDrives(List<string> drives)
        {
            var result = new List<string>(drives.Count);
            foreach (string raw in drives)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string s = raw.Trim();

                if (s.Length == 2 && s[1] == ':') s += "\\";
                else if (!s.EndsWith("\\")) s += "\\";

                if (!result.Contains(s)) result.Add(s);
            }
            return result;
        }

        public static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static bool IsHexColor(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s[0] != '#') return false;
            if (s.Length != 7) return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }
    }

    public sealed class OverlaySettings
    {
        /// <summary>叠层左上角坐标（DIP）。</summary>
        public double X = 48;
        public double Y = 48;
        /// <summary>0-based 显示器序号。</summary>
        public int Monitor = 0;
        /// <summary>true = 鼠标穿透（锁定），false = 可拖动。</summary>
        public bool Locked = true;
        public bool Topmost = true;
        public double CornerRadius = 10;
        public bool Shadow = true;
    }

    public sealed class StyleSettings
    {
        public string FontFamily = "Microsoft YaHei UI";
        public double FontSize = 15;
        public bool Bold = false;
        public string Foreground = "#E6EDF3";
        public string Background = "#0D1117";
        /// <summary>背景不透明度 0~1（窗口本身始终不透明，避免文字发虚）。</summary>
        public double BackgroundOpacity = 0.86;
        public double Padding = 14;
        public double LineHeight = 0;
        public string TextAlignment = "Left";
    }

    public sealed class DataSettings
    {
        public bool ShowCpu = true;
        public bool ShowGpu = true;
        /// <summary>-1 = 汇总所有 GPU；>=0 = 指定 LUID 序号。</summary>
        public int GpuIndex = -1;
        public bool ShowMemory = true;
        public bool ShowDisk = true;
        /// <summary>要统计剩余空间的分区，如 ["C:", "D:"]；空 = 全部固定磁盘。</summary>
        public List<string> DiskDrives = new List<string>();
        public bool ShowNetwork = true;
        /// <summary>网卡 PDH 实例名；空 = 自动选择流量最大的网卡。</summary>
        public string NetworkAdapter = "";
        public bool ShowKeyboard = true;
        public int KeyHistoryCount = 14;
        /// <summary>连续相同按键超过该次数时折叠为 字符×N。</summary>
        public int RepeatThreshold = 3;
        public bool ShowNvidia = true;
        public int NvidiaIntervalMs = 2000;
    }

    public sealed class BehaviorSettings
    {
        public int RefreshMs = 500;
        public bool AutoStart = false;
        public bool StartMinimized = false;
        public bool TrayEnabled = true;

        /// <summary>启动时播放"打字 / 退格"欢迎动画。</summary>
        public bool IntroEnabled = true;
        /// <summary>欢迎语，默认 Welcome Back。</summary>
        public string WelcomeText = "Welcome Back";
        /// <summary>每字符的动画间隔（毫秒），越小越快。</summary>
        public int TypeSpeedMs = 28;
    }

    public sealed class HotkeySettings
    {
        public string ToggleOverlay = "F9";
        public string OpenSettings = "F10";
        public string ToggleLock = "F8";
    }
}
