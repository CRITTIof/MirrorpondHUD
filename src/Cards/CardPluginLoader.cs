using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 运行时卡片插件（第一版）：可信本地 DLL + manifest + 重启生效。
    ///
    /// 目录：<c>&lt;exe&gt;/plugins/cards/&lt;plugin-id&gt;/</c>，不可写时退到
    /// <c>%LOCALAPPDATA%\MirrorpondHUD\plugins\cards</c>。每个插件必须有 manifest.json。
    ///
    /// 契约版本先只支持 <see cref="SupportedApiVersion"/>；插件 ALC 对宿主程序集返回 null，
    /// 强制回落到默认 ALC，保证 <see cref="ICardModule"/> 是同一个 Type。
    /// 这不是安全沙箱：只加载用户明确安装的本地插件。
    /// </summary>
    internal static class CardPluginLoader
    {
        public const int SupportedApiVersion = 1;

        static readonly List<CardPluginLoadContext> _contexts = new List<CardPluginLoadContext>();

        /// <summary>扫描插件目录并返回成功实例化的模块；失败只记录，不阻止宿主启动。</summary>
        public static IList<ICardModule> LoadAll()
        {
            var modules = new List<ICardModule>();
            try
            {
                foreach (string root in PluginRoots())
                {
                    if (!Directory.Exists(root)) continue;

                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        try
                        {
                            ICardModule module = LoadOne(dir);
                            if (module != null) modules.Add(module);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("卡片插件加载失败(" + Path.GetFileName(dir) + "): " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("扫描卡片插件目录失败: " + ex.Message);
            }

            if (modules.Count == 0)
                Log.Info("未发现运行时卡片插件（plugins/cards）");
            return modules;
        }

        static IEnumerable<string> PluginRoots()
        {
            var roots = new List<string>(2);
            string portable = Path.Combine(AppContext.BaseDirectory, "plugins", "cards");
            if (Directory.Exists(portable)) roots.Add(portable);

            string local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MirrorpondHUD", "plugins", "cards");
            if (Directory.Exists(local) &&
                !string.Equals(local, portable, StringComparison.OrdinalIgnoreCase))
                roots.Add(local);

            return roots;
        }

        static ICardModule LoadOne(string dir)
        {
            string manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Log.Warn("卡片插件缺少 manifest.json: " + dir);
                return null;
            }

            PluginManifest manifest = Json.Deserialize<PluginManifest>(File.ReadAllText(manifestPath));
            if (manifest == null)
            {
                Log.Warn("卡片插件 manifest 解析为空: " + dir);
                return null;
            }

            if (manifest.apiVersion != SupportedApiVersion)
            {
                Log.Warn("卡片插件 API 版本不兼容: " + Safe(manifest.id) +
                         " api=" + manifest.apiVersion + "（宿主支持 " + SupportedApiVersion + "）");
                return null;
            }

            string assemblyName = (manifest.assembly ?? "").Trim();
            if (assemblyName.Length == 0 || !string.Equals(Path.GetFileName(assemblyName), assemblyName, StringComparison.Ordinal) ||
                assemblyName == "." || assemblyName == "..")
            {
                Log.Warn("卡片插件 assembly 字段非法: " + Safe(manifest.id));
                return null;
            }

            string assemblyPath = Path.Combine(dir, assemblyName);
            if (!File.Exists(assemblyPath))
            {
                Log.Warn("卡片插件程序集不存在: " + assemblyPath);
                return null;
            }

            string entryType = (manifest.entryType ?? "").Trim();
            if (entryType.Length == 0)
            {
                Log.Warn("卡片插件 entryType 为空: " + Safe(manifest.id));
                return null;
            }

            var context = new CardPluginLoadContext(assemblyPath);
            _contexts.Add(context); // 第一版：保持加载上下文存活，退出时随进程回收

            Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
            Type type = assembly.GetType(entryType, false);
            if (type == null)
            {
                Log.Warn("卡片插件入口类型不存在: " + entryType + "（" + Safe(manifest.id) + "）");
                return null;
            }

            if (type.IsAbstract || type.IsInterface ||
                !typeof(ICardModule).IsAssignableFrom(type) ||
                type.GetConstructor(Type.EmptyTypes) == null)
            {
                Log.Warn("卡片插件入口类型不符合 ICardModule 契约: " + entryType);
                return null;
            }

            var module = (ICardModule)Activator.CreateInstance(type);
            if (module == null) return null;

            Log.Info("已加载卡片插件 " + Safe(manifest.name) + " " + Safe(manifest.version) +
                     "（Kind=" + module.Kind + "，entry=" + entryType + "）");
            return module;
        }

        static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(未命名)" : value;
        }

        /// <summary>宿主程序集必须由默认 ALC 提供；插件自己的依赖走插件 ALC。</summary>
        sealed class CardPluginLoadContext : AssemblyLoadContext
        {
            readonly AssemblyDependencyResolver _resolver;
            readonly string _hostAssemblyName;

            public CardPluginLoadContext(string mainAssemblyPath)
                : base("MirrorpondHUD.CardPlugin:" + Path.GetFileNameWithoutExtension(mainAssemblyPath), true)
            {
                _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
                _hostAssemblyName = typeof(CardRegistry).Assembly.GetName().Name;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                if (string.Equals(assemblyName.Name, _hostAssemblyName, StringComparison.OrdinalIgnoreCase))
                    return null; // 走默认 ALC，保证契约 Type 身份一致

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
