using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗的校验 / 保存 / 运行时字段合并。
///
/// 保存链路（P0-2）：ReadUi → MergeRuntimeFields → ValidateSettings → _onApply（控制器落盘并应用）→
/// 刷新本窗口副本 → 状态行如实区分「已保存并应用」「已应用但落盘失败」「刷新副本失败」。
/// 运行时字段合并（P1-5）：设置窗打开期间热键可能绕过本窗口改了 live settings，
/// 保存前按「打开时的基线」逐字段判断用户是否动过，只把没动过的运行时字段合并回来。
/// </summary>
public partial class SettingsWindow
{
    /// <summary>留空表示禁用；返回 null 表示全部合法。</summary>
    static string ValidateSettings(Settings s)
    {

        int interactive = CardModel.CountInteractive(s.Cards);
        if (interactive > 1)
        {
            string names = string.Join("、", s.Cards
                .Where(c => c is not null && CardRegistry.Get(c.Kind).IsInteractive)
                .Select(c => c.Name));
            return $"只能有一张交互卡片（键盘独占），当前有 {interactive} 张：{names}";
        }

        if (!string.IsNullOrWhiteSpace(s.Hotkeys.ToggleOverlay) &&
            !HotkeyManager.TryParse(s.Hotkeys.ToggleOverlay, out var mods, out var vk))
            return $"显示/隐藏热键无效：{s.Hotkeys.ToggleOverlay}";

        if (!string.IsNullOrWhiteSpace(s.Hotkeys.OpenSettings) &&
            !HotkeyManager.TryParse(s.Hotkeys.OpenSettings, out mods, out vk))
            return $"设置热键无效：{s.Hotkeys.OpenSettings}";

        if (!string.IsNullOrWhiteSpace(s.Hotkeys.ToggleLock) &&
            !HotkeyManager.TryParse(s.Hotkeys.ToggleLock, out mods, out vk))
            return $"锁定热键无效：{s.Hotkeys.ToggleLock}";

        foreach (CardSettings card in s.Cards)
        {
            if (card is null) continue;

            // 卡片自己的设置由模块自己校验（宿主不再认识「自定义命令行」这种具体规则）。
            var validator = CardRegistry.Get(card.Kind) as ICardSettingsValidator;
            if (validator is not null)
            {
                string cardError = validator.Validate(card);
                if (!string.IsNullOrEmpty(cardError))
                    return $"卡片「{card.Name}」{cardError}";
            }

            if (card.Resident) continue;
            if (CardRegistry.Get(card.Kind).IsInteractive) continue;

            if (string.IsNullOrWhiteSpace(card.Hotkey))
                return $"卡片「{card.Name}」没有呼出热键：勾选「常驻」，或给它设一个组合键。";

            if (!HotkeyManager.TryParse(card.Hotkey, out mods, out vk))
                return $"卡片「{card.Name}」的呼出热键无效：{card.Hotkey}";
        }

        return null;
    }

    void SaveClick(object sender, RoutedEventArgs e) => TrySaveSettings();

    /// <summary>读取 / 校验 / 应用 / 刷新副本。返回 true 表示已成功写盘，可用于关闭前提示。</summary>
    bool TrySaveSettings()
    {
        if (_loadFailed || _working is null)
        {
            StatusText.Text = "设置副本不可用，已禁止保存（请重开设置窗口；详情见日志）";
            return false;
        }

        if (ConfigStore.LastLoadReadOnly)
        {
            StatusText.Text = "配置为只读加载：请先处理设置文件并重启应用（本次改动不会写入磁盘）";
            return false;
        }

        Settings s;
        try
        {
            s = ReadUi();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取设置失败：{ex.Message}";
            return false;
        }

        // 设置窗打开期间热键可能绕过本窗口改了 live settings；保存前把运行时字段合并回来。
        MergeRuntimeFields(s);

        string hotkeyError = ValidateSettings(s);
        if (hotkeyError is not null)
        {
            StatusText.Text = hotkeyError;
            return false;
        }

        bool saved;
        try
        {
            saved = _onApply is not null && _onApply(s);
        }
        catch (Exception ex)
        {
            // 控制器可能已经改了一部分运行期状态。至少要把失败显示出来，
            // 不能像原来那样异常直接逸出、状态行还停留在上一次的结果。
            Log.Error("应用设置失败", ex);
            StatusText.Text = $"应用设置失败：{ex.Message}（未写入磁盘；详情见日志）";
            return false;
        }

        // 控制器在 ApplySettings 里直接把传入对象当成 live settings（_settings = settings），
        // 之后热键切模式就落在它上面；记下来供下次保存前的运行时字段合并。
        _liveSettings = s;
        _skipRuntimeMerge = false;

        // 控制器会把实时锚点带回来，同步到界面，避免下次保存又写回旧坐标。
        SyncAnchor(s.Overlay.X, s.Overlay.Y, s.Overlay.Monitor);

        if (TryClone(s, out var next, out var cloneError))
        {
            _loadFailed = false;
            _working = next;
            CardModel.Normalize(_working);
            CaptureRuntimeBaseline();
        }
        else
        {
            // 内存里已经应用了，只是刷新副本失败：保留旧副本，别把界面切成默认值。
            _loadFailed = true;
            Log.Error($"保存后刷新设置副本失败: {cloneError}");
        }

        RebuildCardList();

        // 内存已生效但落盘失败＝重启会回滚：状态行必须如实说（P0-2）。
        if (!saved)
        {
            string reason = string.IsNullOrWhiteSpace(ConfigStore.LastSaveError)
                ? "原因未知（详见日志）"
                : ConfigStore.LastSaveError.Trim();
            StatusText.Text = $"已应用，但保存到磁盘失败：{reason}。重启后仍是旧配置（详见日志）";
            return false;
        }

        // S-06：设置本身已落盘，但附属动作（开机自启写注册表）可能失败；
        // 这类失败不能藏在日志里，否则窗口显示“已保存并应用”与事实不符。
        string applyWarning = _applyWarning?.Invoke();
        bool hasWarning = !string.IsNullOrWhiteSpace(applyWarning);
        StatusText.Text = _loadFailed
            ? (hasWarning
                ? $"已保存并应用，但刷新设置副本失败、{applyWarning}（详情见日志）"
                : "已保存并应用，但刷新设置副本失败（详情见日志）")
            : (hasWarning
                ? $"已保存并应用，但{applyWarning}（{DateTime.Now:HH:mm:ss}）"
                : $"已保存并应用（{DateTime.Now:HH:mm:ss}）");
        ClearDirty();
        CaptureCleanSnapshot();
        return true;
    }

    // ==================== 运行时字段合并（P1-5 设置窗快照） ====================

    /// <summary>记录「用户还没动过」的运行时字段基线；克隆失败时退化成不合并（宁可保持旧行为）。</summary>
    void CaptureRuntimeBaseline()
    {
        if (TryClone(_working, out var baseline, out var error))
        {
            _runtimeBaseline = baseline;
            return;
        }

        _runtimeBaseline = null;
        Log.Warn($"设置基线克隆失败，本次保存不合并运行时字段: {error}");
    }

    /// <summary>
    /// P1-5：设置窗打开期间，热键切 Chat 模式（<c>ChatCard.ToggleMode</c>）和热键被占用后的
    /// 自动回退都会直接改 live settings，而本窗口的 <see cref="_working"/> 是打开时的深拷贝；
    /// 直接保存会把模式 / 实际热键回滚成旧值。
    ///
    /// 这里在保存前只把 live 的<em>运行时字段</em>合并进待保存设置，并且逐个字段和打开时的基线比较：
    /// 与基线不同 = 用户在本窗口改过，保留用户的值（不覆盖未保存编辑）；相同时才采用 live 的值。
    /// 运行时字段限定为：Chat 模式、卡片呼出热键、三个全局热键、卡片通用动作热键。
    /// </summary>
    void MergeRuntimeFields(Settings target)
    {
        if (_skipRuntimeMerge || target is null || _liveSettings is null || _runtimeBaseline is null) return;
        if (ReferenceEquals(target, _liveSettings)) return;

        Settings live = _liveSettings;
        Settings baseline = _runtimeBaseline;

        if (live.Behavior is not null && baseline.Behavior is not null && target.Behavior is not null)
        {
            // 托盘菜单直接改 live 树并立即落盘；设置窗保存时如果用户没动过这个复选框，
            // 必须采用 live 的值，否则会把托盘刚打开的开机自启静默翻回去（L-8）。
            target.Behavior.AutoStart = MergeRuntimeBool(
                target.Behavior.AutoStart, baseline.Behavior.AutoStart, live.Behavior.AutoStart);
        }

        if (live.Hotkeys is not null && baseline.Hotkeys is not null && target.Hotkeys is not null)
        {
            target.Hotkeys.ToggleOverlay = MergeRuntimeValue(
                target.Hotkeys.ToggleOverlay, baseline.Hotkeys.ToggleOverlay, live.Hotkeys.ToggleOverlay);
            target.Hotkeys.OpenSettings = MergeRuntimeValue(
                target.Hotkeys.OpenSettings, baseline.Hotkeys.OpenSettings, live.Hotkeys.OpenSettings);
            target.Hotkeys.ToggleLock = MergeRuntimeValue(
                target.Hotkeys.ToggleLock, baseline.Hotkeys.ToggleLock, live.Hotkeys.ToggleLock);

            if (live.Hotkeys.CardActions is not null)
            {
                foreach (CardActionHotkey entry in live.Hotkeys.CardActions)
                {
                    if (entry is null || string.IsNullOrWhiteSpace(entry.Action)) continue;

                    string action = entry.Action;
                    string targetValue = target.Hotkeys.GetCardAction(action);
                    if (!SameRuntimeValue(targetValue, baseline.Hotkeys.GetCardAction(action))) continue;  // 用户改过

                    string liveValue = live.Hotkeys.GetCardAction(action);
                    if (!SameRuntimeValue(targetValue, liveValue))
                        target.Hotkeys.SetCardAction(action, liveValue);
                }
            }
        }

        MergeCardRuntimeFields(target, live, baseline);
    }

    /// <summary>卡片级的运行时字段：呼出热键（被占用时会自动换成实际组合）与 Chat 模式。</summary>
    void MergeCardRuntimeFields(Settings target, Settings live, Settings baseline)
    {
        if (target.Cards is null || live.Cards is null || baseline.Cards is null) return;

        Dictionary<string, CardSettings> liveCards = IndexCards(live.Cards);
        Dictionary<string, CardSettings> baselineCards = IndexCards(baseline.Cards);

        foreach (CardSettings card in target.Cards)
        {
            if (card is null || string.IsNullOrEmpty(card.Id)) continue;

            if (!liveCards.TryGetValue(card.Id, out var liveCard)) continue;
            if (!baselineCards.TryGetValue(card.Id, out var baselineCard)) continue;
            if (liveCard is null || baselineCard is null) continue;

            card.Hotkey = MergeRuntimeValue(card.Hotkey, baselineCard.Hotkey, liveCard.Hotkey);

            // 卡片自己的运行时字段（对话模式等）由模块声明合并规则，宿主不认 Kind / Extras。
            var merge = CardRegistry.TryGet(card.Kind, out var module)
                ? module as ICardRuntimeSettingsMerge
                : null;
            if (merge is null) continue;

            try { merge.MergeRuntimeFields(card, liveCard, baselineCard); }
            catch (Exception ex) { Log.Warn($"合并卡片运行时字段失败({card.Name}): {ex.Message}"); }
        }
    }

    /// <summary>
    /// 用户没动过这个字段（仍等于打开时的基线）才采用 live 的运行时值。
    /// 空值不合并：热键路由在注册失败时会把 live 热键写成空串，合并回来会让卡片呼出热键
    /// 触发「没有呼出热键」校验而挡住保存；保留窗口里的值，应用设置时还能重试注册。
    /// </summary>
    static string MergeRuntimeValue(string targetValue, string baselineValue, string liveValue)
    {
        if (string.IsNullOrWhiteSpace(liveValue)) return targetValue;
        return SameRuntimeValue(targetValue, baselineValue) ? liveValue : targetValue;
    }

    static bool SameRuntimeValue(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>布尔运行时字段：用户没动过（仍等于基线）才采用 live 的值。</summary>
    static bool MergeRuntimeBool(bool target, bool baseline, bool live) => target == baseline ? live : target;

    /// <summary>按卡片 Id 建索引；Id 是删除 / 重排都不变的稳定标识。</summary>
    static Dictionary<string, CardSettings> IndexCards(List<CardSettings> cards)
    {
        var map = new Dictionary<string, CardSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (CardSettings card in cards)
        {
            if (card is null || string.IsNullOrEmpty(card.Id)) continue;
            if (!map.ContainsKey(card.Id)) map[card.Id] = card;
        }
        return map;
    }
}
