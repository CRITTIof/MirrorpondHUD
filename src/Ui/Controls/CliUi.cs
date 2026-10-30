using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 把「卡片前景色」当主题色的取色工具。
    /// 面板里的所有灰阶（分隔线、小面板底、滑块拇指）都由它按同一个前景色算出来，
    /// 所以换一套配色时面板内部不会留下写死的白 / 黑。
    /// </summary>
    public static class CardPalette
    {
        public static Color TryColor(string hex, Color fallback)
        {
            try { return OverlaySurface.ParseColor(hex); }
            catch { return fallback; }
        }

        public static Color WithAlpha(Color color, double alpha)
        {
            byte a = (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255.0);
            return Color.FromArgb(a, color.R, color.G, color.B);
        }

        public static SolidColorBrush Brush(Color color, double alpha)
        {
            var brush = new SolidColorBrush(WithAlpha(color, alpha));
            brush.Freeze();
            return brush;
        }

        public static FontFamily ResolveFont(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new FontFamily("Microsoft YaHei UI");
            try { return new FontFamily(name); }
            catch { return new FontFamily("Microsoft YaHei UI"); }
        }
    }

    /// <summary>
    /// CLI 面板要能被宿主叫去接键盘输入：命令行是热键唤起的，弹出来就得能直接打字，
    /// 不能像别的卡片那样非要用鼠标点一下输入框。
    /// </summary>
    public interface ICliFocusTarget
    {
        /// <summary>会话开始（热键唤起）：命令行在这里清掉上一轮的记录并复位状态，AI 对话保留上下文。</summary>
        void Begin();

        void FocusInput();

        void Cancel();
    }

    /// <summary>
    /// 卡片的滚动条：一根 7 px 的圆角滑块，颜色跟着卡片前景色，
    /// 和「AI 对话 / 命令行」面板的底、描边是同一套灰阶 —— 深色卡片上不会出现系统那条亮白的滚动条。
    ///
    /// 做成一个可复用的 Style：任何 ScrollViewer 只要把自己的 Resources 里塞进
    /// <c>typeof(ScrollBar)</c> → 这个 Style，下面的滑块就统一了（含设置页预览）。
    /// </summary>
    public static class CliScrollBars
    {
        const string TemplateFormat =
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<ControlTemplate x:Key=\"Thumb\" TargetType=\"Thumb\">" +
            "<Border x:Name=\"Bd\" CornerRadius=\"3\" Margin=\"2,2,2,2\" Background=\"%THUMB%\"/>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
            "<Setter TargetName=\"Bd\" Property=\"Background\" Value=\"%THUMBHOT%\"/></Trigger>" +
            "<Trigger Property=\"IsDragging\" Value=\"True\">" +
            "<Setter TargetName=\"Bd\" Property=\"Background\" Value=\"%THUMBHOT%\"/></Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate>" +
            "<Style x:Key=\"Bar\" TargetType=\"ScrollBar\">" +
            "<Setter Property=\"Width\" Value=\"7\"/>" +
            "<Setter Property=\"Height\" Value=\"Auto\"/>" +
            "<Setter Property=\"Background\" Value=\"Transparent\"/>" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"ScrollBar\">" +
            "<Grid Background=\"Transparent\" SnapsToDevicePixels=\"True\">" +
            "<Track x:Name=\"PART_Track\" IsDirectionReversed=\"True\">" +
            "<Track.Thumb><Thumb MinHeight=\"20\" Template=\"{StaticResource Thumb}\"/></Track.Thumb>" +
            "</Track>" +
            "</Grid>" +
            "</ControlTemplate>" +
            "</Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>";

        static readonly Dictionary<string, Style> Cache = new Dictionary<string, Style>(StringComparer.Ordinal);

        /// <summary>按前景色生成（并缓存）滚动条样式：同一套配色在多个面板里复用同一个 Style。</summary>
        public static Style For(Color foreground)
        {
            string key = foreground.ToString();
            Style style;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out style)) return style;
            }

            // 注意不能用 string.Format：模板里的 {StaticResource ...} 会被当成占位符。
            string xaml = TemplateFormat
                .Replace("%THUMB%", CardPalette.WithAlpha(foreground, 0.18).ToString())
                .Replace("%THUMBHOT%", CardPalette.WithAlpha(foreground, 0.40).ToString());

            var dictionary = (ResourceDictionary)XamlReader.Parse(xaml);
            style = (Style)dictionary["Bar"];

            lock (Cache)
            {
                if (Cache.Count > 16) Cache.Clear();
                Cache[key] = style;
            }
            return style;
        }

        /// <summary>把统一滑块挂到一个滚动区域上（只影响这个子树）。</summary>
        public static void Apply(FrameworkElement host, Color foreground)
        {
            if (host == null) return;
            host.Resources[typeof(ScrollBar)] = For(foreground);
        }
    }

    /// <summary>
    /// 内部小面板：一轮问答 / 一条命令一块，自带内边距、自己的高度上限和自己的滑块。
    /// 记录多了不会把整块面板顶高，也不会让所有记录去抢同一根滚动条。
    /// </summary>
    public sealed class CliEntryPanel : Border
    {
        readonly ScrollViewer _scroller;
        readonly StackPanel _body;
        readonly Color _foreground;
        readonly double _fontSize;

        public CliEntryPanel(StyleSettings style, CliSettings cli, Color foreground, double corner)
        {
            _foreground = foreground;
            _fontSize = Math.Max(10, (style == null ? 15 : style.FontSize) - 1);

            _body = new StackPanel { Orientation = Orientation.Vertical };

            _scroller = new ScrollViewer
            {
                Content = _body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Max(40, cli.EntryMaxHeight),
                Padding = new Thickness(0),
                Focusable = false
            };
            CliScrollBars.Apply(_scroller, foreground);

            Child = _scroller;
            Background = CardPalette.Brush(foreground, 0.045);
            BorderBrush = CardPalette.Brush(foreground, 0.10);
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(Math.Max(3, corner - 3));
            Padding = new Thickness(8, 6, 4, 6);
            Margin = new Thickness(0, 0, 0, 6);
        }

        /// <summary>往里加一段文字，返回这个 TextBlock（流式回答要就地改它）。</summary>
        public TextBlock AddText(string text, double alpha, bool bold, double sizeDelta, double bottomMargin)
        {
            var block = new TextBlock
            {
                Text = text ?? "",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = CliPanelView.MonoFont,
                FontSize = _fontSize + sizeDelta,
                LineHeight = Math.Max(_fontSize + 2, _fontSize * 1.5),
                Foreground = CardPalette.Brush(_foreground, alpha),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 4, bottomMargin)
            };

            _body.Children.Add(block);
            return block;
        }

        public void ScrollToEnd()
        {
            _scroller.UpdateLayout();
            _scroller.ScrollToEnd();
        }

        public double FontSize { get { return _fontSize; } }
        public StackPanel Body { get { return _body; } }
    }

    /// <summary>
    /// 「CLI 交互面板」的骨架：AI 对话与命令行共用这一套。
    ///
    /// 版式与终端一致：标题行（名字 + 状态）→ 分隔线 → 记录区（每条记录一个内部小面板）→ 提示符输入行 → 状态行。
    /// 两个卡片的差别只在「提交之后干什么」：AI 去请求接口，命令行去跑 shell。
    /// 尺寸全部来自 <see cref="CliSettings"/>：整块面板的宽度 / 高度上限、内部小面板的高度上限。
    /// </summary>
    public abstract class CliPanelView : Grid, ICliFocusTarget
    {
        /// <summary>等宽字体：终端样式；中文靠后面的字体接住（WPF 会逐个字形回退）。</summary>
        public static readonly FontFamily MonoFont =
            new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

        protected readonly Settings HostSettings;
        protected readonly StyleSettings StyleSet;
        protected readonly CliSettings Cli;
        protected readonly Color Foreground;
        protected readonly double Corner;
        protected readonly double Pad;
        protected readonly double FontSize;
        protected readonly CardTextBox Input;

        readonly StackPanel _records;
        readonly ScrollViewer _recordView;
        readonly TextBlock _title;
        readonly TextBlock _badge;
        readonly TextBlock _prompt;
        readonly TextBlock _status;
        readonly List<CliEntryPanel> _entries = new List<CliEntryPanel>();

        protected CliPanelView(Settings settings, CliSettings cli)
        {
            HostSettings = settings;
            Cli = cli ?? new CliSettings();
            Cli.Normalize();
            StyleSet = settings == null ? new StyleSettings() : settings.Style;
            Corner = settings == null ? 10 : settings.Overlay.CornerRadius;
            Foreground = CardPalette.TryColor(StyleSet.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            FontSize = Math.Max(10, StyleSet.FontSize - 1);
            Pad = Math.Max(10, StyleSet.Padding * 0.85);

            Width = Math.Max(240, Cli.MaxWidth);
            Background = Brushes.Transparent;
            CliScrollBars.Apply(this, Foreground);

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 标题
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 分隔线
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 记录
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 输入
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 状态

            // ---------- 标题行 ----------
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _title = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = CardPalette.Brush(Foreground, 0.95),
                FontSize = FontSize + 1,
                FontFamily = MonoFont,
                FontWeight = FontWeights.SemiBold
            };

            _badge = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = CardPalette.Brush(Foreground, 0.45),
                FontSize = Math.Max(9, FontSize - 2),
                FontFamily = MonoFont
            };

            Grid.SetColumn(_badge, 1);
            header.Children.Add(_title);
            header.Children.Add(_badge);

            var headerHost = new Border
            {
                Child = header,
                Margin = new Thickness(Pad, Math.Max(6, Pad * 0.55), Pad, Math.Max(3, Pad * 0.3))
            };
            Children.Add(headerHost);
            SetRow(headerHost, 0);

            // 细横线：终端标题栏下面那条分隔线。
            var rule = new Border
            {
                Height = 1,
                Background = CardPalette.Brush(Foreground, 0.14),
                Margin = new Thickness(Pad, 0, Pad, Math.Max(4, Pad * 0.35))
            };
            Children.Add(rule);
            SetRow(rule, 1);

            // ---------- 记录区 ----------
            _records = new StackPanel { Orientation = Orientation.Vertical, Background = Brushes.Transparent };

            // 整块面板的高度上限去掉标题 / 输入 / 状态几行，剩下的给记录区；超出走外层滑块。
            double chrome = Pad * 2.4 + FontSize * 5.2;
            _recordView = new ScrollViewer
            {
                Content = _records,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Max(60, Cli.MaxHeight - chrome),
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Focusable = false
            };
            CliScrollBars.Apply(_recordView, Foreground);

            var recordHost = new Border
            {
                Child = _recordView,
                Margin = new Thickness(Pad, 0, Pad, Math.Max(4, Pad * 0.4))
            };
            Children.Add(recordHost);
            SetRow(recordHost, 2);

            // ---------- 输入行 ----------
            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _prompt = new TextBlock
            {
                Text = ">",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = CardPalette.Brush(Foreground, 0.8),
                FontSize = FontSize + 1,
                FontFamily = MonoFont,
                FontWeight = FontWeights.SemiBold
            };

            Input = new CardTextBox
            {
                AcceptsReturn = false,
                MaxLines = 1,
                MinHeight = Math.Max(24, FontSize * 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = MonoFont,
                FontSize = FontSize + 1,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 4, 2, 4),
                Foreground = CardPalette.Brush(Foreground, 0.95)
            };
            Input.ApplyStyle(StyleSet, Corner);
            Input.FontFamily = MonoFont;
            Input.FontSize = FontSize + 1;
            Input.Background = Brushes.Transparent;
            Input.BorderThickness = new Thickness(0);
            Input.Padding = new Thickness(2, 4, 2, 4);
            Input.Foreground = CardPalette.Brush(Foreground, 0.95);
            Input.PreviewKeyDown += OnInputPreviewKeyDown;

            // 点卡片任何地方都当点终端：焦点回到提示符后面，不用对准输入框。
            PreviewMouseDown += (s, e) => { if (!Input.IsKeyboardFocusWithin) Input.Focus(); };

            Grid.SetColumn(_prompt, 0);
            Grid.SetColumn(Input, 1);
            inputGrid.Children.Add(_prompt);
            inputGrid.Children.Add(Input);

            var inputHost = new Border
            {
                Child = inputGrid,
                Margin = new Thickness(Pad, 0, Pad, Math.Max(3, Pad * 0.3))
            };
            Children.Add(inputHost);
            SetRow(inputHost, 3);

            // ---------- 状态行 ----------
            _status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = Math.Max(9, FontSize - 2),
                FontFamily = MonoFont,
                Foreground = CardPalette.Brush(Foreground, 0.5),
                Margin = new Thickness(Pad, 1, Pad, Math.Max(5, Pad * 0.55))
            };
            Children.Add(_status);
            SetRow(_status, 4);
        }

        // ==================== 给子类用的零件 ====================

        protected void SetTitle(string text)
        {
            _title.Text = text ?? "";
        }

        protected void SetBadge(string text, string tooltip)
        {
            _badge.Text = text ?? "";
            _badge.ToolTip = tooltip;
        }

        protected void SetPrompt(string text)
        {
            _prompt.Text = string.IsNullOrEmpty(text) ? ">" : text;
        }

        /// <summary>
        /// 关掉输入法：命令行的输入是要喂给 shell 的，中文输入法开着的话回车只会确认候选词、
        /// 命令根本发不出去（旧实现也是这么处理的）。AI 对话面板保留输入法，那边要打中文。
        /// </summary>
        protected void DisableIme()
        {
            InputMethod.SetIsInputMethodEnabled(Input, false);
        }

        protected void SetInputReadOnly(bool readOnly)
        {
            Input.IsReadOnly = readOnly;
            _prompt.Foreground = CardPalette.Brush(Foreground, readOnly ? 0.35 : 0.8);
        }

        protected void SetStatus(string text, bool error)
        {
            _status.Text = text ?? "";
            _status.Foreground = CardPalette.Brush(
                error ? Color.FromRgb(0xE5, 0x7B, 0x6E) : Foreground, error ? 1.0 : 0.5);
        }

        /// <summary>开一个新的内部小面板（一轮问答 / 一条命令）。</summary>
        protected CliEntryPanel AddEntry()
        {
            var entry = new CliEntryPanel(StyleSet, Cli, Foreground, Corner);
            _entries.Add(entry);
            _records.Children.Add(entry);
            return entry;
        }

        /// <summary>只保留最近若干条记录：多了把最老的整块面板扔掉。</summary>
        protected void TrimEntries(int max)
        {
            int limit = Math.Max(1, max);
            while (_entries.Count > limit)
            {
                _records.Children.Remove(_entries[0]);
                _entries.RemoveAt(0);
            }
        }

        protected void ScrollRecordsToEnd()
        {
            _recordView.UpdateLayout();
            _recordView.ScrollToEnd();
            if (_entries.Count > 0) _entries[_entries.Count - 1].ScrollToEnd();
        }

        protected void ClearEntries()
        {
            _entries.Clear();
            _records.Children.Clear();
        }

        protected int EntryCount { get { return _entries.Count; } }

        /// <summary>
        /// 离屏截图（--screenshot-cards）用的样张记录：真实卡片上要等用户提问 / 跑命令才有记录，
        /// 而这两块面板的观感（内部小面板、自己的滑块）只有在有记录时才看得出来。
        /// </summary>
        public void AddSampleRecord(string text, bool dim)
        {
            CliEntryPanel entry = AddEntry();
            entry.AddText(text ?? "", dim ? 0.72 : 0.95, false, 0, 0);
            TrimEntries(Cli.MaxEntries);
            ScrollRecordsToEnd();
        }

        // ==================== 输入 ====================

        void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Cancel();
                return;
            }

            if (e.Key != Key.Enter) return;

            e.Handled = true;
            SubmitInput();
        }

        /// <summary>回车：子类接管（AI 发请求 / 命令行跑 shell）。</summary>
        protected abstract void SubmitInput();

        public virtual void Begin() { }

        public void FocusInput()
        {
            try
            {
                Input.Focus();
                Keyboard.Focus(Input);
                Input.CaretIndex = (Input.Text ?? "").Length;
            }
            catch { }
        }

        public virtual void Cancel() { }
    }
}
