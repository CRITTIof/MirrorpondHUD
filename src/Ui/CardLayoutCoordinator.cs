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
    /// <summary>上一次看到的 CardAlign 设置；设置被用户改过时清掉拖动选定的对齐。</summary>
    string _lastAlignSetting;
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

    /// <summary>T-9：堆叠超出工作区的告警节流（每 120ms 一次布局不能每条都写日志）。</summary>
    readonly LogThrottle _workAreaLog = new("卡片堆叠超出工作区", 30000);

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

    /// <summary>
    /// 从锚点开始按顺序把可见卡片往下排，间距取每张卡片自己的 Spacing。
    /// T-10：这里不再清 <see cref="_alignOverride"/>——拖动选定的对齐要一直有效到用户
    /// 下一次拖动（<see cref="BeginDrag"/> 才更新），否则高度变化触发的常规布局会把
    /// 刚对齐好的整叠重新翻边。
    /// </summary>
    public void Layout()
    {
        if (_suspended) return;
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
    /// T-10：同样不清 <see cref="_alignOverride"/>。
    /// </summary>
    public void LayoutAnimated(CardSlot appearing = null)
    {
        if (_suspended) return;
        LayoutCore(true, appearing, true);
    }

    void LayoutCore(bool animate, CardSlot appearing, bool preserveX = false)
    {
        Settings settings = _settings();

        // T-10：拖动选定的对齐只在两种情况下失效——用户重新拖动（BeginDrag），
        // 或用户改了 CardAlign 设置。高度变化 / 显隐 / 常规补位都不再清它。
        string alignSetting = Settings.NormalizeCardAlign(settings.Style.CardAlign);
        if (!string.Equals(_lastAlignSetting, alignSetting, StringComparison.Ordinal))
        {
            _alignOverride = null;
            _lastAlignSetting = alignSetting;
        }

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

        // T-9：堆叠必须夹在工作区内。以前 Y 只做加法，卡片一多最下面的窗口就落到
        // 任务栏以下，用户看不到也拖不回来（拖动要先命中卡片）。这里按目标屏工作区
        // （物理像素 / ScaleY，天然扣掉任务栏）先算总高：放不下就压缩间距，
        // 仍放不下再逐卡夹取（会重叠，但每张都可见、可拖）。
        double workHeight = double.PositiveInfinity;
        try
        {
            // G-14：优先按设备名定位（多屏热插拔后整数序号可能已指到别的屏）。
            ScreenInfo screen = ScreenGeometry.Get(settings.Overlay.Monitor, settings.Overlay.MonitorDevice);
            if (screen is not null && screen.ScaleY > 0.1)
                workHeight = screen.WorkHeight / screen.ScaleY;
        }
        catch (Exception ex)
        {
            // 拿不到显示器信息时按"不夹取"继续布局，至少不能让整叠消失。
            Log.Warn($"读取工作区高度失败，跳过堆叠夹取: {ex.Message}");
        }
        bool hasWorkArea = !double.IsInfinity(workHeight) && workHeight > 1.0;

        double spacingScale = 1.0;
        double stackHeight = MeasureStackHeight(spacingScale, out double stackGaps);
        if (hasWorkArea && stackHeight > workHeight)
        {
            // 间距是可压缩量：保留卡片高度，只把卡片之间的空隙按比例收掉。
            spacingScale = stackGaps > 0
                ? Math.Max(0.0, Math.Min(1.0, (workHeight - (stackHeight - stackGaps)) / stackGaps))
                : 1.0;
            double compressed = MeasureStackHeight(spacingScale, out _);
            _workAreaLog.Failure(
                $"可见卡片总高 {compressed:0} DIP 超出工作区 {workHeight:0} DIP，已压缩间距 {spacingScale:P0}");
            stackHeight = compressed;
        }
        else
        {
            _workAreaLog.Success();
        }

        // 只平移本次布局的起点，不改用户锚点：每帧按同一公式重算，结果稳定；
        // 落盘会在下一次拖动松手时按实际堆叠位置反算锚点。
        double startY = settings.Overlay.Y;
        if (hasWorkArea)
        {
            if (startY + stackHeight > workHeight)
                startY = Math.Max(0.0, workHeight - stackHeight);
            if (startY < 0.0) startY = 0.0;
        }

        double top = 0;
        bool first = true;

        foreach (CardSlot slot in _cards)
        {
            if (!slot.Shown) continue;

            if (!first) top += Math.Max(0, slot.Config.Spacing) * spacingScale;

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

            double cardHeight = slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight;
            double y = startY + top;
            if (hasWorkArea)
            {
                // 极端情况（卡片总高超过整个工作区）：每张卡各自夹在工作区内。
                if (y < 0.0) y = 0.0;
                if (y + cardHeight > workHeight) y = Math.Max(0.0, workHeight - cardHeight);
            }

            slot.Dialog.Overlay.X = x;
            slot.Dialog.Overlay.Y = y;
            slot.Dialog.Overlay.Monitor = settings.Overlay.Monitor;
            slot.Dialog.Overlay.MonitorDevice = settings.Overlay.MonitorDevice;
            slot.Dialog.Overlay.Locked = settings.Overlay.Locked;
            // “跟随卡片对齐”的文字需要知道整叠最终选了哪一边；由布局统一告知，
            // 避免每张卡按自己的中心各猜一个方向。
            slot.Window.SetResolvedCardAlignment(align);
            slot.Window.ApplyPosition(animate && !ReferenceEquals(slot, appearing));

            top += cardHeight;
            first = false;
        }
    }

    /// <summary>
    /// T-9：可见卡片的整叠高度（含按 <paramref name="spacingScale"/> 缩放后的间距）。
    /// gaps 单独返回，用于判断"压缩间距是否还能腾出空间"。
    /// </summary>
    double MeasureStackHeight(double spacingScale, out double gaps)
    {
        double height = 0;
        gaps = 0;
        bool first = true;
        foreach (CardSlot slot in _cards)
        {
            if (!slot.Shown) continue;

            if (!first)
            {
                double gap = Math.Max(0, slot.Config.Spacing) * spacingScale;
                height += gap;
                gaps += gap;
            }
            height += slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight;
            first = false;
        }
        return height;
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

        // T-10：高度变化走带动画的布局。以前走 Layout() 是无动画瞬移，
        // 补位动画刚结束时正好碰上一次高度变化，会在动画末尾再跳一下；
        // LayoutAnimated 的 AnimateTo 支持从当前位置重定向，不会和上一段动画打架。
        if (changed && !_suspended) LayoutAnimated();
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
