using System;
using System.Windows;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui;

/// <summary>托盘控制器需要宿主提供的状态与动作。</summary>
internal interface ITrayHost
{
    Settings Settings { get; }
    bool OverlayVisible { get; }
    string CommandHotkey { get; }
    void ToggleVisibility();
    void ShowSettings();
    void ToggleLock();
    void ToggleAutoStart();
    void ToggleCommandLine();
    void Exit();
}

/// <summary>
/// 托盘图标：创建、菜单事件、可见性 / 命令热键同步、位置日志、启动气泡，
/// 以及托盘被安全策略拦掉时“打开设置窗口”的兜底入口。
///
/// 从 <see cref="OverlayController"/> 抽出来，控制器只负责提供状态与动作。
/// </summary>
internal sealed class TrayController : IDisposable
{
    readonly ITrayHost _host;
    TrayIcon _tray;
    DispatcherTimer _balloonTimer;
    bool _started;
    bool _unavailableNotified;

    public TrayController(ITrayHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public bool Visible => _tray is not null && _tray.Visible;

    public void Start()
    {
        _tray = new TrayIcon();
        _tray.ToggleVisibility += _host.ToggleVisibility;
        _tray.OpenSettings += _host.ShowSettings;
        _tray.ToggleLock += _host.ToggleLock;
        _tray.ToggleAutoStart += _host.ToggleAutoStart;
        _tray.CommandLine += _host.ToggleCommandLine;
        _tray.Exit += _host.Exit;

        Update();

        if (_tray.Visible)
        {
            if (_tray.TryGetRect(out var left, out var top, out var right, out var bottom))
                Log.Info($"托盘图标：已显示，位置 {left},{top} - {right},{bottom}");
            else
                Log.Warn("托盘图标：已添加但未取得屏幕位置（Win11 默认收在“隐藏图标”里）");
        }
        else if (_host.Settings.Behavior.TrayEnabled && _tray.AddFailed)
        {
            // 只兜「设置要求显示但加不进托盘」（安全策略 / Shell 拒绝）这一种情况；
            // 用户自己在设置里关掉托盘属于正常状态，绝不能强弹设置窗（P0-1）。
            Log.Warn("托盘不可用（设置要求显示但图标添加失败），已自动打开设置窗口作为兜底入口");
            _unavailableNotified = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(_host.ShowSettings), DispatcherPriority.Background);
        }
        else
        {
            Log.Info("托盘图标：已按设置关闭");
        }

        _started = true;
    }

    public void Update()
    {
        if (_tray is null) return;

        Settings settings = _host.Settings;
        _tray.Visible = settings.Behavior.TrayEnabled;
        _tray.SetCommandHotkey(_host.CommandHotkey);
        _tray.Update(_host.OverlayVisible, settings.Overlay.Locked, settings.Behavior.AutoStart);

        // 运行期重新勾选托盘失败：不能只写 WARN，用户会以为图标已经打开。
        if (!settings.Behavior.TrayEnabled)
        {
            _unavailableNotified = false;
        }
        else if (_started && _tray.AddFailed && !_unavailableNotified)
        {
            _unavailableNotified = true;
            string reason = "Shell_NotifyIcon 添加失败，可能被安全策略 / 资源管理器拒绝";
            Log.Warn($"运行期重新启用托盘失败: {reason}");
            MessageBox.Show(
                "托盘图标添加失败，程序仍在运行，但托盘入口没有出现。\n\n" +
                reason + "\n\n可以按 " + _host.Settings.Hotkeys.OpenSettings + " 打开设置，或重新登录 Windows 后重试。",
                "镜潭一览 · 托盘不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (!_tray.AddFailed)
        {
            _unavailableNotified = false;
        }
    }

    public void ShowStartupBalloon()
    {
        if (_tray is null || !_tray.Visible) return;

        if (_balloonTimer is not null) _balloonTimer.Stop();
        _balloonTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _balloonTimer.Tick += (s, e) =>
        {
            _balloonTimer.Stop();
            _tray.ShowBalloon("镜潭一览已启动",
                $"左键单击托盘图标，或按 {_host.Settings.Hotkeys.OpenSettings} 打开设置");
        };
        _balloonTimer.Start();
    }

    public void ShowBalloon(string title, string text)
    {
        if (_tray is not null) _tray.ShowBalloon(title, text);
    }

    public void Dispose()
    {
        try { if (_balloonTimer is not null) _balloonTimer.Stop(); } catch { }
        try { _tray?.Dispose(); } catch { }
    }
}
