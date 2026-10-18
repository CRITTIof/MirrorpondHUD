using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WindowOverlay.Ui
{
    /// <summary>
    /// 毛玻璃背景：抓取窗口后面那块屏幕，缩小后做模糊，再当作叠层背景。
    /// 抓屏前窗口会设置 WDA_EXCLUDEFROMCAPTURE，所以截到的画面里不含叠层自己。
    /// </summary>
    internal static class GlassCapture
    {
        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        public static BitmapSource Grab(int left, int top, int width, int height, double blur)
        {
            if (width <= 0 || height <= 0) return null;

            using (var full = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(full))
                {
                    g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
                }

                int smallWidth = Math.Max(8, Math.Min(180, width / 4));
                int smallHeight = Math.Max(8, Math.Min(180, height / 4));

                using (var small = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(full, new System.Drawing.Rectangle(0, 0, smallWidth, smallHeight));
                    }

                    BoxBlur(small, Math.Max(0, (int)Math.Round(blur / 4.0)));
                    return ToBitmapSource(small);
                }
            }
        }

        /// <summary>两次可分离的方框模糊，足够糊掉文字轮廓。</summary>
        static void BoxBlur(Bitmap bitmap, int radius)
        {
            if (radius <= 0) return;

            int width = bitmap.Width;
            int height = bitmap.Height;
            BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
                                              ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);

                for (int pass = 0; pass < 2; pass++)
                {
                    BlurHorizontal(buffer, width, height, data.Stride, radius);
                    BlurVertical(buffer, width, height, data.Stride, radius);
                }

                Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        static void BlurHorizontal(byte[] buffer, int width, int height, int stride, int radius)
        {
            byte[] line = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                Buffer.BlockCopy(buffer, row, line, 0, width * 4);

                for (int x = 0; x < width; x++)
                {
                    int sumB = 0, sumG = 0, sumR = 0, count = 0;
                    int from = Math.Max(0, x - radius);
                    int to = Math.Min(width - 1, x + radius);
                    for (int i = from; i <= to; i++)
                    {
                        int p = i * 4;
                        sumB += line[p];
                        sumG += line[p + 1];
                        sumR += line[p + 2];
                        count++;
                    }

                    int q = row + x * 4;
                    buffer[q] = (byte)(sumB / count);
                    buffer[q + 1] = (byte)(sumG / count);
                    buffer[q + 2] = (byte)(sumR / count);
                }
            }
        }

        static void BlurVertical(byte[] buffer, int width, int height, int stride, int radius)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int sumB = 0, sumG = 0, sumR = 0, count = 0;
                    int from = Math.Max(0, y - radius);
                    int to = Math.Min(height - 1, y + radius);
                    for (int i = from; i <= to; i++)
                    {
                        int p = i * stride + x * 4;
                        sumB += buffer[p];
                        sumG += buffer[p + 1];
                        sumR += buffer[p + 2];
                        count++;
                    }

                    int q = y * stride + x * 4;
                    buffer[q] = (byte)(sumB / count);
                    buffer[q + 1] = (byte)(sumG / count);
                    buffer[q + 2] = (byte)(sumR / count);
                }
            }
        }

        static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            IntPtr handle = bitmap.GetHbitmap();
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }
    }
}
