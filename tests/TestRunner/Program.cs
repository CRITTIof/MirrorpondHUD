using System;
using System.IO;
using System.Threading;
using Xunit.Runners;

namespace TestRunner;

/// <summary>
/// 进程内 xUnit 运行器：部分受限环境里 VSTest testhost 无法 OpenProcess 父进程，
/// 这个入口直接用 xunit.runner.utility 反射执行测试程序集，不需要 testhost。
/// </summary>
internal static class Program
{
    static int Main(string[] args)
    {
        string assembly = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "MirrorpondHUD.Tests.dll");

        if (!File.Exists(assembly))
        {
            Console.Error.WriteLine($"找不到测试程序集: {assembly}");
            return 2;
        }

        int passed = 0;
        int failed = 0;
        int skipped = 0;
        var completed = new ManualResetEventSlim(false);

        using (var runner = AssemblyRunner.WithoutAppDomain(assembly))
        {
            runner.OnTestPassed = info => { Interlocked.Increment(ref passed); Console.WriteLine($"[PASS] {info.TestDisplayName}"); };
            runner.OnTestFailed = info =>
            {
                Interlocked.Increment(ref failed);
                Console.WriteLine($"[FAIL] {info.TestDisplayName}: {info.ExceptionMessage}");
            };
            runner.OnTestSkipped = info => { Interlocked.Increment(ref skipped); Console.WriteLine($"[SKIP] {info.TestDisplayName}"); };
            runner.OnExecutionComplete = info => completed.Set();

            runner.Start();
            if (!completed.Wait(TimeSpan.FromMinutes(2)))
            {
                Console.Error.WriteLine("测试运行超时");
                return 3;
            }
        }

        Console.WriteLine($"passed={passed} failed={failed} skipped={skipped}");
        return failed == 0 ? 0 : 1;
    }
}
