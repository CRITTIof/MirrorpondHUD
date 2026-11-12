using System;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 卡片动效的集中参数。2026-11 动效批次（A 方案：克制专业）：
/// · 拖动时其他卡片同位移刚性跟随；
/// · 松手后的补位 / 自动对齐翻边 180ms ease-out；
/// · 呼出 / 消失 160ms 淡入淡出 + 8px 位移；
/// · 截图 / 自检模式关闭动效，避免离屏验收抓到中间帧。
/// </summary>
internal static class CardMotion
{
    public const int FrameIntervalMs = 15;

    static bool _captureDisabled;
    static bool _userEnabled = true;
    static string _profile = "Current";
    static bool _loopMotion;
    static bool _overallMotion = true;
    static string _slide = "None";

    /// <summary>
    /// 截图 / --selftest 等离屏模式会关闭；正常运行时由设置里的「减少动效」控制。
    /// Off 档位等价于瞬时，但不会把用户的「减少动效」开关改掉。
    /// </summary>
    public static bool Enabled => !_captureDisabled && _userEnabled && _profile != "Off";

    /// <summary>循环动效只在动效可用且用户显式打开时运行。</summary>
    public static bool LoopEnabled => Enabled && _loopMotion;

    /// <summary>“显示 / 隐藏全部”是否播放整体动效；仍受减少动效与 Off 档一票否决。</summary>
    public static bool OverallMotion => Enabled && _overallMotion;

    /// <summary>
    /// 粒子聚字是否生效：文字动效选了粒子，且动效没有被减少动效 / Off 档 / 截图模式关掉。
    /// 启动打字动画与粒子聚字互斥，OverlayIntro 先用这里判断要不要整段跳过。
    /// </summary>
    public static bool ParticleTextEnabled(Settings settings) =>
        settings is not null && Enabled
        && string.Equals(Settings.NormalizeTextEffect(settings.Style.TextEffect),
            "Particle", StringComparison.Ordinal);

    /// <summary>卡片滑入滑出方向：None / Left / Right / Follow；Follow 取整叠最终左 / 右对齐。</summary>
    public static string SlideDirection => _slide;

    public static string Profile => _profile;

    /// <summary>整叠补位 / 自动对齐翻边。Current=180，Cut=90，Industrial=120，Off=0。</summary>
    public static int LayoutDurationMs
    {
        get
        {
            switch (_profile)
            {
                case "Off": return 0;
                case "Cut": return 90;
                case "Industrial": return 120;
                default: return 180;
            }
        }
    }

    public static int ShowDurationMs
    {
        get
        {
            switch (_profile)
            {
                case "Off": return 0;
                case "Cut": return 80;
                case "Industrial": return 110;
                default: return 160;
            }
        }
    }

    public static int HideDurationMs
    {
        get
        {
            switch (_profile)
            {
                case "Off": return 0;
                case "Cut": return 80;
                case "Industrial": return 110;
                default: return 160;
            }
        }
    }

    public static double ShowOffset
    {
        get
        {
            switch (_profile)
            {
                case "Off": return 0;
                case "Cut": return 0;
                case "Industrial": return 2;
                default: return 8;
            }
        }
    }

    public static double HideOffset
    {
        get
        {
            switch (_profile)
            {
                case "Off": return 0;
                case "Cut": return 0;
                case "Industrial": return 2;
                default: return 8;
            }
        }
    }

    /// <summary>截图 / 自检一次关掉，之后设置加载 / 应用也不能重新打开。</summary>
    public static void SuppressForCapture() => _captureDisabled = true;

    /// <summary>按当前设置决定动效档位与是否循环；减少动效仍然一票否决。</summary>
    public static void ApplySettings(Settings settings)
    {
        _userEnabled = settings is null || !settings.Behavior.ReduceMotion;
        _profile = settings is null ? "Current" : Settings.NormalizeMotionProfile(settings.Style.MotionProfile);
        _loopMotion = settings is not null && settings.Style.LoopEffect != "None";
        _overallMotion = settings is null || settings.Behavior.OverallMotion;
        _slide = settings is null ? "None" : Settings.NormalizeCardSlide(settings.Behavior.CardSlide);
    }

    /// <summary>三档 ease-out；Cut 用线性，接近工业界面的“直接切入”。</summary>
    public static double Ease(double t)
    {
        if (_profile == "Cut") return Math.Max(0.0, Math.Min(1.0, t));
        return EaseOut(t);
    }

    /// <summary>三次 ease-out：起步快、收尾稳，适合卡片补位这种短距离移动。</summary>
    public static double EaseOut(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double inv = 1 - t;
        return 1 - inv * inv * inv;
    }
}
