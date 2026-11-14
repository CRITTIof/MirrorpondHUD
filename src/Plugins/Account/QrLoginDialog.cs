using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 森空岛扫码登录对话框（2.47.0）：显示官方登录二维码并轮询结果，
/// 成功后把 cred / token(=salt) 回传给账号卡设置，用户不用再翻浏览器请求头。
/// </summary>
internal sealed class QrLoginDialog : Window
{
    readonly CancellationTokenSource _cts = new();
    readonly QrCodeView _qr;
    readonly TextBlock _status;

    public string Cred { get; private set; } = "";
    public string Salt { get; private set; } = "";

    public QrLoginDialog(Window owner, StyleSettings style)
    {
        StyleSettings s = style ?? new StyleSettings();
        ThemeTokens tokens = ThemeResolver.Resolve(s);
        Color foreground = CardPalette.TryColor(s.Foreground, Colors.White);

        Title = "森空岛扫码登录";
        Width = 380;
        Height = 540;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        if (owner is not null) Owner = owner;
        Background = CardPalette.Brush(CardPalette.TryColor(s.Background, Color.FromRgb(0x0D, 0x11, 0x17)), 1.0);

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = "用森空岛 App 扫码",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = CardPalette.Brush(foreground, 1.0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "扫描后会自动回填凭据，不需要再找请求头。二维码约 2 分钟内有效。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.6,
            Foreground = CardPalette.Brush(tokens.Muted),
            Margin = new Thickness(0, 6, 0, 14)
        });

        // 二维码必须白底黑块才扫得出来：这是功能色，不跟主题走。
        _qr = new QrCodeView { Width = 220, Height = 220, HorizontalAlignment = HorizontalAlignment.Center };
        var qrHost = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(4)
        };
        qrHost.Child = _qr;
        panel.Children.Add(qrHost);

        _status = new TextBlock
        {
            Text = "正在生成二维码…",
            TextWrapping = TextWrapping.Wrap,
            Foreground = CardPalette.Brush(foreground, 0.8),
            Margin = new Thickness(0, 16, 0, 10),
            TextAlignment = TextAlignment.Center
        };
        panel.Children.Add(_status);

        var cancel = new Button { Content = "取消", Padding = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (s, e) => Close();
        panel.Children.Add(cancel);

        Content = panel;
        Closed += (s, e) => _cts.Cancel();
        Loaded += async (s, e) => await RunAsync();
    }

    async Task RunAsync()
    {
        try
        {
            SklandQrSession session = await SklandLogin.StartAsync(_cts.Token);
            if (_cts.IsCancellationRequested) return;

            _qr.SetMatrix(QrCode.Encode(session.ScanUrl));
            _status.Text = "请用森空岛 App 扫描二维码";

            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(1500, _cts.Token);
                SklandScanResult result = await SklandLogin.PollAsync(session.ScanId, _cts.Token);
                if (result.State == SklandScanState.Pending)
                {
                    if (!string.IsNullOrWhiteSpace(result.Message)) _status.Text = result.Message + "…";
                    continue;
                }
                if (result.State == SklandScanState.Failed)
                {
                    _status.Text = "登录失败：" + result.Message;
                    return;
                }

                _status.Text = "已扫码，正在换取凭据…";
                (string cred, string salt) = await SklandLogin.CompleteAsync(result, _cts.Token);
                Cred = cred;
                Salt = salt;
                DialogResult = true;
                Close();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // 用户关窗 / 取消：正常退出，不报错。
        }
        catch (Exception ex)
        {
            _status.Text = "登录失败：" + ex.Message;
            Log.Warn($"森空岛扫码登录失败: {ex.Message}");
        }
    }
}

/// <summary>二维码绘制：白底 + 黑块，按可用尺寸取整数缩放，保证模块边缘清晰。</summary>
internal sealed class QrCodeView : FrameworkElement
{
    bool[,] _modules;

    public void SetMatrix(bool[,] modules)
    {
        _modules = modules;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_modules is null) return;

        int count = _modules.GetLength(0);
        int total = count + 8; // 四周 4 模块静区
        double size = Math.Min(ActualWidth, ActualHeight);
        double scale = Math.Max(1, Math.Floor(size / total));
        double offset = (size - scale * total) / 2 + scale * 4;

        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        for (int y = 0; y < count; y++)
        {
            for (int x = 0; x < count; x++)
            {
                if (!_modules[y, x]) continue;
                dc.DrawRectangle(Brushes.Black, null,
                    new Rect(offset + x * scale, offset + y * scale, scale, scale));
            }
        }
    }
}
