using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>系统信息卡：CPU / 内存 / GPU / 网络 / 磁盘，默认内容带 ReiconFont 图标。</summary>
    public sealed class MetricsCard : CardModuleBase
    {
        public override string Kind { get { return "metrics"; } }
        public override string DisplayName { get { return "系统信息"; } }
        public override string Icon { get { return Icons.Cpu; } }
        public override string DefaultContent { get { return Settings.DefaultMetricsContent; } }
        public override int Order { get { return 0; } }

        public override string Render(CardRenderContext context)
        {
            return context.RenderOrDefault(DefaultContent);
        }
    }
}
