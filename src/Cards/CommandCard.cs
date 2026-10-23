using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 命令行卡：内容由宿主（OverlayController）绘制提示符、历史与输入，
    /// 这里只声明「独占键盘输入」和默认热键，Render 返回空串。
    /// </summary>
    public sealed class CommandCard : CardModuleBase
    {
        public override string Kind { get { return "command"; } }
        public override string DisplayName { get { return "命令行"; } }
        public override string Icon { get { return Icons.Terminal; } }
        public override bool SupportsContent { get { return false; } }
        public override bool IsInteractive { get { return true; } }
        public override bool DefaultResident { get { return false; } }
        public override string DefaultHotkey { get { return "F9"; } }
        public override int Order { get { return 4; } }

        public override string Render(CardRenderContext context)
        {
            return "";
        }
    }
}
