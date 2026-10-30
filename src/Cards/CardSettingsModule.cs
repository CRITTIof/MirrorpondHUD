using System;
using System.Windows;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 模块自己的设置（数据面）：默认值、字段归一化都归模块管。
    /// 设置存在 <see cref="CardSettings.Extras"/> 里，键由模块自己定（命令行卡是 <c>command</c>）。
    ///
    /// 宿主只在「建卡 / 每次配置归一化」时调用 <see cref="InitializeCard"/>，不解释具体内容，
    /// 所以新增一种带设置的卡片不用再改 <c>CardSettings</c> / <c>CardModel</c>。
    /// </summary>
    public interface ICardSettingsModule
    {
        /// <summary>
        /// 补默认设置并做归一化。每次配置归一化都会调用，**不能覆盖用户已填的值**，
        /// 只允许补空缺、夹紧范围。
        /// </summary>
        void InitializeCard(CardSettings config);
    }

    /// <summary>
    /// 模块自己的设置区块（界面面）：设置页里这张卡片的「内容」下方由模块自己画。
    /// 宿主只提供一个 <see cref="System.Windows.Controls.ContentControl"/>，
    /// 不再为每种卡片往 <c>SettingsWindow.xaml</c> 里堆一块面板。
    /// </summary>
    public interface ICardEditorSection
    {
        /// <summary>
        /// 创建这张卡片的设置区块；模块负责把控件里的改动写回 <paramref name="config"/>。
        /// <paramref name="onChanged"/> 在改动后调用，宿主用它刷新卡片摘要。
        /// </summary>
        FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged);
    }

    /// <summary>
    /// 模块自己校验设置（例如「选了自定义命令行但模板是空的」）。返回 null = 通过；
    /// 返回给用户看的错误说明（不含卡片名，宿主会拼上）。
    /// </summary>
    public interface ICardSettingsValidator
    {
        string Validate(CardSettings config);
    }
}
