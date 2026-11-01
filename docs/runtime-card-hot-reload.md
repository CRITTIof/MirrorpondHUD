# 运行时卡片热重载（v2.14.5 基线，dev-only 文字卡）

> 状态：**dev-only 纯文字卡热重载自 2.14.0 起可用**（启动参数 `--plugin-dev`）；2.14.5 基线的契约与共享控件
> 分别在 `MirrorpondHUD.Contracts` / `MirrorpondHUD.UiKit` 程序集里。生产默认仍是「重启生效」，设置页不提供热重载 / 卸载按钮；富内容卡拒绝热重载。
> 基线：`feature/runtime-card-loading`（v2.13.6 调研 / spike，v2.14.0 落地实现；v2.14.1 拆 Contracts、
> v2.14.2 拆 Core→Cards、v2.14.3 迁 `ThemeTokens` / `ThemeResolver`、v2.14.4 删类型转发升 API 2、v2.14.5 拆 UiKit）。
> 结论先行：文字卡 dev 热重载可用；富 WPF 卡片在现有窗口 / 内容 / 设置页引用链下仍不建议热插拔；
> `AssemblyLoadContext` 不是安全沙箱。独立风险复核见 `%TEMP%\hot-reload-review\report.md`，
> 实测日志见 `%TEMP%\mp-hotreload-run\mirrorpond.log`。

---

## 1. 实现概览（2.14.5 基线）

| 层 | 2.14.5 基线实现 | 热重载行为 |
|---|---|---|
| 程序集边界 | 2.14.1 起契约 / DTO 在 `MirrorpondHUD.Contracts`；2.14.5 起 8 个共享 WPF 控件在 `MirrorpondHUD.UiKit`；宿主 ProjectReference 两者 | 插件只引用 Contracts（可选 UiKit），不引用宿主；旧二进制不再兼容（2.14.4） |
| ALC 程序集例外 | `CardPluginLoadContext.Load` 对宿主 `MirrorpondHUD` / 契约 `MirrorpondHUD.Contracts` / UiKit `MirrorpondHUD.UiKit` 都返回 `null` | 三者都由默认 ALC 提供，保证 `ICardModule` 等契约 / 控件 Type 身份一致 |
| 插件目录 | `<exe>/plugins/cards/<id>/` + `%LOCALAPPDATA%\MirrorpondHUD\plugins\cards\<id>/` | 两个根都会监听（不存在时自动创建），变化 700 ms debounce |
| 加载 | `CardPluginLoader.LoadAll()`，每插件一个可收集 ALC；先影子复制再加载 | 每轮重新完整加载 + manifest / Kind 校验 |
| 注册表 | `CardRegistry.Initialize` 启动快照 + `ReplaceExternal` | 换外部模块时保留内置模块，Kind 冲突抛错 |
| 模块实例 | `CardPluginHandle` 持有模块 / ALC / 源目录 / 影子目录 | 句柄列表整体替换 |
| 卡片刷新 | `OverlayController.ReloadPluginCards` | 只重建 `Module` 引用变化的槽位；文字卡直接重画 |
| 设置页 | `SettingsWindow.ReleaseCardEditorsForPluginReload` / `CloseForReal` | 摘 `CardEditor` 模块引用、清 `ItemsSource` 后关闭 |
| 热键 | `HotkeyRouter.Register()` | 刷新后重注册 |
| 卸载 | `CardPluginLoader.Unload` + `CardPluginUnloadProbe` | 先回调 `ICardModuleLifetime.OnPluginUnloading`，再 `Unload()` |
| 回收验证 | 800 ms `DispatcherTimer`（Background）+ GC ×3 + `WeakReference` | 延迟判定模块 / 上下文，区分三种日志结果 |
| 安全能力面 | `PluginDevReloader.IsSafeForHotReload` | 拒绝富内容 / 交互 / 设置区块 / 旧别名 |

2.13.6 时加载器把 `CardPluginLoadContext` 放进静态 `_contexts` 列表保持存活，属于“重启生效”的实现，
也意味着当时**无法**卸载；2.14.0 改成句柄列表，热重载和退出都会释放当前这批。

2.14.1–2.14.5 的程序集拆分不改变热重载的失败策略与能力面（仍只接受纯文字卡）：插件与宿主拿到的契约
始终由默认 ALC 提供；2.14.4 起宿主删除了类型转发，旧插件（2.14.3 及之前编译 / `apiVersion=1`）不再兼容，
需要重编译并把 `apiVersion` 升到 2；2.14.5 起 UiKit 也由默认 ALC 提供。

---

## 2. 为什么不能直接 `Unload()` 后 `LoadAll()`（分析仍然成立）

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
  - 2.14.0 只对这一类开放热重载。
- **富卡片**（图片/视频/CLI 等）：
  - `CardSlot.Content`、窗口视觉树、事件订阅、后台线程/定时器都可能由插件创建；
  - 即使 `ReleaseContent` 调 `IDisposable`，WPF 资源/BAML 仍可能让 ALC 假卸载；
  - 需要插件实现完整的停止线程 / 解绑事件契约，而 `ICardModuleLifetime` 只是可选的 best-effort；
  - 2.14.0 在 `--plugin-dev` 下直接拒绝。

### 2.3 2.14.0 补上的宿主契约

- 可选 `ICardModuleLifetime.OnPluginUnloading()`：卸载前让插件停线程 / 解绑事件；additive，`apiVersion` 保持 1；
- `CardRegistry.ReplaceExternal(IEnumerable<ICardModule>)`：仅供 dev 热重载在 UI 线程替换外部模块快照，
  保留内置模块，Kind 冲突抛错；
- `OverlayController.ReloadPluginCards()`：按 Kind 重绑受影响卡片、重注册热键、重画；
- `SettingsWindow.ReleaseCardEditorsForPluginReload()`：断开设置页对旧模块的引用；
- `CardPluginUnloadProbe`：延迟弱引用验证，并清理对应影子目录。

---

## 3. 实现与验收（2.14.5 基线）

### 3.1 热重载流程

1. `FileSystemWatcher` 捕获插件目录变化，`Dispatcher.BeginInvoke` 回到 UI 线程后 debounce **700 ms**；
2. `CardPluginLoader.LoadAll()` 完整扫描两个插件根；每个插件先影子复制再做 manifest / Kind 校验；
3. `report.HasErrors` 为真 → **整轮放弃、保留旧模块**（日志「热重载放弃：本次扫描有 N 个加载错误」）；
4. 对新旧句柄都执行 `IsSafeForHotReload`；任一不满足 → 放弃本轮并提示重启；
5. `CardRegistry.ReplaceExternal(新模块)`；失败则保留旧模块；
6. 切换 `_handles` → `OverlayController.ReloadPluginCards()`：关闭设置窗、结束命令面板 / 入场动画、
   按 Kind 重绑变化槽位、重注册热键、重画；
7. `CardPluginLoader.Unload(旧句柄)`：先回调 `OnPluginUnloading`，再 `ALC.Unload()`；
8. `ScheduleVerify` 800 ms 后在后续消息轮做 GC ×3 + 弱引用判定，并清理影子目录。

### 3.2 安全能力面（黑名单拒绝）

只接受没有 `IRichCardModule` 且不满足以下任一条的插件；命中任一条都会日志拒绝并要求重启：

| 拒绝项 | 原因 |
|---|---|
| `IRichCardModule` | WPF 控件 / 资源 / BAML 无法保证 ALC 卸载 |
| `IsInteractive` | 键盘独占、生命周期由宿主管理 |
| `ICardEditorSection` | 设置页 WPF 控件会钉住 ALC |
| `ICardLegacyAlias` | 旧 Kind 别名会影响配置迁移 |

这是**黑名单**而不是完整能力白名单：文字卡插件如果还实现 `ICardHotkeySource` / `ICardDropTarget` /
`ICardActivation` 等能力，目前没有逐个拒绝（热键会在刷新后重新注册）。完整白名单仍是未做项。

### 3.3 失败策略

- 任一扫目录加载错误 → 整轮放弃，旧模块继续用，不做部分替换；
- `ReplaceExternal` 失败 → 放弃本轮；
- `ReloadPluginCards` 抛异常 → 注册表已是新模块，旧插件句柄保留到重启（不卸载，避免视觉树引用已卸载类型）；
- 全程不改写 `settings.json`、不退出宿主；
- 被替换 / 丢弃的模块会释放 ALC 并删除影子目录。

### 3.4 回收验证

- `Unload()` 只负责请求卸载、摘掉宿主引用与句柄；
- 延迟 `DispatcherTimer`（Background 优先级，800 ms）在后续消息轮做 3 轮
  `GC.Collect()` + `WaitForPendingFinalizers()`，再检查模块 / ALC 上下文弱引用；
- 日志三种结果：
  1. 「插件 ALC 已卸载（模块 / 上下文弱引用均已回收）」；
  2. 「插件模块已回收，ALC 上下文仍在卸载（运行时延迟回收；开发模式可接受）」；
  3. 「插件 ALC 卸载后仍有模块引用：…」（WARN，文件留待下次启动清理）。

### 3.5 实测验收（2026-11-01）

- 2.14.5 基线：`dotnet build MirrorpondHUD.csproj -c Release`：**0 警告 0 错误**（Contracts / UiKit / 宿主）；
  `--selftest`：**失败项 0**（共享控件、外观令牌等既有断言 OK）；`--smoke 8 --minimized`：exit 0；临时 UiKit 插件（只引 Contracts + UiKit）加载验证通过。
- 2.14.4：插件 API 升到 2——新 Contracts 插件（apiVersion=2）加载成功；`apiVersion=1` 旧插件被明确拒绝
  （日志「卡片插件 API 版本不兼容: … api=1（宿主支持 2）」）且宿主不崩溃；`--selftest` 失败项 0。
- 2.14.3 基线（历史）：`--selftest` 失败项 0，含契约身份断言（`ICardModule` / `CardRenderContext` / `Settings`
  在 `MirrorpondHUD.Contracts`，`CardRegistry` 在 `MirrorpondHUD`，`ConfigStore.SettingsNormalizer != null`）。
- 契约拆分回归（2.14.1）：旧插件（2.13.6 / 2.14.0 编译、引用宿主）经 `TypeForwardedTo` 加载，新示例插件
  （只引用 Contracts）加载；两条路径均实测 `--selftest` 通过。
- 2.14.0 原始验收（保留）：`dotnet build -c Release` **0 警告 0 错误**；`--selftest` **失败项 0**（含占位卡断言）。
- 热重载 v1 → v2：覆盖测试插件（`Kind=hotreload-test`）的 DLL + manifest，日志依次出现
  「检测到插件目录变化，开始热重载」→「已加载卡片插件 Hot Reload Test …（文字卡，影子副本）」→
  「运行时插件模块已替换」→「插件热重载：已刷新 1 张卡片」→「插件热重载完成」；
  卡片 marker 实际渲染新模块文本。
- 运行中移走插件目录：日志「未发现运行时卡片插件」「运行时插件模块已替换：内置 6，外部 0」，
  卡片显示「插件不可用：hotreload-test」；`settings.json` 的 SHA-256 前后一致，`Kind` / `Extras` 未被改写；
  放回后 dev 模式自动恢复（生产模式重启恢复）。
- 运行中覆盖 / 删除源插件 DLL 无 sharing violation → 影子复制生效。
- ALC 回收：最新一轮日志「插件 ALC 已卸载（模块 / 上下文弱引用均已回收）」；
  部分轮次出现「插件模块已回收，ALC 上下文仍在卸载（运行时延迟回收；开发模式可接受）」。

---

## 4. 风险矩阵与决策

| # | 风险 | 严重度 | 2.14.5 决策 | 当前残余 |
|---|---|---|---|---|
| R1 | ALC 卸载引用链（注册表 / 槽位 / 窗口 / 设置页 / 静态 / 退出顺序） | 高 | 先换注册表 + 槽位再 `Unload`；设置页 `ReleaseCardEditorsForPluginReload`；800 ms 定时器 + GC ×3 + 弱引用 | 部分轮次上下文延迟回收，未做 20 轮全回收 gate |
| R2 | WPF 视觉树 / 资源 / BAML / Dispatcher 回调 | 高（富）/ 中（文字） | 富内容、交互、设置区块、旧别名一律拒绝；文字卡只换模块重画 | 富内容卡仍只能重启加载 |
| R3 | 设置页编辑器 / 添加菜单持旧模块 | 中 | `CloseForReal` 前释放编辑器引用、清 `ItemsSource` | 可见设置窗会被强制关闭，**未保存编辑丢失** |
| R4 | 插件 static 字段 / 线程 / `DispatcherTimer` / 事件订阅 | 高 | `ICardModuleLifetime` best-effort + 文档契约 | 无法强制；弱引用只能事后发现 |
| R5 | `Extras` 中绑定插件类型实例 | 中高 | 文档要求 DTO 只用 public field / JSON 原语 | 宿主未做 `Extras` JSON 往返纯化，内存实例可能钉住 ALC |
| R6 | 监视抖动 / 半写入 | 中 | 700 ms debounce；先复制再从影子读 manifest | 无文件稳定等待 / IO 重试；`Error` 后不 rescan |
| R7 | 失败回滚 | 高 | 加载错误整轮放弃；`ReplaceExternal` 失败保留旧模块；`ReloadPluginCards` 失败保留旧句柄到重启 | 无按插件回滚；失败后状态机一致性依赖日志 |
| R8 | 并发 / UI 线程 | 中 | 全部 marshal 到 UI Dispatcher，单飞 `_reloading` | 插件加载本身仍在 UI 线程，坏构造函数可挂起启动 |
| R9 | 安全边界（ALC 不是沙箱） | 高（认知） | 文档明确「可信本地插件」；影子复制 / 清单校验不是安全措施 | 无签名 / 哈希 / 白名单，信任模型未变 |
| R10 | Kind / 别名 / 冲突 | 中 | 加载期校验 Kind，拒绝内置与旧别名，重复 id / Kind 拒绝 | 无 |
| R11 | 影子复制生产回归 | 中高 | 复制失败回退源目录（WARN）；启动清理旧缓存 | 全量复制、无文件大小 / 数量上限、无哈希 |
| R12 | 发布 / 升级数据丢失 | 中高 | `publish.bat` 在清空 `bin\Release` 前后备份 / 恢复 `plugins` | 无 |
| R13 | 测试可验证性 | 中 | 占位卡断言 + 日志验收 | 没有 `LoadAll(roots)` 测试注入点，依赖手工 / 日志验收 |
| R14 | 文档 / 版本一致性 | 中 | 2.14.5 同步更新 README / version-history / 两篇 runtime 文档 / 示例 README | 已在本轮完成 |
| R15 | 程序集边界 / API 兼容（Contracts + UiKit / 默认 ALC） | 中高 | 2.14.1–2.14.5 拆出 Contracts / UiKit；2.14.4 删除 TypeForwardedTo 并升 API 2；loader 对宿主 / Contracts / UiKit 都走默认 ALC | 旧插件需重编并升 `apiVersion=2`；UiKit 插件加载已临时验证但未固化；API 2 迁移只有文档说明 |

风险复核报告（`%TEMP%\hot-reload-review\report.md`）中的 P0-2（失败回滚）、P0-3（按插件回滚）、
P1-4（完整能力白名单）、P1-5（监视重试 / rescan）、P1-11（`Extras` 纯化）仍未闭环，对应上表 R5–R8。

---

## 5. 使用方式（`--plugin-dev` + 改 `plugins/cards`）

```bat
:: 1) 构建宿主与插件（或使用 samples\RuntimeCardExample）
dotnet build -c Release

:: 2) 插件放到以下任一目录（两个根都会监听）
::    bin\Release\plugins\cards\<id>\
::    %LOCALAPPDATA%\MirrorpondHUD\plugins\cards\<id>\

:: 3) 以开发模式启动
bin\Release\MirrorpondHUD.exe --plugin-dev

:: 需要自动退出 / 收集日志时配合 --smoke
bin\Release\MirrorpondHUD.exe --plugin-dev --smoke 90 --smoke-visible
```

- 修改 `manifest.json` 或 DLL 保存后，700 ms debounce 自动重载；日志逐条给出加载 / 拒绝原因。
- 只支持纯文字卡；富内容卡、交互卡、带设置区块或旧 Kind 别名的插件会提示「请重启宿主」，不会替换。
- 重载会关闭设置窗（未保存编辑会丢）、结束命令面板 / 入场动画，然后重画受影响的卡片。
- 热重载不写配置；`Kind` / `Extras` 原样保留。
- 生产使用不要带 `--plugin-dev`：装 / 换插件后重启宿主。

---

## 6. 残余风险（发布后仍存在）

- **富内容卡拒绝热重载**：`IRichCardModule` 仍只能重启加载；生产不承诺富卡片热插拔。
- **ALC 不是安全沙箱**：插件 = 当前用户任意代码；无签名 / 白名单 / 哈希；影子复制不改变信任模型。
- **插件 static / 线程 / `DispatcherTimer` / 事件订阅**仍可能钉住 ALC；`ICardModuleLifetime` 只是 best-effort。
- **设置窗会被强制关闭，未保存的编辑会丢失**；可见时应先保存。
- **上下文可能延迟回收**：实测部分轮次「运行时延迟回收」，不能以 `Unload()` 调用成功当回收成功。
- **生产不承诺热插拔**：正式功能保持重启生效，不提供 UI 热重载 / 卸载按钮。
- **任一插件加载错误会让整轮热重载放弃**，没有按插件回滚；失败后状态机一致性依赖日志与下一次重载。
- 若 `ReloadPluginCards` 失败，旧插件句柄保留到重启。
- 文件监视没有稳定性等待 / IO 重试，`FileSystemWatcher.Error` 后只记日志、不重建；`dotnet build` 边写边触发时靠 debounce 缓解。
- 插件加载在 UI 线程，坏插件构造函数可挂起启动；影子复制全量、无大小 / 数量上限，旧缓存靠下次启动清理。
- `Extras` 若被插件类型实例（属性 DTO）钉住仍可能阻碍回收；插件 API 建议只放 JSON 原语 / public field。
- **2.14.4 起不再兼容旧二进制**：宿主已删除 `TypeForwardedTo`，2.14.3 及之前编译、引用宿主的插件必须重编译
  （改引 Contracts / UiKit）并把 `apiVersion` 升到 2，否则会被拒绝或加载失败。
- **UiKit 插件加载已临时验证**：临时插件（Contracts + UiKit）加载通过，由默认 ALC 提供 UiKit；
  但尚未固化成示例 / 回归用例，插件目录不要复制 UiKit DLL。
- **插件 API 1 → 2 迁移**：属于破坏性变更，目前只在文档里说明，设置页没有“插件需升级”的指引。

---

## 7. 生产建议

1. **正式功能保持「重启生效」**：`plugins/cards` + manifest 的安装方式足够，不把热重载写进用户承诺。
2. 不在设置页提供“卸载 / 重载”按钮；`--plugin-dev` 只是开发者开关。
3. 富 WPF 卡片不承诺热插拔；若未来必须做，先设计插件 `Dispose/Shutdown` 契约、收窄能力白名单，
   并用 `WeakReference` + 强制 GC 验证，不以“UNLOAD 调用成功”当成功。
4. 签名 / 哈希 / 白名单 / 沙箱是独立的信任模型工作，不要和热重载混做。
5. 插件缺失时的「插件不可用」占位卡已在 2.14.0 实现，这是卸载与恢复体验的前置。

---

## 8. 参考

- `src/Cards/CardPluginLoader.cs`（加载、影子复制、校验、卸载探针）
- `src/Cards/CardRegistry.cs`（`ReplaceExternal`、占位模块）
- `src/Ui/PluginDevReloader.cs`（监听、debounce、安全与失败策略、延迟验证）
- `src/Ui/OverlayController.cs`（`ReloadPluginCards` / `ReleaseContent`）
- `src/Ui/SettingsWindow.xaml.cs`（`ReleaseCardEditorsForPluginReload` / `CloseForReal`）
- `src/Contracts/Cards/CardModule.cs`（`ICardModuleLifetime`；2.14.1 起在 Contracts）
- `src/UiKit/MirrorpondHUD.UiKit.csproj`（共享 WPF 控件程序集，2.14.5 起；`src/UiKit/CliUi.cs` 里的 `CardPalette`）
- `src/Contracts/MirrorpondHUD.Contracts.csproj`（独立契约程序集）
- `src/Contracts/Ui/ThemeTokens.cs`（`ThemeTokens` / `ThemeResolver`，2.14.3 起在 Contracts）
- `src/TypeForwards.cs`：2.14.1–2.14.3 的旧插件类型转发，**2.14.4 已删除**
- [runtime-card-extensions.md](runtime-card-extensions.md)（安装、manifest、安全边界）
- [samples/RuntimeCardExample/README.md](../samples/RuntimeCardExample/README.md)（示例插件与 dev 流程）
- 独立风险复核：`%TEMP%\hot-reload-review\report.md`；实测日志：`%TEMP%\mp-hotreload-run\mirrorpond.log`
- [.NET AssemblyLoadContext unloadability](https://learn.microsoft.com/dotnet/standard/assembly/unloadability)
