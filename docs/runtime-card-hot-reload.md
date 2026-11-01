# 运行时卡片热插拔调研（v2.13.6 实验分支）

> 状态：调研，**不实现生产热插拔**。
> 基线：`feature/runtime-card-loading`（v2.13.6），第一版已支持“重启加载”的可信本地插件。
> 结论先行：文字卡可以做出 dev-only 热重载原型；富 WPF 卡片在现有窗口/内容/设置页引用链下不建议热插拔；
> 正式功能应保持“重启生效”，最多提供开发者开关。

---

## 1. 当前实现（可复用的部分）

| 层 | 现状 | 热插拔含义 |
|---|---|---|
| 插件目录 | `plugins/cards/<id>/manifest.json` + DLL + deps | 可监听目录变化 |
| 加载 | `CardPluginLoader.LoadAll()`，每插件一个可收集 `AssemblyLoadContext` | ALC 本身支持 `Unload()` |
| 注册表 | `CardRegistry.Initialize(external)` 一次性快照 | 需要新增“替换外部模块”入口 |
| 模块实例 | 插件 `ICardModule` 单例，`CardSlot.Module` 长期持有 | 替换时必须清空所有 slot 引用 |
| 富内容 | `IRichCardModule.CreateContent` 返回 `FrameworkElement` | 控件类型/资源/事件会钉住插件 ALC |
| 设置页 | `CardEditor`/`CardRegistry.Modules` 直接使用模块实例 | 需要重建编辑器与添加菜单 |
| 状态 | 每卡 `cards/<id>/`，配置 `Extras` 原样保留 | 可跨重载保留 |

加载器目前把 `CardPluginLoadContext` 放进静态 `_contexts` 列表保持存活；这是“重启生效”的实现，
也意味着当前代码**无法**卸载。

---

## 2. 为什么不能直接 `Unload()` 后 `LoadAll()`

### 2.1 引用链

一次典型插件卡片至少有以下引用：

```text
CardRegistry._modules[] ──► plugin module instance
CardSlot.Module          ──► plugin module instance
CardSlot.Rich            ──► plugin IRichCardModule
CardSlot.Content         ──► plugin FrameworkElement / 子控件
CardSlot.Host            ──► 宿主，不反向持有插件
OverlayWindow.Surface    ──► 视觉树 ──► plugin FrameworkElement
SettingsWindow           ──► CardRegistry.Modules / CardEditor / 设置控件
plugin 自己的 static/event/timer ──► plugin assembly
WPF ResourceDictionary / pack URI ──► plugin assembly
```

`AssemblyLoadContext.Unload()` 只是“请求卸载”；只要还有强引用，ALC 就不会被回收。
最容易漏掉的是：

- WPF 视觉树、`Dispatcher.BeginInvoke` 排队回调、`DispatcherTimer`；
- 插件静态事件 / 单例 / 缓存；
- `CardRegistry` 快照与设置页控件；
- `CardSettings.Extras` 中已绑定成插件类型的对象（如果插件把 DTO 类型放进默认 ALC 的配置对象树）；
- 插件通过 `pack://application:,,,/Plugin;component/...` 加载的 BAML/资源。

### 2.2 文字卡与富卡片差异

- **文字卡**（只实现 `ICardModule.Render`，不实现 `IRichCardModule`）：
  - 没有 `CardSlot.Content`，视觉树里只有宿主 `OverlaySurface` 的文本；
  - 热重载时释放模块引用、换模块、重画文本即可；
  - 可行性最高。
- **富卡片**（图片/视频/CLI 等）：
  - `CardSlot.Content`、窗口视觉树、事件订阅、后台线程/定时器都可能由插件创建；
  - 需要插件实现完整的 `Dispose`/停止线程契约，而当前 `ICardModule` 没有统一的生命周期接口；
  - 即便 `ReleaseContent` 调 `IDisposable`，WPF 资源/BAML 仍可能让 ALC 假卸载；
  - 不建议第一版承诺。

### 2.3 当前插件 API 缺少的契约

- 没有 `ICardModule.Dispose` / `Shutdown`：热替换时无法要求插件停线程/解绑事件；
- 没有插件级版本/状态查询：设置页无法显示“旧实例仍被引用”；
- `CardRegistry` 没有“替换外部模块”的线程安全入口；
- `OverlayController` 没有“按插件刷新卡片”的 API，只能走全量 `ApplySettings`；
- 没有 ALC `Unloading` 的日志/弱引用验证。

---

## 3. 可行的 dev-only 原型（建议下一步，不进入正式 UI）

### 3.1 范围

- 只在 `--plugin-dev` 启动参数下启用；
- 只允许 **文字卡** 热重载；检测到 `IRichCardModule` 时拒绝并提示“重启生效”；
- 插件目录变化后 debounce 500ms，重新执行 `CardPluginLoader.LoadAll()`；
- 旧 ALC 卸载后强制 GC + `WeakReference` 验证；
- 失败回退到“旧模块继续用/需要重启”，不能破坏配置。

### 3.2 需要的宿主 API（最小）

1. `CardRegistry.ReplaceExternal(IEnumerable<ICardModule>)`
   - 保留内置模块；
   - 只替换外部模块快照；
   - 同 Kind 冲突拒绝；
   - 返回被替换的旧模块，便于确认引用已释放。
2. `CardPluginLoader` 返回 `PluginHandle { Module, Context, Manifest }`
   - 卸载时 `context.Unload()`；
   - `_contexts` 改成可移除的句柄列表；
   - 弱引用验证 `WeakReference`。
3. `OverlayController.ReloadPluginCards(ISet<string> kinds)`
   - 对受影响 CardSlot：`ReleaseContent` → 换 `Module/Rich` → 重建窗口内容；
   - 文字卡不重建窗口，只重画；
   - 设置页关闭或 Reload，避免旧 `CardEditor` 持有模块。
4. 生命周期契约（可选）
   - 新增 `ICardModuleLifetime`（`OnPluginUnloading`），由插件自行停线程/解绑；
   - 不实现该接口的插件在 dev 热重载时保守拒绝。

### 3.3 伪代码

```csharp
// 仅在 --plugin-dev
var old = CardRegistry.ExternalModules;
var handles = CardPluginLoader.LoadAllWithHandles();
CardRegistry.ReplaceExternal(handles.Select(h => h.Module));
Controller.ReloadPluginCards(affectedKinds);
foreach (var handle in oldHandles) handle.Context.Unload();

for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
bool alive = weakRefs.Any(r => r.IsAlive);
```

### 3.4 验收

- 修改文字卡 DLL → 重启热重载 → 卡片文本立即变化；
- 插件被删除 → 卡片显示“插件不可用”占位（当前未知 Kind 仍回退默认卡，需要先做占位卡）；
- 重载后旧 `WeakReference` 全部回收；
- 富卡片检测到后拒绝热重载并提示重启；
- 设置页 / 添加菜单不残留旧模块；
- 连续重载 20 次无窗口/定时器/句柄增长。

---

## 4. 生产建议

1. **正式功能保持“重启生效”**：当前 `plugins/cards` + manifest 的安装方式足够；
2. 不在设置页提供“卸载/重载”按钮，避免用户误以为可以安全热替换；
3. 若要开发者体验，提供 `--plugin-dev` + 仅文字卡；
4. 富 WPF 卡片的热插拔不做承诺；若未来必须做，先设计插件 `Dispose/Shutdown` 契约，并用
   `WeakReference` + 强制 GC 验证，不以“UNLOAD 调用成功”当成功；
5. 插件缺失时先实现占位卡（保留 Kind/Extras、显示“插件不可用”），这是热重载和卸载体验的前置。

---

## 5. 风险清单

- ALC 不是安全沙箱；热重载只解决开发期替换，不解决信任；
- WPF 资源字典 / BAML 可能把插件程序集钉在默认上下文；
- 插件静态字段、线程、`DispatcherTimer`、事件订阅是主要泄漏源；
- 替换注册表时若设置窗/卡片窗口持有旧模块，可能崩溃或显示旧 UI；
- 卸载失败不能直接报错退出，应保留旧模块并提示“重启生效”；
- `CardSettings.Extras` 的插件类型绑定要避免长期缓存，否则配置对象树会钉住 ALC。

---

## 6. 参考

- `src/Cards/CardPluginLoader.cs`（当前 ALC 加载）
- `src/Cards/CardRegistry.cs`（一次性快照）
- `src/Ui/CardSlot.cs`（模块/富内容引用）
- `src/Ui/OverlayController.cs`（`SyncCards` / `ReleaseContent`）
- `src/Ui/SettingsWindow.xaml.cs`（`CardEditor` / `Modules`）
- [runtime-card-extensions.md](runtime-card-extensions.md)（第一版设计）
- [.NET AssemblyLoadContext unloadability](https://learn.microsoft.com/dotnet/standard/assembly/unloadability)
