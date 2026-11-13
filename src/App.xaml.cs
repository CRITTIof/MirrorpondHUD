using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD;

public partial class App : Application
{
    Mutex _mutex;
    OverlayController _controller;

    /// <summary>
    /// G-12：单实例唤醒事件。拿到互斥后立即创建（早于插件加载 / 配置读取），
    /// 走出启动 try 时转交给控制器；启动失败时由 OnExit 兜底释放。
    /// </summary>
    EventWaitHandle _activation;
    IList<CardPluginHandle> _plugins;

    /// <summary>UI 线程异常的最大容忍次数：超过后即使类型在白名单里也不再吞掉。</summary>
    const int MaxSwallowedUiExceptions = 5;
    int _swallowedUiExceptions;

    /// <summary>P1-8：插件卸载探针由 App 持有，退出收尾时执行弱引用验证。</summary>
    CardPluginUnloadProbe _unloadProbe;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            Log.Error("未处理异常", args.ExceptionObject as Exception);

        DispatcherUnhandledException += (s, args) =>
        {
            Exception ex = args.Exception;
            // P1-1：不能无脑 Handled=true——只有白名单类型（剪贴板 / COM / Win32 句柄 / 已释放对象
            // 这类临时性外部失败）可以在主界面上继续运行，且计入次数；
            // 超过上限、主界面还没起来、或者白名单之外的类型都视为不可信状态，直接退出。
            if (_controller is not null && ex is not null && IsRecoverableUiException(ex) &&
                _swallowedUiExceptions < MaxSwallowedUiExceptions)
            {
                _swallowedUiExceptions++;
                Log.Error($"UI 线程异常（类型可恢复，已吞掉第 {_swallowedUiExceptions}/{MaxSwallowedUiExceptions} 次）", ex);
                args.Handled = true;
                return;
            }

            Log.Error("UI 线程异常不可恢复（白名单外 / 超限 / 主界面未启动），应用将退出", ex);
            // 自绘对话框失败时会自行回退系统窗口（见 HudDialog.Show），提示不会静默消失。
            HudDialog.ShowError(null, "镜潭一览 · 退出",
                "应用已处于不可信状态，即将退出。\n\n异常：" +
                (ex is null ? "(null)" : ex.Message) +
                "\n\n详细信息已写入日志：\n" + Log.FilePath);
            // Handled=false 会让 CLR 继续终止进程（并可能弹 WER），Shutdown(1) 的退出码不会生效。
            // 已记录日志并告知用户，这里接管退出，保证退出码可控（X-2）。
            args.Handled = true;
            Shutdown(1);
        };

        Log.Info($"App 启动  参数=[{string.Join(" ", e.Args)}]  版本={typeof(App).Assembly.GetName().Version}  日志={Log.FilePath}");
        Log.Info($"进程完整性级别: {ProcessIntegrity.Name}（{ProcessIntegrity.Level:X}）{(ProcessIntegrity.Level < 0 ? $"  查询失败: {ProcessIntegrity.LastErrorText}" : "")}");

        if (ProcessIntegrity.IsRestricted)
        {
            Log.Warn("进程运行在低完整性级别，Windows 会拒绝托盘图标、全局热键和键盘钩子");
            HudDialog.ShowWarning(null, "镜潭一览 · 权限受限",
                "程序当前运行在低完整性级别（Low Integrity）环境中。\n\n" +
                "这种情况下 Windows 会禁止：托盘图标、全局热键、全局键盘钩子。\n\n" +
                "请把整个 bin\\Release 目录复制到一个你有完全控制权的普通文件夹\n" +
                "（例如「文档」）后再运行。\n\n" +
                "日志：" + Log.FilePath);
        }

        // X-16：唤醒事件必须在本进程拿到互斥体之前创建。这样只要首实例还活着，
        // 第二实例就能立刻找到事件，不需要在 UI 线程上 Sleep 等它把事件建出来。
        _activation = OverlayController.CreateActivationEvent(out string activationError);
        if (_activation is null)
            Log.Warn($"创建单实例唤醒事件失败，第二实例将无法唤起本进程: {activationError}");

        _mutex = new Mutex(true, "Local\\MirrorpondHUD.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // 立即模式（自检 / 截图）必须是“真跑过”才算通过。已有实例在运行时走唤醒分支
            // 再 Shutdown(0)，会让 --selftest 一项没跑却返回 0，等于伪造绿灯。
            if (CommandLineMode.HasImmediateMode(e.Args))
            {
                Log.Error("检测到已有实例在运行，拒绝执行 --selftest / --screenshot；请先退出正在运行的窗口");
                Shutdown(2);
                return;
            }

            // 首实例现在在拿到互斥后立刻创建唤醒事件（见下面），冷启动窗口期第二次双击
            // 基本不会再遇到“事件还没建好”。这里仍保留有界短重试（100 毫秒），覆盖首实例刚起来、
            // 事件对象正在创建 / 被杀毒软件短暂延迟的极端情况。
            Log.Info("已有实例在运行，等待唤醒句柄");
            EventWaitHandle activation = null;
            Exception lastError = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting(OverlayController.ActivationEventName, out activation))
                    {
                        lastError = null;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // UnauthorizedAccessException 等：继续等也没用，但至少要留下原因。
                    lastError = ex;
                    break;
                }
                Thread.Sleep(25);
            }

            if (activation is null)
            {
                Log.Warn($"唤醒已有实例失败: {(lastError is null ? "100 毫秒内没有找到唤醒事件（首实例可能启动失败或事件创建失败）" : lastError.Message)}");
            }
            else
            {
                using (activation)
                    activation.Set();
            }
            Shutdown(0);
            return;
        }

        // X-16：唤醒事件已在取互斥体之前创建（见上），这里不再重复创建。

        List<string> args = e.Args.ToList();
        Settings settings;

        try
        {
            // 运行时卡片插件必须在第一次访问 CardRegistry / 配置归一化 / 截图之前加载。
            // 插件先影子复制再加载（源目录不锁文件）；替换 / 更新插件后需要重启宿主。
            // 插件损坏 / 版本不兼容不影响宿主启动。
            CardPluginLoader.CleanupShadowCache();
            CardPluginLoadReport pluginReport = CardPluginLoader.LoadAll();
            _plugins = pluginReport.Handles;
            CardRegistry.Initialize(_plugins.Select(h => h.Module));
            // Core 的 ConfigStore 不认识卡片；由宿主在第一次 Load 之前注入归一化钩子。
            ConfigStore.SettingsNormalizer = CardModel.Normalize;
            if (pluginReport.HasErrors)
                Log.Warn($"运行时插件有 {pluginReport.Errors.Count} 个加载失败，详见上面的 WARN");

            // 截图 / 自检这类「跑完即退出」的模式仍在启动 try 内执行：
            // 内部未捕获的异常走下面的初始化失败路径（记录 + 提示 + Shutdown(1)）。
            if (CommandLineMode.TryRunImmediate(args, out var immediateExitCode))
            {
                Shutdown(immediateExitCode);
                return;
            }

            // 运行时卡片插件已在启动早期加载（见 args 之后）。
            settings = ConfigStore.Load();
            // P1-1：启动日志补全配置与插件上下文，出问题时日志自带现场。
            Log.Info($"配置已加载  文件={ConfigStore.Path}  schema={settings.SchemaVersion}/{Settings.CurrentSchemaVersion}  插件={_plugins.Count} 个");
            if (!string.IsNullOrEmpty(ConfigStore.LastLoadWarning))
            {
                HudDialog.ShowWarning(null, "镜潭一览 · 设置文件需要处理",
                    ConfigStore.LastLoadWarning + "\n\n配置位置：" + ConfigStore.Path,
                    settings.Style);
            }
        }
        catch (Exception ex)
        {
            // P1-1：初始化阶段任何失败都必须落地（日志 + 用户可见提示），绝不静默。
            Log.Error("启动初始化失败（插件加载 / 配置读取）", ex);
            HudDialog.ShowError(null, "镜潭一览 · 启动失败",
                "启动失败：" + ex.Message + "\n\n详细信息已写入日志：\n" + Log.FilePath);
            Shutdown(1);
            return;
        }

        // --smoke：摘参数、改写可见性开关并起计时器；其余参数原样交给 OverlayController。
        CommandLineMode.StartSmokeIfRequested(args, Shutdown);

        try
        {
            _controller = new OverlayController(settings, args, _activation);
            _controller.Start();

        }
        catch (Exception ex)
        {
            Log.Error("启动失败", ex);
            HudDialog.ShowError(null, "镜潭一览",
                "启动失败：" + ex.Message + "\n\n详细信息已写入日志：\n" + Log.FilePath);
            Shutdown(1);
        }
    }

    /// <summary>
    /// UI 线程异常白名单：这些类型多数来自外部资源（剪贴板 / COM 组件 / Win32 句柄 /
    /// 已释放的临时窗口），失败发生在局部、不影响全局状态，可以继续运行；
    /// 其余类型（NullReferenceException、OutOfMemoryException 等）一律视为不可信。
    /// </summary>
    static bool IsRecoverableUiException(Exception ex)
    {
        for (Exception cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is InvalidOperationException ||
                cur is System.Runtime.InteropServices.COMException ||
                cur is System.ComponentModel.Win32Exception ||
                cur is ObjectDisposedException)
                return true;
        }
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // P1-1：不管走到哪条退出路径（正常 / 第二实例 / 启动失败），都以日志收尾留痕。
        Log.Info($"App 退出 code={e.ApplicationExitCode}");

        try { _controller?.Dispose(); }
        catch (Exception ex) { Log.Warn($"主控制器收尾失败: {ex.Message}"); }
        // 插件卸载探针前必须断开 App 对控制器的强引用，否则 _cards[].Module 会
        // 稳定把 6 个模块全判成“未回收”（P-5）。
        finally { _controller = null; }

        // G-12：事件句柄的所有权已经转交控制器（正常路径由它释放）；这里只兜底
        // “控制器构造 / 启动失败”的路径，重复 Dispose 对 WaitHandle 是安全的。
        if (_activation is not null)
        {
            try { _activation.Dispose(); }
            catch (Exception ex) { Log.Debug($"唤醒事件释放失败: {ex.Message}"); }
            _activation = null;
        }

        // 先把注册表里的插件模块引用摘掉，再请求卸载 ALC；否则静态注册表会一直钉住旧程序集。
        // 第二实例在互斥检查处就会退出，此时 _plugins 还是 null，不能走这里的清理日志。
        if (_plugins is not null)
        {
            try { CardRegistry.ClearExternal(); }
            catch (Exception ex) { Log.Warn($"CardRegistry 清理失败: {ex.Message}"); }
            try { _unloadProbe = CardPluginLoader.Unload(_plugins); }
            catch (Exception ex) { Log.Error("插件卸载请求失败", ex); }
            // 字段还钉着 Module，会挡住弱引用回收；卸载请求发出后立即摘掉。
            _plugins = null;
        }

        try { _mutex?.ReleaseMutex(); }
        catch (Exception ex) { Log.Debug($"ReleaseMutex 失败（可能本来就没持有）: {ex.Message}"); }
        try { _mutex?.Dispose(); }
        catch (Exception ex) { Log.Debug($"Mutex Dispose 失败: {ex.Message}"); }
        base.OnExit(e);

        // P1-8：卸载探针接回宿主。Unload 只发出卸载请求，弱引用必须在当前调用栈收尾后
        // 才能判定；OnExit 的收尾位置（WPF 清理完成、无代码再持有模块引用）是退出流程里
        // 最早的可验证点。Verify 内部幂等且自带日志（泄漏 / 正常回收）。
        try { _unloadProbe?.Verify(); }
        catch (Exception ex) { Log.Warn($"卸载探针验证失败: {ex.Message}"); }

        // X-8：日志是异步写的，退出流程结束前把队列排空（最多 2 秒），
        // 否则最后几条（包括上面的探针结论）可能随进程一起消失。
        if (!Log.Flush(2000)) Log.Warn("退出时日志队列未在 2 秒内排空，可能有少量记录丢失");
    }
}
