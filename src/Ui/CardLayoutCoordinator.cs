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

    /// <summary>
    /// 2.45.4：长文本把卡片撑宽后，靠右锚点 / 右对齐会把窗口推出工作区，
    /// 用户看到的是"右边一截内容不见了"。宽度、高度各自用独立节流：
    /// 叠放高度正常（Success 会复位状态）时不能把宽度告警一起吞掉。
    /// </summary>
    readonly LogThrottle _cardWidthLog = new("卡片宽度超出工作区", 30000);
    readonly LogThrottle _cardHeightLog = new("卡片高度超出工作区", 30000);

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
        double workWidth = double.PositiveInfinity;
        try
        {
            // G-14：优先按设备名定位（多屏热插拔后整数序号可能已指到别的屏）。
            ScreenInfo screen = ScreenGeometry.Get(settings.Overlay.Monitor, settings.Overlay.MonitorDevice);
            if (screen is not null && screen.ScaleY > 0.1)
                workHeight = screen.WorkHeight / screen.ScaleY;
            if (screen is not null && screen.ScaleX > 0.1)
                workWidth = screen.WorkWidth / screen.ScaleX;
        }
        catch (Exception ex)
        {
            // 拿不到显示器信息时按"不夹取"继续布局，至少不能让整叠消失。
            Log.Warn($"读取工作区尺寸失败，跳过堆叠夹取: {ex.Message}");
        }
        bool hasWorkWidth = !double.IsInfinity(workWidth) && workWidth > 1.0;

        // T-11：纵向几何统一由 PlanStack 计算。拖动限制、松手换算与这里必须用同一份结果，
        // 否则「拖动时能到、松手后被 Layout 拉回」——那正是拖到屏幕下部被吸回的根因。
        StackPlan plan = PlanVisible(workHeight, settings.Overlay.Y);
        if (plan.HasWorkArea && plan.StackHeight > workHeight)
        {
            _workAreaLog.Failure(
                $"可见卡片总高 {plan.StackHeight:0} DIP 超出工作区 {workHeight:0} DIP，已压缩间距 {plan.SpacingScale:P0}");
        }
        else
        {
            _workAreaLog.Success();
        }

        // 只平移本次布局的起点，不改用户锚点：每帧按同一公式重算，结果稳定；
        // 落盘会在下一次拖动松手时按实际堆叠位置反算锚点。
        double startY = plan.StartY;
        if (plan.HasWorkArea && Math.Abs(startY - settings.Overlay.Y) > 0.5)
        {
            // 诊断：锚点在工作区外时整叠会被平移到可容纳位置，松手换算必须按同一口径，
            // 否则下一次拖动的落点会持续偏移（Debug 级，默认等级下不写日志）。
            Log.Debug($"整叠起始 Y 被工作区夹取: 锚点={settings.Overlay.Y:0.#} → 起始={startY:0.#} "
                      + $"整叠={plan.StackHeight:0.#} 工作区={plan.WorkHeight:0.#}");
        }

        int visibleIndex = 0;

        foreach (CardSlot slot in _cards)
        {
            if (!slot.Shown) continue;

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
            }

            // T-9 / 2.45.4：纵向只夹 Y 还不够——信息 / 文本卡的文字变长会把窗口撑宽，
            // 靠右锚点或右对齐时右半边直接落到工作区外，用户看到"内容被裁掉"。
            // 与纵向一致：只平移、不缩放。夹取用窗口实际宽度（CardWidth 已扣掉投影留白，
            // 直接拿它算会残留 10px 出屏）；卡片本身比工作区还宽时平移无法补救，
            // 由 TextCanvas 的宽度上限裁剪（画省略号）兜底，这里留具体数值的日志。
            double cardWidth = slot.Window.CardWidth;
            if (hasWorkWidth)
            {
                if (cardWidth > workWidth)
                {
                    _cardWidthLog.Failure(
                        $"卡片宽 {cardWidth:0} DIP 超出工作区 {workWidth:0} DIP，左右内容会被屏幕裁掉");
                }
                else
                {
                    double windowWidth = cardWidth + (settings.Overlay.Shadow ? 20 : 0);
                    double maxX = Math.Max(0, workWidth - windowWidth);
                    if (x < 0) x = 0;
                    if (x > maxX) x = maxX;
                }
            }
            _knownX[slot] = x;

            double cardHeight = slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight;
            double y = startY + plan.Tops[visibleIndex];
            if (plan.HasWorkArea)
            {
                // 2.45.4：单张卡就比整个工作区高时，平移只能把顶部对齐（y=0），
                // 底部一定会被屏幕裁掉。这是"必须裁"的场景，给出具体数值而不是静默。
                if (cardHeight > workHeight)
                {
                    _cardHeightLog.Failure(
                        $"卡片高 {cardHeight:0} DIP 超出工作区 {workHeight:0} DIP，底部内容会被屏幕裁掉");
                }

                // 极端情况（卡片总高超过整个工作区）：每张卡各自夹在工作区内。
                y = ClampCardY(y, cardHeight, plan.WorkHeight);
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

            visibleIndex++;
        }
    }

    /// <summary>
    /// T-11：一帧卡片堆叠的纵向几何。布局、拖动限制与松手换算共用这一份计算——
    /// 三处各自算一套 clamp 是「卡片拖到屏幕下部松手被吸回」的根因。
    /// 纯计算，不碰窗口 / 设置，自检可直接断言。
    /// </summary>
    internal struct StackPlan
    {
        /// <summary>间距压缩比例（1 = 原始间距；整叠放不下时按比例收掉空隙）。</summary>
        public double SpacingScale;
        /// <summary>压缩后的整叠高度（只算可见卡片）。</summary>
        public double StackHeight;
        /// <summary>按锚点夹取后的整叠起始 Y；拿不到工作区时等于锚点。</summary>
        public double StartY;
        /// <summary>每张可见卡片相对 <see cref="StartY"/> 的偏移。</summary>
        public double[] Tops;
        /// <summary>每张可见卡片的高度。</summary>
        public double[] Heights;
        /// <summary>目标屏工作区高度（DIP）；无效时为 <see cref="double.PositiveInfinity"/>。</summary>
        public double WorkHeight;
        /// <summary>是否拿到有效工作区（拿不到时整叠不做夹取）。</summary>
        public bool HasWorkArea;
    }

    /// <summary>
    /// 把可见卡片按顺序压成整叠几何。整叠超出工作区时先按比例收掉间距（保留卡片高度），
    /// 仍超出则起始 Y 固定为 0，由 <see cref="ClampCardY"/> 逐卡塞进工作区。
    /// </summary>
    internal static StackPlan PlanStack(double anchorY, double workHeight, double[] heights, double[] spacings)
    {
        int count = heights?.Length ?? 0;
        var tops = new double[count];
        var plan = new StackPlan
        {
            SpacingScale = 1.0,
            Tops = tops,
            Heights = heights ?? Array.Empty<double>(),
            WorkHeight = workHeight,
            HasWorkArea = workHeight > 1.0 && !double.IsInfinity(workHeight) && !double.IsNaN(workHeight)
        };

        // 先用原始间距量一次总高，再判断能否靠压缩间距塞进工作区。
        double rawHeight = 0;
        double gaps = 0;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                double gap = Math.Max(0.0, SpacingAt(spacings, i));
                rawHeight += gap;
                gaps += gap;
            }
            rawHeight += heights[i];
        }

        if (plan.HasWorkArea && rawHeight > workHeight && gaps > 0)
            plan.SpacingScale = Math.Clamp((workHeight - (rawHeight - gaps)) / gaps, 0.0, 1.0);

        double top = 0;
        for (int i = 0; i < count; i++)
        {
            if (i > 0) top += Math.Max(0.0, SpacingAt(spacings, i)) * plan.SpacingScale;
            tops[i] = top;
            top += heights[i];
        }

        plan.StackHeight = top;
        plan.StartY = ClampStackStartY(anchorY, top, plan.WorkHeight, plan.HasWorkArea);
        return plan;
    }

    static double SpacingAt(double[] spacings, int index) =>
        spacings is not null && index < spacings.Length ? spacings[index] : 0.0;

    /// <summary>整叠起始 Y 的合法范围：0 到「工作区 - 整叠高」（放不下时只能取 0）。</summary>
    internal static double ClampStackStartY(double anchorY, double stackHeight, double workHeight, bool hasWorkArea)
    {
        if (!hasWorkArea) return anchorY;

        double startY = anchorY;
        if (startY + stackHeight > workHeight) startY = Math.Max(0.0, workHeight - stackHeight);
        if (startY < 0.0) startY = 0.0;
        return startY;
    }

    /// <summary>单张卡片自身的纵向夹取（整叠比工作区还高时逐卡塞进去）。</summary>
    internal static double ClampCardY(double y, double cardHeight, double workHeight)
    {
        if (!(workHeight > 1.0)) return y;
        if (y < 0.0) return 0.0;
        if (y + cardHeight > workHeight) return Math.Max(0.0, workHeight - cardHeight);
        return y;
    }

    /// <summary>
    /// T-11：被抓住的卡片能落到的顶部 Y（DIP，工作区坐标）。拖动与松手共用。
    /// 整叠放得下：落点必须让整叠仍留在工作区内（对应起始 Y ∈ [0, 工作区-整叠高]）；
    /// 整叠超高：Layout 只能把整叠钉在顶部并逐卡夹取，此时卡片只有一个合法位置，
    /// 拖动中就用它挡住用户——用户看得见边界，松手后不会再跳。
    /// </summary>
    internal static double ClampCardTop(double cardTop, int index, StackPlan plan)
    {
        if (!plan.HasWorkArea || index < 0 || index >= plan.Tops.Length) return cardTop;

        double height = plan.Heights[index];
        if (plan.StackHeight > plan.WorkHeight)
            return ClampCardY(plan.Tops[index], height, plan.WorkHeight);

        double min = plan.Tops[index];
        double max = Math.Max(min, plan.WorkHeight - plan.StackHeight + plan.Tops[index]);
        // 兜底：卡片本身比工作区还高时只保留上沿，避免 min > max。
        max = Math.Max(min, Math.Min(max, plan.WorkHeight - height));

        if (cardTop < min) return min;
        if (cardTop > max) return max;
        return cardTop;
    }

    /// <summary>
    /// T-11：被抓住的卡片能落到的左边缘（DIP）。至少在屏内留一部分可见，靠近边缘 16 DIP 吸附。
    /// 右对齐时卡片 X 由 Layout 的 <c>max(0, 右边缘 - 卡宽)</c> 决定，左边缘不能为负，
    /// 否则松手会被推到 0（横向的“吸回”）；这里用同一口径提前挡住。
    /// </summary>
    internal static double ClampCardLeft(double cardLeft, double cardWidth, double workWidth, bool rightAligned)
    {
        if (!(workWidth > 1.0)) return cardLeft;

        double width = Math.Max(40.0, cardWidth);
        double visible = Math.Min(80.0, workWidth / 3.0);
        double min = visible - width;
        if (rightAligned) min = Math.Max(0.0, min);
        double max = Math.Max(min, workWidth - visible);

        if (cardLeft < min + 16.0) return min;
        if (cardLeft > max - 16.0) return max;
        return cardLeft;
    }

    /// <summary>DIP → 物理像素（锚点 / 尺寸都按目标屏 DPI 换算，跨屏不双重缩放）。</summary>
    internal static int DipToPhysical(double dip, double scale, int origin) =>
        origin + (int)Math.Round(dip * (scale > 0.1 ? scale : 1.0));

    /// <summary>物理像素 → DIP（<paramref name="origin"/> 是该屏工作区左上角的物理坐标）。</summary>
    internal static double PhysicalToDip(int physical, double scale, int origin) =>
        (physical - origin) / (scale > 0.1 ? scale : 1.0);

    /// <summary>当前可见卡片的纵向几何（拖动限制 / 松手换算用）。</summary>
    public StackPlan PlanVisible(double workHeight, double anchorY)
    {
        var heights = new List<double>(_cards.Count);
        var spacings = new List<double>(_cards.Count);
        foreach (CardSlot slot in _cards)
        {
            if (!slot.Shown) continue;
            heights.Add(slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight);
            spacings.Add(slot.Config is null ? 0.0 : slot.Config.Spacing);
        }
        return PlanStack(anchorY, workHeight, heights.ToArray(), spacings.ToArray());
    }

    /// <summary>卡片在可见序列里的下标（与 <see cref="PlanVisible"/> 的 Tops 下标一致）。</summary>
    public int ShownIndexOf(CardSlot slot)
    {
        int index = 0;
        foreach (CardSlot card in _cards)
        {
            if (!card.Shown) continue;
            if (ReferenceEquals(card, slot)) return index;
            index++;
        }
        return -1;
    }

    /// <summary>最近一次布局算出的整叠宽度（松手换算右对齐锚点时与 Layout 用同一个值）。</summary>
    public double StackWidth => _stackWidth;

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
