using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 文件操作：打开 / 定位 / 复制路径 / 打开接收目录 / 不可逆删除 / 移除丢失项。
    /// 从 StationView 抽出后，视图只负责选择、列表与状态行回填；所有用户可见文案保持原样。
    /// </summary>
    internal sealed class StationFileActions
    {
        readonly ICardHost _host;
        readonly StationSettings _station;
        readonly Action<string, CardStatusKind> _setStatus;
        readonly Action _persistAndRebuild;

        public StationFileActions(ICardHost host, StationSettings station,
                                  Action<string, CardStatusKind> setStatus, Action persistAndRebuild)
        {
            _host = host;
            _station = station;
            _setStatus = setStatus;
            _persistAndRebuild = persistAndRebuild;
        }

        /// <summary>打开条目；文件已不存在时把 Missing 标回列表并提示，不再尝试 Shell 打开。</summary>
        public void Open(StationItem item)
        {
            if (item == null) return;

            string path = item.DisplayPath;
            if (!item.PathExists())
            {
                item.Missing = true;
                _persistAndRebuild();
                _setStatus("文件已不存在：" + path, CardStatusKind.Error);
                return;
            }

            try { if (_host != null) _host.Shell.OpenPath(path); }
            catch (Exception ex) { _setStatus("打开失败：" + ex.Message, CardStatusKind.Error); }
        }

        /// <summary>在资源管理器里定位；文件已不存在时静默返回（列表不会因此被改写）。</summary>
        public void Reveal(StationItem item)
        {
            if (item == null) return;

            string path = item.DisplayPath;
            if (!item.PathExists()) return;

            try { if (_host != null) _host.Shell.RevealPath(path); }
            catch (Exception ex) { _setStatus("定位失败：" + ex.Message, CardStatusKind.Error); }
        }

        /// <summary>把多选条目的路径逐行放进剪贴板；剪贴板被占用时状态行提示失败。</summary>
        public void CopyPaths(List<StationItem> selected)
        {
            if (selected.Count == 0) return;

            var lines = new List<string>();
            foreach (StationItem item in selected) lines.Add(item.DisplayPath);

            bool ok = _host != null && _host.Shell.TrySetClipboardText(string.Join(Environment.NewLine, lines));
            _setStatus(ok
                    ? (selected.Count > 1 ? selected.Count + " 条路径已复制" : "路径已复制")
                    : "复制路径失败（剪贴板被占用）",
                      ok ? CardStatusKind.Success : CardStatusKind.Error);
        }

        /// <summary>打开接收目录；目录不存在时先建出来，失败原因进状态行。</summary>
        public void OpenDirectory()
        {
            string directory = _station.Directory;
            if (string.IsNullOrWhiteSpace(directory)) return;

            try
            {
                Directory.CreateDirectory(directory);
                if (_host != null) _host.Shell.OpenPath(directory);
            }
            catch (Exception ex)
            {
                _setStatus("打开目录失败：" + ex.Message, CardStatusKind.Error);
            }
        }

        /// <summary>是否至少有一个选中项是「接收目录内、且仍存在」的条目（右键菜单删除项的可用态）。</summary>
        public bool HasDeletable(List<StationItem> selected)
        {
            foreach (StationItem candidate in selected)
            {
                if (string.IsNullOrEmpty(candidate.StoredPath)) continue;
                if (!StationFiles.IsInside(_station.Directory, candidate.StoredPath)) continue;
                if (candidate.PathExists()) return true;
            }
            return false;
        }

        /// <summary>列表里丢失项的数量（右键菜单「移除丢失项」的计数与可用态）。</summary>
        public int CountMissing(IList<StationItem> items)
        {
            int missingCount = 0;
            foreach (StationItem candidate in items)
                if (candidate.Missing || !candidate.PathExists()) missingCount++;
            return missingCount;
        }

        /// <summary>
        /// 不可逆删除：只允许删「接收目录内、且仍存在」的条目，确认框与失败项都保持原样。
        /// 返回实际删除的条数，供视图决定是否清理连选锚点。
        /// </summary>
        public int DeleteFiles(Window owner, List<StationItem> selected, IList<StationItem> items)
        {
            var deletable = new List<StationItem>();
            foreach (StationItem item in selected)
            {
                if (string.IsNullOrEmpty(item.StoredPath)) continue;
                if (!StationFiles.IsInside(_station.Directory, item.StoredPath)) continue;
                if (item.PathExists()) deletable.Add(item);
            }

            if (deletable.Count == 0)
            {
                _setStatus("选中的条目没有可删除的接收目录文件。", CardStatusKind.Warning);
                return 0;
            }

            MessageBoxResult answer;
            string message;
            if (deletable.Count == 1)
            {
                message = deletable[0].IsDirectory
                    ? "确定删除这个文件夹及其内容？\n\n" + deletable[0].Name
                    : "确定删除这个文件？\n\n" + deletable[0].Name;
            }
            else
            {
                message = "确定删除选中的 " + deletable.Count + " 项？\n\n文件夹会连同内容一起删除。";
            }

            if (owner == null)
                answer = MessageBox.Show(message, "文件中转站", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            else
                answer = MessageBox.Show(owner, message, "文件中转站", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return 0;

            int deleted = 0;
            int failed = 0;
            var failures = new List<string>();
            foreach (StationItem item in deletable)
            {
                try
                {
                    if (item.IsDirectory || Directory.Exists(item.StoredPath))
                        Directory.Delete(item.StoredPath, true);
                    else
                        File.Delete(item.StoredPath);
                    items.Remove(item);
                    deleted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    // 不可逆删除的失败必须留底：哪个文件、为什么，否则静默半删后数据去向无从追查。
                    failures.Add(item.Name + "（" + ex.Message + "）");
                }
            }

            if (failed > 0)
                Log.Warn("中转站不可逆删除失败 " + failed + "/" + deletable.Count + " 项: " +
                         string.Join("；", failures.ToArray()));

            if (deleted > 0) _persistAndRebuild();

            if (failed == 0)
                _setStatus(deleted > 1 ? "已删除 " + deleted + " 项" : "已删除", CardStatusKind.Normal);
            else
                _setStatus("已删除 " + deleted + " 项，失败 " + failed + " 项", CardStatusKind.Warning);
            return deleted;
        }

        /// <summary>移除列表里的丢失项（只清列表，不删文件）；返回移除条数，供视图清理连选锚点。</summary>
        public int RemoveMissing(IList<StationItem> items)
        {
            int before = items.Count;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                StationItem item = items[i];
                if (item.Missing || !item.PathExists()) items.RemoveAt(i);
            }

            int removed = before - items.Count;
            if (removed == 0)
            {
                _setStatus("没有丢失的文件。", CardStatusKind.Normal);
                return 0;
            }

            _persistAndRebuild();
            _setStatus("已移除 " + removed + " 个丢失项", CardStatusKind.Success);
            return removed;
        }
    }

    /// <summary>
    /// 中转站共用的路径 / 文件小工具。集中在一处是为了让 Store、CopyController 与视图
    /// 共用同一份语义（路径归一化、大小、重名目标、唯一 Id），避免拆分后实现各自漂移。
    /// </summary>
    internal static class StationFiles
    {
        /// <summary>归一化成绝对路径；空白 / 非法路径返回 null。</summary>
        public static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try { return Path.GetFullPath(path.Trim()); }
            catch { return null; }
        }

        /// <summary>列表里是否已经有同一个源路径或落盘路径的条目。</summary>
        public static bool ContainsPath(IList<StationItem> items, string path)
        {
            foreach (StationItem item in items)
            {
                if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(item.StoredPath, path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>文件大小；取不到时按 0 处理（目录本身不计算大小）。</summary>
        public static long SafeSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        public static string FormatSize(long size)
        {
            if (size <= 0) return "";

            const double unit = 1024.0;
            if (size < unit) return size + " B";
            if (size < unit * unit) return (size / unit).ToString("0.0") + " KB";
            if (size < unit * unit * unit) return (size / unit / unit).ToString("0.0") + " MB";
            return (size / unit / unit / unit).ToString("0.0") + " GB";
        }

        /// <summary>列表显示名：文件取文件名，文件夹取末段目录名（根目录退回完整路径）。</summary>
        public static string ItemName(string path, bool isDirectory)
        {
            if (!isDirectory) return Path.GetFileName(path);
            try
            {
                string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string name = Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch { return path; }
        }

        /// <summary>路径是否位于接收目录内部（用于禁止删除接收目录之外的文件）。</summary>
        public static bool IsInside(string directory, string path)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(path);
                return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>复制 / 移动文件夹时不能把接收目录放进源文件夹内部，否则递归会自我嵌套。</summary>
        public static bool FolderWouldContainReceiveDirectory(string source, string directory)
        {
            try
            {
                string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string targetRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase)) return true;
                return targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || targetRoot.StartsWith(sourceRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>重名自动改成「名字 (2).扩展名」；最多尝试 999 个后缀，保持原有上限。</summary>
        public static string UniqueTarget(string directory, string fileName, bool isDirectory = false)
        {
            string baseName = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);
            string extension = isDirectory ? "" : Path.GetExtension(fileName);
            string candidate = Path.Combine(directory, fileName);

            int index = 2;
            while ((File.Exists(candidate) || Directory.Exists(candidate)) && index < 1000)
            {
                candidate = Path.Combine(directory, baseName + " (" + index + ")" + extension);
                index++;
            }
            return candidate;
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o");
        }

        public static bool SamePath(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                   string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
