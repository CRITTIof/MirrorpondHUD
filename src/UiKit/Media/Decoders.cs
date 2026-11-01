using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls.Media
{
    /// <summary>在机器上找到的一个解码器 / 播放器。</summary>
    public sealed class DecoderInfo
    {
        /// <summary>显示名：FFmpeg / ffprobe / mpv / VLC。</summary>
        public string Kind = "";
        /// <summary>可执行文件完整路径。</summary>
        public string Path = "";
        /// <summary>版本行（取不到时为空）。</summary>
        public string Version = "";
        /// <summary>能不能把视频解成逐帧原始像素（只有 ffmpeg 行）。</summary>
        public bool Frames;

        public string Label
        {
            get
            {
                string version = string.IsNullOrWhiteSpace(Version) ? "" : " " + Version;
                return Kind + version;
            }
        }

        public override string ToString() { return Label; }
    }

    /// <summary>
    /// 解码器探测：先在用户指定路径里找，再扫 PATH，最后看几个常见的安装目录。
    ///
    /// 只有 ffmpeg 能把任意格式解成逐帧 BGRA 交给卡片自己画，所以「自定义解码器」
    /// 实际用的就是它；mpv / VLC 只作为「机器上装了什么」的信息列出来。
    /// 结果缓存 5 分钟，避免每次打开设置页都去启动进程问版本。
    /// </summary>
    public static class Decoders
    {
        static readonly object Gate = new object();        // 只保护缓存字段
        static readonly object ProbeLock = new object();   // 串行化「真正跑进程」的探测
        static List<DecoderInfo> _cached;
        static DateTime _cachedAt = DateTime.MinValue;
        static Task _warming;
        static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

        // 审阅 §21：FindFfmpeg 会在 UI 线程被 Bind / TryStartVideo / LoadImage / StartPoster
        // 同步调用；PATH 里有断开的 UNC / 网络盘时每次扫 PATH 都可能卡数秒。
        // 这里缓存“自动找到的 ffmpeg”，60s 后允许重扫（用户可能刚装好 ffmpeg），
        // 手动「检测」和后台预热会立即刷新。
        static string _autoFfmpeg;
        static bool _autoFfmpegSearched;
        static DateTime _autoFfmpegAt = DateTime.MinValue;
        static readonly TimeSpan AutoFfmpegCacheFor = TimeSpan.FromSeconds(60);

        // 单个版本进程的整体超时；超时由 AsyncProcess 直接 Kill(true)。
        const int VersionTimeoutMs = 1500;

        static readonly string[] LookNames =
        {
            "ffmpeg.exe", "ffprobe.exe", "mpv.exe", "vlc.exe"
        };

        /// <summary>PATH 之外常见的安装位置（安装包 / 包管理器默认目录）。</summary>
        static readonly string[] CommonPaths =
        {
            @"%ProgramFiles%\ffmpeg\bin\ffmpeg.exe",
            @"%ProgramFiles(x86)%\ffmpeg\bin\ffmpeg.exe",
            @"%ProgramData%\chocolatey\bin\ffmpeg.exe",
            @"%ChocolateyInstall%\bin\ffmpeg.exe",
            @"%USERPROFILE%\scoop\shims\ffmpeg.exe",
            @"%LOCALAPPDATA%\Microsoft\WinGet\Links\ffmpeg.exe",
            @"%LOCALAPPDATA%\Programs\ffmpeg\bin\ffmpeg.exe",
            @"%ProgramFiles%\VideoLAN\VLC\vlc.exe",
            @"%ProgramFiles(x86)%\VideoLAN\VLC\vlc.exe",
            @"%ProgramFiles%\mpv\mpv.exe",
            @"%LOCALAPPDATA%\Programs\mpv\mpv.exe"
        };

        /// <summary>
        /// 已找到的解码器列表（按 ffmpeg → ffprobe → mpv → VLC 排序）。
        ///
        /// force = true：设置页「检测」按钮，阻塞扫一遍；每个进程都有整体超时 +
        /// Kill(true)，总耗时约等于最慢的一个（并行探测）。
        /// force = false：**绝不阻塞**。缓存有效直接返回；缓存过期/为空时投递一次
        /// 后台预热，本次先返回旧缓存（没有就返回空表）。
        ///
        /// 为什么要这么分：非强制路径会被 MediaCardSettingsView.DecoderHint 这个
        /// **属性 getter** 在 UI 线程调用（P1-8 的报告点）。只要探测可能卡住，
        /// 在 getter 里同步等就会冻住整个 HUD，所以这里改成缓存 + 后台刷新。
        /// 这个类里所有阻塞调用都只允许出现在后台线程上。
        /// </summary>
        public static List<DecoderInfo> Detect(bool force)
        {
            if (force) return DetectBlocking(true);

            List<DecoderInfo> cached;
            bool fresh;
            lock (Gate)
            {
                cached = _cached;
                fresh = cached != null && DateTime.UtcNow - _cachedAt < CacheFor;
            }

            if (fresh) return cached;

            WarmUp();
            return cached ?? new List<DecoderInfo>();
        }

        public static List<DecoderInfo> Detect() { return Detect(false); }

        /// <summary>只读缓存：不在属性 getter 里启动进程；没预热过就返回空表。</summary>
        public static List<DecoderInfo> Cached
        {
            get
            {
                lock (Gate)
                {
                    return _cached ?? new List<DecoderInfo>();
                }
            }
        }

        /// <summary>缓存是否已经完成过至少一轮探测（首启空缓存 ≠ 真的没找到）。</summary>
        public static bool HasResult
        {
            get { lock (Gate) { return _cached != null; } }
        }

        /// <summary>
        /// 是否正在后台检测。审阅 §18：首启空缓存时设置页应显示“正在检测…”，
        /// 而不是误报“没找到 ffmpeg / mpv / VLC”。
        /// </summary>
        public static bool IsDetecting
        {
            get
            {
                lock (Gate)
                {
                    if (_cached != null) return false;
                    return _warming != null && !_warming.IsCompleted;
                }
            }
        }

        /// <summary>后台预热缓存，不阻塞调用方；可以安全地重复调用。</summary>
        public static void WarmUp()
        {
            _ = WarmUpAsync();
        }

        /// <summary>后台预热并返回同一个任务；设置页可 await 后刷新提示。</summary>
        public static Task WarmUpAsync()
        {
            lock (Gate)
            {
                if (_warming != null && !_warming.IsCompleted) return _warming;
                _warming = Task.Run(() => { try { DetectBlocking(false); } catch { } });
                return _warming;
            }
        }

        /// <summary>异步探测：给以后能改成 await 的调用方用。</summary>
        public static async Task<List<DecoderInfo>> DetectAsync(bool force)
        {
            return await Task.Run(() => DetectBlocking(force)).ConfigureAwait(false);
        }

        static List<DecoderInfo> DetectBlocking(bool force)
        {
            // 同一时刻只允许一轮探测，避免设置页连点「检测」时同时开一堆进程。
            lock (ProbeLock)
            {
                lock (Gate)
                {
                    if (!force && _cached != null && DateTime.UtcNow - _cachedAt < CacheFor)
                        return _cached;
                }

                var found = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in LookNames)
                {
                    string path = Find(name, null);
                    if (path == null || !seen.Add(path)) continue;
                    found.Add(path);
                }

                // 后台探测本来就读了一遍 PATH：顺手刷新 FindFfmpeg 的自动查找缓存，
                // 免得 UI 线程之后又同步扫一遍（审阅 §21）。
                string autoFfmpeg = null;
                for (int i = 0; i < found.Count; i++)
                {
                    if (string.Equals(Path.GetFileName(found[i]), "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        autoFfmpeg = found[i];
                        break;
                    }
                }
                lock (Gate)
                {
                    _autoFfmpeg = autoFfmpeg;
                    _autoFfmpegSearched = true;
                    _autoFfmpegAt = DateTime.UtcNow;
                }

                // 版本号并行探测：每个进程都有自己的整体超时 + Kill(true)，
                // 总耗时约等于最慢的那个，不会因为 4 个串行拖成好几秒。
                var versions = new string[found.Count];
                var tasks = new Task[found.Count];
                for (int i = 0; i < found.Count; i++)
                {
                    int index = i;
                    string exe = found[index];
                    tasks[index] = Task.Run(() => { versions[index] = ReadVersion(exe); });
                }
                try { Task.WaitAll(tasks); } catch { }

                var list = new List<DecoderInfo>(found.Count);
                for (int i = 0; i < found.Count; i++)
                {
                    string path = found[i];
                    string file = Path.GetFileName(path) ?? "";
                    list.Add(new DecoderInfo
                    {
                        Kind = KindName(file),
                        Path = path,
                        Version = versions[i] ?? "",
                        Frames = string.Equals(file, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                    });
                }

                lock (Gate)
                {
                    _cached = list;
                    _cachedAt = DateTime.UtcNow;
                }

                Log.Info("解码器探测：" + (list.Count == 0
                    ? "PATH 与常见目录里没找到 ffmpeg / mpv / VLC"
                    : Describe(list)));
                return list;
            }
        }

        /// <summary>一句话描述探测结果（设置页提示与日志共用）。</summary>
        public static string Describe(List<DecoderInfo> list)
        {
            if (list == null || list.Count == 0) return "没找到 ffmpeg / mpv / VLC";

            var text = new StringBuilder();
            foreach (DecoderInfo info in list)
            {
                if (text.Length > 0) text.Append("；");
                text.Append(info.Label);
            }
            return text.ToString();
        }

        /// <summary>
        /// 取用于解码的 ffmpeg：配置里填了路径就先用它，否则自动找。
        /// 返回 null = 这台机器没有 ffmpeg，只能用系统自带的 Media Foundation。
        /// </summary>
        public static string FindFfmpeg(MediaSettings settings)
        {
            string custom = settings == null ? "" : (settings.DecoderPath ?? "").Trim().Trim('"');
            if (custom.Length > 0)
            {
                if (File.Exists(custom)) return custom;
                Log.Warn("自定义解码器不存在：" + custom + "，改回自动查找");
            }

            // 自动查找结果做短缓存：这个方法是 UI 线程同步调用的，PATH 有断开的网络盘时
            // 每扫一次都可能卡数秒（审阅 §21）。60s 允许重扫，手动「检测」/ 预热会立即刷新。
            string cached;
            bool fresh;
            lock (Gate)
            {
                cached = _autoFfmpeg;
                fresh = _autoFfmpegSearched && DateTime.UtcNow - _autoFfmpegAt < AutoFfmpegCacheFor;
            }
            if (fresh) return cached;

            string found = Find("ffmpeg.exe", null);
            lock (Gate)
            {
                _autoFfmpeg = found;
                _autoFfmpegSearched = true;
                _autoFfmpegAt = DateTime.UtcNow;
            }

            // 顺手预热解码器缓存（后台线程，不阻塞）：设置页的 DecoderHint 是属性 getter，
            // 只要缓存已经就绪就不会出现“没找到解码器”的假提示。
            WarmUp();
            return found;
        }

        /// <summary>只在 PATH 与常见目录里找，不看用户设置。</summary>
        public static string Find(string name)
        {
            return Find(name, null);
        }

        /// <summary>按「自定义路径 → PATH → 常见目录」的顺序找一个可执行文件。</summary>
        public static string Find(string name, string preferred)
        {
            if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
                return preferred;

            string fromPath = SearchPath(name);
            if (fromPath != null) return fromPath;

            foreach (string raw in CommonPaths)
            {
                if (!string.Equals(Path.GetFileName(raw), name, StringComparison.OrdinalIgnoreCase)) continue;

                string expanded = Expand(raw);
                if (expanded != null && File.Exists(expanded)) return expanded;
            }

            return null;
        }

        static string SearchPath(string name)
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar)) return null;

            foreach (string raw in pathVar.Split(';'))
            {
                string dir = (raw ?? "").Trim().Trim('"');
                if (dir.Length == 0) continue;

                try
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            return null;
        }

        static string Expand(string raw)
        {
            try
            {
                string text = Environment.ExpandEnvironmentVariables(raw);
                return text.IndexOf('%') >= 0 ? null : text;   // 变量没展开 = 这台机器没有
            }
            catch { return null; }
        }

        static string KindName(string exe)
        {
            switch (exe.ToLowerInvariant())
            {
                case "ffmpeg.exe": return "FFmpeg";
                case "ffprobe.exe": return "ffprobe";
                case "mpv.exe": return "mpv";
                case "vlc.exe": return "VLC";
                default: return exe;
            }
        }

        /// <summary>
        /// 问一下版本号；拿不到就返回空串（不影响能不能用）。
        /// 只允许在后台线程调用：AsyncProcess 有整体超时（1.5s）+ Kill(true)，
        /// 这里的同步等待最多等这么久，绝不会像原来那样被 ReadToEnd 永久卡住。
        /// </summary>
        static string ReadVersion(string exe)
        {
            try
            {
                string kind = (Path.GetFileName(exe) ?? "").ToLowerInvariant();
                string arg = kind == "vlc.exe" || kind == "mpv.exe" ? "--version" : "-version";

                AsyncProcess.Result result =
                    AsyncProcess.RunAsync(exe, new[] { arg }, VersionTimeoutMs).GetAwaiter().GetResult();

                string line = FirstLine(result.StdOutText);
                if (line.Length == 0) line = FirstLine(result.StdErr);
                return Shorten(line);
            }
            catch
            {
                return "";
            }
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            return (end < 0 ? text : text.Substring(0, end)).Trim();
        }

        static string Shorten(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";

            // "ffmpeg version 6.1.1-full_build-www.gyan.dev Copyright ..." → "6.1.1-full_build..."
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[1].Equals("version", StringComparison.OrdinalIgnoreCase) &&
                (parts[0].Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("ffprobe", StringComparison.OrdinalIgnoreCase)))
                line = parts[2];
            else if (parts.Length >= 2 && parts[0].Equals("mpv", StringComparison.OrdinalIgnoreCase))
                line = parts[1];
            else if (parts.Length >= 2 && parts[0].StartsWith("VLC", StringComparison.OrdinalIgnoreCase))
                line = line.Substring(0, Math.Min(line.Length, 24));

            if (line.Length > 40) line = line.Substring(0, 39) + "…";
            return line;
        }
    }
}
