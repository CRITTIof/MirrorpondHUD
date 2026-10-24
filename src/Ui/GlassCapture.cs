using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 毛玻璃取样：把卡片“后面”的画面按像素拼出来（多个窗口 + 桌面壁纸），再缩小模糊。
    ///
    /// 做法：枚举卡片矩形内、从上层到下层的外部窗口，每个窗口用
    /// <c>PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)</c> 画进离屏位图（这张图里没有叠层自己），
    /// 最后把桌面窗口（Progman）垫在最底层。生成小图时，每个采样点取“盖住它的最上层窗口”的像素，
    /// 所以卡片跨窗口、压住窗口边角、或者干脆只压在桌面上，都能取到正确的底色。
    ///
    /// 全程不抓屏：截图 / 录屏 / 共享屏幕里叠层始终可见（旧的抓屏排除方案会让卡片在 OBS 里周期性消失）。
    /// </summary>
    internal static class GlassCapture
    {
        const uint PW_RENDERFULLCONTENT = 0x00000002;
        const uint BiRGB = 0;

        /// <summary>普通窗口的取样缓存时长（毫秒）。</summary>
        public const int CacheMs = 150;

        /// <summary>桌面壁纸变化很慢，缓存久一点，省掉重复的整屏重绘。</summary>
        public const int DesktopCacheMs = 2000;

        /// <summary>一张卡片最多参考几层窗口（含桌面）。</summary>
        const int MaxLayers = 5;

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Colors;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage,
                                              out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        /// <summary>一层背景：一个窗口 + 它在屏幕上的矩形。</summary>
        public struct Layer
        {
            public IntPtr Window;
            public RECT Rect;
            public bool Desktop;
        }

        /// <summary>一次性缓存：同一个窗口在一个时间片内只重绘一次。</summary>
        sealed class Frame
        {
            public IntPtr Window;
            public long Stamp;
            public int Width;
            public int Height;
            public byte[] Pixels;
            public bool Blank;
        }

        static readonly List<Frame> Frames = new List<Frame>(6);

        // ==================== 找卡片后面的窗口 ====================

        /// <summary>
        /// 收集卡片矩形内、从上层到下层的外部窗口，最后补上桌面窗口。
        /// 跳过本进程窗口与截图 / 录屏工具的浮层，否则它们会让毛玻璃变色。
        /// </summary>
        public static List<Layer> FindLayers(int left, int top, int width, int height)
        {
            var layers = new List<Layer>(MaxLayers);
            bool hasDesktop = false;

            uint self = GetOwnProcessId();
            int right = left + width;
            int bottom = top + height;

            // 「显示桌面」时 Progman 会跑到 z 序最前面，所以桌面层必须留在它真实的位置上，
            // 不能一律垫到最底部，否则会把后面（其实被桌面挡住）的窗口内容当成背景。
            int screenWidth = Math.Max(640, width * 2);
            int screenHeight = Math.Max(480, height * 2);

            try
            {
                EnumWindows((hwnd, param) =>
                {
                    if (hwnd == IntPtr.Zero) return true;
                    if (!IsWindowVisible(hwnd)) return true;

                    RECT rect;
                    if (!GetWindowRect(hwnd, out rect)) return true;
                    if (rect.Width < 16 || rect.Height < 16) return true;
                    if (rect.Right <= left || rect.Left >= right) return true;
                    if (rect.Bottom <= top || rect.Top >= bottom) return true;

                    string cls = ClassNameOf(hwnd);

                    if (IsDesktopClass(cls))
                    {
                        // 桌面上还有一堆 136x39 的 WorkerW 助手窗口，按尺寸过滤掉。
                        if (hasDesktop) return true;
                        if (rect.Width < screenWidth / 2 || rect.Height < screenHeight / 2) return true;
                        if (layers.Count >= MaxLayers) return true;

                        layers.Add(new Layer { Window = hwnd, Rect = rect, Desktop = true });
                        hasDesktop = true;
                        return true;
                    }

                    if (layers.Count >= MaxLayers) return true;

                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == self) return true;
                    if (IsCaptureOverlay(cls, pid)) return true;

                    layers.Add(new Layer { Window = hwnd, Rect = rect });
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Warn("枚举卡片后方窗口失败: " + ex.Message);
            }

            return layers;
        }

        static string ClassNameOf(IntPtr hwnd)
        {
            try
            {
                var buffer = new StringBuilder(128);
                if (GetClassName(hwnd, buffer, 128) > 0) return buffer.ToString();
            }
            catch { }
            return "";
        }

        static bool IsDesktopClass(string cls)
        {
            return string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cls, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 截图 / 录屏工具的浮层：Windows 截图工具（ScreenClippingHost / ScreenSketch）、
        /// 以及常见的第三方截图、共享工具。它们的浮层盖在最上层，一旦被当成背景取用，
        /// 毛玻璃会突然变亮或变灰——所以直接跳过，继续取下面真正的内容。
        /// </summary>
        static bool IsCaptureOverlay(string cls, uint pid)
        {
            if (!string.IsNullOrEmpty(cls))
            {
                if (cls.IndexOf("screenclip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (cls.IndexOf("snipping", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (cls.IndexOf("screenshot", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            string name = ProcessNameOf(pid);
            switch (name)
            {
                case "snippingtool":
                case "screensketch":
                case "screenclippinghost":
                case "snipaste":
                case "sniptool":
                case "nvidia share":
                case "nvsphelper64":
                case "sharex":
                case "picpick":
                case "greenshot":
                    return true;
                default:
                    return false;
            }
        }

        static string ProcessNameOf(uint pid)
        {
            if (pid == 0) return "";
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                    return (process.ProcessName ?? "").Trim().ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        static uint _ownPid;

        static uint GetOwnProcessId()
        {
            if (_ownPid != 0) return _ownPid;
            try { _ownPid = (uint)Process.GetCurrentProcess().Id; }
            catch { _ownPid = 0; }
            return _ownPid;
        }

        // ==================== 取样 ====================

        /// <summary>
        /// 用 <paramref name="layers"/>（上 → 下）拼出卡片区域的模糊背景；
        /// 返回 null 表示这次什么都取不到（调用方保留上一帧）。
        /// </summary>
        public static BitmapSource Grab(List<Layer> layers, int cardLeft, int cardTop,
                                        int cardWidth, int cardHeight, double blur)
        {
            if (layers == null || layers.Count == 0 || cardWidth <= 0 || cardHeight <= 0) return null;

            int divisor = Math.Max(3, Math.Min(12, (int)Math.Round(blur * 0.45 + 3)));
            int targetWidth = Math.Max(8, cardWidth / divisor);
            int targetHeight = Math.Max(8, cardHeight / divisor);

            // 每层整窗像素（带缓存）
            var pixels = new byte[layers.Count][];
            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                if (layer.Rect.Width <= 0 || layer.Rect.Height <= 0) continue;
                pixels[i] = GetFramePixels(layer.Window, layer.Rect.Width, layer.Rect.Height,
                                           layer.Desktop ? DesktopCacheMs : CacheMs);
            }

            var small = new byte[targetWidth * targetHeight * 4];
            bool any = false;

            for (int y = 0; y < targetHeight; y++)
            {
                int sy = cardTop + (int)((long)y * cardHeight / targetHeight);
                int destRow = y * targetWidth * 4;

                for (int x = 0; x < targetWidth; x++)
                {
                    int sx = cardLeft + (int)((long)x * cardWidth / targetWidth);

                    int source = -1;
                    for (int i = 0; i < layers.Count; i++)
                    {
                        Layer layer = layers[i];
                        if (pixels[i] == null) continue;
                        if (sx < layer.Rect.Left || sx >= layer.Rect.Right) continue;
                        if (sy < layer.Rect.Top || sy >= layer.Rect.Bottom) continue;
                        source = i;
                        break;
                    }

                    int d = destRow + x * 4;

                    if (source < 0)
                    {
                        // 没有任何一层覆盖这个点（连桌面都没取到）：留黑，最后统一判空
                        small[d + 3] = 255;
                        continue;
                    }

                    Layer hit = layers[source];
                    int px = sx - hit.Rect.Left;
                    int py = sy - hit.Rect.Top;
                    if (px < 0) px = 0;
                    if (py < 0) py = 0;
                    if (px >= hit.Rect.Width) px = hit.Rect.Width - 1;
                    if (py >= hit.Rect.Height) py = hit.Rect.Height - 1;

                    int s = (py * hit.Rect.Width + px) * 4;
                    byte[] buffer = pixels[source];
                    if (s + 3 >= buffer.Length) continue;

                    small[d] = buffer[s];
                    small[d + 1] = buffer[s + 1];
                    small[d + 2] = buffer[s + 2];
                    small[d + 3] = 255;
                    any = true;
                }
            }

            if (!any || IsBlank(small)) return null;

            int radius = (int)Math.Round(Math.Max(0, blur) / Math.Max(2, divisor * 0.9));
            if (radius > 0) BoxBlur(small, targetWidth, targetHeight, targetWidth * 4, radius);

            BitmapSource image = BitmapSource.Create(targetWidth, targetHeight, 96, 96,
                                                     PixelFormats.Bgr32, null, small, targetWidth * 4);
            image.Freeze();
            return image;
        }

        static byte[] GetFramePixels(IntPtr window, int width, int height, int cacheMs)
        {
            long now = Environment.TickCount64;

            for (int i = 0; i < Frames.Count; i++)
            {
                Frame cached = Frames[i];
                if (cached.Window != window) continue;
                if (cached.Width != width || cached.Height != height) continue;
                if (now - cached.Stamp > cacheMs) continue;
                return cached.Blank ? null : cached.Pixels;
            }

            byte[] fresh = PrintWindowPixels(window, width, height);
            bool blank = fresh == null || IsBlank(fresh);

            Frames.RemoveAll(f => f.Window == window);
            Frames.Add(new Frame
            {
                Window = window,
                Stamp = now,
                Width = width,
                Height = height,
                Pixels = fresh,
                Blank = blank
            });
            if (Frames.Count > 8) Frames.RemoveAt(0);

            return blank ? null : fresh;
        }

        /// <summary>把目标窗口整块画进 32bpp 位图（不含叠层自己）。</summary>
        static byte[] PrintWindowPixels(IntPtr window, int width, int height)
        {
            IntPtr hdc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr dib = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;

            try
            {
                hdc = GetDC(IntPtr.Zero);
                memoryDc = CreateCompatibleDC(hdc);
                if (memoryDc == IntPtr.Zero) return null;

                var info = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                        Width = width,
                        Height = -height,
                        Planes = 1,
                        BitCount = 32,
                        Compression = BiRGB
                    }
                };

                IntPtr bits;
                dib = CreateDIBSection(memoryDc, ref info, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;

                previous = SelectObject(memoryDc, dib);

                if (!PrintWindow(window, memoryDc, PW_RENDERFULLCONTENT))
                {
                    Frames.RemoveAll(f => f.Window == window);
                    return null;
                }

                int stride = width * 4;
                int bytes = stride * height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(bits, buffer, 0, bytes);
                return buffer;
            }
            catch (Exception ex)
            {
                Log.Warn("PrintWindow 取样失败: " + ex.Message);
                return null;
            }
            finally
            {
                if (memoryDc != IntPtr.Zero && previous != IntPtr.Zero) SelectObject(memoryDc, previous);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        static bool IsBlank(byte[] pixels)
        {
            // 有些程序（独占全屏 / 硬件覆盖层）的 PrintWindow 只会给出全黑，
            // 这时候宁可丢弃这一层，也不要让卡片变成黑块。
            int step = Math.Max(4, (pixels.Length / 4096) / 4 * 4);
            int bright = 0, total = 0;

            for (int i = 0; i + 2 < pixels.Length; i += step)
            {
                total++;
                if (pixels[i] > 8 || pixels[i + 1] > 8 || pixels[i + 2] > 8) bright++;
            }

            return total == 0 || bright * 100 / Math.Max(1, total) < 2;
        }

        /// <summary>两遍可分离方框模糊；在小图上跑，几千个像素，开销可以忽略。</summary>
        static void BoxBlur(byte[] buffer, int width, int height, int stride, int radius)
        {
            if (radius <= 0 || width < 3 || height < 3) return;

            var line = new int[Math.Max(width, height) * 4];

            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int from = Math.Max(0, x - radius);
                        int to = Math.Min(width - 1, x + radius);

                        int sumB = 0, sumG = 0, sumR = 0;
                        for (int i = from; i <= to; i++)
                        {
                            int p = row + i * 4;
                            sumB += buffer[p];
                            sumG += buffer[p + 1];
                            sumR += buffer[p + 2];
                        }

                        int count = to - from + 1;
                        int q = x * 4;
                        line[q] = sumB / count;
                        line[q + 1] = sumG / count;
                        line[q + 2] = sumR / count;
                    }

                    for (int x = 0; x < width; x++)
                    {
                        int q = row + x * 4;
                        buffer[q] = (byte)line[x * 4];
                        buffer[q + 1] = (byte)line[x * 4 + 1];
                        buffer[q + 2] = (byte)line[x * 4 + 2];
                    }
                }

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int from = Math.Max(0, y - radius);
                        int to = Math.Min(height - 1, y + radius);

                        int sumB = 0, sumG = 0, sumR = 0;
                        for (int i = from; i <= to; i++)
                        {
                            int p = i * stride + x * 4;
                            sumB += buffer[p];
                            sumG += buffer[p + 1];
                            sumR += buffer[p + 2];
                        }

                        int count = to - from + 1;
                        line[y * 4] = sumB / count;
                        line[y * 4 + 1] = sumG / count;
                        line[y * 4 + 2] = sumR / count;
                    }

                    for (int y = 0; y < height; y++)
                    {
                        int q = y * stride + x * 4;
                        buffer[q] = (byte)line[y * 4];
                        buffer[q + 1] = (byte)line[y * 4 + 1];
                        buffer[q + 2] = (byte)line[y * 4 + 2];
                    }
                }
            }
        }
    }
}
