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
    /// </summary>
    public static class CardRegistry
    {
        static readonly List<ICardModule> _modules = Discover();
        static readonly Dictionary<string, ICardModule> _lookup = BuildLookup();

        /// <summary>全部已注册的卡片模块（按添加菜单顺序）。</summary>
        public static IList<ICardModule> Modules { get { return _modules; } }

        public static ICardModule Get(string kind)
        {
            ICardModule module;
            return TryGet(kind, out module) ? module : _modules[0];
        }

        public static bool TryGet(string kind, out ICardModule module)
        {
            module = null;
            string key = (kind ?? "").Trim().ToLowerInvariant();
            return key.Length > 0 && _lookup.TryGetValue(key, out module);
        }

        /// <summary>未知 / 空的 Kind 一律退回默认卡片（系统信息）。</summary>
        public static string ResolveKind(string kind)
        {
            ICardModule module;
            return TryGet(kind, out module) ? module.Kind : _modules[0].Kind;
        }

        /// <summary>
        /// 旧版本用过的 Kind（如 metrics / clock / keys / text）→ 现在接管它们的模块。
        /// 别名由模块自己通过 <see cref="ICardLegacyAlias"/> 声明，宿主不再维护一份名单。
        /// </summary>
        public static bool TryResolveAlias(string kind, out ICardModule module)
        {
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

        static Dictionary<string, ICardModule> BuildLookup()
        {
            var map = new Dictionary<string, ICardModule>(StringComparer.OrdinalIgnoreCase);
            foreach (ICardModule module in _modules)
            {
                if (module == null) continue;
                map[module.Kind] = module;
            }
            return map;
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

            found.Sort((a, b) =>
            {
                int order = a.Order.CompareTo(b.Order);
                return order != 0 ? order : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
            });

            Log.Info("已注册卡片模块 " + found.Count + " 个: " +
                     string.Join("、", found.ConvertAll(m => m.Kind).ToArray()));
            return found;
        }
    }
}
