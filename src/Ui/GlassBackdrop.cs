using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 方案 G：贴在卡片下面的「亚克力底板窗口」。
    ///
    /// 卡片自己必须保持 <c>AllowsTransparency=true</c>（分层窗口 + 逐像素 alpha），
    /// 而 DWM 不会给分层窗口画系统材质 —— 这是 v2.0 的死结。绕开的办法是：在卡片
    /// **正下方**再放一个独立的、非分层、从不绘制的 HWND，给它
    /// <c>WCA_ACCENT_POLICY = ACRYLICBLURBEHIND</c>，DWM 就会在那个矩形里实时模糊
    /// 桌面与其它窗口（GPU 合成，进程侧没有逐帧工作）。
    ///
    /// 底板不接收鼠标、不夺焦点、不参与绘制；位置与尺寸严格等于卡片的内容区
    /// （扣除投影预留的 10 DIP），z 序由 <see cref="Follow"/> 插到卡片正下方。
    ///
    /// 已知限制：accent 的模糊区域固定是窗口矩形，既不跟 <c>SetWindowRgn</c>、
    /// 也不跟 DWM 圆角走，所以底板永远是直角 —— 圆角的补角由卡片自己的投影 / tint 遮住。
    /// </summary>
    internal sealed class GlassBackdrop : IDisposable
    {
        const string ClassName = "MirrorpondHUD.GlassBackdrop";

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

        const int WcaAccentPolicy = 19;
        const int AccentEnableAcrylicBlurBehind = 4;
        const int AccentFlagsDrawAllBorders = 2;

        /// <summary>SM_REMOTESESSION：远程桌面会话里 DWM 材质会退化成纯色。</summary>
        const int SmRemoteSession = 0x1000;

        static bool _classRegistered;
        static readonly WndProcDelegate WndProcKeepAlive = BackdropWndProc;

        IntPtr _handle;
        bool _visible;
        int _gradientColor = DefaultGradientColor;

        /// <summary>
        /// 亚克力的色调。实测（docs/blockhelm-glass.md 3.2 的探针）：DWM 的 alpha 基本被忽略，
        /// 真正决定观感的是 GradientColor 的 RGB —— 玻璃的**亮度**来自这个颜色，
        /// 背后的画面只贡献色相与细微结构（白底 / 红底 / 黑底都试过）。
        /// 所以这里跟着卡片的「背景」颜色走：深色卡片 = 烟色玻璃，浅色卡片 = 乳白玻璃。
        /// </summary>
        const int DefaultGradientColor = unchecked((int)0x01202020);

        GlassBackdrop(IntPtr handle)
        {
            _handle = handle;
        }

        /// <summary>底板窗口是否已经建好并挂着亚克力。</summary>
        public bool IsActive { get { return _handle != IntPtr.Zero; } }

        public IntPtr Handle { get { return _handle; } }

        // ==================== 可用性 ====================

        /// <summary>
        /// 系统亚克力在这台机器上能不能用。矩阵来自 docs/blockhelm-glass.md 的实测：
        /// 关闭「透明效果」、远程会话、高对比度都会让 DWM 只给纯色，必须退回截屏取样。
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

            bool compositionEnabled;
            int hr = DwmIsCompositionEnabled(out compositionEnabled);
            if (hr != 0 || !compositionEnabled)
            {
                reason = "DWM 合成未启用";
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
                // 读不到就交给 SetWindowCompositionAttribute 的返回值判断
            }
            return true;
        }

        // ==================== 创建 / 销毁 ====================

        /// <summary>
        /// 建一个底板窗口并挂上亚克力。失败时 <paramref name="backdrop"/> 为 null，
        /// <paramref name="reason"/> 是可写进日志 / 界面的人话原因。
        /// </summary>
        public static bool TryCreate(out GlassBackdrop backdrop, out string reason)
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
                ClassName, "MirrorpondHUD Glass Backdrop",
                WsPopup | WsClipSiblings,
                0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

            if (handle == IntPtr.Zero)
            {
                reason = "创建底板窗口失败（错误码 " + Marshal.GetLastWin32Error() + "）";
                return false;
            }

            var instance = new GlassBackdrop(handle);
            if (!instance.ApplyAccent())
            {
                instance.Dispose();
                reason = "系统不支持亚克力（SetWindowCompositionAttribute 无效果）";
                return false;
            }

            reason = "";
            backdrop = instance;
            return true;
        }

        bool ApplyAccent()
        {
            if (_handle == IntPtr.Zero) return false;

            // GradientColor 是 0xAABBGGRR；alpha 给到 1/255 让系统只负责模糊，
            // 颜色 / 不透明度仍然由卡片自己的 tint 层决定（「外观」页的设置全部继续生效）。
            var policy = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = AccentFlagsDrawAllBorders,
                GradientColor = _gradientColor,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, buffer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = buffer,
                    SizeOfData = size
                };
                return SetWindowCompositionAttribute(_handle, ref data) != 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;

            try { DestroyWindow(_handle); }
            catch { }

            _handle = IntPtr.Zero;
            _visible = false;
        }

        // ==================== 跟随卡片 ====================

        /// <summary>
        /// 把玻璃色调设成卡片的背景色（GradientColor 是 0xAABBGGRR，alpha 留 1 以防某些系统真的按 alpha 混）。
        /// 只在颜色变化时才重设材质，拖动 / 缩放时不会反复调。
        ///
        /// 色调要往中灰拉一部分：实测 DWM 亚克力的**亮度**完全由这个颜色决定（背后的白底 / 黑底都不改变亮度，
        /// 只改变色相），直接拿卡片背景色会得到一块几乎全黑的板子。往 #808080 混 55% 之后，
        /// 玻璃的亮度落在中间调，背后的色相（壁纸 / 窗口）仍然透得出来，卡片自己的 tint 层再决定最终颜色。
        /// </summary>
        public void SetTint(Color background)
        {
            byte r = Mix(background.R);
            byte g = Mix(background.G);
            byte b = Mix(background.B);

            int color = unchecked((int)(0x01000000
                | ((uint)b << 16)
                | ((uint)g << 8)
                | (uint)r));

            if (color == _gradientColor) return;
            if (_handle == IntPtr.Zero) return;

            _gradientColor = color;
            ApplyAccent();
        }

        const double TintMidGrayWeight = 0.55;

        static byte Mix(byte value)
        {
            double mixed = value + (128.0 - value) * TintMidGrayWeight;
            return (byte)Math.Round(Math.Max(0.0, Math.Min(255.0, mixed)));
        }

        /// <summary>
        /// 按卡片窗口的当前状态重摆底板：贴住卡片内容区、插到卡片正下方、同步可见性。
        /// 卡片隐藏 / 尺寸为 0 时底板一起隐藏。
        /// </summary>
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
                dpi = VisualTreeHelper.GetDpi(card);
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

            // 插到卡片正下方（hWndInsertAfter = 卡片），同时摆位置 / 显示，
            // 一次 SetWindowPos 把 z 序和几何都定下来，避免拖动时闪。
            SetWindowPos(_handle, cardHandle, left, top, pixelWidth, pixelHeight,
                         SwpNoActivate | SwpNoOwnerZorder | SwpShowWindow);
            _visible = true;
        }

        void SetShown(bool show)
        {
            if (_handle == IntPtr.Zero) return;
            if (_visible == show) return;

            ShowWindow(_handle, show ? SwShowNoActivate : SwHide);
            _visible = show;
        }

        // ==================== 窗口过程 ====================

        static IntPtr BackdropWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WmEraseBkgnd:
                    // 底板不画任何东西：画了就会盖住 DWM 的材质
                    return new IntPtr(1);

                case WmPaint:
                    ValidateRect(hwnd, IntPtr.Zero);
                    return IntPtr.Zero;

                case WmNcHitTest:
                    // 双保险：即使某个版本把 WS_EX_TRANSPARENT 弄丢，也不挡鼠标
                    return new IntPtr(HtTransparent);

                case WmMouseActivate:
                    return new IntPtr(MaNoActivate);
            }

            return DefWindowProcW(hwnd, msg, wParam, lParam);
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
        struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
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

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
                                        int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string name);

        [DllImport("dwmapi.dll")]
        static extern int DwmIsCompositionEnabled(out bool enabled);
    }
}
