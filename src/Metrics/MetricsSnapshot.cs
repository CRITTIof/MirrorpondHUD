using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    public sealed class DriveSample
    {
        public string Name;
        public double FreeGb;
        public double TotalGb;

        public double UsedPercent
        {
            get
            {
                if (TotalGb <= 0) return 0;
                return (TotalGb - FreeGb) / TotalGb * 100.0;
            }
        }
    }

    public sealed class NvidiaSample
    {
        public bool Available;
        public string Name = "";
        public double Utilization = double.NaN;
        public double VramUsedMb = double.NaN;
        public double VramTotalMb = double.NaN;
        public double TempC = double.NaN;
        public double PowerW = double.NaN;
    }

    /// <summary>某一时刻的全部采样结果。后台线程创建，UI 线程只读。</summary>
    public sealed class MetricsSnapshot
    {
        public DateTime Timestamp = DateTime.Now;

        /// <summary>
        /// 预热帧：进程刚起来时 PDH 速率计数器还没有有效基线（第一次读常常是 0% 或 100%），
        /// 界面据此继续显示占位符，不要把假数据显示成真实占用。
        /// </summary>
        public bool Warmup;

        public double CpuPercent;
        public bool GpuAvailable;
        public double GpuPercent;
        public double MemUsedGb;
        public double MemTotalGb;
        public double MemPercent;
        public double DiskPercent;
        public double DiskReadBps;
        public double DiskWriteBps;
        public double NetDownBps;
        public double NetUpBps;
        public string NetAdapter = "";
        public List<DriveSample> Drives = new List<DriveSample>();
        public NvidiaSample Nvidia = new NvidiaSample();
        public string Error = "";

        public static MetricsSnapshot Empty()
        {
            return new MetricsSnapshot();
        }
    }
}
