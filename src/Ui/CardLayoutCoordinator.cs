using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 叠层卡片的堆叠布局：从锚点开始按顺序排、左右对齐、把窗口高度变化合并成一次布局。
///
/// 从 <see cref="OverlayController"/> 抽出来，控制器只负责“什么时候该排”，
/// 这里负责“怎么排”。坐标 / 显示器 / 对齐都只看 <see cref="Settings"/> 与卡片窗口。
/// </summary>
internal sealed class CardLayoutCoordinator
{
    readonly IList<CardSlot> _cards;
    readonly Func<Settings> _settings;
    readonly Func<bool> _disposed;
    readonly Dispatcher _dispatcher;
    bool _scheduled;
    /// <summary>拖动期间暂停常规布局，避免窗口尺寸 / 内容变化把整叠拉回锚点。</summary>
    bool _suspended;
    /// <summary>最近一次被抓住的卡片：自动对齐以它为基准，其余卡片向它看齐。</summary>
    CardSlot _focus;
    /// <summary>本次拖动选定的左 / 右对齐；null = 未拖动过，按设置重新判断。</summary>
    string _alignOverride;
    /// <summary>
    /// 右对齐时真正固定的是右边缘：卡片内容变宽只向左长，整叠不会横向跳动。
    /// 锚点 / 对齐 / 卡片数量变化时才重新取一次。
    /// </summary>
    double _rightEdge;
    /// <summary>最近一次布局算出的整叠最大宽度；EndDrag 用它重算右边缘（不能用 slot.Dialog 的副本）。</summary>
    double _stackWidth;
    double _lastAnchorX = double.NaN;
    int _lastCardCount = -1;
    string _lastAlign;
    bool _widthsComplete;
    /// <summary>每张卡上一次的 X。显隐动画期间保留它，只更新 Y，避免整排横向移动。</summary>
    readonly Dictionary<CardSlot, double> _knownX = new();

    public CardLayoutCoordinator(IList<CardSlot> cards, Func<Settings> settings,
                                 Func<bool> disposed, Dispatcher dispatcher)
    {
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _disposed = disposed ?? (() => false);
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>窗口尺寸变化 / 模块请求布局时合并到下一次 Render 优先级执行。</summary>
    public void Schedule()
    {
        if (_suspended || _scheduled || _disposed()) return;
        _scheduled = true;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            _scheduled = false;
            if (_disposed() || _suspended) return;
            SyncHeights();
            Layout();
        }), DispatcherPriority.Render);
    }

    /// <summary>从锚点开始按顺序把可见卡片往下排，间距取每张卡片自己的 Spacing。</summary>
    public void Layout()
    {
        if (_suspended) return;
        // 非拖动触发的布局（改锚点 / 显隐 / 尺寸变化）重新按当前卡片位置判断，
        // 不把上一次拖动选的对齐永久钉住。
        _alignOverride = null;
        LayoutCore(false, null);
    }

    /// <summary>
    /// 开始拖动：先停掉残余位置动画，把整叠落到上一轮的目标位并同步高度，
    /// 然后挂起常规布局。这样拖动起点和每张卡的布局坐标一致，不会出现
    /// “抓起的卡片已经动了、其它卡片还停在旧动画中间”的错位。
    /// 调用方必须已经 StopPositionMotion。
    /// </summary>
    public void BeginDrag(CardSlot slot)
    {
        _focus = slot;
        _alignOverride = null;
        _suspended = true;
        SyncHeights();
        LayoutCore(false, null);
    }

    /// <summary>
    /// 拖动结束：resolvedAlign 是松手时按被抓卡片算出的左 / 右对齐（Auto 已解析），
    /// 恢复到它并让其余卡片动画跟过去。传 null 则按设置重新判断。
    /// </summary>
    public void EndDrag(CardSlot slot, string resolvedAlign)
    {
        _focus = slot;
        _alignOverride = resolvedAlign;
        _suspended = false;
        SyncHeights();
        // 拖动只更新宿主锚点；slot.Dialog.Overlay 是 BuildDialog 的副本，拖动期间不会更新。
        // 用最近一次布局的整叠宽度重算右边缘，避免松手后按旧坐标回跳。
        if (slot is not null && string.Equals(resolvedAlign, "Right", StringComparison.OrdinalIgnoreCase))
            _rightEdge = _settings().Overlay.X + _stackWidth;
        _lastAnchorX = _settings().Overlay.X;
        LayoutCore(true, null, false);
    }

    /// <summary>
    /// 带动画的补位：已显示的卡片从当前位置 ease-out 到新位置；
    /// appearing 不为空时，这张新卡直接落到目标位（内容由呼出动画负责），
    /// 避免它从窗口默认位置「飞」到锚点。
    /// </summary>
    public void LayoutAnimated(CardSlot appearing = null)
    {
        if (_suspended) return;
        _alignOverride = null;
        LayoutCore(true, appearing, true);
    }

    void LayoutCore(bool animate, CardSlot appearing, bool preserveX = false)
    {
        Settings settings = _settings();

        // 先量出整叠卡片里最宽的一张：右对齐时每张卡片的 X = 锚点 + (最宽 - 自己宽度)，
        // 这样所有卡片的右边缘落在同一条竖线上；左对齐时所有卡片共用锚点 X。
        // 右对齐的 X 由“整叠最宽卡片”决定。若只统计当前显示的卡片，
        // 整体隐藏 / 显示时最宽卡片消失或回来，其余卡片会横向重排，
        // 看起来像无论设置什么方向都在向右滑。这里把常驻卡片也算进去，
        // 显隐期间保持同一条右边缘；非常驻的临时卡片只在显示时参与。
        double visibleWidth = 0;
        double stackWidth = 0;
        bool widthsComplete = true;
        foreach (CardSlot slot in _cards)
        {
            double width = slot.Window.CardWidth;
            bool resident = slot.Config is not null && slot.Config.Resident;
            bool relevant = slot.Shown || resident;
            if (relevant && width <= 0) widthsComplete = false;
            if (width <= 0) continue;

            if (slot.Shown) visibleWidth = Math.Max(visibleWidth, width);
            if (relevant) stackWidth = Math.Max(stackWidth, width);
        }

        if (stackWidth <= 0) stackWidth = visibleWidth;
        _stackWidth = stackWidth;

        string align = ResolveAlign(settings, visibleWidth > 0 ? visibleWidth : stackWidth);
        bool alignChanged = !string.Equals(_lastAlign, align, StringComparison.Ordinal);
        bool anchorChanged = double.IsNaN(_lastAnchorX)
                             || Math.Abs(settings.Overlay.X - _lastAnchorX) > 0.5;
        bool cardsChanged = _lastCardCount != _cards.Count;
        if (alignChanged || anchorChanged || cardsChanged) _knownX.Clear();

        // 右对齐固定右边缘而不是“锚点 + 会变的整叠宽度”：
        // 指标卡宽度变化、整体显隐时，卡片只向左长 / 缩，不会整排向右滑。
        if (string.Equals(align, "Right", StringComparison.Ordinal))
        {
            // 首帧只显示了第一张卡时，其余常驻卡宽度还是 0；此时定型会把右边缘钉死。
            // 先沿用当前估算，等所有相关宽度到齐后再补一次。
            if (_rightEdge <= 0)
                _rightEdge = settings.Overlay.X + stackWidth;

            bool reset = alignChanged || anchorChanged || cardsChanged || !_widthsComplete;
            if (widthsComplete && reset)
                _rightEdge = settings.Overlay.X + stackWidth;
        }
        else
        {
            _rightEdge = 0;
        }
        _widthsComplete = widthsComplete;
        _lastAnchorX = settings.Overlay.X;
        _lastCardCount = _cards.Count;
        _lastAlign = align;

        double top = 0;
        bool first = true;

        foreach (CardSlot slot in _cards)
        {
            if (!slot.Shown) continue;

            if (!first) top += Math.Max(0, slot.Config.Spacing);

            // 显隐动画期间连新出现的卡片也沿用它上一次的 X；只有从未落过位的
            // 新卡片才现算。这样整排在显示 / 隐藏时不会横向移动。
            bool keepX = preserveX && _knownX.TryGetValue(slot, out _);
            double x;
            if (keepX)
            {
                x = _knownX[slot];
            }
            else
            {
                x = settings.Overlay.X;
                if (align == "Right")
                    x = Math.Max(0, _rightEdge - slot.Window.CardWidth);
                _knownX[slot] = x;
            }

            slot.Dialog.Overlay.X = x;
            slot.Dialog.Overlay.Y = settings.Overlay.Y + top;
            slot.Dialog.Overlay.Monitor = settings.Overlay.Monitor;
            slot.Dialog.Overlay.MonitorDevice = settings.Overlay.MonitorDevice;
            slot.Dialog.Overlay.Locked = settings.Overlay.Locked;
            // “跟随卡片对齐”的文字需要知道整叠最终选了哪一边；由布局统一告知，
            // 避免每张卡按自己的中心各猜一个方向。
            slot.Window.SetResolvedCardAlignment(align);
            slot.Window.ApplyPosition(animate && !ReferenceEquals(slot, appearing));

            top += slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight;
            first = false;
        }
    }

    /// <summary>文字变长 / 命令行展开后窗口会自己长高：渲染后校正一次堆叠位置。</summary>
    public void LayoutIfNeeded()
    {
        bool changed = false;
        foreach (CardSlot slot in _cards)
        {
            if (!slot.Shown) continue;
            double height = slot.Window.CardHeight;
            if (Math.Abs(height - slot.BodyHeight) > 0.5)
            {
                slot.BodyHeight = height;
                changed = true;
            }
        }

        if (changed && !_suspended) Layout();
    }

    public void SyncHeight(CardSlot slot)
    {
        if (slot is null) return;
        slot.BodyHeight = slot.Shown ? slot.Window.CardHeight : 0;
    }

    public void SyncHeights()
    {
        foreach (CardSlot slot in _cards) SyncHeight(slot);
    }

    /// <summary>
    /// 左对齐 = 左边缘共线，右对齐 = 右边缘共线。
    /// 自动模式优先用“最近抓住的卡片”在屏幕左 / 右半边决定：用户拖哪张卡，
    /// 哪张卡就是对基准，其余卡片向它对齐；没有焦点时才退回整叠中心。
    /// </summary>
    string ResolveAlign(Settings settings, double stackWidth)
    {
        string mode = Settings.NormalizeCardAlign(settings.Style.CardAlign);
        if (mode == "Left" || mode == "Right") return mode;
        if (!string.IsNullOrEmpty(_alignOverride)) return _alignOverride;

        try
        {
            ScreenInfo screen = ScreenGeometry.Get(settings.Overlay.Monitor, settings.Overlay.MonitorDevice);
            if (screen is null) return "Left";

            double scaleX = screen.ScaleX > 0.1 ? screen.ScaleX : 1.0;
            double workWidthDip = screen.WorkWidth / scaleX;

            double center;
            if (_focus is not null && _focus.Shown && _focus.Window is not null)
            {
                center = _focus.Dialog.Overlay.X + _focus.Window.CardWidth / 2;
            }
            else
            {
                center = settings.Overlay.X + stackWidth / 2;
            }
            return center < workWidthDip / 2 ? "Left" : "Right";
        }
        catch (Exception ex)
        {
            Log.Warn($"判断卡片对齐失败，回落左对齐: {ex.Message}");
            return "Left";
        }
    }
}
