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
using MirrorpondHUD.Ui.Contour;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD;

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
            while (last is null && DateTime.UtcNow < deadline) Thread.Sleep(100);

            engine.Dispose();

            if (!started || last is null)
            {
                failures++;
                Fix(sb, "采样", "失败");
            }
            else
            {
                Fix(sb, "CPU", $"{F(last.CpuPercent)}%");
                Fix(sb, "GPU", $"{(last.GpuAvailable ? F(last.GpuPercent) : "不可用")}%");
                Fix(sb, "内存", $"{F(last.MemUsedGb)}/{F(last.MemTotalGb)} GB ({F(last.MemPercent)}%)");
                Fix(sb, "磁盘", $"{F(last.DiskPercent)}%  读 {Variables.FormatRate(last.DiskReadBps)}  写 {Variables.FormatRate(last.DiskWriteBps)}");
                Fix(sb, "网络", $"↓{Variables.FormatRate(last.NetDownBps)} ↑{Variables.FormatRate(last.NetUpBps)}  ({last.NetAdapter})");

                var drives = new StringBuilder();
                foreach (DriveSample d in last.Drives)
                    drives.Append(d.Name).Append(' ').Append(F(d.FreeGb)).Append('/').Append(F(d.TotalGb)).Append("GB  ");
                Fix(sb, "分区", drives.Length == 0 ? "无" : drives.ToString().Trim());

                NvidiaSample nv = last.Nvidia;
                Fix(sb, "NVIDIA", nv.Available
                    ? $"{nv.Name}  温度 {F(nv.TempC)}℃  显存 {F(nv.VramUsedMb)}/{F(nv.VramTotalMb)}MB  功耗 {F(nv.PowerW)}W"
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
                    ? $"正常（回环捕获到 {history.Render()}）"
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
                    $"{Show(settings.Hotkeys.ToggleOverlay, a)}  {Show(commandHotkey, d)}  {Show(settings.Hotkeys.OpenSettings, b)}  {Show(settings.Hotkeys.ToggleLock, c)}");
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
        sb.AppendLine($"失败项: {failures}");
        Log.Info("=== 自检结果 ===\n" + sb);

        return failures;
    }

    /// <summary>不依赖系统的纯逻辑回归检查：只按顺序调度各领域自检，断言与文案都在下面的方法里。</summary>
    static void RunUnitChecks(StringBuilder sb, ref int failures)
    {
        var results = new UnitCheckResults();

        results.TemplateOk = RunTemplateChecks(out results.UnknownOk);
        results.CollapseOk = RunKeyCollapseChecks(out results.ThresholdOk);
        results.KeyNameOk = RunKeyNameChecks(out results.KeyLimitOk, out results.KeyOverflowOk);
        results.StateStoreOk = RunCardStateStoreChecks();
        results.GlassPolicyOk = RunGlassPolicyChecks();
        results.ThemeOk = RunThemeTokenChecks();
        results.ThemeFileOk = RunThemeFileChecks();
        results.ThemeRejectOk = RunThemeRejectChecks();
        results.ThemeCatalogOk = RunThemeCatalogChecks();
        results.CloneGuardOk = RunCloneGuardChecks();
        results.SingleInteractiveOk = RunSingleInteractiveChecks();
        results.WidgetsOk = RunSharedWidgetChecks();
        results.Widgets2Ok = RunInteractiveWidgetChecks();
        results.FormControlsOk = RunFormControlsChecks();
        results.DataControlsOk = RunDataControlsChecks();
        results.ChartControlsOk = RunChartControlsChecks();
        results.OverlayControlsOk = RunOverlayControlsChecks();
        results.IconFontOk = RunIconFontChecks();
        results.ContextHostOk = RunContextHostChecks();
        results.ShellOk = RunShellChecks();
        results.ConfigOk = RunConfigChecks(out results.ConfigRepairOk);
        results.CardOk = RunCardRegistryChecks();
        results.PluginSelfTestOk = RunPluginSelfTestChecks(sb);
        results.SecretOk = RunSecretStoreChecks();
        results.RenderOk = RunRenderContextChecks();

        if (!results.AllPassed())
            failures++;

        ReportUnitChecks(sb, results);
    }

    /// <summary>模板渲染：占位符格式化、双花括号转义与未知占位符收集。</summary>
    static bool RunTemplateChecks(out bool unknownOk)
    {
        bool templateOk = false;
        unknownOk = false;
        try
        {
            var vars = new Dictionary<string, object> { { "cpu", 23.44 }, { "n", 5 } };
            string text = TemplateEngine.Render("CPU {cpu:0.0}% {{ok}} {n,3} {nope}", vars, out var unknown);

            templateOk = text == "CPU 23.4% {ok}   5 {nope}";
            unknownOk = unknown.Count == 1 && unknown[0] == "nope";
        }
        catch (Exception ex)
        {
            Log.Error("模板自检异常", ex);
        }
        return templateOk;
    }

    /// <summary>按键历史：连续按键折叠，以及超过阈值时展开。</summary>
    static bool RunKeyCollapseChecks(out bool thresholdOk)
    {
        bool collapseOk = false;
        thresholdOk = false;
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
        return collapseOk;
    }

    /// <summary>按键名映射与最大字符数：超长时丢弃最旧的按键。</summary>
    static bool RunKeyNameChecks(out bool keyLimitOk, out bool keyOverflowOk)
    {
        bool keyNameOk = false;
        keyLimitOk = false;
        keyOverflowOk = false;
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
        return keyNameOk;
    }

    /// <summary>每卡状态存储（P1-12）：原子写、回读一致、拒绝路径穿越、不留 .tmp。</summary>
    static bool RunCardStateStoreChecks()
    {
        bool stateStoreOk = false;
        try
        {
            // 每卡状态存储（P1-12）：原子写、回读一致、拒绝路径穿越、不留 .tmp
            string id = $"selftest-{Guid.NewGuid():N}";
            string dir = CardStateStore.DirectoryFor(id);

            bool wrote = CardStateStore.Write(id, "state.json", "{\"n\":1}");
            bool read = CardStateStore.Read(id, "state.json") == "{\"n\":1}";
            bool noTmp = dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "state.json.tmp"));

            bool rejected = CardStateStore.Write(id, "..\\escape.txt", "x") == false
                         && CardStateStore.Write(id, "../escape.txt", "x") == false
                         && CardStateStore.Read(id, "..\\escape.txt") is null
                         && CardStateStore.Write("..\\bad", "a.json", "x") == false;

            bool deleted = CardStateStore.Delete(id, "state.json");
            bool gone = CardStateStore.Read(id, "state.json") is null;

            stateStoreOk = wrote && read && noTmp && rejected && deleted && gone
                        && !string.IsNullOrEmpty(dir);
        }
        catch (Exception ex)
        {
            Log.Error("卡片状态存储自检异常", ex);
        }
        return stateStoreOk;
    }

    /// <summary>毛玻璃实现选择（P2-1）：设置与两块底板可用性 → 实现的纯决策表。</summary>
    static bool RunGlassPolicyChecks()
    {
        bool glassPolicyOk = false;
        try
        {
            // P2-1：毛玻璃实现选择是纯决策——设置 + 两块底板可用性 → 实现，窗口只按结果接线。
            // 2.24.0 起桌面模式名为 DesktopFeed（旧 Dwm 由 Schema 7 迁移归一）。
            glassPolicyOk =
                GlassPolicy.Resolve(false, "DesktopFeed", true, true) == GlassBackdropKind.None
             && GlassPolicy.Resolve(true, "DesktopFeed", true, true) == GlassBackdropKind.DesktopAcrylic
             && GlassPolicy.Resolve(true, "DesktopFeed", true, false) == GlassBackdropKind.SystemAcrylic
             && GlassPolicy.Resolve(true, "DesktopFeed", false, false) == GlassBackdropKind.CompatCapture
             && GlassPolicy.Resolve(true, "System", true, true) == GlassBackdropKind.SystemAcrylic
             && GlassPolicy.Resolve(true, "System", false, true) == GlassBackdropKind.CompatCapture
             && GlassPolicy.Resolve(true, "Compat", true, true) == GlassBackdropKind.CompatCapture;
        }
        catch (Exception ex)
        {
            Log.Error("毛玻璃策略自检异常", ex);
        }
        return glassPolicyOk;
    }

    /// <summary>外观令牌与颜色校验（P1）：默认派生值、严格 7 位前景、非法值回落。</summary>
    static bool RunThemeTokenChecks()
    {
        bool themeOk = false;
        try
        {
            // 外观令牌（P1）：默认派生值必须等于旧 alpha 常量，非法值要被归一化掉。
            ThemeTokens defaults = ThemeResolver.Resolve(new StyleSettings());
            bool defaultOk = defaults.Accent == System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xEB)
                          && defaults.Danger == System.Windows.Media.Color.FromRgb(0xD1, 0x43, 0x43);

            System.Windows.Media.Color fg = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
            bool mutedOk = defaults.Muted.A == (byte)Math.Round(0.45 * 255)
                        && defaults.Muted.R == fg.R && defaults.Muted.G == fg.G && defaults.Muted.B == fg.B;

            bool scrollOk = defaults.Scrollbar.A == (byte)Math.Round(0.18 * 255)
                         && defaults.ScrollbarHot.A == (byte)Math.Round(0.40 * 255);

            // M3：状态色令牌为空时必须精确等于旧行为（Success=Accent，Warning=前景 0.9）。
            bool statusOk = defaults.Success == defaults.Accent
                         && defaults.Warning == ThemeResolver.WithAlpha(fg, 0.9);
            ThemeTokens customStatus = ThemeResolver.Resolve(new StyleSettings
            {
                Success = "#112233",
                Warning = "#445566"
            });
            statusOk = statusOk
                    && customStatus.Success == System.Windows.Media.Color.FromRgb(0x11, 0x22, 0x33)
                    && customStatus.Warning == System.Windows.Media.Color.FromRgb(0x44, 0x55, 0x66);

            // M2：0 圆角必须真的是 0，不能被控件的最小值抬回去。
            bool cornerOk = MirrorpondHUD.Ui.Controls.CardPalette.Corner(0, 6) == 0
                         && MirrorpondHUD.Ui.Controls.CardPalette.Corner(0.005, 4) == 0
                         && MirrorpondHUD.Ui.Controls.CardPalette.Corner(7, 3) == 7
                         && MirrorpondHUD.Ui.Controls.CardPalette.Corner(-1, 4) == 0;

            // 等高线：级数 / 疏密 / 起伏 / 平滑 / 最小闭环都必须被夹到合法范围，
            // ContourLevels 只产出指定条数且单调递增。
            ContourLevels contourLevels = ContourLevels.FromRange(0.2, 0.8, 9);
            bool contourOk = contourLevels.Count == 9
                          && contourLevels.Values[0] > 0.2
                          && contourLevels.Values[8] < 0.8
                          && contourLevels.Values[0] < contourLevels.Values[8];
            var contourStyle = new StyleSettings
            {
                ContourLevels = 999,
                ContourEvening = -1,
                ContourRelief = 99,
                ContourSmooth = 99,
                ContourMinLoopArea = -5,
                DecorationDensity = 5
            };
            new Settings { Style = contourStyle }.Normalize();
            contourOk = contourOk
                     && contourStyle.ContourLevels == 48
                     && contourStyle.ContourEvening == 0
                     && contourStyle.ContourRelief == 4.0
                     && contourStyle.ContourSmooth == 6
                     && contourStyle.ContourMinLoopArea == 0
                     && contourStyle.DecorationDensity == 2
                     // 2.41.0：1.0x = 10 秒一个等高线形变周期，2.0x = 5 秒。
                     && Math.Abs(StyleSettings.ContourPeriodSeconds(1.0) - 10.0) < 1e-9
                     && Math.Abs(StyleSettings.ContourPeriodSeconds(2.0) - 5.0) < 1e-9;

            // M7：四档动效参数是纯数据，Cut 必须零位移、90ms 补位。
            var cutMotion = new Settings();
            cutMotion.Style.MotionProfile = "Cut";
            cutMotion.Style.LoopMotion = true;
            CardMotion.ApplySettings(cutMotion);
            bool motionOk = CardMotion.Profile == "Cut"
                         && CardMotion.LayoutDurationMs == 90
                         && CardMotion.ShowDurationMs == 80
                         && CardMotion.ShowOffset == 0
                         && CardMotion.HideOffset == 0;

            // 2.37.0：整体显隐开关与左右滑入方向；截图模式会压制物理动效，但方向归一化仍要可读。
            var slideSettings = new Settings();
            slideSettings.Behavior.OverallMotion = false;
            slideSettings.Behavior.CardSlide = "RIGHT";
            CardMotion.ApplySettings(slideSettings);
            motionOk = motionOk
                    && Settings.NormalizeCardSlide("左") == "Left"
                    && Settings.NormalizeCardSlide("whatever") == "None"
                    && Settings.NormalizeCardSlide("follow") == "Follow"
                    && Settings.NormalizeCardSlide("跟随对齐") == "Follow"
                    && CardMotion.SlideDirection == "Right";
            CardMotion.ApplySettings(new Settings());

            // 2.41.0：主题预览底色非法值回落到默认深蓝，不能把整份设置带坏。
            var previewSettings = new Settings();
            previewSettings.Behavior.SettingsPreviewBg = "not-a-color";
            previewSettings.Normalize();
            bool previewOk = previewSettings.Behavior.SettingsPreviewBg == "#0B1020"
                          && new Settings().Behavior.SettingsPreviewBg == "#0B1020";

            // 2.44.0：粒子按字符差异工作，不再用“文字骨架”过滤；等长逐字比较、增删用前后缀。
            int[] clockDiff = OverlaySurface.ChangedIndices("11:08:21", "11:08:23");
            int[] appendDiff = OverlaySurface.ChangedIndices("KEY", "KEY A");
            bool textEffectOk = Settings.NormalizeTextEffect("particle") == "Particle"
                             && Settings.NormalizeTextEffect("粒子聚字") == "Particle"
                             && Settings.NormalizeTextEffect("whatever") == "None"
                             && clockDiff.Length == 1 && clockDiff[0] == 7
                             && appendDiff.Length > 0;

            var particleStyle = new StyleSettings
            {
                TextParticleSize = 99,
                TextParticleCount = 9999,
                TextParticleSpeed = 0,
                TextParticleNearest = false,
                TextParticleGeometry = true
            };
            new Settings { Style = particleStyle }.Normalize();
            textEffectOk = textEffectOk
                        // 2.43.2：末排目标中心按粒子半径夹取，不能越过图层边缘。
                        && Math.Abs(ParticleTextLayer.ClampCenter(20.5, 20.0, 2.2) - 18.9) < 1e-9
                        && Math.Abs(particleStyle.TextParticleSize - 4.0) < 1e-9
                        && particleStyle.TextParticleCount == 3000
                        && Math.Abs(particleStyle.TextParticleSpeed - 0.2) < 1e-9
                        && !particleStyle.TextParticleNearest
                        && particleStyle.TextParticleGeometry;

            bool alignOk = Settings.NormalizeTextAlign("follow") == "Card"
                        && Settings.NormalizeTextAlign("CENTER") == "Center"
                        && Settings.NormalizeTextAlign("whatever") == "Left";

            bool validateOk = alignOk
                           && !Settings.IsColorToken("#GGGGGG")
                           && !Settings.IsColorToken("123456")
                           && Settings.IsColorToken("")
                           && Settings.IsColorToken("#80FFFFFF")
                           // P1-3：前景 / 背景走严格 #RRGGBB——9 位 Alpha 写法必须拒绝
                           //（透明度只能走 backgroundOpacity），非 7 位一律不认可。
                           && Settings.IsHexColor("#E6EDF3")
                           && !Settings.IsHexColor("#80E6EDF3")
                           && !Settings.IsHexColor("#GGGGGG")
                           && !Settings.IsHexColor("");

            var bad = new Settings
            {
                Style =
                {
                    Accent = "nope",
                    Muted = "#12",
                    Success = "nope",
                    Warning = "#12",
                    Decoration = "spiral"
                }
            };
            bad.Normalize();
            bool normalizeOk = bad.Style.Accent == "#2F6FEB" && bad.Style.Muted == ""
                            && bad.Style.Success == "" && bad.Style.Warning == ""
                            && bad.Style.Decoration == "None";

            bool codeOk = ColorCode.TryParse("#80E6EDF3", out var ca, out var cr, out var cg, out var cb)
                       && ca == 0x80 && cr == 0xE6 && cg == 0xED && cb == 0xF3
                       && !ColorCode.TryParse("nope", out ca, out cr, out cg, out cb)
                       && !ColorCode.TryParse("#12345", out ca, out cr, out cg, out cb);

            // 非法颜色必须回落到调用方给的 fallback，而不是静默变成白色。
            ThemeTokens badToken = ThemeResolver.Resolve(new StyleSettings { Foreground = "nope" });
            bool fallbackOk = OverlaySurface.ParseColor("nope", fg) == fg
                           && badToken.Muted == ThemeResolver.WithAlpha(fg, 0.45)
                           && badToken.Accent == System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xEB);

            // 2.36.0：M2–M8 默认全开；关闭 M6 后铭牌必须整体消失，信息 / 文本卡始终不显示。
            var metaSettings = new Settings();
            var metaCard = new CardSettings { Id = "chat", Kind = "chat", Name = "对话" };
            metaSettings.Cards.Add(metaCard);
            metaSettings.Overlay.Monitor = 2;
            bool metaOk = metaSettings.Style.Endfield.Shadow
                       && metaSettings.Style.Endfield.SelectionBar
                       && metaSettings.Style.Endfield.Buttons
                       && metaSettings.Style.Endfield.SegmentProgress
                       && metaSettings.Style.Endfield.Meta
                       && metaSettings.Style.Endfield.CornerBrackets
                       && metaSettings.Style.Endfield.CutCorners
                       && CardMeta.Enabled(metaSettings, metaCard)
                       && !CardMeta.Enabled(metaSettings, new CardSettings { Kind = "infotext" })
                       && CardMeta.Header(metaSettings, metaCard) == "// 01 / ";

            metaSettings.Style.Endfield.FooterText = "{code}|{name}|{monitor}";
            string customFooter = CardMeta.Footer(metaSettings, metaCard);
            bool footerOk = customFooter == "01|对话|03";

            metaSettings.Style.Endfield.Meta = false;
            bool metaOffOk = !CardMeta.Enabled(metaSettings, metaCard)
                          && CardMeta.Header(metaSettings, metaCard).Length == 0
                          && CardMeta.Footer(metaSettings, metaCard).Length == 0;
            metaSettings.Style.Endfield.Meta = true;

            bool endfieldOk = metaOk && footerOk && metaOffOk;

            bool settingsThemeOk = Settings.NormalizeSettingsTheme("light") == "Light"
                               && Settings.NormalizeSettingsTheme("DARK") == "Dark"
                               && Settings.NormalizeSettingsTheme("whatever") == "Dark";
            bool motionAliasOk = Settings.NormalizeMotionProfile("elegant") == "Current"
                              && Settings.NormalizeMotionProfile("standard") == "Industrial"
                              && Settings.NormalizeMotionProfile("brief") == "Cut"
                              && Settings.NormalizeMotionProfile("none") == "Off";
            var loopStyle = new StyleSettings
            {
                LoopPulseThickness = 99,
                LoopScanlineThickness = -1
            };
            new Settings { Style = loopStyle }.Normalize();
            bool loopThicknessOk = loopStyle.LoopPulseThickness == 8
                               && loopStyle.LoopScanlineThickness == 1;

            // 2.43.0：富内容文字补丁（描边 / 加粗 / 行高）必须能加也能撤。
            var richStyle = new StyleSettings
            {
                TextOutline = true,
                OutlineThickness = 1.5,
                OutlineColor = "#112233",
                Bold = true,
                LineHeight = 22
            };
            var richProbe = new TextBlock { Text = "富内容" };
            CardTextStyle.ApplyToElement(richProbe, richStyle);
            bool richStyleOk = richProbe.Effect is System.Windows.Media.Effects.DropShadowEffect
                            && richProbe.FontWeight == FontWeights.SemiBold
                            && Math.Abs(richProbe.LineHeight - 22) < 0.01;
            richStyle.TextOutline = false;
            richStyle.Bold = false;
            richStyle.LineHeight = 0;
            CardTextStyle.ApplyToElement(richProbe, richStyle);
            richStyleOk = richStyleOk
                       && richProbe.Effect is null
                       && richProbe.FontWeight == FontWeights.Normal
                       && double.IsNaN(richProbe.LineHeight);

            themeOk = defaultOk && mutedOk && scrollOk && statusOk && cornerOk && contourOk && motionOk
                   && validateOk && normalizeOk && codeOk && fallbackOk && endfieldOk
                   && settingsThemeOk && motionAliasOk && loopThicknessOk && previewOk && textEffectOk
                   && richStyleOk;
        }
        catch (Exception ex)
        {
            Log.Error("外观令牌自检异常", ex);
        }
        return themeOk;
    }

    /// <summary>主题文件（P3/P4）：往返一致与仓库示例主题解析；异常收尾与原单 try 块一致。</summary>
    static bool RunThemeFileChecks()
    {
        bool themeFileOk = false;
        try
        {
            bool roundTripOk = CheckThemeRoundTrip();
            bool samplesOk = CheckThemeSamples();
            themeFileOk = roundTripOk && samplesOk;
        }
        catch (Exception ex)
        {
            Log.Error("主题文件自检异常", ex);
        }
        return themeFileOk;
    }

    /// <summary>主题往返：导出 → 解析 → 套用 → 再导出，字段与外观必须一致（含软色数字写法）。</summary>
    static bool CheckThemeRoundTrip()
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
            TextAlignment = "Card",
            TextOutline = true,
            OutlineColor = "#FFFFFF",
            OutlineThickness = 1.5,
            IconScale = 1.3,
            Accent = "#4C8DFF",
            Danger = "#E57B6E",
            Success = "#3FA34D",
            Warning = "#D9A400",
            Muted = "#73E6EDF3",
            Border = "#1A101820",
            Panel = "#0B101820",
            Scrollbar = "#2E101820",
            ScrollbarHot = "#66101820",
            MonoFont = "Consolas",
            MonoReadouts = true,

            Decoration = "Grid",
            DecorationDensity = 0.35,
            ContourLevels = 14,
            ContourEvening = 0.6,
            ContourRelief = 1.2,
            ContourSmooth = 3,
            ContourMinLoopArea = 55,
            ContourSpeed = 1.6,
            MotionProfile = "Industrial",
            TextEffect = "Particle",
            TextParticleSize = 2.6,
            TextParticleCount = 1200,
            TextParticleSpeed = 1.4,
            TextParticleNearest = false,
            TextParticleGeometry = true,
            LoopEffect = "Scanline",
            LoopDurationMs = 3200,
            LoopAmplitude = 0.18
        };
        source.Overlay = new OverlaySettings { CornerRadius = 12, Shadow = false };
        source.Cli = new CliSettings { MaxWidth = 500, MaxHeight = 440, EntryMaxHeight = 150, MaxEntries = 8 };

        string json = ThemeFile.Export(source, "往返测试");

        bool parseOk = ThemeFile.TryParse(json, out var parsed, out var error)
                    && parsed is not null
                    && parsed.Name == "往返测试"
                    && parsed.Schema == ThemeFile.SupportedSchema;

        var applied = new Settings();
        if (parsed is not null) parsed.ApplyTo(applied);
        applied.Normalize();

        bool same = ThemeSettingsEqual(applied, source);

        // 软色写数字 = 按前景派生：0.45 * 255 ≈ 0x73
        const string softJson = "{\"schema\":1,\"appearance\":{\"colors\":{\"foreground\":\"#E6EDF3\",\"muted\":0.45}}}";
        bool softParsed = ThemeFile.TryParse(softJson, out var soft, out error);
        var softApplied = new Settings();
        if (soft is not null) soft.ApplyTo(softApplied);
        bool alphaOk = softParsed && softApplied.Style.Muted == "#73E6EDF3";

        bool stable = ThemeFile.Export(applied, "往返测试") == json;

        return parseOk && same && alphaOk && stable;
    }

    /// <summary>仓库示例主题（docs/themes/*.mptheme）必须都能解析并套用；发布版没有 docs 目录时跳过。</summary>
    static bool CheckThemeSamples()
    {
        // 仓库里的示例主题（docs/themes/*.mptheme）必须都能解析并套用；发布版没有 docs 目录时跳过。
        bool samplesOk = true;
        DirectoryInfo dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++)
        {
            string themes = Path.Combine(dir.FullName, "docs", "themes");
            if (Directory.Exists(themes))
            {
                string[] files = Directory.GetFiles(themes, "*.mptheme");
                samplesOk = files.Length > 0;
                foreach (string file in files)
                {
                    if (!ThemeFile.TryParse(File.ReadAllText(file), out var sample, out var sampleError))
                    {
                        samplesOk = false;
                        Log.Error($"示例主题解析失败 {Path.GetFileName(file)}: {sampleError}");
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

        return samplesOk;
    }

    /// <summary>主题往返前后逐字段比对；单独成方法只为让 CheckThemeRoundTrip 保持在 60 行内。</summary>
    static bool ThemeSettingsEqual(Settings applied, Settings source) => applied.Style.FontFamily == source.Style.FontFamily
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
            && applied.Style.Success == source.Style.Success
            && applied.Style.Warning == source.Style.Warning
            && applied.Style.MonoReadouts == source.Style.MonoReadouts

            && applied.Style.Decoration == source.Style.Decoration
            && Math.Abs(applied.Style.DecorationDensity - source.Style.DecorationDensity) < 1e-9
            && applied.Style.ContourLevels == source.Style.ContourLevels
            && Math.Abs(applied.Style.ContourEvening - source.Style.ContourEvening) < 1e-9
            && Math.Abs(applied.Style.ContourRelief - source.Style.ContourRelief) < 1e-9
            && applied.Style.ContourSmooth == source.Style.ContourSmooth
            && Math.Abs(applied.Style.ContourMinLoopArea - source.Style.ContourMinLoopArea) < 1e-9
            && Math.Abs(applied.Style.ContourSpeed - source.Style.ContourSpeed) < 1e-9
            && applied.Style.MotionProfile == source.Style.MotionProfile
            && applied.Style.TextEffect == source.Style.TextEffect
            && Math.Abs(applied.Style.TextParticleSize - source.Style.TextParticleSize) < 1e-9
            && applied.Style.TextParticleCount == source.Style.TextParticleCount
            && Math.Abs(applied.Style.TextParticleSpeed - source.Style.TextParticleSpeed) < 1e-9
            && applied.Style.TextParticleNearest == source.Style.TextParticleNearest
            && applied.Style.TextParticleGeometry == source.Style.TextParticleGeometry
            && applied.Style.LoopEffect == source.Style.LoopEffect
            && applied.Style.LoopDurationMs == source.Style.LoopDurationMs
            && Math.Abs(applied.Style.LoopAmplitude - source.Style.LoopAmplitude) < 1e-9
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

    /// <summary>主题文件校验：高版本 / 坏颜色 / 越界 / 缺 appearance / 超大文件均拒绝。</summary>
    static bool RunThemeRejectChecks()
    {
        bool themeRejectOk = false;
        try
        {
            bool future = !ThemeFile.TryParse("{\"schema\":2,\"appearance\":{}}", out var doc, out var error)
                       && error is not null && error.Contains("版本");
            bool badColor = !ThemeFile.TryParse("{\"schema\":1,\"appearance\":{\"colors\":{\"accent\":\"nope\"}}}", out doc, out error);
            bool badRange = !ThemeFile.TryParse("{\"schema\":1,\"appearance\":{\"typography\":{\"fontSize\":200}}}", out doc, out error);
            bool badDecoration = !ThemeFile.TryParse(
                "{\"schema\":1,\"appearance\":{\"industrial\":{\"decoration\":\"spiral\"}}}", out doc, out error)
                && error is not null && error.Contains("decoration");
            bool badMotion = !ThemeFile.TryParse(
                "{\"schema\":1,\"appearance\":{\"motion\":{\"profile\":\"warp\"}}}", out doc, out error)
                && error is not null && error.Contains("profile");
            bool badTextEffect = !ThemeFile.TryParse(
                "{\"schema\":1,\"appearance\":{\"motion\":{\"textEffect\":\"sparkle\"}}}", out doc, out error)
                && error is not null && error.Contains("textEffect");
            bool noAppearance = !ThemeFile.TryParse("{\"schema\":1}", out doc, out error);
            bool malformed = !ThemeFile.TryParse("{oops", out doc, out error);

            // 坏文件不碰当前设置；好文件导入成功且源对象保持不变。
            var current = new Settings();
            current.Style.Accent = "#123456";
            string temp = Path.Combine(Path.GetTempPath(), $"mph-theme-selftest-{Guid.NewGuid():N}.mptheme");
            File.WriteAllText(temp, "{\"schema\":9,\"appearance\":{}}");
            bool rejectedOk = !ThemeFile.TryImport(temp, current, out var rejected, out error) && rejected is null;

            File.WriteAllText(temp, ThemeFile.Export(new Settings(), "临时"));
            bool importedOk = ThemeFile.TryImport(temp, current, out var imported, out error)
                           && imported is not null
                           && imported.Style.Accent == new Settings().Style.Accent
                           && current.Style.Accent == "#123456";

            // P2-7：超过 1 MB 的主题文件必须明确拒绝，不能整读。
            string big = $"{temp}.big";
            File.WriteAllBytes(big, new byte[ThemeFile.MaxImportBytes + 1]);
            bool bigOk = !ThemeFile.TryImport(big, current, out var bigImported, out error)
                      && bigImported is null
                      && error is not null && error.Contains("过大");

            try { File.Delete(temp); } catch { }
            try { File.Delete(big); } catch { }

            themeRejectOk = future && badColor && badRange && badDecoration && badMotion && badTextEffect && noAppearance && malformed
                         && rejectedOk && importedOk && bigOk;
        }
        catch (Exception ex)
        {
            Log.Error("主题文件校验自检异常", ex);
        }
        return themeRejectOk;
    }

    /// <summary>内置主题目录（P4）：EmbeddedResource 编进程序集，发布版不依赖 docs 目录。</summary>
    static bool RunThemeCatalogChecks()
    {
        bool themeCatalogOk = false;
        try
        {
            // P4：示例主题以 EmbeddedResource 编进程序集，发布版不再依赖 docs 目录。
            // 2.30.0 新增终末地墨 / 纸两套，至少 5 份。
            var catalog = ThemeCatalog.Load();
            themeCatalogOk = catalog.Count >= 5
                          && catalog.TrueForAll(p => p.Document is not null && !string.IsNullOrWhiteSpace(p.Name));
            if (!themeCatalogOk)
                Log.Warn($"内置主题数量不足或解析失败: {catalog.Count}");
        }
        catch (Exception ex)
        {
            Log.Error("内置主题自检异常", ex);
        }
        return themeCatalogOk;
    }

    /// <summary>设置副本（#7）：克隆失败必须返回 false + null，不能用默认值顶替。</summary>
    static bool RunCloneGuardChecks()
    {
        bool cloneGuardOk = false;
        try
        {
            // #7：克隆失败必须返回 false + null，绝不能拿 new Settings() 顶替（那会写默认值回盘）。
            bool guarded = !SettingsWindow.TryClone(null, out var badClone, out var badError) && badClone is null;

            bool works = SettingsWindow.TryClone(new Settings(), out var goodClone, out var goodError)
                      && goodClone is not null;

            cloneGuardOk = guarded && works;
        }
        catch (Exception ex)
        {
            Log.Error("设置副本自检异常", ex);
        }
        return cloneGuardOk;
    }

    /// <summary>交互卡唯一：默认配置一张，再加一张应被计数拦下。</summary>
    static bool RunSingleInteractiveChecks()
    {
        bool singleInteractiveOk = false;
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
        return singleInteractiveOk;
    }

    /// <summary>共享控件（2.10.0）：CardList 空态/计数/选中，CardStatus 忙碌/进度/错误。</summary>
    static bool RunSharedWidgetChecks()
    {
        bool widgetsOk = false;
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

            // 2.36.0：关闭工业按钮 / 分段进度后必须能走回旧模板，而不是抛异常。
            var legacyStyle = new StyleSettings
            {
                FontSize = 15,
                Foreground = "#E6EDF3",
                Endfield = new EndfieldStyleSettings
                {
                    Buttons = false,
                    SegmentProgress = false,
                    CutCorners = false,
                    SelectionBar = false
                }
            };
            var legacyButton = new CardButton("旧按钮", CardButtonTone.Primary);
            legacyButton.ApplyStyle(legacyStyle, 12);
            legacyButton.Measure(new Size(200, 100));
            legacyButton.Arrange(new Rect(0, 0, 120, 30));
            var legacyStatus = new CardStatus();
            legacyStatus.ApplyStyle(legacyStyle);
            legacyStatus.SetProgress(0.5);
            legacyStatus.Measure(new Size(300, 80));
            legacyStatus.Arrange(new Rect(0, 0, 300, 40));
            bool legacyOk = legacyButton.CornerRadius <= 7
                         && legacyButton.Template is not null
                         && legacyStatus.Kind == CardStatusKind.Normal;

            // 2.37.0：托盘 / 卡片右键菜单共用样式；只保留四个可见入口。
            // 2.41.0：设置窗菜单随明暗切换，且 Popup 内文字不能再被全局 TextBlock 染成白色。
            var menu = new ContextMenu();
            HudMenuStyle.Apply(menu, false);
            menu.Items.Add(new MenuItem { Header = "隐藏叠层", InputGestureText = "F9" });
            menu.Items.Add(new MenuItem { Header = "打开设置", InputGestureText = "F10" });
            menu.Items.Add(new MenuItem { Header = "解锁", InputGestureText = "F8" });
            menu.Items.Add(new MenuItem { Header = "退出" });
            menu.ApplyTemplate();
            foreach (object entry in menu.Items)
                if (entry is MenuItem item) item.ApplyTemplate();
            menu.Measure(new Size(320, 220));
            menu.Arrange(new Rect(0, 0, 320, 220));

            bool lightMenuOk = MenuContrast(menu, dark: false);
            bool hasTextStyle = menu.Resources[typeof(TextBlock)] is Style;
            HudMenuStyle.Apply(menu, true);
            bool darkMenuOk = MenuContrast(menu, dark: true);
            var accentText = Application.Current?.TryFindResource("AccentText") as SolidColorBrush;
            bool menuOk = menu.Items.Count == 4
                       && menu.Style is not null
                       && menu.ItemContainerStyle is not null
                       && lightMenuOk && darkMenuOk && hasTextStyle
                       && accentText is not null && accentText.Color.R < 50;

            // 2.42.0：菜单项加入集合时就把字符串标题换成带本地 MenuText 的 TextBlock；
            // 自定义标题树里的普通文字也写本地前景色，图标/快捷键的显式颜色保留。
            var headerMenu = new ContextMenu();
            HudMenuStyle.Apply(headerMenu, false);
            var headerItem = new MenuItem { Header = "菜单文字测试" };
            headerMenu.Items.Add(headerItem);
            bool stringHeaderOk = headerItem.Header is TextBlock stringBlock
                               && stringBlock.ReadLocalValue(TextBlock.ForegroundProperty) != DependencyProperty.UnsetValue;

            var headerPanel = new StackPanel();
            var headerLabel = new TextBlock { Text = "自定义标题" };
            var headerIcon = new TextBlock { Text = "图标", Foreground = Brushes.Gold };
            headerPanel.Children.Add(headerLabel);
            headerPanel.Children.Add(headerIcon);
            var panelItem = new MenuItem { Header = headerPanel };
            headerMenu.Items.Add(panelItem);
            bool customHeaderOk = headerLabel.ReadLocalValue(TextBlock.ForegroundProperty) != DependencyProperty.UnsetValue
                               && headerIcon.ReadLocalValue(TextBlock.ForegroundProperty) is SolidColorBrush;
            menuOk = menuOk && stringHeaderOk && customHeaderOk;

            widgetsOk = emptyOk && countOk && selectOk && busyOk && errorOk && legacyOk && menuOk;
        }
        catch (Exception ex)
        {
            Log.Error("共享控件自检异常", ex);
        }
        return widgetsOk;
    }

    /// <summary>菜单背景 / 文字的明暗必须成对：暗色黑底白字、亮色白底黑字。</summary>
    static bool MenuContrast(ContextMenu menu, bool dark)
    {
        if (menu is null) return false;
        var background = menu.Resources["MenuBg"] as SolidColorBrush;
        var foreground = menu.Resources["MenuText"] as SolidColorBrush;
        if (background is null || foreground is null) return false;

        double bg = (background.Color.R + background.Color.G + background.Color.B) / 3.0 / 255.0;
        double fg = (foreground.Color.R + foreground.Color.G + foreground.Color.B) / 3.0 / 255.0;
        return dark ? bg < 0.25 && fg > 0.75 : bg > 0.75 && fg < 0.25;
    }

    /// <summary>共享控件（2.10.1）：Header 按钮可点、Input 回车提交、DropOverlay 显隐。</summary>
    static bool RunInteractiveWidgetChecks()
    {
        bool widgets2Ok = false;
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
            var labelForeground = label is null ? null : label.Foreground as SolidColorBrush;
            var buttonForeground = button.Foreground as SolidColorBrush;
            bool buttonTextOk = labelForeground is not null && buttonForeground is not null
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
        return widgets2Ok;
    }

    /// <summary>表单控件：滑块 / 开关 / 下拉 / 数字 / 颜色 / 分段与声明式表单。</summary>
    static bool RunFormControlsChecks()
    {
        bool formControlsOk = false;
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
        return formControlsOk;
    }

    /// <summary>数据控件（UiKit 自检）：Table / Badge / Tag / Metric / Gauge / Ring。</summary>
    static bool RunDataControlsChecks()
    {
        bool dataControlsOk = false;
        try
        {
            string dataError = CardDataSelfTest.Run();
            dataControlsOk = string.IsNullOrEmpty(dataError);
            if (!dataControlsOk) Log.Warn($"数据控件自检失败: {dataError}");
        }
        catch (Exception ex)
        {
            Log.Error("数据控件自检异常", ex);
        }
        return dataControlsOk;
    }

    /// <summary>图表 / 树 / 时间线（UiKit 自检）：空数据、数据操作与离屏渲染。</summary>
    static bool RunChartControlsChecks()
    {
        bool chartControlsOk = false;
        try
        {
            string chartError = CardChartSelfTest.Run();
            chartControlsOk = string.IsNullOrEmpty(chartError);
            if (!chartControlsOk) Log.Warn($"图表 / 树 / 时间线自检失败: {chartError}");
        }
        catch (Exception ex)
        {
            Log.Error("图表 / 树 / 时间线自检异常", ex);
        }
        return chartControlsOk;
    }

    /// <summary>浮层 / 交互控件（UiKit 自检）：Modal / Dialog / Popover / Tooltip / ContextMenu / Toast。</summary>
    static bool RunOverlayControlsChecks()
    {
        bool overlayControlsOk = false;
        try
        {
            string overlayError = CardOverlaySelfTest.Run();
            overlayControlsOk = string.IsNullOrEmpty(overlayError);
            if (!overlayControlsOk) Log.Warn($"浮层 / 交互控件自检失败: {overlayError}");
        }
        catch (Exception ex)
        {
            Log.Error("浮层 / 交互控件自检异常", ex);
        }
        return overlayControlsOk;
    }

    /// <summary>图标字体：Lucide 码位查询、字体族与 CardIcon 真实渲染。</summary>
    static bool RunIconFontChecks()
    {
        bool iconFontOk = false;
        try
        {
            string iconError = CardIconSelfTest.Run();
            iconFontOk = string.IsNullOrEmpty(iconError);
            if (!iconFontOk) Log.Warn($"图标字体自检失败: {iconError}");
        }
        catch (Exception ex)
        {
            Log.Error("图标字体自检异常", ex);
        }
        return iconFontOk;
    }

    /// <summary>渲染上下文（2.10.2）：富内容创建上下文透传这张卡片的 ICardHost。</summary>
    static bool RunContextHostChecks()
    {
        bool contextHostOk = false;
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
        return contextHostOk;
    }

    /// <summary>Shell 图标缓存、缩略图请求与拖出阈值（2.10.3）。</summary>
    static bool RunShellChecks()
    {
        bool shellOk = false;
        try
        {
            // 2.10.3：Shell 图标缓存一致、缩略图请求不会挂住；拖出阈值与空输入。
            var icon1 = ShellIcons.GetIcon("selftest.zzz", 16);
            var icon2 = ShellIcons.GetIcon("selftest.zzz", 16);
            bool cacheOk = ReferenceEquals(icon1, icon2) && (icon1 is null || icon1.Width > 0);

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
        return shellOk;
    }

    /// <summary>配置层：默认卡片 / JSON 往返（含 Extras）/ 顶层 null 块；修复段共用原单 try 的异常收尾。</summary>
    static bool RunConfigChecks(out bool configRepairOk)
    {
        bool configOk = false;
        configRepairOk = false;
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
                Id = "selftest-config",
                Kind = "infotext",
                Name = "自检卡",
                Preset = "clock",
                Content = "{time}",
                Spacing = 16
            };
            card.Extras["command"] = new Dictionary<string, object>
            {
                { "Shell", "cmd" }, { "TimeoutSeconds", 20 }
            };
            source.Cards.Add(card);
            Settings roundTrip = Json.Deserialize<Settings>(Json.Serialize(source));
            bool roundTripOk = roundTrip is not null && roundTrip.Cards is not null && roundTrip.Cards.Count == 1
                            && roundTrip.Cards[0].Id == "selftest-config"
                            && roundTrip.Cards[0].Kind == "infotext"
                            && roundTrip.Cards[0].Preset == "clock"
                            && roundTrip.Cards[0].Content == "{time}"
                            && roundTrip.Cards[0].Extras.ContainsKey("command");

            Settings nullBlocks = Json.Deserialize<Settings>(
                "{\"SchemaVersion\":7,\"Style\":null,\"Overlay\":null,\"Cli\":null," +
                "\"Data\":null,\"Behavior\":null,\"Hotkeys\":null,\"Cards\":null}");
            nullBlocks.Normalize();
            bool nullOk = nullBlocks.Style is not null && nullBlocks.Overlay is not null && nullBlocks.Cli is not null
                       && nullBlocks.Data is not null && nullBlocks.Behavior is not null
                       && nullBlocks.Hotkeys is not null && nullBlocks.Cards is not null;

            configOk = defaultsOk && roundTripOk && nullOk;

            configRepairOk = CheckConfigRepair();
        }
        catch (Exception ex)
        {
            Log.Error("配置修复自检异常", ex);
        }
        return configOk;
    }

    /// <summary>配置修复：SettingsLoader 版本校验 + 状态存储原子写；异常冒泡给 RunConfigChecks 统一记录。</summary>
    static bool CheckConfigRepair()
    {
        bool settingsLoaderOk = CheckSettingsLoader();
        bool atomicOk = CheckCardStateAtomic();
        return settingsLoaderOk && atomicOk;
    }

    /// <summary>设置加载：损坏 / 旧 Schema / v6→v7 迁移 / 无版本 / 未来版本只读 / 数字溢出 / 未知 Kind 保留。</summary>
    static bool CheckSettingsLoader()
    {
        Settings broken = SettingsLoader.Load("{\"SchemaVersion\":7,", out var needsSave, out var corrupted);
        bool brokenOk = corrupted && !needsSave && broken is not null;

        Settings oldSchema = SettingsLoader.Load("{\"SchemaVersion\":1,\"Cards\":[]}", out needsSave, out corrupted);
        bool oldOk = corrupted && !needsSave && oldSchema is not null;

        Settings unversioned = SettingsLoader.Load(
            "{\"Cards\":[{\"Id\":\"c1\",\"Kind\":\"infotext\",\"Name\":\"文本\"}]}",
            out needsSave, out corrupted);
        bool unversionedOk = !corrupted && needsSave && unversioned.Cards.Count == 1
                          && unversioned.Cards[0].Kind == "infotext";

        Settings future = SettingsLoader.Load("{\"SchemaVersion\":999,\"Cards\":[]}",
                                                  out needsSave, out corrupted, out var futureFlag);
        bool futureOk = !corrupted && futureFlag && !needsSave && future is not null;

        Settings overflow = SettingsLoader.Load("{\"SchemaVersion\":1e300,\"Cards\":[]}",
                                                   out needsSave, out corrupted, out futureFlag);
        bool overflowOk = corrupted && !needsSave;

        // Schema 7（2.24.0）：v6 一次性迁移——玻璃模糊字段合并、GlassMode 的 Dwm 更名 DesktopFeed、
        // Chat 模式别名改写 Context；迁移后必须需要落盘（needsSave）且不能再按损坏处理。
        const string legacyV6 =
            "{\"SchemaVersion\":6,\"Style\":{\"GlassMode\":\"Dwm\",\"GlassBlur\":26,\"GlassFps\":12," +
            "\"AcrylicBlur\":12,\"AcrylicFps\":15},\"Cards\":[{\"Id\":\"chat-legacy\",\"Kind\":\"chat\"," +
            "\"Name\":\"旧对话\",\"Extras\":{\"chat\":{\"Mode\":\"polling\"}}}]}";
        Settings v6 = SettingsLoader.Load(legacyV6, out needsSave, out corrupted);
        bool v6ChatModeOk = false;
        if (v6.Cards is not null && v6.Cards.Count == 1 && v6.Cards[0].Extras is not null)
        {
            if (v6.Cards[0].Extras.TryGetValue("chat", out var chatNode))
            {
                var chat = chatNode as Dictionary<string, object>;
                if (chat is not null && chat.TryGetValue("Mode", out var modeNode))
                    v6ChatModeOk = string.Equals(Convert.ToString(modeNode), "Context", StringComparison.Ordinal);
            }
        }
        bool v6Ok = !corrupted && needsSave && v6.SchemaVersion == 7
                 && v6.Style.GlassMode == "DesktopFeed"
                 && v6.Style.BlurRadius == 26
                 && v6.Style.BlurFps == 12
                 && v6ChatModeOk;

        // 非桌面模式取 AcrylicBlur / AcrylicFps 那一套，Compat 归一后保持 Compat。
        const string legacyV6Compat =
            "{\"SchemaVersion\":6,\"Style\":{\"GlassMode\":\"Compat\",\"GlassBlur\":26,\"GlassFps\":12," +
            "\"AcrylicBlur\":7,\"AcrylicFps\":20},\"Cards\":[]}";
        Settings v6Compat = SettingsLoader.Load(legacyV6Compat, out needsSave, out corrupted);
        bool v6CompatOk = !corrupted && needsSave && v6Compat.SchemaVersion == 7
                       && v6Compat.Style.GlassMode == "Compat"
                       && v6Compat.Style.BlurRadius == 7
                       && v6Compat.Style.BlurFps == 20;

        // 未知 Kind（例如插件卸载）：保留原配置，不改写成默认卡。
        var unknownSettings = new Settings();
        unknownSettings.Cards.Add(new CardSettings
        {
            Id = "plugin-missing",
            Kind = "example-clock",
            Name = "插件卡",
            Content = "插件内容",
            Extras = new Dictionary<string, object> { { "plugin", "keep" } }
        });
        CardModel.Normalize(unknownSettings);
        bool unknownKindOk = unknownSettings.Cards.Count == 1
                          && unknownSettings.Cards[0].Kind == "example-clock"
                          && unknownSettings.Cards[0].Content == "插件内容"
                          && unknownSettings.Cards[0].Extras.ContainsKey("plugin");

        return brokenOk && oldOk && unversionedOk && futureOk && overflowOk
            && v6Ok && v6CompatOk && unknownKindOk;
    }

    /// <summary>状态存储：原子写、损坏回退 .bak、拒绝路径穿越、删除；清理逻辑保持原 try/finally。</summary>
    static bool CheckCardStateAtomic()
    {
        // 状态存储：原子写、损坏回退 .bak、拒绝路径穿越、删除。
        string atomicId = $"selftest-atomic-{Guid.NewGuid():N}";
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
                         && CardStateStore.Read(atomicId, "..\\escape.txt") is null;
            bool deleted = CardStateStore.Delete(atomicId, "state.json");
            bool gone = CardStateStore.Read(atomicId, "state.json") is null;
            atomicOk = current && recoveredFromBak && rejected && deleted && gone;
        }
        finally
        {
            try { if (Directory.Exists(atomicDir)) Directory.Delete(atomicDir, true); } catch { }
        }

        return atomicOk;
    }

    /// <summary>卡片契约与注册表：6 个内置 Kind 齐全、未知 Kind 占位、默认卡片；异常收尾与原单 try 一致。</summary>
    static bool RunCardRegistryChecks()
    {
        bool cardOk = false;
        try
        {
            bool contractsOk = CheckContractsAndRegistry();
            bool cardDefaultsOk = CheckCardDefaults();
            cardOk = contractsOk && cardDefaultsOk;
        }
        catch (Exception ex)
        {
            Log.Error("卡片注册表自检异常", ex);
        }
        return cardOk;
    }

    /// <summary>契约边界与注册表：插件 API 在 Contracts，宿主实现与归一化钩子仍在宿主程序集。</summary>
    static bool CheckContractsAndRegistry()
    {
        // 契约边界：插件 API 在 Contracts；宿主实现与配置归一化钩子仍在宿主程序集。
        bool contractsOk = string.Equals(typeof(ICardModule).Assembly.GetName().Name, "MirrorpondHUD.Contracts", StringComparison.Ordinal)
                        && typeof(CardRenderContext).Assembly == typeof(ICardModule).Assembly
                        && typeof(Settings).Assembly == typeof(ICardModule).Assembly
                        && typeof(SecretStore).Assembly == typeof(ICardModule).Assembly
                        && string.Equals(typeof(CardRegistry).Assembly.GetName().Name, "MirrorpondHUD", StringComparison.Ordinal)
                        && ConfigStore.SettingsNormalizer is not null;

        // 注册表：6 个内置卡片插件必须全部注册；Kind 唯一且元数据完整。
        string[] requiredKinds = { "infotext", "command", "media", "chat", "dropstation", "buttons" };
        IList<ICardModule> modules = CardRegistry.Modules;
        var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        bool metadataOk = true;
        foreach (ICardModule module in modules)
        {
            if (module is null) { metadataOk = false; continue; }
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

        // 版本纪律：6 个内置 manifest 的 version 必须与宿主程序集版本一致。
        // 以前 CardPluginLoader 只把 manifest.version 写日志、不校验，漏改是静默的。
        // 用户插件与内置版本本来就不要求一致，所以只检查 builtin.* 目录。
        bool manifestOk = CheckBundledManifestVersions();

        return contractsOk && registryOk && placeholderOk && manifestOk;
    }

    /// <summary>内置插件 manifest 版本必须与宿主一致；用户插件不参与这项检查。</summary>
    static bool CheckBundledManifestVersions()
    {
        try
        {
            string root = Path.Combine(AppContext.BaseDirectory, "plugins", "cards");
            if (!Directory.Exists(root)) return false;

            Version host = typeof(SelfTest).Assembly.GetName().Version;
            string expected = host is null ? "" : host.ToString(3);
            if (expected.Length == 0) return false;

            bool builtinOk = CheckManifestVersions(root, expected, 6);
            if (!builtinOk) return false;

            // 反例：用户插件目录里放一个版本不一致的 manifest，不能影响内置检查。
            return CheckUserManifestIsIgnored(expected);
        }
        catch (Exception ex)
        {
            Log.Warn($"内置插件 manifest 版本检查失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>只读 builtin.* 目录下的 manifest；用户插件目录不进入检查。</summary>
    static bool CheckManifestVersions(string root, string expected, int minBuiltinCount, bool logMismatch = true)
    {
        if (!Directory.Exists(root)) return false;

        string[] dirs = Directory.GetDirectories(root, "builtin.*", SearchOption.TopDirectoryOnly);
        if (dirs.Length < minBuiltinCount) return false;

        foreach (string dir in dirs)
        {
            string file = Path.Combine(dir, "manifest.json");
            if (!File.Exists(file)) return false;

            var manifest = Json.Deserialize<ManifestVersionProbe>(File.ReadAllText(file));
            if (manifest is null) return false;
            if (!string.Equals((manifest.version ?? "").Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                if (logMismatch)
                    Log.Warn($"内置插件 manifest 版本不一致: {file} version={(manifest.version ?? "")} 期望={expected}");
                return false;
            }
        }
        return true;
    }

    /// <summary>临时目录里同时放 builtin 与 user 插件，验证 user 的版本不参与内置检查。</summary>
    static bool CheckUserManifestIsIgnored(string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), $"mhud-manifest-selftest-{Guid.NewGuid():N}");
        try
        {
            string builtinDir = Path.Combine(root, "builtin.test");
            string userDir = Path.Combine(root, "user.card");
            Directory.CreateDirectory(builtinDir);
            Directory.CreateDirectory(userDir);

            string builtinManifest = Path.Combine(builtinDir, "manifest.json");
            File.WriteAllText(builtinManifest, "{\"version\":\"" + expected + "\"}");
            File.WriteAllText(Path.Combine(userDir, "manifest.json"), "{\"version\":\"1.0.0\"}");

            bool userIgnored = CheckManifestVersions(root, expected, 1, false);

            // 反证：builtin 自己写错时必须失败；这是预期负例，不写 WARN。
            File.WriteAllText(builtinManifest, "{\"version\":\"9.9.9\"}");
            bool builtinMismatchFails = !CheckManifestVersions(root, expected, 1, false);

            return userIgnored && builtinMismatchFails;
        }
        catch (Exception ex)
        {
            Log.Warn($"用户插件 manifest 隔离检查失败: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception ex) { Log.Warn($"用户插件 manifest 隔离目录清理失败: {ex.Message}"); }
        }
    }

    /// <summary>Json.Deserialize 按 public 字段绑定，与 PluginManifest 的协议保持一致。</summary>
    public sealed class ManifestVersionProbe
    {
        public string version = "";
    }

    /// <summary>默认卡片布局（3 张信息文本 + 命令行 + 媒体）与 M-1 的模块自报默认卡。</summary>
    static bool CheckCardDefaults()
    {
        var defaultCards = CardModel.CreateDefaultCards();
        var defaultKinds = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool uniqueIds = true;
        foreach (CardSettings item in defaultCards)
        {
            defaultKinds.Add(item.Kind);
            if (!ids.Add(item.Id)) uniqueIds = false;
        }

        bool defaultCardsOk = defaultCards.Count == 5
                           && defaultKinds.FindAll(k => k == "infotext").Count == 3
                           && defaultKinds.Contains("command") && defaultKinds.Contains("media")
                           && CardModel.CountInteractive(defaultCards) == 1
                           && uniqueIds;

        // M-1：默认卡必须全部来自 ICardDefaultProvider，且模板只在插件里有一份。
        bool providersOk = true;
        int providerCount = 0;
        foreach (ICardModule module in CardRegistry.Modules)
        {
            var provider = module as ICardDefaultProvider;
            if (provider is null) continue;

            providerCount++;
            IList<CardSettings> cards = provider.CreateDefaults();
            if (cards is null || cards.Count == 0) { providersOk = false; continue; }
            foreach (CardSettings item in cards)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Kind)) providersOk = false;
            }
        }

        bool templatesOk = false;
        var infoProvider = CardRegistry.Get("infotext") as ICardDefaultProvider;
        if (infoProvider is not null)
        {
            IList<CardSettings> infoCards = infoProvider.CreateDefaults();
            templatesOk = infoCards is not null && infoCards.Count == 3
                       && infoCards[0].Content.IndexOf("{cpu", StringComparison.Ordinal) >= 0
                       && infoCards[1].Content.IndexOf("{time", StringComparison.Ordinal) >= 0
                       && infoCards[2].Content.IndexOf("{keys", StringComparison.Ordinal) >= 0;
        }

        return defaultCardsOk && providersOk && templatesOk && providerCount >= 3;
    }

    /// <summary>通用插件自检：遍历实现 ICardSelfTest 的模块；结论写进自检输出。</summary>
    static bool RunPluginSelfTestChecks(StringBuilder sb)
    {
        bool pluginSelfTestOk = false;
        try
        {
            // 通用插件自检：宿主不认具体卡片类型，遍历实现 ICardSelfTest 的模块。
            var failuresText = new List<string>();
            int tested = 0;
            foreach (ICardModule module in CardRegistry.Modules)
            {
                var selfTest = module as ICardSelfTest;
                if (selfTest is null) continue;
                tested++;
                try
                {
                    string result = selfTest.RunSelfTest();
                    if (!string.IsNullOrWhiteSpace(result))
                        failuresText.Add($"{module.Kind}: {result}");
                }
                catch (Exception ex)
                {
                    failuresText.Add($"{module.Kind}: {ex.Message}");
                }
            }
            pluginSelfTestOk = tested >= 6 && failuresText.Count == 0;
            string pluginSelfTestText = $"{tested} 个模块自检通过 / {failuresText.Count} 个失败";
            if (failuresText.Count > 0)
                pluginSelfTestText += $"（{string.Join("；", failuresText.ToArray())}）";
            Fix(sb, "插件自检", pluginSelfTestText);
        }
        catch (Exception ex)
        {
            Log.Error("插件自检异常", ex);
        }
        return pluginSelfTestOk;
    }

    /// <summary>字符串加密：DPAPI 往返，明文不落盘（SecretStore 在契约程序集）。</summary>
    static bool RunSecretStoreChecks()
    {
        bool secretOk = false;
        try
        {
            // 字符串加密：DPAPI 往返；明文不落盘（SecretStore 在契约程序集，插件与宿主共用）。
            const string secret = "sk-selftest-0123456789";
            string stored = SecretStore.Protect(secret);
            secretOk = SecretStore.IsProtected(stored)
                    && stored.StartsWith(SecretStore.Prefix, StringComparison.Ordinal)
                    && SecretStore.TryUnprotect(stored, out var plain)
                    && plain == secret
                    && !SecretStore.IsProtected(secret);
        }
        catch (Exception ex)
        {
            Log.Error("Key 加密自检异常", ex);
        }
        return secretOk;
    }

    /// <summary>渲染上下文：共享变量表按帧复用、宿主透传与模块刷新节奏。</summary>
    static bool RunRenderContextChecks()
    {
        bool renderOk = false;
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
                if (module is null) { cadenceOk = false; continue; }
                if (module.RefreshIntervalMs(new CardSettings { Kind = module.Kind }) < 0) cadenceOk = false;
            }
            renderOk = sharedUsed && hostPassed && cadenceOk;
        }
        catch (Exception ex)
        {
            Log.Error("渲染上下文自检异常", ex);
        }
        return renderOk;
    }

    /// <summary>集中输出逻辑自检结论；输出顺序与拆分前完全一致。</summary>
    static void ReportUnitChecks(StringBuilder sb, UnitCheckResults results)
    {
        Fix(sb, "逻辑自检", string.Format(
            "模板={0} 未知占位符={1} 折叠={2} 阈值内展开={3} 配置读写={4} 卡片模块={5} 键名={6} 按键长度={7} 超长截断={8} 状态存储={9} 渲染节流={10} 毛玻璃策略={11}",
            results.TemplateOk ? "OK" : "FAIL",
            results.UnknownOk ? "OK" : "FAIL",
            results.CollapseOk ? "OK" : "FAIL",
            results.ThresholdOk ? "OK" : "FAIL",
            results.ConfigOk ? "OK" : "FAIL",
            results.CardOk ? "OK" : "FAIL",
            results.KeyNameOk ? "OK" : "FAIL",
            results.KeyLimitOk ? "OK" : "FAIL",
            results.KeyOverflowOk ? "OK" : "FAIL",
            results.StateStoreOk ? "OK" : "FAIL",
            results.RenderOk ? "OK" : "FAIL",
            results.GlassPolicyOk ? "OK" : "FAIL"));

        Fix(sb, "外观令牌", results.ThemeOk ? "OK（派生值 = 旧 alpha 常量，非法值回落而不是变白）" : "FAIL");
        Fix(sb, "配置修复", results.ConfigRepairOk
            ? "OK（null 判空 / 损坏标记 / 旧 Schema 按损坏 / 未来版本只读 / 未知 Kind 保留 / 状态 .bak 回退）" : "FAIL");
        Fix(sb, "主题文件", results.ThemeFileOk ? "OK（导出 → 解析 → 套用 → 再导出一致，数字透明度按前景派生）" : "FAIL");
        Fix(sb, "主题文件校验", results.ThemeRejectOk ? "OK（高版本 / 坏颜色 / 越界 / 缺 appearance 均拒绝，坏文件不动当前设置）" : "FAIL");
        Fix(sb, "内置主题", results.ThemeCatalogOk ? "OK（已编进程序集并全部解析）" : "FAIL");
        Fix(sb, "设置副本", results.CloneGuardOk ? "OK（克隆失败返回 null + 原因，不退回默认值）" : "FAIL");
        Fix(sb, "交互卡唯一", results.SingleInteractiveOk ? "OK（默认一张；第二张会被设置页 / 校验拦住）" : "FAIL");
        Fix(sb, "共享控件", results.WidgetsOk ? "OK（CardList 空态/计数/选中；CardStatus 忙碌/进度/错误）" : "FAIL");
        Fix(sb, "共享控件（交互）", results.Widgets2Ok ? "OK（Header 按钮可点；Input 提交；DropOverlay 显隐）" : "FAIL");
        Fix(sb, "表单控件", results.FormControlsOk ? "OK（Slider / Toggle / Select / Number / Color / Segmented / Form）" : "FAIL");
        Fix(sb, "数据控件", results.DataControlsOk ? "OK（Table 排序 / 选中 / 空态；Badge / Tag / Metric / Gauge / Ring）" : "FAIL");
        Fix(sb, "图表 / 树 / 时间线", results.ChartControlsOk ? "OK（Sparkline / Line / Bar / Tree / Timeline 离屏渲染）" : "FAIL");
        Fix(sb, "浮层 / 交互控件", results.OverlayControlsOk ? "OK（Modal / Dialog / Popover / Tooltip / ContextMenu / Toast）" : "FAIL");
        Fix(sb, "图标字体", results.IconFontOk ? "OK（Lucide 码位 / 查询 / CardIcon 渲染）" : "FAIL");
        Fix(sb, "渲染上下文", results.ContextHostOk ? "OK（富内容创建上下文透传 ICardHost）" : "FAIL");
        Fix(sb, "Key 加密", results.SecretOk ? "OK（DPAPI 往返；明文不落盘）" : "FAIL");
        Fix(sb, "卡片注册", results.CardOk ? "OK（6 个内置 Kind 齐全；契约在 Contracts；未知 Kind 占位）" : "FAIL");
        Fix(sb, "Shell / 拖出", results.ShellOk ? "OK（图标缓存一致；空输入安全）" : "FAIL");
    }

    /// <summary>RunUnitChecks 的领域结果。各领域只返回布尔，汇总处只加一次失败，保持拆分前语义。</summary>
    sealed class UnitCheckResults
    {
        public bool TemplateOk;
        public bool UnknownOk;
        public bool CollapseOk;
        public bool ThresholdOk;
        public bool ConfigOk;
        public bool ConfigRepairOk;
        public bool CardOk;
        public bool KeyNameOk;
        public bool KeyLimitOk;
        public bool KeyOverflowOk;
        public bool SecretOk;
        public bool StateStoreOk;
        public bool RenderOk;
        public bool ThemeOk;
        public bool GlassPolicyOk;
        public bool ThemeFileOk;
        public bool ThemeRejectOk;
        public bool ThemeCatalogOk;
        public bool CloneGuardOk;
        public bool SingleInteractiveOk;
        public bool WidgetsOk;
        public bool Widgets2Ok;
        public bool FormControlsOk;
        public bool DataControlsOk;
        public bool ChartControlsOk;
        public bool OverlayControlsOk;
        public bool IconFontOk;
        public bool ContextHostOk;
        public bool ShellOk;
        public bool PluginSelfTestOk;

        /// <summary>任一领域失败即整体失败（对应拆分前末尾那一次 failures++）。</summary>
        public bool AllPassed() => TemplateOk && UnknownOk && CollapseOk && ThresholdOk && ConfigOk && ConfigRepairOk && CardOk &&
                   KeyNameOk && KeyLimitOk && KeyOverflowOk && StateStoreOk && RenderOk &&
                   GlassPolicyOk && ThemeOk && ThemeFileOk && ThemeRejectOk && ThemeCatalogOk && CloneGuardOk &&
                   SingleInteractiveOk && WidgetsOk && Widgets2Ok && FormControlsOk && DataControlsOk &&
                   ChartControlsOk && OverlayControlsOk && IconFontOk && ContextHostOk && ShellOk && SecretOk &&
                   PluginSelfTestOk;
    }


    static bool RegisterIfSet(HotkeyManager hotkeys, string action, string combination)
    {
        if (string.IsNullOrWhiteSpace(combination)) return true;   // 未设置也算正常
        return hotkeys.Register(action, combination);
    }

    static string CommandHotkey(Settings settings)
    {
        if (settings is null || settings.Cards is null) return "";
        foreach (CardSettings card in settings.Cards)
            if (card is not null && string.Equals(card.Kind, "command", StringComparison.OrdinalIgnoreCase))
                return card.Hotkey ?? "";
        return "";
    }

    static string Show(string combination, bool ok)
    {
        if (string.IsNullOrWhiteSpace(combination)) return "(未设置)=OFF";
        return $"{combination}={(ok ? "OK" : "占用")}";
    }

    static void Fix(StringBuilder sb, string key, string value) => sb.Append(key).Append(": ").Append(value).AppendLine();

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
        readonly Dictionary<string, string> _state = new();

        public CardSettings Config { get; set; }
        public string StateDirectory => "";

        public string LastWrittenState { get; private set; }

        public string ReadState(string fileName) => _state.TryGetValue(fileName ?? "", out var value) ? value : null;

        public bool WriteState(string fileName, string text)
        {
            LastWrittenState = text;
            _state[fileName ?? ""] = text ?? "";
            return true;
        }

        public bool DeleteState(string fileName) => _state.Remove(fileName ?? "");

        public void RequestRender() { }
        public void RequestLayout() { }
        public bool SaveSettings() => true;
        public void RefreshContentSignature() { }
        public ICardShellService Shell => null;
    }
}
