using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 一张卡片拿到的宿主服务：自己的状态目录、请求重画 / 重新布局。
    ///
    /// 模块是单例，所以宿主服务是**每张卡片一份**的（由 <see cref="ICardHostAware"/> 在卡片创建 / 销毁时下发），
    /// 模块不要把 host 存进单例字段，而是用在当前这次的 <c>CreateContent</c> 或自己的内容控件里。
    /// </summary>
    public interface ICardHost
    {
        /// <summary>这张卡片的配置（配置变化时会换成新对象，请不要长期缓存）。</summary>
        CardSettings Config { get; }

        /// <summary>这张卡片自己的状态目录（不存在时返回路径，不创建）。</summary>
        string StateDirectory { get; }

        /// <summary>读状态文件；不存在 / 名字非法 / 读失败都返回 null。</summary>
        string ReadState(string fileName);

        /// <summary>原子写状态文件（先写 .tmp 再替换）。</summary>
        bool WriteState(string fileName, string text);

        /// <summary>删掉一个状态文件（不存在也算成功）。</summary>
        bool DeleteState(string fileName);

        /// <summary>请求宿主重画一次（模块的数据变了，但采样快照没变）。</summary>
        void RequestRender();

        /// <summary>请求宿主重新布局（内容尺寸变了，例如列表变长）。</summary>
        void RequestLayout();
    }

    /// <summary>
    /// 模块想在卡片创建 / 销毁时拿到 <see cref="ICardHost"/>。宿主在「建卡」和「应用设置」时调用
    /// <see cref="Attach"/>，卡片被删掉 / 程序退出时调用 <see cref="Detach"/>。
    ///
    /// 约定：模块是单例，**不能把 host 存进字段**；需要跨次使用的状态请放进内容控件或状态文件。
    /// </summary>
    public interface ICardHostAware
    {
        void Attach(ICardHost host);

        void Detach(ICardHost host);
    }
}
