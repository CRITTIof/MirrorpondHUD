using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 一种卡片 = 一个模块。新增功能只要加一个实现这个接口的 public 类，
    /// 构造时由 <see cref="CardRegistry"/> 反射发现，不用改宿主、工程文件或设置页代码。
    /// </summary>
    public interface ICardModule
    {
        /// <summary>稳定的种类标识，写进配置文件的 Kind 字段。</summary>
        string Kind { get; }

        /// <summary>设置页里显示的名字（同种卡片可以有多个，名字可重名）。</summary>
        string DisplayName { get; }

        /// <summary>ReiconFont 图标字符（U+E100 / U+E200 / U+E300 区）。</summary>
        string Icon { get; }

        /// <summary>内容模板为空时使用的默认内容。</summary>
        string DefaultContent { get; }

        /// <summary>是否允许编辑内容模板（命令行卡片是内置界面，不需要）。</summary>
        bool SupportsContent { get; }

        /// <summary>是否是独占键盘输入的卡片（命令行）。</summary>
        bool IsInteractive { get; }

        /// <summary>
        /// 卡片内容自己处理鼠标拖动（画笔、卡片内拖拽排序等）。true 时按住卡片不会移动整叠，
        /// 鼠标事件全部交给内容；画板这类卡片靠它避免「一按就拖卡」。
        /// </summary>
        bool HandlesOwnDrag { get; }

        /// <summary>新卡片的默认「常驻」状态。</summary>
        bool DefaultResident { get; }

        /// <summary>新卡片的默认呼出热键；空 = 不预置。</summary>
        string DefaultHotkey { get; }

        /// <summary>「添加卡片」菜单里的排序，小的在前。</summary>
        int Order { get; }

        /// <summary>
        /// 这张卡片最快多久重画一次（毫秒）；0 = 每个渲染 tick（120 ms）。
        /// 只影响宿主逐帧刷新的文字卡片：时钟这种每秒变一次的不必按 8 帧/秒重画。
        /// </summary>
        int RefreshIntervalMs(CardSettings config);

        /// <summary>把卡片内容渲染成一行文本（多行用 \n）。</summary>
        string Render(CardRenderContext context);
    }

    /// <summary>渲染一张卡片时需要的共享数据。变量表按需构建，同一帧内的多张卡片可以复用。</summary>
    public sealed class CardRenderContext
    {
        Dictionary<string, object> _variables;
        readonly IDictionary<string, object> _sharedVariables;

        public CardRenderContext(Settings settings, MetricsSnapshot snapshot, KeyHistory keys, CardSettings config)
            : this(settings, snapshot, keys, config, null, null)
        {
        }

        /// <summary>
        /// 用一帧里已经建好的变量表（宿主每帧只建一次）。传进来的表**按只读对待**，
        /// 模块不要去改它——同一帧里的所有卡片共用这一份。
        /// </summary>
        public CardRenderContext(Settings settings, MetricsSnapshot snapshot, KeyHistory keys,
                                 CardSettings config, IDictionary<string, object> sharedVariables)
            : this(settings, snapshot, keys, config, sharedVariables, null)
        {
        }

        /// <summary>
        /// 富内容创建 / 刷新时，宿主会把这张卡片的 <see cref="ICardHost"/> 一起传进来；
        /// 内容控件可以保存它，用来 <see cref="ICardHost.RequestLayout"/> 或读写自己的状态文件。
        /// 文字渲染与截图路径没有 host，<see cref="Host"/> 为 null。
        /// </summary>
        public CardRenderContext(Settings settings, MetricsSnapshot snapshot, KeyHistory keys,
                                 CardSettings config, IDictionary<string, object> sharedVariables, ICardHost host)
        {
            Settings = settings ?? new Settings();
            Snapshot = snapshot ?? new MetricsSnapshot();
            Keys = keys;
            Config = config;
            _sharedVariables = sharedVariables;
            Host = host;
        }

        public Settings Settings { get; private set; }
        public MetricsSnapshot Snapshot { get; private set; }
        public KeyHistory Keys { get; private set; }
        public CardSettings Config { get; private set; }

        /// <summary>这张卡片的宿主服务（可能为 null：文字渲染 / 截图 / 单测）。</summary>
        public ICardHost Host { get; private set; }

        public IDictionary<string, object> Variables
        {
            get
            {
                if (_sharedVariables != null) return _sharedVariables;
                if (_variables == null)
                    _variables = Core.Variables.Build(Snapshot, Settings, Keys);
                return _variables;
            }
        }

        public string Render(string template)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return TemplateEngine.Render(template, Variables);
        }

        /// <summary>内容模板为空时回退到卡片默认内容。</summary>
        public string RenderOrDefault(string defaultContent)
        {
            string content = Config == null ? null : Config.Content;
            return Render(string.IsNullOrWhiteSpace(content) ? defaultContent : content);
        }
    }

    /// <summary>
    /// 一种卡片可以对外提供「这种卡片通用」的热键动作（作用于全部同类卡片）。
    /// 例如媒体卡的播放 / 暂停、前进、后退、加速、减速。
    /// </summary>
    public sealed class CardHotkeyAction
    {
        public CardHotkeyAction(string action, string label, string defaultHotkey)
        {
            Action = action ?? "";
            Label = label ?? action ?? "";
            DefaultHotkey = defaultHotkey ?? "";
        }

        /// <summary>稳定动作名，写进配置并用作热键动作名（如 media.play-pause）。</summary>
        public string Action { get; private set; }
        /// <summary>设置页显示的名字。</summary>
        public string Label { get; private set; }
        /// <summary>新配置的默认热键；空 = 默认不启用。</summary>
        public string DefaultHotkey { get; private set; }
    }

    /// <summary>卡片模块提供一组通用热键（设置页「热键」里按卡片分组显示）。</summary>
    public interface ICardHotkeySource
    {
        IList<CardHotkeyAction> HotkeyActions { get; }
    }

    /// <summary>卡片模块执行通用热键（宿主把动作转发给每张同类卡片的内容）。</summary>
    public interface ICardHotkeyTarget
    {
        void ExecuteHotkey(string action, FrameworkElement content);
    }

    /// <summary>
    /// 卡片接受拖放（例如中转站接住从资源管理器拖进来的文件）。
    /// 模块只声明「收哪些、收到后做什么」，窗口层的 AllowDrop / DragOver / Drop 由宿主统一注册。
    ///
    /// 注意：锁定（点击穿透）状态下窗口收不到拖放——这是「锁定 = 不挡鼠标」的既有语义；
    /// 需要拖入时先解锁。
    /// </summary>
    public interface ICardDropTarget
    {
        /// <summary>这份拖放数据能不能接（决定鼠标指针是「复制」还是「禁止」）。</summary>
        bool CanDrop(IDataObject data);

        /// <summary>接住这份数据；<paramref name="content"/> 是这张卡片的富内容（可能为 null）。</summary>
        void Drop(IDataObject data, FrameworkElement content);
    }

    /// <summary>
    /// 交互卡片（命令行；将来的画板 / 中转站）的呼出 / 收起回调。
    /// 宿主只负责显示窗口、抢焦点；「进来之后怎么初始化、日志里写什么」由模块自己解释自己的设置，
    /// 宿主不再认识 <c>CommandSettings</c> 这种具体类型。
    /// </summary>
    public interface ICardActivation
    {
        /// <summary>卡片被呼出、窗口与焦点就绪后调用；返回一句可写入日志的摘要（null = 不记录）。</summary>
        string OnActivated(CardSettings config);

        /// <summary>卡片收起前调用；模块可以在这里取消还在跑的任务（面板自己的收尾仍走 ICliFocusTarget）。</summary>
        void OnDeactivated(CardSettings config);
    }

    /// <summary>
    /// 拖入 / 拖出卡片窗口的反馈（不是每次 DragOver）：卡片据此显示 / 隐藏拖放高亮。
    /// 与 <see cref="ICardDropTarget"/> 搭配使用；宿主在 DragEnter / DragLeave / Drop 时各调一次。
    /// </summary>
    public interface ICardDropFeedback
    {
        void OnDropStateChanged(bool over, IDataObject data);
    }

    /// <summary>卡片模块的公共基类：只提供默认实现，子类覆盖需要的部分。</summary>
    public abstract class CardModuleBase : ICardModule
    {
        public abstract string Kind { get; }
        public abstract string DisplayName { get; }
        public abstract string Icon { get; }

        public virtual string DefaultContent { get { return ""; } }
        public virtual bool SupportsContent { get { return true; } }
        public virtual bool IsInteractive { get { return false; } }
        public virtual bool HandlesOwnDrag { get { return false; } }
        public virtual bool DefaultResident { get { return true; } }
        public virtual string DefaultHotkey { get { return ""; } }
        public virtual int Order { get { return 50; } }

        public virtual int RefreshIntervalMs(CardSettings config) { return 0; }

        public abstract string Render(CardRenderContext context);

        public override string ToString() { return DisplayName; }
    }
}
