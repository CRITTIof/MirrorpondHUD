using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// 卡片里的单行 / 多行输入：占位符、Enter 提交（多行用 Ctrl+Enter）、错误描边。
    /// 底子仍是 <see cref="CardTextBox"/>，只是把“提交 / 空提示 / 错误态”这层常用行为收进来。
    /// </summary>
    public sealed class CardInput : Grid
    {
        readonly CardTextBox _box;
        readonly TextBlock _placeholder;
        readonly bool _multiline;

        StyleSettings _style;
        ThemeTokens _tokens;
        Color _foreground;
        bool _error;

        public CardInput(bool multiline = false)
        {
            _multiline = multiline;

            _box = new CardTextBox
            {
                MinHeight = multiline ? 64 : 28,
                Padding = new Thickness(8, 5, 8, 5),
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MaxLines = multiline ? int.MaxValue : 1,
                VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center
            };
            _box.TextChanged += (s, e) =>
            {
                UpdatePlaceholder();
                Action<string> handler = TextChanged;
                if (handler != null) handler(_box.Text ?? "");
            };
            _box.GotKeyboardFocus += (s, e) => UpdatePlaceholder();
            _box.LostKeyboardFocus += (s, e) => UpdatePlaceholder();
            _box.PreviewKeyDown += OnKeyDown;

            _placeholder = new TextBlock
            {
                IsHitTestVisible = false,
                Margin = new Thickness(9, 6, 9, 0),
                VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Children.Add(_box);
            Children.Add(_placeholder);
            ApplyStyle(null, 8);
        }

        /// <summary>回车提交（多行时 Ctrl+Enter）。参数是原始文本，调用方自己 Trim。</summary>
        public event Action<string> Submitted;

        /// <summary>每次文本变化（搜索框 / 实时预览用）；参数是原始文本。</summary>
        public event Action<string> TextChanged;

        public string Text
        {
            get { return _box.Text ?? ""; }
            set
            {
                _box.Text = value ?? "";
                UpdatePlaceholder();
            }
        }

        public string Placeholder
        {
            get { return _placeholder.Text ?? ""; }
            set
            {
                _placeholder.Text = value ?? "";
                UpdatePlaceholder();
            }
        }

        public bool IsReadOnly
        {
            get { return _box.IsReadOnly; }
            set { _box.IsReadOnly = value; }
        }

        public void Submit()
        {
            Action<string> handler = Submitted;
            if (handler != null) handler(_box.Text ?? "");
        }

        public void Clear()
        {
            _box.Text = "";
        }

        public void FocusInput()
        {
            _box.Focus();
        }

        /// <summary>错误态只改描边颜色，不动文字（避免泄漏错误信息本身）。</summary>
        public void SetError(bool error)
        {
            _error = error;
            _box.BorderBrush = error
                ? CardPalette.Brush(_tokens.Danger)
                : CardPalette.Brush(_foreground, 0.24);
        }

        public void ApplyStyle(StyleSettings style, double cornerRadius)
        {
            _style = style ?? new StyleSettings();
            _tokens = ThemeResolver.Resolve(_style);
            _foreground = OverlaySurface.ParseColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            _box.ApplyStyle(_style, cornerRadius);
            _placeholder.FontFamily = CardPalette.ResolveFont(_style.FontFamily);
            _placeholder.FontSize = Math.Max(10, _style.FontSize - 1);
            _placeholder.Foreground = CardPalette.Brush(_foreground, 0.38);

            SetError(_error);
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            // 单行：回车提交；多行：回车换行，Ctrl+Enter 提交。
            bool submit = !_multiline || (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!submit) return;

            e.Handled = true;
            Submit();
        }

        void UpdatePlaceholder()
        {
            bool empty = string.IsNullOrEmpty(_box.Text);
            _placeholder.Visibility = empty && !_box.IsKeyboardFocusWithin
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
