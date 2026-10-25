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
        DispatcherTimer _timer;

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
        }

        public bool IsActive { get { return _handle != IntPtr.Zero; } }

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
            lock (Live)
            {
                Live.Remove(this);
            }

            if (_timer != null) { _timer.Stop(); _timer = null; }

            if (_handle == IntPtr.Zero) return;

            try { SetWindowDisplayAffinity(_handle, WdaNone); } catch { }
            try { DestroyWindow(_handle); } catch { }

            ReleaseSurface();
            _handle = IntPtr.Zero;
            _visible = false;
        }

        /// <summary>卡片窗口也要排除在截屏之外：否则卡片自己会被拍进底板的取样图。</summary>
        public static void ExcludeFromCapture(Window card, bool exclude)
        {
            if (card == null) return;

            IntPtr hwnd;
            try { hwnd = new WindowInteropHelper(card).Handle; }
            catch { return; }
            if (hwnd == IntPtr.Zero) return;

            try { SetWindowDisplayAffinity(hwnd, exclude ? WdaExcludeFromCapture : WdaNone); }
            catch (Exception ex) { Log.Warn("设置截屏排除失败: " + ex.Message); }
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

            if (_timer == null) StartCapture();
            else if (moved) Capture();
        }

        void SetShown(bool show)
        {
            if (_handle == IntPtr.Zero) return;
            if (_visible == show) return;

            ShowWindow(_handle, show ? SwShowNoActivate : SwHide);
            _visible = show;

            if (_timer != null)
            {
                if (show) _timer.Start();
                else _timer.Stop();
            }
        }

        // ==================== 截屏 + 模糊 ====================

        void StartCapture()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += (s, e) => Capture();
            _timer.Start();
            Capture();
        }

        void Capture()
        {
            if (_handle == IntPtr.Zero || !_visible) return;
            if (_width < 4 || _height < 4) return;

            int w = Math.Max(2, _width / Downscale);
            int h = Math.Max(2, _height / Downscale);

            if (w != _bufferWidth || h != _bufferHeight || _memoryDc == IntPtr.Zero)
            {
                if (!Allocate(w, h)) return;
            }

            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                SetStretchBltMode(_memoryDc, Halftone);
                StretchBlt(_memoryDc, 0, 0, w, h, screen, _left, _top, _width, _height, Srccopy);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }

            // 在缩略图上做两遍方框模糊（横 + 竖），等价于原图上约 2× 的半径
            var buffer = new byte[w * h * 4];
            Marshal.Copy(_dibBits, buffer, 0, buffer.Length);

            GlassCapture.BoxBlur(buffer, w, h, BlurRadius, true);
            GlassCapture.BoxBlur(buffer, w, h, BlurRadius, false);

            if (Diagnose) LogSample(buffer, w, h);
            CheckUsable(buffer, w, h);

            Marshal.Copy(buffer, 0, _dibBits, buffer.Length);

            InvalidateRect(_handle, IntPtr.Zero, false);
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
        static extern bool StretchBlt(IntPtr dest, int destX, int destY, int destW, int destH,
                                      IntPtr src, int srcX, int srcY, int srcW, int srcH, uint rop);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string name);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
