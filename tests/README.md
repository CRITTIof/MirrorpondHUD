# MirrorpondHUD.Tests

纯逻辑回归测试（xUnit）。主程序与插件仍保持零第三方 NuGet 依赖；这里只有测试工程引用测试框架。

```bat
:: 正常环境
dotnet test tests\MirrorpondHUD.Tests\MirrorpondHUD.Tests.csproj -c Release

:: 受限环境（VSTest testhost 无法 OpenProcess 父进程时）
dotnet run --project tests\TestRunner\TestRunner.csproj -c Release
```

覆盖：`SettingsLoader` 版本边界（含大小写绕过）、`TemplateEngine` 对齐极值、`Json` 往返与未知键、`Settings.Normalize` 夹取、`AtomicFile` 原子写/备份、`ConfigStore` 隔离路径。
