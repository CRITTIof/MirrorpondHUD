using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 第三种毛玻璃：DWM 桌面亚克力底板（对应 C:\Coding\acrylic-multiwindow 的做法）。
    ///
    /// 和 <see cref="GlassBackdrop"/> 一样，卡片下面再放一块从不绘制的 HWND，
    /// 但材质换成**文档化的** <c>DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW</c>
    /// （设置界面里的「桌面亚克力」），外加 DWM 圆角、无边框色、跟随系统深色模式。
    ///
    /// 关键差别：DWM 只给**当前活动窗口**画这层材质。卡片窗口是分层窗口（本来就没有 DWM 材质），
    /// 而底板窗口永远不会成为活动窗口，所以这里实际上要靠底板自己**截屏 + 模糊**来顶上——
    /// 这正是 multiwindow 项目 <c>PersistentAcrylic</c> 的同一套做法，代价也一样：
    /// 为了让截屏不把自己拍进去，底板和卡片都必须设 <c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c>，
    /// 于是**开了这个模式之后，截图 / 录屏里看不到卡片**。
    ///
    /// 与兼容实现的区别在于取的是**合成后的屏幕**（动态壁纸、硬件加速窗口、视频都在里面），
    /// 而不是各窗口 <c>PrintWindow</c> 拼出来的内容；模糊用项目自己的滑动窗口方框模糊。
    /// </summary>
    internal sealed class AcrylicBackdrop : IDisposable
    {
        const string ClassName = "MirrorpondHUD.AcrylicBackdrop";

        /// <summary>投影开启时卡片四周预留的透明边（与 OverlaySurface 的 Margin 一致）。</summary>
        public const double ShadowInset = 10.0;

        const int WsPopup = unchecked((int)0x80000000);
        const int WsClipSiblings = 0x04000000;

        const int WsExTransparent = 0x00000020;
        const int WsExToolWindow = 0x00000080;
        const int WsExTopmost = 0x00000008;
        const int WsExNoActivate = 0x08000000;

        const int SwHide = 0;
        const int SwShowNoActivate = 4;

        const uint SwpNoActivate = 0x0010;
        const uint SwpNoOwnerZorder = 0x0200;
        const uint SwpShowWindow = 0x0040;

        const uint WmPaint = 0x000F;
        const uint WmEraseBkgnd = 0x0014;
        const uint WmNcHitTest = 0x0084;
        const uint WmMouseActivate = 0x0021;

        const int HtTransparent = -1;
        const int MaNoActivate = 3;

        // DWM 属性
        const int DwmwaUseImmersiveDarkMode = 20;
        const int DwmwaWindowCornerPreference = 33;
        const int DwmwaBorderColor = 34;
        const int DwmwaSystemBackdropType = 38;

        const int DwmsbtTransientWindow = 3;     // 桌面亚克力（Desktop Acrylic）
        const int DwmwcpRound = 2;
        const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

        const int SmRemoteSession = 0x1000;

        const uint WdaNone = 0;
        const uint WdaExcludeFromCapture = 0x11;

        /// <summary>GetWindow 的 GW_OWNER：找「owner 是这张卡片的窗口」（视频窗）。</summary>
        const int GwOwner = 4;

        const uint Srccopy = 0x00CC0020;
        const int Halftone = 4;

        /// <summary>截屏之后缩到 1/2，模糊半径按缩略图算，单帧成本才压得住。</summary>
        const int Downscale = 2;

        /// <summary>缩略图上的模糊半径（换成原图约 ×2）。</summary>
        const int BlurRadius = 9;

        static bool? _selfCapture;

        /// <summary>
        /// 底板是否自己截屏顶上。默认开；设 MIRRORPOND_GLASS_CAPTURE=0 可以关掉，用来验证
        /// DWM 到底会不会给「非活动窗口」画这层材质。
        /// </summary>
        static bool SelfCapture
        {
            get
            {
                if (!_selfCapture.HasValue)
                {
                    string value = Environment.GetEnvironmentVariable("MIRRORPOND_GLASS_CAPTURE");
                    _selfCapture = !(value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));
                }
                return _selfCapture.Value;
            }
        }

        static bool _classRegistered;
        static readonly WndProcDelegate WndProcKeepAlive = BackdropWndProc;

        static readonly System.Collections.Generic.HashSet<AcrylicBackdrop> Live =
            new System.Collections.Generic.HashSet<AcrylicBackdrop>();

        IntPtr _handle;
        bool _visible;

        /// <summary>唯一的那只截屏定时器（200 ms）：不再每块底板一个。</summary>
        static DispatcherTimer _pump;

        /// <summary>
        /// 整叠卡片区域的屏幕快照（1:1）。每轮只从屏幕上读一次，各块底板再从这张图里
        /// 取自己那一块 —— 屏幕 DC 的 StretchBlt 要等 DWM 合成，一次 10–17 ms。
        /// </summary>
        static IntPtr _sharedDc;
        static IntPtr _sharedDib;
        static IntPtr _sharedBits;
        static IntPtr _sharedPrevious;
        static int _sharedWidth;
        static int _sharedHeight;

        IntPtr _memoryDc;
        IntPtr _dib;
        IntPtr _dibBits;
        IntPtr _dibPrevious;
        int _bufferWidth;
        int _bufferHeight;

        int _left;
        int _top;
        int _width;
        int _height;

        AcrylicBackdrop(IntPtr handle)
        {
            _handle = handle;
            lock (Live) Live.Add(this);
            EnsureWinEventHook();
        }

        public bool IsActive { get { return _handle != IntPtr.Zero; } }

        // ==================== 窗口栈变了就补帧 ====================
        // 底板只靠 200 ms 的定时器刷新，等于「切窗口 / 最小化 / 弹出新窗口」之后
        // 玻璃要等最多一整帧才跟上，观感就是「换窗口时玻璃不跟手」。
        // 这里挂一个全局 WinEvent 钩子（跳过本进程），一有窗口栈变化就立刻补一帧，
        // 40 ms 后再补一帧接住切换动画的最后一帧；定时器继续负责视频 / 动画壁纸。

        const uint WineventOutofcontext = 0x0000;
        const uint WineventSkipownprocess = 0x0002;
        const uint EventSystemForeground = 0x0003;
        const uint EventSystemMovesizeEnd = 0x000B;
        const uint EventSystemSwitchEnd = 0x0015;
        const uint EventSystemMinimizeStart = 0x0016;
        const uint EventSystemMinimizeEnd = 0x0017;
        const uint EventObjectShow = 0x8002;
        const uint EventObjectHide = 0x8003;

        static WinEventDelegate _winEventProc;
        static IntPtr _winEventHookSystem;
        static IntPtr _winEventHookObject;
        static DateTime _lastRefreshRequest;
        static DispatcherTimer _burst;
        static int _burstStep;

        const int BurstIntervalMs = 40;   // 补帧间隔
        const int BurstFrames = 2;        // 立刻 + 40 ms（切换动画期间画面还在变）

        /// <summary>装钩子（幂等）。必须在有消息循环的线程上调用 —— 也就是 UI 线程。</summary>
        static void EnsureWinEventHook()
        {
            if (_winEventProc != null) return;

            try
            {
                _winEventProc = OnWinEvent;
                _winEventHookSystem = SetWinEventHook(
                    EventSystemForeground, EventSystemMinimizeEnd, IntPtr.Zero, _winEventProc,
                    0, 0, WineventOutofcontext | WineventSkipownprocess);
                _winEventHookObject = SetWinEventHook(
                    EventObjectShow, EventObjectHide, IntPtr.Zero, _winEventProc,
                    0, 0, WineventOutofcontext | WineventSkipownprocess);
            }
            catch (Exception ex)
            {
                _winEventProc = null;
                Log.Warn("窗口事件钩子安装失败，玻璃只按定时器刷新: " + ex.Message);
            }
        }

        static void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild,
                               uint thread, uint time)
        {
            switch (evt)
            {
                case EventSystemForeground:
                case EventSystemMovesizeEnd:
                case EventSystemSwitchEnd:
                case EventSystemMinimizeStart:
                case EventSystemMinimizeEnd:
                case EventObjectShow:
                case EventObjectHide:
                    break;

                default:
                    return;
            }

            // out-of-context 钩子回到安装线程，但统一丢回 UI 线程更稳（Capture 要用 UI 线程）。
            Dispatcher dispatcher = Application.Current == null ? null : Application.Current.Dispatcher;
            if (dispatcher == null) return;

            try { dispatcher.BeginInvoke(new Action(ScheduleRefresh), DispatcherPriority.Input); }
            catch { }
        }

        static void ScheduleRefresh()
        {
            DateTime now = DateTime.UtcNow;

            // 切一次窗口会连发好几个事件（FOREGROUND + SHOW + SWITCHEND…），合并掉
            if ((now - _lastRefreshRequest).TotalMilliseconds < 30) return;
            _lastRefreshRequest = now;

            // 一次切换补一串帧（立刻 + 40 / 80 / 120 ms）：只补一帧的话很容易正好落在
            // 「DWM 还没合成完」或「切换动画中间」上，实测仍然要 ~80 ms 才看得到新画面。
            _burstStep = 0;
            CaptureAll();
            StartBurst();
        }

        /// <summary>补帧的尾巴：切换动画期间画面还在变，多补几帧才能接住最后一帧。</summary>
        static void StartBurst()
        {
            if (_burst == null)
            {
                _burst = new DispatcherTimer(DispatcherPriority.Input)
                {
                    Interval = TimeSpan.FromMilliseconds(BurstIntervalMs)
                };
                _burst.Tick += (s, e) => TickBurst();
            }

            _burst.Stop();
            _burst.Start();
        }

        static void TickBurst()
        {
            CaptureAll();
            if (++_burstStep >= BurstFrames) _burst.Stop();
        }

        // ==================== 可用性 ====================

        /// <summary>
        /// DWM 桌面亚克力能不能用：Win11 22H2+ 才认 <c>DWMWA_SYSTEMBACKDROP_TYPE</c>，
        /// 关闭透明效果 / 远程会话也会退化成纯色。
        /// </summary>
        public static bool IsAvailable(out string reason)
        {
            if (SystemParameters.HighContrast)
            {
                reason = "系统处于高对比度模式";
                return false;
            }

            if (IsRemoteSession())
            {
                reason = "远程桌面会话";
                return false;
            }

            if (!IsTransparencyEnabled())
            {
                reason = "系统「透明效果」已关闭";
                return false;
            }

            if (Environment.OSVersion.Version.Build < 22621)
            {
                reason = "系统早于 Windows 11 22H2，没有 DWM 桌面亚克力";
                return false;
            }

            reason = "";
            return true;
        }

        static bool IsRemoteSession()
        {
            try { return GetSystemMetrics(SmRemoteSession) != 0; }
            catch { return false; }
        }

        static bool IsTransparencyEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("EnableTransparency");
                    if (value is int) return (int)value != 0;
                    if (value is long) return (long)value != 0;
                }
            }
            catch
            {
                // 读不到就交给 DWM 的返回值判断
            }
            return true;
        }

        // ==================== 创建 / 销毁 ====================

        public static bool TryCreate(out AcrylicBackdrop backdrop, out string reason)
        {
            backdrop = null;

            if (!IsAvailable(out reason)) return false;

            if (!_classRegistered)
            {
                var wc = new WndClassEx
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(WndClassEx)),
                    lpfnWndProc = WndProcKeepAlive,
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = ClassName
                };

                ushort atom = RegisterClassExW(ref wc);
                if (atom == 0)
                {
                    reason = "注册底板窗口类失败（错误码 " + Marshal.GetLastWin32Error() + "）";
                    return false;
                }
                _classRegistered = true;
            }

            IntPtr handle = CreateWindowExW(
                WsExToolWindow | WsExTopmost | WsExNoActivate | WsExTransparent,
                ClassName, "MirrorpondHUD Acrylic Backdrop",
                WsPopup | WsClipSiblings,
                0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

            if (handle == IntPtr.Zero)
            {
                reason = "创建底板窗口失败（错误码 " + Marshal.GetLastWin32Error() + "）";
                return false;
            }

            var instance = new AcrylicBackdrop(handle);
            if (!instance.ApplyBackdrop())
            {
                instance.Dispose();
                reason = "系统不支持 DWM 桌面亚克力（DWMWA_SYSTEMBACKDROP_TYPE 无效果）";
                return false;
            }

            reason = "";
            backdrop = instance;
            return true;
        }

        bool ApplyBackdrop()
        {
            if (_handle == IntPtr.Zero) return false;

            int backdrop = DwmsbtTransientWindow;
            if (DwmSetWindowAttribute(_handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) != 0)
                return false;

            int corner = DwmwcpRound;
            DwmSetWindowAttribute(_handle, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            int dark = IsDarkTheme() ? 1 : 0;
            DwmSetWindowAttribute(_handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            int noBorder = DwmwaColorNone;
            DwmSetWindowAttribute(_handle, DwmwaBorderColor, ref noBorder, sizeof(int));

            // 排除自己，否则截屏会把自己（以及上一帧）拍进画面，形成拖影
            if (SelfCapture) SetWindowDisplayAffinity(_handle, WdaExcludeFromCapture);
            return true;
        }

        static bool IsDarkTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value == 0;
                }
            }
            catch { }
            return true;
        }

        public void Dispose()
        {
            bool empty;
            lock (Live)
            {
                Live.Remove(this);
                empty = Live.Count == 0;
            }

            if (empty) ReleaseSharedSurface();

            if (_handle == IntPtr.Zero) return;

            try { SetWindowDisplayAffinity(_handle, WdaNone); } catch { }
            try { DestroyWindow(_handle); } catch { }

            ReleaseSurface();
            _handle = IntPtr.Zero;
            _visible = false;
        }

        /// <summary>
        /// 卡片窗口也要排除在截屏之外：否则卡片自己会被拍进底板的取样图。
        /// 卡片名下的其它顶层窗口（owner 是卡片的视频窗）一起排除 —— 否则截图里会剩下
        /// 一块单独的视频，而卡片框已经不见了。
        /// </summary>
        public static void ExcludeFromCapture(Window card, bool exclude)
        {
            if (card == null) return;

            IntPtr hwnd;
            try { hwnd = new WindowInteropHelper(card).Handle; }
            catch { return; }
            if (hwnd == IntPtr.Zero) return;

            try { SetWindowDisplayAffinity(hwnd, exclude ? WdaExcludeFromCapture : WdaNone); }
            catch (Exception ex) { Log.Warn("设置截屏排除失败: " + ex.Message); }

            ExcludeOwnedWindows(hwnd, exclude);
        }

        /// <summary>把 owner 是这个窗口的顶层窗口一并排除 / 放回（视频窗就是这么挂的）。</summary>
        static void ExcludeOwnedWindows(IntPtr owner, bool exclude)
        {
            var owned = new System.Collections.Generic.List<IntPtr>();

            try
            {
                EnumWindows((hwnd, param) =>
                {
                    if (GetWindow(hwnd, GwOwner) == owner) owned.Add(hwnd);
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Warn("枚举卡片子窗口失败: " + ex.Message);
                return;
            }

            foreach (IntPtr hwnd in owned)
            {
                try { SetWindowDisplayAffinity(hwnd, exclude ? WdaExcludeFromCapture : WdaNone); }
                catch { }
            }
        }

        // ==================== 跟随卡片 ====================

        /// <summary>把底板贴到卡片内容区、插到卡片正下方，并同步可见性。</summary>
        public void Follow(Window card, bool shadow, bool visible)
        {
            if (_handle == IntPtr.Zero) return;

            if (card == null || !visible || !card.IsVisible)
            {
                SetShown(false);
                return;
            }

            IntPtr cardHandle;
            try { cardHandle = new WindowInteropHelper(card).Handle; }
            catch { cardHandle = IntPtr.Zero; }

            if (cardHandle == IntPtr.Zero)
            {
                SetShown(false);
                return;
            }

            double inset = shadow ? ShadowInset : 0.0;
            double width = card.ActualWidth - inset * 2.0;
            double height = card.ActualHeight - inset * 2.0;
            if (width < 4.0 || height < 4.0)
            {
                SetShown(false);
                return;
            }

            Point origin;
            DpiScale dpi;
            try
            {
                origin = card.PointToScreen(new Point(inset, inset));
                dpi = System.Windows.Media.VisualTreeHelper.GetDpi(card);
            }
            catch
            {
                SetShown(false);
                return;
            }

            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            int left = (int)Math.Round(origin.X);
            int top = (int)Math.Round(origin.Y);
            int pixelWidth = (int)Math.Round(width * scaleX);
            int pixelHeight = (int)Math.Round(height * scaleY);
            if (pixelWidth < 4 || pixelHeight < 4)
            {
                SetShown(false);
                return;
            }

            SetWindowPos(_handle, cardHandle, left, top, pixelWidth, pixelHeight,
                         SwpNoActivate | SwpNoOwnerZorder | SwpShowWindow);
            _visible = true;

            bool moved = left != _left || top != _top || pixelWidth != _width || pixelHeight != _height;
            _left = left;
            _top = top;
            _width = pixelWidth;
            _height = pixelHeight;

            if (!SelfCapture)
            {
                if (moved) InvalidateRect(_handle, IntPtr.Zero, false);
                return;
            }

            EnsurePump();
            if (moved) CaptureAll();
        }

        void SetShown(bool show)
        {
            if (_handle == IntPtr.Zero) return;
            if (_visible == show) return;

            ShowWindow(_handle, show ? SwShowNoActivate : SwHide);
            _visible = show;
        }

        // ==================== 截屏 + 模糊 ====================

        static void EnsurePump()
        {
            if (_pump != null) return;

            _pump = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _pump.Tick += (s, e) => CaptureAll();
            _pump.Start();

            CaptureAll();
        }

        /// <summary>
        /// 把整叠卡片区域从屏幕上读一次，再分给每块底板。
        ///
        /// 为什么不是每块底板各读一次屏：从屏幕 DC 做 StretchBlt 要 10–17 ms
        /// （约一个 vsync，得等 DWM 合成完），4 张卡顺序读就是 66 ms —— 这段时间 UI 线程
        /// 被占住，切窗口时玻璃要等下一轮才跟上，也就是「换窗口不跟手」的根源。
        /// 改成「整叠区域读一次 + 内存 DC 之间拉伸」以后，一轮只花一帧。
        /// </summary>
        static void CaptureAll()
        {
            AcrylicBackdrop[] snapshot;
            lock (Live)
            {
                snapshot = new AcrylicBackdrop[Live.Count];
                Live.CopyTo(snapshot);
            }

            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            int count = 0;

            foreach (AcrylicBackdrop item in snapshot)
            {
                int l, t, w, h;
                if (!item.TryGetScreenRect(out l, out t, out w, out h)) continue;

                count++;
                if (l < left) left = l;
                if (t < top) top = t;
                if (l + w > right) right = l + w;
                if (t + h > bottom) bottom = t + h;
            }

            if (count == 0) return;

            int width = right - left;
            int height = bottom - top;
            if (width < 4 || height < 4) return;

            // 共享图按 128 px 取整，免得开场动画每长一行字就重建一次 DIB
            // （实际只用到左上角 width × height 这一块）。
            int surfaceWidth = RoundUp(width);
            int surfaceHeight = RoundUp(height);
            if (!EnsureSharedSurface(surfaceWidth, surfaceHeight)) return;

            IntPtr screen = GetDC(IntPtr.Zero);
            System.Diagnostics.Stopwatch clock = DiagnoseRaw ? System.Diagnostics.Stopwatch.StartNew() : null;
            bool fromDuplication = false;
            try
            {
                // 优先走 DXGI 桌面复制：取的是合成器现成的帧，约 1 ms；GDI 读屏要等一个 vsync（9–17 ms）。
                // 取不到（不支持的 Windows 生成号 / 独占全屏 / 远程桌面 / 设备被抢）就退回 BitBlt。
                DesktopFrameSource source = EnsureFrameSource();
                if (source != null)
                {
                    _frameSourceIdle = DateTime.UtcNow;
                    fromDuplication = source.TryReadInto(_sharedBits, surfaceWidth * 4, left, top, width, height);
                }

                if (!fromDuplication)
                {
                    BitBlt(_sharedDc, 0, 0, width, height, screen, left, top, Srccopy);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }

            if (clock != null)
            {
                clock.Stop();
                TraceBands(clock.Elapsed.TotalMilliseconds, width, height, fromDuplication);
            }

            // 走桌面复制时一轮只要 1 ms 左右（GDI 读屏要等一个 vsync，18 ms），可以把刷新从 200 ms 提到 100 ms
            // —— 玻璃落后屏幕从 46 ms 掉到 16 ms（CPU 4.8% → 7.6~9.8%，见 docs/glass-implementations.md §4.3）。
            // GDI 路径保持 200 ms，别把 UI 线程堵得更频繁。
            if (fromDuplication && _pump != null && _pump.Interval.TotalMilliseconds > 100)
            {
                _pump.Interval = TimeSpan.FromMilliseconds(100);
            }

            foreach (AcrylicBackdrop item in snapshot) item.CaptureFrom(_sharedDc, left, top);
        }

        static int RoundUp(int value)
        {
            return ((value + 127) / 128) * 128;
        }

        // ==================== 取图后端 ====================
        // 默认优先 DXGI 桌面复制（见 DesktopFrameSource）；不可用就一直是 GDI 读屏。
        // 桌面复制会让 GPU 保持唤醒，所以闲置超过 5 秒就释放，下次取图再建。

        static DesktopFrameSource _frameSource;
        static bool _frameSourceTried;

        /// <summary>上一次真正取图的时间：超过 5 秒没人用就放掉桌面复制（别让 GPU 一直醒着）。</summary>
        static DateTime _frameSourceIdle = DateTime.MinValue;

        static DesktopFrameSource EnsureFrameSource()
        {
            // 卡片增删会让底板短暂全部销毁再建，别因此反复创建 D3D 设备 / 桌面复制；
            // 真正长时间不用（默认 5 秒）才释放。
            if (_frameSource != null && (DateTime.UtcNow - _frameSourceIdle).TotalSeconds > 5)
            {
                ReleaseFrameSource();
            }

            if (_frameSource != null || _frameSourceTried) return _frameSource;
            _frameSourceTried = true;

            DesktopFrameSource source;
            string reason;
            if (DesktopFrameSource.TryCreate(out source, out reason))
            {
                _frameSource = source;
                _frameSourceIdle = DateTime.UtcNow;
                Log.Info("玻璃取图改用 DXGI 桌面复制（不再用 GDI 读屏）");
            }
            else
            {
                Log.Info("玻璃取图用 GDI 读屏：" + reason);
            }

            return _frameSource;
        }

        static void ReleaseFrameSource()
        {
            if (_frameSource == null) return;
            try { _frameSource.Dispose(); } catch { }
            _frameSource = null;
            _frameSourceTried = false;
            _frameSourceIdle = DateTime.MinValue;
        }

        // ==================== 诊断：原始取样图的逐带亮度 ====================
        // MIRRORPOND_RAW_TRACE=1 时，每轮把「刚读下来的屏幕图」按水平带统计亮度写进日志，
        // 用来判断「玻璃从上到下变深」到底是取样就带进来了，还是绘制/合成侧的。

        static bool? _rawTrace;

        static bool DiagnoseRaw
        {
            get
            {
                if (!_rawTrace.HasValue)
                {
                    string value = Environment.GetEnvironmentVariable("MIRRORPOND_RAW_TRACE");
                    _rawTrace = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }
                return _rawTrace.Value;
            }
        }

        static void TraceBands(double readMs, int width, int height, bool fromDuplication)
        {
            if (_sharedBits == IntPtr.Zero) return;

            const int Bands = 8;
            int bandHeight = height / Bands;
            if (bandHeight < 1) return;

            var bytes = new byte[width * height * 4];
            Marshal.Copy(_sharedBits, bytes, 0, bytes.Length);

            var trace = new System.Text.StringBuilder();
            trace.Append("RAWB ").Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                 .Append(' ').Append(fromDuplication ? "src=dup" : "src=gdi")
                 .Append(" read=").Append(readMs.ToString("F1"));

            for (int b = 0; b < Bands; b++)
            {
                long total = 0;
                int count = 0;
                for (int y = b * bandHeight; y < (b + 1) * bandHeight; y++)
                {
                    int row = y * width * 4;
                    for (int x = 0; x < width; x += 4)
                    {
                        int p = row + x * 4;
                        total += bytes[p] + 2 * bytes[p + 1] + bytes[p + 2];
                        count++;
                    }
                }
                trace.Append(' ').Append(count == 0 ? "0" : (total / (4 * count)).ToString());
            }

            Log.Info(trace.ToString());
        }

        bool TryGetScreenRect(out int left, out int top, out int width, out int height)
        {
            left = _left;
            top = _top;
            width = _width;
            height = _height;
            return _handle != IntPtr.Zero && _visible && width >= 4 && height >= 4;
        }

        /// <summary>从共享的屏幕图里取自己这一块，缩小 + 模糊，然后请求重画。</summary>
        void CaptureFrom(IntPtr sourceDc, int originLeft, int originTop)
        {
            if (_handle == IntPtr.Zero || !_visible) return;
            if (_width < 4 || _height < 4) return;

            int w = Math.Max(2, _width / Downscale);
            int h = Math.Max(2, _height / Downscale);

            if (w != _bufferWidth || h != _bufferHeight || _memoryDc == IntPtr.Zero)
            {
                if (!Allocate(w, h)) return;
            }

            SetStretchBltMode(_memoryDc, Halftone);
            StretchBlt(_memoryDc, 0, 0, w, h, sourceDc, _left - originLeft, _top - originTop,
                       _width, _height, Srccopy);

            FinishFrame(w, h);
        }

        /// <summary>缩略图 → 两遍方框模糊 → 写回 DIB → 立刻贴到窗口上。</summary>
        void FinishFrame(int w, int h)
        {
            var buffer = new byte[w * h * 4];
            Marshal.Copy(_dibBits, buffer, 0, buffer.Length);

            GlassCapture.BoxBlur(buffer, w, h, BlurRadius, true);
            GlassCapture.BoxBlur(buffer, w, h, BlurRadius, false);

            if (Diagnose) LogSample(buffer, w, h);
            CheckUsable(buffer, w, h);

            // 新的一帧和上一帧几乎一样就不重画。静置时背景没变，重画只会让 DWM
            // 每 200 ms 重算一次「排除窗口背后那块干净区域」，肉眼就是玻璃闪一下；
            // 顺便也省掉一次 2× 半分辨率放大到窗口的 StretchBlt。
            if (SameAsLastFrame(buffer, w, h)) return;

            Marshal.Copy(buffer, 0, _dibBits, buffer.Length);

            // 同步画上去，而不是 InvalidateRect 等消息循环：底板图省下一次「无效 →
            // 合成前还没重画」的中间状态，也就少了一次可能被看成闪烁的窗口。
            Paint();
            TraceFrame(buffer, w, h);
        }

        // ==================== 逐帧追踪（MIRRORPOND_GLASS_TRACE=1） ====================
        // 每画一帧就往 %TEMP%\mirrorpond-trace.log 写一行「时间 + 尺寸 + 平均亮度」，
        // hud-test/compare.py 靠它算「屏幕上换了窗口 → 玻璃上的画面多久跟着变」。

        static bool? _trace;
        static System.IO.StreamWriter _traceWriter;
        static readonly object TraceLock = new object();

        static bool Trace
        {
            get
            {
                if (!_trace.HasValue)
                {
                    string value = Environment.GetEnvironmentVariable("MIRRORPOND_GLASS_TRACE");
                    _trace = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }
                return _trace.Value;
            }
        }

        static void TraceFrame(byte[] buffer, int w, int h)
        {
            if (!Trace) return;

            long sum = 0;
            int count = 0;
            for (int i = 0; i + 2 < buffer.Length; i += 16)
            {
                sum += (buffer[i] + 2 * buffer[i + 1] + buffer[i + 2]) / 4;
                count++;
            }

            lock (TraceLock)
            {
                try
                {
                    if (_traceWriter == null)
                    {
                        string path = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(), "mirrorpond-trace.log");
                        _traceWriter = new System.IO.StreamWriter(path, false) { AutoFlush = true };
                    }
                    _traceWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " CAP " + w + "x" + h
                                           + " luma=" + (count == 0 ? 0 : sum / count));
                }
                catch (Exception ex)
                {
                    Log.Warn("玻璃帧追踪写入失败: " + ex.Message);
                }
            }
        }

        int _lastWidth;
        int _lastHeight;
        byte[] _lastFrame;

        /// <summary>模糊图上差 2 以内看不出来 —— 低于这个就当作「没变」，不重画。</summary>
        const int SkipTolerance = 2;

        bool SameAsLastFrame(byte[] buffer, int w, int h)
        {
            if (_lastFrame == null || _lastWidth != w || _lastHeight != h)
            {
                KeepFrame(buffer, w, h);
                return false;
            }

            // 隔 4 个像素看一次；和「上一次真正画出去的帧」比，
            // 慢漂移会一点点累积到超过阈值，不会因为阈值永远跳过。
            for (int i = 0; i + 3 < buffer.Length; i += 16)
            {
                int db = buffer[i] - _lastFrame[i];
                if (db < -SkipTolerance || db > SkipTolerance) { KeepFrame(buffer, w, h); return false; }
                int dg = buffer[i + 1] - _lastFrame[i + 1];
                if (dg < -SkipTolerance || dg > SkipTolerance) { KeepFrame(buffer, w, h); return false; }
                int dr = buffer[i + 2] - _lastFrame[i + 2];
                if (dr < -SkipTolerance || dr > SkipTolerance) { KeepFrame(buffer, w, h); return false; }
            }

            return true;
        }

        void KeepFrame(byte[] buffer, int w, int h)
        {
            if (_lastFrame == null || _lastFrame.Length != buffer.Length) _lastFrame = new byte[buffer.Length];
            Buffer.BlockCopy(buffer, 0, _lastFrame, 0, buffer.Length);
            _lastWidth = w;
            _lastHeight = h;
        }

        static bool EnsureSharedSurface(int width, int height)
        {
            if (_sharedDc != IntPtr.Zero && _sharedWidth == width && _sharedHeight == height) return true;
            ReleaseSharedSurface();

            var bmi = new BitmapInfo
            {
                Size = Marshal.SizeOf(typeof(BitmapInfo)),
                Width = width,
                Height = -height,     // 负数 = 自上而下
                Planes = 1,
                BitCount = 32,
                Compression = 0       // BI_RGB
            };

            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                _sharedDc = CreateCompatibleDC(screen);
                IntPtr bits;
                _sharedDib = CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0);
                _sharedBits = bits;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }

            if (_sharedDc == IntPtr.Zero || _sharedDib == IntPtr.Zero)
            {
                ReleaseSharedSurface();
                return false;
            }

            _sharedPrevious = SelectObject(_sharedDc, _sharedDib);
            if (_sharedPrevious == IntPtr.Zero || _sharedPrevious == new IntPtr(-1))
            {
                ReleaseSharedSurface();
                return false;
            }

            _sharedWidth = width;
            _sharedHeight = height;
            return true;
        }

        static void ReleaseSharedSurface()
        {

            if (_sharedPrevious != IntPtr.Zero && _sharedDc != IntPtr.Zero)
            {
                SelectObject(_sharedDc, _sharedPrevious);
                _sharedPrevious = IntPtr.Zero;
            }

            if (_sharedDib != IntPtr.Zero) { DeleteObject(_sharedDib); _sharedDib = IntPtr.Zero; }
            if (_sharedDc != IntPtr.Zero) { DeleteDC(_sharedDc); _sharedDc = IntPtr.Zero; }
            _sharedBits = IntPtr.Zero;
            _sharedWidth = 0;
            _sharedHeight = 0;
        }

        /// <summary>底板取不到内容时通知宿主（<see cref="Unavailable"/>）。</summary>
        public event Action<string> Unavailable;

        int _blankFrames;

        /// <summary>
        /// 连续几帧取样全黑，说明这个模式在当前环境里读不到背景：
        /// 可能是屏幕本身就是纯黑、截屏被安全策略拦截、或 DC/DIB 状态异常
        /// （与 <c>WDA_EXCLUDEFROMCAPTURE</c> 无关——被排除的窗口在屏幕 DC 里
        /// 显示的是它身后的内容，而不是黑色）。退回是正确选择。
        /// </summary>
        void CheckUsable(byte[] buffer, int w, int h)
        {
            if (Unavailable == null) return;

            long sum = 0;
            for (int i = 0; i + 2 < buffer.Length; i += 4)
                sum += (buffer[i] + 2 * buffer[i + 1] + buffer[i + 2]) / 4;

            long mean = w * h == 0 ? 0 : sum / (w * h);

            if (mean > 2)
            {
                _blankFrames = 0;
                return;
            }

            if (++_blankFrames < 3) return;

            _blankFrames = 0;
            var handler = Unavailable;
            if (handler == null) return;

            try { handler("底板连续 3 帧取样全黑：当前环境读不到屏幕内容（截屏被拦截或屏幕本来就是黑的）"); }
            catch (Exception ex) { Log.Warn("底板失败回调出错: " + ex.Message); }
        }

        // ==================== 诊断 ====================

        static bool? _diagnose;

        /// <summary>设了环境变量 MIRRORPOND_GLASS_DEBUG=1 时，每 5 秒把取样统计写进日志（排查黑图用）。</summary>
        static bool Diagnose
        {
            get
            {
                if (!_diagnose.HasValue)
                {
                    string value = Environment.GetEnvironmentVariable("MIRRORPOND_GLASS_DEBUG");
                    _diagnose = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }
                return _diagnose.Value;
            }
        }

        DateTime _lastReport = DateTime.MinValue;

        void LogSample(byte[] buffer, int w, int h)
        {
            if ((DateTime.UtcNow - _lastReport).TotalSeconds < 5) return;
            _lastReport = DateTime.UtcNow;

            long sum = 0;
            int max = 0;
            int count = w * h;
            for (int i = 0; i + 2 < buffer.Length; i += 4)
            {
                int luma = (buffer[i] + 2 * buffer[i + 1] + buffer[i + 2]) / 4;
                sum += luma;
                if (luma > max) max = luma;
            }

            Log.Info("DWM 底板取样 " + w + "x" + h + "：平均亮度 " + (count == 0 ? 0 : sum / count)
                     + "，最亮 " + max);

            try { DumpBmp(buffer, w, h); }
            catch (Exception ex) { Log.Warn("导出底板取样失败: " + ex.Message); }
        }

        /// <summary>把取样图导出成 BMP（32 位、自上而下），方便肉眼确认糊成什么样。</summary>
        static void DumpBmp(byte[] pixels, int w, int h)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mirrorpond-glass-debug.bmp");
            int imageSize = w * h * 4;

            using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Create,
                                                       System.IO.FileAccess.Write))
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write((ushort)0x4D42);       // BM
                writer.Write(54 + imageSize);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write(54);

                writer.Write(40);                   // BITMAPINFOHEADER
                writer.Write(w);
                writer.Write(-h);                   // 负数 = 自上而下
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0);                    // BI_RGB
                writer.Write(imageSize);
                writer.Write(2835);
                writer.Write(2835);
                writer.Write(0);
                writer.Write(0);

                writer.Write(pixels, 0, imageSize);
            }
        }

        bool Allocate(int w, int h)
        {
            ReleaseSurface();

            var bmi = new BitmapInfo
            {
                Size = Marshal.SizeOf(typeof(BitmapInfo)),
                Width = w,
                Height = -h,          // 负数 = 自上而下
                Planes = 1,
                BitCount = 32,
                Compression = 0       // BI_RGB
            };

            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                _memoryDc = CreateCompatibleDC(screen);
                _dib = CreateDIBSection(screen, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }

            if (_memoryDc == IntPtr.Zero || _dib == IntPtr.Zero || _dibBits == IntPtr.Zero)
            {
                ReleaseSurface();
                return false;
            }

            // 必须把 DIB 选进内存 DC：否则下面的 StretchBlt 画在 DC 默认的 1×1 单色位图上，
            // _dibBits 永远是全零 —— 取样全黑，CheckUsable 会误判为「排除截屏只能读到黑色」。
            _dibPrevious = SelectObject(_memoryDc, _dib);
            if (_dibPrevious == IntPtr.Zero || _dibPrevious == new IntPtr(-1))
            {
                ReleaseSurface();
                return false;
            }

            _bufferWidth = w;
            _bufferHeight = h;
            return true;
        }

        void ReleaseSurface()
        {
            if (_dibPrevious != IntPtr.Zero && _memoryDc != IntPtr.Zero)
            {
                SelectObject(_memoryDc, _dibPrevious);
                _dibPrevious = IntPtr.Zero;
            }

            if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
            if (_memoryDc != IntPtr.Zero) { DeleteDC(_memoryDc); _memoryDc = IntPtr.Zero; }
            _dibBits = IntPtr.Zero;
            _bufferWidth = 0;
            _bufferHeight = 0;
        }

        /// <summary>把缩略图按窗口大小画出来（StretchBlt + HALFTONE 自带平滑放大）。</summary>
        void Paint()
        {
            if (_memoryDc == IntPtr.Zero) return;

            IntPtr dc = GetDC(_handle);
            if (dc == IntPtr.Zero) return;

            try
            {
                SetStretchBltMode(dc, Halftone);
                StretchBlt(dc, 0, 0, _width, _height, _memoryDc, 0, 0,
                           _bufferWidth, _bufferHeight, Srccopy);
            }
            finally
            {
                ReleaseDC(_handle, dc);
            }
        }

        // ==================== 窗口过程 ====================

        static IntPtr BackdropWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WmEraseBkgnd:
                    return new IntPtr(1);

                case WmPaint:
                {
                    AcrylicBackdrop target = FindByHandle(hwnd);
                    if (target != null) target.Paint();
                    ValidateRect(hwnd, IntPtr.Zero);
                    return IntPtr.Zero;
                }

                case WmNcHitTest:
                    return new IntPtr(HtTransparent);

                case WmMouseActivate:
                    return new IntPtr(MaNoActivate);
            }

            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        static AcrylicBackdrop FindByHandle(IntPtr hwnd)
        {
            lock (Live)
            {
                foreach (AcrylicBackdrop item in Live)
                {
                    if (item._handle == hwnd) return item;
                }
            }
            return null;
        }

        // ==================== interop ====================

        delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WndClassEx
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfo
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern ushort RegisterClassExW(ref WndClassEx wndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(
            int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);

        [DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
                                        int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        static extern int SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr param);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hwnd, int command);

        delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild,
                                       uint thread, uint time);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
                                             WinEventDelegate callback, uint processId, uint threadId,
                                             uint flags);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo bmi, uint usage,
                                              out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern int SetStretchBltMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr dest, int destX, int destY, int destW, int destH,
                                  IntPtr src, int srcX, int srcY, uint rop);

        [DllImport("gdi32.dll")]
        static extern bool StretchBlt(IntPtr dest, int destX, int destY, int destW, int destH,
                                      IntPtr src, int srcX, int srcY, int srcW, int srcH, uint rop);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string name);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
