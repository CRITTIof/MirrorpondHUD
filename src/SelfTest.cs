using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;

namespace MirrorpondHUD
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

                // nvidia-smi 冷启动偶尔要好几秒，这里最多等 15 秒
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (last == null && DateTime.UtcNow < deadline) Thread.Sleep(100);

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
                    bool a = RegisterIfSet(hotkeys, HotkeyManager.ActionToggleOverlay, settings.Hotkeys.ToggleOverlay);
                    bool b = RegisterIfSet(hotkeys, HotkeyManager.ActionOpenSettings, settings.Hotkeys.OpenSettings);
                    bool c = RegisterIfSet(hotkeys, HotkeyManager.ActionToggleLock, settings.Hotkeys.ToggleLock);
                    string commandHotkey = CommandHotkey(settings);
                    bool d = RegisterIfSet(hotkeys, HotkeyManager.CardAction("selftest-command"), commandHotkey);

                    Fix(sb, "热键",
                        Show(settings.Hotkeys.ToggleOverlay, a) + "  " +
                        Show(commandHotkey, d) + "  " +
                        Show(settings.Hotkeys.OpenSettings, b) + "  " +
                        Show(settings.Hotkeys.ToggleLock, c));
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
            bool cardOk = false;
            bool keyNameOk = false;
            bool keyLimitOk = false;
            bool keyOverflowOk = false;
            bool commandOk = false;
            bool truncateOk = false;
            bool toolLabelOk = false;
            bool secretOk = false;
            bool chatHotkeyOk = false;

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
                var command = new CommandSettings { Shell = "cmd" };
                CommandResult result = CommandRunner.Run(command, "echo overlay-selftest", CancellationToken.None);
                commandOk = !result.Failed && !result.TimedOut &&
                            result.Output != null &&
                            result.Output.IndexOf("overlay-selftest", StringComparison.Ordinal) >= 0;
                if (!commandOk)
                    Log.Warn("命令行自检输出异常: failed=" + result.Failed + " timedout=" + result.TimedOut +
                             " out=[" + result.Output + "] err=[" + result.Error + "]");
            }
            catch (Exception ex)
            {
                Log.Error("命令行自检异常", ex);
            }

            try
            {
                var limit = new CommandSettings { Truncate = true, MaxLineChars = 10 };
                var noLimit = new CommandSettings { Truncate = false, MaxLineChars = 10 };
                truncateOk = CommandRunner.TruncateLine("0123456789ABCDEF", limit) == "…6789ABCDEF"
                          && CommandRunner.TruncateLine("short", limit) == "short"
                          && CommandRunner.TruncateLine("0123456789ABCDEF", noLimit) == "0123456789ABCDEF";
            }
            catch (Exception ex)
            {
                Log.Error("命令行截断自检异常", ex);
            }

            try
            {
                var custom = new CommandSettings
                {
                    Shell = "custom",
                    CustomShell = "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoProfile -Command {cmd}"
                };

                toolLabelOk = CommandRunner.ToolLabel(new CommandSettings { Shell = "cmd" }) == "CMD"
                           && CommandRunner.ToolLabel(new CommandSettings { Shell = "powershell" }) == "PWSH"
                           && CommandRunner.ToolLabel(custom) == "PWSH"
                           && CommandRunner.BuildPrompt(new CommandSettings { Shell = "cmd" }).StartsWith("[CMD] [", StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Log.Error("命令行提示符自检异常", ex);
            }

            try
            {
                var settings = new Settings
                {
                    Style = { FontSize = 21, IconScale = 1.35 },
                    Overlay = { X = 123.5, Locked = false },
                    Data = { DiskDrives = new List<string> { "C:\\" } }
                };
                settings.Cards.Add(new CardSettings
                {
                    Id = "self", Kind = "text", Name = "自检", Content = "x{cpu}", Resident = true, Spacing = 16
                });
                settings.Cards.Add(new CardSettings
                {
                    Id = "cmd", Kind = "command", Name = "自检命令行", Resident = false, Hotkey = "F9",
                    Command = { Shell = "pwsh", MaxLineChars = 77, Truncate = false, WorkingDirectory = "C:\\Temp" }
                });
                settings.Cards.Add(new CardSettings
                {
                    Id = "media", Kind = "media", Name = "自检媒体", Resident = false, Hotkey = "F11",
                    Media =
                    {
                        Path = "C:\\demo.png", Fit = "Cover", Width = 480, Muted = false,
                        Rounded = false, Decoder = "FFmpeg", DecoderPath = "C:\\ffmpeg.exe"
                    }
                });

                Settings back = Json.Deserialize<Settings>(Json.Serialize(settings));
                configOk = back != null
                           && back.Cards != null && back.Cards.Count == 3
                           && back.Cards[0].Content == "x{cpu}"
                           && back.Cards[0].Kind == "text"
                           && Math.Abs(back.Style.FontSize - 21) < 0.001
                           && Math.Abs(back.Style.IconScale - 1.35) < 0.001
                           && Math.Abs(back.Overlay.X - 123.5) < 0.001
                           && back.Overlay.Locked == false
                           && back.Data.DiskDrives.Count == 1
                           && back.Cards[1].Command.Shell == "pwsh"
                           && back.Cards[1].Command.MaxLineChars == 77
                           && back.Cards[1].Command.Truncate == false
                           && back.Cards[1].Command.WorkingDirectory == "C:\\Temp"
                           && back.Cards[2].Media.Path == "C:\\demo.png"
                           && back.Cards[2].Media.Fit == "Cover"
                           && Math.Abs(back.Cards[2].Media.Width - 480) < 0.001
                           && back.Cards[2].Media.Muted == false
                           && back.Cards[2].Media.Rounded == false
                           && back.Cards[2].Media.Decoder == "FFmpeg"
                           && back.Cards[2].Media.DecoderPath == "C:\\ffmpeg.exe"
                           && MediaSettings.NormalizeDecoder("mediafoundation") == "BuiltIn"
                           && MediaSettings.NormalizeDecoder("ffmpeg.exe") == "FFmpeg"
                           && MediaSettings.NormalizeDecoder("whatever") == "Auto";
            }
            catch (Exception ex)
            {
                Log.Error("配置往返自检异常", ex);
            }

            try
            {
                var settings = new Settings();
                settings.Normalize();
                CardModel.Normalize(settings);

                string clock = CardRegistry.Get("clock").Render(
                    new CardRenderContext(settings, Screenshots.DemoSnapshot(), null,
                                          new CardSettings { Kind = "clock", Content = "{time}" }));

                var vars = Variables.Build(Screenshots.DemoSnapshot(), settings, null);
                bool weekdayOk = vars.ContainsKey("weekday_zh") && vars.ContainsKey("weekday_en")
                              && vars["weekday_zh"].ToString().StartsWith("星期", StringComparison.Ordinal)
                              && vars["weekday_en"].ToString().Length > 3;

                cardOk = CardRegistry.Modules.Count >= 3
                         && CardRegistry.Get("media") is IRichCardModule
                         && CardRegistry.Get("infotext") is InfoTextCard
                         && settings.Cards.Count >= 1
                         && !string.IsNullOrWhiteSpace(clock)
                         && clock.IndexOf('{') < 0
                         && weekdayOk;
            }
            catch (Exception ex)
            {
                Log.Error("卡片模块自检异常", ex);
            }

            try
            {
                const string secret = "sk-selftest-0123456789";

                string stored = SecretStore.Protect(secret);
                string plain;
                bool roundTrip = SecretStore.IsProtected(stored)
                              && SecretStore.TryUnprotect(stored, out plain)
                              && plain == secret;

                // 落盘的东西里不能有明文 Key
                var chat = new ChatSettings { Model = "selftest-model" };
                chat.SetApiKey(secret);

                var settings = new Settings();
                settings.Cards.Add(new CardSettings { Id = "chat", Kind = "chat", Name = "自检对话", Chat = chat });

                string json = Json.Serialize(settings);
                bool notPlain = json.IndexOf(secret, StringComparison.Ordinal) < 0
                             && json.IndexOf(SecretStore.Prefix, StringComparison.Ordinal) > 0;

                Settings back = Json.Deserialize<Settings>(json);
                bool usable = back != null && back.Cards != null && back.Cards.Count == 1
                           && back.Cards[0].Chat != null
                           && back.Cards[0].Chat.HasApiKey
                           && back.Cards[0].Chat.PlainApiKey == secret;

                // 旧配置里的明文 Key：Normalize 之后变密文，而且还能用
                var legacy = new ChatSettings { ApiKey = secret };
                legacy.Normalize();

                // 设置页：已存 Key 时输入框空的、只显示提示；输入新 Key 才改，提交后又收起
                var cardSettings = new CardSettings { Id = "chat-ui", Kind = "chat", Name = "自检对话", Chat = new ChatSettings() };
                var editor = new CardEditor(cardSettings, new ChatCard(), true);

                bool uiHidden = editor.ChatApiKey.Length == 0
                             && editor.ChatApiKeyPlaceholderVisibility == System.Windows.Visibility.Visible
                             && editor.ChatApiKeyClearVisibility == System.Windows.Visibility.Collapsed;

                cardSettings.Chat.SetApiKey(secret);
                var withKey = new CardEditor(cardSettings, new ChatCard(), true);
                bool uiMasked = withKey.ChatApiKey.Length == 0
                             && withKey.ChatApiKeyPlaceholder == ChatSettings.KeyPlaceholder
                             && withKey.ChatApiKeyPlaceholderVisibility == System.Windows.Visibility.Visible
                             && withKey.ChatApiKeyClearVisibility == System.Windows.Visibility.Visible;

                withKey.ChatApiKey = "sk-typed-123";
                bool uiTyping = withKey.ChatApiKey == "sk-typed-123"
                             && withKey.ChatApiKeyPlaceholderVisibility == System.Windows.Visibility.Collapsed;

                withKey.CommitChatApiKey();
                bool uiCommit = withKey.ChatApiKey.Length == 0
                             && cardSettings.Chat.PlainApiKey == "sk-typed-123"
                             && SecretStore.IsProtected(cardSettings.Chat.ApiKey);

                secretOk = roundTrip && notPlain && usable
                        && SecretStore.IsProtected(legacy.ApiKey)
                        && legacy.PlainApiKey == secret
                        && uiHidden && uiMasked && uiTyping && uiCommit;
            }
            catch (Exception ex)
            {
                Log.Error("Key 加密自检异常", ex);
            }

            try
            {
                var chatCard = new ChatCard();
                uint mods, vk;

                chatHotkeyOk = HotkeyManager.TryParse(ChatCard.DefaultModeHotkey, out mods, out vk) && vk != 0
                            && chatCard.HotkeyActions != null
                            && chatCard.HotkeyActions.Count == 2
                            && chatCard.HotkeyActions[0].Action == ChatCard.ActionToggleMode
                            && chatCard is ICardHotkeyTarget
                            && chatCard.NeedsMouseInput;
            }
            catch (Exception ex)
            {
                Log.Error("对话热键自检异常", ex);
            }

            if (!(templateOk && unknownOk && collapseOk && thresholdOk && configOk && cardOk &&
                  keyNameOk && keyLimitOk && keyOverflowOk &&
                  commandOk && truncateOk && toolLabelOk && secretOk && chatHotkeyOk)) failures++;

            Fix(sb, "逻辑自检", string.Format(
                "模板={0} 未知占位符={1} 折叠={2} 阈值内展开={3} 配置读写={4} 卡片模块={11} 键名={5} 按键长度={6} 超长截断={7} 命令行={8} 行截断={9} 工具名={10} Key加密={12} 对话热键={13}",
                templateOk ? "OK" : "FAIL",
                unknownOk ? "OK" : "FAIL",
                collapseOk ? "OK" : "FAIL",
                thresholdOk ? "OK" : "FAIL",
                configOk ? "OK" : "FAIL",
                keyNameOk ? "OK" : "FAIL",
                keyLimitOk ? "OK" : "FAIL",
                keyOverflowOk ? "OK" : "FAIL",
                commandOk ? "OK" : "FAIL",
                truncateOk ? "OK" : "FAIL",
                toolLabelOk ? "OK" : "FAIL",
                cardOk ? "OK" : "FAIL",
                secretOk ? "OK" : "FAIL",
                chatHotkeyOk ? "OK" : "FAIL"));
        }

        static bool RegisterIfSet(HotkeyManager hotkeys, string action, string combination)
        {
            if (string.IsNullOrWhiteSpace(combination)) return true;   // 未设置也算正常
            return hotkeys.Register(action, combination);
        }

        static string CommandHotkey(Settings settings)
        {
            if (settings == null || settings.Cards == null) return "";
            foreach (CardSettings card in settings.Cards)
                if (card != null && string.Equals(card.Kind, "command", StringComparison.OrdinalIgnoreCase))
                    return card.Hotkey ?? "";
            return "";
        }

        static string Show(string combination, bool ok)
        {
            if (string.IsNullOrWhiteSpace(combination)) return "(未设置)=OFF";
            return combination + "=" + (ok ? "OK" : "占用");
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
