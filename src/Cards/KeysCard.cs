using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>按键卡：最近按键，用 ReiconFont 的键帽字形显示。</summary>
    public sealed class KeysCard : CardModuleBase
    {
        public override string Kind { get { return "keys"; } }
        public override string DisplayName { get { return "按键"; } }
        public override string Icon { get { return Icons.TabHotkey; } }
        public override string DefaultContent { get { return Settings.DefaultKeysContent; } }
        public override int Order { get { return 2; } }

        public override string Render(CardRenderContext context)
        {
            return context.RenderOrDefault(DefaultContent);
        }
    }
}
