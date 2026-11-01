using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// CardOverlayControls 的最小运行时自检：实例化每个浮层控件、走一遍打开 / 关闭 / 渲染，
    /// 确认 Popup / ContextMenu 这类独立 HWND 不会留在进程里，定时器和关闭事件也能正常收尾。
    ///
    /// 用法（宿主 SelfTest 里调用）：
    /// <code>
    /// string error = CardOverlaySelfTest.Run();
    /// if (error != null) ... // error 是中文失败说明
    /// </code>
    ///
    /// 必须在 STA / UI 线程可用环境里跑：本线程是 STA 时直接执行；不是 STA 时自己起一个
    /// 独立 STA 线程执行并等它结束。返回 null = 通过，否则返回中文失败说明。
    /// </summary>
    public static class CardOverlaySelfTest
    {
        /// <summary>跑一遍全部浮层控件；null = 通过，否则是中文失败说明。</summary>
        public static string Run()
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return RunCore();

            // 宿主自检可能在 MTA 线程上跑：浮层控件必须有 STA，另起一条线程执行。
            string result = "CardOverlaySelfTest 未执行";
            var thread = new Thread(() => { result = RunCore(); })
            {
                IsBackground = true,
                Name = "CardOverlaySelfTest"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!thread.Join(TimeSpan.FromSeconds(60)))
                return "CardOverlaySelfTest 超时：60 秒内没有执行完（Popup/ContextMenu 可能没有关闭）";

            return result;
        }

        static string RunCore()
        {
            var failures = new List<string>();
            var host = new Grid();
            var style = new StyleSettings { FontFamily = "Microsoft YaHei UI", FontSize = 15 };

            CardModal modal = null;
            CardDialog dialog = null;
            CardDialog cancelDialog = null;
            CardPopover popover = null;
            CardTooltip tooltip = null;
            CardContextMenu menu = null;
            var toasts = new List<CardToast>();

            try
            {
                // ---------- CardModal ----------
                modal = new CardModal
                {
                    Body = new TextBlock { Text = "模态内容" }
                };
                bool modalClosed = false;
                modal.Closed += (s, e) => modalClosed = true;
                modal.ApplyStyle(style, 12);

                Check(failures, modal.CloseOnBackdropClick, "CardModal.CloseOnBackdropClick 默认应为 true");
                modal.Show(host);
                Check(failures, modal.IsOpen, "CardModal.Show 后 IsOpen 应为 true");
                Check(failures, host.Children.Contains(modal), "CardModal.Show 后应加入宿主面板");
                Render(host, "CardModal");

                modal.Close();
                Check(failures, !modal.IsOpen, "CardModal.Close 后 IsOpen 应为 false");
                Check(failures, !host.Children.Contains(modal), "CardModal.Close 后应从宿主移除");
                Check(failures, modalClosed, "CardModal.Close 应触发 Closed");

                // ---------- CardDialog ----------
                dialog = new CardDialog
                {
                    Title = "确认操作",
                    Message = "继续会覆盖当前内容。",
                    Buttons = CardDialogButtons.OkCancel
                };
                bool dialogClosed = false;
                dialog.Closed += (s, e) => dialogClosed = true;
                dialog.ApplyStyle(style, 12);

                Task<bool> dialogTask = dialog.ShowAsync(host);
                Check(failures, dialog.IsOpen, "CardDialog.ShowAsync 应打开对话框");
                Check(failures, dialogTask != null, "CardDialog.ShowAsync 应返回 Task<bool>");
                Check(failures, ReferenceEquals(dialog.ShowAsync(host), dialogTask), "CardDialog 重复 Show 不应叠加");
                Render(host, "CardDialog");

                dialog.CloseWith(true);
                Check(failures, dialog.DialogResult, "CardDialog.CloseWith(true) 的 DialogResult 应为 true");
                Check(failures, !dialog.IsOpen && !host.Children.Contains(dialog), "CardDialog 关闭后应从宿主移除");
                Check(failures, dialogClosed, "CardDialog 关闭应触发 Closed");
                Check(failures, dialogTask.IsCompleted && dialogTask.Result, "CardDialog 确认路径的 Task 应以 true 完成");

                cancelDialog = new CardDialog
                {
                    Title = "取消",
                    Message = "Esc / 点遮罩等同取消。",
                    Buttons = CardDialogButtons.YesNo
                };
                cancelDialog.ApplyStyle(style, 12);
                Task<bool> cancelTask = cancelDialog.ShowAsync(host);
                cancelDialog.Close();
                Check(failures, cancelTask.IsCompleted && !cancelTask.Result,
                      "CardDialog 取消路径（Close / Esc / 遮罩）的 Task 应以 false 完成");

                // ---------- CardPopover ----------
                popover = new CardPopover
                {
                    Content = new TextBlock { Text = "浮层内容" }
                };
                popover.ApplyStyle(style, 12);
                Check(failures, popover.Content != null, "CardPopover.Content 应可设置");

                var target = new Border { Width = 60, Height = 24, Background = Brushes.Gray };
                host.Children.Add(target);

                // 目标故意不 Loaded：ShowAt 不允许抛异常。
                popover.ShowAt(target);
                Check(failures, popover.IsOpen, "CardPopover.ShowAt 后应打开（目标未 Loaded 也不应抛异常）");
                Render((Visual)popover.Child, "CardPopover");

                popover.Close();
                Check(failures, !popover.IsOpen, "CardPopover.Close 后应关闭");

                // ---------- CardTooltip ----------
                tooltip = new CardTooltip { Text = "提示文本", DelayMs = 0 };
                tooltip.ApplyStyle(style, 12);
                tooltip.Attach(target);
                Render((Visual)tooltip.Child, "CardTooltip");
                tooltip.Detach();
                Check(failures, !tooltip.IsOpen, "CardTooltip.Detach 后应关闭");

                // ---------- CardContextMenu ----------
                menu = new CardContextMenu();
                bool menuFired = false;
                MenuItem openItem = menu.Add("打开", () => menuFired = true, "Ctrl+O");
                menu.AddSeparator();
                menu.AddHeader("分组标题");
                menu.Add("删除", null, null, true);
                menu.ApplyStyle(style, 12);

                Check(failures, menu.Items.Count == 4, "CardContextMenu 条目数应为 4（2 条 + 分隔线 + 标题）");
                openItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(failures, menuFired, "CardContextMenu 条目的点击回调应被触发");
                Render(menu, "CardContextMenu");
                menu.IsOpen = false;

                // ---------- CardToast ----------
                CardToast toast = CardToast.Show(host, "已保存", CardToastTone.Success, 5000);
                toasts.Add(toast);
                Check(failures, toast != null, "CardToast.Show 应返回实例");
                Check(failures, toast != null && host.Children.Contains(toast), "CardToast 应自动加入宿主面板");

                bool toastClosed = false;
                if (toast != null) toast.Closed += (s, e) => toastClosed = true;
                Render(host, "CardToast");

                toast.Dismiss();
                Check(failures, !host.Children.Contains(toast), "CardToast.Dismiss 后应从宿主移除");
                Check(failures, toastClosed, "CardToast.Dismiss 应触发 Closed");

                CardToast first = CardToast.Show(host, "一", CardToastTone.Info, 5000);
                CardToast second = CardToast.Show(host, "二", CardToastTone.Danger, 5000, "撤销", () => { });
                toasts.Add(first);
                toasts.Add(second);
                Check(failures, host.Children.Contains(first) && host.Children.Contains(second),
                      "多个 CardToast 应能各自独立存在");

                first.Dismiss();
                second.Dismiss();
                Check(failures, !host.Children.Contains(first) && !host.Children.Contains(second),
                      "多个 CardToast 应能各自移除");

                // 正常命令 / 主题缺省参数不应影响控件创建。
                popover.ApplyStyle(null, 0);
                toast.ApplyStyle(null, 0);
            }
            catch (Exception ex)
            {
                failures.Add("CardOverlaySelfTest 异常：" + ex.GetType().Name + "：" + ex.Message);
            }
            finally
            {
                // 所有独立 HWND / 定时器 / 动画都必须在这里关掉，测试进程里不能留浮层。
                Try(() => { if (modal != null) modal.Close(); });
                Try(() => { if (dialog != null) dialog.Close(); });
                Try(() => { if (cancelDialog != null) cancelDialog.Close(); });
                Try(() => { if (popover != null) popover.Close(); });
                Try(() => { if (tooltip != null) tooltip.Dispose(); });
                Try(() => { if (menu != null) menu.IsOpen = false; });
                foreach (CardToast toast in toasts)
                {
                    CardToast current = toast;
                    Try(() => current.Dismiss());
                }
            }

            return failures.Count == 0 ? null : string.Join("；", failures);
        }

        static void Check(List<string> failures, bool ok, string message)
        {
            if (!ok) failures.Add(message);
        }

        static void Try(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { }
        }

        /// <summary>Measure / Arrange + RenderTargetBitmap（320×180）；只验证不崩，不比较像素。</summary>
        static void Render(Visual visual, string name)
        {
            var element = visual as FrameworkElement;
            if (element != null)
            {
                element.ApplyTemplate();
                element.Measure(new Size(320, 180));
                element.Arrange(new Rect(0, 0, 320, 180));
            }

            var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            if (bitmap.PixelWidth < 320 || bitmap.PixelHeight < 180)
                throw new InvalidOperationException(name + " 的渲染结果小于 320×180");
        }
    }
}
