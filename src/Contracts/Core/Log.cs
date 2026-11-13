using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MirrorpondHUD.Core;

/// <summary>日志最小输出等级；数值越小越详细。</summary>
public enum LogSeverity
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

/// <summary>
/// 极简文件日志。优先写到 exe 同目录（便携、权限问题最少），
/// 不可写时退到 %LOCALAPPDATA%\MirrorpondHUD，最后退到临时目录。
/// 可用环境变量 MIRRORPONDHUD_LOG 指定绝对路径。
///
/// 写失败不再永久熔断：按指数冷却重试（期间日志丢弃但进程不受影响），
/// 并把失败的记录降级写到 %TEMP%\MirrorpondHUD\mirrorpond-fallback.log，
/// 保证"最需要日志的时候日志恰好失效"这种场景有迹可寻。
///
/// X-8：磁盘 IO 不再发生在调用线程上。调用方只把记录放进有界队列，
/// 由一条后台线程串行写文件；这样 UI 线程 / 渲染线程 / 采样线程打日志时
/// 不会因为磁盘慢（杀软扫描、网络盘、日志轮转）被拖住。
/// 退出时 <see cref="Flush"/> 会把队列排空（App.OnExit 与 ProcessExit 都会调），
/// 崩溃路径仍保留同步降级：队列满 / 程序退出后直接写，绝不无限积压。
/// </summary>
public static class Log
{
    static readonly object Gate = new();

    const long MaxBytes = 512 * 1024;   // 单文件上限
    const int KeepBackups = 3;          // 镜像pond.log.1/.2/.3，总量约 2 MB
    const int MaxRetryDelayMs = 30_000;

    /// <summary>X-8：队列上限；满了就丢弃并计数，宁可丢日志也不能拖住调用线程。</summary>
    const int QueueCapacity = 8192;

    static string _path;
    static int _writeFailures;
    // 冷却窗口内被丢弃的条数：恢复后补一条汇总，避免“故障瞬间没有日志、事后也不知道丢了多少”。
    static int _droppedDuringCooldown;
    // X-8：队列溢出丢弃的条数（与磁盘冷却分开统计）。
    static int _droppedInQueue;
    static DateTime _nextRetryUtc = DateTime.MinValue;

    static bool? _debugEnabled;

    /// <summary>当前最小输出等级；设置页「行为 → 日志等级」控制，默认 Info。</summary>
    static int _minLevel = (int)LogSeverity.Info;

    public static LogSeverity MinLevel
    {
        get => (LogSeverity)Volatile.Read(ref _minLevel);
        set => Volatile.Write(ref _minLevel, (int)value);
    }

    /// <summary>按设置字符串设置最小等级；非法值由 NormalizeLogLevel 回落 Info。</summary>
    public static void SetMinLevel(string value) => MinLevel = ParseSeverity(Settings.NormalizeLogLevel(value));

    static LogSeverity ParseSeverity(string level) =>
        string.Equals(level, "Debug", StringComparison.OrdinalIgnoreCase) ? LogSeverity.Debug
        : string.Equals(level, "Warn", StringComparison.OrdinalIgnoreCase) ? LogSeverity.Warn
        : string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase) ? LogSeverity.Error
        : LogSeverity.Info;

    static LogSeverity SeverityOf(string level) =>
        string.Equals(level, "DEBUG", StringComparison.OrdinalIgnoreCase) ? LogSeverity.Debug
        : string.Equals(level, "WARN", StringComparison.OrdinalIgnoreCase) ? LogSeverity.Warn
        : string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase) ? LogSeverity.Error
        : LogSeverity.Info;

    readonly struct Entry
    {
        public readonly string Level;
        public readonly string Message;

        public Entry(string level, string message)
        {
            Level = level;
            Message = message;
        }
    }

    static readonly BlockingCollection<Entry> Queue = new(new ConcurrentQueue<Entry>(), QueueCapacity);

    /// <summary>已入队但还没写完的条数；Flush 用它判断是否排空。</summary>
    static int _pending;

    static Log()
    {
        var writer = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "MirrorpondHUD.LogWriter"
        };
        writer.Start();

        // 进程退出（含未走 App.OnExit 的路径）时再兜一次，尽量不丢最后几条。
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Flush(2000);
    }

    /// <summary>日志文件路径（第一次访问时决定并缓存）。</summary>
    public static string FilePath
    {
        get
        {
            if (_path is not null) return _path;
            lock (Gate) { if (_path is null) _path = ResolvePath(); }
            return _path;
        }
    }

    static string ResolvePath()
    {
        string overridePath = Environment.GetEnvironmentVariable("MIRRORPONDHUD_LOG");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        // 1) exe 同目录：最直观，也不受 %LOCALAPPDATA% 被重定向的影响
        try
        {
            string exeDir = AppContext.BaseDirectory;
            if (CanWrite(exeDir)) return Path.Combine(exeDir, "mirrorpond.log");
        }
        catch { }

        // 2) %LOCALAPPDATA%
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                string dir = Path.Combine(local, "MirrorpondHUD");
                Directory.CreateDirectory(dir);
                if (CanWrite(dir)) return Path.Combine(dir, "mirrorpond.log");
            }
        }
        catch { }

        // 3) 临时目录
        try
        {
            string temp = Path.Combine(Path.GetTempPath(), "MirrorpondHUD");
            Directory.CreateDirectory(temp);
            return Path.Combine(temp, "mirrorpond.log");
        }
        catch { }

        return "mirrorpond.log";
    }

    static bool CanWrite(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir)) return false;
            string probe = Path.Combine(dir, $".wo-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>DEBUG 级别默认关闭；MIRRORPONDHUD_DEBUG=1 / true 打开。</summary>
    static bool DebugEnabled
    {
        get
        {
            if (!_debugEnabled.HasValue)
            {
                string value = Environment.GetEnvironmentVariable("MIRRORPONDHUD_DEBUG");
                _debugEnabled = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
            return _debugEnabled.Value;
        }
    }

    public static void Debug(string msg)
    {
        // 环境变量仍可强制打开 Debug（排查启动期问题）；否则跟随设置里的最小等级。
        if (DebugEnabled || MinLevel <= LogSeverity.Debug) Write("DEBUG", msg);
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);

    /// <summary>
    /// 错误与堆栈**必须写在同一条记录**里（以前堆栈单独一条，轮转 / 并发后与消息错位）；
    /// 内层异常只带一行摘要（类型 + 消息），堆栈一层就够。
    /// </summary>
    public static void Error(string msg, Exception ex = null)
    {
        if (ex is null)
        {
            Write("ERROR", msg);
            return;
        }

        var sb = new StringBuilder();
        sb.Append(msg).Append(" :: ").Append(ex.GetType().Name).Append(": ");
        sb.Append(string.IsNullOrEmpty(ex.Message) ? "(无消息)" : ex.Message);

        string stack = ex.StackTrace;
        if (!string.IsNullOrWhiteSpace(stack))
        {
            foreach (string line in stack.Replace("\r\n", "\n").Split('\n'))
                sb.Append(Environment.NewLine).Append("    at ").Append(line.Trim());
        }

        Exception inner = ex.InnerException;
        for (int depth = 0; inner is not null && depth < 3; depth++)
        {
            sb.Append(Environment.NewLine).Append("  内部: ").Append(inner.GetType().Name);
            sb.Append(": ").Append(string.IsNullOrEmpty(inner.Message) ? "(无消息)" : inner.Message);
            inner = inner.InnerException;
        }

        Write("ERROR", sb.ToString());
    }

    /// <summary>
    /// 把队列里的记录尽量写完。返回 false 表示超时后仍未排空（剩余记录已经被本线程
    /// 同步写掉，返回 false 只用于调用方记录“这次退出可能丢日志”）。
    /// </summary>
    public static bool Flush(int timeoutMs = 2000)
    {
        if (Queue.Count == 0 && Volatile.Read(ref _pending) == 0) return true;

        var watch = Stopwatch.StartNew();
        while (Volatile.Read(ref _pending) > 0 && watch.ElapsedMilliseconds < Math.Max(1, timeoutMs))
            Thread.Sleep(5);

        if (Volatile.Read(ref _pending) <= 0) return true;

        // 超时：由当前线程把剩下的写掉（Gate 串行化磁盘写），避免退出时整段丢失。
        while (Queue.TryTake(out Entry entry))
        {
            WriteEntry(entry);
            Interlocked.Decrement(ref _pending);
        }

        return Volatile.Read(ref _pending) <= 0;
    }

    static void Write(string level, string msg)
    {
        // 日志等级过滤放在入队之前：低于最小等级的记录既不占队列也不碰磁盘。
        if (SeverityOf(level) < MinLevel) return;

        // X-8：先计数再入队，写线程消费后减回去；队列满则丢弃并计数。
        Interlocked.Increment(ref _pending);
        if (!Queue.TryAdd(new Entry(level, msg)))
        {
            Interlocked.Decrement(ref _pending);
            Interlocked.Increment(ref _droppedInQueue);
        }
    }

    static void WriterLoop()
    {
        foreach (Entry entry in Queue.GetConsumingEnumerable())
        {
            try { WriteEntry(entry); }
            finally
            {
                if (Interlocked.Decrement(ref _pending) < 0) Interlocked.Exchange(ref _pending, 0);
            }
        }
    }

    /// <summary>真正的磁盘写；只在写线程（或 Flush 超时后的退出路径）调用。</summary>
    static void WriteEntry(Entry entry)
    {
        lock (Gate)
        {
            // 写失败后的冷却窗口：期间直接丢弃，不做无意义的 IO 重试；
            // 窗口一过就再试，所以最坏情况是"短暂丢日志"，不会永久熔断。
            if (_nextRetryUtc > DateTime.UtcNow)
            {
                _droppedDuringCooldown++;
                return;
            }

            try
            {
                string path = FilePath;

                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                catch { }

                RotateIfNeeded(path);

                int dropped = _droppedDuringCooldown;
                int queueDropped = Interlocked.Exchange(ref _droppedInQueue, 0);
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {(entry.Message ?? "")}{Environment.NewLine}";
                if (dropped > 0)
                {
                    _droppedDuringCooldown = 0;
                    line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WARN] 日志写入恢复，冷却期间丢弃 {dropped} 条记录{Environment.NewLine}" + line;
                }
                if (queueDropped > 0)
                {
                    line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WARN] 日志队列溢出，丢弃 {queueDropped} 条记录{Environment.NewLine}" + line;
                }

                File.AppendAllText(path, line, Encoding.UTF8);

                if (_writeFailures > 0) _writeFailures = 0;
                _nextRetryUtc = DateTime.MinValue;
            }
            catch
            {
                _writeFailures++;
                // 500ms → 1s → 2s → 4s → 8s → 上限 30s：失败期间不刷盘，恢复后继续。
                int delay = Math.Min(MaxRetryDelayMs, 500 * (1 << Math.Min(_writeFailures - 1, 6)));
                _nextRetryUtc = DateTime.UtcNow.AddMilliseconds(delay);

                // 降级：把这条日志按原样写到临时目录，主日志恢复后两边会有一段时间都存在；
                // 调用方排查时至少不会丢掉故障瞬间的记录。降级失败彻底忽略，绝不能抛。
                try
                {
                    string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string fallbackDir = string.IsNullOrEmpty(localData)
                        ? Path.Combine(Path.GetTempPath(), "MirrorpondHUD")
                        : Path.Combine(localData, "MirrorpondHUD");

                    File.AppendAllText(Path.Combine(fallbackDir, "mirrorpond-fallback.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [FALLBACK {entry.Level}] {(entry.Message ?? "")}{Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch { }
            }
        }
    }

    /// <summary><c>mirrorpond.log → .1 → .2 → .3</c> 链式轮转，共保留约 (KeepBackups+1)×512KB。</summary>
    static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxBytes) return;

            // 先删最旧的，再逐级让位；每一步失败独立吞掉（只读盘 / 杀软锁单个文件都轮不转）。
            string oldest = $"{path}.{KeepBackups}";
            try { if (File.Exists(oldest)) File.Delete(oldest); } catch { }

            for (int i = KeepBackups - 1; i >= 1; i--)
            {
                string from = $"{path}.{i}";
                string to = $"{path}.{(i + 1)}";
                try { if (File.Exists(from)) File.Move(from, to, true); } catch { }
            }

            try { File.Move(path, $"{path}.1", true); } catch { }
        }
        catch { }
    }
}
