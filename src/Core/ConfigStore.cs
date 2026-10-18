using System;
using System.IO;
using System.Text;

namespace WindowOverlay.Core
{
    /// <summary>设置文件读写。优先放在 exe 同目录（便携），不可写时退回 %LOCALAPPDATA%。</summary>
    public static class ConfigStore
    {
        static string _path;

        public static string Path
        {
            get
            {
                if (_path != null) return _path;

                string portable = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");
                if (CanWrite(AppContext.BaseDirectory))
                {
                    _path = portable;
                }
                else
                {
                    string dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WindowOverlay");
                    Directory.CreateDirectory(dir);
                    _path = System.IO.Path.Combine(dir, "settings.json");
                }
                return _path;
            }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string probe = System.IO.Path.Combine(dir, ".write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                string file = Path;
                if (File.Exists(file))
                {
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Settings loaded = Json.Deserialize<Settings>(text);
                        if (loaded != null) settings = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取设置失败，使用默认值: " + ex.Message);
                TryBackupBroken();
            }

            settings.Normalize();
            return settings;
        }

        public static bool Save(Settings settings)
        {
            try
            {
                settings.Normalize();
                string file = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));

                if (File.Exists(file))
                {
                    try { File.Copy(file, file + ".bak", true); }
                    catch { /* 备份失败不影响主流程 */ }
                }

                string tmp = file + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
                Log.Info("设置已保存: " + file);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败", ex);
                return false;
            }
        }

        static void TryBackupBroken()
        {
            try
            {
                string file = Path;
                if (File.Exists(file))
                {
                    string bad = file + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Copy(file, bad, true);
                    Log.Warn("已备份损坏的配置文件到 " + bad);
                }
            }
            catch { }
        }
    }
}
