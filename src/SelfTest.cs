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
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
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
            bool configRepairOk = false;
            bool cardOk = false;
            bool keyNameOk = false;
            bool keyLimitOk = false;
            bool keyOverflowOk = false;
            bool secretOk = false;
            bool stateStoreOk = false;
            bool renderOk = false;
            bool themeOk = false;
            bool glassPolicyOk = false;
            bool themeFileOk = false;
            bool themeRejectOk = false;
            bool themeCatalogOk = false;
            bool cloneGuardOk = false;
            bool singleInteractiveOk = false;
            bool widgetsOk = false;
            bool widgets2Ok = false;
            bool formControlsOk = false;
            bool dataControlsOk = false;
            bool chartControlsOk = false;
            bool overlayControlsOk = false;
            bool contextHostOk = false;
            bool shellOk = false;
            bool pluginSelfTestOk = false;
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

                // P2-7：超过 1 MB 的主题文件必须明确拒绝，不能整读。
                string big = temp + ".big";
                File.WriteAllBytes(big, new byte[ThemeFile.MaxImportBytes + 1]);
                Settings bigImported;
                bool bigOk = !ThemeFile.TryImport(big, current, out bigImported, out error)
                          && bigImported == null
                          && error != null && error.Contains("过大");

                try { File.Delete(temp); } catch { }
                try { File.Delete(big); } catch { }

                themeRejectOk = future && badColor && badRange && noAppearance && malformed
                             && rejectedOk && importedOk && bigOk;
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

                // 2.11.1：按钮文字必须真的用按钮自己的前景色（幽灵描边样式，内容不再靠模板继承）。
                var label = button.Content as TextBlock;
                var labelForeground = label == null ? null : label.Foreground as SolidColorBrush;
                var buttonForeground = button.Foreground as SolidColorBrush;
                bool buttonTextOk = labelForeground != null && buttonForeground != null
                                    && labelForeground.Color == buttonForeground.Color;

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
                          && buttonTextOk
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
                // UiKit 表单控件：滑块 / 开关 / 下拉 / 数字 / 颜色 / 分段 + 声明式表单。
                var style = new StyleSettings { FontSize = 14, Foreground = "#E6EDF3" };
                var form = new CardSettingsForm();
                var slider = form.AddSlider("音量", 0, 100, 42);
                var toggle = form.AddToggle("启用", true);
                var select = form.AddSelect("模式", new[] { "A", "B" }, "B");
                var number = form.AddNumber("数量", 7, 1, 10);
                var color = form.AddColor("颜色", "#FF2F6FEB");
                var segment = form.AddSegmented("布局", "横向", "纵向");
                form.AddDescription("说明文字");
                form.AddDivider();
                form.ApplyStyle(style, 10);

                formControlsOk = Math.Abs(slider.Value - 42) < 0.01
                               && toggle.IsChecked == true
                               && select.SelectedItem == "B"
                               && Math.Abs(number.Value - 7) < 0.01
                               && color.Value == "#FF2F6FEB"
                               && segment.SelectedIndex == 0
                               && form.Children.Count >= 8;
            }
            catch (Exception ex)
            {
                Log.Error("表单控件自检异常", ex);
            }


            try
            {
                string dataError = CardDataSelfTest.Run();
                dataControlsOk = string.IsNullOrEmpty(dataError);
                if (!dataControlsOk) Log.Warn("数据控件自检失败: " + dataError);
            }
            catch (Exception ex)
            {
                Log.Error("数据控件自检异常", ex);
            }


            try
            {
                string chartError = CardChartSelfTest.Run();
                chartControlsOk = string.IsNullOrEmpty(chartError);
                if (!chartControlsOk) Log.Warn("图表 / 树 / 时间线自检失败: " + chartError);
            }
            catch (Exception ex)
            {
                Log.Error("图表 / 树 / 时间线自检异常", ex);
            }


            try
            {
                string overlayError = CardOverlaySelfTest.Run();
                overlayControlsOk = string.IsNullOrEmpty(overlayError);
                if (!overlayControlsOk) Log.Warn("浮层 / 交互控件自检失败: " + overlayError);
            }
            catch (Exception ex)
            {
                Log.Error("浮层 / 交互控件自检异常", ex);
            }


            try
            {
                // 2.10.2：富内容的渲染上下文要把这张卡片的 ICardHost 透传给内容控件。
                var host = new TestCardHost();
                var context = new CardRenderContext(
                    new Settings(), new MetricsSnapshot(), null,
                    new CardSettings { Id = "host-test", Kind = "infotext" }, null, host);

                contextHostOk = ReferenceEquals(context.Host, host);
            }
            catch (Exception ex)
            {
                Log.Error("渲染上下文 Host 自检异常", ex);
            }


            try
            {
                // 2.10.3：Shell 图标缓存一致、缩略图请求不会挂住；拖出阈值与空输入。
                var icon1 = ShellIcons.GetIcon("selftest.zzz", 16);
                var icon2 = ShellIcons.GetIcon("selftest.zzz", 16);
                bool cacheOk = ReferenceEquals(icon1, icon2) && (icon1 == null || icon1.Width > 0);

                var thumbnail = ShellIcons.GetThumbnailAsync("C:\\__mirrorpond_no_such__\\x.png", 32);
                bool thumbOk = thumbnail.Wait(2000);

                bool dragOk = !DragSource.ShouldStart(new Point(0, 0), new Point(1, 1))
                           && DragSource.ShouldStart(new Point(0, 0), new Point(10, 0))
                           && DragSource.BeginFiles(null, new[] { "a" }) == DragDropEffects.None;

                shellOk = cacheOk && thumbOk && dragOk;
            }
            catch (Exception ex)
            {
                Log.Error("Shell / 拖出自检异常", ex);
            }


            try
            {
                // 配置层：默认卡片 / JSON 往返（含通用 Extras）/ 顶层 null 块 / 旧 Schema 按损坏 / 未来版本只读 / 未知 Kind 保留。
                var defaults = new Settings();
                defaults.Normalize();
                CardModel.Normalize(defaults);
                bool defaultsOk = defaults.Cards.Count == 5
                               && defaults.Cards[0].Kind == "infotext"
                               && defaults.Cards[0].Preset == "metrics"
                               && !string.IsNullOrWhiteSpace(defaults.Cards[0].Content);

                var source = new Settings();
                var card = new CardSettings
                {
                    Id = "selftest-config", Kind = "infotext", Name = "自检卡",
                    Preset = "clock", Content = "{time}", Spacing = 16
                };
                card.Extras["command"] = new Dictionary<string, object>
                {
                    { "Shell", "cmd" }, { "TimeoutSeconds", 20 }
                };
                source.Cards.Add(card);
                Settings roundTrip = Json.Deserialize<Settings>(Json.Serialize(source));
                bool roundTripOk = roundTrip != null && roundTrip.Cards != null && roundTrip.Cards.Count == 1
                                && roundTrip.Cards[0].Id == "selftest-config"
                                && roundTrip.Cards[0].Kind == "infotext"
                                && roundTrip.Cards[0].Preset == "clock"
                                && roundTrip.Cards[0].Content == "{time}"
                                && roundTrip.Cards[0].Extras.ContainsKey("command");

                Settings nullBlocks = Json.Deserialize<Settings>(
                    "{\"SchemaVersion\":6,\"Style\":null,\"Overlay\":null,\"Cli\":null," +
                    "\"Data\":null,\"Behavior\":null,\"Hotkeys\":null,\"Cards\":null}");
                nullBlocks.Normalize();
                bool nullOk = nullBlocks.Style != null && nullBlocks.Overlay != null && nullBlocks.Cli != null
                           && nullBlocks.Data != null && nullBlocks.Behavior != null
                           && nullBlocks.Hotkeys != null && nullBlocks.Cards != null;

                configOk = defaultsOk && roundTripOk && nullOk;

                bool needsSave;
                bool corrupted;
                bool futureFlag;
                Settings broken = SettingsMigration.Load("{\"SchemaVersion\":6,", out needsSave, out corrupted);
                bool brokenOk = corrupted && !needsSave && broken != null;

                Settings oldSchema = SettingsMigration.Load("{\"SchemaVersion\":1,\"Cards\":[]}", out needsSave, out corrupted);
                bool oldOk = corrupted && !needsSave && oldSchema != null;

                Settings unversioned = SettingsMigration.Load(
                    "{\"Cards\":[{\"Id\":\"c1\",\"Kind\":\"infotext\",\"Name\":\"文本\"}]}",
                    out needsSave, out corrupted);
                bool unversionedOk = !corrupted && needsSave && unversioned.Cards.Count == 1
                                  && unversioned.Cards[0].Kind == "infotext";

                Settings future = SettingsMigration.Load("{\"SchemaVersion\":999,\"Cards\":[]}",
                                                          out needsSave, out corrupted, out futureFlag);
                bool futureOk = !corrupted && futureFlag && !needsSave && future != null;

                Settings overflow = SettingsMigration.Load("{\"SchemaVersion\":1e300,\"Cards\":[]}",
                                                           out needsSave, out corrupted, out futureFlag);
                bool overflowOk = corrupted && !needsSave;

                // 未知 Kind（例如插件卸载）：保留原配置，不改写成默认卡。
                var unknownSettings = new Settings();
                unknownSettings.Cards.Add(new CardSettings
                {
                    Id = "plugin-missing", Kind = "example-clock", Name = "插件卡", Content = "插件内容",
                    Extras = new Dictionary<string, object> { { "plugin", "keep" } }
                });
                CardModel.Normalize(unknownSettings);
                bool unknownKindOk = unknownSettings.Cards.Count == 1
                                  && unknownSettings.Cards[0].Kind == "example-clock"
                                  && unknownSettings.Cards[0].Content == "插件内容"
                                  && unknownSettings.Cards[0].Extras.ContainsKey("plugin");

                // 状态存储：原子写、损坏回退 .bak、拒绝路径穿越、删除。
                string atomicId = "selftest-atomic-" + Guid.NewGuid().ToString("N");
                string atomicDir = CardStateStore.DirectoryFor(atomicId);
                bool atomicOk = false;
                try
                {
                    CardStateStore.Write(atomicId, "state.json", "{\"v\":1}");
                    CardStateStore.Write(atomicId, "state.json", "{\"v\":2}");
                    string main = Path.Combine(atomicDir, "state.json");
                    bool current = CardStateStore.Read(atomicId, "state.json") == "{\"v\":2}";
                    File.WriteAllText(main, "{bad");
                    bool recoveredFromBak = CardStateStore.Read(atomicId, "state.json") == "{\"v\":1}";
                    bool rejected = CardStateStore.Write(atomicId, "..\\escape.txt", "x") == false
                                 && CardStateStore.Read(atomicId, "..\\escape.txt") == null;
                    bool deleted = CardStateStore.Delete(atomicId, "state.json");
                    bool gone = CardStateStore.Read(atomicId, "state.json") == null;
                    atomicOk = current && recoveredFromBak && rejected && deleted && gone;
                }
                finally
                {
                    try { if (Directory.Exists(atomicDir)) Directory.Delete(atomicDir, true); } catch { }
                }

                configRepairOk = brokenOk && oldOk && unversionedOk && futureOk && overflowOk
                              && unknownKindOk && atomicOk;
            }
            catch (Exception ex)
            {
                Log.Error("配置修复自检异常", ex);
            }

            try
            {
                // 契约边界：插件 API 在 Contracts；宿主实现与配置归一化钩子仍在宿主程序集。
                bool contractsOk = string.Equals(typeof(ICardModule).Assembly.GetName().Name, "MirrorpondHUD.Contracts", StringComparison.Ordinal)
                                && typeof(CardRenderContext).Assembly == typeof(ICardModule).Assembly
                                && typeof(Settings).Assembly == typeof(ICardModule).Assembly
                                && typeof(SecretStore).Assembly == typeof(ICardModule).Assembly
                                && string.Equals(typeof(CardRegistry).Assembly.GetName().Name, "MirrorpondHUD", StringComparison.Ordinal)
                                && ConfigStore.SettingsNormalizer != null;

                // 注册表：6 个内置卡片插件必须全部注册；Kind 唯一且元数据完整。
                string[] requiredKinds = { "infotext", "command", "media", "chat", "dropstation", "buttons" };
                IList<ICardModule> modules = CardRegistry.Modules;
                var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var missing = new List<string>();
                bool metadataOk = true;
                foreach (ICardModule module in modules)
                {
                    if (module == null) { metadataOk = false; continue; }
                    string kind = (module.Kind ?? "").Trim();
                    if (kind.Length == 0 || !kinds.Add(kind)) metadataOk = false;
                    if (string.IsNullOrWhiteSpace(module.DisplayName) || string.IsNullOrWhiteSpace(module.Icon)) metadataOk = false;
                }
                foreach (string required in requiredKinds)
                    if (!kinds.Contains(required)) missing.Add(required);
                bool registryOk = modules.Count >= requiredKinds.Length && missing.Count == 0 && metadataOk;

                // 未知 Kind 必须有占位模块（保留 Kind、不可交互、给出提示）。
                ICardModule missingModule = CardRegistry.Get("selftest-missing-plugin");
                string missingText = missingModule.Render(new CardRenderContext(
                    new Settings(), Screenshots.DemoSnapshot(), null,
                    new CardSettings { Kind = "selftest-missing-plugin" }));
                bool placeholderOk = missingModule.Kind == "selftest-missing-plugin"
                                  && missingModule.DisplayName == "插件不可用"
                                  && !missingModule.IsInteractive
                                  && missingText.IndexOf("插件不可用", StringComparison.Ordinal) >= 0;

                // 宿主默认内容模板仍可用（CardDefaults 保留在宿主）。
                bool cardDefaultsOk = CardDefaults.MetricsContent.IndexOf("{cpu", StringComparison.Ordinal) >= 0
                                   && CardDefaults.ClockContent.IndexOf("{time", StringComparison.Ordinal) >= 0
                                   && CardDefaults.KeysContent.IndexOf("{keys", StringComparison.Ordinal) >= 0
                                   && CardDefaults.TextContent.IndexOf("{host", StringComparison.Ordinal) >= 0;

                // 默认卡片：3 张信息文本 + 命令行 + 媒体，且交互卡只有 1 张。
                var defaultCards = CardModel.CreateDefaultCards();
                var defaultKinds = new List<string>();
                foreach (CardSettings item in defaultCards) defaultKinds.Add(item.Kind);
                bool defaultCardsOk = defaultCards.Count == 5
                                   && defaultKinds.FindAll(k => k == "infotext").Count == 3
                                   && defaultKinds.Contains("command") && defaultKinds.Contains("media")
                                   && CardModel.CountInteractive(defaultCards) == 1;

                cardOk = contractsOk && registryOk && placeholderOk && cardDefaultsOk && defaultCardsOk;
            }
            catch (Exception ex)
            {
                Log.Error("卡片注册表自检异常", ex);
            }

            try
            {
                // 通用插件自检：宿主不认具体卡片类型，遍历实现 ICardSelfTest 的模块。
                var failuresText = new List<string>();
                int tested = 0;
                foreach (ICardModule module in CardRegistry.Modules)
                {
                    var selfTest = module as ICardSelfTest;
                    if (selfTest == null) continue;
                    tested++;
                    try
                    {
                        string result = selfTest.RunSelfTest();
                        if (!string.IsNullOrWhiteSpace(result))
                            failuresText.Add(module.Kind + ": " + result);
                    }
                    catch (Exception ex)
                    {
                        failuresText.Add(module.Kind + ": " + ex.Message);
                    }
                }
                pluginSelfTestOk = tested >= 6 && failuresText.Count == 0;
                string pluginSelfTestText = tested + " 个模块自检通过 / " + failuresText.Count + " 个失败";
                if (failuresText.Count > 0)
                    pluginSelfTestText += "（" + string.Join("；", failuresText.ToArray()) + "）";
                Fix(sb, "插件自检", pluginSelfTestText);
            }
            catch (Exception ex)
            {
                Log.Error("插件自检异常", ex);
            }

            try
            {
                // 字符串加密：DPAPI 往返；明文不落盘（SecretStore 在契约程序集，插件与宿主共用）。
                const string secret = "sk-selftest-0123456789";
                string stored = SecretStore.Protect(secret);
                string plain;
                secretOk = SecretStore.IsProtected(stored)
                        && stored.StartsWith(SecretStore.Prefix, StringComparison.Ordinal)
                        && SecretStore.TryUnprotect(stored, out plain)
                        && plain == secret
                        && !SecretStore.IsProtected(secret);
            }
            catch (Exception ex)
            {
                Log.Error("Key 加密自检异常", ex);
            }

            try
            {
                // 渲染上下文：一帧一份变量表（同一个字典实例被复用）+ 富内容宿主透传 + 模块刷新节奏。
                var shared = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { { "cpu", 23.44 } };
                var config = new CardSettings { Id = "selftest-context", Kind = "infotext", Preset = "metrics" };
                var host = new TestCardHost { Config = config };
                var context = new CardRenderContext(new Settings(), Screenshots.DemoSnapshot(), null, config, shared, host);
                bool sharedUsed = ReferenceEquals(context.Variables, shared)
                               && context.Render("CPU {cpu:0.0}%") == "CPU 23.4%";
                bool hostPassed = ReferenceEquals(context.Host, host);
                bool cadenceOk = true;
                foreach (ICardModule module in CardRegistry.Modules)
                {
                    if (module == null) { cadenceOk = false; continue; }
                    if (module.RefreshIntervalMs(new CardSettings { Kind = module.Kind }) < 0) cadenceOk = false;
                }
                renderOk = sharedUsed && hostPassed && cadenceOk;
            }
            catch (Exception ex)
            {
                Log.Error("渲染上下文自检异常", ex);
            }


            if (!(templateOk && unknownOk && collapseOk && thresholdOk && configOk && configRepairOk && cardOk &&
                  keyNameOk && keyLimitOk && keyOverflowOk && stateStoreOk && renderOk &&
                  glassPolicyOk && themeOk && themeFileOk && themeRejectOk && themeCatalogOk && cloneGuardOk &&
                  singleInteractiveOk && widgetsOk && widgets2Ok && formControlsOk && dataControlsOk &&
                  chartControlsOk && overlayControlsOk && contextHostOk && shellOk && secretOk && pluginSelfTestOk))
                failures++;

            Fix(sb, "逻辑自检", string.Format(
                "模板={0} 未知占位符={1} 折叠={2} 阈值内展开={3} 配置读写={4} 卡片模块={5} 键名={6} 按键长度={7} 超长截断={8} 状态存储={9} 渲染节流={10} 毛玻璃策略={11}",
                templateOk ? "OK" : "FAIL",
                unknownOk ? "OK" : "FAIL",
                collapseOk ? "OK" : "FAIL",
                thresholdOk ? "OK" : "FAIL",
                configOk ? "OK" : "FAIL",
                cardOk ? "OK" : "FAIL",
                keyNameOk ? "OK" : "FAIL",
                keyLimitOk ? "OK" : "FAIL",
                keyOverflowOk ? "OK" : "FAIL",
                stateStoreOk ? "OK" : "FAIL",
                renderOk ? "OK" : "FAIL",
                glassPolicyOk ? "OK" : "FAIL"));

            Fix(sb, "外观令牌", themeOk ? "OK（派生值 = 旧 alpha 常量，非法值回落而不是变白）" : "FAIL");
            Fix(sb, "配置修复", configRepairOk
                ? "OK（null 判空 / 损坏标记 / 旧 Schema 按损坏 / 未来版本只读 / 未知 Kind 保留 / 状态 .bak 回退）" : "FAIL");
            Fix(sb, "主题文件", themeFileOk ? "OK（导出 → 解析 → 套用 → 再导出一致，数字透明度按前景派生）" : "FAIL");
            Fix(sb, "主题文件校验", themeRejectOk ? "OK（高版本 / 坏颜色 / 越界 / 缺 appearance 均拒绝，坏文件不动当前设置）" : "FAIL");
            Fix(sb, "内置主题", themeCatalogOk ? "OK（已编进程序集并全部解析）" : "FAIL");
            Fix(sb, "设置副本", cloneGuardOk ? "OK（克隆失败返回 null + 原因，不退回默认值）" : "FAIL");
            Fix(sb, "交互卡唯一", singleInteractiveOk ? "OK（默认一张；第二张会被设置页 / 校验拦住）" : "FAIL");
            Fix(sb, "共享控件", widgetsOk ? "OK（CardList 空态/计数/选中；CardStatus 忙碌/进度/错误）" : "FAIL");
            Fix(sb, "共享控件（交互）", widgets2Ok ? "OK（Header 按钮可点；Input 提交；DropOverlay 显隐）" : "FAIL");
            Fix(sb, "表单控件", formControlsOk ? "OK（Slider / Toggle / Select / Number / Color / Segmented / Form）" : "FAIL");
            Fix(sb, "数据控件", dataControlsOk ? "OK（Table 排序 / 选中 / 空态；Badge / Tag / Metric / Gauge / Ring）" : "FAIL");
            Fix(sb, "图表 / 树 / 时间线", chartControlsOk ? "OK（Sparkline / Line / Bar / Tree / Timeline 离屏渲染）" : "FAIL");
            Fix(sb, "浮层 / 交互控件", overlayControlsOk ? "OK（Modal / Dialog / Popover / Tooltip / ContextMenu / Toast）" : "FAIL");
            Fix(sb, "渲染上下文", contextHostOk ? "OK（富内容创建上下文透传 ICardHost）" : "FAIL");
            Fix(sb, "Key 加密", secretOk ? "OK（DPAPI 往返；明文不落盘）" : "FAIL");
            Fix(sb, "卡片注册", cardOk ? "OK（6 个内置 Kind 齐全；契约在 Contracts；未知 Kind 占位）" : "FAIL");
            Fix(sb, "Shell / 拖出", shellOk ? "OK（图标缓存一致；空输入安全）" : "FAIL");
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

        /// <summary>自检里跑一次 WPF 消息泵，等待后台任务回到 UI 线程（最多 timeoutMs）。</summary>
        static bool PumpUntil(Func<bool> condition, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(10);
            }
            return condition();
        }

        /// <summary>自检用的假宿主：状态放内存，不读写磁盘。</summary>
        sealed class TestCardHost : ICardHost
        {
            readonly Dictionary<string, string> _state = new Dictionary<string, string>();

            public CardSettings Config { get; set; }
            public string StateDirectory { get { return ""; } }

            public string LastWrittenState { get; private set; }

            public string ReadState(string fileName)
            {
                string value;
                return _state.TryGetValue(fileName ?? "", out value) ? value : null;
            }

            public bool WriteState(string fileName, string text)
            {
                LastWrittenState = text;
                _state[fileName ?? ""] = text ?? "";
                return true;
            }

            public bool DeleteState(string fileName)
            {
                return _state.Remove(fileName ?? "");
            }

            public void RequestRender() { }
            public void RequestLayout() { }
            public bool SaveSettings() { return true; }
            public ICardShellService Shell { get { return null; } }
        }
    }
}
