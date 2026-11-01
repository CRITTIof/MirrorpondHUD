using System;
using System.IO;
using System.Text;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 每张卡片自己的状态文件（中转站的文件列表、画板的草稿……）。
    ///
    /// 位置：<c>&lt;settings.json 所在目录&gt;\cards\&lt;卡片 Id&gt;\</c>——和配置一起便携，
    /// 不塞进 <c>settings.json</c>（否则配置越读越大，还会和 Normalize 打架）。
    /// 写入走「临时文件 + 替换」，中途崩溃不会留下半个文件。
    /// </summary>
    public static class CardStateStore
    {
        static readonly object WriteSync = new object();
        /// <summary>这张卡片的状态目录（不存在时返回路径，不创建）。Id 非法时返回 null。</summary>
        public static string DirectoryFor(string cardId)
        {
            string id = SanitizeId(cardId);
            if (id == null) return null;

            string settings = ConfigStore.Path;
            string root = Path.Combine(Path.GetDirectoryName(settings) ?? ".", "cards");
            return Path.Combine(root, id);
        }

        /// <summary>读状态文件；主文件不存在或不是合法 JSON 时回退 .bak；都失败返回 null。</summary>
        public static string Read(string cardId, string fileName)
        {
            string path = Resolve(cardId, fileName);
            if (path == null) return null;

            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    if (LooksLikeJson(text)) return text;
                    Log.Warn("卡片状态主文件损坏，尝试 .bak(" + fileName + ")");
                }

                string backup = path + ".bak";
                if (File.Exists(backup))
                {
                    string text = File.ReadAllText(backup, Encoding.UTF8);
                    if (LooksLikeJson(text))
                    {
                        Log.Warn("已从卡片状态 .bak 恢复(" + fileName + ")");
                        return text;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("读取卡片状态失败(" + fileName + "): " + ex.Message);
                return null;
            }
        }

        static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                Json.Parse(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>原子写状态文件（临时文件 + Replace），并保留上一版 .bak。</summary>
        public static bool Write(string cardId, string fileName, string text)
        {
            string path = Resolve(cardId, fileName);
            if (path == null) return false;

            lock (WriteSync)
            {
                return WriteCore(path, text);
            }
        }

        static bool WriteCore(string path, string text)
        {
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                WriteDurable(tmp, text ?? "");

                if (File.Exists(path))
                {
                    string backup = path + ".bak";
                    try
                    {
                        File.Replace(tmp, path, backup, true);
                    }
                    catch (Exception ex)
                    {
                        // Replace 失败时先备份再覆盖；备份失败必须中止，不能无备份覆盖。
                        Log.Warn("卡片状态原子替换失败，回退到备份+覆盖: " + ex.Message);
                        try
                        {
                            File.Copy(path, backup, true);
                        }
                        catch (Exception copyEx)
                        {
                            Log.Error("卡片状态回退备份失败，已中止写入(" + fileNameOf(path) + ")", copyEx);
                            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                            return false;
                        }

                        File.Move(tmp, path, true);
                    }
                }
                else
                {
                    File.Move(tmp, path, true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("写入卡片状态失败(" + fileNameOf(path) + "): " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }

        static string fileNameOf(string path)
        {
            return Path.GetFileName(path);
        }

        /// <summary>删掉一个状态文件（连同 .bak；不存在也算成功）。</summary>
        public static bool Delete(string cardId, string fileName)
        {
            string path = Resolve(cardId, fileName);
            if (path == null) return false;

            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("删除卡片状态失败(" + fileName + "): " + ex.Message);
                return false;
            }
        }

        static void WriteDurable(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                                               4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                {
                    writer.Write(text);
                    writer.Flush();
                }
                stream.Flush(true);
            }
        }

        static string Resolve(string cardId, string fileName)
        {
            string dir = DirectoryFor(cardId);
            if (dir == null || !IsSafeFileName(fileName)) return null;
            return Path.Combine(dir, fileName);
        }

        /// <summary>卡片 Id 只允许字母 / 数字 / 短横线 / 下划线；其余一律拒绝，避免路径穿越。</summary>
        static string SanitizeId(string cardId)
        {
            string id = (cardId ?? "").Trim();
            if (id.Length == 0 || id.Length > 64) return null;

            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return null;
            }

            return id;
        }

        /// <summary>状态文件名：单层文件名，不含目录分隔符，也不允许 ".."。</summary>
        static bool IsSafeFileName(string fileName)
        {
            string name = (fileName ?? "").Trim();
            if (name.Length == 0 || name.Length > 100) return false;
            if (name == "." || name == "..") return false;

            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return false;
            if (name.IndexOf(':') >= 0) return false;
            return true;
        }
    }
}
