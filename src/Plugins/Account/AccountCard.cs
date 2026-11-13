using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>账号卡设置，存在 <c>CardSettings.Extras["account"]</c>。凭据只保存在本地配置。</summary>
public sealed class AccountExtras
{
    public string Cred = "";
    public string Salt = "";
    public string Uid = "";
}

/// <summary>
/// 明日方舟账号状态卡：用户自行提供森空岛 cred/salt，只读拉取 binding + player/info。
/// 不实现签到/战斗/写接口；失败原因进卡片状态行和日志。
/// </summary>
public sealed class AccountCard : RichCardModuleBase, ICardSettingsModule, ICardEditorSection,
                                  ICardSelfTest, ICardSummary
{
    public const string SettingsKey = "account";

    public override string Kind => "account";
    public override string DisplayName => "明日方舟 · 账号状态";
    public override string Icon => Icons.Agent;
    public override int Order => 40;
    public override bool NeedsMouseInput => true;

    public override FrameworkElement CreateContent(CardRenderContext context)
        => new AccountView(context.Settings, context.Config);

    public void InitializeCard(CardSettings config)
    {
        if (config is null) return;
        AccountExtras extras = config.ExtrasAs<AccountExtras>(SettingsKey);
        extras.Cred = (extras.Cred ?? "").Trim();
        extras.Salt = (extras.Salt ?? "").Trim();
        extras.Uid = (extras.Uid ?? "").Trim();
        config.SetExtras(SettingsKey, extras);
    }

    public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged)
    {
        AccountExtras extras = config.ExtrasAs<AccountExtras>(SettingsKey);
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = "仅本地只读：cred / salt 只写入本机 settings.json，不发送到除森空岛外的任何服务。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var cred = new TextBox { Text = extras.Cred ?? "", Margin = new Thickness(0, 0, 0, 6) };
        var salt = new TextBox { Text = extras.Salt ?? "", Margin = new Thickness(0, 0, 0, 6) };
        var uid = new TextBox { Text = extras.Uid ?? "", Width = 160 };
        panel.Children.Add(Label("cred"));
        panel.Children.Add(cred);
        panel.Children.Add(Label("salt"));
        panel.Children.Add(salt);

        var uidRow = new StackPanel { Orientation = Orientation.Horizontal };
        uidRow.Children.Add(new TextBlock { Text = "UID", Width = 52, VerticalAlignment = VerticalAlignment.Center });
        uidRow.Children.Add(uid);
        var auto = new Button { Content = "自动获取", Padding = new Thickness(9, 2, 9, 2), Margin = new Thickness(8, 0, 0, 0) };
        var state = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 };
        uidRow.Children.Add(auto);
        uidRow.Children.Add(state);
        panel.Children.Add(uidRow);

        void Commit()
        {
            extras.Cred = (cred.Text ?? "").Trim();
            extras.Salt = (salt.Text ?? "").Trim();
            extras.Uid = (uid.Text ?? "").Trim();
            config.SetExtras(SettingsKey, extras);
            onChanged?.Invoke();
        }

        cred.TextChanged += (s, e) => Commit();
        salt.TextChanged += (s, e) => Commit();
        uid.TextChanged += (s, e) => Commit();

        auto.Click += async (s, e) =>
        {
            auto.IsEnabled = false;
            state.Text = "获取中…";
            using var client = new SklandClient(cred.Text, salt.Text);
            (string foundUid, string nick, string error) =
                await client.FindArknightsUidAsync(CancellationToken.None);
            if (foundUid is null) state.Text = error ?? "未找到";
            else
            {
                uid.Text = foundUid;
                state.Text = string.IsNullOrWhiteSpace(nick) ? foundUid : $"{nick} ({foundUid})";
            }
            auto.IsEnabled = true;
        };
        return panel;
    }

    static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 4, 0, 2),
        Opacity = 0.7
    };

    public string Validate(CardSettings config) => null;

    public string Summary(CardSettings config)
    {
        AccountExtras extras = config is null ? new AccountExtras() : config.ExtrasAs<AccountExtras>(SettingsKey);
        return string.IsNullOrWhiteSpace(extras.Uid) ? "森空岛 · 未绑定" : "森空岛 · " + extras.Uid;
    }

    public string RunSelfTest()
    {
        const string sample = "{\"code\":0,\"data\":{\"status\":{\"nickName\":\"博士\",\"level\":120,\"uid\":\"12345678\",\"ap\":{\"current\":42,\"completeRecoveryTime\":1760000000}},\"chars\":[{\"rarity\":6},{\"rarity\":4},{\"rarity\":6}]}}";
        AccountSnapshot snapshot = AccountSnapshot.Parse(sample, out string error);
        return error is null && snapshot is not null
               && snapshot.NickName == "博士" && snapshot.Level == 120
               && snapshot.CharCount == 3 && snapshot.SixStarCount == 2
            ? null : "账号快照解析失败: " + (error ?? "字段不符");
    }
}

/// <summary>player/info 的展示快照，解析与 UI 分离，便于自检。</summary>
public sealed class AccountSnapshot
{
    public string NickName = "博士";
    public int Level;
    public string Uid = "";
    public int ApCurrent;
    public string ApRecoverAt = "";
    public int CharCount;
    public int SixStarCount;

    public static AccountSnapshot Parse(string json, out string error)
    {
        error = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json ?? "");
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("data", out JsonElement data)) { error = "响应缺少 data"; return null; }
            if (!data.TryGetProperty("status", out JsonElement status)) { error = "响应缺少 data.status"; return null; }

            var snapshot = new AccountSnapshot
            {
                NickName = ReadString(status, "nickName") ?? "博士",
                Level = ReadInt(status, "level"),
                Uid = ReadString(status, "uid") ?? ""
            };

            if (status.TryGetProperty("ap", out JsonElement ap))
            {
                snapshot.ApCurrent = ReadInt(ap, "current");
                long ts = ReadLong(ap, "completeRecoveryTime");
                if (ts > 0)
                {
                    try { snapshot.ApRecoverAt = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture); }
                    catch (Exception ex) { Log.Debug($"账号卡理智恢复时间解析失败: {ex.Message}"); }
                }
            }

            if (data.TryGetProperty("chars", out JsonElement chars) && chars.ValueKind == JsonValueKind.Array)
            {
                snapshot.CharCount = chars.GetArrayLength();
                foreach (JsonElement c in chars.EnumerateArray())
                {
                    int rarity = ReadInt(c, "rarity");
                    // 方舟 player/info 的 rarity 是 1–6；旧接口 0 基偏移只在抽卡里出现。
                    if (rarity >= 6) snapshot.SixStarCount++;
                }
            }
            return snapshot;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int ReadInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;

    static long ReadLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;
}

/// <summary>账号卡界面。</summary>
internal sealed class AccountView : Grid
{
    readonly Settings _settings;
    readonly CardSettings _config;
    readonly AccountExtras _extras;
    readonly StyleSettings _style;
    readonly TextBlock _title;
    readonly TextBlock _name;
    readonly TextBlock _meta;
    readonly TextBlock _stats;
    readonly TextBlock _status;
    readonly DispatcherTimer _timer;
    readonly CancellationTokenSource _cts = new();

    bool _busy;
    bool _disposed;

    public AccountView(Settings settings, CardSettings config)
    {
        _settings = settings ?? new Settings();
        _config = config;
        _style = _settings.Style ?? new StyleSettings();
        _extras = config is null ? new AccountExtras() : config.ExtrasAs<AccountExtras>(AccountCard.SettingsKey);

        Width = Math.Max(320, Math.Min(500, _style.FixedWidth > 0 ? _style.FixedWidth : 390));
        Background = Brushes.Transparent;
        Margin = new Thickness(Math.Max(12, _style.Padding), 8, Math.Max(12, _style.Padding), 8);

        var panel = new StackPanel();
        _title = Text("明日方舟 · 账号状态", Math.Max(10, _style.FontSize - 3), 0.6, false);
        panel.Children.Add(_title);
        _name = Text("等待森空岛数据…", _style.FontSize + 5, 1.0, true);
        _name.Margin = new Thickness(0, 4, 0, 2);
        panel.Children.Add(_name);
        _meta = Text("", Math.Max(10, _style.FontSize - 2), 0.65, false);
        panel.Children.Add(_meta);
        _stats = Text("", _style.FontSize, 0.85, false);
        _stats.Margin = new Thickness(0, 6, 0, 2);
        panel.Children.Add(_stats);
        _status = Text("", Math.Max(9, _style.FontSize - 4), 0.45, false);
        _status.Margin = new Thickness(0, 4, 0, 0);
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        panel.Children.Add(_status);

        Children.Add(panel);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += (s, e) => _ = RefreshAsync();
        Loaded += (s, e) => { _ = RefreshAsync(); _timer.Start(); };
        Unloaded += (s, e) => { _timer.Stop(); _disposed = true; _cts.Cancel(); };
    }

    TextBlock Text(string text, double size, double alpha, bool bold) => new()
    {
        Text = text,
        FontFamily = HudFont.Resolve(_style.FontFamily),
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = CardPalette.Brush(CardPalette.TryColor(_style.Foreground, Colors.White), alpha),
        TextWrapping = TextWrapping.Wrap
    };

    async Task RefreshAsync()
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(_extras.Cred) || string.IsNullOrWhiteSpace(_extras.Salt))
            {
                SetStatus("未配置森空岛凭据", "明日方舟 · 未绑定");
                return;
            }

            using var client = new SklandClient(_extras.Cred, _extras.Salt);
            string uid = _extras.Uid;
            if (string.IsNullOrWhiteSpace(uid))
            {
                (string found, string nick, string findError) = await client.FindArknightsUidAsync(_cts.Token);
                if (found is null) { SetStatus(findError ?? "未找到角色", "明日方舟 · 未绑定"); return; }
                uid = found;
                _extras.Uid = uid;
                IfNotDisposed(() =>
                {
                    _config.SetExtras(AccountCard.SettingsKey, _extras);
                    _name.Text = nick ?? "博士";
                });
            }

            (string json, string error) = await client.GetArknightsInfoAsync(uid, _cts.Token);
            if (json is null) { SetStatus(error ?? "读取失败", "明日方舟 · 读取失败"); return; }
            AccountSnapshot snapshot = AccountSnapshot.Parse(json, out string parseError);
            if (snapshot is null) { SetStatus(parseError ?? "响应解析失败", "明日方舟 · 读取失败"); return; }
            IfNotDisposed(() => Apply(snapshot));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "明日方舟 · 读取失败");
            Log.Warn($"账号卡读取失败: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    void Apply(AccountSnapshot snapshot)
    {
        _title.Text = "明日方舟 · 账号状态";
        _name.Text = snapshot.NickName;
        _meta.Text = $"Lv.{snapshot.Level} · UID {snapshot.Uid}";
        _stats.Text = $"理智 {snapshot.ApCurrent} · 干员 {snapshot.CharCount} · 六星 {snapshot.SixStarCount}";
        _status.Text = string.IsNullOrWhiteSpace(snapshot.ApRecoverAt)
            ? "森空岛只读 · 每 5 分钟刷新" : $"理智回满 {snapshot.ApRecoverAt} · 每 5 分钟刷新";
    }

    void SetStatus(string text, string title) => IfNotDisposed(() =>
    {
        _status.Text = text ?? "";
        if (!string.IsNullOrWhiteSpace(title)) _title.Text = title;
    });

    void IfNotDisposed(Action action)
    {
        if (_disposed) return;
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }
}
