using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 一块卡片窗口的毛玻璃运行时：底板选择 / 创建 / 退役、截屏排除、兼容取图定时器与运行期降级。
    ///
    /// 三种实现：
    /// 1) 系统亚克力（默认）：卡片下面一个独立的底板窗口，挂未文档化的 accent 亚克力，
    ///    DWM / GPU 负责模糊，进程侧零逐帧工作，模糊半径由系统决定；
    /// 2) 桌面亚克力：截屏 + 自己模糊，把结果**画进卡片自己的内容层**（见 GlassFeed）。
    ///    代价：为了让截屏不拍到自己，卡片必须排除在截屏之外 —— 这个模式下截图 / 录像里没有卡片；
    /// 3) 兼容：把卡片后面的画面拼出来（枚举窗口 + PrintWindow + 壁纸），缩小模糊后当底色，
    ///    可调半径与帧率，任何系统都能用。
    /// 1 / 2 不可用时自动退回 3。
    ///
    /// 从 <see cref="OverlayWindow"/> 迁入（2026-11 审阅第三批第 12 项）：窗口只保留
    /// 「什么时候通知」与自身句柄 / 可见性，谁 active、谁不可用、是否正在抓图只有这里一份真相。
    /// 注意：Acrylic / Glass 双套模糊字段的合并、GlassRuntimeState 单一数据源是
    /// AGENTS.md §10 明确推迟的重构，这里只做职责搬迁，不碰它们的语义。
    /// </summary>
    internal sealed class GlassRuntime : IDisposable
    {
        readonly OverlayWindow _window;

        /// <summary>两块底板（系统亚克力 / 桌面亚克力）。谁 active 由 <see cref="GlassPolicy"/> 决定。</summary>
        readonly SystemAcrylicBackdrop _systemBackdrop = new SystemAcrylicBackdrop();
        readonly DesktopAcrylicBackdrop _desktopBackdrop = new DesktopAcrylicBackdrop();

        DispatcherTimer _glassTimer;
        bool _glassBusy;

        public GlassRuntime(OverlayWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        /// <summary>
        /// 有一块底板正在卡片下面画模糊。三种实现是互斥的：谁在干活谁就是 true，
        /// 切换实现时另一块必须收掉（否则会留下没人管的玻璃板）。
        /// </summary>
        public bool BackdropActive { get { return _systemBackdrop.IsActive || _desktopBackdrop.IsActive; } }

        /// <summary>桌面亚克力当前处于冷却 / 不可用（拖动补帧据此决定是否走兼容取图）。</summary>
        public bool DesktopAcrylicUnavailable { get { return _desktopBackdrop.IsUnavailable; } }

        /// <summary>设置变了就再给两块底板一次机会（底板对象保留，不重建）。</summary>
        public void ResetAvailability()
        {
            _systemBackdrop.ResetAvailability();
            _desktopBackdrop.ResetAvailability();
        }

        /// <summary>
        /// 决定这一帧由谁提供模糊：纯决策在 <see cref="GlassPolicy"/>，这里只负责
        /// 「收掉不该用的底板 → 让该用的那块接管」。两块互斥，切走时必须先 Retire，
        /// 否则会留下没人管的玻璃板（系统亚克力会留灰板，桌面亚克力会留「卡片不进截图」）。
        /// </summary>
        public void UpdateBackdrop()
        {
            if (!_window.GlassHandleReady) return;

            Settings settings = _window.GlassSettings;

            // 取样参数（取图方式 / 模糊强度 / 刷新帧率 / 是否留投影边）随时可能被改。
            // 放在 acrylic 判断之前，免得「先关毛玻璃改设置、再打开」时沿用旧参数。
            GlassFeed.Configure(settings.Style.GlassSource, settings.Style.GlassBlur,
                                settings.Style.GlassFps, settings.Overlay.Shadow);

            GlassBackdropKind kind = GlassPolicy.Resolve(
                settings.Style.Acrylic, settings.Style.GlassMode,
                !_systemBackdrop.IsUnavailable, !_desktopBackdrop.IsUnavailable);

            if (kind != GlassBackdropKind.SystemAcrylic) _systemBackdrop.Retire(_window);
            if (kind != GlassBackdropKind.DesktopAcrylic) _desktopBackdrop.Retire(_window);

            string reason;
            bool applied;

            switch (kind)
            {
                case GlassBackdropKind.SystemAcrylic:
                    applied = _systemBackdrop.Apply(_window, out reason);
                    break;

                case GlassBackdropKind.DesktopAcrylic:
                    applied = _desktopBackdrop.Apply(_window, out reason);
                    break;

                default:
                    return;   // 没有底板接管：兼容取样的截屏路径照常工作
            }

            if (!applied)
            {
                HideBackdrops();
                if (!string.IsNullOrEmpty(reason))
                    Log.Warn("毛玻璃底板不可用（" + reason + "），改用截屏取样");
            }
        }

        /// <summary>重新决定底板 + 起停兼容取图定时器。窗口位置 / 尺寸 / 可见性 / 设置变化后调用。</summary>
        public void UpdateGlassCapture()
        {
            if (!_window.GlassHandleReady) return;

            UpdateBackdrop();

            // 系统亚克力由底板窗口在卡片下面画模糊，卡片内部这层要关掉，也不用跑定时器；
            // 桌面亚克力的模糊图**正是画在卡片内部**（GlassFeed），这里不能清。
            if (BackdropActive)
            {
                if (_glassTimer != null) _glassTimer.Stop();
                if (_systemBackdrop.IsActive) _window.Surface.ClearGlass();
                return;
            }

            Settings settings = _window.GlassSettings;
            bool enabled = settings.Style.Acrylic;

            if (!enabled)
            {
                if (_glassTimer != null) _glassTimer.Stop();
                _window.Surface.ClearGlass();
                return;
            }

            int fps = Math.Max(2, Math.Min(30, settings.Style.AcrylicFps));
            TimeSpan interval = TimeSpan.FromMilliseconds(1000.0 / fps);

            if (_glassTimer == null)
            {
                _glassTimer = new DispatcherTimer(DispatcherPriority.Background, _window.Dispatcher)
                {
                    Interval = interval
                };
                _glassTimer.Tick += (s, e) => CaptureGlass();
            }
            else
            {
                _glassTimer.Interval = interval;
            }

            if (!_glassTimer.IsEnabled)
            {
                _glassTimer.Start();
                Log.Info("毛玻璃背景已启用（" + fps + " 帧/秒，模糊 " + settings.Style.AcrylicBlur + "px）");
            }

            CaptureGlass();
        }

        /// <summary>
        /// 兼容实现每画出一帧玻璃就记一条 GLAS 时间戳（MIRRORPOND_GLASS_TRACE=1）。
        /// 卡片排除在截屏之外以后没法靠截图验收，只能靠这种时间戳量刷新节奏。
        /// </summary>
        bool TraceGlassFrames
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("MIRRORPOND_GLASS_TRACE");
                return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        public void CaptureGlass()
        {
            if (_window.GlassClosed) return;                // 窗口已关闭：不再抓屏，也不写 UI
            if (BackdropActive) return;                     // 系统底板接管时不抓屏

            Settings settings = _window.GlassSettings;
            if (!settings.Style.Acrylic || !_window.GlassHandleReady || _glassBusy) return;
            if (!_window.IsVisible || _window.WindowState == WindowState.Minimized) return;

            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;

            Native.Rect rect;
            if (!Native.GetWindowRect(hwnd, out rect)) return;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 8 || height < 8) return;

            int left = rect.Left;
            int top = rect.Top;
            double blur = settings.Style.AcrylicBlur;

            // 卡片后面可能压着好几个窗口，甚至只压着桌面：
            // FindLayers 会按 z 序收集（桌面垫底），Grab 再逐点选最上层的那一层。
            List<GlassCapture.Layer> layers = GlassCapture.FindLayers(left, top, width, height);
            if (layers.Count == 0)
            {
                _window.Surface.ClearGlass();
                return;
            }

            _glassBusy = true;

            Task.Run(() =>
            {
                BitmapSource image = null;
                try
                {
                    image = GlassCapture.Grab(layers, left, top, width, height, blur);
                }
                catch (Exception ex)
                {
                    Log.Warn("毛玻璃取样失败: " + ex.Message);
                }

                try
                {
                    _window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _glassBusy = false;
                        // 审阅 §21：Closed 只停了定时器，Start 之前投递的抓屏完成后仍会写 UI。
                        // 窗口关闭后到达的在途结果直接丢弃。
                        if (_window.GlassClosed) return;
                        if (image != null)
                        {
                            _window.Surface.SetGlass(image);
                            if (TraceGlassFrames)
                                Log.Info("GLAS " + DateTime.Now.ToString("HH:mm:ss.fff") + " hwnd=" + hwnd.ToInt64());
                        }
                    }));
                }
                catch
                {
                    _glassBusy = false;
                }
            });
        }

        /// <summary>
        /// 取不到背景（连续几帧取样全黑：截屏被安全策略挡掉，或者屏幕本来就是黑的）。
        /// 标记这块底板不可用、收掉，再按剩下的选择重新决定。
        /// </summary>
        public void OnDesktopAcrylicUnavailable(string reason)
        {
            if (_desktopBackdrop.IsUnavailable) return;

            _desktopBackdrop.MarkUnavailable();
            Log.Warn("桌面亚克力不可用（" + reason + "），毛玻璃退回系统亚克力底板");

            // 顺序：先撤掉截屏排除，再按「系统」那条路重新决定用哪块底板
            _desktopBackdrop.Retire(_window);
            UpdateGlassCapture();
        }

        /// <summary>关掉毛玻璃 / 切回兼容实现：两块底板和截屏排除都要撤掉。</summary>
        void HideBackdrops()
        {
            _systemBackdrop.Retire(_window);
            _desktopBackdrop.Retire(_window);
        }

        /// <summary>
        /// 窗口关闭时收尾：停掉取图定时器（P2-4：否则它会每帧把已关闭窗口的可视树
        /// 钉在 Dispatcher 上），撤掉截屏排除，释放系统亚克力底板。
        /// </summary>
        public void Dispose()
        {
            if (_glassTimer != null)
            {
                try { _glassTimer.Stop(); } catch { }
                _glassTimer = null;
            }

            HideBackdrops();
            _systemBackdrop.Dispose();
        }
    }
}
