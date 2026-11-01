using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;

namespace MirrorpondHUD.Ui.Controls
{
    // =====================================================================================
    // 卡片内浮层控件：模态 / 对话框 / 浮层 / 提示 / 右键菜单 / Toast。
    //
    // 设计约束：
    // - 只用 WPF 原生控件 + Contracts 里的 StyleSettings，不引用宿主（OverlayController 等）；
    // - 所有视觉都由 ApplyStyle(StyleSettings, double) 从主题派生，不依赖系统白底 / 资源字典；
    // - 不用系统 MessageBox / Window；
    // - Popup / ContextMenu 是独立 HWND，Close / Detach / Dismiss 时必须停掉动画和定时器并关闭。
    // =====================================================================================

    /// <summary>Toast 的语气：信息、成功、警告、危险。颜色由主题令牌 + 语气色混出来。</summary>
    public enum CardToastTone
    {
        Info,
        Success,
        Warning,
        Danger
    }

    /// <summary>对话框的按钮组合。</summary>
    public enum CardDialogButtons
    {
        Ok,
        OkCancel,
        YesNo
    }

    /// <summary>
    /// 卡片内模态遮罩：半透明遮罩 + 居中主题面板。
    ///
    /// 用法：
    /// <code>
    /// var modal = new CardModal { Body = new TextBlock { Text = "内容" } };
    /// modal.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
    /// modal.Show(this);   // this 是卡片内容根（Grid 之类的 Panel）
    /// ...
    /// modal.Close();
    /// </code>
    ///
    /// 不用系统 MessageBox / Window：模态只在卡片内部生效，不会挡住整个桌面。
    /// </summary>
    public class CardModal : Grid
    {
        readonly Border _backdrop;
        readonly Border _panel;
        Panel _host;
        IInputElement _previousFocus;
        bool _open;

        public CardModal()
        {
            Focusable = true;
            FocusVisualStyle = null;
            SnapsToDevicePixels = true;
            ClipToBounds = true;

            _backdrop = new Border();
            _backdrop.MouseLeftButtonDown += OnBackdropMouseDown;

            _panel = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18),
                MaxWidth = 560,
                MinWidth = 220,
                BorderThickness = new Thickness(1)
            };

            Children.Add(_backdrop);
            Children.Add(_panel);
            System.Windows.Controls.Panel.SetZIndex(_backdrop, 0);
            System.Windows.Controls.Panel.SetZIndex(_panel, 1);

            ApplyStyleCore(new StyleSettings(), 12);
        }

        /// <summary>关闭时触发（只触发一次；由 <see cref="Close"/> 统一出口）。</summary>
        public event EventHandler Closed;

        /// <summary>是否已经加入宿主并处于打开状态。</summary>
        public bool IsOpen { get { return _open; } }

        /// <summary>点遮罩是否关闭；默认 true。点面板内部永远不关。</summary>
        public bool CloseOnBackdropClick { get; set; } = true;

        /// <summary>面板里的内容，由调用方放。</summary>
        public UIElement Body
        {
            get { return _panel.Child; }
            set { _panel.Child = value; }
        }

        /// <summary>子类可以拿到居中面板继续摆自己的内容。</summary>
        protected Border Panel { get { return _panel; } }

        /// <summary>把遮罩层加进宿主面板并置顶、抢键盘焦点；重复 Show 不会叠加。</summary>
        public void Show(Panel host)
        {
            if (_open || host == null) return;

            _host = host;
            System.Windows.Controls.Panel.SetZIndex(this, 10000);
            if (!host.Children.Contains(this)) host.Children.Add(this);

            _open = true;
            _previousFocus = Keyboard.FocusedElement;
            Focusable = true;
            Focus();
            Keyboard.Focus(this);
        }

        /// <summary>从宿主移除并触发 <see cref="Closed"/>；重复调用安全。</summary>
        public void Close()
        {
            if (!_open) return;

            _open = false;
            if (_host != null)
            {
                _host.Children.Remove(this);
                _host = null;
            }

            // 抢来的焦点还回去，键盘不会留在已经移除的遮罩上。
            if (_previousFocus != null && _previousFocus is UIElement)
            {
                Keyboard.Focus(_previousFocus);
            }
            _previousFocus = null;

            OnClosed();
            EventHandler handler = Closed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>子类在关闭时做收尾（默认什么都不做），<see cref="Close"/> 里已从宿主移除。</summary>
        protected virtual void OnClosed()
        {
        }

        /// <summary>套主题：遮罩颜色、面板底色 / 描边 / 圆角 / 内边距 / 字体。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            ApplyStyleCore(style, cornerRadius);
            OnStyleChanged(style ?? new StyleSettings(), cornerRadius);
        }

        /// <summary>子类追加自己的样式；保证在基础样式之后调用。</summary>
        protected virtual void OnStyleChanged(StyleSettings style, double cornerRadius)
        {
        }

        void ApplyStyleCore(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            ThemeTokens tokens = ThemeResolver.Resolve(s);
            Color background = CardPalette.TryColor(s.Background, Color.FromRgb(0x0D, 0x11, 0x17));
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            // 遮罩：卡片底色往黑里压，再半透明盖住内容；不改变宿主窗口本身的透明度。
            Color mask = Blend(background, Colors.Black, 0.55);
            _backdrop.Background = CardPalette.Brush(mask, 0.62);

            // 面板保持不透明：浮层上的字不能和底下的卡片内容叠在一起。
            _panel.Background = CardPalette.Brush(background, 1.0);
            _panel.BorderBrush = CardPalette.Brush(tokens.Border);
            _panel.CornerRadius = new CornerRadius(Math.Max(6, cornerRadius));
            _panel.Padding = new Thickness(Math.Max(14, s.Padding));
            _panel.MaxWidth = 560;

            TextElement.SetFontFamily(_panel, CardPalette.ResolveFont(s.FontFamily));
            TextElement.SetFontSize(_panel, Math.Max(11, s.FontSize));
            TextElement.SetForeground(_panel, CardPalette.Brush(foreground, 0.96));
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled) return;

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        void OnBackdropMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!CloseOnBackdropClick) return;
            e.Handled = true;
            Close();
        }

        internal static Color Blend(Color a, Color b, double amount)
        {
            double t = Math.Max(0.0, Math.Min(1.0, amount));
            return Color.FromRgb(
                (byte)Math.Round(a.R * (1 - t) + b.R * t),
                (byte)Math.Round(a.G * (1 - t) + b.G * t),
                (byte)Math.Round(a.B * (1 - t) + b.B * t));
        }

        internal static string Hex(Color color)
        {
            return "#" + color.A.ToString("X2") + color.R.ToString("X2")
                 + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }

    /// <summary>
    /// 卡片内对话框：标题 + 正文 + 一行按钮（Ok / OkCancel / YesNo），基于 <see cref="CardModal"/>。
    ///
    /// <code>
    /// var dialog = new CardDialog { Title = "删除？", Message = "不可恢复", Buttons = CardDialogButtons.YesNo };
    /// dialog.ApplyStyle(style, corner);
    /// bool ok = await dialog.ShowAsync(this);
    /// </code>
    ///
    /// Enter = 确认，Esc = 取消；重复调用 ShowAsync 返回同一个 Task，不会叠出第二层遮罩。
    /// </summary>
    public class CardDialog : CardModal
    {
        readonly TextBlock _title;
        readonly TextBlock _message;
        readonly StackPanel _buttonRow;

        CardButton _okButton;
        CardButton _cancelButton;
        TaskCompletionSource<bool> _pending;
        StyleSettings _style = new StyleSettings();
        double _corner = 12;
        string _okText;
        string _cancelText;
        CardDialogButtons _buttons = CardDialogButtons.Ok;

        public CardDialog()
        {
            _title = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _message = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18),
                MaxWidth = 460
            };
            _buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var root = new StackPanel();
            root.Children.Add(_title);
            root.Children.Add(_message);
            root.Children.Add(_buttonRow);
            Panel.Child = root;

            RebuildButtons();
            ApplyStyle(new StyleSettings(), _corner);
        }

        public string Title
        {
            get { return _title.Text; }
            set { _title.Text = value ?? ""; }
        }

        public string Message
        {
            get { return _message.Text; }
            set { _message.Text = value ?? ""; }
        }

        public CardDialogButtons Buttons
        {
            get { return _buttons; }
            set
            {
                if (_buttons == value) return;
                _buttons = value;
                RebuildButtons();
                RestyleButtons();
            }
        }

        /// <summary>确认按钮文字；YesNo 时默认「是」。</summary>
        public string OkText
        {
            get { return _okText ?? (_buttons == CardDialogButtons.YesNo ? "是" : "确定"); }
            set
            {
                _okText = value;
                if (_okButton != null) _okButton.Text = OkText;
            }
        }

        /// <summary>取消按钮文字；YesNo 时默认「否」。</summary>
        public string CancelText
        {
            get { return _cancelText ?? (_buttons == CardDialogButtons.YesNo ? "否" : "取消"); }
            set
            {
                _cancelText = value;
                if (_cancelButton != null) _cancelButton.Text = CancelText;
            }
        }

        /// <summary>最近一次关闭时的结果（点确认 / Enter = true；取消 / Esc / 点遮罩 = false）。</summary>
        public bool DialogResult { get; private set; }

        /// <summary>显示对话框并等结果；已经打开时直接返回同一个 Task（不叠加遮罩）。</summary>
        public Task<bool> ShowAsync(Panel host)
        {
            if (IsOpen && _pending != null) return _pending.Task;
            if (host == null) return Task.FromResult(false);

            DialogResult = false;
            _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Show(host);
            return _pending.Task;
        }

        /// <summary>带结果关闭；已经在关闭流程里也不会重复触发 <see cref="CardModal.Closed"/>。</summary>
        public void CloseWith(bool result)
        {
            DialogResult = result;
            Close();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.IsRepeat)
            {
                e.Handled = true;
                CloseWith(true);
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        protected override void OnClosed()
        {
            base.OnClosed();

            // Esc / 点遮罩 / 外部 Close() 走这里，也要把等待方唤醒（默认取消）。
            TaskCompletionSource<bool> pending = _pending;
            _pending = null;
            if (pending != null && !pending.Task.IsCompleted)
                pending.TrySetResult(DialogResult);
        }

        protected override void OnStyleChanged(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _corner = cornerRadius;

            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontFamily font = CardPalette.ResolveFont(_style.FontFamily);

            _title.FontFamily = font;
            _title.FontSize = Math.Max(13, _style.FontSize + 3);
            _title.FontWeight = FontWeights.SemiBold;
            _title.Foreground = CardPalette.Brush(foreground, 0.97);

            _message.FontFamily = font;
            _message.FontSize = Math.Max(11, _style.FontSize);
            _message.Foreground = CardPalette.Brush(foreground, 0.86);

            RestyleButtons();
        }

        void RebuildButtons()
        {
            _buttonRow.Children.Clear();

            _okButton = new CardButton(OkText, CardButtonTone.Primary);
            _okButton.Margin = new Thickness(0);
            _okButton.Click += OnOkClick;
            _buttonRow.Children.Add(_okButton);

            _cancelButton = null;
            if (_buttons != CardDialogButtons.Ok)
            {
                _cancelButton = new CardButton(CancelText);
                _cancelButton.Margin = new Thickness(10, 0, 0, 0);
                _cancelButton.Click += OnCancelClick;
                _buttonRow.Children.Add(_cancelButton);
            }
        }

        void RestyleButtons()
        {
            if (_okButton != null) _okButton.ApplyStyle(_style, _corner);
            if (_cancelButton != null) _cancelButton.ApplyStyle(_style, _corner);
        }

        void OnOkClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            CloseWith(true);
        }

        void OnCancelClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            CloseWith(false);
        }
    }

    /// <summary>
    /// 卡片内浮层（Popup）：独立 HWND 里放一个主题化 Border + 调用方内容。
    /// StaysOpen=false：点外面自动关；Close() 会停掉淡入动画并清掉透明度。
    ///
    /// 没有 Loaded 的目标也能安全调用 <see cref="ShowAt"/>：目标还没进可视树时退化成绝对坐标，
    /// 不会因为 Popup 定位去 PointToScreen 而抛异常。
    /// </summary>
    public class CardPopover : Popup
    {
        readonly Border _border;
        FrameworkElement _content;
        ThemeTokens _tokens = ThemeResolver.Resolve(null);

        public CardPopover()
        {
            AllowsTransparency = true;
            StaysOpen = false;
            PopupAnimation = PopupAnimation.None;

            _border = new Border { Opacity = 0 };
            Child = _border;
            ApplyStyleCore(new StyleSettings(), 10);
        }

        /// <summary>浮层内容；由主题化 Border 承载。</summary>
        public FrameworkElement Content
        {
            get { return _content; }
            set
            {
                _content = value;
                _border.Child = value;
            }
        }

        /// <summary>当前主题令牌（子类 / 调用方需要跟主题色时用）。</summary>
        protected ThemeTokens Tokens { get { return _tokens; } }

        /// <summary>
        /// 在目标元素旁边打开；目标未 Loaded / 不在可视树也能调用（退化成绝对坐标，不抛异常）。
        /// </summary>
        public void ShowAt(FrameworkElement target, PlacementMode placement = PlacementMode.Bottom)
        {
            if (target == null) return;

            PlacementTarget = target;
            Placement = placement;

            // 没进可视树的目标没有 PresentationSource：Popup 定位会去 PointToScreen 抛
            // InvalidOperationException。这里退化成绝对坐标，调用方不用先等 Loaded。
            if (PresentationSource.FromVisual(target) == null)
            {
                Placement = PlacementMode.Absolute;
                HorizontalOffset = 0;
                VerticalOffset = 0;
            }

            try
            {
                IsOpen = true;
            }
            catch (InvalidOperationException)
            {
                // 窗口正在销毁 / 没有可用窗口站：保持关闭，不把异常抛给调用方。
                IsOpen = false;
                return;
            }

            FadeIn();
        }

        /// <summary>关闭浮层（立即 Stop 动画，Popup 的 Closed 事件照常触发）。</summary>
        public void Close()
        {
            StopFade();
            if (IsOpen) IsOpen = false;
            _border.Opacity = 0;
        }

        /// <summary>套主题：浮层底色 / 描边 / 圆角 / 内边距 / 字体。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            ApplyStyleCore(style, cornerRadius);
            OnStyleChanged(style ?? new StyleSettings(), cornerRadius);
        }

        /// <summary>子类追加样式（在基础样式之后）。</summary>
        protected virtual void OnStyleChanged(StyleSettings style, double cornerRadius)
        {
        }

        protected override void OnClosed(EventArgs e)
        {
            // Popup 是独立 HWND：关闭时停掉动画、复位透明度，避免下次打开还挂着上一次的动画时钟。
            StopFade();
            _border.Opacity = 0;
            base.OnClosed(e);
        }

        void ApplyStyleCore(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            _tokens = ThemeResolver.Resolve(s);

            Color background = CardPalette.TryColor(s.Background, Color.FromRgb(0x0D, 0x11, 0x17));
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            _border.Background = CardPalette.Brush(background, 1.0);
            _border.BorderBrush = CardPalette.Brush(_tokens.Border);
            _border.BorderThickness = new Thickness(1);
            _border.CornerRadius = new CornerRadius(Math.Max(6, cornerRadius));
            _border.Padding = new Thickness(12, 8, 12, 8);
            _border.MaxWidth = 520;

            TextElement.SetFontFamily(_border, CardPalette.ResolveFont(s.FontFamily));
            TextElement.SetFontSize(_border, Math.Max(11, s.FontSize));
            TextElement.SetForeground(_border, CardPalette.Brush(foreground, 0.96));
        }

        void FadeIn()
        {
            _border.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _border.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        void StopFade()
        {
            _border.BeginAnimation(UIElement.OpacityProperty, null);
        }
    }

    /// <summary>
    /// 鼠标悬停提示：延迟显示、离开立即隐藏；目标 Unloaded 时自动 Detach。
    /// 定时器 / 事件在 <see cref="Detach"/> / <see cref="Dispose"/> 里清理。
    /// </summary>
    public sealed class CardTooltip : CardPopover, IDisposable
    {
        readonly TextBlock _text;
        readonly DispatcherTimer _delayTimer;
        FrameworkElement _target;
        bool _mouseInside;
        bool _disposed;

        public CardTooltip()
        {
            _text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            Content = _text;

            _delayTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _delayTimer.Tick += OnDelayTick;

            ApplyStyle(new StyleSettings(), 10);
        }

        /// <summary>提示文字。</summary>
        public string Text
        {
            get { return _text.Text; }
            set { _text.Text = value ?? ""; }
        }

        /// <summary>鼠标停留多久后显示，默认 400ms；0 = 立即显示。</summary>
        public int DelayMs { get; set; } = 400;

        /// <summary>挂到一个目标元素上（会先 Detach 旧目标）。</summary>
        public void Attach(FrameworkElement target)
        {
            if (target == null || ReferenceEquals(_target, target)) return;

            Detach();
            _target = target;
            _mouseInside = false;

            target.MouseEnter += OnTargetMouseEnter;
            target.MouseLeave += OnTargetMouseLeave;
            target.Unloaded += OnTargetUnloaded;
        }

        /// <summary>摘掉目标：停定时器、解事件、关浮层。</summary>
        public void Detach()
        {
            _delayTimer.Stop();

            if (_target != null)
            {
                _target.MouseEnter -= OnTargetMouseEnter;
                _target.MouseLeave -= OnTargetMouseLeave;
                _target.Unloaded -= OnTargetUnloaded;
            }

            _target = null;
            _mouseInside = false;
            Close();
        }

        /// <summary>释放：等价于 Detach（幂等）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
        }

        protected override void OnStyleChanged(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            Color foreground = CardPalette.TryColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            _text.FontFamily = CardPalette.ResolveFont(s.FontFamily);
            _text.FontSize = Math.Max(11, s.FontSize - 1);
            _text.Foreground = CardPalette.Brush(foreground, 0.92);
        }

        void OnTargetMouseEnter(object sender, MouseEventArgs e)
        {
            _mouseInside = true;
            if (DelayMs <= 0) ShowNow();
            else
            {
                _delayTimer.Interval = TimeSpan.FromMilliseconds(DelayMs);
                _delayTimer.Stop();
                _delayTimer.Start();
            }
        }

        void OnTargetMouseLeave(object sender, MouseEventArgs e)
        {
            _mouseInside = false;
            _delayTimer.Stop();
            Close();
        }

        void OnTargetUnloaded(object sender, RoutedEventArgs e)
        {
            Detach();
        }

        void OnDelayTick(object sender, EventArgs e)
        {
            _delayTimer.Stop();
            if (_mouseInside) ShowNow();
        }

        void ShowNow()
        {
            if (_target == null) return;
            ShowAt(_target, PlacementMode.Bottom);
        }
    }

    /// <summary>
    /// 卡片内右键菜单：自绘 MenuItem / Separator / 分组标题，透明底 + 悬停强调淡底，
    /// 危险项用危险色，快捷键右对齐；不出现系统白色菜单底板。
    ///
    /// 菜单是独立 HWND：用完（IsOpen=false 或点条目）会自然关闭；Close 时没有定时器残留。
    /// </summary>
    public sealed class CardContextMenu : ContextMenu
    {
        const string MenuTemplateFormat =
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type MenuItem}\">" +
            "<Grid SnapsToDevicePixels=\"True\">" +
            "<Border x:Name=\"Hover\" Background=\"%HOVER%\" CornerRadius=\"6\" Margin=\"4,1\" Visibility=\"Collapsed\"/>" +
            "<ContentPresenter Margin=\"12,6,12,6\" ContentSource=\"Header\" HorizontalAlignment=\"Stretch\"/>" +
            "</Grid>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsHighlighted\" Value=\"True\">" +
            "<Setter TargetName=\"Hover\" Property=\"Visibility\" Value=\"Visible\"/>" +
            "</Trigger>" +
            "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
            "<Setter Property=\"Opacity\" Value=\"0.55\"/>" +
            "</Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate>";

        const string HeaderTemplate =
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type MenuItem}\">" +
            "<ContentPresenter Margin=\"12,7,12,2\" HorizontalAlignment=\"Stretch\"/>" +
            "</ControlTemplate>";

        const string MenuPanelFormat =
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ContextMenu}\">" +
            "<Border Background=\"%BACKGROUND%\" BorderBrush=\"%BORDER%\" BorderThickness=\"1\" " +
            "CornerRadius=\"10\" Padding=\"4,6\">" +
            "<ItemsPresenter/>" +
            "</Border>" +
            "</ControlTemplate>";

        const string SeparatorFormat =
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type Separator}\">" +
            "<Border Height=\"1\" Margin=\"10,5\" Background=\"%LINE%\"/>" +
            "</ControlTemplate>";

        StyleSettings _style = new StyleSettings();
        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        double _corner = 10;
        ControlTemplate _normalTemplate;
        ControlTemplate _dangerTemplate;
        ControlTemplate _headerTemplate;
        ControlTemplate _separatorTemplate;

        public CardContextMenu()
        {
            SnapsToDevicePixels = true;
            HasDropShadow = false;
            ApplyStyle(_style, _corner);
        }

        /// <summary>加一条可点条目；shortcut 显示在右侧，danger = 危险色。</summary>
        public MenuItem Add(string text, Action click, string shortcut = null, bool danger = false)
        {
            var item = new CardMenuItem(text, click, shortcut, danger, false);
            Items.Add(item);
            StyleItem(item);
            return item;
        }

        /// <summary>加一条分隔线。</summary>
        public Separator AddSeparator()
        {
            var separator = new Separator();
            Items.Add(separator);
            StyleSeparator(separator);
            return separator;
        }

        /// <summary>加一条分组标题（不可点）。</summary>
        public MenuItem AddHeader(string text)
        {
            var item = new CardMenuItem(text ?? "", null, null, false, true);
            Items.Add(item);
            StyleItem(item);
            return item;
        }

        /// <summary>在目标元素旁边 / 指定坐标打开；目标未进可视树时直接不打开（不抛异常）。</summary>
        public void ShowAt(FrameworkElement target, Point? point = null)
        {
            if (target == null) return;

            PlacementTarget = target;
            if (point.HasValue)
            {
                Placement = PlacementMode.RelativePoint;
                PlacementRectangle = new Rect(point.Value, new Size(0, 0));
                HorizontalOffset = 0;
                VerticalOffset = 0;
            }
            else
            {
                Placement = PlacementMode.Bottom;
                HorizontalOffset = 0;
                VerticalOffset = 0;
            }

            if (PresentationSource.FromVisual(target) == null) return;

            try { IsOpen = true; }
            catch (InvalidOperationException) { IsOpen = false; }
        }

        /// <summary>套主题：菜单底板、条目悬停 / 危险色 / 快捷键 / 字体。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _corner = cornerRadius;
            _tokens = ThemeResolver.Resolve(_style);

            Color background = CardPalette.TryColor(_style.Background, Color.FromRgb(0x0D, 0x11, 0x17));
            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            FontFamily font = CardPalette.ResolveFont(_style.FontFamily);
            FontFamily monoFont = CardPalette.ResolveFont(_tokens.MonoFont);

            FontFamily = font;
            FontSize = Math.Max(11, _style.FontSize - 1);
            Foreground = CardPalette.Brush(foreground, 0.92);

            _normalTemplate = ParseTemplate(MenuTemplateFormat
                .Replace("%HOVER%", CardModal.Hex(ThemeResolver.WithAlpha(_tokens.Accent, 0.16))));
            _dangerTemplate = ParseTemplate(MenuTemplateFormat
                .Replace("%HOVER%", CardModal.Hex(ThemeResolver.WithAlpha(_tokens.Danger, 0.16))));
            _headerTemplate = ParseTemplate(HeaderTemplate);
            _separatorTemplate = ParseTemplate(SeparatorFormat
                .Replace("%LINE%", CardModal.Hex(_tokens.Border)));

            Template = ParseTemplate(MenuPanelFormat
                .Replace("%BACKGROUND%", CardModal.Hex(background))
                .Replace("%BORDER%", CardModal.Hex(_tokens.Border)));

            // 已经加进去的条目也要跟着换肤（先加 Add、后调 ApplyStyle 的用例）。
            foreach (object entry in Items)
            {
                var item = entry as CardMenuItem;
                if (item != null) { StyleItem(item); continue; }
                var separator = entry as Separator;
                if (separator != null) StyleSeparator(separator);
            }
        }

        void StyleItem(CardMenuItem item)
        {
            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            item.FontFamily = FontFamily;
            item.FontSize = FontSize;

            if (item.IsHeader)
            {
                item.Template = _headerTemplate;
                item.Foreground = CardPalette.Brush(_tokens.Muted);
                item.IsEnabled = false;
                item.Cursor = Cursors.Arrow;
                item.Label.FontFamily = FontFamily;
                item.Label.FontSize = Math.Max(10, _style.FontSize - 3);
                item.Label.FontWeight = FontWeights.SemiBold;
                item.Label.Foreground = CardPalette.Brush(_tokens.Muted);
                return;
            }

            item.Template = item.IsDanger ? _dangerTemplate : _normalTemplate;
            Color text = item.IsDanger ? _tokens.Danger : foreground;
            item.Foreground = CardPalette.Brush(text, item.IsDanger ? 1.0 : 0.92);
            item.Cursor = Cursors.Hand;

            item.Label.FontFamily = FontFamily;
            item.Label.FontSize = Math.Max(11, _style.FontSize - 1);
            item.Label.Foreground = CardPalette.Brush(text, item.IsDanger ? 1.0 : 0.92);

            if (item.Shortcut != null)
            {
                item.Shortcut.FontFamily = CardPalette.ResolveFont(_tokens.MonoFont);
                item.Shortcut.FontSize = Math.Max(10, _style.FontSize - 3);
                item.Shortcut.Foreground = CardPalette.Brush(_tokens.Muted);
            }
        }

        void StyleSeparator(Separator separator)
        {
            separator.Template = _separatorTemplate;
            separator.Background = Brushes.Transparent;
        }

        static ControlTemplate ParseTemplate(string xaml)
        {
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        /// <summary>菜单条目：保存点击动作、快捷键与危险标记，自绘 Header。</summary>
        sealed class CardMenuItem : MenuItem
        {
            readonly Action _click;

            public CardMenuItem(string text, Action click, string shortcut, bool danger, bool header)
            {
                _click = click;
                IsDanger = danger;
                IsHeader = header;

                var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 28 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Label = new TextBlock
                {
                    Text = text ?? "",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(Label, 0);
                grid.Children.Add(Label);

                if (!string.IsNullOrEmpty(shortcut))
                {
                    Shortcut = new TextBlock
                    {
                        Text = shortcut,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(24, 0, 0, 0)
                    };
                    Grid.SetColumn(Shortcut, 2);
                    grid.Children.Add(Shortcut);
                }

                Header = grid;
                Background = Brushes.Transparent;
                Padding = new Thickness(0);
                StaysOpenOnClick = false;
                HorizontalContentAlignment = HorizontalAlignment.Stretch;

                if (header) IsEnabled = false;
                else Click += OnClickInvoke;
            }

            public bool IsDanger { get; private set; }

            public bool IsHeader { get; private set; }

            public TextBlock Label { get; private set; }

            public TextBlock Shortcut { get; private set; }

            void OnClickInvoke(object sender, RoutedEventArgs e)
            {
                if (_click != null) _click();
            }
        }
    }

    /// <summary>
    /// 卡片内 Toast：从宿主底部居中浮出，自动消失（淡出后移除）或手动 <see cref="Dismiss"/>。
    /// 多个 toast 各自独立，按加入顺序从下往上排；定时器 / 动画在关闭时清理。
    /// </summary>
    public sealed class CardToast : Border
    {
        readonly TextBlock _text;
        readonly CardButton _actionButton;
        readonly Grid _root;

        Panel _host;
        DispatcherTimer _timer;
        StyleSettings _style = new StyleSettings();
        ThemeTokens _tokens = ThemeResolver.Resolve(null);
        double _corner = 10;
        bool _closed;
        bool _dismissing;

        CardToastTone _tone = CardToastTone.Info;
        string _actionText = "";

        public CardToast()
        {
            _text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            _actionButton = new CardButton("", CardButtonTone.Primary)
            {
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };
            _actionButton.Click += OnActionClick;

            _root = new Grid { VerticalAlignment = VerticalAlignment.Center };
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_text, 0);
            Grid.SetColumn(_actionButton, 1);
            _root.Children.Add(_text);
            _root.Children.Add(_actionButton);

            Child = _root;
            BorderThickness = new Thickness(1);
            SnapsToDevicePixels = true;

            ApplyStyle(new StyleSettings(), _corner);
        }

        /// <summary>消失后触发（自动消失 / 手动 Dismiss 都只触发一次）。</summary>
        public event EventHandler Closed;

        public string Text
        {
            get { return _text.Text; }
            set { _text.Text = value ?? ""; }
        }

        /// <summary>语气色：Info / Success / Warning / Danger。</summary>
        public CardToastTone Tone
        {
            get { return _tone; }
            set
            {
                _tone = value;
                RefreshTone();
            }
        }

        /// <summary>自动消失时间（毫秒），默认 3000；&lt;= 0 = 不自动消失。</summary>
        public int DurationMs { get; set; } = 3000;

        /// <summary>可选的行动按钮文字；空 = 不显示按钮。</summary>
        public string ActionText
        {
            get { return _actionText; }
            set
            {
                _actionText = value ?? "";
                _actionButton.Text = _actionText;
                _actionButton.Visibility = _actionText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>行动按钮点击回调。</summary>
        public Action Action { get; set; }

        /// <summary>在宿主底部居中显示一条 toast（自动加入宿主、自动消失）。</summary>
        public static CardToast Show(Panel host, string text, CardToastTone tone = CardToastTone.Info,
                                     int durationMs = 3000)
        {
            return ShowCore(host, text, tone, durationMs, null, null);
        }

        /// <summary>带行动按钮的重载。</summary>
        public static CardToast Show(Panel host, string text, CardToastTone tone, int durationMs,
                                     string actionText, Action action)
        {
            return ShowCore(host, text, tone, durationMs, actionText, action);
        }

        static CardToast ShowCore(Panel host, string text, CardToastTone tone, int durationMs,
                                  string actionText, Action action)
        {
            if (host == null) return null;

            var toast = new CardToast
            {
                Text = text,
                Tone = tone,
                DurationMs = durationMs
            };
            if (!string.IsNullOrEmpty(actionText))
            {
                toast.ActionText = actionText;
                toast.Action = action;
            }

            toast.ApplyStyle(new StyleSettings(), 10);
            toast.AttachTo(host);
            return toast;
        }

        /// <summary>立即关闭：停定时器 / 停动画 / 从宿主移除 / 触发 Closed；重复调用安全。</summary>
        public void Dismiss()
        {
            Cleanup();
        }

        /// <summary>套主题：底色 / 描边 / 圆角 / 内边距 / 字体 / 行动按钮。</summary>
        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _corner = cornerRadius;
            _tokens = ThemeResolver.Resolve(_style);

            Color foreground = CardPalette.TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            Color background = CardPalette.TryColor(_style.Background, Color.FromRgb(0x0D, 0x11, 0x17));

            Color tint = ToneColor(_style);
            Color fill = CardModal.Blend(background, tint, 0.18);

            Background = CardPalette.Brush(fill, 0.98);
            BorderBrush = CardPalette.Brush(tint, 0.55);
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(Math.Max(8, cornerRadius));
            Padding = new Thickness(14, 10, 14, 10);

            TextElement.SetFontFamily(this, CardPalette.ResolveFont(_style.FontFamily));
            TextElement.SetFontSize(this, Math.Max(11, _style.FontSize - 1));
            TextElement.SetForeground(this, CardPalette.Brush(foreground, 0.95));

            _text.FontFamily = CardPalette.ResolveFont(_style.FontFamily);
            _text.FontSize = Math.Max(11, _style.FontSize - 1);
            _text.Foreground = CardPalette.Brush(foreground, 0.95);

            _actionButton.ApplyStyle(_style, cornerRadius);
            RefreshTone();
        }

        void AttachTo(Panel host)
        {
            _host = host;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Bottom;
            Panel.SetZIndex(this, 20000);   // 高于 CardModal(10000)，toast 不能被模态遮罩压暗
            host.Children.Add(this);
            Restack(host);

            if (DurationMs > 0)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(DurationMs)
                };
                _timer.Tick += OnTimerTick;
                _timer.Start();
            }
        }

        void OnTimerTick(object sender, EventArgs e)
        {
            StopTimer();
            if (_closed || _dismissing) return;

            _dismissing = true;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += OnFadeCompleted;
            BeginAnimation(UIElement.OpacityProperty, fade);
        }

        void OnFadeCompleted(object sender, EventArgs e)
        {
            Cleanup();
        }

        void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        void Cleanup()
        {
            if (_closed) return;
            _closed = true;
            _dismissing = false;

            StopTimer();
            BeginAnimation(UIElement.OpacityProperty, null);
            Opacity = 0;

            Panel host = _host;
            _host = null;
            if (host != null && host.Children.Contains(this))
            {
                host.Children.Remove(this);
                Restack(host);
            }

            EventHandler handler = Closed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        void RefreshTone()
        {
            if (_tokens == null) return;

            Color background = CardPalette.TryColor(_style.Background, Color.FromRgb(0x0D, 0x11, 0x17));
            Color tint = ToneColor(_style);
            Background = CardPalette.Brush(CardModal.Blend(background, tint, 0.18), 0.98);
            BorderBrush = CardPalette.Brush(tint, 0.55);
        }

        Color ToneColor(StyleSettings style)
        {
            switch (_tone)
            {
                case CardToastTone.Success:
                    return Color.FromRgb(0x3F, 0xB9, 0x50);
                case CardToastTone.Warning:
                    return Color.FromRgb(0xD2, 0x99, 0x22);
                case CardToastTone.Danger:
                    return ThemeResolver.Resolve(style).Danger;
                default:
                    return ThemeResolver.Resolve(style).Accent;
            }
        }

        void OnActionClick(object sender, RoutedEventArgs e)
        {
            Action action = Action;
            if (action != null) action();
        }

        static void Restack(Panel host)
        {
            if (host == null) return;

            double bottom = 24;
            foreach (UIElement child in host.Children)
            {
                var toast = child as CardToast;
                if (toast == null) continue;

                toast.Margin = new Thickness(0, 0, 0, bottom);
                bottom += MeasureHeight(toast, host) + 8;
            }
        }

        static double MeasureHeight(CardToast toast, Panel host)
        {
            double width = host.ActualWidth > 0 ? host.ActualWidth : 320;
            toast.Measure(new Size(Math.Max(120, width - 32), double.PositiveInfinity));
            double height = toast.DesiredSize.Height;
            return height > 0 ? height : 44;
        }
    }
}
