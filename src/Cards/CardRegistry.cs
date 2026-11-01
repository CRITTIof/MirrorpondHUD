using System;
using System.Collections.Generic;
using System.Reflection;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 卡片注册表：反射扫描本程序集里所有 <see cref="ICardModule"/> 实现，
    /// 按 Order 排好。设置页的「添加卡片」菜单、默认内容、渲染都从这里取，
    /// 宿主里不再出现 switch(kind)。
    ///
    /// 运行时插件：<see cref="Initialize"/> 在启动早期把内置模块与插件模块合成一份只读快照；
    /// <see cref="ReplaceExternal"/> 仅供 --plugin-dev 热重载在 UI 线程替换外部模块。
    /// 未知 Kind 由 <see cref="Get"/> 返回「插件不可用」占位模块，不会把配置改写成默认卡。
    /// </summary>
    public static class CardRegistry
    {
        static readonly object Sync = new object();
        static List<ICardModule> _modules;
        static Dictionary<string, ICardModule> _lookup;
        static List<ICardModule> _builtins;
        static readonly Dictionary<string, ICardModule> _placeholders =
            new Dictionary<string, ICardModule>(StringComparer.OrdinalIgnoreCase);
        static bool _initialized;

        /// <summary>全部已注册的卡片模块（按添加菜单顺序；初始化后为只读快照）。</summary>
        public static IList<ICardModule> Modules
        {
            get
            {
                EnsureInitialized();
                return _modules.AsReadOnly();
            }
        }

        /// <summary>
        /// 启动早期调用一次：内置模块 + 外部插件模块。
        /// 必须在任何 <see cref="Modules"/> / <see cref="Get"/> / 配置归一化之前调用，否则只能拿到内置模块。
        /// </summary>
        public static void Initialize(IEnumerable<ICardModule> external)
        {
            lock (Sync)
            {
                if (_initialized)
                {
                    Log.Warn("卡片注册表已初始化，忽略运行时插件追加（插件必须在启动早期加载）");
                    return;
                }

                List<ICardModule> builtins = Discover();
                int added;
                List<ICardModule> modules = Merge(builtins, external, out added);
                Sort(modules);

                _builtins = new List<ICardModule>(builtins);
                _modules = modules;
                _lookup = BuildLookup(modules);
                _initialized = true;

                Log.Info("已注册卡片模块 " + modules.Count + " 个（内置 " + (modules.Count - added) +
                         "，运行时插件 " + added + "）: " +
                         string.Join("、", modules.ConvertAll(m => m.Kind).ToArray()));
            }
        }

        /// <summary>
        /// 替换外部插件模块，保留内置模块。仅供 --plugin-dev 热重载使用。
        /// 校验失败会抛异常且不改变当前注册表，调用方应保留旧模块。
        /// </summary>
        public static void ReplaceExternal(IEnumerable<ICardModule> external)
        {
            lock (Sync)
            {
                EnsureInitializedCore();

                var modules = new List<ICardModule>(_builtins);
                var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ICardModule module in _builtins)
                    if (module != null) kinds.Add(module.Kind);

                if (external != null)
                {
                    foreach (ICardModule module in external)
                    {
                        if (module == null)
                            throw new InvalidOperationException("运行时插件模块为空");

                        string kind = (module.Kind ?? "").Trim();
                        if (kind.Length == 0)
                            throw new InvalidOperationException("运行时插件模块 Kind 为空: " + module.GetType().FullName);

                        if (!kinds.Add(kind))
                            throw new InvalidOperationException("运行时插件卡片 Kind 重复或与内置模块冲突: " + kind);

                        modules.Add(module);
                    }
                }

                Sort(modules);
                _modules = modules;
                _lookup = BuildLookup(modules);
                _placeholders.Clear();

                Log.Info("运行时插件模块已替换：内置 " + _builtins.Count +
                         "，外部 " + (modules.Count - _builtins.Count) + "，共 " + modules.Count + " 个");
            }
        }

        public static ICardModule Get(string kind)
        {
            EnsureInitialized();

            ICardModule module;
            if (TryGet(kind, out module)) return module;

            string key = (kind ?? "").Trim();
            if (key.Length == 0) return _modules[0];

            // 旧 Kind（metrics / clock / keys / text）仍按别名找回模块，
            // 只有真正未知的外部插件 Kind 才进入占位卡。
            if (TryResolveAlias(key, out module)) return module;

            lock (Sync)
            {
                if (!_placeholders.TryGetValue(key, out module))
                {
                    module = new UnavailableCardModule(key);
                    _placeholders[key] = module;
                }
                return module;
            }
        }

        public static bool TryGet(string kind, out ICardModule module)
        {
            EnsureInitialized();
            module = null;
            string key = (kind ?? "").Trim().ToLowerInvariant();
            return key.Length > 0 && _lookup.TryGetValue(key, out module);
        }

        /// <summary>
        /// 未知 / 空的 Kind：空的回退默认卡片；非空但未知的保留原值。
        /// 不能把插件卡片改写成默认 Kind，否则插件卸载后配置会被静默破坏。
        /// </summary>
        public static string ResolveKind(string kind)
        {
            EnsureInitialized();
            string trimmed = (kind ?? "").Trim();
            ICardModule module;
            return TryGet(trimmed, out module) ? module.Kind : (trimmed.Length == 0 ? _modules[0].Kind : trimmed);
        }

        /// <summary>
        /// 旧版本用过的 Kind（如 metrics / clock / keys / text）→ 现在接管它们的模块。
        /// 别名由模块自己通过 <see cref="ICardLegacyAlias"/> 声明，宿主不再维护一份名单。
        /// </summary>
        public static bool TryResolveAlias(string kind, out ICardModule module)
        {
            EnsureInitialized();
            module = null;

            string key = (kind ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0) return false;

            foreach (ICardModule candidate in _modules)
            {
                var alias = candidate as ICardLegacyAlias;
                if (alias == null || alias.LegacyKinds == null) continue;

                foreach (string item in alias.LegacyKinds)
                {
                    if (string.Equals(item, key, StringComparison.OrdinalIgnoreCase))
                    {
                        module = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (Sync)
            {
                EnsureInitializedCore();
            }
        }

        static void EnsureInitializedCore()
        {
            if (_initialized) return;

            List<ICardModule> builtins = Discover();
            Sort(builtins);
            _builtins = new List<ICardModule>(builtins);
            _modules = builtins;
            _lookup = BuildLookup(builtins);
            _initialized = true;

            Log.Info("已注册内置卡片模块 " + builtins.Count + " 个: " +
                     string.Join("、", builtins.ConvertAll(m => m.Kind).ToArray()));
        }

        static List<ICardModule> Merge(List<ICardModule> builtins, IEnumerable<ICardModule> external, out int added)
        {
            var modules = new List<ICardModule>(builtins);
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ICardModule module in builtins)
                if (module != null) kinds.Add(module.Kind);

            added = 0;
            if (external == null) return modules;

            foreach (ICardModule module in external)
            {
                if (module == null) continue;

                string kind = (module.Kind ?? "").Trim();
                if (kind.Length == 0)
                {
                    Log.Warn("运行时插件模块 Kind 为空，已拒绝: " + module.GetType().FullName);
                    continue;
                }

                if (!kinds.Add(kind))
                {
                    Log.Warn("运行时插件卡片 Kind 与已有模块冲突，已拒绝: " + kind);
                    continue;
                }

                modules.Add(module);
                added++;
            }

            return modules;
        }

        static Dictionary<string, ICardModule> BuildLookup(List<ICardModule> modules)
        {
            var map = new Dictionary<string, ICardModule>(StringComparer.OrdinalIgnoreCase);
            foreach (ICardModule module in modules)
            {
                if (module == null) continue;
                map[module.Kind] = module;
            }
            return map;
        }

        static void Sort(List<ICardModule> modules)
        {
            modules.Sort((a, b) =>
            {
                int order = a.Order.CompareTo(b.Order);
                return order != 0 ? order : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
            });
        }

        static List<ICardModule> Discover()
        {
            var found = new List<ICardModule>();
            Assembly assembly = typeof(CardRegistry).Assembly;

            foreach (Type type in assembly.GetTypes())
            {
                try
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(ICardModule).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                    var module = (ICardModule)Activator.CreateInstance(type);
                    if (module != null) found.Add(module);
                }
                catch (Exception ex)
                {
                    Log.Warn("卡片模块加载失败 " + type.FullName + ": " + ex.Message);
                }
            }

            Sort(found);
            return found;
        }
    }
}
