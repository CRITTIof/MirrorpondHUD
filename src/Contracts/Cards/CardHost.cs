using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 一张卡片拿到的宿主服务：自己的状态目录、请求重画 / 重新布局。
///
/// 模块是单例，宿主服务是**每张卡片一份**的；新插件应通过
/// <see cref="CardRenderContext.Host"/> 在创建内容时拿到它，并交给当前这张卡的内容控件，
/// 不要把 host 存进模块字段（同种卡有多张时会串）。
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

    /// <summary>
    /// 卡片绕过设置窗直接改了运行期字段（如 Chat 模式）并落盘后调用：
    /// 让宿主把这张卡的“内容签名”刷成当前配置，下一次应用设置不会因此重建富内容。
    /// 没有该卡 / 视图已废弃时是空操作。
    /// </summary>
    void RefreshContentSignature();

    /// <summary>宿主 Shell 能力（剪贴板 / 图标 / 拖出 / 打开文件）。</summary>
    ICardShellService Shell { get; }
}
