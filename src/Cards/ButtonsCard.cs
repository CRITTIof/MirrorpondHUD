using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 实验性卡片：验证卡片里能放按钮与输入框（<see cref="CardButton"/> / <see cref="CardTextBox"/>）。
    ///
    /// 它没有实际用途，只是把「模块化卡片 = 随便什么 WPF 内容」这条契约用最小的例子跑一遍：
    /// - 三种语气（普通 / 主操作 / 危险）的按钮，颜色、圆角、字体跟着卡片走；
    /// - 按钮点击真的收到事件（计数会变），鼠标悬停 / 按下有反馈；
    /// - 输入框能打字、回车或点按钮把内容回显出来；
    /// - 禁用态按钮不会亮。
    ///
    /// 卡片声明 <see cref="NeedsMouseInput"/>，所以宿主不会给这张卡开点击穿透——
    /// 代价是它会挡住身下的桌面点击，这也正是「交互卡片」这个方向的边界。
    /// </summary>
    public sealed class ButtonsCard : RichCardModuleBase
    {
        public override string Kind { get { return "buttons"; } }
        public override string DisplayName { get { return "按钮演示（实验）"; } }
        public override string Icon { get { return Icons.Add; } }
        public override bool NeedsMouseInput { get { return true; } }
        public override int Order { get { return 90; } }

        public override FrameworkElement CreateContent(CardRenderContext context)
        {
            Settings settings = context == null ? null : context.Settings;
            return new ButtonsView(settings);
        }
    }

    /// <summary>按钮演示卡片的界面。</summary>
    internal sealed class ButtonsView : StackPanel
    {
        readonly StyleSettings _style;
        readonly double _corner;
        readonly Color _foreground;
        readonly double _pad;

        readonly TextBlock _counter;
        readonly TextBlock _echo;
        readonly CardTextBox _input;

        int _clicks;

        public ButtonsView(Settings settings)
        {
            _style = settings == null ? new StyleSettings() : settings.Style;
            _corner = settings == null ? 10 : settings.Overlay.CornerRadius;
            _foreground = OverlaySurface.ParseColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            _pad = Math.Max(10, _style.Padding * 0.85);

            Width = 340;
            Background = Brushes.Transparent;

            Children.Add(Muted("按钮控件（三种语气 + 禁用态）", Math.Max(10, _style.FontSize - 3), 0.55));

            var tones = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            tones.Children.Add(MakeButton("普通", CardButtonTone.Normal, null));
            tones.Children.Add(MakeButton("主操作", CardButtonTone.Primary, null));
            tones.Children.Add(MakeButton("危险", CardButtonTone.Danger, null));

            CardButton disabled = MakeButton("禁用", CardButtonTone.Normal, null);
            disabled.IsEnabled = false;
            tones.Children.Add(disabled);
            Children.Add(tones);

            _counter = Muted("点「主操作」试试：点了 0 次", _style.FontSize, 0.8);
            _counter.Margin = new Thickness(0, 8, 0, 0);
            Children.Add(_counter);

            Children.Add(Muted("输入框（回车或点「回显」）", Math.Max(10, _style.FontSize - 3), 0.55, 10));

            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _input = new CardTextBox { MinHeight = Math.Max(24, _style.FontSize * 2) };
            _input.ApplyStyle(_style, _corner);
            _input.KeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                e.Handled = true;
                Echo();
            };

            CardButton echo = MakeButton("回显", CardButtonTone.Primary, (s, e) => Echo());
            echo.MinWidth = 0;
            echo.Padding = new Thickness(10, 4, 10, 4);
            echo.Margin = new Thickness(6, 0, 0, 0);
            echo.VerticalAlignment = VerticalAlignment.Stretch;

            Grid.SetColumn(_input, 0);
            Grid.SetColumn(echo, 1);
            row.Children.Add(_input);
            row.Children.Add(echo);
            Children.Add(row);

            _echo = Muted("（还没输入）", _style.FontSize, 0.8);
            _echo.Margin = new Thickness(0, 6, 0, 0);
            _echo.TextWrapping = TextWrapping.Wrap;
            Children.Add(_echo);

            Children.Add(Muted("这张卡不点击穿透，所以按钮能点到；代价是会挡住身下的桌面。",
                Math.Max(9, _style.FontSize - 4), 0.45, 10));
        }

        CardButton MakeButton(string text, CardButtonTone tone, RoutedEventHandler click)
        {
            var button = new CardButton(text, tone);
            button.ApplyStyle(_style, _corner);
            button.Margin = new Thickness(0, 0, 6, 0);

            if (click != null)
            {
                button.Click += click;
            }
            else if (tone == CardButtonTone.Primary)
            {
                button.Click += (s, e) =>
                {
                    _clicks++;
                    _counter.Text = "点「主操作」试试：点了 " + _clicks + " 次";
                };
            }
            else
            {
                button.Click += (s, e) =>
                {
                    _counter.Text = "你点了「" + button.Text + "」，主操作计数还是 " + _clicks + " 次";
                };
            }

            return button;
        }

        void Echo()
        {
            string text = (_input.Text ?? "").Trim();
            _echo.Text = text.Length == 0 ? "（输入框是空的）" : "回显：" + text;
        }

        /// <summary>一行小字：字号 / 明度 / 上边距都按卡片设置来。</summary>
        TextBlock Muted(string text, double fontSize, double alpha, double topMargin = 0)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = Math.Max(9, fontSize),
                FontFamily = ResolveFont(_style.FontFamily),
                Foreground = new SolidColorBrush(Alpha(_foreground, alpha)),
                Margin = new Thickness(0, topMargin, 0, 0)
            };
        }

        static Color Alpha(Color color, double alpha)
        {
            byte a = (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255.0);
            return Color.FromArgb(a, color.R, color.G, color.B);
        }

        static FontFamily ResolveFont(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new FontFamily("Microsoft YaHei UI");
            try { return new FontFamily(name); }
            catch { return new FontFamily("Microsoft YaHei UI"); }
        }
    }
}
