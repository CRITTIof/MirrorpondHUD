using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// --plugin-dev：开发期插件目录监听 + 文字卡热重载。
    ///
    /// 生产模式不支持热插拔，插件默认重启生效；这个类只在显式传入 --plugin-dev 时启动。
    /// 安全策略：
    /// - 只接受没有 IRichCardModule 的插件（WPF 富内容无法保证 ALC 卸载）；
    /// - 目录变化先 debounce，避免 dotnet build 边写边被当成多次更新；
    /// - 先完整加载新模块，任何一个目录加载失败就放弃本轮、保留旧模块；
    /// - 先替换注册表再刷新卡片，最后 Unload 旧 ALC，并用弱引用记录回收情况；
    /// - 任何失败都不改写配置、不退出宿主。
    /// </summary>
    internal sealed class PluginDevReloader : IDisposable
    {
        readonly OverlayController _controller;
        readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        readonly List<CardPluginUnloadProbe> _pendingProbes = new List<CardPluginUnloadProbe>();
        readonly DispatcherTimer _debounce;
        DispatcherTimer _verifyTimer;
        List<CardPluginHandle> _handles;
        bool _reloading;
        bool _disposed;

        public PluginDevReloader(OverlayController controller, IList<CardPluginHandle> handles)
        {
            _controller = controller;
            _handles = new List<CardPluginHandle>();
            if (handles != null)
            {
                foreach (CardPluginHandle handle in handles)
                    if (handle != null) _handles.Add(handle);
            }

            _debounce = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _debounce.Tick += (s, e) =>
            {
                _debounce.Stop();
                ReloadIfChanged();
            };
        }

        public void Start()
        {
            foreach (string root in CardPluginLoader.WatchRoots())
            {
                if (string.IsNullOrWhiteSpace(root)) continue;

                try
                {
                    Directory.CreateDirectory(root);

                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                       NotifyFilters.LastWrite | NotifyFilters.Size,
                        Filter = "*.*",
                        InternalBufferSize = 32 * 1024
                    };
                    watcher.Changed += OnChanged;
                    watcher.Created += OnChanged;
                    watcher.Deleted += OnChanged;
                    watcher.Renamed += OnRenamed;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);

                    Log.Info("插件热重载监听: " + root);
                }
                catch (Exception ex)
                {
                    Log.Warn("插件热重载监听失败(" + root + "): " + ex.Message);
                }
            }

            if (_watchers.Count == 0)
                Log.Warn("插件热重载未启用：没有可监听的插件目录");
            else
                Log.Info("插件开发热重载已启用（--plugin-dev）：只支持文字卡，修改 manifest/DLL 后自动重载");
        }

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;
            Schedule();
        }

        void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (_disposed) return;
            Schedule();
        }

        void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Log.Warn("插件目录监听错误: " + (e == null || e.GetException() == null ? "未知" : e.GetException().Message));
        }

        void Schedule()
        {
            if (_disposed) return;

            var dispatcher = System.Windows.Application.Current == null
                ? null
                : System.Windows.Application.Current.Dispatcher;
            if (dispatcher == null) return;

            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed) return;
                    _debounce.Stop();
                    _debounce.Start();
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Warn("插件热重载调度失败: " + ex.Message);
            }
        }

        void ReloadIfChanged()
        {
            if (_disposed || _reloading) return;
            _reloading = true;

            try
            {
                Log.Info("检测到插件目录变化，开始热重载");

                CardPluginLoadReport report = CardPluginLoader.LoadAll();
                if (report.HasErrors)
                {
                    Log.Warn("插件热重载放弃：本次扫描有 " + report.Errors.Count +
                             " 个加载错误，保留旧模块（详见上面的 WARN）");
                    Discard(report);
                    return;
                }

                string unsafeReason;
                foreach (CardPluginHandle handle in report.Handles)
                {
                    if (IsSafeForHotReload(handle, out unsafeReason)) continue;
                    Log.Warn("插件热重载只支持纯文字卡，检测到 " + handle.Id + "：" + unsafeReason + "；请重启宿主");
                    Discard(report);
                    return;
                }

                foreach (CardPluginHandle handle in _handles)
                {
                    if (IsSafeForHotReload(handle, out unsafeReason)) continue;
                    Log.Warn("插件热重载只支持纯文字卡，当前已加载 " + handle.Id + "：" + unsafeReason + "；请重启宿主");
                    Discard(report);
                    return;
                }

                try
                {
                    CardRegistry.ReplaceExternal(SelectModules(report.Handles));
                }
                catch (Exception ex)
                {
                    Log.Warn("插件热重载注册失败，保留旧模块: " + ex.Message);
                    Discard(report);
                    return;
                }

                List<CardPluginHandle> old = _handles;
                _handles = new List<CardPluginHandle>();
                foreach (CardPluginHandle handle in report.Handles)
                    _handles.Add(handle);

                try
                {
                    _controller.ReloadPluginCards();
                }
                catch (Exception ex)
                {
                    // 注册表已经是新模块，卡片刷新失败时不再卸载旧 ALC，避免视觉树仍引用旧类型。
                    Log.Error("插件热重载刷新卡片失败，旧插件保留到重启", ex);
                    return;
                }

                ScheduleVerify(CardPluginLoader.Unload(old));
                Log.Info("插件热重载完成：当前 " + _handles.Count + " 个插件");
            }
            catch (Exception ex)
            {
                Log.Error("插件热重载异常，保留当前模块", ex);
            }
            finally
            {
                _reloading = false;
            }
        }

        static bool IsSafeForHotReload(CardPluginHandle handle, out string reason)
        {
            reason = "";
            if (handle == null || handle.Module == null)
            {
                reason = "模块为空";
                return false;
            }

            if (handle.Module is IRichCardModule)
            {
                reason = "富内容卡片（WPF 控件无法保证 ALC 卸载）";
                return false;
            }

            if (handle.Module.IsInteractive)
            {
                reason = "交互卡片（键盘独占 / 生命周期由宿主管理）";
                return false;
            }

            if (handle.Module is ICardEditorSection)
            {
                reason = "提供 WPF 设置区块（设置页控件会钉住 ALC）";
                return false;
            }

            return true;
        }

        static void Discard(CardPluginLoadReport report)
        {
            if (report == null) return;
            CardPluginUnloadProbe probe = CardPluginLoader.Unload(report.Handles);
            if (probe != null) probe.Verify(false);
        }

        void ScheduleVerify(CardPluginUnloadProbe probe)
        {
            if (probe == null || _disposed) return;

            _pendingProbes.Add(probe);

            if (_verifyTimer == null)
            {
                _verifyTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(800)
                };
                _verifyTimer.Tick += (s, e) =>
                {
                    _verifyTimer.Stop();
                    VerifyPending();
                };
            }

            _verifyTimer.Stop();
            _verifyTimer.Start();
        }

        void VerifyPending()
        {
            if (_disposed || _pendingProbes.Count == 0) return;

            var probes = new List<CardPluginUnloadProbe>(_pendingProbes);
            _pendingProbes.Clear();

            foreach (CardPluginUnloadProbe probe in probes)
            {
                try { probe.Verify(); } catch (Exception ex) { Log.Warn("插件回收验证失败: " + ex.Message); }
            }
        }

        /// <summary>退出时卸载当前这一批（热重载后 App 手里的初始句柄已经不是最新的）。</summary>
        public void UnloadCurrent()
        {
            List<CardPluginHandle> handles = _handles;
            _handles = new List<CardPluginHandle>();

            CardPluginUnloadProbe probe = CardPluginLoader.Unload(handles);
            if (probe != null) probe.Verify(false);
        }

        static IEnumerable<ICardModule> SelectModules(IEnumerable<CardPluginHandle> handles)
        {
            var modules = new List<ICardModule>();
            foreach (CardPluginHandle handle in handles)
            {
                if (handle != null && handle.Module != null) modules.Add(handle.Module);
            }
            return modules;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _debounce.Stop(); } catch { }

            foreach (FileSystemWatcher watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
            }
            _watchers.Clear();
        }
    }
}
