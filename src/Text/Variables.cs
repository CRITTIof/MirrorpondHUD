using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowOverlay.Core
{
    /// <summary>把一次采样展开成模板可用的变量表。</summary>
    public static class Variables
    {
        const double Gb = 1024.0 * 1024.0 * 1024.0;

        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        public static Dictionary<string, object> Build(MetricsSnapshot snap, Settings settings, KeyHistory keys)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (snap == null) snap = new MetricsSnapshot();
            if (settings == null) settings = new Settings();

            DateTime now = DateTime.Now;

            // ---------- 通用 ----------
            map["time"] = now.ToString("HH:mm:ss");
            map["date"] = now.ToString("yyyy-MM-dd");
            map["weekday"] = CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(now.DayOfWeek);
            map["host"] = Environment.MachineName;
            map["user"] = Environment.UserName;
            map["uptime"] = FormatUptime(TimeSpan.FromMilliseconds(GetTickCount64()));

            // ---------- CPU ----------
            map["cpu"] = snap.CpuPercent;
            map["cpu_bar"] = Bar(snap.CpuPercent / 100.0);

            // ---------- 内存 ----------
            map["mem_used"] = snap.MemUsedGb;
            map["mem_total"] = snap.MemTotalGb;
            map["mem_free"] = Math.Max(0, snap.MemTotalGb - snap.MemUsedGb);
            map["mem_pct"] = snap.MemPercent;
            map["mem_bar"] = Bar(snap.MemPercent / 100.0);

            // ---------- GPU ----------
            map["gpu"] = snap.GpuPercent;
            map["gpu_bar"] = Bar(snap.GpuPercent / 100.0);
            NvidiaSample nv = snap.Nvidia ?? new NvidiaSample();
            map["gpu_name"] = nv.Available && nv.Name.Length > 0 ? nv.Name : "GPU";
            map["gpu_util"] = nv.Utilization;
            map["vram_used"] = nv.VramUsedMb;
            map["vram_total"] = nv.VramTotalMb;
            map["vram_pct"] = nv.VramTotalMb > 0 ? nv.VramUsedMb / nv.VramTotalMb * 100.0 : 0.0;
            map["gpu_temp"] = nv.TempC;
            map["gpu_power"] = nv.PowerW;

            // ---------- 磁盘 ----------
            map["disk"] = snap.DiskPercent;
            map["disk_bar"] = Bar(snap.DiskPercent / 100.0);
            map["disk_read"] = FormatRate(snap.DiskReadBps);
            map["disk_write"] = FormatRate(snap.DiskWriteBps);
            map["disk_read_bps"] = snap.DiskReadBps;
            map["disk_write_bps"] = snap.DiskWriteBps;

            if (snap.Drives != null)
            {
                foreach (DriveSample drive in snap.Drives)
                {
                    string tag = DriveTag(drive.Name);
                    if (tag.Length == 0) continue;
                    map["free_" + tag] = drive.FreeGb;
                    map["total_" + tag] = drive.TotalGb;
                    map["used_pct_" + tag] = drive.UsedPercent;
                }
            }

            // ---------- 网络 ----------
            map["net_down"] = FormatRate(snap.NetDownBps);
            map["net_up"] = FormatRate(snap.NetUpBps);
            map["net_down_bps"] = snap.NetDownBps;
            map["net_up_bps"] = snap.NetUpBps;
            map["net_total"] = FormatRate(snap.NetDownBps + snap.NetUpBps);
            map["net_adapter"] = string.IsNullOrEmpty(snap.NetAdapter) ? "—" : snap.NetAdapter;

            // ---------- 键盘 ----------
            string rendered = keys == null ? "" : keys.Render();
            map["keys"] = rendered.Length == 0 ? "—" : rendered;
            map["key_count"] = keys == null ? 0 : keys.Total;

            return map;
        }

        public static string DriveTag(string driveName)
        {
            if (string.IsNullOrEmpty(driveName)) return "";
            string s = driveName.Trim().TrimEnd('\\', '/');
            if (s.Length >= 2 && s[1] == ':') return s.Substring(0, 2);
            return s.Replace("\\", "").Replace("/", "");
        }

        public static string FormatRate(double bytesPerSecond)
        {
            if (double.IsNaN(bytesPerSecond) || bytesPerSecond < 0) bytesPerSecond = 0;
            if (bytesPerSecond < 1024) return bytesPerSecond.ToString("0", CultureInfo.InvariantCulture) + " B/s";
            if (bytesPerSecond < 1024 * 1024) return (bytesPerSecond / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " KB/s";
            if (bytesPerSecond < 1024 * 1024 * 1024) return (bytesPerSecond / (1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
            return (bytesPerSecond / Gb).ToString("0.00", CultureInfo.InvariantCulture) + " GB/s";
        }

        public static string FormatUptime(TimeSpan span)
        {
            if (span.TotalDays >= 1)
                return ((int)span.TotalDays) + "天" + span.Hours + "小时";
            if (span.TotalHours >= 1)
                return span.Hours + ":" + span.Minutes.ToString("00");
            return span.Minutes + ":" + span.Seconds.ToString("00");
        }

        public static string Bar(double ratio)
        {
            const int cells = 10;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            int filled = (int)Math.Round(ratio * cells);
            var sb = new StringBuilder(cells);
            for (int i = 0; i < cells; i++) sb.Append(i < filled ? '█' : '░');
            return sb.ToString();
        }
    }
}
