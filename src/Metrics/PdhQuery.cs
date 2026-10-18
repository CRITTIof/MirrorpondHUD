using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowOverlay.Core
{
    internal struct InstanceValue
    {
        public string Name;
        public double Value;
    }

    /// <summary>PDH 性能计数器封装。全部使用英文计数器名，中文系统同样可用。</summary>
    internal sealed class PdhQuery : IDisposable
    {
        const uint PDH_FMT_DOUBLE = 0x00000200;
        const uint PDH_MORE_DATA = 0x800007D2;
        const uint PDH_NO_DATA = 0x800007D6;
        const uint PDH_INVALID_DATA = 0xC0000BC6;

        [StructLayout(LayoutKind.Sequential)]
        struct PdhFmtCounterValue
        {
            public uint CStatus;
            public double DoubleValue;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PdhFmtCounterValueItemW
        {
            public IntPtr SzName;
            public PdhFmtCounterValue FmtValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);

        [DllImport("pdh.dll")]
        static extern uint PdhCloseQuery(IntPtr query);

        IntPtr _query = IntPtr.Zero;
        readonly Dictionary<string, IntPtr> _counters = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        public PdhQuery()
        {
            uint rc = PdhOpenQueryW(null, IntPtr.Zero, out _query);
            if (rc != 0)
            {
                _query = IntPtr.Zero;
                throw new InvalidOperationException("PdhOpenQuery 失败: 0x" + rc.ToString("X8"));
            }
        }

        public bool IsOpen { get { return _query != IntPtr.Zero; } }

        public bool TryAdd(string path)
        {
            if (!IsOpen) return false;
            if (_counters.ContainsKey(path)) return true;

            IntPtr handle;
            uint rc = PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out handle);
            if (rc != 0)
            {
                Log.Warn("添加计数器失败 0x" + rc.ToString("X8") + " : " + path);
                return false;
            }
            _counters[path] = handle;
            return true;
        }

        public bool Collect()
        {
            if (!IsOpen) return false;
            return PdhCollectQueryData(_query) == 0;
        }

        /// <summary>读取一次计数器数组（通配实例）。成功返回 true。</summary>
        public bool TryRead(string path, List<InstanceValue> output)
        {
            output.Clear();
            if (!IsOpen) return false;
            if (!_counters.TryGetValue(path, out IntPtr counter)) return false;

            uint size = 0;
            uint count = 0;
            uint rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
            if (rc == 0 && count == 0) return true;           // 没有实例
            if (rc == PDH_NO_DATA) return true;               // 首轮采样还没数据，下轮再取
            if (rc == PDH_INVALID_DATA) return true;          // 个别计数器（如无活动的 GPU）暂时无效
            if (rc != PDH_MORE_DATA)
            {
                Log.Warn("读取计数器失败 0x" + rc.ToString("X8") + " : " + path);
                return false;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, ref count, buffer);
                if (rc == PDH_NO_DATA || rc == PDH_INVALID_DATA) return true;
                if (rc != 0)
                {
                    Log.Warn("读取计数器数据失败 0x" + rc.ToString("X8") + " : " + path);
                    return false;
                }

                int stride = Marshal.SizeOf(typeof(PdhFmtCounterValueItemW));
                for (int i = 0; i < count; i++)
                {
                    IntPtr ptr = new IntPtr(buffer.ToInt64() + i * stride);
                    PdhFmtCounterValueItemW item = (PdhFmtCounterValueItemW)Marshal.PtrToStructure(ptr, typeof(PdhFmtCounterValueItemW));
                    if (item.FmtValue.CStatus != 0) continue; // 无效样本跳过

                    string name = item.SzName == IntPtr.Zero ? "" : Marshal.PtrToStringUni(item.SzName);
                    output.Add(new InstanceValue { Name = name ?? "", Value = item.FmtValue.DoubleValue });
                }
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>读取单个值时用得着的便捷方法。</summary>
        public double ReadSum(string path)
        {
            var list = new List<InstanceValue>(32);
            if (!TryRead(path, list)) return 0;
            double sum = 0;
            for (int i = 0; i < list.Count; i++) sum += list[i].Value;
            return sum;
        }

        /// <summary>临时查询一次实例名列表（用于界面里选择网卡）。</summary>
        public static string[] EnumerateInstances(string counterPath)
        {
            try
            {
                using (var q = new PdhQuery())
                {
                    if (!q.TryAdd(counterPath)) return new string[0];
                    q.Collect();
                    Thread.Sleep(120);
                    q.Collect();

                    var list = new List<InstanceValue>(32);
                    q.TryRead(counterPath, list);

                    var names = new List<string>(list.Count);
                    for (int i = 0; i < list.Count; i++) names.Add(list[i].Name);
                    return names.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("枚举计数器实例失败: " + ex.Message);
                return new string[0];
            }
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                try { PdhCloseQuery(_query); } catch { }
                _query = IntPtr.Zero;
            }
            _counters.Clear();
        }
    }
}
