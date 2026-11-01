using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MirrorpondHUD.Core
{
    public enum CardJobState
    {
        Pending,
        Running,
        Succeeded,
        Failed,
        Canceled
    }

    /// <summary>
    /// 一个可取消的后台任务（复制文件、导出、下载……）。
    /// 进度用 <see cref="IProgress{T}"/> 直接回报，状态线程安全；<see cref="Task"/> 可用于等待 / 串联。
    /// </summary>
    public sealed class CardJob : IProgress<double>
    {
        readonly CancellationTokenSource _cancellation;
        readonly object _sync = new object();

        internal CardJob(string name, CancellationTokenSource cancellation)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "任务" : name.Trim();
            _cancellation = cancellation;
        }

        public string Name { get; private set; }
        public CardJobState State { get; private set; }
        public double Progress { get; private set; }
        public string Error { get; private set; }

        /// <summary>整个任务（含排队）的 Task；异常已经被吃掉并转成 <see cref="CardJobState.Failed"/>。</summary>
        public Task Task { get; internal set; }

        public CancellationToken Token { get { return _cancellation.Token; } }

        public bool IsFinished
        {
            get
            {
                lock (_sync) return State == CardJobState.Succeeded || State == CardJobState.Failed || State == CardJobState.Canceled;
            }
        }

        public void Cancel()
        {
            try { _cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>0~1 的进度；越界会被夹紧，NaN 忽略。</summary>
        public void Report(double value)
        {
            if (double.IsNaN(value)) return;
            double clamped = Math.Max(0.0, Math.Min(1.0, value));
            lock (_sync) Progress = clamped;
        }

        internal void MarkRunning()
        {
            lock (_sync)
            {
                if (State == CardJobState.Pending) State = CardJobState.Running;
            }
        }

        internal void Finish(CardJobState state, string error = null)
        {
            lock (_sync)
            {
                State = state;
                Error = error;
                if (state == CardJobState.Succeeded) Progress = 1.0;
            }
        }
    }

    /// <summary>
    /// 串行（默认 1 并发）的后台任务队列：中转站的“同时拖入 20 个文件”会排队复制，
    /// 不会把磁盘和 UI 一起打满。队列本身线程安全，可在任意线程 Enqueue / CancelAll。
    /// </summary>
    public sealed class CardJobQueue : IDisposable
    {
        // 已结束任务在列表里保留的条数上限：Jobs 快照还要能看到“刚完成”的状态，
        // 但更早的必须移除，否则长时间大量复制会把 CardJob / Task / CTS 无限累积（P2-10）。
        const int FinishedRetention = 16;

        readonly SemaphoreSlim _gate;
        readonly List<CardJob> _jobs = new List<CardJob>();
        readonly object _sync = new object();
        bool _disposed;
        bool _gateDisposed;
        int _live;   // 还没结束的任务数（排队中 + 运行中）

        public CardJobQueue(int maxConcurrent = 1)
        {
            _gate = new SemaphoreSlim(Math.Max(1, maxConcurrent));
        }

        public CardJob Enqueue(string name, Func<CancellationToken, IProgress<double>, Task> work)
        {
            if (work == null) throw new ArgumentNullException("work");

            CardJob job;
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException("CardJobQueue");
                job = new CardJob(name, new CancellationTokenSource());
                _jobs.Add(job);
                _live++;
            }

            job.Task = RunAsync(job, work);
            return job;
        }

        /// <summary>
        /// 当前队列里的任务快照（数组拷贝，遍历时不会被并发修改影响）。
        /// P2-10：已结束的任务只保留最近 <see cref="FinishedRetention"/> 个 —— 以前这里只增不减。
        /// </summary>
        public IReadOnlyList<CardJob> Jobs
        {
            get { lock (_sync) return _jobs.ToArray(); }
        }

        public void CancelAll()
        {
            CardJob[] jobs;
            lock (_sync) jobs = _jobs.ToArray();

            foreach (CardJob job in jobs) job.Cancel();
        }

        public void Dispose()
        {
            bool disposeNow;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                disposeNow = _live == 0;
            }

            // 先取消：排队的任务在 WaitAsync 处会抛 OperationCanceledException 自己收尾。
            CancelAll();

            // 还有 in-flight 任务时**不能**立刻 Dispose 信号量：
            //  · 运行中的任务在 finally 里还要 _gate.Release()；
            //  · 排队中的任务可能还挂在 WaitAsync 上。
            // 立刻 Dispose 会让它们抛 ObjectDisposedException（P2-10 的竞态）。
            // 改成延迟到最后一个任务退出时再释放（CompleteJob → DisposeGate）。
            if (disposeNow) DisposeGate();
        }

        void DisposeGate()
        {
            lock (_sync)
            {
                if (_gateDisposed || _live > 0) return;
                _gateDisposed = true;
            }
            try { _gate.Dispose(); } catch { }
        }

        /// <summary>任务结束后：计数递减、剪掉过老的已结束任务，必要时释放信号量。</summary>
        void CompleteJob(CardJob job)
        {
            bool disposeNow;
            lock (_sync)
            {
                if (_live > 0) _live--;

                // 只移除“已经结束”的任务，保留最近 FinishedRetention 个；
                // 排队 / 运行中的任务绝不移除，所以 Jobs 的顺序语义不变。
                int finished = _jobs.Count - _live;
                for (int i = 0; i < _jobs.Count && finished > FinishedRetention; )
                {
                    if (_jobs[i].IsFinished)
                    {
                        _jobs.RemoveAt(i);
                        finished--;
                    }
                    else
                    {
                        i++;
                    }
                }

                disposeNow = _disposed && _live <= 0;
            }

            if (disposeNow) DisposeGate();
        }

        async Task RunAsync(CardJob job, Func<CancellationToken, IProgress<double>, Task> work)
        {
            bool acquired = false;
            try
            {
                try
                {
                    await _gate.WaitAsync(job.Token).ConfigureAwait(false);
                    acquired = true;
                }
                catch (OperationCanceledException)
                {
                    job.Finish(CardJobState.Canceled);
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // 防御：正常路径不该走到（信号量只在没有 in-flight 任务时才释放）。
                    // 真走到这里也按取消收尾，绝不把异常抛给调用方（P2-10）。
                    job.Finish(CardJobState.Canceled);
                    return;
                }

                job.MarkRunning();

                if (job.Token.IsCancellationRequested)
                {
                    job.Finish(CardJobState.Canceled);
                    return;
                }

                await work(job.Token, job).ConfigureAwait(false);

                job.Finish(job.Token.IsCancellationRequested ? CardJobState.Canceled : CardJobState.Succeeded);
            }
            catch (OperationCanceledException)
            {
                job.Finish(CardJobState.Canceled);
            }
            catch (Exception ex)
            {
                job.Finish(CardJobState.Failed, ex.Message);
            }
            finally
            {
                if (acquired)
                {
                    // 信号量只在没有 in-flight 任务时才会被释放，正常不会命中；
                    // 兜底吞掉，绝不把 ObjectDisposedException 抛回给 in-flight 任务（P2-10）。
                    try { _gate.Release(); }
                    catch (ObjectDisposedException) { }
                }

                CompleteJob(job);
            }
        }
    }
}
