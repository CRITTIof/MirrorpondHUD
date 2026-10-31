using System;
using System.IO;
using System.Text;
using MirrorpondHUD.Cards;

namespace MirrorpondHUD.Core
{
    /// <summary>设置文件读写。优先放在 exe 同目录（便携），不可写时退回 %LOCALAPPDATA%。</summary>
    public static class ConfigStore
    {
        static string _path;

        /// <summary>最近一次加载设置的警告（损坏 / 从备份恢复）。正常加载为空。</summary>
        public static string LastLoadWarning { get; private set; } = "";

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
                        "MirrorpondHUD");
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
            LastLoadWarning = "";

            Settings settings = new Settings();
            bool migrated = false;
            bool corrupted = false;
            bool recovered = false;

            string file = Path;
            if (File.Exists(file))
            {
                if (!TryReadSettings(file, out settings, out migrated, out corrupted))
                {
                    corrupted = true;
                }

                if (corrupted)
                {
                    TryBackupBroken();
                    Log.Warn("设置文件损坏，已备份为 .broken-*");
                    LastLoadWarning = "设置文件损坏，原文件已备份为 .broken-*。";

                    Settings backupSettings;
                    bool backupMigrated;
                    if (TryLoadBackup(out backupSettings, out backupMigrated))
                    {
                        settings = backupSettings;
                        migrated = backupMigrated;
                        recovered = true;
                        LastLoadWarning += " 已从 settings.json.bak 恢复。";
                        Log.Warn("已从 settings.json.bak 恢复设置");
                    }
                    else
                    {
                        settings = new Settings();
                        LastLoadWarning += " 没有可用的备份，已使用默认设置。";
                    }
                }
            }

            // 必须在 Normalize 之前检测：Normalize 会把明文 Key 就地加密，之后就看不出来了。
            bool plainKey = HasPlainApiKey(settings);

            try
            {
                settings.Normalize();
                CardModel.Normalize(settings);
            }
            catch (Exception ex)
            {
                Log.Error("设置归一化失败，使用默认设置", ex);
                TryBackupBroken();
                settings = new Settings();
                plainKey = false;
                try
                {
                    settings.Normalize();
                    CardModel.Normalize(settings);
                }
                catch (Exception retry)
                {
                    Log.Error("默认设置归一化也失败", retry);
                }

                corrupted = true;
                LastLoadWarning = "设置归一化失败，已使用默认设置；原文件已备份为 .broken-*。";
            }

            // 旧版单窗口配置 / 初版多卡片配置：迁移后立刻落盘，避免每次启动重复迁移。
            // 旧版明文 API Key 同理：Normalize 已经把它换成 dpapi:…。
            // plainKey 用归一化之前的结果，确保这次保存不会生成含明文的 .bak。

            // 以前版本迁移后可能把明文旧文件留在 .bak；本次启动顺手清掉。
            ScrubPlaintextBackup();

            // 从 .bak 恢复时不要再把损坏的主文件复制回 .bak；明文 Key 迁移时也不能留下含明文的旧备份。
            bool keepBackup = !(plainKey || recovered);
            bool deletePlaintextBackup = plainKey;
            if (migrated || plainKey || recovered || corrupted)
                Save(settings, keepBackup, deletePlaintextBackup);

            return settings;
        }

        /// <summary>旧版本可能把明文 Key 的旧文件备份成 .bak；发现就删掉，别让“加密迁移”留下明文尾巴。</summary>
        static void ScrubPlaintextBackup()
        {
            string backup = Path + ".bak";
            if (!File.Exists(backup)) return;

            try
            {
                string text = File.ReadAllText(backup, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return;

                bool needsSave;
                bool corrupted;
                Settings old = SettingsMigration.Load(text, out needsSave, out corrupted);
                if (corrupted || old == null || !HasPlainApiKey(old)) return;

                File.Delete(backup);
                Log.Warn("检测到 settings.json.bak 里有明文 API Key，已删除旧备份");
            }
            catch (Exception ex)
            {
                Log.Warn("检查旧设置备份失败: " + ex.Message);
            }
        }

        static bool TryReadSettings(string file, out Settings settings, out bool migrated, out bool corrupted)
        {
            settings = new Settings();
            migrated = false;
            corrupted = false;

            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return true; // 空文件按“没有配置”处理

                bool needsSave;
                bool bad;
                settings = SettingsMigration.Load(text, out needsSave, out bad);
                migrated = needsSave;
                corrupted = bad;
                return !bad;
            }
            catch (Exception ex)
            {
                Log.Warn("读取设置失败: " + ex.Message);
                return false;
            }
        }

        static bool TryLoadBackup(out Settings settings, out bool migrated)
        {
            settings = null;
            migrated = false;

            string backup = Path + ".bak";
            if (!File.Exists(backup)) return false;

            try
            {
                string text = File.ReadAllText(backup, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return false;

                bool needsSave;
                bool bad;
                Settings loaded = SettingsMigration.Load(text, out needsSave, out bad);
                if (bad) return false;

                settings = loaded;
                migrated = needsSave;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("读取设置备份失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>配置文件里还有明文的 AI Key（2.4.0 及更早的版本是明文存的）。</summary>
        static bool HasPlainApiKey(Settings settings)
        {
            if (settings == null || settings.Cards == null) return false;

            foreach (CardSettings card in settings.Cards)
            {
                if (card == null) continue;

                ChatSettings chat = card.ExtrasAs<ChatSettings>("chat");
                if (string.IsNullOrEmpty(chat.ApiKey)) continue;
                if (!SecretStore.IsProtected(chat.ApiKey)) return true;
            }

            return false;
        }

        public static bool Save(Settings settings)
        {
            return Save(settings, true, false);
        }

        static bool Save(Settings settings, bool backup, bool deleteBackup)
        {
            if (settings == null) return false;

            string tmp = null;
            try
            {
                settings.Normalize();
                CardModel.Normalize(settings);

                string file = Path;
                string dir = System.IO.Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                tmp = file + ".tmp";
                WriteDurable(tmp, Json.Serialize(settings));

                if (File.Exists(file))
                {
                    if (backup)
                    {
                        string bak = file + ".bak";
                        try
                        {
                            File.Replace(tmp, file, bak, true);
                        }
                        catch (Exception ex)
                        {
                            // 某些文件系统 / 杀软会拒绝 Replace：退回“先备份再覆盖”，至少不丢主文件。
                            Log.Warn("设置原子替换失败，回退到备份+覆盖: " + ex.Message);
                            try { File.Copy(file, bak, true); }
                            catch { /* 备份失败不影响主流程 */ }
                            File.Move(tmp, file, true);
                        }
                    }
                    else
                    {
                        // 明文 Key 迁移 / 从备份恢复：默认不生成新 .bak；只有确认旧备份含明文时才删。
                        if (deleteBackup)
                        {
                            try { if (File.Exists(file + ".bak")) File.Delete(file + ".bak"); }
                            catch { }
                        }
                        File.Move(tmp, file, true);
                    }
                }
                else
                {
                    File.Move(tmp, file, true);
                }

                tmp = null;
                Log.Info("设置已保存: " + file);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败", ex);
                return false;
            }
            finally
            {
                if (tmp != null)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }

        /// <summary>写到临时文件并尽量落盘（WriteThrough + Flush(true)）。</summary>
        static void WriteDurable(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                                               4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                {
                    writer.Write(text ?? "");
                    writer.Flush();
                }
                stream.Flush(true);
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
