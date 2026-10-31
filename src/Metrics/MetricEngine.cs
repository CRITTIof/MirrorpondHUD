using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MirrorpondHUD.Core
{
    /// <summary>后台采样线程：CPU / GPU / 内存 / 磁盘 / 网络 / 分区 / NVIDIA。</summary>
    public sealed class MetricEngine : IDisposable
    {
        public const string CpuPath = @"\Processor Information(_Total)\% Processor Time";
        public const string GpuPath = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
        public const string DiskTimePath = @"\PhysicalDisk(_Total)\% Disk Time";
        public const string DiskReadPath = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
        public const string DiskWritePath = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";
        public const string NetRxPath = @"\Network Interface(*)\Bytes Received/sec";
        public const string NetTxPath = @"\Network Interface(*)\Bytes Sent/sec";

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        static readonly string[] AdapterExcludes =
        {
            "loopback", "isatap", "teredo", "pseudo", "wan miniport", "bluetooth", "6to4", "ip-tunnel", "tunnel"
        };

        readonly PdhQuery _pdh;
        readonly NvidiaSmi _nvidia = new NvidiaSmi();

        // P1-9：
        //  · _pdhGate 保护 PDH 句柄 —— 采样线程和 UI 线程（设置变更后重新解析适配器）都可能碰；
        //  · 不再有共享的 _buffer：每次读计数器都用局部 List，UI/采样不会并发 Clear/Add 同一个 List；
        //  · _settings 用 volatile：UI 线程只换引用，采样线程每轮读一次；
        //  · _adapterDirty 是 UI → 采样线程的唯一信号：UI 不碰 PDH，只置脏标志。
        readonly object _pdhGate = new object();

        volatile Settings _settings;
        Thread _thread;
        volatile bool _running;
        volatile string _resolvedAdapter = "";
        volatile bool _adapterDirty;
        DateTime _nextAdapterCheckUtc = DateTime.MinValue;

        public event Action<MetricsSnapshot> Updated;

        public string ResolvedAdapter { get { return _resolvedAdapter; } }

        public MetricEngine(Settings settings)
        {
            _settings = settings;
            _pdh = new PdhQuery();
        }

        public bool Start()
        {
            if (_thread != null) return true;

            bool any = false;
            // P1-9：PDH 句柄统一在 _pdhGate 里访问。
            lock (_pdhGate)
            {
                any |= _pdh.TryAdd(CpuPath);
                any |= _pdh.TryAdd(GpuPath);
                any |= _pdh.TryAdd(DiskTimePath);
                any |= _pdh.TryAdd(DiskReadPath);
                any |= _pdh.TryAdd(DiskWritePath);
                any |= _pdh.TryAdd(NetRxPath);
                any |= _pdh.TryAdd(NetTxPath);

                // 预热：第一次 collect 只是建立基线
                _pdh.Collect();
            }
            Thread.Sleep(250);
            lock (_pdhGate) { _pdh.Collect(); }

            // UI 线程不解析适配器：只置脏标志，交给采样线程在第一轮里做（P1-9）。
            _adapterDirty = true;

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MirrorpondHUD.Metrics" };
            _thread.Start();
            Log.Info("采样线程已启动，计数器 " + (any ? "正常" : "全部失败"));
            return any;
        }

        public void UpdateSettings(Settings settings)
        {
            _settings = settings;

            // P1-9：UI 线程（OverlayController 应用设置）只投递「配置已变」标志。
            // 适配器解析、PDH 读取全部留在采样线程，避免 UI 与采样线程并发碰同一份
            // PDH 状态 / 同一个 List。
            _adapterDirty = true;
        }

        public void Stop()
        {
            _running = false;
            Thread t = _thread;
            _thread = null;
            if (t != null && t.IsAlive)
            {
                try { t.Join(1500); } catch { }
            }
        }

        void Loop()
        {
            while (_running)
            {
                try
                {
                    MetricsSnapshot snapshot = Sample();
                    Action<MetricsSnapshot> handler = Updated;
                    if (handler != null) handler(snapshot);
                }
                catch (Exception ex)
                {
                    // 正常退出时 PDH 句柄可能已释放，不必当成错误刷日志。
                    if (_running) Log.Error("采样循环异常", ex);
                }

                int interval = 500;
                Settings current = _settings;
                if (current != null) interval = Math.Max(100, current.Behavior.RefreshMs);

                for (int slept = 0; slept < interval && _running; slept += 50)
                    Thread.Sleep(50);
            }
        }

        int _sampleCount;

        public MetricsSnapshot Sample()
        {
            Settings s = _settings;
            var snap = new MetricsSnapshot { Timestamp = DateTime.Now };
            snap.Warmup = Interlocked.Increment(ref _sampleCount) == 1;

            bool collected;
            lock (_pdhGate) { collected = _pdh.Collect(); }
            if (!collected) snap.Error = "PDH 采集失败";

            if (s.Data.ShowCpu) snap.CpuPercent = Clamp01(ReadSum(CpuPath));
            if (s.Data.ShowGpu) SampleGpu(snap, s);
            if (s.Data.ShowDisk) SampleDisk(snap);
            if (s.Data.ShowNetwork) SampleNetwork(snap);

            SampleMemory(snap);

            if (s.Data.ShowDisk) snap.Drives = ReadDrives(s.Data.DiskDrives);

            if (s.Data.ShowNvidia)
            {
                _nvidia.Sample(s.Data.NvidiaIntervalMs);
                snap.Nvidia = _nvidia.Current;
            }

            return snap;
        }

        void SampleGpu(MetricsSnapshot snap, Settings s)
        {
            // 局部 List：原来用共享 _buffer，UI 线程保存设置时可能和采样线程同时
            // Clear/Add 同一个 List（P1-7..P1-9 里的 P1-9）。
            var instances = new List<InstanceValue>(64);
            lock (_pdhGate)
            {
                if (!_pdh.TryRead(GpuPath, instances)) return;
            }

            var byLuid = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var keys = new List<string>();
            double total = 0;

            for (int i = 0; i < instances.Count; i++)
            {
                InstanceValue item = instances[i];
                string luid = ExtractLuid(item.Name);
                if (!byLuid.TryGetValue(luid, out double acc))
                {
                    acc = 0;
                    keys.Add(luid);
                }
                acc += item.Value;
                byLuid[luid] = acc;
                total += item.Value;
            }

            total = Clamp01(total);
            snap.GpuAvailable = instances.Count > 0;

            int index = s.Data.GpuIndex;
            if (index >= 0 && index < keys.Count)
            {
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                snap.GpuPercent = Clamp01(byLuid[keys[index]]);
            }
            else
            {
                snap.GpuPercent = total;
            }
        }

        static string ExtractLuid(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName)) return "";
            int start = instanceName.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return instanceName;

            int end = instanceName.IndexOf("_phys", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return instanceName.Substring(start);
            return instanceName.Substring(start, end - start);
        }

        void SampleDisk(MetricsSnapshot snap)
        {
            snap.DiskPercent = Clamp01(ReadSum(DiskTimePath));
            snap.DiskReadBps = Math.Max(0, ReadSum(DiskReadPath));
            snap.DiskWriteBps = Math.Max(0, ReadSum(DiskWritePath));
        }

        /// <summary>加锁读一次计数器总和（P1-9：PDH 句柄只在 _pdhGate 里访问）。</summary>
        double ReadSum(string path)
        {
            lock (_pdhGate) { return _pdh.ReadSum(path); }
        }

        void SampleNetwork(MetricsSnapshot snap)
        {
            // UI 线程只置 _adapterDirty，适配器解析在采样线程这里做（P1-9）。
            bool dirty = _adapterDirty;
            if (dirty) _adapterDirty = false;
            EnsureAdapter(dirty);

            string adapter = _resolvedAdapter;
            snap.NetAdapter = adapter;
            if (string.IsNullOrEmpty(adapter)) return;

            // 每次都用局部 List，绝不共享（P1-9）。
            var rx = new List<InstanceValue>(32);
            lock (_pdhGate) { _pdh.TryRead(NetRxPath, rx); }
            if (rx.Count > 0) snap.NetDownBps = Math.Max(0, FindValue(rx, adapter));

            var tx = new List<InstanceValue>(32);
            lock (_pdhGate) { _pdh.TryRead(NetTxPath, tx); }
            if (tx.Count > 0) snap.NetUpBps = Math.Max(0, FindValue(tx, adapter));
        }

        static double FindValue(List<InstanceValue> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return list[i].Value;
            }
            return 0;
        }

        void EnsureAdapter(bool force)
        {
            if (!force && !string.IsNullOrEmpty(_resolvedAdapter))
            {
                if (DateTime.UtcNow < _nextAdapterCheckUtc) return;
                _nextAdapterCheckUtc = DateTime.UtcNow.AddSeconds(5);
            }
            else
            {
                _nextAdapterCheckUtc = DateTime.UtcNow.AddSeconds(5);
            }

            Settings current = _settings;
            string wanted = current == null ? "" : current.Data.NetworkAdapter;

            // 局部 List（P1-9）：原来传共享 _buffer，UI/采样并发时会与 Clear/Add 撞车。
            var instances = new List<InstanceValue>(32);
            lock (_pdhGate) { _pdh.TryRead(NetRxPath, instances); }

            if (instances.Count == 0)
            {
                _resolvedAdapter = wanted ?? "";
                return;
            }

            if (!string.IsNullOrEmpty(wanted) && HasName(instances, wanted))
            {
                _resolvedAdapter = wanted;
                return;
            }

            string best = "";
            double bestValue = -1;
            for (int i = 0; i < instances.Count; i++)
            {
                InstanceValue item = instances[i];
                if (!IsRealAdapter(item.Name)) continue;
                if (item.Value > bestValue)
                {
                    bestValue = item.Value;
                    best = item.Name;
                }
            }

            _resolvedAdapter = best;
            if (!string.IsNullOrEmpty(wanted) && !string.Equals(wanted, best, StringComparison.OrdinalIgnoreCase))
                Log.Warn("配置的网卡未找到: " + wanted + "，自动改用: " + best);
        }

        static bool HasName(List<InstanceValue> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool IsRealAdapter(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            for (int i = 0; i < AdapterExcludes.Length; i++)
                if (lower.Contains(AdapterExcludes[i])) return false;
            return true;
        }

        void SampleMemory(MetricsSnapshot snap)
        {
            var status = new MemoryStatusEx();
            status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            if (!GlobalMemoryStatusEx(ref status)) return;

            const double gb = 1024.0 * 1024.0 * 1024.0;
            snap.MemTotalGb = status.TotalPhys / gb;
            snap.MemUsedGb = (status.TotalPhys - status.AvailPhys) / gb;
            snap.MemPercent = status.MemoryLoad;
        }

        public static List<DriveSample> ReadDrives(List<string> whitelist)
        {
            var result = new List<DriveSample>();
            const double gb = 1024.0 * 1024.0 * 1024.0;

            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                        if (whitelist != null && whitelist.Count > 0 && !whitelist.Contains(drive.Name))
                            continue;

                        result.Add(new DriveSample
                        {
                            Name = drive.Name,
                            FreeGb = drive.AvailableFreeSpace / gb,
                            TotalGb = drive.TotalSize / gb
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取分区失败: " + ex.Message);
            }

            return result;
        }

        public static string[] GetFixedDrives()
        {
            List<DriveSample> drives = ReadDrives(null);
            var names = new string[drives.Count];
            for (int i = 0; i < drives.Count; i++) names[i] = drives[i].Name;
            return names;
        }

        public static string[] GetNetworkAdapters()
        {
            string[] raw = PdhQuery.EnumerateInstances(NetRxPath);
            var list = new List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
                if (IsRealAdapter(raw[i])) list.Add(raw[i]);
            return list.ToArray();
        }

        public static int GetGpuCount()
        {
            string[] raw = PdhQuery.EnumerateInstances(GpuPath);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < raw.Length; i++)
            {
                string luid = ExtractLuid(raw[i]);
                if (luid.Length > 0) set.Add(luid);
            }
            return set.Count;
        }

        static double Clamp01(double percent)
        {
            if (double.IsNaN(percent)) return 0;
            if (percent < 0) return 0;
            if (percent > 100) return 100;
            return percent;
        }

        public void Dispose()
        {
            // 不要用 Stop()：它会把 _thread 清空，这里需要知道线程是否真的退出了。
            _running = false;
            Thread t = _thread;
            _thread = null;
            bool stopped = true;
            if (t != null && t.IsAlive)
            {
                try { stopped = t.Join(2000); } catch { stopped = false; }
            }

            // 采样线程还在 nvidia-smi 里时关闭 PDH 句柄会抛 NullReferenceException，
            // 交给进程退出回收即可；确认线程退出后也要在锁里关（P1-9）。
            if (stopped)
            {
                lock (_pdhGate) { _pdh.Dispose(); }
            }
        }
    }
}
