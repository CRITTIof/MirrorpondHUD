using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// AI 对话卡自己的设置区块：接口地址 / 模型 / API Key / 模式 / 系统提示 / 超时。
    ///
    /// Key 永远不会回显：输入框里只是草稿，失焦或点「清除」时才提交；
    /// 真正的密文存在 <c>CardSettings.Extras["chat"]</c>（DPAPI）。
    /// </summary>
    public partial class ChatCardSettingsView : UserControl, INotifyPropertyChanged
    {
        readonly ChatSettings _chat;
        readonly Action _onChanged;
        string _apiKeyDraft = "";

        public ChatCardSettingsView(CardSettings config, Action onChanged)
        {
            InitializeComponent();

            _chat = ChatCard.ChatOf(config);
            _onChanged = onChanged;
            DataContext = this;
        }

        public string ChatBaseUrl
        {
            get { return _chat.BaseUrl; }
            set
            {
                string text = (value ?? "").Trim();
                if (_chat.BaseUrl == text) return;
                _chat.BaseUrl = text;
                Raise("ChatBaseUrl");
                Raise("ChatHint");
            }
        }

        public string ChatModel
        {
            get { return _chat.Model; }
            set
            {
                string text = (value ?? "").Trim();
                if (_chat.Model == text) return;
                _chat.Model = text;
                Raise("ChatModel");
                Raise("ChatHint");
            }
        }

        /// <summary>
        /// 输入框里的草稿文字。已经存过 Key 时这里永远是空字符串——不回显密钥，
        /// 框里显示的是「已填写 API Key，重新输入以更换」那行提示。
        /// 真要把新 Key 存下去，靠 <see cref="CommitChatApiKey"/>（失焦时调用）。
        /// </summary>
        public string ChatApiKey
        {
            get { return _apiKeyDraft; }
            set
            {
                string text = value ?? "";
                if (_apiKeyDraft == text) return;
                _apiKeyDraft = text;
                Raise("ChatApiKey");
                Raise("ChatApiKeyPlaceholderVisibility");
            }
        }

        /// <summary>把输入框里的 Key 加密存下（失焦时调用）；空 = 保持原来的 Key 不动。</summary>
        public void CommitChatApiKey()
        {
            string text = (_apiKeyDraft ?? "").Trim();
            if (text.Length == 0) return;

            _chat.SetApiKey(text);
            _apiKeyDraft = "";
            Raise("ChatApiKey");
            Raise("ChatApiKeyPlaceholder");
            Raise("ChatApiKeyPlaceholderVisibility");
            Raise("ChatApiKeyClearVisibility");
            Raise("ChatHint");
        }

        /// <summary>忘记 Key。</summary>
        public void ClearChatApiKey()
        {
            if (_chat.ApiKey.Length == 0) return;

            _chat.ClearApiKey();
            _apiKeyDraft = "";
            Raise("ChatApiKey");
            Raise("ChatApiKeyPlaceholder");
            Raise("ChatApiKeyPlaceholderVisibility");
            Raise("ChatApiKeyClearVisibility");
            Raise("ChatHint");
        }

        /// <summary>输入框空着时压在上面的提示（不回显密钥）。</summary>
        public string ChatApiKeyPlaceholder
        {
            get
            {
                if (_chat.ApiKeyUnreadable) return "已存的 Key 解不开（换过用户 / 机器？），请重新填写";
                if (_chat.ApiKey.Length > 0) return ChatSettings.KeyPlaceholder;
                return "还没填写（本地服务可以留空）";
            }
        }

        public Visibility ChatApiKeyPlaceholderVisibility
        {
            get { return _apiKeyDraft.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ChatApiKeyClearVisibility
        {
            get { return _chat.ApiKey.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        /// <summary>0 = 单次（每次独立），1 = 轮询（保留上下文）。</summary>
        public int ChatModeIndex
        {
            get { return ChatSettings.NormalizeMode(_chat.Mode) == "Context" ? 1 : 0; }
            set
            {
                string mode = value == 1 ? "Context" : "Single";
                if (ChatSettings.NormalizeMode(_chat.Mode) == mode) return;
                _chat.Mode = mode;
                Raise("ChatModeIndex");
                Raise("ChatHint");
            }
        }

        public string ChatSystemPrompt
        {
            get { return _chat.SystemPrompt; }
            set
            {
                string text = value ?? "";
                if (_chat.SystemPrompt == text) return;
                _chat.SystemPrompt = text;
                Raise("ChatSystemPrompt");
            }
        }

        public int ChatTimeoutSeconds
        {
            get { return _chat.TimeoutSeconds; }
            set
            {
                int v = (int)Math.Max(5, Math.Min(300, value));
                if (_chat.TimeoutSeconds == v) return;
                _chat.TimeoutSeconds = v;
                Raise("ChatTimeoutSeconds");
            }
        }

        /// <summary>设置页里的一行提示：还缺什么。</summary>
        public string ChatHint
        {
            get
            {
                if (_chat.BaseUrl.Length == 0) return "接口地址为空：例如 https://api.openai.com/v1（本地服务填到 /v1）";
                if (_chat.Model.Length == 0) return "模型名为空：卡片第一行就显示它，例如 gpt-4o-mini / deepseek-chat";
                if (!_chat.BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return "接口地址要以 http:// 或 https:// 开头";

                if (_chat.ApiKeyUnreadable)
                    return "已存的 API Key 在这台机器上解不开（换过 Windows 用户或机器），请重新填写";

                return _chat.ApiKey.Length == 0
                    ? "没填 Key：本地服务（LM Studio / Ollama 兼容层）可以留空；云端接口需要填"
                    : "Key 已用 Windows DPAPI 加密存在 settings.json 里（只有当前用户能解），请求发到 "
                      + _chat.BaseUrl + "/chat/completions";
            }
        }

        // ==================== 事件 ====================

        void ChatApiKeyBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitChatApiKey();
        }

        void ClearChatApiKeyClick(object sender, RoutedEventArgs e)
        {
            ClearChatApiKey();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void Raise(string name)
        {
            _onChanged?.Invoke();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
