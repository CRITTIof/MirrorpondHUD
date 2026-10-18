using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowOverlay.Core;

namespace WindowOverlay.Ui
{
    /// <summary>离屏渲染成 PNG，用于自动化验收（--screenshot）。</summary>
    public static class Screenshots
    {
        public static MetricsSnapshot DemoSnapshot()
        {
            var snap = new MetricsSnapshot
            {
                CpuPercent = 23.4,
                GpuAvailable = true,
                GpuPercent = 47.8,
                MemUsedGb = 12.6,
                MemTotalGb = 31.9,
                MemPercent = 39.5,
                DiskPercent = 12.3,
                DiskReadBps = 42.1 * 1024 * 1024,
                DiskWriteBps = 3.4 * 1024 * 1024,
                NetDownBps = 1.8 * 1024 * 1024,
                NetUpBps = 96 * 1024,
                NetAdapter = "Realtek Gaming GbE Family Controller"
            };

            snap.Drives.Add(new DriveSample { Name = "C:\\", FreeGb = 2725.0, TotalGb = 3815.3 });
            snap.Drives.Add(new DriveSample { Name = "D:\\", FreeGb = 53.3, TotalGb = 931.5 });

            snap.Nvidia = new NvidiaSample
            {
                Available = true,
                Name = "NVIDIA GeForce RTX 4060",
                Utilization = 47.8,
                VramUsedMb = 2345,
                VramTotalMb = 8188,
                TempC = 52,
                PowerW = 68.4
            };

            return snap;
        }

        public static KeyHistory DemoKeys(int visible, int threshold)
        {
            var keys = new KeyHistory();
            keys.Configure(visible, threshold);
            string[] seq = { "Ctrl", "C", "Ctrl", "V", "A", "A", "A", "A", "A", "A", "Enter" };
            foreach (string k in seq) keys.Push(k);
            return keys;
        }

        public static void CaptureOverlay(string path)
        {
            try
            {
                Settings settings = ConfigStore.Load();
                MetricsSnapshot snap = DemoSnapshot();
                KeyHistory keys = DemoKeys(settings.Data.KeyHistoryCount, settings.Data.RepeatThreshold);

                var vars = Variables.Build(snap, settings, keys);
                string text = TemplateEngine.Render(settings.Content, vars);

                var surface = new OverlaySurface();
                surface.Apply(settings, text);

                double width = SurfaceSizeWidth(surface) + 120;
                double height = SurfaceSizeHeight(surface) + 120;

                var host = new Grid
                {
                    Width = width,
                    Height = height,
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0x1B, 0x2A, 0x3D),
                        Color.FromRgb(0x33, 0x22, 0x2E),
                        45)
                };

                surface.HorizontalAlignment = HorizontalAlignment.Center;
                surface.VerticalAlignment = VerticalAlignment.Center;
                host.Children.Add(surface);

                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();

                const double scale = 2.0;
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(width * scale),
                    (int)Math.Ceiling(height * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                rtb.Render(host);

                SavePng(rtb, path);
                Log.Info("已生成叠层截图: " + path);
            }
            catch (Exception ex)
            {
                Log.Error("生成截图失败", ex);
            }
        }

        static double SurfaceSizeWidth(OverlaySurface surface)
        {
            surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Max(60, surface.DesiredSize.Width);
        }

        static double SurfaceSizeHeight(OverlaySurface surface)
        {
            return Math.Max(40, surface.DesiredSize.Height);
        }

        public static void CaptureSettings(string path)
        {
            try
            {
                Settings settings = ConfigStore.Load();
                var window = new SettingsWindow(settings, s => { }, (x, y, m) => { }, () => { });
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.ShowInTaskbar = false;
                window.Left = -20000;
                window.Top = -20000;
                window.Show();
                window.UpdateLayout();

                Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Loaded);

                var root = window.Content as FrameworkElement;
                if (root == null)
                {
                    Log.Warn("设置窗口没有内容");
                    window.CloseForReal();
                    return;
                }

                double width = Math.Max(1, window.ActualWidth);
                double height = Math.Max(1, window.ActualHeight);

                string basePath = Path.GetFullPath(path);
                string dir = Path.GetDirectoryName(basePath);
                string stem = Path.GetFileNameWithoutExtension(basePath);

                for (int tab = 0; tab < window.Tabs.Items.Count; tab++)
                {
                    window.Tabs.SelectedIndex = tab;
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);

                    var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);

                    string target = tab == 0
                        ? basePath
                        : Path.Combine(dir, stem + "-tab" + tab + ".png");
                    SavePng(rtb, target);
                    Log.Info("已生成设置界面截图: " + target);
                }

                window.CloseForReal();
            }
            catch (Exception ex)
            {
                Log.Error("生成设置界面截图失败", ex);
            }
        }

        static void SavePng(BitmapSource source, string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path))
                encoder.Save(stream);
        }
    }
}
