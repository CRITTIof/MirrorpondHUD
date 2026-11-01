using System;
using System.IO;
using System.Text;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 极简文件日志。优先写到 exe 同目录（便携、权限问题最少），
    /// 不可写时退到 %LOCALAPPDATA%\MirrorpondHUD，最后退到临时目录。
    /// 可用环境变量 MIRRORPONDHUD_LOG 指定绝对路径。
    /// </summary>
    internal static class Log
    {
        static readonly object Gate = new object();
        static string _path;
        static bool _failed;

        public static string FilePath
        {
            get
            {
                if (_path != null) return _path;
                _path = ResolvePath();
                return _path;
            }
        }

        static string ResolvePath()
        {
            string overridePath = Environment.GetEnvironmentVariable("MIRRORPONDHUD_LOG");
            if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

            // 1) exe 同目录：最直观，也不受 %LOCALAPPDATA% 被重定向的影响
            try
            {
                string exeDir = AppContext.BaseDirectory;
                if (CanWrite(exeDir)) return Path.Combine(exeDir, "mirrorpond.log");
            }
            catch { }

            // 2) %LOCALAPPDATA%
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    string dir = Path.Combine(local, "MirrorpondHUD");
                    Directory.CreateDirectory(dir);
                    if (CanWrite(dir)) return Path.Combine(dir, "mirrorpond.log");
                }
            }
            catch { }

            // 3) 临时目录
            try
            {
                string temp = Path.Combine(Path.GetTempPath(), "MirrorpondHUD");
                Directory.CreateDirectory(temp);
                return Path.Combine(temp, "mirrorpond.log");
            }
            catch { }

            return "mirrorpond.log";
        }

        static bool CanWrite(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                string probe = Path.Combine(dir, ".wo-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }

        public static void Error(string msg, Exception ex = null)
        {
            Write("ERROR", ex == null ? msg : msg + " :: " + ex.GetType().Name + ": " + ex.Message);
            if (ex != null) Write("ERROR", ex.StackTrace ?? "");
        }

        static void Write(string level, string msg)
        {
            if (_failed) return;
            try
            {
                lock (Gate)
                {
                    string path = FilePath;
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024)
                        File.Move(path, path + ".1", true);

                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { _failed = true; }
        }
    }
}
