using System;
using System.ComponentModel;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// P-25：四张卡片的设置区块共用的“改草稿 → 通知绑定 / 标记配置已改”样板。
///
/// 为什么用组合而不是基类：XAML 视图的 partial class 基类由生成的 .g.cs 固定成
/// <see cref="System.Windows.Controls.UserControl"/>，代码后台再声明别的基类会冲突；
/// 组合既能保持各视图的 XAML 与绑定名不变，也能让纯代码构建的设置区块复用同一套通知。
/// </summary>
public sealed class CardEditorNotifier
{
    readonly Action _onChanged;

    public CardEditorNotifier(Action onChanged)
    {
        _onChanged = onChanged ?? (() => { });
    }

    /// <summary>绑定通知；使用方把 XAML / 宿主的 PropertyChanged 事件转发到这里。</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>改了一个会写进配置的属性：通知绑定并标记配置已改。</summary>
    public void Raise(string name)
    {
        _onChanged.Invoke();
        Notify(name);
    }

    /// <summary>
    /// 只刷新界面、不标脏：解码器探测结果这类派生状态不是用户改的配置，
    /// 不能因为它让设置窗显示“未保存”。
    /// </summary>
    public void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>只标脏、不通知绑定：纯代码构建、没有绑定面时用。</summary>
    public void MarkChanged() => _onChanged.Invoke();
}
