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
            Text = "只读展示，不会签到或修改账号。点「扫码登录」用森空岛 App 扫一下即可，不用再找浏览器请求头。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var loginState = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.6 };
        var scan = new Button { Content = "扫码登录", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
        var loginRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        loginRow.Children.Add(scan);
        loginRow.Children.Add(loginState);
        panel.Children.Add(loginRow);

        var cred = new TextBox { Text = extras.Cred ?? "", Margin = new Thickness(0, 0, 0, 6) };
        var salt = new TextBox { Text = extras.Salt ?? "", Margin = new Thickness(0, 0, 0, 6) };
        var uid = new TextBox { Text = extras.Uid ?? "", Width = 160 };
        panel.Children.Add(Label("登录凭据（cred）"));
        panel.Children.Add(cred);
        panel.Children.Add(Label("凭据密钥（salt）"));
        panel.Children.Add(salt);
        panel.Children.Add(new TextBlock
        {
            Text = "扫码登录会自动填好这两项；也可以手动粘贴。cred 与 salt 都是只读凭据，只保存在本机。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.55,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var uidRow = new StackPanel { Orientation = Orientation.Horizontal };
        uidRow.Children.Add(new TextBlock { Text = "角色 UID", Width = 72, VerticalAlignment = VerticalAlignment.Center });
        uidRow.Children.Add(uid);
        var auto = new Button { Content = "自动识别", Padding = new Thickness(9, 2, 9, 2), Margin = new Thickness(8, 0, 0, 0) };
        var test = new Button { Content = "测试连接", Padding = new Thickness(9, 2, 9, 2), Margin = new Thickness(6, 0, 0, 0) };
        var state = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 };
        uidRow.Children.Add(auto);
        uidRow.Children.Add(test);
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

        scan.Click += (s, e) =>
        {
            var dialog = new QrLoginDialog(Window.GetWindow(panel), settings?.Style);
            bool ok = dialog.ShowDialog() == true;
            if (!ok)
            {
                loginState.Text = "已取消";
                return;
            }
            cred.Text = dialog.Cred;
            salt.Text = dialog.Salt;
            loginState.Text = "登录成功，凭据已回填";
        };

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
            if (foundUid is null) state.Text = error ?? "未找到角色";
            else
            {
                uid.Text = foundUid;
                state.Text = string.IsNullOrWhiteSpace(nick) ? foundUid : $"{nick} ({foundUid})";
            }
            auto.IsEnabled = true;
        };

        test.Click += async (s, e) =>
        {
            test.IsEnabled = false;
            state.Text = "连接中…";
            using var client = new SklandClient(cred.Text, salt.Text);
            if (string.IsNullOrWhiteSpace(uid.Text))
            {
                (string found, string nick, string findError) = await client.FindArknightsUidAsync(CancellationToken.None);
                if (found is null) { state.Text = findError ?? "凭据无效"; test.IsEnabled = true; return; }
                uid.Text = found;
                state.Text = string.IsNullOrWhiteSpace(nick) ? "已识别 " + found : "已连接：" + nick;
            }
            else
            {
                (string json, string error) = await client.GetArknightsInfoAsync(uid.Text.Trim(), CancellationToken.None);
                if (json is null) state.Text = error ?? "连接失败";
                else
                {
                    AccountSnapshot snapshot = AccountSnapshot.Parse(json, out string parseError);
                    state.Text = snapshot is null ? (parseError ?? "数据解析失败")
                        : $"已连接：{snapshot.NickName} Lv.{snapshot.Level}";
                }
            }
            test.IsEnabled = true;
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
        bool snapshotOk = error is null && snapshot is not null
                          && snapshot.NickName == "博士" && snapshot.Level == 120
                          && snapshot.CharCount == 3 && snapshot.SixStarCount == 2;

        // 2.47.0：扫码登录的二维码必须稳定生成（固定 v4-L，33×33）。
        bool qrOk;
        try
        {
            bool[,] matrix = QrCode.Encode("hypergryph://scan_login?scanId=" + new string('a', 32));
            qrOk = matrix.GetLength(0) == 33 && matrix.GetLength(1) == 33
                   && matrix[0, 0] && matrix[0, 6] && matrix[6, 0] && matrix[6, 6]
                   && !matrix[1, 1] && matrix[3, 3]
                   && matrix[6, 8] && !matrix[6, 9]
                   && matrix[25, 8]; // 固定深色模块 (8, size-8)
        }
        catch (Exception ex)
        {
            Log.Warn($"二维码自检异常: {ex.Message}");
            qrOk = false;
        }

        return snapshotOk && qrOk ? null : "账号快照解析或扫码二维码生成失败";
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
        catch (OperationCanceledException)
        {
            // 卡片被隐藏 / 重建时取消刷新，属于正常生命周期收尾。
            Log.Debug("账号卡刷新已取消");
        }
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
