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
        /// <summary>这张卡片的状态目录（不存在时返回路径，不创建）。Id 非法时返回 null。</summary>
        public static string DirectoryFor(string cardId)
        {
            string id = SanitizeId(cardId);
            if (id == null) return null;

            string settings = ConfigStore.Path;
            string root = Path.Combine(Path.GetDirectoryName(settings) ?? ".", "cards");
            return Path.Combine(root, id);
        }

        /// <summary>读状态文件；不存在 / 名字非法 / 读失败都返回 null。</summary>
        public static string Read(string cardId, string fileName)
        {
            string path = Resolve(cardId, fileName);
            if (path == null) return null;

            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }
            catch (Exception ex)
            {
                Log.Warn("读取卡片状态失败(" + fileName + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>原子写状态文件（先写 .tmp 再替换）。</summary>
        public static bool Write(string cardId, string fileName, string text)
        {
            string path = Resolve(cardId, fileName);
            if (path == null) return false;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text ?? "", new UTF8Encoding(false));

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("写入卡片状态失败(" + fileName + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>删掉一个状态文件（不存在也算成功）。</summary>
        public static bool Delete(string cardId, string fileName)
        {
            string path = Resolve(cardId, fileName);
            if (path == null) return false;

            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("删除卡片状态失败(" + fileName + "): " + ex.Message);
                return false;
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
