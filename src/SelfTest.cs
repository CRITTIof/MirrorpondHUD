using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using WindowOverlay.Core;
using WindowOverlay.Ui;

namespace WindowOverlay
{
    /// <summary>--selftest：在真实环境跑一遍采样 / 键盘钩子 / 热键注册，把结果写进日志。</summary>
    internal static class SelfTest
    {
        const byte VkF13 = 0x7C;

        [StructLayout(LayoutKind.Sequential)]
        struct KeybdInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct Input
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public KeybdInput Keyboard;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32.dll")]
        static extern ushort MapVirtualKey(uint code, uint mapType);

        static void SendKey(ushort vk)
        {
            var down = new Input
            {
                Type = 1,
                Keyboard = new KeybdInput
                {
                    VirtualKey = vk,
                    ScanCode = MapVirtualKey(vk, 0),
                    Flags = 0
                }
            };
            var up = new Input
            {
                Type = 1,
                Keyboard = new KeybdInput
                {
                    VirtualKey = vk,
                    ScanCode = MapVirtualKey(vk, 0),
                    Flags = 2
                }
            };
            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(Input)));
        }

        public static int Run()
        {
            var sb = new StringBuilder();
            int failures = 0;

            Settings settings = ConfigStore.Load();

            Fix(sb, "配置", ConfigStore.Path);

            MetricsSnapshot last = null;
            try
            {
                var engine = new MetricEngine(settings);
                engine.Updated += snapshot => last = snapshot;
                bool started = engine.Start();
                Thread.Sleep(4000);
                engine.Dispose();

                if (!started || last == null)
                {
                    failures++;
                    Fix(sb, "采样", "失败");
                }
                else
                {
                    Fix(sb, "CPU", F(last.CpuPercent) + "%");
                    Fix(sb, "GPU", (last.GpuAvailable ? F(last.GpuPercent) : "不可用") + "%");
                    Fix(sb, "内存", F(last.MemUsedGb) + "/" + F(last.MemTotalGb) + " GB (" + F(last.MemPercent) + "%)");
                    Fix(sb, "磁盘", F(last.DiskPercent) + "%  读 " + Variables.FormatRate(last.DiskReadBps) +
                                      "  写 " + Variables.FormatRate(last.DiskWriteBps));
                    Fix(sb, "网络", "↓" + Variables.FormatRate(last.NetDownBps) +
                                    " ↑" + Variables.FormatRate(last.NetUpBps) + "  (" + last.NetAdapter + ")");

                    var drives = new StringBuilder();
                    foreach (DriveSample d in last.Drives)
                        drives.Append(d.Name).Append(' ').Append(F(d.FreeGb)).Append('/').Append(F(d.TotalGb)).Append("GB  ");
                    Fix(sb, "分区", drives.Length == 0 ? "无" : drives.ToString().Trim());

                    NvidiaSample nv = last.Nvidia;
                    Fix(sb, "NVIDIA", nv.Available
                        ? nv.Name + "  温度 " + F(nv.TempC) + "℃  显存 " + F(nv.VramUsedMb) + "/" + F(nv.VramTotalMb) +
                          "MB  功耗 " + F(nv.PowerW) + "W"
                        : "不可用");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Log.Error("自检采样异常", ex);
            }

            try
            {
                var history = new KeyHistory();
                history.Configure(14, 3);

                var hook = new KeyboardHook(history) { AllowInjected = true };
                bool ok = hook.Start();

                if (ok)
                {
                    Thread.Sleep(250);
                    SendKey(VkF13);
                    Thread.Sleep(600);
                }

                bool captured = history.Total > 0;
                hook.Dispose();

                if (!ok) failures++;

                Fix(sb, "键盘钩子", !ok
                    ? "安装失败"
                    : captured
                        ? "正常（回环捕获到 " + history.Render() + "）"
                        : "已安装；本环境未放行模拟按键，请用真实按键确认");
            }
            catch (Exception ex)
            {
                failures++;
                Log.Error("自检键盘钩子异常", ex);
            }

            try
            {
                using (var hotkeys = new HotkeyManager())
                {
                    bool a = hotkeys.Register(HotkeyManager.ActionToggleOverlay, settings.Hotkeys.ToggleOverlay);
                    bool b = hotkeys.Register(HotkeyManager.ActionOpenSettings, settings.Hotkeys.OpenSettings);
                    bool c = hotkeys.Register(HotkeyManager.ActionToggleLock, settings.Hotkeys.ToggleLock);
                    Fix(sb, "热键", settings.Hotkeys.ToggleOverlay + "=" + (a ? "OK" : "占用") + "  " +
                                    settings.Hotkeys.OpenSettings + "=" + (b ? "OK" : "占用") + "  " +
                                    settings.Hotkeys.ToggleLock + "=" + (c ? "OK" : "占用"));
                }
            }
            catch (Exception ex)
            {
                failures++;
                Log.Error("自检热键异常", ex);
            }

            try
            {
                string[] adapters = MetricEngine.GetNetworkAdapters();
                Fix(sb, "网卡", adapters.Length == 0 ? "无" : string.Join(" | ", adapters));
                Fix(sb, "GPU 数量", MetricEngine.GetGpuCount().ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Log.Error("自检设备枚举异常", ex);
            }

            RunUnitChecks(sb, ref failures);

            sb.AppendLine();
            sb.AppendLine("失败项: " + failures);
            Log.Info("=== 自检结果 ===\n" + sb);

            return failures;
        }

        /// <summary>不依赖系统的纯逻辑回归检查。</summary>
        static void RunUnitChecks(StringBuilder sb, ref int failures)
        {
            bool templateOk = false;
            bool unknownOk = false;
            bool collapseOk = false;
            bool thresholdOk = false;
            bool configOk = false;
            bool keyNameOk = false;
            bool keyLimitOk = false;
            bool keyOverflowOk = false;

            try
            {
                var vars = new Dictionary<string, object> { { "cpu", 23.44 }, { "n", 5 } };
                List<string> unknown;
                string text = TemplateEngine.Render("CPU {cpu:0.0}% {{ok}} {n,3} {nope}", vars, out unknown);

                templateOk = text == "CPU 23.4% {ok}   5 {nope}";
                unknownOk = unknown.Count == 1 && unknown[0] == "nope";
            }
            catch (Exception ex)
            {
                Log.Error("模板自检异常", ex);
            }

            try
            {
                var longRun = new KeyHistory();
                longRun.Configure(14, 3);
                for (int i = 0; i < 6; i++) longRun.Push("A");
                collapseOk = longRun.Render() == "A×6";

                var shortRun = new KeyHistory();
                shortRun.Configure(14, 3);
                shortRun.Push("A");
                shortRun.Push("A");
                shortRun.Push("A");
                thresholdOk = shortRun.Render() == "A A A";
            }
            catch (Exception ex)
            {
                Log.Error("按键折叠自检异常", ex);
            }

            try
            {
                keyNameOk = KeyNames.Of(0x08) == "Back"
                         && KeyNames.Of(0x14) == "Cap"
                         && KeyNames.Of(0x2D) == "ins";

                // 默认 18 字符：3 个 Space 共 17 字符（含分隔符），放得下
                var capped = new KeyHistory();
                capped.Configure(14, 3, 18);
                for (int i = 0; i < 3; i++) capped.Push("Space");
                keyLimitOk = capped.Render() == "Space Space Space";

                // 上限 15 字符时第 3 个 Space 就会超长（17 > 15），丢弃最旧的一个
                var tight = new KeyHistory();
                tight.Configure(14, 3, 15);
                for (int i = 0; i < 3; i++) tight.Push("Space");
                keyOverflowOk = tight.Render() == "Space Space";
            }
            catch (Exception ex)
            {
                Log.Error("按键长度自检异常", ex);
            }

            try
            {
                var settings = new Settings
                {
                    Content = "x{cpu}",
                    Style = { FontSize = 21 },
                    Overlay = { X = 123.5, Locked = false },
                    Data = { DiskDrives = new List<string> { "C:\\" } }
                };

                Settings back = Json.Deserialize<Settings>(Json.Serialize(settings));
                configOk = back != null
                           && back.Content == "x{cpu}"
                           && Math.Abs(back.Style.FontSize - 21) < 0.001
                           && Math.Abs(back.Overlay.X - 123.5) < 0.001
                           && back.Overlay.Locked == false
                           && back.Data.DiskDrives.Count == 1;
            }
            catch (Exception ex)
            {
                Log.Error("配置往返自检异常", ex);
            }

            if (!(templateOk && unknownOk && collapseOk && thresholdOk && configOk &&
                  keyNameOk && keyLimitOk && keyOverflowOk)) failures++;

            Fix(sb, "逻辑自检", string.Format(
                "模板={0} 未知占位符={1} 折叠={2} 阈值内展开={3} 配置读写={4} 键名={5} 按键长度={6} 超长截断={7}",
                templateOk ? "OK" : "FAIL",
                unknownOk ? "OK" : "FAIL",
                collapseOk ? "OK" : "FAIL",
                thresholdOk ? "OK" : "FAIL",
                configOk ? "OK" : "FAIL",
                keyNameOk ? "OK" : "FAIL",
                keyLimitOk ? "OK" : "FAIL",
                keyOverflowOk ? "OK" : "FAIL"));
        }

        static void Fix(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append(": ").Append(value).AppendLine();
        }

        static string F(double v)
        {
            if (double.IsNaN(v)) return "n/a";
            return v.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
