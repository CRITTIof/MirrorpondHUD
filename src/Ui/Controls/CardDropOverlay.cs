using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 拖入卡片时的覆盖层：高亮底色 + 描边 + 一句提示（“松开以添加 N 项” / “这里不收这种内容”）。
    ///
    /// 拖放机制仍由宿主统一注册（<c>ICardDropTarget</c>）；卡片在实现 <c>ICardDropFeedback</c> 后，
    /// 收到“进入 / 离开”回调时调用 <see cref="Show(int,bool)"/> / <see cref="Hide"/> 即可。
    /// </summary>
    public sealed class CardDropOverlay : Border
    {
        readonly TextBlock _text;
        ThemeTokens _tokens;
        Color _foreground;
        Color _background = Color.FromRgb(0x0D, 0x11, 0x17);
        double _corner = 10;
        bool _accepted = true;

        public CardDropOverlay()
        {
            _text = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 10, 14, 10)
            };

            Child = _text;
            BorderThickness = new Thickness(1.5);
            CornerRadius = new CornerRadius(_corner);
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            Panel.SetZIndex(this, 100);

            _tokens = ThemeResolver.Resolve(null);
            _foreground = Color.FromRgb(0xE6, 0xED, 0xF3);
            UpdateColors();
        }

        public void Show(int count, bool accepted = true)
        {
            Show(accepted ? "松开以添加 " + Math.Max(0, count) + " 项" : "这里不收这种内容", accepted);
        }

        public void Show(string text, bool accepted = true)
        {
            _text.Text = text ?? "";
            _accepted = accepted;
            UpdateColors();
            Visibility = Visibility.Visible;
        }

        public void Hide()
        {
            Visibility = Visibility.Collapsed;
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            StyleSettings s = style ?? new StyleSettings();
            _tokens = ThemeResolver.Resolve(s);
            _foreground = OverlaySurface.ParseColor(s.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));
            _background = OverlaySurface.ParseColor(s.Background, Color.FromRgb(0x0D, 0x11, 0x17));
            _corner = cornerRadius;

            _text.FontFamily = CardPalette.ResolveFont(s.FontFamily);
            _text.FontSize = Math.Max(11, s.FontSize);
            CornerRadius = new CornerRadius(Math.Max(6, cornerRadius));
            UpdateColors();
        }

        void UpdateColors()
        {
            Color tint = _accepted ? _tokens.Accent : _tokens.Danger;

            // 覆盖层要盖住底下的空状态 / 列表文字，所以用“卡片底色混一点强调色”的不透明底，
            // 不能只铺一层半透明高亮（会透出底下的字，叠字很难看）。
            var brush = new SolidColorBrush(Blend(_background, tint, 0.22));
            brush.Freeze();
            Background = brush;
            BorderBrush = CardPalette.Brush(tint, 0.85);
            _text.Foreground = _accepted ? CardPalette.Brush(_foreground, 0.95) : CardPalette.Brush(_tokens.Danger);
        }

        static Color Blend(Color a, Color b, double amount)
        {
            double t = Math.Max(0.0, Math.Min(1.0, amount));
            return Color.FromRgb(
                (byte)Math.Round(a.R * (1 - t) + b.R * t),
                (byte)Math.Round(a.G * (1 - t) + b.G * t),
                (byte)Math.Round(a.B * (1 - t) + b.B * t));
        }
    }
}
