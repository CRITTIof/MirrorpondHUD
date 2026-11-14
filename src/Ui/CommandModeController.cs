using System;
using System.Collections.Generic;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Ui;

/// <summary>命令行模式需要宿主提供的显示 / 键盘 / 重绘动作。</summary>
internal interface ICommandModeHost
{
    IList<CardSlot> Cards { get; }
    bool OverlayVisible { get; }
    bool IntroRunning { get; }

    void FinishIntro();
    void ShowOverlay();
    void ShowCard(CardSlot slot, string text, bool animate);
    void HideCard(CardSlot slot, bool animate);
    void RenderNow();
    void UpdateCommandInfo();
    void SetKeyboardSuppressed(bool suppressed);
}

/// <summary>
/// 快捷命令行（交互卡）的会话状态：当前卡片、进入 / 退出序列、界面提示。
///
/// 从 <see cref="OverlayController"/> 抽出来，控制器只决定“哪个卡片是交互卡”，
/// 显示 / 隐藏 / 键盘抑制 / 重绘仍由宿主执行。
/// </summary>
internal sealed class CommandModeController
{
    readonly ICommandModeHost _host;

    public CommandModeController(ICommandModeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public bool IsActive { get; private set; }
    public CardSlot Card { get; private set; }

    public string CommandHotkey => Card is null || Card.Config is null ? "" : Card.Config.Hotkey;

    public void Toggle(CardSlot slot)
    {
        if (slot is null || slot.Module is null || !slot.Module.IsInteractive)
        {
            Log.Info("没有可用的命令行卡片");
            return;
        }

        if (IsActive && ReferenceEquals(Card, slot))
        {
            Exit();
            return;
        }

        if (IsActive) Exit();
        if (_host.IntroRunning) _host.FinishIntro();

        if (!_host.OverlayVisible) _host.ShowOverlay();

        Enter(slot);
    }

    void Enter(CardSlot slot)
    {
        IsActive = true;
        Card = slot;

        _host.SetKeyboardSuppressed(true);

        slot.Summoned = true;

        // 交互卡也是非常驻卡：和媒体卡一样走 160ms 呼出 + 其余卡片 180ms 让位。
        // 键盘焦点在 ShowCard 之后才接管，淡入期间输入不会丢。
        _host.ShowCard(slot, "", true);
        slot.Window.EnsureTopmost();
        slot.Window.ResetStableSize();

        // 命令行面板是富内容（和 AI 对话同一套 CliPanelView）：开始一次新会话，
        // 再让窗口把前台和键盘焦点抢过来。
        ICliFocusTarget target = slot.Content as ICliFocusTarget;
        if (target is not null)
        {
            try { target.Begin(); }
            catch (Exception ex) { Log.Warn($"命令行面板初始化失败: {ex.Message}"); }
        }

        slot.Window.BeginCommandInput();

        // 交互卡片自己解释自己的设置并给出日志摘要（宿主不再认识 CommandSettings）。
        string summary = null;
        var activation = slot.Module as ICardActivation;
        if (activation is not null)
        {
            try { summary = activation.OnActivated(slot.Config); }
            catch (Exception ex) { Log.Warn($"卡片呼出回调失败: {ex.Message}"); }
        }

        _host.UpdateCommandInfo();

        string cardName = slot.Config is null ? "?" : slot.Config.Name;
        Log.Info($"快捷命令行已打开（卡片={cardName}{(string.IsNullOrEmpty(summary) ? "" : $"，{summary}")}）");
    }

    public void Exit()
    {
        if (!IsActive) return;

        IsActive = false;

        CardSlot slot = Card;
        Card = null;

        if (slot is not null)
        {
            // 交互卡片先收自己的状态，再让面板收尾。
            var activation = slot.Module as ICardActivation;
            if (activation is not null)
            {
                try { activation.OnDeactivated(slot.Config); }
                catch (Exception ex) { Log.Warn($"卡片收起回调失败: {ex.Message}"); }
            }

            // 面板自己管着「还在跑的命令」和记录列表，收起前先让它收尾。
            ICliFocusTarget target = slot.Content as ICliFocusTarget;
            if (target is not null)
            {
                try { target.Cancel(); }
                catch (Exception ex) { Log.Warn($"命令行收尾失败: {ex.Message}"); }
            }

            slot.Window.EndCommandInput();
            slot.Window.ResetStableSize();
            slot.Summoned = false;

            // 先标记不占位并让其余卡片补位，面板本身淡出后再真正 Hide。
            _host.HideCard(slot, true);
        }

        _host.SetKeyboardSuppressed(false);
        _host.UpdateCommandInfo();
        _host.RenderNow();
        Log.Info("快捷命令行已关闭，命令与返回已清空");
    }

    /// <summary>卡片被删 / 重建时直接丢掉会话状态，不触碰已经销毁的窗口。</summary>
    public void Reset()
    {
        IsActive = false;
        Card = null;
    }
}
