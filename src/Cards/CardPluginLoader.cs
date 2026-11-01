using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 一个成功加载的插件：模块实例、可收集 ALC，以及源目录 / 影子目录。
    /// 影子复制让插件目录里的 DLL 可以在宿主运行时被覆盖（下一次加载用新文件），
    /// 同时避免直接从源目录加载导致文件被锁。
    /// </summary>
    internal sealed class CardPluginHandle
    {
        public CardPluginHandle(string id, string name, string version,
                                string sourceDirectory, string shadowDirectory,
                                AssemblyLoadContext context, ICardModule module)
        {
            Id = id ?? "";
            Name = name ?? "";
            Version = version ?? "";
            SourceDirectory = sourceDirectory ?? "";
            ShadowDirectory = shadowDirectory ?? "";
            Context = context;
            Module = module;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; }
        public string SourceDirectory { get; private set; }
        public string ShadowDirectory { get; private set; }

        public ICardModule Module { get; internal set; }

        internal AssemblyLoadContext Context { get; private set; }

        /// <summary>只实现文字渲染、没有 WPF 富内容（仅用于加载日志分类）。</summary>
        public bool IsTextOnly
        {
            get
            {
                if (Module == null) return true;
                return !(Module is IRichCardModule);
            }
        }

        internal void Release()
        {
            Module = null;
            Context = null;
        }
    }

    /// <summary>一次插件扫描的结果：成功加载的句柄 + 失败原因（仅用于启动日志与诊断）。</summary>
    internal sealed class CardPluginLoadReport
    {
        readonly List<CardPluginHandle> _handles;
        readonly List<string> _errors;

        public CardPluginLoadReport(List<CardPluginHandle> handles, List<string> errors)
        {
            _handles = handles ?? new List<CardPluginHandle>();
            _errors = errors ?? new List<string>();
        }

        public IList<CardPluginHandle> Handles { get { return _handles.AsReadOnly(); } }
        public IList<string> Errors { get { return _errors.AsReadOnly(); } }
        public bool HasErrors { get { return _errors.Count > 0; } }
    }

    /// <summary>
    /// 一次卸载的延迟验证句柄。Unload 只负责请求卸载、摘掉宿主引用；
    /// 弱引用判定必须等当前调用栈 / Dispatcher 队列 / WPF 绑定清理都走完，
    /// 否则会把仍可达（且马上会释放）的模块误报成泄漏。
    /// </summary>
    internal sealed class CardPluginUnloadProbe
    {
        readonly List<WeakReference> _modules = new List<WeakReference>();
        readonly List<WeakReference> _contexts = new List<WeakReference>();
        readonly List<string> _shadowDirs = new List<string>();
        readonly List<string> _names = new List<string>();
        bool _verified;

        internal void Add(CardPluginHandle handle)
        {
            if (handle == null) return;

            if (handle.Module != null) _modules.Add(new WeakReference(handle.Module));
            if (handle.Context != null) _contexts.Add(new WeakReference(handle.Context));

            _names.Add(handle.Id);
            if (!string.IsNullOrEmpty(handle.ShadowDirectory)) _shadowDirs.Add(handle.ShadowDirectory);
        }

        /// <summary>强制 GC 后检查弱引用；在 UI 线程的 ApplicationIdle / 延迟定时器里调用。</summary>
        public void Verify() { Verify(true); }

        /// <summary><paramref name="log"/> 为 false 时不写回收结论（用于丢弃从未挂到 UI 的候选模块）。</summary>
        public void Verify(bool log)
        {
            if (_verified) return;
            _verified = true;

            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            int aliveModules = 0;
            foreach (WeakReference reference in _modules)
                if (reference.IsAlive) aliveModules++;

            int aliveContexts = 0;
            foreach (WeakReference reference in _contexts)
                if (reference.IsAlive) aliveContexts++;

            if (log)
            {
                if (aliveModules > 0)
                    Log.Warn("插件 ALC 卸载后仍有模块引用：模块 " + aliveModules + "/" + _modules.Count +
                             "，上下文 " + aliveContexts + "/" + _contexts.Count +
                             "（" + string.Join("、", _names.ToArray()) + "）；文件将在下次启动清理");
                else if (aliveContexts > 0)
                    Log.Info("插件模块已回收，ALC 上下文仍在卸载（运行时延迟回收；开发模式可接受）: " +
                             string.Join("、", _names.ToArray()));
                else
                    Log.Info("插件 ALC 已卸载（模块 / 上下文弱引用均已回收）: " +
                             string.Join("、", _names.ToArray()));
            }

            foreach (string shadowDir in _shadowDirs)
                CardPluginLoader.TryDeleteDirectory(shadowDir);
        }
    }

    /// <summary>
    /// 运行时卡片插件（2.14.0）：可信本地 DLL + manifest + 默认重启生效。
    ///
    /// 目录：<c>&lt;exe&gt;/plugins/cards/&lt;plugin-id&gt;/</c>，不可写时退到
    /// <c>%LOCALAPPDATA%\MirrorpondHUD\plugins\cards</c>。每个插件必须有 manifest.json。
    ///
    /// 插件加载前会复制到 <c>%LOCALAPPDATA%\MirrorpondHUD\plugin-cache</c> 再加载，
    /// 这样插件目录里的 DLL 不会被宿主锁住，可以随时覆盖 / 删除；下一次启动用新文件。
    ///
    /// 契约版本先只支持 <see cref="SupportedApiVersion"/>；插件 ALC 对宿主 / Contracts / UiKit 程序集返回 null，
    /// 强制回落到默认 ALC，保证 <see cref="ICardModule"/> 是同一个 Type。
    /// 这不是安全沙箱：只加载用户明确安装的本地插件。
    /// </summary>
    internal static class CardPluginLoader
    {
        public const int SupportedApiVersion = 2;
        const int MaxManifestBytes = 256 * 1024;

        /// <summary>便携插件根目录（exe 同目录）。</summary>
        public static string PortableRoot
        {
            get { return Path.Combine(AppContext.BaseDirectory, "plugins", "cards"); }
        }

        /// <summary>用户级插件根目录。</summary>
        public static string LocalRoot
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return string.IsNullOrEmpty(local)
                    ? ""
                    : Path.Combine(local, "MirrorpondHUD", "plugins", "cards");
            }
        }

        static string ShadowRoot
        {
            get
            {
                try
                {
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (!string.IsNullOrEmpty(local))
                        return Path.Combine(local, "MirrorpondHUD", "plugin-cache");
                }
                catch { }

                return Path.Combine(Path.GetTempPath(), "MirrorpondHUD", "plugin-cache");
            }
        }

        /// <summary>启动时清掉上一次进程留下的影子副本。必须在第一批插件加载前调用。</summary>
        public static void CleanupShadowCache()
        {
            try
            {
                string root = ShadowRoot;
                if (!Directory.Exists(root)) return;

                int removed = 0;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        removed++;
                    }
                    catch { }
                }

                if (removed > 0) Log.Info("已清理插件影子缓存 " + removed + " 个");
            }
            catch (Exception ex)
            {
                Log.Warn("清理插件影子缓存失败: " + ex.Message);
            }
        }

        /// <summary>扫描插件目录并返回成功实例化的句柄；失败只记录，不阻止宿主启动。</summary>
        public static CardPluginLoadReport LoadAll()
        {
            var handles = new List<CardPluginHandle>();
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in ExistingRoots())
            {
                string[] dirs;
                try
                {
                    dirs = Directory.GetDirectories(root);
                }
                catch (Exception ex)
                {
                    string error = "扫描插件目录失败(" + root + "): " + ex.Message;
                    Log.Warn(error);
                    errors.Add(error);
                    continue;
                }

                foreach (string dir in dirs)
                {
                    CardPluginHandle handle;
                    try
                    {
                        handle = LoadOne(dir, errors);
                    }
                    catch (Exception ex)
                    {
                        string error = Path.GetFileName(dir) + ": " + ex.Message;
                        Log.Warn("卡片插件加载失败(" + Path.GetFileName(dir) + "): " + ex.Message);
                        errors.Add(error);
                        continue;
                    }

                    if (handle == null) continue;

                    if (!ids.Add(handle.Id))
                    {
                        string error = "插件 id 重复: " + handle.Id + "（" + handle.SourceDirectory + "）";
                        Log.Warn("卡片插件 id 重复，已拒绝: " + handle.Id + "（" + handle.SourceDirectory + "）");
                        errors.Add(error);
                        Discard(handle);
                        continue;
                    }

                    string kind = (handle.Module == null ? "" : handle.Module.Kind ?? "").Trim();
                    if (kind.Length == 0)
                    {
                        string error = "插件卡片 Kind 为空: " + handle.Id;
                        Log.Warn("卡片插件 Kind 为空，已拒绝: " + handle.Id);
                        errors.Add(error);
                        Discard(handle);
                        continue;
                    }
                    if (!kinds.Add(kind))
                    {
                        string error = "插件卡片 Kind 重复: " + kind + "（" + handle.Id + "）";
                        Log.Warn("卡片插件 Kind 重复，已拒绝: " + kind + "（" + handle.Id + "）");
                        errors.Add(error);
                        Discard(handle);
                        continue;
                    }

                    handles.Add(handle);
                }
            }

            if (handles.Count == 0 && errors.Count == 0)
                Log.Info("未发现运行时卡片插件（plugins/cards）");
            else
                Log.Info("运行时插件加载完成：成功 " + handles.Count + " 个，失败 " + errors.Count + " 个" +
                         (handles.Count == 0 ? "" : "（" +
                          string.Join("、", handles.ConvertAll(h => h.Id).ToArray()) + "）"));

            return new CardPluginLoadReport(handles, errors);
        }

        static IEnumerable<string> ExistingRoots()
        {
            var roots = new List<string>(2);
            string portable = PortableRoot;
            if (Directory.Exists(portable)) roots.Add(portable);

            string local = LocalRoot;
            if (local.Length > 0 && Directory.Exists(local) &&
                !string.Equals(local, portable, StringComparison.OrdinalIgnoreCase))
                roots.Add(local);

            return roots;
        }

        static CardPluginHandle LoadOne(string dir, IList<string> errors)
        {
            string label = Path.GetFileName(dir);
            string manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                string error = label + ": 缺少 manifest.json";
                Log.Warn("卡片插件缺少 manifest.json: " + dir);
                errors.Add(error);
                return null;
            }

            PluginManifest manifest = null;
            try
            {
                var info = new FileInfo(manifestPath);
                if (info.Length > MaxManifestBytes)
                {
                    string error = label + ": manifest.json 超过 " + MaxManifestBytes + " 字节";
                    Log.Warn("卡片插件 manifest 过大: " + manifestPath);
                    errors.Add(error);
                    return null;
                }

                manifest = Json.Deserialize<PluginManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                string error = label + ": manifest 读取失败: " + ex.Message;
                Log.Warn("卡片插件 manifest 读取失败(" + label + "): " + ex.Message);
                errors.Add(error);
                return null;
            }

            if (manifest == null)
            {
                string error = label + ": manifest 解析为空";
                Log.Warn("卡片插件 manifest 解析为空: " + manifestPath);
                errors.Add(error);
                return null;
            }

            string id = (manifest.id ?? "").Trim();
            if (!IsSafePluginId(id))
            {
                string error = label + ": manifest id 非法";
                Log.Warn("卡片插件 id 非法: " + Safe(id) + "（" + dir + "）");
                errors.Add(error);
                return null;
            }

            if (manifest.apiVersion != SupportedApiVersion)
            {
                string error = label + ": API 版本不兼容 api=" + manifest.apiVersion;
                Log.Warn("卡片插件 API 版本不兼容: " + id + " api=" + manifest.apiVersion +
                         "（宿主支持 " + SupportedApiVersion + "）");
                errors.Add(error);
                return null;
            }

            string assemblyName = (manifest.assembly ?? "").Trim();
            if (!IsSafeAssemblyFileName(assemblyName))
            {
                string error = label + ": assembly 字段非法";
                Log.Warn("卡片插件 assembly 字段非法: " + id);
                errors.Add(error);
                return null;
            }

            string entryType = (manifest.entryType ?? "").Trim();
            if (entryType.Length == 0 || entryType.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
            {
                string error = label + ": entryType 非法";
                Log.Warn("卡片插件 entryType 非法: " + Safe(id));
                errors.Add(error);
                return null;
            }

            string shadowDir = null;
            string loadDir = dir;
            AssemblyLoadContext context = null;
            try
            {
                try
                {
                    shadowDir = CreateShadowDirectory(id);
                    CopyDirectory(dir, shadowDir);
                    loadDir = shadowDir;
                }
                catch (Exception shadowEx)
                {
                    // 影子复制失败（LocalAppData 不可写 / 杀软拦截 / 磁盘满）不能直接判插件无效；
                    // 回退到源目录加载，但源 DLL 会被锁住，在线覆盖 / 删除可能不可用。
                    Log.Warn("插件影子复制失败，回退源目录加载（源 DLL 会被锁，在线覆盖可能不可用）: " +
                             id + ": " + shadowEx.Message);
                    TryDeleteDirectory(shadowDir);
                    shadowDir = null;
                    loadDir = dir;
                }

                string assemblyPath = Path.Combine(loadDir, assemblyName);
                if (!File.Exists(assemblyPath))
                {
                    string error = label + ": 程序集不存在 " + assemblyName;
                    Log.Warn("卡片插件程序集不存在: " + assemblyPath);
                    errors.Add(error);
                    TryDeleteDirectory(shadowDir);
                    return null;
                }

                context = new CardPluginLoadContext(id, assemblyPath);
                Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);

                // 2.14.4 起不再维护宿主类型转发：插件必须只引用 Contracts（需要控件时加 UiKit），
                // 引用宿主程序集的插件明确拒绝，避免“看起来能加载”的隐性兼容。
                string hostAssemblyName = typeof(CardRegistry).Assembly.GetName().Name;
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (!string.Equals(reference.Name, hostAssemblyName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string error = label + ": 插件引用了宿主程序集 MirrorpondHUD，必须改为引用 MirrorpondHUD.Contracts";
                    Log.Warn("卡片插件引用了宿主程序集，已拒绝: " + id +
                             "（请改为引用 MirrorpondHUD.Contracts）");
                    errors.Add(error);
                    TryDiscardContext(context, shadowDir);
                    return null;
                }

                Type type = assembly.GetType(entryType, false);
                if (type == null)
                {
                    string error = label + ": 入口类型不存在 " + entryType;
                    Log.Warn("卡片插件入口类型不存在: " + entryType + "（" + id + "）");
                    errors.Add(error);
                    TryDiscardContext(context, shadowDir);
                    return null;
                }

                if (type.IsAbstract || type.IsInterface ||
                    !typeof(ICardModule).IsAssignableFrom(type) ||
                    type.GetConstructor(Type.EmptyTypes) == null)
                {
                    string error = label + ": 入口类型不符合 ICardModule 契约";
                    Log.Warn("卡片插件入口类型不符合 ICardModule 契约: " + entryType);
                    errors.Add(error);
                    TryDiscardContext(context, shadowDir);
                    return null;
                }

                var module = (ICardModule)Activator.CreateInstance(type);
                if (module == null)
                {
                    string error = label + ": 入口类型实例化为空";
                    errors.Add(error);
                    TryDiscardContext(context, shadowDir);
                    return null;
                }

                string kind = (module.Kind ?? "").Trim();
                if (!IsSafeKind(kind))
                {
                    string error = label + ": 模块 Kind 非法或为空";
                    Log.Warn("卡片插件 Kind 非法: " + Safe(kind) + "（" + id + "）");
                    errors.Add(error);
                    TryDiscardContext(context, shadowDir);
                    return null;
                }

                var handle = new CardPluginHandle(
                    id,
                    Safe(manifest.name),
                    Safe(manifest.version),
                    dir,
                    shadowDir,
                    context,
                    module);

                Log.Info("已加载卡片插件 " + handle.Name + " " + handle.Version +
                         "（Kind=" + module.Kind + "，entry=" + entryType + "，" +
                         (handle.IsTextOnly ? "文字卡" : "富内容卡") + "，" +
                         (string.IsNullOrEmpty(shadowDir) ? "源目录（未复制）" : "影子副本") + "）");
                return handle;
            }
            catch (Exception ex)
            {
                string error = label + ": " + ex.Message;
                Log.Warn("卡片插件加载失败(" + label + "): " + ex.Message);
                errors.Add(error);
                TryDiscardContext(context, shadowDir);
                return null;
            }
        }

        /// <summary>
        /// 请求卸载一批插件：先通知生命周期接口，再 Unload ALC，返回延迟验证句柄。
        /// 注意不要在这里立刻做弱引用判定——当前调用栈 / WPF 绑定可能还持有模块，
        /// 调用方应在 Dispatcher 空闲后再执行 <see cref="CardPluginUnloadProbe.Verify"/>。
        /// </summary>
        public static CardPluginUnloadProbe Unload(IEnumerable<CardPluginHandle> handles)
        {
            if (handles == null) return null;

            var list = new List<CardPluginHandle>();
            foreach (CardPluginHandle handle in handles)
                if (handle != null) list.Add(handle);

            if (list.Count == 0) return null;

            var probe = new CardPluginUnloadProbe();

            foreach (CardPluginHandle handle in list)
            {
                probe.Add(handle);

                try
                {
                    var lifetime = handle.Module as ICardModuleLifetime;
                    if (lifetime != null) lifetime.OnPluginUnloading();
                }
                catch (Exception ex)
                {
                    Log.Warn("插件卸载回调失败(" + handle.Id + "): " + ex.Message);
                }

                try
                {
                    if (handle.Context != null) handle.Context.Unload();
                }
                catch (Exception ex)
                {
                    Log.Warn("插件 ALC 卸载请求失败(" + handle.Id + "): " + ex.Message);
                }

                handle.Release();
            }

            return probe;
        }

        static string CreateShadowDirectory(string pluginId)
        {
            string root = ShadowRoot;
            Directory.CreateDirectory(root);

            string safe = SanitizeFolderName(pluginId);
            string name = safe + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" +
                          Guid.NewGuid().ToString("N").Substring(0, 8);
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length)
                                      .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                string parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Copy(file, target, true);
            }
        }

        static void Discard(CardPluginHandle handle)
        {
            if (handle == null) return;
            try
            {
                if (handle.Context != null) handle.Context.Unload();
            }
            catch { }
            handle.Release();
            TryDeleteDirectory(handle.ShadowDirectory);
        }

        static void TryDiscardContext(AssemblyLoadContext context, string shadowDir)
        {
            try
            {
                if (context != null) context.Unload();
            }
            catch { }
            TryDeleteDirectory(shadowDir);
        }

        internal static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // 可能仍被 ALC/文件映射持有；下次启动 CleanupShadowCache 再删。
            }
        }

        static bool IsSafePluginId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return false;
            if (id == "." || id == "..") return false;

            foreach (char c in id)
            {
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
                    return false;
            }
            return true;
        }

        static bool IsSafeKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind) || kind.Length > 64) return false;
            if (kind != kind.Trim()) return false;

            foreach (char c in kind)
            {
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
                    return false;
            }
            return true;
        }

        static bool IsSafeAssemblyFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 260) return false;
            if (name == "." || name == "..") return false;
            if (name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0) return false;
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        static string SanitizeFolderName(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) text = "plugin";
            if (text.Length > 40) text = text.Substring(0, 40);

            var chars = new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                chars[i] = (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') ? c : '_';
            }
            string safe = new string(chars).Trim('.', '-');
            return safe.Length == 0 ? "plugin" : safe;
        }

        static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(未命名)" : value;
        }

        /// <summary>宿主 / Contracts / UiKit 程序集必须由默认 ALC 提供；插件自己的依赖走插件 ALC。</summary>
        sealed class CardPluginLoadContext : AssemblyLoadContext
        {
            readonly AssemblyDependencyResolver _resolver;
            readonly string _hostAssemblyName;
            readonly string _contractAssemblyName;
            readonly string _uiKitAssemblyName;

            public CardPluginLoadContext(string pluginId, string mainAssemblyPath)
                : base("MirrorpondHUD.CardPlugin:" + pluginId, true)
            {
                _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
                _hostAssemblyName = typeof(CardRegistry).Assembly.GetName().Name;
                _contractAssemblyName = typeof(ICardModule).Assembly.GetName().Name;
                _uiKitAssemblyName = typeof(MirrorpondHUD.Ui.Controls.CardButton).Assembly.GetName().Name;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                // 宿主 / 契约 / UiKit 都必须走默认 ALC，否则插件与宿主拿到的类型会是两份。
                if (string.Equals(assemblyName.Name, _hostAssemblyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assemblyName.Name, _contractAssemblyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assemblyName.Name, _uiKitAssemblyName, StringComparison.OrdinalIgnoreCase))
                    return null;

                string path = _resolver.ResolveAssemblyToPath(assemblyName);
                return path == null ? null : LoadFromAssemblyPath(path);
            }
        }

        // Json 通过反射写字段，编译器看不到赋值；这不是未初始化字段。
#pragma warning disable CS0649
        // 字段名必须与 manifest 的 camelCase 键一致：Json.Bind 按字段名精确匹配。
        sealed class PluginManifest
        {
            public string id = "";
            public string name = "";
            public string version = "";
            public int apiVersion;
            public string entryType = "";
            public string assembly = "";
            public string author = "";
        }
#pragma warning restore CS0649
    }
}
