using System;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 毛玻璃运行时的当前实现状态（单一数据源）：窗口、设置窗都读这一份，不再各自按设置重算。
    ///
    /// 为什么需要它（AGENTS.md §10 推迟项的落地）：以前「实际在用哪种玻璃」散在三处 ——
    /// <see cref="GlassPolicy.Resolve"/> 算一遍、<see cref="OverlayWindow"/> 拖动补帧再按设置算一遍、
    /// 设置窗只看下拉框（用户选的是 DesktopFeed，但实际可能已经退回兼容取样）。现在由
    /// <see cref="GlassRuntime"/> 独占一份状态，决策失败 / 降级也写在这里，其余位置只读。
    ///
    /// 字段只读：状态变化一律换新实例（<see cref="GlassRuntime"/> 的 SetState），
    /// 避免别处读到写了一半的对象。
    /// </summary>
    internal sealed class GlassRuntimeState
    {
        /// <summary>这一帧由谁提供模糊（None = 毛玻璃没开 / 窗口已关闭）。</summary>
        public readonly GlassBackdropKind Kind;

        /// <summary>为什么是当前实现（降级原因等）；空串 = 没发生降级。</summary>
        public readonly string Reason;

        /// <summary>最近一次底板应用失败的原因；成功时清空。日志排查与后续状态展示用。</summary>
        public readonly string LastError;

        /// <summary>系统亚克力底板此刻可用（接口失效 / 创建失败后为 false）。</summary>
        public readonly bool SystemAvailable;

        /// <summary>桌面取样底板此刻可用（远程桌面 / 连续取样全黑后为 false）。</summary>
        public readonly bool DesktopAvailable;

        public GlassRuntimeState(GlassBackdropKind kind, string reason, string lastError,
                                 bool systemAvailable, bool desktopAvailable)
        {
            Kind = kind;
            Reason = reason ?? "";
            LastError = lastError ?? "";
            SystemAvailable = systemAvailable;
            DesktopAvailable = desktopAvailable;
        }

        /// <summary>
        /// 一句中文描述当前实现；Reason 非空时追加原因（设置窗「当前实际：…」直接用这个）。
        /// 文案是用户可见的状态说明，不要在别处再拼一套。
        /// </summary>
        public string Describe()
        {
            string text;
            switch (Kind)
            {
                case GlassBackdropKind.SystemAcrylic:
                    text = "系统亚克力（accent 底板）";
                    break;

                case GlassBackdropKind.DesktopAcrylic:
                    text = "桌面取样（截屏 + 模糊）";
                    break;

                case GlassBackdropKind.CompatCapture:
                    text = "兼容取样（CPU 模糊）";
                    break;

                default:
                    text = "未启用";
                    break;
            }

            if (!string.IsNullOrEmpty(Reason)) text += "：" + Reason;
            return text;
        }
    }
}
