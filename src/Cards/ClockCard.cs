using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>时钟卡：时间 / 日期 / 星期 / 运行时长。</summary>
    public sealed class ClockCard : CardModuleBase
    {
        public override string Kind { get { return "clock"; } }
        public override string DisplayName { get { return "时钟"; } }
        public override string Icon { get { return Icons.Web; } }
        public override string DefaultContent { get { return Settings.DefaultClockContent; } }
        public override int Order { get { return 1; } }

        public override string Render(CardRenderContext context)
        {
            return context.RenderOrDefault(DefaultContent);
        }
    }
}
