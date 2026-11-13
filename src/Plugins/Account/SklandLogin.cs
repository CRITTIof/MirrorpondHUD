using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MirrorpondHUD.Cards;

/// <summary>扫码会话：二维码内容 + 轮询用的 scanId。</summary>
internal sealed class SklandQrSession
{
    public string ScanId = "";
    public string ScanUrl = "";
}

internal enum SklandScanState
{
    Pending,
    Confirmed,
    Failed
}

/// <summary>一次轮询的结果。</summary>
internal sealed class SklandScanResult
{
    public SklandScanState State;
    public string ScanCode = "";
    public string Token = "";
    public string Message = "";
}

/// <summary>
/// 森空岛扫码登录（2.47.0）：直接复刻官方 App 的登录链路，用户不用再从浏览器请求头里找 cred/salt。
///
/// 链路：
///  1. POST as.hypergryph.com/general/v1/gen_scan/login   → scanId + hypergryph://scan_login?scanId=…
///  2. GET  …/general/v1/scan_status?scanId=…             → 确认后给 scanCode
///  3. POST …/user/auth/v1/token_by_scan_code             → 通行证 access_token
///  4. POST …/user/oauth2/v2/grant                        → 森空岛授权 code
///  5. POST zonai.skland.com/api/v1/user/auth/generate_cred_by_code
///        （网页端对这一步跳过签名，只带 platform / vName / timestamp）→ cred + token(=salt)
///
/// 只读：不调用签到等任何写接口；凭据只回填到当前卡片设置，不落额外文件。
/// </summary>
internal static class SklandLogin
{
    const string AppCode = "4ca99fa6b56cc2ba";
    const string SklandUa = "Skland/1.32.0 (com.hypergryph.skland; build:103200; Android 33; ) Okhttp/4.11.0";

    static readonly HttpClient Http = CreateClient();

    public static async Task<SklandQrSession> StartAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://as.hypergryph.com/general/v1/gen_scan/login")
        {
            Content = JsonContent("{\"appCode\":\"" + AppCode + "\"}")
        };
        using JsonDocument doc = await SendAsync(request, token);
        JsonElement root = doc.RootElement;
        EnsureOk(root, "生成二维码");
        if (!root.TryGetProperty("data", out JsonElement data))
            throw new InvalidOperationException("生成二维码失败：返回缺少 data");

        var session = new SklandQrSession
        {
            ScanId = Str(data, "scanId"),
            ScanUrl = Str(data, "scanUrl")
        };
        if (session.ScanId.Length == 0 || session.ScanUrl.Length == 0)
            throw new InvalidOperationException("生成二维码失败：scanId / scanUrl 为空");
        return session;
    }

    public static async Task<SklandScanResult> PollAsync(string scanId, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://as.hypergryph.com/general/v1/scan_status?scanId=" + Uri.EscapeDataString(scanId));
        using JsonDocument doc = await SendAsync(request, token);
        JsonElement root = doc.RootElement;

        int status = Int(root, "status");
        int code = Int(root, "code");
        string message = Str(root, "msg");
        if (message.Length == 0) message = Str(root, "message");

        if (root.TryGetProperty("data", out JsonElement data))
        {
            string scanCode = Str(data, "scanCode", "scan_code");
            string passport = Str(data, "token");
            if (scanCode.Length > 0 || passport.Length > 0)
                return new SklandScanResult { State = SklandScanState.Confirmed, ScanCode = scanCode, Token = passport };

            // 有些实现把 scanCode 直接放 data 字符串。
            if (data.ValueKind == JsonValueKind.String && data.GetString()?.Length > 0)
                return new SklandScanResult { State = SklandScanState.Confirmed, ScanCode = data.GetString() };
        }

        if (status == 0 || code == 0)
            return new SklandScanResult { State = SklandScanState.Pending, Message = message };

        // 100 = 未扫码 / 已扫码待确认，继续等；其余是明确失败。
        if (status == 100 || status == 101)
            return new SklandScanResult { State = SklandScanState.Pending, Message = message };

        return new SklandScanResult
        {
            State = SklandScanState.Failed,
            Message = message.Length > 0 ? message : $"扫码状态异常（status={status}）"
        };
    }

    /// <summary>用 scanCode（或轮询直接拿到的通行证 token）换 cred / salt。</summary>
    public static async Task<(string Cred, string Salt)> CompleteAsync(SklandScanResult scan, CancellationToken token)
    {
        string passport = scan.Token;
        if (passport.Length == 0)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://as.hypergryph.com/user/auth/v1/token_by_scan_code")
            {
                Content = JsonContent("{\"scanCode\":\"" + Escape(scan.ScanCode) + "\"}")
            };
            using JsonDocument doc = await SendAsync(request, token);
            JsonElement root = doc.RootElement;
            EnsureOk(root, "确认扫码");
            JsonElement data = DataOf(root);
            passport = Str(data, "token", "accessToken", "access_token");
        }
        if (passport.Length == 0) throw new InvalidOperationException("扫码成功但没拿到通行证 token");

        string authCode;
        using (var grantRequest = new HttpRequestMessage(HttpMethod.Post,
                   "https://as.hypergryph.com/user/oauth2/v2/grant")
               {
                   Content = JsonContent("{\"token\":\"" + Escape(passport)
                                         + "\",\"appCode\":\"" + AppCode + "\",\"type\":0}")
               })
        using (JsonDocument grant = await SendAsync(grantRequest, token))
        {
            JsonElement root = grant.RootElement;
            EnsureOk(root, "换取森空岛授权");
            authCode = Str(DataOf(root), "code", "authCode");
        }
        if (authCode.Length == 0) throw new InvalidOperationException("换取森空岛授权码失败");

        using var credRequest = new HttpRequestMessage(HttpMethod.Post,
            "https://zonai.skland.com/api/v1/user/auth/generate_cred_by_code")
        {
            Content = JsonContent("{\"kind\":1,\"code\":\"" + Escape(authCode) + "\"}")
        };
        credRequest.Headers.TryAddWithoutValidation("platform", "3");
        credRequest.Headers.TryAddWithoutValidation("vName", "1.0.0");
        credRequest.Headers.TryAddWithoutValidation("timestamp",
            (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1).ToString());

        using JsonDocument credDoc = await SendAsync(credRequest, token);
        JsonElement credRoot = credDoc.RootElement;
        EnsureOk(credRoot, "生成森空岛凭据");
        JsonElement credData = DataOf(credRoot);
        string cred = Str(credData, "cred");
        // generate_cred_by_code 返回的 token 就是后续请求签名用的 salt。
        string salt = Str(credData, "token", "salt");
        if (cred.Length == 0 || salt.Length == 0)
            throw new InvalidOperationException("森空岛凭据为空（cred/token 缺失）");
        return (cred, salt);
    }

    static async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", SklandUa);
        using HttpResponseMessage response = await Http.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{Trim(body)}");

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("接口返回的不是 JSON：" + Trim(body));
        }
    }

    static void EnsureOk(JsonElement root, string action)
    {
        int code = Int(root, "code");
        int status = Int(root, "status");
        bool ok = root.TryGetProperty("code", out _) ? code == 0 : status == 0;
        if (ok) return;

        string message = Str(root, "msg");
        if (message.Length == 0) message = Str(root, "message");
        throw new InvalidOperationException($"{action}失败：{(message.Length > 0 ? message : "未知错误")}");
    }

    static JsonElement DataOf(JsonElement root) =>
        root.TryGetProperty("data", out JsonElement data) ? data : root;

    static string Str(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString();
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        return "";
    }

    static int Int(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return -1;
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out int number) ? number : -1;
    }

    static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string Trim(string body)
    {
        string text = (body ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= 160 ? text : text.Substring(0, 160) + "…";
    }

    static StringContent JsonContent(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(SklandUa);
        return client;
    }
}
