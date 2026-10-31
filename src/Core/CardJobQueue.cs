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
        readonly SemaphoreSlim _gate;
        readonly List<CardJob> _jobs = new List<CardJob>();
        readonly object _sync = new object();
        bool _disposed;

        public CardJobQueue(int maxConcurrent = 1)
        {
            _gate = new SemaphoreSlim(Math.Max(1, maxConcurrent));
        }

        public CardJob Enqueue(string name, Func<CancellationToken, IProgress<double>, Task> work)
        {
            if (work == null) throw new ArgumentNullException("work");
            if (_disposed) throw new ObjectDisposedException("CardJobQueue");

            var job = new CardJob(name, new CancellationTokenSource());
            lock (_sync) _jobs.Add(job);

            job.Task = RunAsync(job, work);
            return job;
        }

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
            if (_disposed) return;
            _disposed = true;
            CancelAll();
            _gate.Dispose();
        }

        async Task RunAsync(CardJob job, Func<CancellationToken, IProgress<double>, Task> work)
        {
            try
            {
                await _gate.WaitAsync(job.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                job.Finish(CardJobState.Canceled);
                return;
            }
            catch (ObjectDisposedException)
            {
                job.Finish(CardJobState.Canceled);
                return;
            }

            try
            {
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
                _gate.Release();
            }
        }
    }
}
