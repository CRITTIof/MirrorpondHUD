using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace UiKitGallery
{
    /// <summary>
    /// UiKit 公共控件画廊（示例插件，不是内置卡）。
    ///
    /// 目的：给「想用共享控件但不想读 UiKit 源码」的插件作者一个可以直接编译、安装、照抄的样例。
    /// 卡片本身没有业务逻辑：<see cref="CreateContent"/> 只返回一个可滚动的 <see cref="GalleryView"/>，
    /// 里面按中文分区实例化 UiKit 的公共控件；颜色与字号全部来自卡片当前外观设置
    /// （StyleSettings → ThemeResolver 令牌），交互只改内存里的演示状态。
    ///
    /// 安装方式见同目录 README.md；不要把它当内置卡加进宿主工程——它只通过 plugins/cards 加载。
    /// </summary>
    public sealed class GalleryCard : RichCardModuleBase
    {
        /// <summary>稳定 Kind：写进设置文件 Cards[].Kind，也是「添加卡片」后保存的值。</summary>
        public override string Kind { get { return "sample.uikitgallery"; } }

        /// <summary>「设置 → 内容 → 添加卡片」里显示的名字。</summary>
        public override string DisplayName { get { return "UiKit 控件画廊"; } }

        /// <summary>设置页图标沿用宿主约定的 Reicon 码位，这里用「内容」页签图标。</summary>
        public override string Icon { get { return Icons.TabContent; } }

        /// <summary>画廊里有按钮 / 输入框 / 滑块，必须让宿主关闭窗口的点击穿透。</summary>
        public override bool NeedsMouseInput { get { return true; } }

        /// <summary>示例卡默认常驻，添加后立刻能看到。</summary>
        public override bool DefaultResident { get { return true; } }

        /// <summary>排在大部分内置卡之后，避免干扰日常使用的卡片菜单。</summary>
        public override int Order { get { return 90; } }

        /// <summary>每次卡片配置变化时宿主会重建一次内容；这里只传设置，不保存宿主对象。</summary>
        public override FrameworkElement CreateContent(CardRenderContext context)
        {
            Settings settings = context == null ? null : context.Settings;
            return new GalleryView(settings);
        }
    }
}
