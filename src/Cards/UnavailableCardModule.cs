using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 配置里引用了某个 Kind，但对应插件没有安装 / 加载失败时的占位模块。
    ///
    /// 它由 <see cref="CardRegistry.Get"/> 按未知 Kind 动态创建，**不进入 Modules 列表**，
    /// 所以不会出现在「添加卡片」菜单里；配置里的 Kind / Content / Extras 原样保留，
    /// 插件重新出现后下一次启动会自动恢复成真正的卡片。
    /// </summary>
    internal sealed class UnavailableCardModule : CardModuleBase
    {
        readonly string _kind;

        public UnavailableCardModule(string kind)
        {
            _kind = string.IsNullOrWhiteSpace(kind) ? "(未知)" : kind.Trim();
        }

        public override string Kind { get { return _kind; } }
        public override string DisplayName { get { return "插件不可用"; } }
        public override string Icon { get { return Icons.Txt; } }
        public override string DefaultContent { get { return Message; } }
        public override bool SupportsContent { get { return false; } }
        public override bool IsInteractive { get { return false; } }
        public override int Order { get { return int.MaxValue; } }

        string Message
        {
            get { return "插件不可用：" + _kind + "\n重新安装插件并重启 MirrorpondHUD"; }
        }

        public override int RefreshIntervalMs(CardSettings config) { return 1000; }

        public override string Render(CardRenderContext context) { return Message; }
    }
}
