using System.Windows;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 能画出任意 WPF 内容的卡片模块：图片、位图、动图、视频、以后的自定义面板都走这条契约。
    ///
    /// 文字卡片只需要 <see cref="ICardModule.Render"/> 返回字符串；富内容卡片改成返回一个
    /// <see cref="FrameworkElement"/>，宿主负责把它放进卡片窗口、跟着设置重建、退出时释放。
    /// 模块本身不碰窗口、不碰叠层位置，所以新增一种富内容卡片还是「只加一个类」。
    /// </summary>
    public interface IRichCardModule : ICardModule
    {
        /// <summary>为一张卡片创建内容。每次卡片配置变化时宿主会重建一次。</summary>
        FrameworkElement CreateContent(CardRenderContext context);

        /// <summary>配置没变、只想刷新数据时调用（默认什么都不做）。</summary>
        void UpdateContent(FrameworkElement content, CardRenderContext context);

        /// <summary>卡片被显示 / 隐藏时调用：视频、动图可以在这里开始或暂停。</summary>
        void OnVisibilityChanged(FrameworkElement content, bool visible);

        /// <summary>
        /// 卡片重新置顶后调用。视频用独立的子窗口显示，卡片把自己抬到最上层时
        /// 子窗口会被压在下面，需要在这里再把子窗口摆回卡片上方。
        /// </summary>
        void OnZOrderChanged(FrameworkElement content);
    }

    /// <summary>富内容卡片的基类：文字模板不适用，Render 固定返回空串。</summary>
    public abstract class RichCardModuleBase : CardModuleBase, IRichCardModule
    {
        public override bool SupportsContent { get { return false; } }

        public override string Render(CardRenderContext context) { return ""; }

        public abstract FrameworkElement CreateContent(CardRenderContext context);

        public virtual void UpdateContent(FrameworkElement content, CardRenderContext context) { }

        public virtual void OnVisibilityChanged(FrameworkElement content, bool visible) { }

        public virtual void OnZOrderChanged(FrameworkElement content) { }
    }
}
