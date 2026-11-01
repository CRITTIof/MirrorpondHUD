using System;
using System.IO;
using System.Text;

namespace MirrorpondHUD.Core
{
    /// <summary>设置文件读写。优先放在 exe 同目录（便携），不可写时退回 %LOCALAPPDATA%。</summary>
    public static class ConfigStore
    {
        static string _path;
        static readonly object SaveSync = new object();

        /// <summary>
        /// 宿主注入的卡片归一化钩子（实际是 <c>CardModel.Normalize</c>）。
        /// ConfigStore 在 Core 层，不应该为了卡片结构反过来引用 Cards；
        /// App 在启动早期（插件注册之后、第一次 Load 之前）注入这个委托。
        /// </summary>
        public static Action<Settings> SettingsNormalizer;

        static void NormalizeCards(Settings settings)
        {
            var normalizer = SettingsNormalizer;
            if (normalizer != null) normalizer(settings);
        }

        /// <summary>最近一次加载设置的警告（损坏 / 从备份恢复 / 未来版本只读）。正常加载为空。</summary>
        public static string LastLoadWarning { get; private set; } = "";

        /// <summary>配置来自更高 SchemaVersion：只读加载，Save 拒绝覆盖。</summary>
        public static bool LastLoadReadOnly { get; private set; }

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
            LastLoadReadOnly = false;

            Settings settings = new Settings();
            bool migrated = false;
            bool corrupted = false;
            bool recovered = false;
            bool future = false;
            bool empty = false;

            string file = Path;
            bool exists = File.Exists(file);
            if (exists)
            {
                if (!TryReadSettings(file, out settings, out migrated, out corrupted, out empty, out future))
                    corrupted = true;

                if (corrupted || empty)
                {
                    if (corrupted)
                    {
                        TryBackupBroken();
                        Log.Warn("设置文件损坏，已备份为 .broken-*");
                        LastLoadWarning = "设置文件损坏，原文件已备份为 .broken-*。";
                    }
                    else
                    {
                        LastLoadWarning = "设置文件为空。";
                    }

                    Settings backupSettings;
                    bool backupMigrated;
                    bool backupFuture;
                    if (TryLoadBackup(out backupSettings, out backupMigrated, out backupFuture))
                    {
                        settings = backupSettings;
                        migrated = backupMigrated;
                        future = backupFuture;
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
            else
            {
                // 主文件缺失（杀软隔离 / OneDrive 占位 / 误删）：不要直接覆盖成默认值，先看 .bak。
                Settings backupSettings;
                bool backupMigrated;
                bool backupFuture;
                if (TryLoadBackup(out backupSettings, out backupMigrated, out backupFuture))
                {
                    settings = backupSettings;
                    migrated = backupMigrated;
                    future = backupFuture;
                    recovered = true;
                    LastLoadWarning = "settings.json 不存在，已从 settings.json.bak 恢复。";
                    Log.Warn("settings.json 不存在，已从 .bak 恢复");
                }
            }

            if (future)
            {
                LastLoadReadOnly = true;
                LastLoadWarning = "设置来自更高版本，已只读加载；不会覆盖原文件。";
                Log.Warn("设置来自更高 SchemaVersion，只读加载");
            }

            // 必须在 Normalize 之前检测：Normalize 会把明文 Key 就地加密，之后就看不出来了。
            bool plainKey = HasPlainApiKey(settings);

            try
            {
                settings.Normalize();
                NormalizeCards(settings);
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
                    NormalizeCards(settings);
                }
                catch (Exception retry)
                {
                    Log.Error("默认设置归一化也失败", retry);
                }

                corrupted = true;
                LastLoadWarning = "设置归一化失败，已使用默认设置；原文件已备份为 .broken-*。";
            }

            // 明文 Key 迁移 / 损坏恢复后立刻落盘，避免每次启动重复处理。
            // 从 .bak 恢复时不要再把损坏的主文件复制回 .bak；明文 Key 迁移时不生成含明文的旧备份。
            if (!LastLoadReadOnly)
            {
                bool keepBackup = !(plainKey || recovered);
                if (migrated || plainKey || recovered || corrupted || empty)
                {
                    if (Save(settings, keepBackup))
                    {
                        // 保存成功后再清洗旧备份：先删后保存会在磁盘满/占用时丢掉唯一恢复副本。
                        ScrubPlaintextArtifacts();
                    }
                }
            }

            return settings;
        }

        /// <summary>保存成功后清洗旧 `.bak` / `.broken-*` / `.tmp` 里残留的明文 Key。</summary>
        static void ScrubPlaintextArtifacts()
        {
            string file = Path;
            string dir = System.IO.Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            string name = System.IO.Path.GetFileName(file);
            try
            {
                foreach (string candidate in Directory.EnumerateFiles(dir, name + "*"))
                {
                    if (string.Equals(candidate, file, StringComparison.OrdinalIgnoreCase)) continue;

                    string suffix = candidate.Substring(file.Length);
                    bool artifact = suffix == ".bak" || suffix == ".tmp" ||
                                    suffix.StartsWith(".broken-", StringComparison.OrdinalIgnoreCase);
                    if (!artifact) continue;

                    if (!ContainsPlaintextApiKey(candidate)) continue;

                    File.Delete(candidate);
                    Log.Warn("已删除含明文 API Key 的旧设置文件: " + System.IO.Path.GetFileName(candidate));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("清洗旧设置备份失败: " + ex.Message);
            }
        }

        static bool ContainsPlaintextApiKey(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return false;

                bool needsSave;
                bool corrupted;
                bool future;
                Settings old = SettingsMigration.Load(text, out needsSave, out corrupted, out future);
                return !corrupted && old != null && HasPlainApiKey(old);
            }
            catch
            {
                return false;
            }
        }

        static bool TryReadSettings(string file, out Settings settings, out bool migrated,
                                    out bool corrupted, out bool empty, out bool future)
        {
            settings = new Settings();
            migrated = false;
            corrupted = false;
            empty = false;
            future = false;

            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                {
                    empty = true;
                    return true;
                }

                bool needsSave;
                bool bad;
                bool isFuture;
                settings = SettingsMigration.Load(text, out needsSave, out bad, out isFuture);
                migrated = needsSave;
                corrupted = bad;
                future = isFuture;
                return !bad;
            }
            catch (Exception ex)
            {
                Log.Warn("读取设置失败: " + ex.Message);
                return false;
            }
        }

        static bool TryLoadBackup(out Settings settings, out bool migrated, out bool future)
        {
            settings = null;
            migrated = false;
            future = false;

            string backup = Path + ".bak";
            if (!File.Exists(backup)) return false;

            try
            {
                string text = File.ReadAllText(backup, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return false;

                bool needsSave;
                bool bad;
                bool isFuture;
                Settings loaded = SettingsMigration.Load(text, out needsSave, out bad, out isFuture);
                if (bad) return false;

                settings = loaded;
                migrated = needsSave;
                future = isFuture;
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
            return Save(settings, true);
        }

        static bool Save(Settings settings, bool backup)
        {
            // 并发 Save 共用 settings.json.tmp：串行化，避免互相删掉正在写的临时文件。
            lock (SaveSync)
            {
                return SaveCore(settings, backup);
            }
        }

        static bool SaveCore(Settings settings, bool backup)
        {
            if (settings == null) return false;
            if (LastLoadReadOnly)
            {
                Log.Warn("配置来自更高版本，只读模式拒绝保存，避免降级覆盖");
                return false;
            }

            string tmp = null;
            try
            {
                settings.Normalize();
                NormalizeCards(settings);

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
                            // 某些文件系统 / 杀软会拒绝 Replace：退回“先备份再覆盖”。
                            // 备份失败必须中止，绝不能无备份覆盖主文件。
                            Log.Warn("设置原子替换失败，回退到备份+覆盖: " + ex.Message);
                            try
                            {
                                File.Copy(file, bak, true);
                            }
                            catch (Exception copyEx)
                            {
                                Log.Error("设置回退备份失败，已中止保存", copyEx);
                                return false;
                            }

                            File.Move(tmp, file, true);
                        }
                    }
                    else
                    {
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
