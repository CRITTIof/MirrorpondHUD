using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Cards
{
    /// <summary>复制 / 移动流程需要的最小 UI 回填；具体控件只留在 StationView 里。</summary>
    internal interface IStationCopySink
    {
        bool IsDisposed { get; }
        void CopyStatus(string text, CardStatusKind kind);
        void CopyItemRemoved(StationItem item);
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
        readonly CardJobQueue _jobs = new CardJobQueue(1);
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

        public int Total
        {
            get { return _copyTotal; }
        }

        public int Done
        {
            get { return _copyDone; }
        }

        public bool CancelRequested
        {
            get { return _cancelRequested; }
        }

        public string ActiveName
        {
            get { return _activeName; }
        }

        public double ActivePercent
        {
            get { return _activePercent; }
        }

        public double ActiveSpeed
        {
            get { return _activeSpeed; }
        }

        public bool IsBusy
        {
            get { return _copyTotal > 0 && _copyDone < _copyTotal; }
        }

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
                    CompleteCopy(item, job, context.Target);
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
        public void Dispose()
        {
            _jobs.Dispose();
        }

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
            }
            catch
            {
                // 失败 / 取消：把写了一半的目标文件 / 文件夹删掉，别留半成品。
                CleanupPartial(context.Directory, context.Target);
                throw;
            }
        }

        /// <summary>文件夹：拒绝符号链接，先建目录树，再枚举出「源 → 目标」文件计划与总字节数。</summary>
        long BuildDirectoryPlan(CopyContext context, List<KeyValuePair<string, string>> plan, CancellationToken token)
        {
            if ((File.GetAttributes(context.Source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("暂不支持收纳符号链接文件夹。");

            context.Target = StationFiles.UniqueTarget(context.Directory, context.FileName, true);
            Directory.CreateDirectory(context.Target);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false
            };

            foreach (string dir in Directory.EnumerateDirectories(context.Source, "*", options))
            {
                token.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(context.Source, dir);
                Directory.CreateDirectory(Path.Combine(context.Target, relative));
            }

            long total = 0;
            foreach (string file in Directory.EnumerateFiles(context.Source, "*", options))
            {
                token.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(context.Source, file);
                plan.Add(new KeyValuePair<string, string>(file, Path.Combine(context.Target, relative)));
                total += StationFiles.SafeSize(file);
            }
            return total;
        }

        /// <summary>单文件：先定唯一目标名，计划就是源 → 目标一条。</summary>
        long BuildFilePlan(CopyContext context, List<KeyValuePair<string, string>> plan)
        {
            context.Target = StationFiles.UniqueTarget(context.Directory, context.FileName);
            plan.Add(new KeyValuePair<string, string>(context.Source, context.Target));
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
                        Log.Warn("中转站清理跳过越界目标: " + target);
                        return;
                    }

                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    else if (File.Exists(target)) File.Delete(target);
                }
            }
            catch (Exception ex)
            {
                // 清理是尽力而为：目标可能正被占用；复制本身的异常会继续抛出，这里只把清理失败记到 Debug。
                Log.Debug("中转站清理未完成的目标失败: " + ex.Message);
            }
        }

        /// <summary>在 UI 线程回填完成结果：写回 StoredPath、移动模式删源、失败 / 取消移除条目。</summary>
        void CompleteCopy(StationItem item, CardJob job, string target)
        {
            // P1-6：销毁后迟到的完成回调一律忽略（被取消的复制任务也可能走到这里）。
            if (_sink.IsDisposed) return;

            _copyDone++;
            _activeName = null;
            _activePercent = 0;
            _activeSpeed = 0;

            if (job.State == CardJobState.Succeeded && !string.IsNullOrEmpty(target))
            {
                item.StoredPath = target;
                item.Name = Path.GetFileName(target);
                item.Size = item.IsDirectory ? 0 : StationFiles.SafeSize(target);
                item.Missing = false;

                if (_station.Mode == "Move")
                {
                    try
                    {
                        // 盘符根 / 共享根绝不能进 Move 的删源分支；正常入口已拒收，
                        // 这里再守一次，防止手改 items.json 后点“移动”。
                        if (StationFiles.IsRootPath(item.Path))
                        {
                            Log.Warn("中转站移动模式拒绝删除根路径源: " + item.Path);
                            _sink.CopyStatus("已收纳，但源是根路径，未删除", CardStatusKind.Warning);
                        }
                        else if (item.IsDirectory) Directory.Delete(item.Path, true);
                        else File.Delete(item.Path);

                        if (!StationFiles.IsRootPath(item.Path))
                            _sink.CopyStatus("已移动 " + item.Name, CardStatusKind.Success);
                    }
                    catch (Exception ex)
                    {
                        // 复制已经成功，目标文件 / 文件夹保留；源删不掉只提示，不回滚目标。
                        _sink.CopyStatus("已收纳，但删除源失败：" + ex.Message, CardStatusKind.Warning);
                        // P1-5：源删除失败意味着磁盘上留了一份重复数据，必须让日志知道。
                        Log.Warn("中转站移动模式删除源失败（目标保留）  源=" + item.Path + "  原因=" + ex.Message);
                    }
                }
                else
                {
                    _sink.CopyStatus("已收纳 " + item.Name, CardStatusKind.Success);
                }
            }
            else if (job.State == CardJobState.Failed)
            {
                _sink.CopyItemRemoved(item);
                _sink.CopyStatus("收纳失败：" + (job.Error ?? "未知错误"), CardStatusKind.Error);
                // P1-5：静默只有卡片状态行有记录，卡片一关（换目录 / 删卡）这条信息就没了。
                Log.Error("中转站收纳失败  文件=" + item.Name + "  目标=" + target +
                          "  原因=" + (job.Error ?? "未知错误"));
            }
            else
            {
                _sink.CopyItemRemoved(item);
                _sink.CopyStatus("收纳已取消", CardStatusKind.Warning);
                Log.Info("中转站收纳已取消  文件=" + item.Name);
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
        }
    }
}
