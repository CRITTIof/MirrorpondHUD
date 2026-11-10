using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// items.json 的纯逻辑层：Load / MergeWithDisk / Persist / Trim，以及「视图销毁后完成项」的
    /// 单条合并。这里不碰任何 WPF 控件，便于单独推演与后续自检（审阅第三批第 11 项）。
    ///
    /// P1-6：写盘串行化用一把**静态**锁——新旧视图的完成回调可能在不同线程先后回来，
    /// 不能交错读改写同一份 items.json；锁必须跨视图实例共享，所以不能挂在某个 StationStore 上。
    /// </summary>
    internal sealed class StationStore
    {
        public const string StateFile = "items.json";

        /// <summary>写 items.json 的串行锁：新旧视图的完成回调不会交错读改写同一份文件（P1-6）。</summary>
        static readonly object PersistSync = new object();

        readonly ICardHost _host;
        readonly int _maxEntries;

        /// <summary>本视图加载 / 上次写盘时列表里的条目 Id：合并时区分「本视图删的」和「别人新加的」（P1-6）。</summary>
        readonly HashSet<string> _loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 读到高于当前支持的 FormatVersion，或列表文件读不出来：拒绝加载 / 拒绝写盘，
        /// 之后所有整表写入都是空操作，绝不覆盖原文件。
        /// </summary>
        bool _readOnly;
        bool _readOnlyLogged;
        string _readOnlyReason = "";

        public StationStore(ICardHost host, int maxEntries)
        {
            _host = host;
            _maxEntries = Math.Max(1, maxEntries);
        }

        /// <summary>items.json 是否因版本过高被拒写；视图据此跳过整表写盘，避免把「拒绝覆盖」误报成写失败。</summary>
        public bool IsReadOnly
        {
            get { return _readOnly; }
        }

        /// <summary>只读原因（版本过高 / 读取失败），给状态行显示。</summary>
        public string ReadOnlyReason
        {
            get { return _readOnlyReason; }
        }

        /// <summary>
        /// 读取列表：过滤空条目、修正目录标记、按 AddedUtc 旧→新排序、裁剪到 MaxEntries，
        /// 记下加载基线；<paramref name="autoRemoveMissing"/> 为真时再剔除丢失项（只清列表，不删文件）。
        /// 返回的列表交给视图持有，Store 不缓存条目对象。
        /// </summary>
        public List<StationItem> Load(bool autoRemoveMissing, out int autoRemoved)
        {
            autoRemoved = 0;
            var items = new List<StationItem>();

            StationState state = ReadState(out bool readFailed);
            if (readFailed)
            {
                EnterReadOnly("列表文件读取失败");
                return items;
            }
            if (state == null || state.Items == null) return items;

            try
            {
                foreach (StationItem item in state.Items)
                {
                    if (item == null || string.IsNullOrEmpty(item.DisplayPath)) continue;
                    if (!item.IsDirectory && Directory.Exists(item.DisplayPath)) item.IsDirectory = true;
                    item.Missing = !item.PathExists();
                    items.Add(item);
                }

                // 旧版本可能把「磁盘旧条目」堆在文件尾部；先按 AddedUtc 排回旧→新，
                // 再从头部裁掉最旧的，保证留下的是最新 MaxEntries 条（P1-6 合并截断）。
                List<StationItem> ordered = OrderOldestFirst(items);
                items.Clear();
                items.AddRange(ordered);
                Trim(items);

                // 先记下加载基线：之后本视图删掉的条目，写盘合并时不会从磁盘「复活」（P1-6）。
                RememberLoaded(items);

                if (autoRemoveMissing)
                {
                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        if (!items[i].Missing) continue;
                        items.RemoveAt(i);
                        autoRemoved++;
                    }
                    if (autoRemoved > 0) Persist(items);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("中转站状态解析失败: " + ex.Message);
            }
            return items;
        }

        /// <summary>
        /// 把当前列表写回 items.json。P1-6：复制完成的回调可能晚于新视图写盘，所以不能
        /// 直接整表覆盖——在读-改-写锁里先读磁盘，按 <see cref="StationItem.Id"/> 合并：
        /// · 本视图加载时就存在、现在没了的条目 = 本视图删掉的，不复活；
        /// · 本次加载之后由别的视图新增的条目 = 保留，别抹掉；
        /// · 同 Id 以本视图为准（本视图持有最新的 StoredPath / Missing）。
        /// 合并结果按 AddedUtc 从旧到新排序，超过 MaxEntries 时从头部（最旧）裁剪。
        /// 只有写盘成功才把当前列表记为新的「已加载 / 已写盘」基线；失败时保留旧基线，
        /// 下一次 Persist 会带着同一份内存列表重试，删掉的条目也不会因此复活。
        /// 读到更高 FormatVersion 时这里直接放弃（_readOnly），绝不用空列表覆盖原文件。
        /// </summary>
        public void Persist(List<StationItem> items)
        {
            if (_readOnly) return;

            try
            {
                lock (PersistSync)
                {
                    StationState merged = MergeWithDisk(items);
                    // MergeWithDisk 读盘时可能才发现 FormatVersion 过高：此时不能写、也不要报「失败等待重试」。
                    if (_readOnly) return;

                    if (!WriteStateJson(merged))
                    {
                        Log.Warn("中转站状态写入失败，保留写入基线等待下次重试。");
                        return;
                    }

                    RememberLoaded(items);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("中转站状态写入失败: " + ex.Message);
            }
        }

        /// <summary>把列表从头部（最旧）裁到最多 MaxEntries 条；只清列表，不删文件。</summary>
        public void Trim(List<StationItem> items)
        {
            while (items.Count > _maxEntries) items.RemoveAt(0);
        }

        /// <summary>
        /// 视图销毁后复制才完成：目标已落盘，但旧视图不能再碰 UI / 不能整表覆盖新视图。
        /// 这里只把这一条按 Id（没有 Id 的旧数据按路径）合并进 items.json，并按
        /// AddedUtc 保留最新 <see cref="StationSettings.MaxEntries"/> 条，让新视图
        /// Load 时能看到已完成的收纳，而不是留下「磁盘上有、列表里没有」的孤儿文件（P1-6）。
        /// 视图已销毁，所以不再做 Move 模式的删源副作用，只补齐状态。
        /// 调用方必须保证 <paramref name="item"/> 不会再被 UI 线程并发读写：
        /// 这里只读取它的值，写盘走 <see cref="PersistSync"/> 与新视图串行。
        /// </summary>
        public void MergeCompletedAfterDispose(StationItem item, CardJob job, string target)
        {
            try
            {
                if (item == null || job == null) return;
                if (job.State != CardJobState.Succeeded || string.IsNullOrEmpty(target)) return;
                // 完成回调里目标已经不在（被外部删除 / 移走）时不记录指向空路径的条目。
                if (!File.Exists(target) && !Directory.Exists(target)) return;

                var completed = new StationItem
                {
                    Id = string.IsNullOrEmpty(item.Id) ? StationFiles.NewId() : item.Id,
                    Path = item.Path,
                    StoredPath = target,
                    Name = Path.GetFileName(target),
                    Size = item.IsDirectory ? 0 : StationFiles.SafeSize(target),
                    AddedUtc = string.IsNullOrEmpty(item.AddedUtc) ? StationFiles.UtcNow() : item.AddedUtc,
                    Missing = false,
                    IsDirectory = item.IsDirectory
                };

                lock (PersistSync)
                {
                    // 版本过高：连这一条也不能写，原文件必须保持原样。
                    if (_readOnly)
                    {
                        Log.Warn("中转站列表只读，丢弃晚到的完成项: " + target);
                        return;
                    }

                    StationState disk = ReadState(out bool readFailed);
                    if (readFailed)
                    {
                        EnterReadOnly("列表文件读取失败");
                        return;
                    }
                    // 读盘时可能刚发现更高版本：连这条晚到结果也不能写，原文件保持原样。
                    if (_readOnly) return;

                    var items = new List<StationItem>();
                    if (disk != null && disk.Items != null)
                    {
                        foreach (StationItem existing in disk.Items)
                        {
                            if (existing == null) continue;
                            // 同 Id：用完成后的结果替换（StoredPath 从空变成落盘路径）。
                            if (!string.IsNullOrEmpty(existing.Id) &&
                                string.Equals(existing.Id, completed.Id, StringComparison.OrdinalIgnoreCase))
                                continue;
                            // 旧数据没有 Id：按源路径 / 目标路径去重，别让同一条出现两次。
                            if (string.IsNullOrEmpty(existing.Id) &&
                                (StationFiles.SamePath(existing.DisplayPath, item.Path) ||
                                 StationFiles.SamePath(existing.DisplayPath, target)))
                                continue;
                            items.Add(existing);
                        }
                    }

                    items.Add(completed);
                    items = OrderOldestFirst(items);
                    TrimMerged(items);

                    if (!WriteStateJson(new StationState { Items = items }))
                        Log.Warn("中转站销毁后合并完成项写盘失败，目标文件可能不在列表中: " + target);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("中转站销毁后合并完成项失败: " + ex.Message);
            }
        }

        /// <summary>合并内存列表与磁盘状态（P1-6）。调用方必须持有 <see cref="PersistSync"/>。</summary>
        StationState MergeWithDisk(List<StationItem> currentItems)
        {
            var current = new List<StationItem>(currentItems);
            var onlyOnDisk = new List<StationItem>();

            StationState disk = ReadState(out bool readFailed);
            if (readFailed)
            {
                // 磁盘状态读不出来时不能按“只剩内存条目”合并：下一次写盘会把原文件覆盖成
                // 当前（可能是空）列表。和版本过高一样进入只读，等用户处理原文件。
                EnterReadOnly("列表文件读取失败");
                return new StationState { Items = current };
            }

            if (disk != null && disk.Items != null)
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (StationItem item in current)
                {
                    if (item == null) continue;
                    if (!string.IsNullOrEmpty(item.Id)) ids.Add(item.Id);
                    if (!string.IsNullOrEmpty(item.DisplayPath)) paths.Add(item.DisplayPath);
                }

                foreach (StationItem item in disk.Items)
                {
                    if (item == null || string.IsNullOrEmpty(item.DisplayPath)) continue;

                    if (!string.IsNullOrEmpty(item.Id))
                    {
                        if (ids.Contains(item.Id)) continue;          // 本视图有：以本视图为准
                        if (_loadedIds.Contains(item.Id)) continue;   // 加载过、被本视图删了：不复活
                    }
                    else if (paths.Contains(item.DisplayPath))
                    {
                        continue;                                      // 旧数据没有 Id：按路径去重
                    }

                    onlyOnDisk.Add(item);
                    if (!string.IsNullOrEmpty(item.Id)) ids.Add(item.Id);
                    paths.Add(item.DisplayPath);
                }
            }

            // 「只在磁盘上」的条目先拼、内存列表后拼：AddedUtc 都有效时由下面的排序决定先后；
            // 时间相同 / 旧数据没有 AddedUtc 时，只在磁盘上的多半是加载时从头部裁掉的更旧条目，
            // 让它们在稳定排序里排在前面，TrimMerged 才会先裁它们，而不是把内存里的新条目裁掉。
            var merged = new List<StationItem>(onlyOnDisk.Count + current.Count);
            merged.AddRange(onlyOnDisk);
            merged.AddRange(current);

            // 关键：磁盘上的旧条目不能直接追加到尾部当成「最新」。先按 AddedUtc 排回旧→新，
            // 再从头裁最旧，才能做到旧条不复活、最新 MaxEntries 条被保留。
            merged = OrderOldestFirst(merged);
            TrimMerged(merged);
            return new StationState { Items = merged };
        }

        /// <summary>合并结果按 AddedUtc 排好序后，从头部裁掉最旧的条目，只留最新 MaxEntries 条（P1-6）。</summary>
        void TrimMerged(List<StationItem> items)
        {
            while (items.Count > _maxEntries) items.RemoveAt(0);
        }

        /// <summary>
        /// 按 <see cref="StationItem.AddedUtc"/> 从旧到新稳定排序。时间相同或解析不了
        /// （空字符串 / 旧数据）的条目保持原有相对顺序；解析不了的排在最前，视为最旧。
        /// </summary>
        static List<StationItem> OrderOldestFirst(IEnumerable<StationItem> items)
        {
            return items
                .Where(item => item != null)
                .OrderBy(AddedKey)
                .ToList();
        }

        static DateTime AddedKey(StationItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.AddedUtc)) return DateTime.MinValue;

            DateTime parsed;
            return DateTime.TryParse(item.AddedUtc, CultureInfo.InvariantCulture,
                                     DateTimeStyles.RoundtripKind, out parsed)
                ? parsed
                : DateTime.MinValue;
        }

        /// <summary>把当前内存列表记为「已加载 / 已写盘」基线（P1-6）。</summary>
        void RememberLoaded(IEnumerable<StationItem> items)
        {
            _loadedIds.Clear();
            foreach (StationItem item in items)
                if (item != null && !string.IsNullOrEmpty(item.Id)) _loadedIds.Add(item.Id);
        }

        /// <summary>读磁盘上的状态；没有 / 读坏了返回 null；版本高于当前支持时置只读并返回 null。</summary>
        StationState ReadState(out bool readFailed)
        {
            readFailed = false;
            string path = _host == null ? "" : Path.Combine(_host.StateDirectory, StateFile);

            try
            {
                string json = _host == null ? null : _host.ReadState(StateFile);
                if (string.IsNullOrEmpty(json))
                {
                    // 文件不存在 = 首次使用；文件存在但读不到内容 = I/O / 权限失败，不能当空列表。
                    readFailed = path.Length > 0 && File.Exists(path);
                    return null;
                }

                StationState state = Json.Deserialize<StationState>(json);
                if (state == null)
                {
                    readFailed = true;
                    return null;
                }
                if (state.FormatVersion > StationState.CurrentFormatVersion)
                {
                    RefuseHigherFormat(state.FormatVersion);
                    return null;
                }
                return state;
            }
            catch (Exception ex)
            {
                readFailed = true;
                Log.Warn("中转站状态解析失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>读失败 / 版本过高时进入只读；只记一条 WARN，避免每次读写重复刷屏。</summary>
        void EnterReadOnly(string reason)
        {
            _readOnly = true;
            _readOnlyReason = reason;
            if (_readOnlyLogged) return;

            _readOnlyLogged = true;
            Log.Warn("中转站列表进入只读（" + reason + "），本次不会覆盖 items.json；请检查文件权限 / 内容后重启卡片");
        }

        /// <summary>列表文件比当前代码新：只警告一次、拒绝加载，并让所有后续写盘变成空操作。</summary>
        void RefuseHigherFormat(int version)
        {
            _readOnly = true;
            _readOnlyReason = "列表文件版本过高（" + version + "，支持 " + StationState.CurrentFormatVersion + "）";
            if (_readOnlyLogged) return;

            _readOnlyLogged = true;
            Log.Warn("中转站列表文件版本过高，已拒绝加载并保持原文件不动  version=" + version +
                     "  支持=" + StationState.CurrentFormatVersion);
        }

        /// <summary>写 items.json；返回是否成功（宿主 / CardStateStore 都可能返回 false，版本过高一律不写）。</summary>
        bool WriteStateJson(StationState state)
        {
            if (_readOnly) return false;

            state.FormatVersion = StationState.CurrentFormatVersion;
            string json = Json.Serialize(state);
            return _host != null && _host.WriteState(StateFile, json);
        }
    }
}
