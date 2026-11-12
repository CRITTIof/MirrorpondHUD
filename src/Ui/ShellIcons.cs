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

namespace MirrorpondHUD.Ui;

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

    static readonly object Gate = new();
    static readonly Dictionary<string, ImageSource> Icons = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, ImageSource> Thumbnails = new(StringComparer.OrdinalIgnoreCase);
    static readonly Lazy<StaWorker> Worker = new(() => new StaWorker(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// S-15：缩略图取图失败（HRESULT 负数）。网络盘断连 / 提供程序崩溃时会连片出现，
    /// 用节流器把「状态转移 + 60 秒补记」压住，避免文件列表刷新时刷满日志。
    /// 「S_OK 但句柄为空」是这类文件本来就没有缩略图，属正常降级，不打这个点。
    /// </summary>
    static readonly LogThrottle _thumbnailFailure = new("Shell 缩略图", 60000);

    /// <summary>
    /// 取文件图标。默认只按扩展名（<paramref name="extensionOnly"/> = true），不访问真实文件；
    /// 需要 exe / 快捷方式的专属图标时传 false（会碰磁盘，慢盘请放后台线程）。
    /// </summary>
    public static ImageSource GetIcon(string path, int size = 16, bool extensionOnly = true)
    {
        string key = IconKey(path, size, extensionOnly);

        lock (Gate)
        {
            if (Icons.TryGetValue(key, out var cached)) return cached;
        }

        ImageSource icon = LoadIcon(path, size, extensionOnly);
        if (icon is null) return null;

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
            if (Thumbnails.TryGetValue(key, out var cached)) return Task.FromResult(cached);
        }

        return Worker.Value.Run(() => LoadThumbnail(path, size)).ContinueWith(task =>
        {
            if (task.Status != TaskStatus.RanToCompletion) return null;

            ImageSource image = task.Result;
            if (image is null) return null;

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
            return $"path|{(path ?? "")}|{size}";

        string ext = "";
        try { ext = Path.GetExtension(path ?? ""); }
        catch (Exception)
        {
            // 只有畸形路径（非法字符 / 超长）才会进来：缓存键退化为「无扩展名」，
            // 图标仍按通用文件图标返回。这条路径每次刷新列表都会跑，记日志只会刷屏。
        }

        return $"ext|{(ext ?? "").ToLowerInvariant()}|{size}";
    }

    static string ThumbnailKey(string path, int size)
    {
        string full = path ?? "";
        try { full = Path.GetFullPath(full); }
        catch (Exception)
        {
            // 同上：规范化失败就用原始串当缓存键，只影响命中率（相对路径按不同 cwd 解析时
            // 本来也会分开缓存），不影响缩略图本身能否取到。
        }

        return $"{size}|{full.ToLowerInvariant()}";
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
                catch (Exception)
                {
                    // 扩展名解析失败按「无扩展名」处理，下面回退到通用文件图标；
                    // 每次列表刷新都会走，不值得为它写日志。
                }

                target = string.IsNullOrEmpty(ext) ? "file" : $"x{ext}";
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
            Log.Warn($"Shell 图标读取失败({path}): {ex.Message}");
            return null;
        }
    }

    static ImageSource LoadThumbnail(string path, int size)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            Guid iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (factory is null) return null;

            IntPtr hBitmap = IntPtr.Zero;
            int hr = factory.GetImage(new SIZE { cx = size, cy = size },
                                      SIIGBF.THUMBNAILONLY | SIIGBF.RESIZETOFIT, out hBitmap);
            if (hr < 0)
            {
                // S-15：以前这里声明成 void，HRESULT 被直接丢弃，失败与「没有缩略图」同形；
                // 现在显式判失败。失败时 out 句柄的内容由 Shell 决定、不保证为 0，
                // 因此绝不能把它当位图用（会访问非法 GDI 对象），统一释放掉。
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                _thumbnailFailure.Failure($"{path} HRESULT=0x{hr:X8}");
                return null;
            }

            if (hBitmap == IntPtr.Zero)
            {
                // 正常情形：文本 / 部分归档没有缩略图。只做恢复打点，不逐次记日志。
                _thumbnailFailure.Success();
                return null;
            }

            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                if (source.CanFreeze) source.Freeze();
                _thumbnailFailure.Success();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch (Exception ex)
        {
            // 走到这里说明 COM 激活 / 建图本身失败（不是「没有缩略图」），必须留痕。
            _thumbnailFailure.Failure($"{path} {ex.Message}");
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
        /// <summary>
        /// S-15：必须保留签名拿 HRESULT。声明成 <c>void</c> 时失败码无人接收，
        /// 屏上表现就是「有的文件没缩略图」，和真正的取图失败无法区分，
        /// 且失败时 out 句柄的内容未定义。
        /// </summary>
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
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
        readonly BlockingCollection<Action> _queue = new();
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
                catch (Exception ex)
                {
                    // X-9：后台图标任务失败过去完全静默，界面只会少一个图标。
                    Log.Warn($"Shell 图标任务失败: {ex.Message}");
                }
            }
        }
    }
}
