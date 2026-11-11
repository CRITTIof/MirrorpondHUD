using System;

namespace MirrorpondHUD.Core;

/// <summary>AI 对话卡自己的设置（存在 CardSettings.Extras["chat"]）。</summary>
public sealed class ChatSettings
{
    /// <summary>设置页里「已经存了 Key」时输入框里的提示（不回显密钥本身）。</summary>
    public const string KeyPlaceholder = "已填写 API Key，重新输入以更换";

    /// <summary>接口根地址，例如 https://api.openai.com/v1（末尾的 / 会自动去掉）。</summary>
    public string BaseUrl = "https://api.openai.com/v1";

    /// <summary>模型名，显示在卡片第一行。</summary>
    public string Model = "";

    /// <summary>
    /// API Key 的落盘形式：<c>dpapi:&lt;base64&gt;</c>，用 Windows DPAPI（当前用户）加密，
    /// 只有同一台机器、同一个 Windows 用户能解开。空 = 没填（本地服务可以留空）。
    /// </summary>
    public string ApiKey = "";

    /// <summary>Single = 每次提问都不带历史；Context = 保留上下文连续对话（轮询）。</summary>
    public string Mode = "Single";

    /// <summary>系统提示词；空 = 不发送。</summary>
    public string SystemPrompt = "";

    /// <summary>请求超时（秒）。</summary>
    public int TimeoutSeconds = 60;

    // 旧的 Width / AnswerLines 字段已删除：宽度与答案区行数现在统一由 CLI 面板
    // （CliSettings.MaxWidth / MaxHeight）按所有卡片一致管理，这两个旧键从后端
    // 读进来后会因为「字段不存在」被 Bind 丢弃，没有消费者。

    public ChatSettings Clone() => new ChatSettings
    {
        BaseUrl = BaseUrl,
        Model = Model,
        ApiKey = ApiKey,
        Mode = Mode,
        SystemPrompt = SystemPrompt,
        TimeoutSeconds = TimeoutSeconds
    };

    string _plainKeyCache;
    string _plainKeySource;

    /// <summary>解出来的明文 Key（只在内存里，不落盘）。</summary>
    public string PlainApiKey
    {
        get
        {
            if (_plainKeyCache is not null && string.Equals(_plainKeySource, ApiKey, StringComparison.Ordinal))
                return _plainKeyCache;

            string stored = ApiKey ?? "";
            string plain;
            if (stored.Length == 0) plain = "";
            else if (!SecretStore.IsProtected(stored)) plain = stored;
            else if (!SecretStore.TryUnprotect(stored, out plain)) plain = "";

            _plainKeySource = ApiKey;
            _plainKeyCache = plain;
            return plain;
        }
    }

    /// <summary>有可用的 Key（能解开、非空）。</summary>
    public bool HasApiKey => PlainApiKey.Length > 0;

    /// <summary>settings.json 里存了 Key，但在这台机器 / 这个用户下解不开。</summary>
    public bool ApiKeyUnreadable => !string.IsNullOrEmpty(ApiKey) && SecretStore.IsProtected(ApiKey)
                && PlainApiKey.Length == 0;

    /// <summary>填 / 换 Key：进内存前先加密，落盘就只有密文；加密失败时保留旧 Key。</summary>
    public void SetApiKey(string plain)
    {
        plain = (plain ?? "").Trim();
        if (plain.Length == 0)
        {
            ApiKey = "";
            _plainKeySource = null;
            _plainKeyCache = null;
            return;
        }

        if (!SecretStore.TryProtect(plain, out var stored))
        {
            Log.Error("API Key 加密失败，保留原 Key，新 Key 未保存");
            return;
        }

        ApiKey = stored;
        _plainKeySource = null;
        _plainKeyCache = null;
    }

    /// <summary>忘记 Key（清空）。</summary>
    public void ClearApiKey()
    {
        ApiKey = "";
        _plainKeySource = null;
        _plainKeyCache = null;
    }

    public void Normalize()
    {
        BaseUrl = (BaseUrl ?? "").Trim().TrimEnd('/');
        Model = (Model ?? "").Trim();

        ApiKey = (ApiKey ?? "").Trim();
        if (ApiKey.Length > 0 && !SecretStore.IsProtected(ApiKey))
        {
            if (SecretStore.TryProtect(ApiKey, out var stored)) ApiKey = stored;
            else ApiKey = "";
        }

        Mode = NormalizeMode(Mode);
        SystemPrompt = SystemPrompt ?? "";
        TimeoutSeconds = (int)Settings.Clamp(TimeoutSeconds, 5, 300);
    }

    /// <summary>
    /// 对话模式：Single（单次） / Context（保留上下文连续对话）。
    /// 2.24.0 起只接受 Context；continuous / poll / polling 的历史别名已移除，
    /// 旧配置由 Schema 7 的 v6→v7 迁移一次性改写成 Context（见 SettingsLoader）。
    /// </summary>
    public static string NormalizeMode(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "context":
                return "Context";
            default:
                return "Single";
        }
    }
}
