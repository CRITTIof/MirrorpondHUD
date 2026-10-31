using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// Shell 图标 / 缩略图（中转站、媒体卡、文件列表共用）。
    ///
    /// - 图标：默认按扩展名取（<c>SHGFI_USEFILEATTRIBUTES</c>，不碰磁盘 / 网络盘），按“扩展名 + 尺寸”缓存；
    /// - 缩略图：<c>IShellItemImageFactory</c>，在**独立 STA 线程**上跑，避免网络盘 / 慢文件把 UI 卡住；
    /// - 缓存有上限；同一路径换文件后缩略图可能旧，需要时调 <see cref="ClearCache"/>。
    /// </summary>
    public static class ShellIcons
    {
        const uint SHGFI_ICON = 0x000000100;
        const uint SHGFI_LARGEICON = 0x000000000;
        const uint SHGFI_SMALLICON = 0x000000001;
        const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        const int MaxIcons = 256;
        const int MaxThumbnails = 128;

        static readonly object Gate = new object();
        static readonly Dictionary<string, ImageSource> Icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ImageSource> Thumbnails = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly Lazy<StaWorker> Worker = new Lazy<StaWorker>(() => new StaWorker(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 取文件图标。默认只按扩展名（<paramref name="extensionOnly"/> = true），不访问真实文件；
        /// 需要 exe / 快捷方式的专属图标时传 false（会碰磁盘，慢盘请放后台线程）。
        /// </summary>
        public static ImageSource GetIcon(string path, int size = 16, bool extensionOnly = true)
        {
            string key = IconKey(path, size, extensionOnly);

            lock (Gate)
            {
                ImageSource cached;
                if (Icons.TryGetValue(key, out cached)) return cached;
            }

            ImageSource icon = LoadIcon(path, size, extensionOnly);
            if (icon == null) return null;

            lock (Gate)
            {
                if (Icons.Count > MaxIcons) Icons.Clear();
                Icons[key] = icon;
            }
            return icon;
        }

        /// <summary>后台 STA 线程取缩略图；文件不存在 / 没有缩略图 / 失败都返回 null。</summary>
        public static Task<ImageSource> GetThumbnailAsync(string path, int size = 96)
        {
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult<ImageSource>(null);

            string key = ThumbnailKey(path, size);
            lock (Gate)
            {
                ImageSource cached;
                if (Thumbnails.TryGetValue(key, out cached)) return Task.FromResult(cached);
            }

            return Worker.Value.Run(() => LoadThumbnail(path, size)).ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion) return null;

                ImageSource image = task.Result;
                if (image == null) return null;

                lock (Gate)
                {
                    if (Thumbnails.Count > MaxThumbnails) Thumbnails.Clear();
                    Thumbnails[key] = image;
                }
                return image;
            }, TaskScheduler.Default);
        }

        public static void ClearCache()
        {
            lock (Gate)
            {
                Icons.Clear();
                Thumbnails.Clear();
            }
        }

        static string IconKey(string path, int size, bool extensionOnly)
        {
            if (!extensionOnly)
                return "path|" + (path ?? "") + "|" + size;

            string ext = "";
            try { ext = Path.GetExtension(path ?? ""); }
            catch { }

            return "ext|" + (ext ?? "").ToLowerInvariant() + "|" + size;
        }

        static string ThumbnailKey(string path, int size)
        {
            string full = path ?? "";
            try { full = Path.GetFullPath(full); }
            catch { }

            return size + "|" + full.ToLowerInvariant();
        }

        static ImageSource LoadIcon(string path, int size, bool extensionOnly)
        {
            try
            {
                string target;
                uint flags = SHGFI_ICON | (size <= 16 ? SHGFI_SMALLICON : SHGFI_LARGEICON);

                if (extensionOnly)
                {
                    string ext = "";
                    try { ext = Path.GetExtension(path ?? ""); }
                    catch { }

                    target = string.IsNullOrEmpty(ext) ? "file" : "x" + ext;
                    flags |= SHGFI_USEFILEATTRIBUTES;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                    target = path;
                }

                var info = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(target, FILE_ATTRIBUTE_NORMAL, ref info,
                                              (uint)Marshal.SizeOf(typeof(SHFILEINFO)), flags);
                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

                try
                {
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    if (source.CanFreeze) source.Freeze();
                    return source;
                }
                finally
                {
                    DestroyIcon(info.hIcon);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Shell 图标读取失败(" + path + "): " + ex.Message);
                return null;
            }
        }

        static ImageSource LoadThumbnail(string path, int size)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

                Guid iid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                if (factory == null) return null;

                IntPtr hBitmap;
                factory.GetImage(new SIZE { cx = size, cy = size },
                                 SIIGBF.THUMBNAILONLY | SIIGBF.RESIZETOFIT, out hBitmap);
                if (hBitmap == IntPtr.Zero) return null;

                try
                {
                    BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    if (source.CanFreeze) source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch (Exception ex)
            {
                Log.Info("Shell 缩略图不可用(" + path + "): " + ex.Message);
                return null;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        enum SIIGBF
        {
            RESIZETOFIT = 0x00,
            BIGGERSIZEOK = 0x01,
            MEMORYONLY = 0x02,
            ICONONLY = 0x04,
            THUMBNAILONLY = 0x08,
            INCACHEONLY = 0x10,
            CROPTOSQUARE = 0x20,
            WIDETHUMBNAILS = 0x40,
            ICONBACKGROUND = 0x80,
            SCALEUP = 0x100
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
                                           ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
                                                       [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        /// <summary>一个常驻 STA 线程 + 队列：Shell COM 在这里跑，调用方不用关心 apartment。</summary>
        sealed class StaWorker
        {
            readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
            readonly Thread _thread;

            public StaWorker()
            {
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "Mirrorpond Shell",
                    Priority = ThreadPriority.BelowNormal
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }

            public Task<T> Run<T>(Func<T> work)
            {
                var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Add(() =>
                {
                    try { completion.SetResult(work()); }
                    catch (Exception ex) { completion.SetException(ex); }
                });
                return completion.Task;
            }

            void Run()
            {
                foreach (Action action in _queue.GetConsumingEnumerable())
                {
                    try { action(); }
                    catch { }
                }
            }
        }
    }
}
