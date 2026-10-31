using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;
using MirrorpondHUD.Media;

namespace MirrorpondHUD.Ui
{
    /// <summary>离屏渲染成 PNG，用于自动化验收（--screenshot）。</summary>
    public static class Screenshots
    {
        /// <summary>MIRRORPOND_SETTINGS_SCROLL=1：设置页每一页再拍一张「滚到底」的图。</summary>
        static bool SettingsScrollDemo
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("MIRRORPOND_SETTINGS_SCROLL");
                return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        static ScrollViewer FindScroller(DependencyObject root)
        {
            if (root == null) return null;
            var viewer = root as ScrollViewer;
            if (viewer != null) return viewer;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                ScrollViewer found = FindScroller(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>MIRRORPOND_CLI_DEMO=1：给 CLI 面板塞两条样张记录（截图验收用）。</summary>
        static bool CliDemo
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("MIRRORPOND_CLI_DEMO");
                return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

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

        public static KeyHistory DemoKeys(int visible, int threshold, int maxChars)
        {
            var keys = new KeyHistory();
            keys.Configure(visible, threshold, maxChars);
            string[] seq = { "Ctrl", "C", "Ctrl", "V", "A", "A", "A", "A", "A", "A", "Enter" };
            foreach (string k in seq) keys.Push(k);
            return keys;
        }

        public static void CaptureCards(string path)
        {
            try
            {
                Settings settings = ConfigStore.Load();
                // 文档截图关掉投影、拉开间距：卡片边缘干净，不会互相压到。
                settings.Overlay.Shadow = false;
                MetricsSnapshot snap = DemoSnapshot();
                KeyHistory keys = DemoKeys(settings.Data.KeyHistoryCount, settings.Data.RepeatThreshold,
                                           settings.Data.MaxKeyChars);

                var stack = new StackPanel { Orientation = Orientation.Vertical };
                int count = 0;

                foreach (CardSettings card in settings.Cards)
                {
                    if (card == null) continue;

                    ICardModule module = CardRegistry.Get(card.Kind);
                    var surface = new OverlaySurface();

                    // 每张卡片外面再包一层来留间距：OverlaySurface.Apply 会重写自己的 Margin（投影留白）。
                    var cardHost = new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, Math.Max(16, card.Spacing)),
                        Child = surface
                    };

                    var rich = module as IRichCardModule;
                    if (rich != null)
                    {
                        // 图片 / 动图：离屏渲染第一帧；视频没有真实窗口，取 ffmpeg 解出的第一帧当封面。
                        var view = rich.CreateContent(new CardRenderContext(settings, snap, keys, card));
                        if (view == null) continue;

                        var mediaView = view as MediaView;
                        string mediaPath = MediaCard.MediaOf(card).Path;
                        if (mediaView != null && MediaView.IsVideoPath(mediaPath))
                            mediaView.ShowPoster(mediaPath);

                        // CLI 面板（AI 对话 / 命令行）平时空着，只有提问 / 跑命令才有记录；
                        // MIRRORPOND_CLI_DEMO=1 时塞两条样张，才能检查内部小面板和它们自己的滑块。
                        var cli = view as CliPanelView;
                        if (cli != null && CliDemo)
                        {
                            if (string.Equals(card.Kind, "command", StringComparison.OrdinalIgnoreCase))
                            {
                                cli.AddSampleRecord("[CMD] [C:\\Users\\CRITT] > dir /b", true);
                                cli.AddSampleRecord(
                                    "Desktop\nDocuments\nDownloads\nMirrorpondHUD\nMusic\nPictures\nVideos\n"
                                    + "desktop.ini\n\n这条记录故意写得长一点，用来检查内部小面板超过高度上限之后自己的滑块。",
                                    false);
                            }
                            else
                            {
                                cli.AddSampleRecord("> 什么是宾语从句？举一个例子，并说明它和定语从句的区别。", true);
                                cli.AddSampleRecord(
                                    "宾语从句在句子里充当宾语，用一句话来做宾语。它属于名词性从句的一种。\n\n"
                                    + "例如：I know that he is right. 里的 that he is right 就是宾语从句。\n"
                                    + "定语从句修饰名词，宾语从句充当宾语 —— 前者回答「哪一个」，后者回答「什么」。\n"
                                    + "这条记录故意写得长一点，用来检查内部小面板超过高度上限之后自己的滑块。", false);
                            }
                        }

                        surface.Apply(settings, "");
                        surface.SetContent(view);
                    }
                    else
                    {
                        surface.Apply(settings, module.Render(
                            new CardRenderContext(settings, snap, keys, card)));
                    }

                    stack.Children.Add(cardHost);
                    count++;
                }

                if (count == 0)
                {
                    Log.Warn("没有可截图的卡片");
                    return;
                }

                stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Size size = stack.DesiredSize;

                var host = new Grid
                {
                    Width = Math.Max(420, size.Width + 160),
                    Height = Math.Max(220, size.Height + 160),
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0x14, 0x1B, 0x2B),
                        Color.FromRgb(0x2A, 0x1D, 0x2A),
                        45)
                };

                stack.HorizontalAlignment = HorizontalAlignment.Left;
                stack.VerticalAlignment = VerticalAlignment.Center;
                stack.Margin = new Thickness(80, 0, 80, 0);
                host.Children.Add(stack);

                host.Measure(new Size(host.Width, host.Height));
                host.Arrange(new Rect(0, 0, host.Width, host.Height));
                host.UpdateLayout();

                const double scale = 2.0;
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(host.Width * scale),
                    (int)Math.Ceiling(host.Height * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                rtb.Render(host);

                SavePng(rtb, path);
                Log.Info("已生成卡片组截图: " + path + "（" + count + " 张）");
            }
            catch (Exception ex)
            {
                Log.Error("生成卡片组截图失败", ex);
            }
        }

        public static void CaptureOverlay(string path)
        {
            try
            {
                Settings settings = ConfigStore.Load();
                settings.Overlay.Shadow = false;
                MetricsSnapshot snap = DemoSnapshot();
                KeyHistory keys = DemoKeys(settings.Data.KeyHistoryCount, settings.Data.RepeatThreshold,
                                           settings.Data.MaxKeyChars);

                CardSettings first = null;
                foreach (CardSettings card in settings.Cards)
                {
                    if (card == null || string.Equals(card.Kind, "command", StringComparison.OrdinalIgnoreCase)) continue;
                    first = card;
                    break;
                }

                string text;
                if (first != null)
                {
                    text = CardRegistry.Get(first.Kind).Render(
                        new CardRenderContext(settings, snap, keys, first));
                }
                else
                {
                    text = TemplateEngine.Render(Settings.DefaultMetricsContent,
                                                 Variables.Build(snap, settings, keys));
                }

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

        public static void CaptureWidgets(string path)
        {
            try
            {
                Settings settings = ConfigStore.Load();
                settings.Overlay.Shadow = false;
                StyleSettings style = settings.Style;
                Color foreground = OverlaySurface.ParseColor(style.Foreground, Color.FromRgb(0xE6, 0xED, 0xF3));

                var stack = new StackPanel { Width = 520 };

                var header = new CardHeader("文件中转站") { Badge = "12 个文件", Margin = new Thickness(0, 0, 0, 10) };
                header.Actions.Add("打开目录", null, CardButtonTone.Normal, "在资源管理器中打开接收目录");
                header.Actions.Add("清空", null, CardButtonTone.Danger, "清空列表（不删除文件）");
                header.ApplyStyle(style, settings.Overlay.CornerRadius);
                stack.Children.Add(header);

                var status = new CardStatus { Margin = new Thickness(0, 0, 0, 10) };
                status.ApplyStyle(style);
                status.SetText("正在复制 2/5 · a.zip", CardStatusKind.Busy);
                status.SetProgress(0.42);
                stack.Children.Add(status);

                var list = new CardList { Height = 160, Margin = new Thickness(0, 0, 0, 10) };
                list.ApplyStyle(style, settings.Cli);
                list.SetEmptyText("把文件拖进来");
                list.Items.Add(WidgetRow("a.zip", "12.4 MB", foreground));
                list.Items.Add(WidgetRow("screenshot.png", "2.1 MB", foreground));
                list.Items.Add(WidgetRow("notes.txt", "8 KB", foreground));
                list.Items.Add(WidgetRow("video.mp4", "248 MB", foreground));
                list.SelectedIndex = 1;
                stack.Children.Add(list);

                var input = new CardInput(false) { Margin = new Thickness(0, 0, 0, 10) };
                input.Placeholder = "输入备注，回车提交…";
                input.ApplyStyle(style, settings.Overlay.CornerRadius);
                stack.Children.Add(input);

                var dropHost = new Grid { Height = 100, Margin = new Thickness(0, 0, 0, 10) };
                var dropList = new CardList();
                dropList.ApplyStyle(style, settings.Cli);
                dropList.SetEmptyText("把文件拖进来（空状态）");
                dropHost.Children.Add(dropList);

                var dropOverlay = new CardDropOverlay { Margin = new Thickness(2) };
                dropOverlay.ApplyStyle(style, settings.Overlay.CornerRadius);
                dropOverlay.Show(3);
                dropHost.Children.Add(dropOverlay);
                stack.Children.Add(dropHost);

                var error = new CardStatus { Margin = new Thickness(0, 0, 0, 6) };
                error.ApplyStyle(style);
                error.SetText("复制失败：磁盘空间不足", CardStatusKind.Error);
                error.SetProgress(null);
                stack.Children.Add(error);

                var card = new Border
                {
                    Width = 560,
                    Background = CardPalette.Brush(
                        OverlaySurface.ParseColor(style.Background, Color.FromRgb(0x0D, 0x11, 0x17)),
                        style.BackgroundOpacity),
                    CornerRadius = new CornerRadius(settings.Overlay.CornerRadius),
                    Padding = new Thickness(20),
                    Child = stack
                };

                var host = new Grid
                {
                    Width = 640,
                    Height = 560,
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0x14, 0x1B, 0x2B),
                        Color.FromRgb(0x2A, 0x1D, 0x2A),
                        45)
                };
                card.HorizontalAlignment = HorizontalAlignment.Center;
                card.VerticalAlignment = VerticalAlignment.Center;
                host.Children.Add(card);

                host.Measure(new Size(host.Width, host.Height));
                host.Arrange(new Rect(0, 0, host.Width, host.Height));
                host.UpdateLayout();

                const double scale = 2.0;
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(host.Width * scale),
                    (int)Math.Ceiling(host.Height * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                rtb.Render(host);

                SavePng(rtb, path);
                Log.Info("已生成共享控件截图: " + path);
            }
            catch (Exception ex)
            {
                Log.Error("生成共享控件截图失败", ex);
            }
        }

        static FrameworkElement WidgetRow(string name, string meta, Color foreground)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new TextBlock
            {
                Text = name,
                Foreground = CardPalette.Brush(foreground, 0.92),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var right = new TextBlock
            {
                Text = meta,
                Foreground = CardPalette.Brush(foreground, 0.45),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(right, 1);

            grid.Children.Add(left);
            grid.Children.Add(right);
            return grid;
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
                // 外观页很长，一张图看全：窗口拉到 1560 高再渲染（窗口在屏外，不影响桌面）。
                window.Height = Math.Max(window.Height, 1560);
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

                    // MIRRORPOND_SETTINGS_SCROLL=1：把这一页滚到底再拍一张，长页面（外观页）的下半部分才看得到。
                    if (SettingsScrollDemo)
                    {
                        ScrollViewer scroller = FindScroller(root);
                        if (scroller != null)
                        {
                            scroller.ScrollToEnd();
                            root.UpdateLayout();
                            Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);

                            var bottom = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                            bottom.Render(root);
                            SavePng(bottom, Path.Combine(dir, stem + "-tab" + tab + "-bottom.png"));
                            Log.Info("已生成设置界面截图（滚到底）: " + stem + "-tab" + tab + "-bottom.png");
                        }
                    }
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
