using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 桌面背景取样：不再用 `PrintWindow` 抓 Progman。
    ///
    /// Windows 10/11 的壁纸是 DWM 画的，`PrintWindow(Progman)` 经常只拿到黑色或纯色
    /// （尤其是 Windows 聚焦 / 幻灯片壁纸）；而且抓 Progman 会让桌面跟着重绘，拖桌面图标时
    /// 卡片会闪。这里直接按注册表里的壁纸文件渲染出屏幕区域（按 1/2 缩放缓存）：
    /// 不碰 Explorer、不闪、壁纸换了一张也能跟上。图标不参与取样（模糊后本来也看不出来）。
    /// </summary>
    internal static class DesktopBackground
    {
        /// <summary>缓存缩放：1/2 分辨率足够模糊用，1920×1080 只占 2 MB。</summary>
        public const double Scale = 0.5;

        const int CacheMs = 2000;
        const uint ColorDesktop = 1;

        public struct Frame
        {
            public byte[] Pixels;
            public int Width;
            public int Height;
            public double Scale;
        }

        [DllImport("user32.dll")]
        static extern uint GetSysColor(int index);

        static readonly object Gate = new object();
        static readonly BitmapImage[] CachedImage = new BitmapImage[1];
        static string _cachedPath;

        static Frame _frame;
        static long _stamp;
        static string _framePath;
        static string _frameStyle;
        static int _frameWidth;
        static int _frameHeight;

        /// <summary>取 <paramref name="bounds"/>（屏幕物理像素）对应的壁纸像素；失败时退成桌面纯色。</summary>
        public static bool TryGet(GlassCapture.RECT bounds, out Frame frame)
        {
            frame = default(Frame);

            int width = Math.Max(1, (int)Math.Round(bounds.Width * Scale));
            int height = Math.Max(1, (int)Math.Round(bounds.Height * Scale));

            string path = CurrentWallpaperPath();
            string style = CurrentWallpaperStyle();

            lock (Gate)
            {
                long now = Environment.TickCount64;
                if (_frame.Pixels != null &&
                    now - _stamp <= CacheMs &&
                    _frameWidth == width && _frameHeight == height &&
                    string.Equals(_framePath, path, StringComparison.OrdinalIgnoreCase) &&
                    _frameStyle == style)
                {
                    frame = _frame;
                    return true;
                }

                byte[] pixels = Render(path, style, width, height);
                if (pixels == null) return false;

                _frame = new Frame { Pixels = pixels, Width = width, Height = height, Scale = Scale };
                _stamp = now;
                _framePath = path;
                _frameStyle = style;
                _frameWidth = width;
                _frameHeight = height;

                frame = _frame;
                return true;
            }
        }

        static byte[] Render(string path, string style, int width, int height)
        {
            BitmapSource source = Load(path);
            if (source != null)
            {
                try { return RenderImage(source, style, width, height); }
                catch (Exception ex) { Log.Warn("壁纸取样失败，改用桌面颜色: " + ex.Message); }
            }

            return SolidColor(width, height);
        }

        static byte[] RenderImage(BitmapSource source, string style, int width, int height)
        {
            int iw = source.PixelWidth, ih = source.PixelHeight;
            if (iw <= 0 || ih <= 0) return null;

            var converted = source;
            if (source.Format != PixelFormats.Bgra32)
            {
                var convert = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                convert.Freeze();
                converted = convert;
            }

            // Stretch（"2"）：整张图拉满；其余（Fill "10" / Span "22" / 默认）按 cover 居中裁切。
            bool stretch = style == "2";

            double scale = stretch ? 0 : Math.Max(width / (double)iw, height / (double)ih);
            int cropWidth = stretch ? iw : Math.Min(iw, Math.Max(1, (int)Math.Round(width / scale)));
            int cropHeight = stretch ? ih : Math.Min(ih, Math.Max(1, (int)Math.Round(height / scale)));

            int cropX = Math.Max(0, (iw - cropWidth) / 2);
            int cropY = Math.Max(0, (ih - cropHeight) / 2);

            var cropped = new CroppedBitmap(converted, new System.Windows.Int32Rect(cropX, cropY, cropWidth, cropHeight));
            cropped.Freeze();

            BitmapSource final = cropped;
            if (cropWidth != width || cropHeight != height)
            {
                var transform = new ScaleTransform(width / (double)cropWidth, height / (double)cropHeight);
                var scaled = new TransformedBitmap(cropped, transform);
                scaled.Freeze();
                final = scaled;
            }

            var pixels = new byte[width * height * 4];
            final.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }

        static BitmapSource Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            lock (Gate)
            {
                if (CachedImage[0] != null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
                    return CachedImage[0];
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                lock (Gate)
                {
                    CachedImage[0] = image;
                    _cachedPath = path;
                }
                return image;
            }
            catch (Exception ex)
            {
                Log.Warn("读取壁纸失败: " + ex.Message);
                return null;
            }
        }

        static byte[] SolidColor(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            uint color = GetSysColor((int)ColorDesktop);
            byte b = (byte)(color & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte r = (byte)((color >> 16) & 0xFF);

            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
            return pixels;
        }

        static string CurrentWallpaperPath()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null);
                string path = value as string;
                return string.IsNullOrWhiteSpace(path) ? "" : Environment.ExpandEnvironmentVariables(path);
            }
            catch { return ""; }
        }

        static string CurrentWallpaperStyle()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallpaperStyle", null);
                string style = value as string;
                return string.IsNullOrWhiteSpace(style) ? "10" : style.Trim();
            }
            catch { return "10"; }
        }
    }
}
