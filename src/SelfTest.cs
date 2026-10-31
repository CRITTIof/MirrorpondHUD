using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Media;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

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
            bool settingsEditorOk = false;
            bool stateStoreOk = false;
            bool renderOk = false;
            bool themeOk = false;
            bool restyleOk = false;
            bool glassPolicyOk = false;
            bool themeFileOk = false;
            bool themeRejectOk = false;
            bool themeCatalogOk = false;
            bool activationOk = false;
            bool cloneGuardOk = false;
            bool restyleHostOk = false;
            bool chatContextOk = false;
            bool singleInteractiveOk = false;
            bool widgetsOk = false;
            bool widgets2Ok = false;
            bool contextHostOk = false;
            bool jobQueueOk = false;

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
                var commandCard = new CardSettings
                {
                    Id = "cmd", Kind = "command", Name = "自检命令行", Resident = false, Hotkey = "F9"
                };
                commandCard.SetExtras(CommandCard.SettingsKey, new CommandSettings
                {
                    Shell = "pwsh", MaxLineChars = 77, Truncate = false, WorkingDirectory = "C:\\Temp"
                });
                settings.Cards.Add(commandCard);

                var mediaCard = new CardSettings
                {
                    Id = "media", Kind = "media", Name = "自检媒体", Resident = false, Hotkey = "F11"
                };
                mediaCard.SetExtras(MediaCard.SettingsKey, new MediaSettings
                {
                    Path = "C:\\demo.png", Fit = "Cover", Width = 480, Muted = false,
                    Rounded = false, Decoder = "FFmpeg", DecoderPath = "C:\\ffmpeg.exe"
                });
                settings.Cards.Add(mediaCard);

                Settings back = Json.Deserialize<Settings>(Json.Serialize(settings));
                CommandSettings backCommand = back == null || back.Cards == null || back.Cards.Count < 2
                    ? new CommandSettings()
                    : back.Cards[1].ExtrasAs<CommandSettings>(CommandCard.SettingsKey);
                MediaSettings backMedia = back == null || back.Cards == null || back.Cards.Count < 3
                    ? new MediaSettings()
                    : back.Cards[2].ExtrasAs<MediaSettings>(MediaCard.SettingsKey);

                configOk = back != null
                           && back.Cards != null && back.Cards.Count == 3
                           && back.Cards[0].Content == "x{cpu}"
                           && back.Cards[0].Kind == "text"
                           && Math.Abs(back.Style.FontSize - 21) < 0.001
                           && Math.Abs(back.Style.IconScale - 1.35) < 0.001
                           && Math.Abs(back.Overlay.X - 123.5) < 0.001
                           && back.Overlay.Locked == false
                           && back.Data.DiskDrives.Count == 1
                           && backCommand.Shell == "pwsh"
                           && backCommand.MaxLineChars == 77
                           && backCommand.Truncate == false
                           && backCommand.WorkingDirectory == "C:\\Temp"
                           && backMedia.Path == "C:\\demo.png"
                           && backMedia.Fit == "Cover"
                           && Math.Abs(backMedia.Width - 480) < 0.001
                           && backMedia.Muted == false
                           && backMedia.Rounded == false
                           && backMedia.Decoder == "FFmpeg"
                           && backMedia.DecoderPath == "C:\\ffmpeg.exe"
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

                // 旧 Kind 由模块自己认领（P1-3）：clock / text → infotext，预设按内容推断恢复
                // （CardSettings.Preset 默认是 metrics，旧配置也靠内容匹配来认回 clock / text）
                var legacySettings = new Settings();
                legacySettings.Cards.Add(new CardSettings
                {
                    Id = "old-clock", Kind = "clock", Content = Settings.DefaultClockContent
                });
                legacySettings.Cards.Add(new CardSettings
                {
                    Id = "old-text", Kind = "text", Content = Settings.DefaultTextContent
                });
                CardModel.Normalize(legacySettings);

                bool legacyOk = legacySettings.Cards.Count == 2
                             && legacySettings.Cards[0].Kind == "infotext"
                             && legacySettings.Cards[0].Preset == "clock"
                             && legacySettings.Cards[1].Kind == "infotext"
                             && legacySettings.Cards[1].Preset == "text";

                cardOk = CardRegistry.Modules.Count >= 3
                         && CardRegistry.Get("media") is IRichCardModule
                         && CardRegistry.Get("infotext") is InfoTextCard
                         && legacyOk
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

                var chatCardSettings = new CardSettings { Id = "chat", Kind = "chat", Name = "自检对话" };
                chatCardSettings.SetExtras(ChatCard.SettingsKey, chat);

                var settings = new Settings();
                settings.Cards.Add(chatCardSettings);

                string json = Json.Serialize(settings);
                bool notPlain = json.IndexOf(secret, StringComparison.Ordinal) < 0
                             && json.IndexOf(SecretStore.Prefix, StringComparison.Ordinal) > 0;

                Settings back = Json.Deserialize<Settings>(json);
                ChatSettings backChat = back == null || back.Cards == null || back.Cards.Count < 1
                    ? new ChatSettings()
                    : back.Cards[0].ExtrasAs<ChatSettings>(ChatCard.SettingsKey);

                bool usable = back != null && back.Cards != null && back.Cards.Count == 1
                           && backChat.HasApiKey
                           && backChat.PlainApiKey == secret;

                // 旧配置里的明文 Key：Normalize 之后变密文，而且还能用
                var legacy = new ChatSettings { ApiKey = secret };
                legacy.Normalize();

                // 设置页区块：已存 Key 时不回显、只显示提示；输入新 Key 并提交后收起
                var uiCard = new CardSettings { Id = "chat-ui", Kind = "chat", Name = "自检对话" };
                var editor = new ChatCardSettingsView(uiCard, null);

                bool uiHidden = editor.ChatApiKey.Length == 0
                             && editor.ChatApiKeyPlaceholderVisibility == System.Windows.Visibility.Visible
                             && editor.ChatApiKeyClearVisibility == System.Windows.Visibility.Collapsed;

                ChatCard.ChatOf(uiCard).SetApiKey(secret);
                var withKey = new ChatCardSettingsView(uiCard, null);
                bool uiMasked = withKey.ChatApiKey.Length == 0
                             && withKey.ChatApiKeyPlaceholder == ChatSettings.KeyPlaceholder
                             && withKey.ChatApiKeyPlaceholderVisibility == System.Windows.Visibility.Visible
                             && withKey.ChatApiKeyClearVisibility == System.Windows.Visibility.Visible;

                withKey.ChatApiKey = "sk-typed-123";
                bool uiTyping = withKey.ChatApiKey == "sk-typed-123"
                             && withKey.ChatApiKeyPlaceholderVisibility == System.Windows.Visibility.Collapsed;

                withKey.CommitChatApiKey();
                ChatSettings committed = ChatCard.ChatOf(uiCard);
                bool uiCommit = withKey.ChatApiKey.Length == 0
                             && committed.PlainApiKey == "sk-typed-123"
                             && SecretStore.IsProtected(committed.ApiKey);

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

            try
            {
                // 模块自己的设置（P1-1 / P1-2）：命令行卡把设置写进 Extras，并给出自己的设置区块。
                var editorCard = new CardSettings { Id = "cmd-ui", Kind = "command", Name = "自检命令行" };
                var commandEditor = new CardEditor(editorCard, new CommandCard(), new Settings(), true);

                settingsEditorOk = commandEditor.SettingsSection != null
                                && commandEditor.SettingsSectionVisibility == System.Windows.Visibility.Visible
                                && editorCard.Extras.ContainsKey(CommandCard.SettingsKey)
                                && editorCard.ExtrasAs<CommandSettings>(CommandCard.SettingsKey).Shell == "cmd";
            }
            catch (Exception ex)
            {
                Log.Error("卡片设置区块自检异常", ex);
            }

            try
            {
                // 每卡状态存储（P1-12）：原子写、回读一致、拒绝路径穿越、不留 .tmp
                string id = "selftest-" + Guid.NewGuid().ToString("N");
                string dir = CardStateStore.DirectoryFor(id);

                bool wrote = CardStateStore.Write(id, "state.json", "{\"n\":1}");
                bool read = CardStateStore.Read(id, "state.json") == "{\"n\":1}";
                bool noTmp = dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "state.json.tmp"));

                bool rejected = CardStateStore.Write(id, "..\\escape.txt", "x") == false
                             && CardStateStore.Write(id, "../escape.txt", "x") == false
                             && CardStateStore.Read(id, "..\\escape.txt") == null
                             && CardStateStore.Write("..\\bad", "a.json", "x") == false;

                bool deleted = CardStateStore.Delete(id, "state.json");
                bool gone = CardStateStore.Read(id, "state.json") == null;

                stateStoreOk = wrote && read && noTmp && rejected && deleted && gone
                            && !string.IsNullOrEmpty(dir);
            }
            catch (Exception ex)
            {
                Log.Error("卡片状态存储自检异常", ex);
            }

            try
            {
                // P0-5：一帧一份变量表（同一个字典实例被复用）+ 模块自己声明刷新节奏
                var shared = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "time", "12:34:56" },
                    { "cpu", 1.0 }
                };

                var context = new CardRenderContext(new Settings(), Screenshots.DemoSnapshot(), null,
                                                    new CardSettings { Kind = "infotext", Preset = "clock", Content = "{time}" },
                                                    shared);

                bool sharedUsed = ReferenceEquals(context.Variables, shared)
                               && context.Render("{time}") == "12:34:56";

                ICardModule infotext = CardRegistry.Get("infotext");
                bool cadenceOk = infotext.RefreshIntervalMs(new CardSettings { Kind = "infotext", Preset = "clock" }) == 1000
                              && infotext.RefreshIntervalMs(new CardSettings { Kind = "infotext", Preset = "keys" }) == 250
                              && infotext.RefreshIntervalMs(new CardSettings { Kind = "infotext", Preset = "metrics" }) == 0;

                // 阶段 C：媒体卡支持就地换肤（样式变化不重建，视频不重启）
                bool styleAwareOk = CardRegistry.Get("media") is ICardStyleAware;

                try
                {
                    var mediaView = new MediaView();
                    mediaView.ApplyStyle(new StyleSettings { FontSize = 20, Foreground = "#FFFFFF", Padding = 16 }, 14);
                }
                catch (Exception ex)
                {
                    styleAwareOk = false;
                    Log.Error("媒体卡换肤自检异常", ex);
                }

                renderOk = sharedUsed && cadenceOk && styleAwareOk;
            }
            catch (Exception ex)
            {
                Log.Error("渲染上下文自检异常", ex);
            }

            try
            {
                // P2-1：毛玻璃实现选择是纯决策——设置 + 两块底板可用性 → 实现，窗口只按结果接线。
                glassPolicyOk =
                    GlassPolicy.Resolve(false, "Dwm", true, true) == GlassBackdropKind.None
                 && GlassPolicy.Resolve(true, "Dwm", true, true) == GlassBackdropKind.DesktopAcrylic
                 && GlassPolicy.Resolve(true, "Dwm", true, false) == GlassBackdropKind.SystemAcrylic
                 && GlassPolicy.Resolve(true, "Dwm", false, false) == GlassBackdropKind.CompatCapture
                 && GlassPolicy.Resolve(true, "System", true, true) == GlassBackdropKind.SystemAcrylic
                 && GlassPolicy.Resolve(true, "System", false, true) == GlassBackdropKind.CompatCapture
                 && GlassPolicy.Resolve(true, "Compat", true, true) == GlassBackdropKind.CompatCapture;
            }
            catch (Exception ex)
            {
                Log.Error("毛玻璃策略自检异常", ex);
            }

            try
            {
                // 外观令牌（P1）：默认派生值必须等于旧 alpha 常量，非法值要被归一化掉。
                ThemeTokens defaults = ThemeResolver.Resolve(new StyleSettings());
                bool defaultOk = defaults.Accent == System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xEB)
                              && defaults.Danger == System.Windows.Media.Color.FromRgb(0xD1, 0x43, 0x43);

                System.Windows.Media.Color fg = System.Windows.Media.Color.FromRgb(0xE6, 0xED, 0xF3);
                bool mutedOk = defaults.Muted.A == (byte)Math.Round(0.45 * 255)
                            && defaults.Muted.R == fg.R && defaults.Muted.G == fg.G && defaults.Muted.B == fg.B;

                bool scrollOk = defaults.Scrollbar.A == (byte)Math.Round(0.18 * 255)
                             && defaults.ScrollbarHot.A == (byte)Math.Round(0.40 * 255);

                bool validateOk = !Settings.IsColorToken("#GGGGGG")
                               && !Settings.IsColorToken("123456")
                               && Settings.IsColorToken("")
                               && Settings.IsColorToken("#80FFFFFF");

                var bad = new Settings { Style = { Accent = "nope", Muted = "#12" } };
                bad.Normalize();
                bool normalizeOk = bad.Style.Accent == "#2F6FEB" && bad.Style.Muted == "";

                byte ca, cr, cg, cb;
                bool codeOk = ColorCode.TryParse("#80E6EDF3", out ca, out cr, out cg, out cb)
                           && ca == 0x80 && cr == 0xE6 && cg == 0xED && cb == 0xF3
                           && !ColorCode.TryParse("nope", out ca, out cr, out cg, out cb)
                           && !ColorCode.TryParse("#12345", out ca, out cr, out cg, out cb);

                // 非法颜色必须回落到调用方给的 fallback，而不是静默变成白色。
                ThemeTokens badToken = ThemeResolver.Resolve(new StyleSettings { Foreground = "nope" });
                bool fallbackOk = OverlaySurface.ParseColor("nope", fg) == fg
                               && badToken.Muted == ThemeResolver.WithAlpha(fg, 0.45)
                               && badToken.Accent == System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xEB);

                themeOk = defaultOk && mutedOk && scrollOk && validateOk && normalizeOk && codeOk && fallbackOk;
            }
            catch (Exception ex)
            {
                Log.Error("外观令牌自检异常", ex);
            }

            try
            {
                // P2：CLI 卡片支持就地换肤，且换肤后已有文字按新前景重算（不重建控件）
                bool cardsOk = CardRegistry.Get("chat") is ICardStyleAware
                            && CardRegistry.Get("command") is ICardStyleAware;

                var styleA = new StyleSettings { FontSize = 15, Foreground = "#E6EDF3", Padding = 14 };
                var entry = new CliEntryPanel(styleA, new CliSettings(), System.Windows.Media.Color.FromRgb(0xE6, 0xED, 0xF3), 10);
                TextBlock block = entry.AddText("hello", 0.72, false, 0, 0);

                var styleB = new StyleSettings { FontSize = 20, Foreground = "#FFFFFF", Padding = 18 };
                entry.ApplyStyle(styleB, System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF), 14);

                var color = ((SolidColorBrush)block.Foreground).Color;
                bool entryOk = block.FontSize > 18
                            && color.R == 0xFF
                            && color.A == (byte)Math.Round(0.72 * 255);

                restyleOk = cardsOk && entryOk;
            }
            catch (Exception ex)
            {
                Log.Error("CLI 换肤自检异常", ex);
            }

            try
            {
                // P3：导出 → 解析 → 套用 → 再导出，字段与外观必须一致（含软色的数字写法）。
                var source = new Settings();
                source.Style = new StyleSettings
                {
                    FontFamily = "Segoe UI",
                    FontSize = 17,
                    Bold = true,
                    Foreground = "#101820",
                    Background = "#FAFAF7",
                    BackgroundOpacity = 0.9,
                    Padding = 12,
                    LineHeight = 4,
                    TextAlignment = "Center",
                    TextOutline = true,
                    OutlineColor = "#FFFFFF",
                    OutlineThickness = 1.5,
                    IconScale = 1.3,
                    Accent = "#4C8DFF",
                    Danger = "#E57B6E",
                    Muted = "#73E6EDF3",
                    Border = "#1A101820",
                    Panel = "#0B101820",
                    Scrollbar = "#2E101820",
                    ScrollbarHot = "#66101820",
                    MonoFont = "Consolas"
                };
                source.Overlay = new OverlaySettings { CornerRadius = 12, Shadow = false };
                source.Cli = new CliSettings { MaxWidth = 500, MaxHeight = 440, EntryMaxHeight = 150, MaxEntries = 8 };

                string json = ThemeFile.Export(source, "往返测试");

                ThemeDocument parsed;
                string error;
                bool parseOk = ThemeFile.TryParse(json, out parsed, out error)
                            && parsed != null
                            && parsed.Name == "往返测试"
                            && parsed.Schema == ThemeFile.SupportedSchema;

                var applied = new Settings();
                if (parsed != null) parsed.ApplyTo(applied);
                applied.Normalize();

                bool same = applied.Style.FontFamily == source.Style.FontFamily
                         && applied.Style.FontSize == source.Style.FontSize
                         && applied.Style.Bold == source.Style.Bold
                         && applied.Style.Foreground == source.Style.Foreground
                         && applied.Style.Background == source.Style.Background
                         && Math.Abs(applied.Style.BackgroundOpacity - source.Style.BackgroundOpacity) < 1e-9
                         && applied.Style.Padding == source.Style.Padding
                         && applied.Style.LineHeight == source.Style.LineHeight
                         && applied.Style.TextAlignment == source.Style.TextAlignment
                         && applied.Style.TextOutline == source.Style.TextOutline
                         && applied.Style.OutlineColor == source.Style.OutlineColor
                         && Math.Abs(applied.Style.OutlineThickness - source.Style.OutlineThickness) < 1e-9
                         && Math.Abs(applied.Style.IconScale - source.Style.IconScale) < 1e-9
                         && applied.Style.Accent == source.Style.Accent
                         && applied.Style.Danger == source.Style.Danger
                         && applied.Style.Muted == source.Style.Muted
                         && applied.Style.Border == source.Style.Border
                         && applied.Style.Panel == source.Style.Panel
                         && applied.Style.Scrollbar == source.Style.Scrollbar
                         && applied.Style.ScrollbarHot == source.Style.ScrollbarHot
                         && applied.Style.MonoFont == source.Style.MonoFont
                         && applied.Overlay.CornerRadius == source.Overlay.CornerRadius
                         && applied.Overlay.Shadow == source.Overlay.Shadow
                         && applied.Cli.MaxWidth == source.Cli.MaxWidth
                         && applied.Cli.MaxHeight == source.Cli.MaxHeight
                         && applied.Cli.EntryMaxHeight == source.Cli.EntryMaxHeight
                         && applied.Cli.MaxEntries == source.Cli.MaxEntries;

                // 软色写数字 = 按前景派生：0.45 * 255 ≈ 0x73
                const string softJson = "{\"schema\":1,\"appearance\":{\"colors\":{\"foreground\":\"#E6EDF3\",\"muted\":0.45}}}";
                ThemeDocument soft;
                bool softParsed = ThemeFile.TryParse(softJson, out soft, out error);
                var softApplied = new Settings();
                if (soft != null) soft.ApplyTo(softApplied);
                bool alphaOk = softParsed && softApplied.Style.Muted == "#73E6EDF3";

                bool stable = ThemeFile.Export(applied, "往返测试") == json;

                // 仓库里的示例主题（docs/themes/*.mptheme）必须都能解析并套用；发布版没有 docs 目录时跳过。
                bool samplesOk = true;
                DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    string themes = Path.Combine(dir.FullName, "docs", "themes");
                    if (Directory.Exists(themes))
                    {
                        string[] files = Directory.GetFiles(themes, "*.mptheme");
                        samplesOk = files.Length > 0;
                        foreach (string file in files)
                        {
                            ThemeDocument sample;
                            string sampleError;
                            if (!ThemeFile.TryParse(File.ReadAllText(file), out sample, out sampleError))
                            {
                                samplesOk = false;
                                Log.Error("示例主题解析失败 " + Path.GetFileName(file) + ": " + sampleError);
                                continue;
                            }

                            var probe = new Settings();
                            sample.ApplyTo(probe);
                            if (probe.Style.Foreground != sample.Colors.Foreground) samplesOk = false;
                        }
                        break;
                    }
                    dir = dir.Parent;
                }

                themeFileOk = parseOk && same && alphaOk && stable && samplesOk;
            }
            catch (Exception ex)
            {
                Log.Error("主题文件自检异常", ex);
            }

            try
            {
                string error;
                ThemeDocument doc;
                bool future = !ThemeFile.TryParse("{\"schema\":2,\"appearance\":{}}", out doc, out error)
                           && error != null && error.Contains("版本");
                bool badColor = !ThemeFile.TryParse("{\"schema\":1,\"appearance\":{\"colors\":{\"accent\":\"nope\"}}}", out doc, out error);
                bool badRange = !ThemeFile.TryParse("{\"schema\":1,\"appearance\":{\"typography\":{\"fontSize\":200}}}", out doc, out error);
                bool noAppearance = !ThemeFile.TryParse("{\"schema\":1}", out doc, out error);
                bool malformed = !ThemeFile.TryParse("{oops", out doc, out error);

                // 坏文件不碰当前设置；好文件导入成功且源对象保持不变。
                var current = new Settings();
                current.Style.Accent = "#123456";
                string temp = Path.Combine(Path.GetTempPath(), "mph-theme-selftest-" + Guid.NewGuid().ToString("N") + ".mptheme");
                File.WriteAllText(temp, "{\"schema\":9,\"appearance\":{}}");
                Settings rejected;
                bool rejectedOk = !ThemeFile.TryImport(temp, current, out rejected, out error) && rejected == null;

                File.WriteAllText(temp, ThemeFile.Export(new Settings(), "临时"));
                Settings imported;
                bool importedOk = ThemeFile.TryImport(temp, current, out imported, out error)
                               && imported != null
                               && imported.Style.Accent == new Settings().Style.Accent
                               && current.Style.Accent == "#123456";

                try { File.Delete(temp); } catch { }

                themeRejectOk = future && badColor && badRange && noAppearance && malformed && rejectedOk && importedOk;
            }
            catch (Exception ex)
            {
                Log.Error("主题文件校验自检异常", ex);
            }

            try
            {
                // P4：三份示例主题以 EmbeddedResource 编进程序集，发布版不再依赖 docs 目录。
                var catalog = ThemeCatalog.Load();
                themeCatalogOk = catalog.Count >= 3
                              && catalog.TrueForAll(p => p.Document != null && !string.IsNullOrWhiteSpace(p.Name));
                if (!themeCatalogOk)
                    Log.Warn("内置主题数量不足或解析失败: " + catalog.Count);
            }
            catch (Exception ex)
            {
                Log.Error("内置主题自检异常", ex);
            }

            try
            {
                // P1-10 / #4：交互卡片通过 ICardActivation 自己解释设置，宿主不再认识 CommandSettings。
                var activation = CardRegistry.Get("command") as ICardActivation;
                var config = new CardSettings
                {
                    Id = "selftest-command", Kind = "command", Name = "命令行",
                    Extras = new Dictionary<string, object>()
                };
                string summary = activation == null ? null : activation.OnActivated(config);
                activationOk = activation != null
                            && !string.IsNullOrEmpty(summary)
                            && summary.Contains("工具=") && summary.Contains("目录=");
            }
            catch (Exception ex)
            {
                Log.Error("卡片激活自检异常", ex);
            }

            try
            {
                // #7：克隆失败必须返回 false + null，绝不能拿 new Settings() 顶替（那会写默认值回盘）。
                Settings badClone;
                string badError;
                bool guarded = !SettingsWindow.TryClone(null, out badClone, out badError) && badClone == null;

                Settings goodClone;
                string goodError;
                bool works = SettingsWindow.TryClone(new Settings(), out goodClone, out goodError)
                          && goodClone != null;

                cloneGuardOk = guarded && works;
            }
            catch (Exception ex)
            {
                Log.Error("设置副本自检异常", ex);
            }

            try
            {
                // 回归：保存设置后宿主会换成新的 Settings 对象。ApplyStyle 必须整份跟随，
                // 否则 CLI 卡片的圆角 / 宽度会一直读构造期那份旧配置（媒体卡同理，底层由 RefreshSurface 重画）。
                var before = new Settings
                {
                    Style = new StyleSettings { FontSize = 15, Padding = 14, Foreground = "#E6EDF3" },
                    Overlay = new OverlaySettings { CornerRadius = 10 },
                    Cli = new CliSettings { MaxWidth = 460 }
                };
                var after = new Settings
                {
                    Style = new StyleSettings
                    {
                        FontSize = 20, Padding = 18, Foreground = "#FF0000",
                        MonoFont = "Consolas, Microsoft YaHei UI"
                    },
                    Overlay = new OverlaySettings { CornerRadius = 24 },
                    Cli = new CliSettings { MaxWidth = 540 }
                };

                var view = new CommandView(before, new CommandSettings());
                view.ApplyStyle(after);

                bool followsHost = ReferenceEquals(view.BoundSettings, after)
                                && Math.Abs(view.BoundCorner - 24) < 0.01
                                && Math.Abs(view.Width - 540) < 0.01
                                && view.BoundMono.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0;

                // 富内容卡片的底色 / 圆角由 OverlaySurface 画：重画后文字与圆角都要跟着新设置。
                var surface = new OverlaySurface();
                surface.Apply(before, "hello");
                surface.Apply(after, surface.Text);
                bool surfaceOk = surface.Text == "hello"
                              && Math.Abs(surface.CornerRadius.TopLeft - 24) < 0.01;

                restyleHostOk = followsHost && surfaceOk;
            }
            catch (Exception ex)
            {
                Log.Error("换肤跟随设置自检异常", ex);
            }

            try
            {
                // 对话上下文跟随「CLI 交互面板 → 最多保留」：每轮两条，至少两条。
                chatContextOk = ChatView.ContextMessageLimit(0) == 2
                             && ChatView.ContextMessageLimit(1) == 2
                             && ChatView.ContextMessageLimit(3) == 6
                             && ChatView.ContextMessageLimit(30) == 60;
            }
            catch (Exception ex)
            {
                Log.Error("对话上下文自检异常", ex);
            }

            try
            {
                // 宿主只认第一张交互卡：默认配置正好一张；再加一张命令行应被数出来（设置页会拦住）。
                var cards = CardModel.CreateDefaultCards();
                bool one = CardModel.CountInteractive(cards) == 1;

                cards.Add(new CardSettings { Id = "selftest-second-command", Kind = "command", Name = "第二张命令行" });
                bool two = CardModel.CountInteractive(cards) == 2;

                singleInteractiveOk = one && two;
            }
            catch (Exception ex)
            {
                Log.Error("交互卡唯一性自检异常", ex);
            }

            try
            {
                // 2.10.0 共享控件：列表的空状态 / 计数 / 选中，状态行的忙碌 / 进度 / 错误态。
                var style = new StyleSettings { FontSize = 15, Foreground = "#E6EDF3" };
                var cli = new CliSettings();

                var list = new CardList();
                list.ApplyStyle(style, cli);
                bool emptyOk = list.IsEmpty;

                list.Items.Add("a");
                list.Items.Add("b");
                bool countOk = !list.IsEmpty && list.Items.Count == 2;

                list.SelectedIndex = 1;
                bool selectOk = list.SelectedIndex == 1;

                var status = new CardStatus();
                status.ApplyStyle(style);
                status.SetText("正在复制 2/5", CardStatusKind.Busy);
                status.SetProgress(0.4);
                bool busyOk = status.Kind == CardStatusKind.Busy;

                status.SetBusy(false);
                status.SetText("复制失败：磁盘已满", CardStatusKind.Error);
                status.SetProgress(null);
                bool errorOk = status.Kind == CardStatusKind.Error;

                widgetsOk = emptyOk && countOk && selectOk && busyOk && errorOk;
            }
            catch (Exception ex)
            {
                Log.Error("共享控件自检异常", ex);
            }

            try
            {
                // 2.10.1 共享控件：Header/Toolbar 的按钮可点，Input 回车提交，DropOverlay 显隐。
                var style = new StyleSettings { FontSize = 15, Foreground = "#E6EDF3" };

                var header = new CardHeader("文件中转站") { Badge = "12 个文件" };
                bool clicked = false;
                CardButton button = header.Actions.Add("清空", () => clicked = true);
                header.ApplyStyle(style, 10);
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                var input = new CardInput(false);
                input.ApplyStyle(style, 10);
                string submitted = null;
                input.Submitted += text => submitted = text;
                input.Text = "hello";
                input.Submit();

                var overlay = new CardDropOverlay();
                overlay.ApplyStyle(style, 10);
                overlay.Show(3);
                bool shown = overlay.Visibility == Visibility.Visible;
                overlay.Hide();

                widgets2Ok = header.Title == "文件中转站"
                          && header.Badge == "12 个文件"
                          && clicked
                          && submitted == "hello"
                          && shown
                          && overlay.Visibility == Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error("共享控件（交互）自检异常", ex);
            }

            try
            {
                // 2.10.2：富内容的渲染上下文要把这张卡片的 ICardHost 透传给内容控件。
                var host = new TestCardHost();
                var context = new CardRenderContext(
                    new Settings(), new MetricsSnapshot(), null,
                    new CardSettings { Id = "host-test", Kind = "text" }, null, host);

                contextHostOk = ReferenceEquals(context.Host, host);
            }
            catch (Exception ex)
            {
                Log.Error("渲染上下文 Host 自检异常", ex);
            }

            try
            {
                // 2.10.2：串行任务队列——成功 / 失败 / 取消三种结局都要落到正确的状态。
                using (var queue = new CardJobQueue(1))
                {
                    var okJob = queue.Enqueue("ok", (ct, progress) =>
                    {
                        progress.Report(0.5);
                        progress.Report(1.0);
                        return Task.CompletedTask;
                    });

                    var failJob = queue.Enqueue("fail", (ct, progress) =>
                    {
                        throw new InvalidOperationException("boom");
                    });

                    var cancelJob = queue.Enqueue("cancel", (ct, progress) => Task.Delay(500, ct));
                    cancelJob.Cancel();

                    bool finished = Task.WaitAll(
                        new[] { okJob.Task, failJob.Task, cancelJob.Task }, 3000);

                    jobQueueOk = finished
                              && okJob.State == CardJobState.Succeeded
                              && Math.Abs(okJob.Progress - 1.0) < 0.001
                              && failJob.State == CardJobState.Failed
                              && failJob.Error == "boom"
                              && cancelJob.State == CardJobState.Canceled;
                }
            }
            catch (Exception ex)
            {
                Log.Error("后台任务队列自检异常", ex);
            }

            if (!(templateOk && unknownOk && collapseOk && thresholdOk && configOk && cardOk &&
                  keyNameOk && keyLimitOk && keyOverflowOk &&
                  commandOk && truncateOk && toolLabelOk && secretOk && chatHotkeyOk && settingsEditorOk &&
                  stateStoreOk && renderOk && glassPolicyOk && themeOk && restyleOk &&
                  themeFileOk && themeRejectOk && themeCatalogOk && activationOk && cloneGuardOk &&
                  restyleHostOk && chatContextOk && singleInteractiveOk && widgetsOk && widgets2Ok &&
                  contextHostOk && jobQueueOk)) failures++;

            Fix(sb, "逻辑自检", string.Format(
                "模板={0} 未知占位符={1} 折叠={2} 阈值内展开={3} 配置读写={4} 卡片模块={11} 键名={5} 按键长度={6} 超长截断={7} 命令行={8} 行截断={9} 工具名={10} Key加密={12} 对话热键={13} 卡片设置={14} 状态存储={15} 渲染节流={16} 毛玻璃策略={17}",
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
                chatHotkeyOk ? "OK" : "FAIL",
                settingsEditorOk ? "OK" : "FAIL",
                stateStoreOk ? "OK" : "FAIL",
                renderOk ? "OK" : "FAIL",
                glassPolicyOk ? "OK" : "FAIL"));

            Fix(sb, "外观令牌", themeOk ? "OK（派生值 = 旧 alpha 常量，非法值回落而不是变白）" : "FAIL");
            Fix(sb, "CLI 换肤", restyleOk ? "OK（两张 CLI 卡片支持就地换肤，文字按新前景重算）" : "FAIL");
            Fix(sb, "主题文件", themeFileOk ? "OK（导出 → 解析 → 套用 → 再导出一致，数字透明度按前景派生）" : "FAIL");
            Fix(sb, "主题文件校验", themeRejectOk ? "OK（高版本 / 坏颜色 / 越界 / 缺 appearance 均拒绝，坏文件不动当前设置）" : "FAIL");
            Fix(sb, "内置主题", themeCatalogOk ? "OK（已编进程序集并全部解析）" : "FAIL");
            Fix(sb, "卡片激活", activationOk ? "OK（CommandCard 实现 ICardActivation，返回工具 / 目录摘要）" : "FAIL");
            Fix(sb, "设置副本", cloneGuardOk ? "OK（克隆失败返回 null + 原因，不退回默认值）" : "FAIL");
            Fix(sb, "换肤跟随设置", restyleHostOk ? "OK（CLI 换肤跟随新 Settings 对象，底层圆角随重画更新）" : "FAIL");
            Fix(sb, "对话上下文", chatContextOk ? "OK（每条记录两条消息，跟随 CLI 最多保留条数）" : "FAIL");
            Fix(sb, "交互卡唯一", singleInteractiveOk ? "OK（默认一张；第二张会被设置页 / 校验拦住）" : "FAIL");
            Fix(sb, "共享控件", widgetsOk ? "OK（CardList 空态/计数/选中；CardStatus 忙碌/进度/错误）" : "FAIL");
            Fix(sb, "共享控件（交互）", widgets2Ok ? "OK（Header 按钮可点；Input 提交；DropOverlay 显隐）" : "FAIL");
            Fix(sb, "渲染上下文", contextHostOk ? "OK（富内容创建上下文透传 ICardHost）" : "FAIL");
            Fix(sb, "后台任务", jobQueueOk ? "OK（串行队列：成功 / 失败 / 取消）" : "FAIL");
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

        /// <summary>自检用的假宿主：只验证透传，不读写磁盘。</summary>
        sealed class TestCardHost : ICardHost
        {
            public CardSettings Config { get; set; }
            public string StateDirectory { get { return ""; } }
            public string ReadState(string fileName) { return null; }
            public bool WriteState(string fileName, string text) { return true; }
            public bool DeleteState(string fileName) { return true; }
            public void RequestRender() { }
            public void RequestLayout() { }
        }
    }
}
