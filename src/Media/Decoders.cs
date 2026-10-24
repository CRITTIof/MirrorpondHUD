using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Media
{
    /// <summary>在机器上找到的一个解码器 / 播放器。</summary>
    internal sealed class DecoderInfo
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
    internal static class Decoders
    {
        static readonly object Gate = new object();
        static List<DecoderInfo> _cached;
        static DateTime _cachedAt = DateTime.MinValue;
        static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

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

        /// <summary>已找到的解码器列表（按 ffmpeg → ffprobe → mpv → VLC 排序）。</summary>
        public static List<DecoderInfo> Detect(bool force)
        {
            lock (Gate)
            {
                if (!force && _cached != null && DateTime.UtcNow - _cachedAt < CacheFor)
                    return _cached;

                var list = new List<DecoderInfo>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string name in LookNames)
                {
                    string path = Find(name, null);
                    if (path == null || !seen.Add(path)) continue;

                    list.Add(new DecoderInfo
                    {
                        Kind = KindName(name),
                        Path = path,
                        Version = ReadVersion(path),
                        Frames = string.Equals(name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                    });
                }

                _cached = list;
                _cachedAt = DateTime.UtcNow;

                Log.Info("解码器探测：" + (list.Count == 0
                    ? "PATH 与常见目录里没找到 ffmpeg / mpv / VLC"
                    : Describe(list)));
                return list;
            }
        }

        public static List<DecoderInfo> Detect() { return Detect(false); }

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

            return Find("ffmpeg.exe", null);
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

        /// <summary>问一下版本号；拿不到就返回空串（不影响能不能用）。</summary>
        static string ReadVersion(string exe)
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                string kind = Path.GetFileName(exe).ToLowerInvariant();
                psi.ArgumentList.Add(kind == "vlc.exe" || kind == "mpv.exe" ? "--version" : "-version");

                using (var process = Process.Start(psi))
                {
                    if (process == null) return "";

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    if (!process.WaitForExit(1500))
                    {
                        try { process.Kill(); } catch { }
                        return "";
                    }

                    string line = FirstLine(output);
                    if (line.Length == 0) line = FirstLine(error);
                    return Shorten(line);
                }
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
