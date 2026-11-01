using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 宿主提供的 Shell 能力：剪贴板、文件图标、拖出、用资源管理器打开 / 定位。
    /// 插件不该自己 P/Invoke 或抓窗口句柄——这些行为要跟宿主窗口的生命周期和权限模型保持一致。
    /// </summary>
    public interface ICardShellService
    {
        /// <summary>从剪贴板取文件列表；没有文件时返回 false。</summary>
        bool TryGetClipboardFiles(out string[] paths);

        /// <summary>把文本放进剪贴板；剪贴板被别的进程占用时返回 false。</summary>
        bool TrySetClipboardText(string text);

        /// <summary>
        /// 取文件 / 文件夹的 Shell 图标。失败返回 null；相同参数的图标会被宿主缓存。
        /// <paramref name="extensionOnly"/> = true 时按扩展名取通用图标（不读具体文件）。
        /// </summary>
        ImageSource GetFileIcon(string path, int size, bool extensionOnly);

        /// <summary>
        /// 开始一次文件拖出（阻塞到拖放结束）。<paramref name="source"/> 必须是卡片内容里的元素。
        /// 返回实际发生的拖放效果。
        /// </summary>
        DragDropEffects BeginDragFiles(FrameworkElement source, IList<string> paths, DragDropEffects allowed);

        /// <summary>用默认程序打开文件 / 文件夹。</summary>
        void OpenPath(string path);

        /// <summary>在资源管理器中定位文件（或打开文件夹）。</summary>
        void RevealPath(string path);
    }

    /// <summary>
    /// 模块自己的设置区块里可能有「失焦才提交」的草稿（例如 API Key 输入框）。
    /// 宿主在用户点「确定」前调用这个接口，让区块把草稿写回配置。
    /// 实现方通常是 <see cref="ICardEditorSection.CreateEditor"/> 返回的根元素。
    /// </summary>
    public interface ICardEditorCommit
    {
        void CommitPendingEdits();
    }

    /// <summary>
    /// 富内容卡片可以参与文档截图：截图时宿主先调用它，让内容切到可离屏渲染的状态
    /// （例如视频卡把第一帧画成静态封面）。返回的控件必须仍可测量 / 渲染。
    /// </summary>
    public interface ICardScreenshotProvider
    {
        void PrepareScreenshot(FrameworkElement content);
    }

    /// <summary>
    /// 卡片模块提供可切换的预设（信息与文本的四种预设）。宿主设置页用它画下拉框，
    /// 不需要认识任何具体卡片类型；换预设时由模块决定新内容。
    /// </summary>
    public sealed class CardPreset
    {
        public CardPreset(string id, string label, string defaultContent, string icon)
        {
            Id = id ?? "";
            Label = label ?? id ?? "";
            DefaultContent = defaultContent ?? "";
            Icon = icon ?? "";
        }

        public string Id { get; private set; }
        public string Label { get; private set; }
        public string DefaultContent { get; private set; }
        public string Icon { get; private set; }

        /// <summary>该预设是否允许用户编辑内容模板（按键卡的模板由模块固定）。</summary>
        public bool EditableContent { get; set; }
    }

    public interface ICardPresetSource
    {
        /// <summary>按显示顺序排列的预设。</summary>
        IList<CardPreset> Presets { get; }

        /// <summary>当前预设 Id（未知 / 空值返回模块的默认预设）。</summary>
        string CurrentPreset(CardSettings config);

        /// <summary>切到指定预设：模块负责把卡片内容重置成该预设的默认内容。</summary>
        void ApplyPreset(CardSettings config, string presetId);
    }

    /// <summary>
    /// 卡片有「绕过设置窗直接写 live settings」的运行时字段（对话模式）时实现。
    /// 设置窗保存前会用自己的副本 / 打开时基线 / 当前 live 三方比较，只合并用户没在窗口里改过的值。
    /// </summary>
    public interface ICardRuntimeSettingsMerge
    {
        void MergeRuntimeFields(CardSettings target, CardSettings live, CardSettings baseline);
    }

    /// <summary>
    /// 卡片自带回归自检。宿主 <c>--selftest</c> 会逐个调用，返回 null / 空串表示通过，
    /// 否则返回一条给用户看的失败说明。富内容 / 平台相关检查放插件自己这里，宿主不认具体类型。
    /// </summary>
    public interface ICardSelfTest
    {
        string RunSelfTest();
    }
}
