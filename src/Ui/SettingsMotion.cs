using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MirrorpondHUD.Ui;

/// <summary>
/// 设置窗专用动效（2.45.3，只服务 <see cref="SettingsWindow"/>）。
///
/// 节奏取自 docs/UI/endfield-motion.md：短（0.12–0.20s）、硬（只动透明度 / 位移 / 高度，不用弹跳）、
/// 不过冲（QuadraticEase EaseOut，或线性），且退场比入场短。窗口级动作的时长都写在下面的常量里，
/// 改节奏只改这里，不散落到事件处理器。
///
/// 三个必须尊重的开关，任一命中就**直接到终态**，绝不把窗口留在 Opacity 0 或错误高度：
/// · 用户「减少动效」（<see cref="SyncReduceMotion"/>，来自 Behavior.ReduceMotion）；
/// · 系统关闭「显示动画」（<see cref="SystemParameters.ClientAreaAnimation"/>）；
/// · 截图 / 自检（<see cref="CardMotion.SuppressedForCapture"/>，与卡片动效共用同一个开关，
///   不引入第二套全局状态）。
///
/// 实现约定：动画一律用 BeginAnimation 直接挂在元素上，不建 Storyboard 时钟树；
/// 位移用的 TranslateTransform / 缩放用的 ScaleTransform 按元素缓存（ConditionalWeakTable），
/// 同一元素反复触发时复用同一个 Transform，不每次分配。
/// </summary>
internal static class SettingsMotion
{
    /// <summary>窗口开场：淡入 200ms + 0.985 → 1 轻微缩放（8px 位移的窗口级版本，不位移）。</summary>
    public const int WindowInMs = 200;

    /// <summary>窗口退场只淡出 120ms：走的时候不挡路，且绝不阻塞保存提示 / 退出流程。</summary>
    public const int WindowOutMs = 120;

    /// <summary>页面切换：淡入 180ms + 8px 位移归位。</summary>
    public const int PageMs = 180;

    /// <summary>折叠分组展开 / 收起：高度 200ms（规范 180–240ms 区间）。</summary>
    public const int SectionMs = 200;

    /// <summary>左侧导航项悬停 / 选中：背景块与 3px 指示条 140ms。</summary>
    public const int NavMs = 140;

    /// <summary>明暗切换：内容区不透明度起伏 160ms（整窗透明会透出桌面，所以只动内容）。</summary>
    public const int ThemeMs = 160;

    /// <summary>状态行文案更新淡入 150ms；只淡入不淡出，失败 / 警告因此常显。</summary>
    public const int StatusMs = 150;

    /// <summary>共享缓动：二次 EaseOut，起步快、收尾稳，无过冲。</summary>
    static readonly QuadraticEase EaseOut = CreateEase();

    /// <summary>位移 Transform 缓存：同一元素反复做切页 / 分组动画时复用。</summary>
    static readonly ConditionalWeakTable<UIElement, TranslateTransform> Offsets = new();

    /// <summary>窗口缩放 Transform 缓存。</summary>
    static readonly ConditionalWeakTable<FrameworkElement, ScaleTransform> Scales = new();

    /// <summary>导航项模板部件缓存（Fill / Bar / HoverFill 与指示条位移）。</summary>
    static readonly ConditionalWeakTable<DependencyObject, NavSlot> Navs = new();

    static bool _reduceMotion;

    /// <summary>
    /// 退场动画的作废令牌：淡出途中窗口被重新打开（Show）时，
    /// 旧的 Completed 回调必须放弃隐藏，否则刚打开的窗口会立刻被藏掉。
    /// </summary>
    static int _fadeToken;

    /// <summary>设置树里的「减少动效」变化时同步一次（LoadUi / 应用设置后调用）。</summary>
    public static void SyncReduceMotion(bool reduce) => _reduceMotion = reduce;

    /// <summary>动效是否可用：截图 / 减少动效 / 系统动画开关，任意一项关闭即直接到终态。</summary>
    public static bool Enabled =>
        !CardMotion.SuppressedForCapture && !_reduceMotion && SystemParameters.ClientAreaAnimation;

    static QuadraticEase CreateEase()
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    // ==================== 窗口 ====================

    /// <summary>
    /// 窗口开场：整窗淡入 + 从 0.985 放大到 1（RenderTransformOrigin 居中，不移动窗口位置）。
    /// 每次 Show 都会调用，隐藏后再打开同样有入场；截图模式自动只落终态。
    /// </summary>
    public static void PlayWindowIn(FrameworkElement root)
    {
        if (root is null) return;

        // 先作废可能还在跑的退场回调，避免它把这次刚打开的窗口藏掉。
        _fadeToken++;
        root.BeginAnimation(UIElement.OpacityProperty, null);
        root.Opacity = 1;

        ScaleTransform scale = ScaleOf(root);
        if (scale is not null)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        if (!Enabled) return;

        // 透明度与缩放各走各的曲线，但同长：面板「先看得清、再落稳」只需一段，不必拖到 300ms。
        var fade = new DoubleAnimation(0, 1, Ms(WindowInMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        root.BeginAnimation(UIElement.OpacityProperty, fade);

        if (scale is null) return;
        var zoom = new DoubleAnimation(0.985, 1, Ms(WindowInMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
    }

    /// <summary>
    /// 关闭前淡出 120ms，完成时回调 <paramref name="done"/> 真正隐藏。
    /// 返回 false 表示这次不播（动效关闭 / 窗口本来就不可见），调用方必须立刻自己隐藏，
    /// 不能因为动画把关闭流程挂起——保存提示与退出流程都不允许被延时。
    /// </summary>
    public static bool TryFadeWindowOut(FrameworkElement root, Action done)
    {
        if (root is null || done is null || !Enabled || !root.IsVisible) return false;

        int token = ++_fadeToken;
        root.BeginAnimation(UIElement.OpacityProperty, null);
        root.Opacity = 1;

        var fade = new DoubleAnimation(1, 0, Ms(WindowOutMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (sender, args) =>
        {
            // 淡出途中被重新打开：这次退场作废，不能隐藏用户的窗口。
            if (token != _fadeToken) return;
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
            done();
        };
        root.BeginAnimation(UIElement.OpacityProperty, fade);
        return true;
    }

    /// <summary>窗口缩放 Transform：首次用时创建并居中，之后复用。</summary>
    static ScaleTransform ScaleOf(FrameworkElement root)
    {
        if (Scales.TryGetValue(root, out ScaleTransform cached)) return cached;

        var scale = new ScaleTransform(1, 1);
        Scales.Add(root, scale);
        // 缩放锚点必须在窗口中心，否则开场会从左上角「推」出来。
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = scale;
        return scale;
    }

    // ==================== 页面 / 分组内容 ====================

    /// <summary>
    /// 淡入 + 水平位移归位（页面切换、以及以后任何「内容换了一页」的场景共用）。
    ///
    /// 起点是透明 + <paramref name="fromX"/>，终点是 1 / 0；FillBehavior.Stop 让动画结束后
    /// 回到基础值，所以连续快速切换时后来的一次直接替换前一次时钟，不会留下半透明或偏位的中间态。
    /// </summary>
    public static void FadeSlide(UIElement element, double fromX, int durationMs)
    {
        if (element is null) return;

        ResetElement(element);
        if (!Enabled) return;

        var fade = new DoubleAnimation(0, 1, Ms(durationMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);

        if (Math.Abs(fromX) < 0.01) return;
        var slide = new DoubleAnimation(fromX, 0, Ms(durationMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        OffsetOf(element).BeginAnimation(TranslateTransform.XProperty, slide);
    }

    /// <summary>把元素上的新动效压到终态（透明度 1、位移 0）。禁用动效与重复触发都先走这里。</summary>
    public static void ResetElement(UIElement element)
    {
        if (element is null) return;

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;

        if (!Offsets.TryGetValue(element, out TranslateTransform offset)) return;
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        offset.X = 0;
        offset.BeginAnimation(TranslateTransform.YProperty, null);
        offset.Y = 0;
    }

    static TranslateTransform OffsetOf(UIElement element)
    {
        if (Offsets.TryGetValue(element, out TranslateTransform cached)) return cached;

        var offset = new TranslateTransform();
        Offsets.Add(element, offset);
        element.RenderTransform = offset;
        return offset;
    }

    // ==================== 折叠分组 ====================

    /// <summary>
    /// 2.45.5：卡片设置体的展开 / 收起，复用分组折叠的同一套高度动画。
    /// 卡片的 Body 是带 Visibility 绑定的 StackPanel（不是 Expander 模板部件），
    /// 所以这里自己接管本地 Visibility：收起动画结束后再清掉本地值，让绑定恢复 Collapsed。
    /// </summary>
    public static void AnimateBody(FrameworkElement body, bool expand)
    {
        if (body is null) return;

        double current = Math.Max(0, body.ActualHeight);
        body.BeginAnimation(FrameworkElement.HeightProperty, null);

        if (!Enabled)
        {
            body.ClearValue(FrameworkElement.HeightProperty);
            body.ClipToBounds = false;
            body.ClearValue(UIElement.VisibilityProperty);
            return;
        }

        double width = body.ActualWidth > 1
            ? body.ActualWidth
            : Math.Max(1, (body.Parent as FrameworkElement)?.ActualWidth ?? body.DesiredSize.Width);

        if (expand)
        {
            body.Visibility = Visibility.Visible;
            body.ClearValue(FrameworkElement.HeightProperty);
            body.Measure(new Size(width, double.PositiveInfinity));
            double target = Math.Max(0, body.DesiredSize.Height);

            body.ClipToBounds = true;
            body.Height = current;
            var grow = new DoubleAnimation(current, target, Ms(SectionMs))
            {
                EasingFunction = EaseOut,
                FillBehavior = FillBehavior.Stop
            };
            grow.Completed += (sender, args) =>
            {
                body.BeginAnimation(FrameworkElement.HeightProperty, null);
                body.ClearValue(FrameworkElement.HeightProperty);
                body.ClipToBounds = false;
                // 本地 Visibility 必须清掉，否则绑定永远看不到 Collapsed。
                body.ClearValue(UIElement.VisibilityProperty);
            };
            body.BeginAnimation(FrameworkElement.HeightProperty, grow);
            return;
        }

        body.Visibility = Visibility.Visible;
        body.Height = current;
        body.ClipToBounds = true;
        var shrink = new DoubleAnimation(current, 0, Ms(SectionMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        shrink.Completed += (sender, args) =>
        {
            body.BeginAnimation(FrameworkElement.HeightProperty, null);
            body.ClearValue(FrameworkElement.HeightProperty);
            body.ClipToBounds = false;
            body.ClearValue(UIElement.VisibilityProperty);
        };
        body.BeginAnimation(FrameworkElement.HeightProperty, shrink);
    }

    /// <summary>
    /// 折叠分组展开 / 收起：对模板里的内容呈现器做高度动画 + 裁切，避免瞬间跳变。
    ///
    /// 关键取舍：
    /// · 展开与收起都从**当前实际高度**起算，靠连点时从半路折返，不会跳回 0 再重来；
    /// · 收起期间用本地 Visibility 压过模板触发器，**收起动画结束才真正折叠**；
    /// · 结束时清掉本地高度，让下次展开重新按内容量高度（否则会带着 0 高度看不见）。
    /// 高度动画会每帧重排当前页，但分组子项都是轻量控件，200ms 内可接受；比透明度+位移更符合「收放」语义。
    /// </summary>
    public static void AnimateSection(Expander expander, ContentPresenter body, bool expand)
    {
        if (expander is null || body is null) return;

        double current = Math.Max(0, body.ActualHeight);
        body.BeginAnimation(FrameworkElement.HeightProperty, null);

        if (!Enabled)
        {
            body.ClearValue(FrameworkElement.HeightProperty);
            body.ClipToBounds = false;
            // 交回模板触发器：展开 = 可见、收起 = 折叠。
            body.ClearValue(UIElement.VisibilityProperty);
            return;
        }

        double width = body.ActualWidth > 1 ? body.ActualWidth : Math.Max(1, expander.ActualWidth);

        if (expand)
        {
            body.Visibility = Visibility.Visible;
            body.ClearValue(FrameworkElement.HeightProperty);
            body.Measure(new Size(width, double.PositiveInfinity));
            double target = Math.Max(0, body.DesiredSize.Height);

            body.ClipToBounds = true;
            body.Height = current;
            var grow = new DoubleAnimation(current, target, Ms(SectionMs))
            {
                EasingFunction = EaseOut,
                FillBehavior = FillBehavior.Stop
            };
            grow.Completed += (sender, args) =>
            {
                body.BeginAnimation(FrameworkElement.HeightProperty, null);
                body.ClearValue(FrameworkElement.HeightProperty);
                body.ClipToBounds = false;
            };
            body.BeginAnimation(FrameworkElement.HeightProperty, grow);
            return;
        }

        body.Visibility = Visibility.Visible;
        body.Height = current;
        body.ClipToBounds = true;
        var shrink = new DoubleAnimation(current, 0, Ms(SectionMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        shrink.Completed += (sender, args) =>
        {
            body.BeginAnimation(FrameworkElement.HeightProperty, null);
            body.ClearValue(FrameworkElement.HeightProperty);
            body.ClipToBounds = false;
            // 动画走完才折叠：模板触发器负责把内容收起来（「收起完成后才折叠」）。
            body.ClearValue(UIElement.VisibilityProperty);
        };
        body.BeginAnimation(FrameworkElement.HeightProperty, shrink);
    }

    // ==================== 左侧导航 ====================

    /// <summary>
    /// 导航项选中态：强调底块 0 → 0.78、3px 指示条 0 → 1 且从 -3px 滑到 0（140ms）。
    /// 只改透明度与 Transform，不参与布局，所以悬停 / 选中不会让文字位置抖动。
    /// 正文字色与字重仍由模板触发器在选中那一刻切换（规范：状态不只靠动效表达）。
    /// </summary>
    public static void NavSelected(DependencyObject item, bool selected, bool animate = true)
    {
        NavSlot slot = NavOf(item);
        if (slot.Fill is not null) FadeNav(slot.Fill, selected ? 0.78 : 0, animate);

        if (slot.Bar is null) return;
        FadeNav(slot.Bar, selected ? 1 : 0, animate);

        TranslateTransform offset = slot.BarOffset;
        if (offset is null) return;
        if (!animate || !Enabled)
        {
            offset.BeginAnimation(TranslateTransform.XProperty, null);
            offset.X = selected ? 0 : -3;
            return;
        }

        offset.BeginAnimation(TranslateTransform.XProperty, null);
        offset.X = 0;
        var slide = new DoubleAnimation(-3, 0, Ms(NavMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        offset.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    /// <summary>导航项悬停：HoverBg 底块 0 → 1 / 1 → 0，只换透明度，不放大、不上浮。</summary>
    public static void NavHover(DependencyObject item, bool over)
    {
        NavSlot slot = NavOf(item);
        if (slot.HoverFill is null) return;
        FadeNav(slot.HoverFill, over ? 1 : 0, true);
    }

    /// <summary>
    /// 导航底块的不透明度过渡。基础值直接写成目标值，动画用 Stop 回落——
    /// 这样中途被打断时读到的当前值就是动画值，下一次从半路继续，不会闪一下。
    /// </summary>
    static void FadeNav(UIElement element, double target, bool animate)
    {
        double from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = target;

        if (!animate || !Enabled || Math.Abs(from - target) < 0.001) return;
        var fade = new DoubleAnimation(from, target, Ms(NavMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    static NavSlot NavOf(DependencyObject item)
    {
        if (Navs.TryGetValue(item, out NavSlot cached)) return cached;

        var slot = new NavSlot();
        if (item is Control control && control.Template is ControlTemplate template)
        {
            slot.Fill = template.FindName("Fill", control) as Border;
            slot.Bar = template.FindName("Bar", control) as Border;
            slot.HoverFill = template.FindName("HoverFill", control) as Border;
        }

        if (slot.Bar is not null)
        {
            // 模板内联的 RenderTransform 会被 WPF 冻结（而且可能被所有模板实例共享），
            // 直接对它 BeginAnimation 会抛「已密封或已冻结」。这里换成代码创建的实例再动。
            slot.BarOffset = slot.Bar.RenderTransform as TranslateTransform;
            if (slot.BarOffset is null || slot.BarOffset.IsFrozen)
            {
                slot.BarOffset = new TranslateTransform(-3, 0);
                slot.Bar.RenderTransform = slot.BarOffset;
            }
        }

        // 模板还没套用时部件全是 null：这次不缓存，等下次事件再解析。
        if (slot.Fill is null && slot.Bar is null && slot.HoverFill is null) return slot;

        Navs.Add(item, slot);
        return slot;
    }

    // ==================== 主题 / 状态行 ====================

    /// <summary>
    /// 明暗切换后给内容区一次 160ms 的不透明度起伏（1 → 0.84 → 1）。
    /// 不动整窗透明度：窗口是 AllowsTransparency，整窗变淡会直接透出桌面；
    /// 只让内容在已换过颜色的底板上轻落一次，看起来像交叉淡化，也不会闪白 / 闪黑。
    /// </summary>
    public static void PlayThemeDip(FrameworkElement content)
    {
        if (content is null) return;

        content.BeginAnimation(UIElement.OpacityProperty, null);
        content.Opacity = 1;
        if (!Enabled) return;

        var dip = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        dip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0), EaseOut));
        dip.KeyFrames.Add(new EasingDoubleKeyFrame(0.84, KeyTime.FromPercent(0.4), EaseOut));
        dip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(Ms(ThemeMs)), EaseOut));
        content.BeginAnimation(UIElement.OpacityProperty, dip);
    }

    /// <summary>
    /// 状态行文案更新时淡入 150ms。只淡入、从不淡出：失败 / 警告必须一直看得见，
    /// 不允许「过一会儿自己消失」。空文案（清空状态行）直接落终态。
    /// </summary>
    public static void FadeStatusIn(TextBlock status)
    {
        if (status is null) return;

        status.BeginAnimation(UIElement.OpacityProperty, null);
        status.Opacity = 1;
        if (!Enabled || string.IsNullOrEmpty(status.Text)) return;

        var fade = new DoubleAnimation(0, 1, Ms(StatusMs))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop
        };
        status.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>导航项模板里参与动效的部件（第一次解析后缓存）。</summary>
    sealed class NavSlot
    {
        public Border Fill;
        public Border Bar;
        public Border HoverFill;
        public TranslateTransform BarOffset;
    }
}
