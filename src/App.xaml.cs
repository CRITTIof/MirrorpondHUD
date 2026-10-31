using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;

namespace MirrorpondHUD
{
    public partial class App : Application
    {
        Mutex _mutex;
        OverlayController _controller;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Log.Error("未处理异常", args.ExceptionObject as Exception);

            DispatcherUnhandledException += (s, args) =>
            {
                Log.Error("UI 线程异常", args.Exception);
                args.Handled = true;
            };

            Log.Info("App 启动  参数=[" + string.Join(" ", e.Args) + "]  版本=" +
                     typeof(App).Assembly.GetName().Version + "  日志=" + Log.FilePath);
            Log.Info("进程完整性级别: " + ProcessIntegrity.Name + "（" +
                     ProcessIntegrity.Level.ToString("X") + "）");

            if (ProcessIntegrity.IsRestricted)
            {
                Log.Warn("进程运行在低完整性级别，Windows 会拒绝托盘图标、全局热键和键盘钩子");
                MessageBox.Show(
                    "程序当前运行在低完整性级别（Low Integrity）环境中。\n\n" +
                    "这种情况下 Windows 会禁止：托盘图标、全局热键、全局键盘钩子。\n\n" +
                    "请把整个 bin\\Release 目录复制到一个你有完全控制权的普通文件夹\n" +
                    "（例如「文档」）后再运行。\n\n" +
                    "日志：" + Log.FilePath,
                    "镜潭一览 · 权限受限", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            bool createdNew;
            _mutex = new Mutex(true, "Local\\MirrorpondHUD.SingleInstance", out createdNew);
            if (!createdNew)
            {
                // 已经有一个实例在跑：把它唤到前台（打开设置），而不是无声退出
                Log.Info("已有实例在运行，通知它打开设置窗口");
                try
                {
                    using (EventWaitHandle activation = EventWaitHandle.OpenExisting("Local\\MirrorpondHUD.Activate"))
                        activation.Set();
                }
                catch (Exception ex)
                {
                    Log.Warn("唤醒已有实例失败: " + ex.Message);
                }
                Shutdown(0);
                return;
            }

            List<string> args = e.Args.ToList();

            string shotCards = GetOption(args, "--screenshot-cards");
            if (shotCards != null)
            {
                Screenshots.CaptureCards(shotCards);
                Shutdown(0);
                return;
            }

            string shot = GetOption(args, "--screenshot");
            if (shot != null)
            {
                Screenshots.CaptureOverlay(shot);
                Shutdown(0);
                return;
            }

            string shotSettings = GetOption(args, "--screenshot-settings");
            if (shotSettings != null)
            {
                Screenshots.CaptureSettings(shotSettings);
                Shutdown(0);
                return;
            }

            string shotWidgets = GetOption(args, "--screenshot-widgets");
            if (shotWidgets != null)
            {
                Screenshots.CaptureWidgets(shotWidgets);
                Shutdown(0);
                return;
            }

            if (args.Contains("--selftest"))
            {
                int failures = SelfTest.Run();
                Shutdown(failures);
                return;
            }

            Settings settings = ConfigStore.Load();

            string smoke = GetOption(args, "--smoke");
            if (smoke != null)
            {
                args.Remove(smoke);
                args.Remove("--smoke");
                bool visible = args.Remove("--smoke-visible");
                if (visible) args.Add("--visible");
                else if (!args.Contains("--minimized")) args.Add("--minimized");

                int seconds;
                if (!int.TryParse(smoke, out seconds)) seconds = 6;

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(Math.Max(1, seconds))
                };
                timer.Tick += (s, a) =>
                {
                    timer.Stop();
                    Log.Info("冒烟测试结束，退出");
                    Shutdown(0);
                };
                timer.Start();
            }

            try
            {
                _controller = new OverlayController(settings, args);
                _controller.Start();
            }
            catch (Exception ex)
            {
                Log.Error("启动失败", ex);
                MessageBox.Show(
                    "启动失败：" + ex.Message + "\n\n详细信息已写入日志：\n" + Log.FilePath,
                    "镜潭一览", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        static string GetOption(List<string> args, string name)
        {
            int index = args.IndexOf(name);
            if (index >= 0 && index + 1 < args.Count) return args[index + 1];

            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(name.Length + 1);
            }
            return null;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _controller?.Dispose(); } catch { }
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
