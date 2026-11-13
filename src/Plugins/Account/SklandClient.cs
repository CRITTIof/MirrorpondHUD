using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 森空岛只读客户端（仅方舟）：cred + salt 换取 binding / player/info。
/// 只实现 GET；不封装签到、战斗、抽卡等任何写操作或非公开协议。
/// </summary>
public sealed class SklandClient : IDisposable
{
    const string Base = "https://zonai.skland.com";
    readonly HttpClient _http;
    readonly string _cred;
    readonly string _salt;

    public SklandClient(string cred, string salt)
    {
        _cred = (cred ?? "").Trim();
        _salt = (salt ?? "").Trim();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Skland/1.0.0 (com.hypergryph.skland; build:100000; Android 33; ) Okhttp/4.11.0");
    }

    public bool Configured => _cred.Length > 0 && _salt.Length > 0;

    /// <summary>从绑定列表里找第一个方舟角色 UID；失败返回 null + 原因。</summary>
    public async Task<(string Uid, string Nick, string Error)> FindArknightsUidAsync(CancellationToken token)
    {
        if (!Configured) return (null, null, "未填写 cred / salt");
        string path = "/api/v1/game/player/binding";
        (JsonDocument doc, string error) = await SendAsync(path, "", true, token);
        if (doc is null) return (null, null, error);

        using (doc)
        {
            try
            {
                JsonElement root = doc.RootElement;
                if (ReadCode(root) != 0)
                    return (null, null, ReadMessage(root, "森空岛返回业务错误"));

                string uid = FindFirstString(root, "uid");
                string nick = FindFirstString(root, "nickName") ?? FindFirstString(root, "nickname");
                return uid is null ? (null, null, "绑定列表中没有找到角色 UID") : (uid, nick, null);
            }
            catch (Exception ex)
            {
                return (null, null, "解析绑定列表失败: " + ex.Message);
            }
        }
    }

    /// <summary>读取方舟 player/info；返回原始 JSON 文本与错误。</summary>
    public async Task<(string Json, string Error)> GetArknightsInfoAsync(string uid, CancellationToken token)
    {
        if (!Configured) return (null, "未填写 cred / salt");
        if (string.IsNullOrWhiteSpace(uid)) return (null, "未填写 UID");
        string path = "/api/v1/game/player/info";
        string query = "?uid=" + Uri.EscapeDataString(uid.Trim());
        (JsonDocument doc, string error) = await SendAsync(path, query, false, token);
        if (doc is null) return (null, error);
        using (doc)
        {
            JsonElement root = doc.RootElement;
            int code = ReadCode(root);
            if (code != 0) return (null, ReadMessage(root, $"森空岛业务错误 {code}"));
            return (root.GetRawText(), null);
        }
    }

    async Task<(JsonDocument Doc, string Error)> SendAsync(string path, string query, bool v2, CancellationToken token)
    {
        string error = null;
        try
        {
            string timestamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1).ToString(CultureInfo.InvariantCulture);
            string sign;
            var request = new HttpRequestMessage(HttpMethod.Get, Base + path + query);
            request.Headers.TryAddWithoutValidation("cred", _cred);
            request.Headers.TryAddWithoutValidation("timestamp", timestamp);

            if (v2)
            {
                string headerJson = "{\"platform\":\"3\",\"timestamp\":\"" + timestamp
                    + "\",\"dId\":\"\",\"vName\":\"1.0.0\"}";
                string message = path + "" + timestamp + headerJson;
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_salt));
                string hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
                sign = Md5(hex);
                request.Headers.TryAddWithoutValidation("platform", "3");
                request.Headers.TryAddWithoutValidation("dId", "");
                request.Headers.TryAddWithoutValidation("vName", "1.0.0");
            }
            else
            {
                sign = Md5(path + query + timestamp + _salt);
            }

            request.Headers.TryAddWithoutValidation("sign", sign);
            using HttpResponseMessage response = await _http.SendAsync(request, token);
            string text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                error = $"HTTP {(int)response.StatusCode}: {Shorten(text)}";
                return (null, error);
            }
            return (JsonDocument.Parse(text), null);
        }
        catch (Exception ex)
        {
            error = "请求森空岛失败: " + ex.Message;
            return (null, error);
        }
    }

    static int ReadCode(JsonElement root) =>
        root.TryGetProperty("code", out JsonElement code) && code.ValueKind == JsonValueKind.Number
            ? code.GetInt32() : -1;

    static string ReadMessage(JsonElement root, string fallback) =>
        root.TryGetProperty("message", out JsonElement msg) && msg.ValueKind == JsonValueKind.String
            ? msg.GetString() : fallback;

    static string FindFirstString(JsonElement node, string key)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in node.EnumerateObject())
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    return prop.Value.ToString();
                string found = FindFirstString(prop.Value, key);
                if (found is not null) return found;
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            {
                string found = FindFirstString(item, key);
                if (found is not null) return found;
            }
        }
        return null;
    }

    static string Md5(string text)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    static string Shorten(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= 160 ? text : text.Substring(0, 160) + "…";

    public void Dispose() => _http.Dispose();
}
