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
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 「AI 对话」卡片：命令行（CLI）样式的一张终端。
    ///
    /// 版式由 <see cref="CliPanelView"/> 提供（和「命令行」卡片共用）：第一行模型名和当前模式，
    /// 下面是带 <c>&gt;</c> 提示符的输入行，中间是记录区 —— **每一轮问答是一块内部小面板**，
    /// 各带自己的滑块和高度上限，轮次多了不会把整块面板顶高。
    ///
    /// 走 OpenAI 兼容的 <c>POST {BaseUrl}/chat/completions</c>，所以 OpenAI / DeepSeek / 通义 /
    /// 本地 vLLM / LM Studio / Ollama 的兼容层都能接（每张卡片自己一套 BaseUrl + 模型 + Key，
    /// 想同时挂两个模型就放两张卡）。
    ///
    /// 两种模式<b>不放在卡片上</b>：切换走「设置 → 热键」里的全局热键（默认 Ctrl+Alt+Shift+M），
    /// 清空上下文也有一个可配热键。卡片只留输入行和记录，看起来就像个终端。
    /// - 单次：每次提问都不带历史，只问这一句；
    /// - 轮询：保留上下文，把之前的问答一起发回去，形成连续对话。
    ///
    /// 卡片声明 <see cref="NeedsMouseInput"/>：输入框要收鼠标和键盘，所以这张卡不点击穿透，
    /// 会挡住它身下的桌面点击（其余卡片不受影响）。
    /// </summary>
    public sealed class ChatCard : RichCardModuleBase, ICardHotkeySource, ICardHotkeyTarget
    {
        public const string ActionToggleMode = "chat.toggle-mode";
        public const string ActionClear = "chat.clear";

        /// <summary>
        /// 没在设置里改过时的默认切换热键（和设置页里的默认值一致）。
        /// 用四键组合：三键的 Ctrl+Alt+M / Ctrl+Shift+M 之类太容易被别的程序占掉或抢走。
        /// </summary>
        public const string DefaultModeHotkey = "Ctrl+Alt+Shift+M";

        static readonly List<CardHotkeyAction> Actions = new List<CardHotkeyAction>
        {
            new CardHotkeyAction(ActionToggleMode, "AI 对话：切换单次 / 轮询", DefaultModeHotkey),
            new CardHotkeyAction(ActionClear, "AI 对话：清空当前上下文", "")
        };

        public override string Kind { get { return "chat"; } }
        public override string DisplayName { get { return "AI 对话"; } }
        public override string Icon { get { return Icons.Agent; } }
        public override bool NeedsMouseInput { get { return true; } }
        public override int Order { get { return 40; } }

        /// <summary>「热键」页里 AI 对话卡片那一组动作（对所有对话卡同时生效）。</summary>
        public IList<CardHotkeyAction> HotkeyActions { get { return Actions; } }

        public void ExecuteHotkey(string action, FrameworkElement content)
        {
            var view = content as ChatView;
            if (view != null) view.HandleHotkey(action);
        }

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
    /// AI 对话卡片的终端界面与请求逻辑。一张卡片一个实例，互不影响（各带自己的上下文）。
    /// </summary>
    internal sealed class ChatView : CliPanelView
    {
        /// <summary>共享 HttpClient：连接复用，避免每问一次握手一次。</summary>
        static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        /// <summary>「思考中」的盲文转圈。</summary>
        static readonly string[] Spin = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        readonly ChatSettings _config;

        readonly List<Message> _messages = new List<Message>();
        /// <summary>每一轮问答那块内部小面板，和 <see cref="_messages"/> 里的助手回复一一对应。</summary>
        readonly List<Round> _rounds = new List<Round>();

        CancellationTokenSource _pending;
        bool _busy;
        int _spinFrame;
        readonly DispatcherTimer _spin;

        sealed class Message
        {
            public string Role;
            public string Text;
        }

        sealed class Round
        {
            public CliEntryPanel Panel;
            public TextBlock Question;
            public TextBlock Answer;
        }

        public ChatView(Settings settings, ChatSettings config)
            : base(settings, settings == null ? new CliSettings() : settings.Cli)
        {
            _config = config ?? new ChatSettings();
            _config.Normalize();

            SetPrompt(">");

            _spin = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(90)
            };
            _spin.Tick += OnSpinTick;

            RefreshHeader();
            UpdateEnabled();
            SetStatus(IdleHint(), false);
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

        /// <summary>切换模式的热键（没配置时显示默认值）。</summary>
        string ModeKey
        {
            get
            {
                string key = HostSettings == null || HostSettings.Hotkeys == null
                    ? ""
                    : HostSettings.Hotkeys.GetCardAction(ChatCard.ActionToggleMode);
                return string.IsNullOrWhiteSpace(key) ? ChatCard.DefaultModeHotkey : key.Trim();
            }
        }

        string IdleHint()
        {
            if (_config.ApiKeyUnreadable)
                return "! 已存的 API Key 解不开（换过 Windows 用户 / 机器？），去设置里重填";

            if (!Configured)
                return "! 还没配置：设置 → 内容 →「AI 接口」填地址、模型、Key";

            return "● 就绪 · 回车发送 · " + ModeKey + " 切换单次 / 轮询";
        }

        void OnSpinTick(object sender, EventArgs e)
        {
            if (!_busy)
            {
                _spin.Stop();
                return;
            }

            _spinFrame = (_spinFrame + 1) % Spin.Length;
            SetStatus(Spin[_spinFrame] + " 思考中…", false);
        }

        void RefreshHeader()
        {
            SetTitle(_config.Model.Length > 0 ? _config.Model : "（未配置模型）");
            SetBadge(
                _config.Mode == "Context" ? "[轮询]" : "[单次]",
                _config.Mode == "Context"
                    ? "轮询：保留上下文，连续对话（" + ModeKey + " 切换）"
                    : "单次：每次提问都不带历史（" + ModeKey + " 切换）");
        }

        // ==================== 热键 ====================

        /// <summary>宿主转发过来的全局热键：切换模式 / 清空上下文。</summary>
        public void HandleHotkey(string action)
        {
            if (string.Equals(action, ChatCard.ActionClear, StringComparison.OrdinalIgnoreCase))
            {
                ClearConversation();
                return;
            }

            if (!string.Equals(action, ChatCard.ActionToggleMode, StringComparison.OrdinalIgnoreCase)) return;
            ToggleMode();
        }

        void ToggleMode()
        {
            if (_busy) return;

            _config.Mode = _config.Mode == "Context" ? "Single" : "Context";
            _messages.Clear();          // 换模式就当开新对话，避免把上一轮的历史混进来
            ClearRounds();
            RefreshHeader();
            Persist();

            SetStatus(_config.Mode == "Context"
                ? "[轮询] 已切到轮询：保留上下文，连续对话"
                : "[单次] 已切到单次：每次提问都不带历史", false);
        }

        void ClearConversation()
        {
            _messages.Clear();
            ClearRounds();
            SetStatus("已清空上下文", false);
        }

        void ClearRounds()
        {
            _rounds.Clear();
            ClearEntries();
        }

        /// <summary>模式改动要立刻落盘，下次启动还是这个模式。</summary>
        void Persist()
        {
            if (HostSettings == null) return;
            try { ConfigStore.Save(HostSettings); }
            catch (Exception ex) { Log.Warn("保存对话模式失败: " + ex.Message); }
        }

        void UpdateEnabled()
        {
            // 不要用 IsEnabled = false：那会让输入框丢焦点，等回答回来键盘就打不进去了。
            // 只读 + Send() 里的 _busy 判断就够了，焦点一直留在提示符后面。
            SetInputReadOnly(_busy);
        }

        // ==================== 对话 ====================

        protected override void SubmitInput()
        {
            Send();
        }

        async void Send()
        {
            if (_busy) return;

            string text = (Input.Text ?? "").Trim();
            if (text.Length == 0) return;

            if (!Configured)
            {
                SetStatus("接口地址或模型名为空，见「设置 → 内容 → AI 接口」", true);
                return;
            }

            if (_config.ApiKeyUnreadable)
            {
                SetStatus("已存的 API Key 解不开，请到设置里重新填", true);
                return;
            }

            bool context = _config.Mode == "Context";
            if (!context)
            {
                _messages.Clear();
                ClearRounds();
            }

            _messages.Add(new Message { Role = "user", Text = text });
            TrimHistory();

            Input.Text = "";

            // 这一轮问答自己一块内部小面板：问题暗一点，回答亮一点，各自超出高度上限走自己的滑块。
            var round = new Round { Panel = AddEntry() };
            round.Question = round.Panel.AddText("> " + text, 0.72, false, 0, 3);
            round.Answer = round.Panel.AddText("…", 0.95, false, 0, 0);
            _rounds.Add(round);
            TrimEntries(Cli.MaxEntries);
            round.Panel.ScrollToEnd();
            ScrollRecordsToEnd();

            _pending = new CancellationTokenSource(TimeSpan.FromSeconds(_config.TimeoutSeconds));

            _busy = true;
            UpdateEnabled();
            _spinFrame = 0;
            SetStatus(Spin[0] + " 思考中…", false);
            _spin.Start();

            try
            {
                string reply = await RequestAsync(BuildMessages(), _pending.Token);
                _messages.Add(new Message { Role = "assistant", Text = reply });
                TrimHistory();
                if (round.Answer != null) round.Answer.Text = reply;
                SetStatus("● 完成 · 回车继续提问", false);
            }
            catch (OperationCanceledException)
            {
                if (round.Answer != null) round.Answer.Text = "（已取消）";
                SetStatus("已取消（或超过 " + _config.TimeoutSeconds + " 秒）", true);
            }
            catch (Exception ex)
            {
                if (round.Answer != null) round.Answer.Text = "! " + Explain(ex);
                SetStatus(Explain(ex), true);
            }
            finally
            {
                _busy = false;
                _spin.Stop();
                if (_pending != null) { _pending.Dispose(); _pending = null; }
                UpdateEnabled();
                ScrollRecordsToEnd();
            }
        }

        /// <summary>卡片被隐藏 / 重建时取消还没回来的请求。</summary>
        public override void Cancel()
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
            string key = _config.PlainApiKey;

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                if (key.Length > 0)
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);

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
            // 状态行只有一行：真实的换行和 JSON 里转义的 \n 都压成空格。
            text = text.Replace("\\r", " ").Replace("\\n", " ")
                       .Replace("\\\\r", " ").Replace("\\\\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        static string Explain(Exception ex)
        {
            if (ex is HttpRequestException) return "连不上接口：" + ex.Message;
            return ex.Message;
        }
    }
}
