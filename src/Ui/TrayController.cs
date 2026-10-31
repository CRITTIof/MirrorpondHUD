using System;
using System.Windows;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
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

        public TrayController(ITrayHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public bool Visible { get { return _tray != null && _tray.Visible; } }

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

            int left, top, right, bottom;
            if (!_tray.Visible)
            {
                Log.Info("托盘图标：未启用");
            }
            else if (_tray.TryGetRect(out left, out top, out right, out bottom))
            {
                Log.Info("托盘图标：已显示，位置 " + left + "," + top + " - " + right + "," + bottom);
            }
            else
            {
                Log.Warn("托盘图标：已添加但未取得屏幕位置（Win11 默认收在“隐藏图标”里）");
            }

            // 托盘被安全策略拦掉时不要让用户“无处可点”，直接打开设置窗口。
            if (!_tray.Visible)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(_host.ShowSettings), DispatcherPriority.Background);
                Log.Warn("托盘不可用，已自动打开设置窗口作为兜底入口");
            }
        }

        public void Update()
        {
            if (_tray == null) return;

            Settings settings = _host.Settings;
            _tray.Visible = settings.Behavior.TrayEnabled;
            _tray.SetCommandHotkey(_host.CommandHotkey);
            _tray.Update(_host.OverlayVisible, settings.Overlay.Locked, settings.Behavior.AutoStart);
        }

        public void ShowStartupBalloon()
        {
            if (_tray == null || !_tray.Visible) return;

            if (_balloonTimer != null) _balloonTimer.Stop();
            _balloonTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _balloonTimer.Tick += (s, e) =>
            {
                _balloonTimer.Stop();
                _tray.ShowBalloon("镜潭一览已启动",
                    "左键单击托盘图标，或按 " + _host.Settings.Hotkeys.OpenSettings + " 打开设置");
            };
            _balloonTimer.Start();
        }

        public void ShowBalloon(string title, string text)
        {
            if (_tray != null) _tray.ShowBalloon(title, text);
        }

        public void Dispose()
        {
            try { if (_balloonTimer != null) _balloonTimer.Stop(); } catch { }
            try { _tray?.Dispose(); } catch { }
        }
    }
}
