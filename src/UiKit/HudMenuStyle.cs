using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 托盘与卡片右键菜单共用的现代样式。
/// 设置窗里的菜单显式传 <c>dark</c>，跟随设置窗明暗；托盘 / 卡片菜单保持固定白底黑字，
/// 这样暗色叠层上仍有一块高对比菜单。样式集中在这里，避免一个亮色默认菜单配一个暗色菜单。
/// </summary>
public static class HudMenuStyle
{
    /// <summary>
    /// X-10：亮色菜单令牌直接取 Theme.xaml 的 Menu* 画刷，避免再维护一份重复颜色。
    /// 资源未加载（设计期 / 单测）时回退到原默认值。
    /// </summary>
    static (Color Bg, Color Text, Color Border, Color Hover, Color Muted) LightPalette()
    {
        if (Application.Current is not null)
        {
            SolidColorBrush bg = Application.Current.TryFindResource("MenuBg") as SolidColorBrush;
            SolidColorBrush text = Application.Current.TryFindResource("MenuText") as SolidColorBrush;
            SolidColorBrush border = Application.Current.TryFindResource("MenuBorder") as SolidColorBrush;
            SolidColorBrush hover = Application.Current.TryFindResource("MenuHover") as SolidColorBrush;
            SolidColorBrush muted = Application.Current.TryFindResource("MenuMuted") as SolidColorBrush;
            if (bg is not null && text is not null && border is not null && hover is not null && muted is not null)
                return (bg.Color, text.Color, border.Color, hover.Color, muted.Color);
        }

        return (Rgb(0xFF, 0xFF, 0xFF), Rgb(0x11, 0x11, 0x11), Rgb(0xD8, 0xD8, 0xD0),
                Rgb(0xF0, 0xF0, 0xE8), Rgb(0x55, 0x55, 0x55));
    }

    /// <summary>暗色菜单令牌：黑底白字，和设置窗暗色页面同一套 Endfield 基调。</summary>
    static readonly (Color Bg, Color Text, Color Border, Color Hover, Color Muted) Dark =
        (Rgb(0x0F, 0x12, 0x14), Rgb(0xF2, 0xF2, 0xED), Rgb(0x2A, 0x2F, 0x32),
         Rgb(0x1E, 0x24, 0x27), Rgb(0x9A, 0xA0, 0xA4));

    public static void Apply(ContextMenu menu) => Apply(menu, false);

    /// <summary>
    /// 一次 Apply 挂上的处理器。附加属性持有它，重复 Apply 只换调色板、不叠加订阅（K-41）。
    /// </summary>
    sealed class MenuHooks
    {
        public NotifyCollectionChangedEventHandler ItemsChanged;
        public RoutedEventHandler Opened;
        public RoutedEventHandler Closed;
        public EventHandler LayoutUpdated;
        public bool LayoutHooked;
    }

    static readonly DependencyProperty HooksProperty =
        DependencyProperty.RegisterAttached("Hooks", typeof(MenuHooks), typeof(HudMenuStyle),
            new PropertyMetadata(null));

    /// <summary>
    /// 套用菜单样式。<paramref name="dark"/> 只写菜单自己的资源字典，
    /// 不污染 Application 资源，所以设置窗切明暗不会把托盘菜单一起改掉。
    ///
    /// K-41：此方法会被宿主反复调用（每次右键 / 设置窗切换明暗），订阅必须是幂等的 ——
    /// 以前每次 Apply 都再挂一次 CollectionChanged / Opened / LayoutUpdated，
    /// LayoutUpdated 还永不解绑，处理器越积越多、每次布局都重复遍历一遍可视树。
    /// </summary>
    public static void Apply(ContextMenu menu, bool dark)
    {
        if (menu is null) return;

        var style = Application.Current?.TryFindResource("HudContextMenu") as Style;
        var itemStyle = Application.Current?.TryFindResource("HudMenuItem") as Style;
        if (style is null || itemStyle is null)
        {
            Log.Warn("右键菜单样式缺失，本次使用系统默认菜单");
            return;
        }

        // DynamicResource 在菜单自己的资源里先命中；后套 Style 时菜单背景 / 文字会立即取到。
        ApplyPalette(menu, dark ? Dark : LightPalette());

        // 菜单的 ContentPresenter 会把字符串生成 TextBlock，而全局 TextBlock 样式会强制
        // 前景色。这里的局部隐式样式优先于 Application 资源，白底 / 黑底都不会再出现
        // "白底白字"。
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            new DynamicResourceExtension("MenuText")));
        menu.Resources[typeof(TextBlock)] = textStyle;

        menu.Style = style;
        menu.ItemContainerStyle = itemStyle;

        if (menu.GetValue(HooksProperty) is MenuHooks existing)
        {
            // 重复 Apply：调色板与样式已刷新，已有条目也要按当前明暗重排一次标题，
            // 但绝不再订阅事件。
            foreach (object entry in menu.Items)
                NormalizeHeader(entry);
            if (menu.IsOpen) NormalizeTextForeground(menu);
            return;
        }

        var hooks = new MenuHooks();

        // 更早一层：菜单项一加进来就把字符串标题换成带本地 MenuText 的 TextBlock。
        // 这样不依赖 Popup 打开后的视觉树，也不会先闪一帧全局白字。
        foreach (object entry in menu.Items)
            NormalizeHeader(entry);

        if (menu.Items is INotifyCollectionChanged changed)
        {
            hooks.ItemsChanged = (s, e) =>
            {
                if (e.NewItems is null) return;
                foreach (object entry in e.NewItems) NormalizeHeader(entry);
            };
            changed.CollectionChanged += hooks.ItemsChanged;
        }

        // Popup 里的文字是 MenuItem 内容，全局 TextBlock 样式可能已经在模板展开时
        // 把前景色写死成 TextPrimary；仅靠资源字典兜不住。菜单打开后遍历一次，
        // 只给没有本地前景色的 TextBlock 写 MenuText，保留快捷键的 MenuMuted 和图标的 Accent。
        hooks.Opened = (s, e) =>
        {
            // 打开期间才需要 LayoutUpdated 兜底（极少数模板在 Opened 之后才生成内容）；
            // 关闭时立刻解绑，菜单关闭后不再参与宿主布局回调（K-41）。
            if (!hooks.LayoutHooked)
            {
                menu.LayoutUpdated += hooks.LayoutUpdated;
                hooks.LayoutHooked = true;
            }
            NormalizeTextForeground(menu);
            menu.Dispatcher.BeginInvoke(new Action(() => NormalizeTextForeground(menu)),
                DispatcherPriority.Loaded);
        };
        hooks.LayoutUpdated = (s, e) => NormalizeTextForeground(menu);
        hooks.Closed = (s, e) =>
        {
            if (!hooks.LayoutHooked) return;
            menu.LayoutUpdated -= hooks.LayoutUpdated;
            hooks.LayoutHooked = false;
        };

        menu.Opened += hooks.Opened;
        menu.Closed += hooks.Closed;
        menu.SetValue(HooksProperty, hooks);
    }

    static void NormalizeHeader(object entry)
    {
        if (entry is not MenuItem item) return;
        object header = item.Header;

        if (header is string text)
        {
            var block = new TextBlock { Text = text };
            block.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
            item.Header = block;
            return;
        }

        NormalizeHeaderElement(header);
        foreach (object sub in item.Items)
            NormalizeHeader(sub);
    }

    static void NormalizeHeaderElement(object header)
    {
        if (header is TextBlock text)
        {
            if (text.ReadLocalValue(TextBlock.ForegroundProperty) == DependencyProperty.UnsetValue)
                text.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
            return;
        }

        if (header is Panel panel)
        {
            foreach (UIElement child in panel.Children) NormalizeHeaderElement(child);
            return;
        }

        if (header is Border border)
        {
            NormalizeHeaderElement(border.Child);
            return;
        }

        if (header is ContentControl control)
            NormalizeHeaderElement(control.Content);
    }

    static void NormalizeTextForeground(ContextMenu menu)
    {
        if (menu is null) return;
        foreach (object entry in menu.Items)
            if (entry is MenuItem item) NormalizeItem(item);
    }

    static void NormalizeItem(MenuItem item)
    {
        if (item is null) return;
        item.ApplyTemplate();
        Walk(item);
        foreach (object entry in item.Items)
            if (entry is MenuItem child) NormalizeItem(child);
    }

    static void Walk(DependencyObject node)
    {
        if (node is TextBlock text)
        {
            // 有本地值（快捷键 MenuMuted、图标 Accent、自定义色）一律不动；
            // ContentPresenter 生成的纯文字没有本地值，正好补 MenuText。
            if (text.ReadLocalValue(TextBlock.ForegroundProperty) == DependencyProperty.UnsetValue)
                text.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            Walk(VisualTreeHelper.GetChild(node, i));
    }

    static void ApplyPalette(ContextMenu menu, (Color Bg, Color Text, Color Border, Color Hover, Color Muted) palette)
    {
        menu.Resources["MenuBg"] = new SolidColorBrush(palette.Bg);
        menu.Resources["MenuText"] = new SolidColorBrush(palette.Text);
        menu.Resources["MenuBorder"] = new SolidColorBrush(palette.Border);
        menu.Resources["MenuHover"] = new SolidColorBrush(palette.Hover);
        menu.Resources["MenuMuted"] = new SolidColorBrush(palette.Muted);
    }

    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
