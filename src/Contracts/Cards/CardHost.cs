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

        /// <summary>
        /// 把当前整棵设置树写回磁盘（卡片运行期改了 <see cref="Config"/> 的 Extras 时用，
        /// 例如对话卡切换单次 / 轮询）。返回是否写成功；配置只读 / 写失败都会返回 false。
        /// </summary>
        bool SaveSettings();

        /// <summary>宿主 Shell 能力（剪贴板 / 图标 / 拖出 / 打开文件）。</summary>
        ICardShellService Shell { get; }
    }

    /// <summary>
    /// 模块想在卡片创建 / 销毁时拿到 <see cref="ICardHost"/>。宿主在「建卡」和「应用设置」时调用
    /// <see cref="Attach"/>，卡片被删掉 / 程序退出时调用 <see cref="Detach"/>。
    ///
    /// 这是 apiVersion 2 的**稳定扩展点**，不是死代码：内置卡没有跨次宿主状态，所以都不实现它；
    /// 第三方插件可以按需实现。但模块是单例、Attach 不携带卡片 / 内容标识，因此**不能把 host
    /// 存进模块字段**（同种卡有多张时会串）。新插件应优先在 <c>CreateContent</c> 里用
    /// <see cref="CardRenderContext.Host"/> 把 host 交给当前这张卡的内容控件；只有在
    /// 「该种卡最多一张」或不需要把 host 与具体卡片关联时才适合实现本接口。
    /// </summary>
    public interface ICardHostAware
    {
        /// <summary>卡片创建 / 应用设置时调用。不要长期保存 host，见接口说明。</summary>
        void Attach(ICardHost host);

        /// <summary>删卡 / 退出时与 <see cref="Attach"/> 成对调用。</summary>
        void Detach(ICardHost host);
    }
}
