using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 「桌面亚克力」的取样循环：从屏幕抓卡片下面那块画面 → 缩小模糊 → **画进卡片自己的内容层**。
    ///
    /// 为什么不再用一块独立的底板窗口：2.4.2 及以前是在卡片下面放一个 HWND，自己 GDI 贴模糊图。
    /// 那块窗口和卡片是**两个**被 DWM 分别合成的窗口，形状对不齐就会露馅 ——
    /// 卡片圆角 10 px、底板的圆角由 DWM 决定，于是卡片圆角外面会多出一圈「更大的透明圆角」；
    /// 两者差 1–2 px 时，卡片顶边还会周期性露出一条底板（下半截是卡片、上半截是底板，
    /// 看起来就是「顶部突然变深」）。改成画进卡片内部以后，圆角由卡片自己的
    /// <c>Border.CornerRadius</c> 裁剪、和卡片背景/文字在同一次合成里，这两类问题在结构上就不可能发生。
    ///
    /// 取图仍然优先 DXGI 桌面复制（见 <see cref="DesktopFrameSource"/>，一轮 0.1–1.5 ms），
    /// 不可用时退回 GDI <c>BitBlt</c>（一轮约 18 ms）。卡片必须保持
    /// <c>WDA_EXCLUDEFROMCAPTURE</c>，否则取样会把自己拍进去形成拖影。
    /// </summary>
    internal static class GlassFeed
    {
        const uint WdaNone = 0;
        const uint WdaExcludeFromCapture = 0x11;

        const uint Srccopy = 0x00CC0020;
        const int Halftone = 4;

        const int GwOwner = 4;

        /// <summary>取样后缩到 1/2，模糊半径按缩略图算，单帧成本才压得住。</summary>
        const int Downscale = 2;

        /// <summary>投影开启时卡片四周预留的透明边（与 OverlaySurface 的 Margin 一致）。</summary>
        public const double ShadowInset = 10.0;

        sealed class Target
        {
            public OverlayWindow Window;
            public OverlaySurface Surface;
            public Action<string> OnUnavailable;

            public IntPtr MemoryDc;
            public IntPtr Dib;
            public IntPtr Bits;
            public IntPtr Previous;
            public int Width;
            public int Height;

            public byte[] LastFrame;
            public int LastWidth;
            public int LastHeight;
        }

        static readonly List<Target> Live = new List<Target>();

        static DispatcherTimer _pump;
        static int _fps = 10;
        static int _blurRadius = 9;      // 半分辨率上的半径
        static int _blurPixels = 18;     // 设置里的值（全分辨率像素）
        static string _source = "Auto";
        static bool _shadow;

        /// <summary>整叠卡片区域的屏幕快照（1:1）。每轮只从屏幕上读一次，各卡片再从这张图里取自己那一块。</summary>
        static IntPtr _sharedDc;
        static IntPtr _sharedDib;
        static IntPtr _sharedBits;
        static IntPtr _sharedPrevious;
        static int _sharedWidth;
        static int _sharedHeight;

        static DesktopFrameSource _frameSource;
        static bool _frameSourceTried;
        static DateTime _frameSourceIdle = DateTime.MinValue;

        static int _blankFrames;
        static int _gdiWarnedFps;
        static int _gdiRounds;
        static bool _pushed;

        // ==================== 挂接 / 参数 ====================

        public static void Attach(OverlayWindow window, Action<string> onUnavailable)
        {
            if (window == null) return;

            lock (Live)
            {
                foreach (Target existing in Live)
                {
                    if (existing.Window != window) continue;
                    existing.Surface = window.Surface;
                    existing.OnUnavailable = onUnavailable;
                    return;                 // 已经在跑，别再触发一次取样
                }

                Live.Add(new Target
                {
                    Window = window,
                    Surface = window.Surface,
                    OnUnavailable = onUnavailable
                });
            }

            EnsurePump();
        }

        public static void Detach(OverlayWindow window)
        {
            if (window == null) return;

            lock (Live)
            {
                for (int i = Live.Count - 1; i >= 0; i--)
                {
                    if (Live[i].Window != window) continue;
                    ReleaseSurface(Live[i]);
                    Live.RemoveAt(i);
                }

                if (Live.Count == 0)
                {
                    if (_pump != null) _pump.Stop();
                    ReleaseSharedSurface();
                }
            }

            try { window.Surface.ClearGlass(); } catch { }
        }

        /// <summary>同步设置：取图方式、模糊强度、刷新帧率、是否留投影边。</summary>
        public static void Configure(string source, int blurPixels, int fps, bool shadow)
        {
            string mode = Settings.NormalizeGlassSource(source);
            int radius = Math.Max(1, (int)Math.Round(Math.Max(0, blurPixels) / (double)Downscale));
            int frameRate = Math.Max(2, Math.Min(30, fps));

            bool sourceChanged = !string.Equals(mode, _source, StringComparison.Ordinal);
            bool lookChanged = radius != _blurRadius || shadow != _shadow;

            _source = mode;
            _blurPixels = Math.Max(0, blurPixels);
            _blurRadius = radius;
            _fps = frameRate;
            _shadow = shadow;

            if (sourceChanged) ReleaseFrameSource();

            if (lookChanged)
            {
                // 模糊半径变了但背景没变时，「和上一帧一样就不更新」会把新半径挡在门外，这里先清掉缓存。
                lock (Live)
                {
                    foreach (Target target in Live) target.LastFrame = null;
                }
            }

            if (_pump != null) _pump.Interval = Interval();
        }

        static TimeSpan Interval()
        {
            return TimeSpan.FromMilliseconds(1000.0 / Math.Max(2, Math.Min(30, _fps)));
        }

        /// <summary>
        /// 桌面亚克力能不能用。
        /// 以前这里还要检查 DWM 桌面亚克力材质（Windows 11 22H2+ / 系统透明效果），
        /// 现在模糊图是画进卡片自己的内容层的，只要求能把卡片排除在截屏之外 ——
        /// 唯一的硬限制是远程桌面（那边经常取不到画面，没必要先跑起来再退）。
        /// </summary>
        public static bool IsAvailable(out string reason)
        {
            if (IsRemoteSession())
            {
                reason = "远程桌面会话";
                return false;
            }

            reason = "";
            return true;
        }

        const int SmRemoteSession = 0x1000;

        static bool IsRemoteSession()
        {
            try { return GetSystemMetrics(SmRemoteSession) != 0; }
            catch { return false; }
        }

        static void EnsurePump()
        {
            if (_pump == null)
            {
                _pump = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval() };
                _pump.Tick += (s, e) => CaptureAll();
            }

            if (!_pump.IsEnabled) _pump.Start();
            CaptureAll();
        }

        // ==================== 排除截屏 ====================

        /// <summary>
        /// 卡片窗口要排除在截屏之外：否则取样会把自己（以及上一帧）拍进去，形成拖影。
        /// 卡片名下的其它顶层窗口（owner 是卡片的视频窗）一起排除。
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

        static void ExcludeOwnedWindows(IntPtr owner, bool exclude)
        {
            var owned = new List<IntPtr>();

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

        // ==================== 一轮取样 ====================

        static void CaptureAll()
        {
            Target[] snapshot;
            lock (Live)
            {
                if (Live.Count == 0) return;
                snapshot = Live.ToArray();
            }

            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            int count = 0;

            foreach (Target target in snapshot)
            {
                int l, t, w, h;
                if (!TryGetScreenRect(target.Window, out l, out t, out w, out h)) continue;

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

            // 共享图按 128 px 取整，免得开场动画每长一行字就重建一次 DIB（实际只用到左上角 width × height）。
            if (!EnsureSharedSurface(RoundUp(width), RoundUp(height))) return;

            IntPtr screen = GetDC(IntPtr.Zero);
            System.Diagnostics.Stopwatch clock = DiagnoseRaw ? System.Diagnostics.Stopwatch.StartNew() : null;
            bool fromDuplication = false;

            try
            {
                DesktopFrameSource source = EnsureFrameSource();
                if (source != null)
                {
                    _frameSourceIdle = DateTime.UtcNow;
                    fromDuplication = source.TryReadInto(_sharedBits, _sharedWidth * 4, left, top, width, height);
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

            // GDI 读屏一轮要占 UI 线程 18 ms，帧率再高就会明显拖慢交互 —— 提醒一次。
            // 只在「确实拿不到桌面复制」时提醒：第一帧走 GDI 是正常的（复制对象还在预热）。
            if (fromDuplication)
            {
                _gdiRounds = 0;
            }
            else if (_frameSource == null && _fps > 6 && _gdiWarnedFps != _fps && ++_gdiRounds > 5)
            {
                _gdiWarnedFps = _fps;
                Log.Info("GDI 读屏每轮约 18 ms，刷新 " + _fps + " 帧/秒会占用较多 UI 线程时间；"
                         + "取图方式换成「GPU 加速」可以降到 1 ms/轮");
            }

            foreach (Target target in snapshot) Render(target, left, top);
        }

        static bool TryGetScreenRect(OverlayWindow card, out int left, out int top, out int width, out int height)
        {
            left = top = 0;
            width = height = 0;

            if (card == null) return false;
            try
            {
                if (!card.IsVisible || card.WindowState == WindowState.Minimized) return false;
            }
            catch { return false; }

            double inset = _shadow ? ShadowInset : 0.0;
            double w = card.ActualWidth - inset * 2.0;
            double h = card.ActualHeight - inset * 2.0;
            if (w < 4.0 || h < 4.0) return false;

            Point origin;
            DpiScale dpi;
            try
            {
                origin = card.PointToScreen(new Point(inset, inset));
                dpi = VisualTreeHelper.GetDpi(card);
            }
            catch { return false; }

            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            left = (int)Math.Round(origin.X);
            top = (int)Math.Round(origin.Y);
            width = (int)Math.Round(w * scaleX);
            height = (int)Math.Round(h * scaleY);
            return width >= 4 && height >= 4;
        }

        /// <summary>从共享的屏幕图里取自己这一块，缩小 + 模糊，然后交给卡片画出来。</summary>
        static void Render(Target target, int originLeft, int originTop)
        {
            int left, top, width, height;
            if (!TryGetScreenRect(target.Window, out left, out top, out width, out height)) return;

            int w = Math.Max(2, width / Downscale);
            int h = Math.Max(2, height / Downscale);

            if (!EnsureSurface(target, w, h)) return;

            SetStretchBltMode(target.MemoryDc, Halftone);
            StretchBlt(target.MemoryDc, 0, 0, w, h, _sharedDc, left - originLeft, top - originTop,
                       width, height, Srccopy);

            var buffer = new byte[w * h * 4];
            Marshal.Copy(target.Bits, buffer, 0, buffer.Length);

            GlassCapture.BoxBlur(buffer, w, h, _blurRadius, true);
            GlassCapture.BoxBlur(buffer, w, h, _blurRadius, false);

            if (Diagnose) LogSample(buffer, w, h);
            CheckUsable(target, buffer, w, h);

            // 和上一次真正画出去的那一帧几乎一样就不动 WPF：静置时背景没变，重画只是白费。
            if (SameAsLastFrame(target, buffer, w, h)) return;

            // Bgr32 = 忽略 alpha：从屏幕 DC 拷出来的数据 alpha 全是 0，用 Bgra32 建图会整块透明
            BitmapSource image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, buffer, w * 4);
            image.Freeze();

            try
            {
                target.Surface.SetGlass(image);
                if (!_pushed)
                {
                    _pushed = true;
                    Log.Info("玻璃画面已画进卡片：" + w + "x" + h + "（模糊半径 " + _blurRadius
                             + "px，取图后端 " + (_frameSource != null ? "DXGI 桌面复制" : "GDI 读屏") + "）");
                }
            }
            catch (Exception ex) { Log.Warn("设置卡片玻璃失败: " + ex.Message); }

            TraceFrame(buffer, w, h);
        }

        static bool EnsureSurface(Target target, int w, int h)
        {
            if (target.MemoryDc != IntPtr.Zero && target.Width == w && target.Height == h) return true;

            ReleaseSurface(target);

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
                target.MemoryDc = CreateCompatibleDC(screen);
                target.Dib = CreateDIBSection(screen, ref bmi, 0, out target.Bits, IntPtr.Zero, 0);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }

            if (target.MemoryDc == IntPtr.Zero || target.Dib == IntPtr.Zero || target.Bits == IntPtr.Zero)
            {
                ReleaseSurface(target);
                return false;
            }

            // 必须把 DIB 选进内存 DC：否则 StretchBlt 画在 DC 默认的 1×1 单色位图上，取样永远是全黑。
            target.Previous = SelectObject(target.MemoryDc, target.Dib);
            if (target.Previous == IntPtr.Zero || target.Previous == new IntPtr(-1))
            {
                ReleaseSurface(target);
                return false;
            }

            target.Width = w;
            target.Height = h;
            target.LastFrame = null;
            return true;
        }

        static void ReleaseSurface(Target target)
        {
            if (target == null) return;

            if (target.Previous != IntPtr.Zero && target.MemoryDc != IntPtr.Zero)
            {
                SelectObject(target.MemoryDc, target.Previous);
                target.Previous = IntPtr.Zero;
            }

            if (target.Dib != IntPtr.Zero) { DeleteObject(target.Dib); target.Dib = IntPtr.Zero; }
            if (target.MemoryDc != IntPtr.Zero) { DeleteDC(target.MemoryDc); target.MemoryDc = IntPtr.Zero; }
            target.Bits = IntPtr.Zero;
            target.Width = 0;
            target.Height = 0;
            target.LastFrame = null;
        }

        static bool SameAsLastFrame(Target target, byte[] buffer, int w, int h)
        {
            if (target.LastFrame == null || target.LastWidth != w || target.LastHeight != h)
            {
                KeepFrame(target, buffer, w, h);
                return false;
            }

            // 隔 4 个像素看一次。和「上一次真正画出去的帧」比，慢漂移会累积到超过阈值，不会永远跳过。
            for (int i = 0; i + 3 < buffer.Length; i += 16)
            {
                int db = buffer[i] - target.LastFrame[i];
                if (db < -SkipTolerance || db > SkipTolerance) { KeepFrame(target, buffer, w, h); return false; }
                int dg = buffer[i + 1] - target.LastFrame[i + 1];
                if (dg < -SkipTolerance || dg > SkipTolerance) { KeepFrame(target, buffer, w, h); return false; }
                int dr = buffer[i + 2] - target.LastFrame[i + 2];
                if (dr < -SkipTolerance || dr > SkipTolerance) { KeepFrame(target, buffer, w, h); return false; }
            }

            return true;
        }

        /// <summary>模糊图上差 2 以内看不出来 —— 低于这个就当作「没变」。</summary>
        const int SkipTolerance = 2;

        static void KeepFrame(Target target, byte[] buffer, int w, int h)
        {
            if (target.LastFrame == null || target.LastFrame.Length != buffer.Length)
                target.LastFrame = new byte[buffer.Length];

            Buffer.BlockCopy(buffer, 0, target.LastFrame, 0, buffer.Length);
            target.LastWidth = w;
            target.LastHeight = h;
        }

        static int RoundUp(int value)
        {
            return ((value + 127) / 128) * 128;
        }

        // ==================== 取图后端 ====================

        static DesktopFrameSource EnsureFrameSource()
        {
            // 卡片增删会让目标短暂全部消失再出现，别因此反复创建 D3D 设备 / 桌面复制；
            // 真正长时间不用（5 秒）才释放，别让 GPU 一直醒着。
            if (_frameSource != null && (DateTime.UtcNow - _frameSourceIdle).TotalSeconds > 5)
            {
                ReleaseFrameSource();
            }

            if (_frameSource != null || _frameSourceTried) return _frameSource;
            _frameSourceTried = true;

            DesktopFrameSource source;
            string reason;
            if (DesktopFrameSource.TryCreate(_source, out source, out reason))
            {
                _frameSource = source;
                _frameSourceIdle = DateTime.UtcNow;
                Log.Info("玻璃取图改用 DXGI 桌面复制（GPU 加速，不再用 GDI 读屏）");
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
                Compression = 0
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

        // ==================== 保险 ====================

        /// <summary>
        /// 连续几帧取样全黑，说明这个模式在当前环境里读不到背景（截屏被安全策略拦截 / 屏幕本来就是黑的 /
        /// DC 状态异常）。退回是正确选择 —— 卡片会被放回截屏里，改走系统亚克力或兼容实现。
        /// </summary>
        static void CheckUsable(Target target, byte[] buffer, int w, int h)
        {
            if (target.OnUnavailable == null) return;

            long sum = 0;
            for (int i = 0; i + 2 < buffer.Length; i += 4)
                sum += (buffer[i] + 2 * buffer[i + 1] + buffer[i + 2]) / 4;

            long mean = w * h == 0 ? 0 : sum / (w * h);

            if (mean > 2) { _blankFrames = 0; return; }
            if (++_blankFrames < 3) return;

            _blankFrames = 0;
            Action<string> handler = target.OnUnavailable;
            if (handler == null) return;

            try { handler("底板连续 3 帧取样全黑：当前环境读不到屏幕内容（截屏被拦截或屏幕本来就是黑的）"); }
            catch (Exception ex) { Log.Warn("取样失败回调出错: " + ex.Message); }
        }

        // ==================== 诊断 ====================

        static bool? _diagnose;

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

        static DateTime _lastReport = DateTime.MinValue;

        static void LogSample(byte[] buffer, int w, int h)
        {
            if ((DateTime.UtcNow - _lastReport).TotalSeconds < 5) return;
            _lastReport = DateTime.UtcNow;

            long sum = 0;
            int max = 0;
            for (int i = 0; i + 2 < buffer.Length; i += 4)
            {
                int luma = (buffer[i] + 2 * buffer[i + 1] + buffer[i + 2]) / 4;
                sum += luma;
                if (luma > max) max = luma;
            }

            int count = w * h;
            Log.Info("玻璃取样 " + w + "x" + h + "：平均亮度 " + (count == 0 ? 0 : sum / count)
                     + "，最亮 " + max + "，模糊半径 " + _blurRadius + "px（半分辨率）");
        }

        // ==================== 逐帧追踪 ====================
        // MIRRORPOND_GLASS_TRACE=1：每画一帧写一行「时间 + 尺寸 + 平均亮度」，
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

        // ==================== interop ====================

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

        delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr param);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hwnd, int command);

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
        static extern bool StretchBlt(IntPtr destination, int destinationX, int destinationY,
                                      int destinationWidth, int destinationHeight,
                                      IntPtr source, int sourceX, int sourceY, int sourceWidth,
                                      int sourceHeight, uint operation);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr destination, int destinationX, int destinationY,
                                  int width, int height, IntPtr source, int sourceX, int sourceY,
                                  uint operation);
    }
}
