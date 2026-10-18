using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace WindowOverlay.Core
{
    /// <summary>通过 nvidia-smi 读取温度 / 显存 / 功耗。没有 N 卡时静默降级。</summary>
    internal sealed class NvidiaSmi
    {
        const string Query =
            "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw " +
            "--format=csv,noheader,nounits";

        readonly object _gate = new object();
        string _exe;
        bool _searched;
        bool _reportedMissing;
        DateTime _nextTryUtc = DateTime.MinValue;
        NvidiaSample _current = new NvidiaSample();

        public NvidiaSample Current
        {
            get { lock (_gate) { return _current; } }
        }

        public void Sample(int intervalMs)
        {
            lock (_gate)
            {
                if (DateTime.UtcNow < _nextTryUtc) return;
                _nextTryUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(500, intervalMs));
            }

            string exe = FindExe();
            if (exe == null)
            {
                lock (_gate)
                {
                    if (_current.Available)
                    {
                        _current = new NvidiaSample();
                        Log.Info("nvidia-smi 不再可用");
                    }
                }
                return;
            }

            try
            {
                var psi = new ProcessStartInfo(exe, Query)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();
                    string text = proc.StandardOutput.ReadToEnd();
                    if (!proc.WaitForExit(1500))
                    {
                        try { proc.Kill(); } catch { }
                        return;
                    }
                    if (proc.ExitCode != 0) return;

                    NvidiaSample sample = Parse(text);
                    if (sample != null)
                    {
                        lock (_gate) { _current = sample; }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("nvidia-smi 调用失败: " + ex.Message);
            }
        }

        NvidiaSample Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string line = text.Split('\n')[0].Trim().Trim('\r');
            if (line.Length == 0) return null;

            string[] parts = line.Split(',');
            if (parts.Length < 3) return null;

            var sample = new NvidiaSample { Available = true, Name = parts[0].Trim() };
            if (parts.Length > 1) sample.Utilization = Num(parts[1]);
            if (parts.Length > 2) sample.VramUsedMb = Num(parts[2]);
            if (parts.Length > 3) sample.VramTotalMb = Num(parts[3]);
            if (parts.Length > 4) sample.TempC = Num(parts[4]);
            if (parts.Length > 5) sample.PowerW = Num(parts[5]);
            return sample;
        }

        static double Num(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return double.NaN;
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return double.NaN; // "[N/A]" 之类
        }

        string FindExe()
        {
            if (_searched) return _exe;
            _searched = true;

            try
            {
                string system32 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
                if (File.Exists(system32)) { _exe = system32; return _exe; }

                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in pathVar.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                    if (File.Exists(candidate)) { _exe = candidate; return _exe; }
                }
            }
            catch { }

            if (!_reportedMissing)
            {
                _reportedMissing = true;
                Log.Info("未找到 nvidia-smi，GPU 温度/显存/功耗不可用");
            }
            return null;
        }
    }
}
