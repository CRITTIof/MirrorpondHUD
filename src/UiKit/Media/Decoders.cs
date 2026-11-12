using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls.Media;

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
            string version = string.IsNullOrWhiteSpace(Version) ? "" : $" {Version}";
            return Kind + version;
        }
    }

    public override string ToString() => Label;
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
    static readonly object Gate = new();        // 只保护缓存字段
    static readonly object ProbeLock = new();   // 串行化「真正跑进程」的探测
    static List<DecoderInfo> _cached;
    static DateTime _cachedAt = DateTime.MinValue;
    static Task _warming;
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    // 审阅 §21：FindFfmpeg 会在 UI 线程被 Bind / TryStartVideo / LoadImage / StartPoster
    // 同步调用；PATH 里有断开的 UNC / 网络盘时每次扫 PATH 都可能卡数秒。
    // K-15：首次查找也改成后台进行 —— FindFfmpeg 只读缓存，未命中就投递查找并返回旧值，
    // 绝不在 UI 线程同步扫 PATH。调用方（CardMediaView）显示加载态并等查找完成后重试。
    static string _autoFfmpeg;
    static bool _autoFfmpegSearched;
    static DateTime _autoFfmpegAt = DateTime.MinValue;
    static readonly TimeSpan AutoFfmpegCacheFor = TimeSpan.FromSeconds(60);
    static Task<string> _autoLookup;

    /// <summary>K-15：等待后台查找的上限。超过就按"没有 ffmpeg"继续，避免卡住调用方。</summary>
    const int AutoLookupTimeoutMs = 3000;

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
            fresh = cached is not null && DateTime.UtcNow - _cachedAt < CacheFor;
        }

        if (fresh) return cached;

        WarmUp();
        return cached ?? new List<DecoderInfo>();
    }

    public static List<DecoderInfo> Detect() => Detect(false);

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
        get { lock (Gate) { return _cached is not null; } }
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
                if (_cached is not null) return false;
                return _warming is not null && !_warming.IsCompleted;
            }
        }
    }

    /// <summary>后台预热缓存，不阻塞调用方；可以安全地重复调用。</summary>
    public static void WarmUp() => _ = WarmUpAsync();

    /// <summary>后台预热并返回同一个任务；设置页可 await 后刷新提示。</summary>
    public static Task WarmUpAsync()
    {
        lock (Gate)
        {
            if (_warming is not null && !_warming.IsCompleted) return _warming;
            _warming = Task.Run(() =>
            {
                try { DetectBlocking(false); }
                catch (Exception ex)
                {
                    // 后台预热失败不能让设置页永远显示"检测中"；记日志并保留旧缓存。
                    Log.Warn($"后台解码器探测失败: {ex.Message}");
                }
            });
            return _warming;
        }
    }

    /// <summary>异步探测：给以后能改成 await 的调用方用。</summary>
    public static async Task<List<DecoderInfo>> DetectAsync(bool force) => await Task.Run(() => DetectBlocking(force)).ConfigureAwait(false);

    static List<DecoderInfo> DetectBlocking(bool force)
    {
        // 同一时刻只允许一轮探测，避免设置页连点「检测」时同时开一堆进程。
        lock (ProbeLock)
        {
            lock (Gate)
            {
                if (!force && _cached is not null && DateTime.UtcNow - _cachedAt < CacheFor)
                    return _cached;
            }

            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in LookNames)
            {
                string path = Find(name, null);
                if (path is null || !seen.Add(path)) continue;
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
            try { Task.WaitAll(tasks); }
            catch (Exception ex)
            {
                // ReadVersion 自己会兜住单个进程的失败；这里兜的是 Task 自身的异常
                // （例如线程池拒绝）。版本号只是展示信息，但失败原因仍要留痕。
                Log.Warn($"读取解码器版本号失败: {ex.Message}");
            }

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

            Log.Info($"解码器探测：{(list.Count == 0
                ? "PATH 与常见目录里没找到 ffmpeg / mpv / VLC"
                : Describe(list))}");
            return list;
        }
    }

    /// <summary>一句话描述探测结果（设置页提示与日志共用）。</summary>
    public static string Describe(List<DecoderInfo> list)
    {
        if (list is null || list.Count == 0) return "没找到 ffmpeg / mpv / VLC";

        var text = new StringBuilder();
        foreach (DecoderInfo info in list)
        {
            if (text.Length > 0) text.Append("；");
            text.Append(info.Label);
        }
        return text.ToString();
    }

    /// <summary>
    /// 取用于解码的 ffmpeg：配置里填了路径就先用它，否则用自动查找的缓存。
    /// 返回 null = 这一刻还没有可用的 ffmpeg（不等于这台机器没有）。
    ///
    /// K-15：这个方法在 UI 线程被调用，所以**绝不允许同步扫 PATH**：
    /// 缓存命中直接返回；未命中就投递后台查找并返回旧值（首次为 null）。
    /// 需要"等一会"的调用方用 <see cref="WaitForFfmpegAsync"/>；需要重试的用
    /// <see cref="EnsureFfmpegLookup"/> 挂 ContinueWith。
    /// </summary>
    public static string FindFfmpeg(MediaSettings settings)
    {
        string custom = settings is null ? "" : (settings.DecoderPath ?? "").Trim().Trim('"');
        if (custom.Length > 0)
        {
            if (File.Exists(custom)) return custom;
            Log.Warn($"自定义解码器不存在：{custom}，改回自动查找");
        }

        string cached;
        bool fresh;
        lock (Gate)
        {
            cached = _autoFfmpeg;
            fresh = _autoFfmpegSearched && DateTime.UtcNow - _autoFfmpegAt < AutoFfmpegCacheFor;
        }
        if (fresh) return cached;

        // 未命中：后台查找 + 缓存刷新都投递出去，本次先返回旧缓存（可能是 null）。
        EnsureFfmpegLookup();
        WarmUp();
        return cached;
    }

    /// <summary>
    /// K-15：确保 ffmpeg 自动查找在后台进行，返回同一个任务（可重复调用）。
    /// 缓存有效时返回已完成任务，因此 ContinueWith 不会立刻触发重试。
    /// </summary>
    public static Task<string> EnsureFfmpegLookup()
    {
        lock (Gate)
        {
            if (_autoFfmpegSearched && DateTime.UtcNow - _autoFfmpegAt < AutoFfmpegCacheFor)
                return Task.FromResult(_autoFfmpeg);
            if (_autoLookup is not null && !_autoLookup.IsCompleted) return _autoLookup;

            // 先查固定候选目录（本地、快），再扫 PATH（断网盘会卡，但在后台线程不冻 UI）。
            _autoLookup = Task.Run(() => LookupFfmpeg());
            return _autoLookup;
        }
    }

    /// <summary>后台查找本体：结果写入缓存，成功/失败/未找到都留日志。</summary>
    static string LookupFfmpeg()
    {
        try
        {
            string found = Find("ffmpeg.exe", null);
            lock (Gate)
            {
                _autoFfmpeg = found;
                _autoFfmpegSearched = true;
                _autoFfmpegAt = DateTime.UtcNow;
            }

            if (found is null)
                Log.Warn("后台查找 ffmpeg 完成：固定候选目录与 PATH 里都没有 ffmpeg.exe，按系统解码处理");
            else
                Log.Info($"后台查找 ffmpeg 完成：{found}");
            return found;
        }
        catch (Exception ex)
        {
            // 扫描本身失败（PATH 解析异常等）：记下原因并按"没找到"缓存，避免反复重试。
            lock (Gate)
            {
                _autoFfmpeg = null;
                _autoFfmpegSearched = true;
                _autoFfmpegAt = DateTime.UtcNow;
            }
            Log.Warn($"后台查找 ffmpeg 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// K-15：等待后台查找结果（带超时）。给"必须用 ffmpeg 才能进行"的路径用：
    /// 例如第一张 webp 图片；超时按没有 ffmpeg 处理并写日志（原因可见）。
    /// </summary>
    public static async Task<string> WaitForFfmpegAsync(int timeoutMs = AutoLookupTimeoutMs)
    {
        Task<string> lookup = EnsureFfmpegLookup();
        if (lookup.IsCompleted) return await lookup.ConfigureAwait(false);

        Task completed = await Task.WhenAny(lookup, Task.Delay(Math.Max(1, timeoutMs))).ConfigureAwait(false);
        if (!ReferenceEquals(completed, lookup))
        {
            Log.Warn($"查找 ffmpeg 超时（{timeoutMs}ms）：PATH 里可能有不可达的网络盘，本轮按无 ffmpeg 处理");
            return null;
        }

        return await lookup.ConfigureAwait(false);
    }

    /// <summary>只在 PATH 与常见目录里找，不看用户设置。</summary>
    public static string Find(string name) => Find(name, null);

    /// <summary>按「自定义路径 → 固定候选目录 → PATH」的顺序找一个可执行文件。</summary>
    public static string Find(string name, string preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
            return preferred;

        // K-15：固定候选目录先查 —— 都是本地已知路径，不会像 PATH 里断开的
        // UNC / 网络盘那样一卡几秒；PATH 放最后当兜底，而且它只会在后台线程被扫。
        foreach (string raw in CommonPaths)
        {
            if (!string.Equals(Path.GetFileName(raw), name, StringComparison.OrdinalIgnoreCase)) continue;

            string expanded = Expand(raw);
            if (expanded is not null && File.Exists(expanded)) return expanded;
        }

        return SearchPath(name);
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
            catch (ArgumentException)
            {
                // 良性：PATH 里某个目录含非法字符，Path.Combine 会抛。跳过它继续扫
                // （其它目录仍可能命中），所以不需要 Warn 刷屏。
            }
            catch (Exception ex)
            {
                // 其余异常（例如长路径 / 权限）不是常态，记一次便于排查找不到解码器的原因。
                Log.Warn($"扫描 PATH 目录失败（{dir}）: {ex.Message}");
            }
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
        catch (Exception ex)
        {
            // 环境变量格式异常（很少见）：按"这个候选路径不存在"处理，但要留痕，
            // 否则用户会只看到"没找到 ffmpeg"而不知道为什么候选目录被跳过。
            Log.Warn($"展开候选路径失败（{raw}）: {ex.Message}");
            return null;
        }
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
        catch (Exception ex)
        {
            // 版本号只是展示信息：拿不到不影响能不能用，但原因要可见（X-9）。
            Log.Warn($"读取 {exe} 版本号失败: {ex.Message}");
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

        if (line.Length > 40) line = $"{line.Substring(0, 39)}…";
        return line;
    }
}
