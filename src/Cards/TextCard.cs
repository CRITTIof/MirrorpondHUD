using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>文本卡：自定义文字与信息，支持全部占位符。</summary>
    public sealed class TextCard : CardModuleBase
    {
        public override string Kind { get { return "text"; } }
        public override string DisplayName { get { return "文本信息"; } }
        public override string Icon { get { return Icons.Txt; } }
        public override string DefaultContent { get { return Settings.DefaultTextContent; } }
        public override int Order { get { return 3; } }

        public override string Render(CardRenderContext context)
        {
            return context.RenderOrDefault(DefaultContent);
        }
    }
}
