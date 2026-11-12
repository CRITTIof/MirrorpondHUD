using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards;

/// <summary>复制 / 移动流程需要的最小 UI 回填；具体控件只留在 StationView 里。</summary>
internal interface IStationCopySink
{
    bool IsDisposed { get; }
    void CopyStatus(string text, CardStatusKind kind);
    void CopyItemRemoved(StationItem item);
    /// <summary>这一项是否仍在视图列表里；复制完成时用来识别“期间被移除”的条目（P-3）。</summary>
    bool HasItem(StationItem item);
    void CopyPersistAndRebuild();
    void CopyStateChanged();
}

/// <summary>
/// 复制 / 移动队列：计划、流式复制、进度节流、取消、完成回填、移动模式删源，
/// 以及视图销毁后才落盘的「晚到结果」合并（P1-6）。
///
/// 并发语义保持原样：CardJobQueue(1) 串行执行；排队 / 运行中的任务由它自己取消；
/// 进度与完成回填仍通过 Dispatcher.BeginInvoke 回到 UI 线程，并先校验视图是否已销毁。
/// </summary>
internal sealed class StationCopyController : IDisposable
{
    readonly CardJobQueue _jobs = new(1);
    readonly StationStore _store;
    readonly StationSettings _station;
    readonly Dispatcher _dispatcher;
    readonly IStationCopySink _sink;

    int _copyTotal;
    int _copyDone;
    string _activeName;
    double _activePercent;
    double _activeSpeed;
    bool _cancelRequested;

    public StationCopyController(StationStore store, StationSettings station, Dispatcher dispatcher, IStationCopySink sink)
    {
        _store = store;
        _station = station;
        _dispatcher = dispatcher;
        _sink = sink;
    }

    public int Total => _copyTotal;

    public int Done => _copyDone;

    public bool CancelRequested => _cancelRequested;

    public string ActiveName => _activeName;

    public double ActivePercent => _activePercent;

    public double ActiveSpeed => _activeSpeed;

    public bool IsBusy => _copyTotal > 0 && _copyDone < _copyTotal;

    /// <summary>排入一个复制 / 移动任务；具体执行顺序由 CardJobQueue 串行化。</summary>
    public void Enqueue(StationItem item)
    {
        // P1-6：视图已销毁（换接收目录 / 删卡 / 退出）就不再排新任务。
        if (_sink.IsDisposed) return;

        var context = new CopyContext();
        context.Source = item.Path;
        context.Directory = _station.Directory;
        context.FileName = string.IsNullOrEmpty(item.Name) ? Path.GetFileName(context.Source) : item.Name;
        context.IsDirectory = item.IsDirectory;

        _copyTotal++;
        _cancelRequested = false;
        _sink.CopyStateChanged();

        CardJob job = _jobs.Enqueue(context.FileName, (token, progress) =>
        {
            RunCopy(context, token, progress);
            return Task.CompletedTask;
        });

        job.Task.ContinueWith(task =>
        {
            // P1-6：视图可能已经被销毁（换接收目录 / 删卡 / 退出）。
            // 销毁后不能再回 UI 线程改列表、也不能整表覆盖新视图；但复制已经成功、
            // 目标已经落盘时不能把结果整个丢掉——只把这一条合并进 items.json，
            // 让下一个视图 Load 时能看到（见 StationStore.MergeCompletedAfterDispose）。
            if (_sink.IsDisposed)
            {
                _store.MergeCompletedAfterDispose(item, job, context.Target);
                return;
            }

            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_sink.IsDisposed)
                {
                    _store.MergeCompletedAfterDispose(item, job, context.Target);
                    return;
                }
                CompleteCopy(item, job, context);
            }));
        });
    }

    /// <summary>取消排队与进行中的复制；状态行由视图按 CancelRequested 回填。</summary>
    public void Cancel()
    {
        if (_copyTotal == 0 || _copyDone >= _copyTotal) return;

        _cancelRequested = true;
        _jobs.CancelAll();
        _sink.CopyStateChanged();
    }

    /// <summary>视图销毁时收尾队列；延迟释放信号量的语义在 CardJobQueue 内部（P2-10）。</summary>
    public void Dispose() => _jobs.Dispose();

    /// <summary>
    /// 一次复制任务的主体：建目录、后台计划、逐文件流式复制并节流回填进度。
    /// 全程同步（与原实现一致：首个任务会在调用线程上直接跑，排队任务由队列调度）。
    /// </summary>
    void RunCopy(CopyContext context, CancellationToken token, IProgress<double> progress)
    {
        Directory.CreateDirectory(context.Directory);

        _dispatcher.BeginInvoke(new Action(() =>
        {
            // P1-6：视图可能已销毁，排队的回调直接丢掉。
            if (_sink.IsDisposed || _cancelRequested || _copyDone >= _copyTotal) return;
            _activeName = context.FileName;
            _activePercent = 0;
            _activeSpeed = 0;
            _sink.CopyStateChanged();
        }));

        var plan = new List<KeyValuePair<string, string>>();
        long total = 0;

        try
        {
            total = context.IsDirectory
                ? BuildDirectoryPlan(context, plan, token)
                : BuildFilePlan(context, plan);

            CopyEntries(context, plan, total, token, progress);
            if (total == 0) progress.Report(1.0);

            // P-14：跳过项必须在完成前汇总进日志；状态行由 CompleteCopy 按数量降级为 Warning，
            // 绝不把「目标内容不全」报成 Success。
            LogSkippedLinks(context);
            if (context.SourceLinkNote is not null)
                Log.Warn($"中转站收纳的源是符号链接 / 联接点，复制的是指向的内容，链接语义本身不会保留"
                       + $"  源={context.Source}  项={context.SourceLinkNote}");

            // Move 模式：删源与目标大小计算都留在工作线程。回到 UI 线程递归删除
            // 几万个文件会把整个叠层冻住，而且“取消”按钮此时已经无效（P-2）。
            context.TargetSize = context.IsDirectory ? 0 : StationFiles.SafeSize(context.Target);
            if (_station.Mode == "Move") TryDeleteSource(context, token);
        }
        catch
        {
            // 失败 / 取消：把写了一半的目标文件 / 文件夹删掉，别留半成品。
            CleanupPartial(context.Directory, context.Target);
            throw;
        }
    }

    /// <summary>Move 模式的删源：只在工作线程执行，失败只记录不回滚已复制的目标。</summary>
    void TryDeleteSource(CopyContext context, CancellationToken token)
    {
        if (StationFiles.IsRootPath(context.Source))
        {
            context.SourceDeleteError = "源是盘符根 / 共享根，拒绝删除";
            Log.Warn($"中转站移动模式拒绝删除根路径源: {context.Source}");
            return;
        }

        // P-14：计划里出现过符号链接 / 联接点，说明源目录并不完整地复制到了目标。
        // 此时递归删源会连带删掉链接本身（目标里也没有它们），必须保留源让用户手动处理。
        if (context.SkippedLinks.Count > 0)
        {
            context.SourceDeleteError = $"源内含 {context.SkippedLinks.Count} 个符号链接 / 联接点，已保留源";
            Log.Warn($"中转站移动模式保留源：计划里有 {context.SkippedLinks.Count} 个未跟随的链接，"
                   + $"递归删除会连带删掉链接本身  源={context.Source}");
            return;
        }

        try
        {
            // 取消时保留源：宁可留一份重复数据，也不要在用户已经取消后继续删。
            token.ThrowIfCancellationRequested();
            if (context.IsDirectory) Directory.Delete(context.Source, true);
            else File.Delete(context.Source);
            context.SourceDeleted = true;
        }
        catch (OperationCanceledException)
        {
            context.SourceDeleteError = "已取消，保留源";
        }
        catch (Exception ex)
        {
            context.SourceDeleteError = ex.Message;
            Log.Warn($"中转站移动模式删除源失败（目标保留）  源={context.Source}  原因={ex.Message}");
        }
    }

    /// <summary>
    /// P-14：判断一个条目是否是 reparse point（符号链接 / 联接点 / OneDrive 占位等）。
    /// 属性读不出来（失效链接、权限不足）时**保守按链接处理**：宁可少复制一个，
    /// 也不要在无法确认安全的情况下递归进入一个可能指向任意位置的链接。
    /// 返回 true 时 <paramref name="reason"/> 说明是链接还是读属性失败。
    /// </summary>
    static bool ShouldSkipLink(string path, out string reason)
    {
        reason = null;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                reason = "符号链接 / 联接点";
                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or System.Security.SecurityException)
        {
            reason = $"无法读取属性（{ex.Message}）";
            return true;
        }
    }

    /// <summary>
    /// 文件夹：拒绝链接作根，先建目录树，再枚举出「源 → 目标」文件计划与总字节数。
    ///
    /// P-14：手工递归而不是 <c>AttributesToSkip = ReparsePoint</c>。枚举选项会**静默**剔除
    /// 子层级链接，计划偏小却仍报「已收纳」，Move 随后递归删源 → 目标缺文件、链接也消失。
    /// 现在改为「跳过但不跟随、逐条记账」：计划里出现链接时完成状态降为 Warning 并写日志，
    /// Move 模式还会保留源（见 <see cref="TryDeleteSource"/>）。跟随链接会引入环路与越界写，
    /// 所以「跳过但可见」是安全侧的正确折中。
    /// </summary>
    long BuildDirectoryPlan(CopyContext context, List<KeyValuePair<string, string>> plan, CancellationToken token)
    {
        if ((File.GetAttributes(context.Source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("暂不支持收纳符号链接文件夹。");

        context.Target = StationFiles.UniqueTarget(context.Directory, context.FileName, true);
        Directory.CreateDirectory(context.Target);

        long total = 0;
        void Walk(string sourceDir, int depth)
        {
            if (depth > 64) throw new IOException("目录层级过深（可能包含链接环）。");

            foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                if (ShouldSkipLink(file, out string reason))
                {
                    NoteSkipped(context, file, reason);
                    continue;
                }

                string relative = Path.GetRelativePath(context.Source, file);
                plan.Add(new KeyValuePair<string, string>(file, Path.Combine(context.Target, relative)));
                total += StationFiles.SafeSize(file);
            }

            foreach (string dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                if (ShouldSkipLink(dir, out string reason))
                {
                    // 不建目标目录、也不递归进入：链接指向的内容可能在这个盘之外。
                    NoteSkipped(context, dir, reason);
                    continue;
                }

                Directory.CreateDirectory(Path.Combine(context.Target, Path.GetRelativePath(context.Source, dir)));
                Walk(dir, depth + 1);
            }
        }

        Walk(context.Source, 0);
        return total;
    }

    /// <summary>记下一条被跳过的链接（相对源根的路径），供完成状态与日志使用。</summary>
    static void NoteSkipped(CopyContext context, string path, string reason)
    {
        string relative;
        try { relative = Path.GetRelativePath(context.Source, path); }
        catch (ArgumentException) { relative = path; }

        context.SkippedLinks.Add($"{relative}（{reason}）");
    }

    /// <summary>
    /// P-14：把跳过项汇总成一条 Warn。逐条 Log 在含 junction 的开发目录里会刷屏，
    /// 这里只列前几条，完整数量进汇总行。
    /// </summary>
    static void LogSkippedLinks(CopyContext context)
    {
        if (context.SkippedLinks.Count == 0) return;

        const int sample = 5;
        var preview = new System.Text.StringBuilder();
        for (int i = 0; i < context.SkippedLinks.Count && i < sample; i++)
        {
            if (preview.Length > 0) preview.Append("；");
            preview.Append(context.SkippedLinks[i]);
        }
        if (context.SkippedLinks.Count > sample) preview.Append($"，等共 {context.SkippedLinks.Count} 项");

        Log.Warn($"中转站收纳跳过 {context.SkippedLinks.Count} 个符号链接 / 联接点（未跟随、未复制）"
               + $"  源={context.Source}  跳过项={preview}");
    }

    /// <summary>单文件：先定唯一目标名，计划就是源 → 目标一条。</summary>
    long BuildFilePlan(CopyContext context, List<KeyValuePair<string, string>> plan)
    {
        context.Target = StationFiles.UniqueTarget(context.Directory, context.FileName);
        plan.Add(new KeyValuePair<string, string>(context.Source, context.Target));

        // P-14：单文件链接是用户显式拖进来的。复制会跟随链接拿到内容（File.OpenRead 的语义），
        // Move 的 File.Delete 只删链接本身、不会递归删目标——行为安全但语义变了，必须留痕。
        if (ShouldSkipLink(context.Source, out string reason))
            context.SourceLinkNote = $"{context.FileName}（{reason}）";

        return StationFiles.SafeSize(context.Source);
    }

    /// <summary>逐个文件流式复制（1 MB 缓冲）；取消检查细到每个缓冲区。</summary>
    void CopyEntries(CopyContext context, List<KeyValuePair<string, string>> plan, long total,
                     CancellationToken token, IProgress<double> progress)
    {
        long copied = 0;
        var watch = Stopwatch.StartNew();
        DateTime lastReport = DateTime.MinValue;

        // 1 MB 缓冲提到循环外：一个文件夹几百个文件时不再反复分配 LOH 级数组。
        var buffer = new byte[1024 * 1024];

        foreach (KeyValuePair<string, string> pair in plan)
        {
            token.ThrowIfCancellationRequested();

            string parent = Path.GetDirectoryName(pair.Value);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            using (var input = File.OpenRead(pair.Key))
            using (var output = File.Create(pair.Value))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    copied += read;

                    double ratio = total > 0 ? Math.Min(1.0, (double)copied / total) : 1.0;
                    progress.Report(ratio);
                    lastReport = ReportUiProgress(context.FileName, ratio, copied, watch, lastReport);
                }
            }
        }
    }

    /// <summary>进度回 UI 线程的最小间隔 120ms；节流期间只更新任务进度，不回 UI。</summary>
    DateTime ReportUiProgress(string fileName, double ratio, long copied, Stopwatch watch, DateTime lastReport)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - lastReport).TotalMilliseconds < 120) return lastReport;

        double speed = watch.Elapsed.TotalSeconds > 0
            ? copied / (1024.0 * 1024.0) / watch.Elapsed.TotalSeconds
            : 0;
        double uiRatio = ratio;
        double uiSpeed = speed;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_sink.IsDisposed) UpdateActiveCopy(fileName, uiRatio, uiSpeed);
        }));
        return now;
    }

    void UpdateActiveCopy(string name, double percent, double speed)
    {
        if (_sink.IsDisposed || _cancelRequested || _copyTotal == 0 || _copyDone >= _copyTotal) return;

        _activeName = name;
        _activePercent = Math.Max(0, Math.Min(1, percent));
        _activeSpeed = Math.Max(0, speed);
        _sink.CopyStateChanged();
    }

    /// <summary>失败 / 取消：把写了一半的目标文件 / 文件夹删掉，别留半成品。</summary>
    void CleanupPartial(string directory, string target)
    {
        try
        {
            if (!string.IsNullOrEmpty(target))
            {
                // 只删接收目录内部的半成品。目标被路径缺陷带到盘符根 / 接收目录外时，
                // 宁可留下半成品并记 Warn，也不执行递归删除。
                if (!StationFiles.IsInside(directory, target))
                {
                    Log.Warn($"中转站清理跳过越界目标: {target}");
                    return;
                }

                if (Directory.Exists(target)) Directory.Delete(target, true);
                else if (File.Exists(target)) File.Delete(target);
            }
        }
        catch (Exception ex)
        {
            // 清理是尽力而为：目标可能正被占用；复制本身的异常会继续抛出，这里只把清理失败记到 Debug。
            Log.Debug($"中转站清理未完成的目标失败: {ex.Message}");
        }
    }

    /// <summary>在 UI 线程回填完成结果：写回 StoredPath、移动模式删源、失败 / 取消移除条目。</summary>
    void CompleteCopy(StationItem item, CardJob job, CopyContext context)
    {
        // P1-6：销毁后迟到的完成回调一律忽略（被取消的复制任务也可能走到这里）。
        if (_sink.IsDisposed) return;

        string target = context.Target;

        _copyDone++;
        _activeName = null;
        _activePercent = 0;
        _activeSpeed = 0;

        if (job.State == CardJobState.Succeeded && !string.IsNullOrEmpty(target))
        {
            if (!_sink.HasItem(item))
            {
                // 用户在复制期间把这一项移除 / 清空：目标已经落盘，必须把记录合并回
                // items.json，否则接收目录里会出现“列表里没有”的孤儿文件（P-3）。
                _store.MergeCompletedAfterDispose(item, job, target);
                _sink.CopyStatus($"已收纳 {Path.GetFileName(target)}，但该项已被移出列表（已记入列表文件）",
                                 CardStatusKind.Warning);
                Log.Warn($"中转站在途项被移除后完成，已合并记录  文件={target}");
                if (_copyDone >= _copyTotal)
                {
                    _copyTotal = 0;
                    _copyDone = 0;
                    _cancelRequested = false;
                }
                _sink.CopyStateChanged();
                return;
            }

            item.StoredPath = target;
            item.Name = Path.GetFileName(target);
            item.Size = context.TargetSize;
            item.Missing = false;

            if (_station.Mode == "Move")
            {
                // 删源已在工作线程完成（RunCopy）。这里只把结果翻成状态行，
                // 绝不能在 UI 线程再删一次（P-2）。
                if (StationFiles.IsRootPath(item.Path))
                {
                    _sink.CopyStatus("已收纳，但源是根路径，未删除", CardStatusKind.Warning);
                }
                else if (context.SourceDeleted)
                {
                    _sink.CopyStatus($"已移动 {item.Name}", CardStatusKind.Success);
                }
                else
                {
                    _sink.CopyStatus($"已收纳，但未能删除源：{context.SourceDeleteError ?? "未知原因"}",
                                     CardStatusKind.Warning);
                }
            }
            else
            {
                // P-14：目标少了链接指向的内容，不能报 Success，否则又是一次「收纳成功但不完整」。
                if (context.SkippedLinks.Count > 0)
                {
                    _sink.CopyStatus($"已收纳 {item.Name}，跳过 {context.SkippedLinks.Count} 个符号链接 / 联接点"
                                   + "（未跟随、未复制）", CardStatusKind.Warning);
                }
                else
                {
                    _sink.CopyStatus($"已收纳 {item.Name}", CardStatusKind.Success);
                }
            }
        }
        else if (job.State == CardJobState.Failed)
        {
            _sink.CopyItemRemoved(item);
            _sink.CopyStatus($"收纳失败：{(job.Error ?? "未知错误")}", CardStatusKind.Error);
            // P1-5：静默只有卡片状态行有记录，卡片一关（换目录 / 删卡）这条信息就没了。
            Log.Error($"中转站收纳失败  文件={item.Name}  目标={target}  原因={(job.Error ?? "未知错误")}");
        }
        else
        {
            _sink.CopyItemRemoved(item);
            _sink.CopyStatus("收纳已取消", CardStatusKind.Warning);
            Log.Info($"中转站收纳已取消  文件={item.Name}");
        }

        _sink.CopyPersistAndRebuild();

        if (_copyDone >= _copyTotal)
        {
            _copyTotal = 0;
            _copyDone = 0;
            _cancelRequested = false;
        }

        _sink.CopyStateChanged();
    }

    /// <summary>一次复制任务的上下文；Target 在计划阶段确定，完成回调要读同一个值。</summary>
    sealed class CopyContext
    {
        public string Source;
        public string Directory;
        public string FileName;
        public bool IsDirectory;
        public string Target;
        // Move 模式在工作线程删源后回填；完成回调只读，不再碰文件系统（P-2）。
        public bool SourceDeleted;
        public string SourceDeleteError;
        public long TargetSize;
        // P-14：计划里因 reparse point 被跳过的条目（相对路径 + 原因）。非空即目标内容不全。
        public readonly List<string> SkippedLinks = new();
        // 单文件链接提示：复制跟随了链接内容，Move 只删链接本身；仅用于日志留痕。
        public string SourceLinkNote;
    }
}
