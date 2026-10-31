using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 一张卡片在宿主里的运行时状态：配置、模块、窗口、富内容实例与签名。
    /// 从 <see cref="OverlayController"/> 里抽出来，布局协调器与启动动画可以直接使用，
    /// 不再把模型定义埋在控制器内部。
    /// </summary>
    internal sealed class CardSlot
    {
        public CardSettings Config;
        public ICardModule Module;

        /// <summary>模块同时是富内容卡片时（图片 / 视频 / CLI 面板）非空。</summary>
        public IRichCardModule Rich;

        public Settings Dialog;
        public OverlayWindow Window;
        public bool Summoned;
        public bool Shown;
        public double BodyHeight;

        /// <summary>富内容控件的当前实例（隐藏时保留，设置变化时重建）。</summary>
        public FrameworkElement Content;

        /// <summary>
        /// 建 / 刷新这个内容时用的**内容**签名（种类、模板、本卡设置）。
        /// 应用设置时拿它和新签名比较：一样就只刷新数据，不一样才重建控件。
        /// </summary>
        public string ContentSignature;

        /// <summary>**样式**签名（外观 / CLI 尺寸 / 圆角 / 投影）；只变它时优先就地换肤。</summary>
        public string StyleSignature;

        /// <summary>这张卡片拿到的宿主服务（状态目录、请求重画 / 布局）。</summary>
        public CardHost Host;

        /// <summary>上次真正重画的时间；和模块的刷新间隔配合做节流。</summary>
        public System.DateTime LastRenderUtc;
    }

    /// <summary>
    /// 每张卡片一份的宿主服务实现：把状态读写落到 <c>cards\&lt;id&gt;\</c>，
    /// 把「重画 / 重新布局」转给控制器。模块是单例，所以这只是下发对象，不长期持有。
    /// </summary>
    internal sealed class CardHost : ICardHost
    {
        readonly OverlayController _owner;

        public CardHost(OverlayController owner, CardSettings config)
        {
            _owner = owner;
            Config = config;
        }

        /// <summary>卡片配置；应用设置时由控制器换成新对象。</summary>
        public CardSettings Config { get; set; }

        public string StateDirectory
        {
            get { return CardStateStore.DirectoryFor(CardId); }
        }

        public string ReadState(string fileName)
        {
            return CardStateStore.Read(CardId, fileName);
        }

        public bool WriteState(string fileName, string text)
        {
            return CardStateStore.Write(CardId, fileName, text);
        }

        public bool DeleteState(string fileName)
        {
            return CardStateStore.Delete(CardId, fileName);
        }

        public void RequestRender()
        {
            _owner.RequestRenderFromModule();
        }

        public void RequestLayout()
        {
            _owner.RequestLayoutFromModule();
        }

        string CardId { get { return Config == null ? "" : Config.Id; } }
    }
}
