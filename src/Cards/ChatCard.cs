using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 「AI 对话」卡片：第一行是模型名，第二行输入，下面是问答记录。
    ///
    /// 走 OpenAI 兼容的 <c>POST {BaseUrl}/chat/completions</c>，所以 OpenAI / DeepSeek / 通义 /
    /// 本地 vLLM / LM Studio / Ollama 的兼容层都能接（每张卡片自己一套 BaseUrl + 模型 + Key，
    /// 想同时挂两个模型就放两张卡）。
    ///
    /// 两种模式（在卡片上点「单次 / 轮询」切换，也会写回卡片设置）：
    /// - 单次：每次提问都不带历史，只问这一句；
    /// - 轮询：保留上下文，把之前的问答一起发回去，形成连续对话。
    ///
    /// 卡片声明 <see cref="NeedsMouseInput"/>：输入框和按钮要收鼠标，所以这张卡不点击穿透，
    /// 会挡住它身下的桌面点击（其余卡片不受影响）。
    /// </summary>
    public sealed class ChatCard : RichCardModuleBase
    {
        public override string Kind { get { return "chat"; } }
        public override string DisplayName { get { return "AI 对话"; } }
        public override string Icon { get { return Icons.Agent; } }
        public override bool NeedsMouseInput { get { return true; } }
        public override int Order { get { return 40; } }

        public override FrameworkElement CreateContent(CardRenderContext context)
        {
            ChatSettings config = context != null && context.Config != null && context.Config.Chat != null
                ? context.Config.Chat
                : new ChatSettings();
            config.Normalize();

            return new ChatView(context == null ? null : context.Settings, config);
        }

        public override void OnVisibilityChanged(FrameworkElement content, bool visible)
        {
            var view = content as ChatView;
            if (view != null && !visible) view.Cancel();
        }
    }

    /// <summary>
    /// AI 对话卡片的界面与请求逻辑。一张卡片一个实例，互不影响（各带自己的上下文）。
    /// </summary>
    internal sealed class ChatView : Grid
    {
        /// <summary>共享 HttpClient：连接复用，避免每问一次握手一次。</summary>
        static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        readonly StyleSettings _style;
        readonly ChatSettings _config;
        readonly double _corner;
        readonly Color _foreground;
        readonly double _pad;

        readonly TextBlock _model;
        readonly CardButton _singleButton;
        readonly CardButton _contextButton;
        readonly CardButton _clearButton;
        readonly CardTextBox _input;
        readonly CardButton _sendButton;
        readonly TextBlock _answer;
        readonly TextBlock _status;
        readonly ScrollViewer _scroller;
        readonly Border _answerHost;

        readonly List<Message> _messages = new List<Message>();

        CancellationTokenSource _pending;
        bool _busy;

        sealed class Message
        {
            public string Role;
            public string Text;
        }

        public ChatView(Settings settings, ChatSettings config)
        {
            _config = config;
            _style = settings == null ? new StyleSettings() : settings.Style;
            _corner = settings == null ? 10 : settings.Overlay.CornerRadius;
            _foreground = TryColor(_style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

            _pad = Math.Max(10, _style.Padding * 0.85);

            Width = Math.Max(240, _config.Width);
            Background = Brushes.Transparent;

            RowDefinition header = new RowDefinition { Height = GridLength.Auto };
            RowDefinition input = new RowDefinition { Height = GridLength.Auto };
            RowDefinition answer = new RowDefinition { Height = GridLength.Auto };
            RowDefinition status = new RowDefinition { Height = GridLength.Auto };
            RowDefinitions.Add(header);
            RowDefinitions.Add(input);
            RowDefinitions.Add(answer);
            RowDefinitions.Add(status);

            // ---------- 第一行：模型名 + 模式 ----------
            var headerPanel = new DockPanel { LastChildFill = true };

            var modePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };

            _singleButton = Toggle("单次");
            _contextButton = Toggle("轮询");
            _clearButton = Toggle("清空");
            _singleButton.Click += (s, e) => SetMode("Single");
            _contextButton.Click += (s, e) => SetMode("Context");
            _clearButton.Click += (s, e) => ClearConversation();

            modePanel.Children.Add(_singleButton);
            modePanel.Children.Add(_contextButton);
            modePanel.Children.Add(_clearButton);
            DockPanel.SetDock(modePanel, Dock.Right);

            _model = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Alpha(_foreground, 0.62)),
                FontSize = Math.Max(11, _style.FontSize - 3),
                FontFamily = ResolveFont(_style.FontFamily)
            };

            headerPanel.Children.Add(modePanel);
            headerPanel.Children.Add(_model);

            var headerHost = new Border
            {
                Child = headerPanel,
                Margin = new Thickness(_pad, Math.Max(6, _pad * 0.6), _pad, Math.Max(4, _pad * 0.4))
            };
            Children.Add(headerHost);
            SetRow(headerHost, 0);

            // ---------- 第二行：输入 + 发送 ----------
            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _input = new CardTextBox
            {
                AcceptsReturn = false,
                MaxLines = 1,
                MinHeight = Math.Max(24, _style.FontSize * 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _input.ApplyStyle(_style, _corner);
            _input.KeyDown += OnInputKeyDown;

            _sendButton = new CardButton("发送", CardButtonTone.Primary);
            _sendButton.ApplyStyle(_style, _corner);
            _sendButton.MinWidth = 0;
            _sendButton.Padding = new Thickness(10, 4, 10, 4);
            _sendButton.Margin = new Thickness(6, 0, 0, 0);
            _sendButton.VerticalAlignment = VerticalAlignment.Stretch;
            _sendButton.Click += (s, e) => Send();

            Grid.SetColumn(_input, 0);
            Grid.SetColumn(_sendButton, 1);
            inputGrid.Children.Add(_input);
            inputGrid.Children.Add(_sendButton);

            var inputHost = new Border
            {
                Child = inputGrid,
                Margin = new Thickness(_pad, 0, _pad, Math.Max(4, _pad * 0.35))
            };
            Children.Add(inputHost);
            SetRow(inputHost, 1);

            // ---------- 问答记录 ----------
            _answer = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Alpha(_foreground, 0.92)),
                FontSize = Math.Max(10, _style.FontSize),
                FontFamily = ResolveFont(_style.FontFamily),
                LineHeight = _style.FontSize * 1.45
            };

            _scroller = new ScrollViewer
            {
                Content = _answer,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Max(40, _config.AnswerLines * _style.FontSize * 1.5),
                Padding = new Thickness(0, 2, 6, 2)
            };

            var answerHost = new Border
            {
                Child = _scroller,
                Margin = new Thickness(_pad, 0, _pad, 0),
                Visibility = Visibility.Collapsed
            };
            Children.Add(answerHost);
            SetRow(answerHost, 2);
            _answerHost = answerHost;

            // ---------- 状态行 ----------
            _status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = Math.Max(10, _style.FontSize - 3),
                FontFamily = ResolveFont(_style.FontFamily),
                Foreground = new SolidColorBrush(Alpha(_foreground, 0.55)),
                Margin = new Thickness(_pad, 2, _pad, Math.Max(6, _pad * 0.6))
            };
            Children.Add(_status);
            SetRow(_status, 3);

            RefreshHeader();
            UpdateEnabled();
            SetStatus(Hint, false);
        }

        // ==================== 状态 ====================

        bool Configured
        {
            get
            {
                return _config.BaseUrl.Length > 0 && _config.Model.Length > 0
                    && _config.BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            }
        }

        string Hint
        {
            get
            {
                return Configured
                    ? "回车发送 · 每张卡片各带一套上下文"
                    : "还没配置：设置 → 内容 →「AI 接口」填地址、模型、Key";
            }
        }

        CardButton Toggle(string text)
        {
            var button = new CardButton(text, CardButtonTone.Normal);
            button.ApplyStyle(_style, _corner);
            button.MinWidth = 0;
            button.Padding = new Thickness(8, 3, 8, 3);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.FontSize = Math.Max(10, _style.FontSize - 3);
            return button;
        }

        void RefreshHeader()
        {
            string model = _config.Model.Length > 0 ? _config.Model : "（未配置模型）";
            _model.Text = model + (_config.Mode == "Context" ? " · 轮询" : " · 单次");
            _singleButton.SetActive(_config.Mode != "Context");
            _contextButton.SetActive(_config.Mode == "Context");
        }

        void SetMode(string mode)
        {
            string normalized = ChatSettings.NormalizeMode(mode);
            if (_config.Mode == normalized) return;

            _config.Mode = normalized;
            _messages.Clear();          // 换模式就当开新对话，避免把上一轮的历史混进来
            RefreshHeader();
            RenderTranscript();
            SetStatus(Hint, false);
        }

        void ClearConversation()
        {
            _messages.Clear();
            RenderTranscript();
            SetStatus("已清空上下文", false);
        }

        void UpdateEnabled()
        {
            _sendButton.IsEnabled = !_busy;
            _input.IsEnabled = !_busy;
            _singleButton.IsEnabled = !_busy;
            _contextButton.IsEnabled = !_busy;
            _clearButton.IsEnabled = !_busy;
            _sendButton.Text = _busy ? "…" : "发送";
        }

        void SetStatus(string text, bool error)
        {
            _status.Text = text ?? "";
            _status.Foreground = new SolidColorBrush(
                error ? Color.FromRgb(0xE5, 0x7B, 0x6E) : Alpha(_foreground, 0.55));
        }

        // ==================== 对话 ====================

        void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Send();
        }

        async void Send()
        {
            if (_busy) return;

            string text = (_input.Text ?? "").Trim();
            if (text.Length == 0) return;

            if (!Configured)
            {
                SetStatus("接口地址或模型名为空，见「设置 → 内容 → AI 接口」", true);
                return;
            }

            bool context = _config.Mode == "Context";
            if (!context) _messages.Clear();
            _messages.Add(new Message { Role = "user", Text = text });
            TrimHistory();

            _input.Text = "";
            _pending = new CancellationTokenSource(TimeSpan.FromSeconds(_config.TimeoutSeconds));

            _busy = true;
            UpdateEnabled();
            RenderTranscript();
            SetStatus("思考中…", false);

            try
            {
                string reply = await RequestAsync(BuildMessages(), _pending.Token);
                _messages.Add(new Message { Role = "assistant", Text = reply });
                TrimHistory();
                SetStatus("", false);
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消（或超过 " + _config.TimeoutSeconds + " 秒）", true);
            }
            catch (Exception ex)
            {
                SetStatus(Explain(ex), true);
            }
            finally
            {
                _busy = false;
                if (_pending != null) { _pending.Dispose(); _pending = null; }
                UpdateEnabled();
                RenderTranscript();
            }
        }

        /// <summary>卡片被隐藏 / 重建时取消还没回来的请求。</summary>
        public void Cancel()
        {
            try { if (_pending != null) _pending.Cancel(); }
            catch { }
        }

        void TrimHistory()
        {
            // 轮询模式最多保留最近 12 条（6 轮）；单次模式只会有一条。
            const int maxMessages = 12;
            while (_messages.Count > maxMessages) _messages.RemoveAt(0);
        }

        List<object> BuildMessages()
        {
            var list = new List<object>();

            if (!string.IsNullOrWhiteSpace(_config.SystemPrompt))
                list.Add(new Dictionary<string, object> { { "role", "system" }, { "content", _config.SystemPrompt.Trim() } });

            foreach (Message message in _messages)
                list.Add(new Dictionary<string, object> { { "role", message.Role }, { "content", message.Text } });

            return list;
        }

        async Task<string> RequestAsync(List<object> messages, CancellationToken token)
        {
            var body = new Dictionary<string, object>
            {
                { "model", _config.Model },
                { "messages", messages },
                { "stream", false }
            };

            string url = _config.BaseUrl + "/chat/completions";

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                if (_config.ApiKey.Length > 0)
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _config.ApiKey);

                using (HttpResponseMessage response =
                       await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token))
                {
                    string payload = await response.Content.ReadAsStringAsync(token);

                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(
                            "HTTP " + (int)response.StatusCode + "：" + FirstError(payload));

                    string content = ExtractContent(payload);
                    if (string.IsNullOrWhiteSpace(content))
                        throw new InvalidOperationException("接口返回里没有内容：" + ClipText(payload, 160));

                    return content.Trim();
                }
            }
        }

        // ==================== JSON ====================

        static string ExtractContent(string payload)
        {
            var root = Json.Parse(payload) as Dictionary<string, object>;
            if (root == null) return "";

            var choices = root.ContainsKey("choices") ? root["choices"] as List<object> : null;
            if (choices == null || choices.Count == 0) return "";

            var choice = choices[0] as Dictionary<string, object>;
            if (choice == null) return "";

            var message = choice.ContainsKey("message") ? choice["message"] as Dictionary<string, object> : null;
            if (message != null && message.ContainsKey("content"))
                return Flatten(message["content"]);

            // 少数兼容层返回的是 choices[0].text
            return choice.ContainsKey("text") ? Flatten(choice["text"]) : "";
        }

        /// <summary>content 一般是字符串；有些服务返回 [{type:"text", text:"…"}, …]。</summary>
        static string Flatten(object node)
        {
            if (node == null) return "";

            if (node is string text) return text;

            if (node is List<object> parts)
            {
                var sb = new StringBuilder();
                foreach (object part in parts)
                {
                    var map = part as Dictionary<string, object>;
                    if (map != null && map.ContainsKey("text")) sb.Append(Flatten(map["text"]));
                    else sb.Append(Flatten(part));
                }
                return sb.ToString();
            }

            var dict = node as Dictionary<string, object>;
            if (dict != null && dict.ContainsKey("text")) return Flatten(dict["text"]);

            return node.ToString();
        }

        static string FirstError(string payload)
        {
            try
            {
                var root = Json.Parse(payload) as Dictionary<string, object>;
                if (root != null && root.ContainsKey("error"))
                {
                    var error = root["error"] as Dictionary<string, object>;
                    if (error != null && error.ContainsKey("message")) return Convert.ToString(error["message"]);
                    return Convert.ToString(root["error"]);
                }
            }
            catch { }

            return ClipText(payload, 160);
        }

        static string ClipText(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        static string Explain(Exception ex)
        {
            if (ex is HttpRequestException) return "连不上接口：" + ex.Message;
            return ex.Message;
        }

        // ==================== 显示 ====================

        void RenderTranscript()
        {
            if (_messages.Count == 0)
            {
                _answerHost.Visibility = Visibility.Collapsed;
                _answer.Text = "";
                return;
            }

            _answerHost.Visibility = Visibility.Visible;

            var sb = new StringBuilder();
            foreach (Message message in _messages)
            {
                sb.Append(message.Role == "user" ? "你：" : "AI：");
                sb.Append(message.Text.Trim());
                sb.Append('\n');
            }

            string text = sb.ToString().TrimEnd('\n');
            const int maxChars = 4000;
            if (text.Length > maxChars) text = "…" + text.Substring(text.Length - maxChars);

            _answer.Text = text;

            _scroller.UpdateLayout();
            _scroller.ScrollToVerticalOffset(_scroller.ExtentHeight);
        }

        // ==================== 小工具 ====================

        static Color TryColor(string hex, Color fallback)
        {
            try { return OverlaySurface.ParseColor(hex); }
            catch { return fallback; }
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
