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
    public sealed class ChatCard : RichCardModuleBase, ICardHotkeySource, ICardHotkeyTarget,
                                   ICardSettingsModule, ICardEditorSection, ICardSummary, ICardStyleAware,
                                   ICardSelfTest, ICardRuntimeSettingsMerge
    {
        /// <summary>对话设置存在 <see cref="CardSettings.Extras"/> 里的键名。</summary>
        public const string SettingsKey = "chat";
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
            ChatSettings config = ChatOf(context == null ? null : context.Config);

            return new ChatView(context == null ? null : context.Settings, config,
                                context == null ? null : context.Host);
        }

        /// <summary>
        /// P1-5：宿主只在「内容签名没变」时调这里（改卡片名 / 热键 / 间距，或应用设置后换掉
        /// 配置对象树）。视图必须跟着换成宿主当前这棵对象树，否则它会长期捏着旧的
        /// <see cref="Settings"/> / <see cref="ChatSettings"/>；之后按热键切模式时保存整份配置
        /// 会把用户刚保存的新名字 / 新热键 / 新外观回滚成旧值。2.16.0 起宿主提供
        /// <see cref="ICardHost.SaveSettings"/>，视图只刷新引用，写回落在当前树上。
        /// </summary>
        public override void UpdateContent(FrameworkElement content, CardRenderContext context)
        {
            var view = content as ChatView;
            if (view == null || context == null) return;

            view.Refresh(context.Settings,
                         context.Config ?? (context.Host == null ? null : context.Host.Config),
                         context.Host);
        }

        // ==================== 模块自己的设置 ====================

        /// <summary>这张卡片自己的对话设置；缺省时回落到默认值并写回配置。</summary>
        public static ChatSettings ChatOf(CardSettings card)
        {
            if (card == null) return new ChatSettings();

            ChatSettings chat = card.ExtrasAs<ChatSettings>(SettingsKey);
            chat.Normalize();
            card.SetExtras(SettingsKey, chat);
            return chat;
        }

        /// <inheritdoc />
        public void InitializeCard(CardSettings config)
        {
            if (config == null) return;
            ChatOf(config);
        }

        /// <inheritdoc />
        public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged)
        {
            return new ChatCardSettingsView(config, onChanged);
        }

        /// <summary>设置窗保存时只合并用户没改过的对话模式（热键切换会绕过设置窗直接写 live）。</summary>
        public void MergeRuntimeFields(CardSettings target, CardSettings live, CardSettings baseline)
        {
            if (target == null || live == null || baseline == null) return;

            string liveMode = ChatSettings.NormalizeMode(ChatOf(live).Mode);
            string targetMode = ChatSettings.NormalizeMode(ChatOf(target).Mode);
            string baselineMode = ChatSettings.NormalizeMode(ChatOf(baseline).Mode);
            if (!string.Equals(targetMode, baselineMode, StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(targetMode, liveMode, StringComparison.OrdinalIgnoreCase)) return;

            ChatOf(target).Mode = liveMode;
        }

        public string RunSelfTest()
        {
            if (ChatSettings.NormalizeMode("polling") != "Context") return "轮询模式归一化失败";
            if (ChatSettings.NormalizeMode("") != "Single") return "空模式没有归一到单次";
            var chat = new ChatSettings { BaseUrl = " https://example.com/v1/ ", TimeoutSeconds = 999 };
            chat.Normalize();
            if (chat.BaseUrl != "https://example.com/v1") return "BaseUrl 归一化失败";
            if (chat.TimeoutSeconds != 300) return "超时时间没有夹紧";
            return null;
        }

        /// <summary>只换外观：面板就地重套样式，对话历史不丢。</summary>
        public void ApplyStyle(FrameworkElement content, CardRenderContext context)
        {
            var view = content as ChatView;
            if (view == null || context == null || context.Settings == null) return;

            view.ApplyStyle(context.Settings);   // 基类：换外观 / CLI 尺寸 / HostSettings
            view.Refresh(context.Settings,       // 同时换 ChatSettings 与 ICardHost（P1-5）
                         context.Config ?? (context.Host == null ? null : context.Host.Config),
                         context.Host);
        }

        /// <inheritdoc />
        public string Summary(CardSettings config)
        {
            ChatSettings chat = ChatOf(config);
            if (string.IsNullOrWhiteSpace(chat.Model)) return "未填写模型";
            return chat.Model;
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
    internal sealed class ChatView : CliPanelView, IDisposable
    {
        /// <summary>共享 HttpClient：连接复用，避免每问一次握手一次。</summary>
        static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        /// <summary>「思考中」的盲文转圈。</summary>
        static readonly string[] Spin = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        /// <summary>
        /// 对话设置：跟着宿主的 <see cref="CardSettings.Extras"/> 走。应用设置后宿主会换新对象树，
        /// 所以不能是 readonly —— <see cref="Refresh"/> 会把它换到当前树上（P1-5）。
        /// </summary>
        ChatSettings _config;

        /// <summary>这张卡片的宿主服务（状态目录 / 重绘）；由 CardRenderContext 传入，刷新时一起换。</summary>
        ICardHost _host;

        readonly List<Message> _messages = new List<Message>();
        /// <summary>每一轮问答那块内部小面板，和 <see cref="_messages"/> 里的助手回复一一对应。</summary>
        readonly List<Round> _rounds = new List<Round>();

        CancellationTokenSource _pending;
        bool _busy;
        bool _disposed;
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

        public ChatView(Settings settings, ChatSettings config, ICardHost host = null)
            : base(settings, settings == null ? new CliSettings() : settings.Cli)
        {
            _host = host;
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

        // ==================== 配置刷新（P1-5） ====================

        /// <summary>
        /// 应用设置后宿主可能换成新的 <see cref="Settings"/> / <see cref="CardSettings"/> 对象树，
        /// 但内容签名没变时不会重建控件。这里把视图缓存的引用换到宿主当前这棵树上：
        /// 对话历史保留，模式切换与写盘都落到当前配置，不会整份回滚。
        /// </summary>
        public void Refresh(Settings settings, CardSettings card, ICardHost host)
        {
            if (settings != null)
            {
                HostSettings = settings;
                if (settings.Cli != null) Cli = settings.Cli;
            }

            if (card != null) _config = ChatCard.ChatOf(card);

            _host = host;
            RefreshHeader();
            UpdateEnabled();
            if (!_busy) SetStatus(IdleHint(), false);
        }

        // ==================== 状态 ====================

        /// <summary>
        /// 接口地址能不能用（P1-10）：必须能解析成绝对的 http / https 地址，不再用
        /// <c>StartsWith("http")</c> —— 那连 <c>httpx://</c> 这种拼写都会放行。
        /// 带 API Key 时只允许 https，避免 Bearer Key 明文过网；本机回环
        /// （127.0.0.1 / localhost / ::1，<see cref="Uri.IsLoopback"/>）例外：
        /// 本地 vLLM / LM Studio / Ollama 的兼容层通常就是 http。
        /// </summary>
        bool Configured
        {
            get
            {
                if (string.IsNullOrEmpty(_config.BaseUrl) || string.IsNullOrEmpty(_config.Model)) return false;
                return IsSafeEndpoint(_config.BaseUrl, _config.PlainApiKey.Length > 0);
            }
        }

        /// <summary>
        /// 接口地址安全闸门（P1-10）：绝对 http/https；带 Key 时只允许 https，
        /// 本机回环（127.0.0.1 / localhost / ::1）允许 http 便于本地模型。
        /// </summary>
        internal static bool IsSafeEndpoint(string baseUrl, bool hasKey)
        {
            Uri uri;
            if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out uri)) return false;

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) return false;

            return !hasKey || uri.IsLoopback;
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

            if (!Configured) return "! " + ConfigHint();

            return "● 就绪 · 回车发送 · " + ModeKey + " 切换单次 / 轮询";
        }

        /// <summary>配置不可用时的原因（区分「没填」和「地址不安全」，P1-10）。</summary>
        string ConfigHint()
        {
            if (string.IsNullOrEmpty(_config.BaseUrl) || string.IsNullOrEmpty(_config.Model))
                return "还没配置：设置 → 内容 →「AI 接口」填地址、模型、Key";

            return "接口地址要用 http / https；带 API Key 时必须 https（本机 127.0.0.1 / localhost 除外）";
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

            // P1-5：以宿主当前配置为准（应用设置后宿主会换新对象），别在旧对象上切换。
            ChatSettings live = CurrentChatConfig();
            if (live != null) _config = live;

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

        /// <summary>宿主当前的对话配置；拿不到（截图 / 自检没传 host）就退回视图缓存的那份。</summary>
        ChatSettings CurrentChatConfig()
        {
            if (_host != null && _host.Config != null) return ChatCard.ChatOf(_host.Config);
            return _config;
        }

        /// <summary>模式改动要立刻落盘，下次启动还是这个模式。</summary>
        void Persist()
        {
            // P1-5：只写宿主当前这份 Settings，绝不写视图手里的旧对象树。
            // 1) 卡片对象以宿主为准：应用设置时 ICardHost.Config 已经换成新对象；
            // 2) 它还必须确实挂在 HostSettings.Cards 上（引用比较），否则说明这是被设置应用
            //    替换掉的旧视图 —— 此时整份 Save 会把用户刚保存的新名字 / 新热键 / 新外观
            //    回滚成旧值。宁可这次不落盘（设置应用会重建视图），也不允许整份回滚。
            Settings live = HostSettings;
            CardSettings card = FindLiveCard(live);
            if (live == null || card == null) return;

            ChatSettings chat = ChatCard.ChatOf(card);
            chat.Mode = _config.Mode;   // 把模式写进当前树，再保存这一份
            _config = chat;

            if (_host == null) return;
            if (!_host.SaveSettings())
                Log.Warn("保存对话模式失败：宿主拒绝了配置写入");
        }

        /// <summary>在当前 Settings 里找这张对话卡的配置；找不到返回 null（旧视图，不能写盘）。</summary>
        CardSettings FindLiveCard(Settings live)
        {
            if (live == null || live.Cards == null) return null;

            // 宿主服务的 Config 永远是当前对象；还要确认它在这份 Settings 的卡片列表里。
            CardSettings hostCard = _host == null ? null : _host.Config;
            if (hostCard != null && live.Cards.Contains(hostCard)) return hostCard;

            // 没有 host（截图 / 自检）时按引用在当前树里回溯；只回退到「chat」卡，
            // 避免 ChatOf 往别的卡片 Extras 里写一份对话设置。
            foreach (CardSettings card in live.Cards)
            {
                if (card == null || !string.Equals(card.Kind, "chat", StringComparison.OrdinalIgnoreCase)) continue;
                if (ReferenceEquals(ChatCard.ChatOf(card), _config)) return card;
            }
            return null;
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
            // 顶层兜底：try 之前的 AddEntry / SetStatus 等 UI 调用一旦抛异常，
            // async void 会把它交给 Dispatcher，默认行为是结束进程。
            try
            {
                await SendCore();
            }
            catch (Exception ex)
            {
                _busy = false;
                _spin.Stop();
                if (_pending != null) { _pending.Dispose(); _pending = null; }
                // 极端异常（AddEntry / SetInputReadOnly 等）也要把输入框恢复成可输入，
                // 否则一次异常之后这张卡片会永久只读，只能重开窗口。
                UpdateEnabled();
                SetStatus("! " + Explain(ex), true);
                Log.Error("对话请求异常", ex);
            }
        }

        async Task SendCore()
        {
            if (_busy) return;

            string text = (Input.Text ?? "").Trim();
            if (text.Length == 0) return;

            if (!Configured)
            {
                SetStatus(ConfigHint(), true);
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

        /// <summary>
        /// 宿主 <c>ReleaseContent</c> 会在换设置 / 删卡 / 退出时 Dispose 实现了 <see cref="IDisposable"/>
        /// 的卡片内容；之前 ChatView 没实现，销毁只能靠 <see cref="Cancel"/> 兜着。这里取消在途请求、
        /// 停掉转圈定时器，避免已释放的视图还挂着 <see cref="DispatcherTimer"/>。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Cancel();

            // DispatcherTimer 只能在自己的 Dispatcher 线程上 Stop；正常调用点（ReleaseContent）就在 UI 线程。
            if (_spin.Dispatcher.CheckAccess())
            {
                _spin.Stop();
                _spin.Tick -= OnSpinTick;
            }

            if (_pending != null)
            {
                try { _pending.Dispose(); } catch (ObjectDisposedException) { }
                _pending = null;
            }
        }

        /// <summary>
        /// 上下文保留多少条消息：一块记录（一轮问答）是两条（问 + 答），
        /// 跟随「外观 → CLI 交互面板 → 最多保留」；至少 2 条，避免设成 1 时把问题也裁掉。
        /// </summary>
        internal static int ContextMessageLimit(int maxEntries)
        {
            return Math.Max(2, Math.Max(1, maxEntries) * 2);
        }

        void TrimHistory()
        {
            int maxMessages = ContextMessageLimit(Cli.MaxEntries);
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
