# AGENTS.md — AI 编码代理工作指南

本文件是为在本仓库工作的 AI 代理（Claude Code、OpenCode 等）编写的工程守则。目的是让代理不必重新发现本项目的契约与坑，改出**编译通过、自检通过、不破坏隐性约束**的代码。

---

## 1. 项目是什么

- **MirrorpondHUD（镜潭一览）**：.NET 8 / WPF 桌面顶层卡片叠层应用。卡片（硬件指标、时钟、按键、命令行、AI 对话、媒体、文件中转站）堆叠在桌面最上层，设置窗口统一管理；默认鼠标完全穿透。
- 只跑 Windows 10 / 11；多处 P/Invoke（DWM、DXGI、winmm、user32/gdi32），x64 与 ARM64 两个平台。
- **零第三方依赖**：没有 `PackageReference`，纯手写原生互操作与 JSON 解析（`Json.cs` 反射绑定）。新增功能优先走这种自研路径，不要随手引入 NuGet 包。
- UI 与全部注释、日志、用户提示都是**简体中文**。

## 2. 构建与验证

```bat
dotnet build -c Release          :: 必须做到 0 警告 0 错误
bin\Release\MirrorpondHUD.exe --selftest   :: 自检，退出码=失败数，必须为 0
run.bat                          :: 编译（如需要）并从 bin\Release 直接运行
build.bat / publish.bat          :: 构建 / 发布（publish 默认单文件）
```

- 配置与日志生成在 `bin\Release\`：`settings.json`、`mirrorpond.log`。排查问题/验证行为时先看这两处。
- 其它可用的命令行模式：`--smoke N`（自动起跑 N 秒后退出）、`--perf N`（性能采样，结果写日志，见 `docs/performance-baseline.md`）、`--screenshot[-cards|-settings|-widgets] <标题>`（无头截图，用于 UI 自查）。
- **改完代码必须跑一次构建 + `--selftest`**；动了设置存储、主题解析、卡片契约的改动尤其要看自检对应段落的结论。

## 3. 多程序集结构（最容易踩的坑）

同一个 `src/` 目录被 **多个独立 csproj 的默认通配**各自收集，靠根工程里的 `<Compile Remove>` 划界：

| 路径 | 所属程序集 | 说明 |
| --- | --- | --- |
| 根（其余所有 src/**） | `MirrorpondHUD.csproj`（WinExe 宿主） | App、Ui、Core（配置 / 主题）、Cards 加载器、Metrics |
| `src/Contracts/**` | `MirrorpondHUD.Contracts.csproj` | 契约与基础设施：`Settings`、`Json`、`Log`、`LogThrottle`、ConfigStore 相关契约、各 `ICard*` 接口 |
| `src/UiKit/**` | `MirrorpondHUD.UiKit.csproj` | 主题化控件库 + `Media`（FfmpegPlayer / WaveOutPlayer / VideoWindow / CardMediaView） |
| `src/Plugins/<Name>/**` | 各自的 `plugin.csproj` | **6 张内置卡本身就是运行时 DLL 插件**（`builtin.*`），与用户插件走同一条 `CardPluginLoader` 路径 |

约束：
- 契约程序集被宿主、UiKit、所有插件引用；它**不得**反向引用任何一方。
- 六张内置卡编译后由 `<exe>/plugins/cards/builtin.<id>/` 目录的 `manifest.json` + DLL 加载；**没有热重载**——改完插件必须整体重启进程验证。
- 插件列为 `ProjectReference … ReferenceOutputAssembly="false"`：只构建不引用，宿主通过 ALC 加载。不要把 `src/Plugins/**` 里的类型 `using` 进宿主代码。
- 新增文件只看它落在哪条通配边界内；跨边界的共用代码（契约、控件）必须搬到 Contracts / UiKit。

## 4. 代码风格（现代 C# 默认，契约/互操作有红线）

- **新代码与本次触碰的文件按现代 C# 风格写**：file-scoped namespace、表达式体属性/单表达式方法、字符串插值、`is null` / `is not null`、目标类型 `new()`、`out var`。不再要求为了统一把现代写法改回 `get { return ...; }`。
- 未触碰的旧文件不强制批量重排；如果整仓批量重排，必须单独提交、单独验证，避免和功能改动混在一起。
- `Nullable=disable`、`ImplicitUsings=disable` 继续：usings 手写齐全，可空性自己处理；不要用 `#nullable enable` 局部试点绕过评审。
- `_camelCase` 私有字段、公开字段用 PascalCase（DTO 风格）；事件字段 + 手写 null 判防护继续保留。
- 注释一律中文，写**为什么**（设计动机、失败路径、历史评审编号），不写显而易见的重复描述。大量注释里引用了历史审阅编号（P0-x / P1-x / P2-x），别删——它们是变更依据的存档。
- 控件 / 卡片 UI 一律沿 `MirrorpondHUD.UiKit` 的主题令牌（`StyleSettings` + `ThemeResolver`）取颜色与字号，不要硬编码颜色常量。

### 4.1 禁止“现代化”的红线

以下区域即使看起来像风格问题，也不得做成员形状/布局改造：

1. **序列化 DTO（Contracts 公共字段 + `PluginManifest` + 各种 `Extras` DTO）**：保持 `public` 实例字段、无参构造。禁止改成属性、`record`、`init`、`required` 或主构造——`Json.cs` 只反射 public 字段，且 `Json.Deserialize<T>` 要求 `new()`；改动会静默丢配置、丢 manifest，或破坏插件 ABI。
2. **P/Invoke / 互操作结构体**：`[StructLayout]`、`[FieldOffset]`、`[DllImport]` 所在结构体的字段顺序、类型、可见性和布局不得改；不要用 `record struct`、属性化或“顺手重排”。
3. **XAML 绑定面**：`x:Class`、`x:Name`、事件处理方法名、资源键不得因为格式化改名或移动。
4. **catch / 代次 / 句柄收尾**：不要为了风格把 `using (...)` 批量改成 `using var`，不要把空 catch 合并或删除；失败可见与释放时机的约束高于行数。
5. **插件 ABI**：Contracts/UiKit 的公共成员形状变化必须先评估 `apiVersion`；Nullable 注解本身不破坏二进制，但成员形状变化会。

## 5. 失败必须可见（本项目最高纪律）

2026-11 的代码审阅（P0 批次）确立的铁律：**不允许简单隐藏错误**。

- 不允许写 `catch { }` / `catch { return null; }` 这类吞错。允许的写法只有：
  1. `catch (Exception ex) { Log.Warn/Error(...); }`——落日志；
  2. catch 后**把失败原因传给用户可见的 UI 状态**（如 `SetStatus(...)`、设置窗状态行）；
  3. 真正的良性竞态（对象正在 Dispose 等），catch 收窄到具体异常类型并注释为什么无害。
- 长周期循环（PDH 采样、nvidia-smi、桌面取图）的失败用 `LogThrottle`（Contracts）做状态转移 + 最小间隔节流，失败/恢复各记一条，**恢复时也要记**。
- 用户可操作的失败必须透传到 UI：`ConfigStore.Save` 失败经 `LastSaveError` → 设置窗状态行；宿主 `ApplySettings` 返回 bool。
- 静默降级（如 GPU 指标不可用 → 没那个卡片数据）不算"错误"，但降级的**原因**要进过日志。

## 6. 关键隐性约束（改相关代码前必读）

### 6.1 设置存储（`src/Core/ConfigStore.cs`）
- 保存语义：写临时文件 → `File.Replace`（生成 `.bak`）→ 失败回退 Copy+Move。**备份失败必须中止保存**，绝不能破坏原文件；Load 侧同理（损坏备份失败 → 只读模式，不 Save 覆盖）。
- `SchemaVersion` 高于当前 → 只读加载，Save 直接拒绝（保护新版本配置）。
- 归一化钩子 `SettingsNormalizer` 由宿主注入（= `CardModel.Normalize`），在第一次 Load 前必须就位。

### 6.2 设置树引用纪律（卡片插件最容易写坏的地方）
- 卡片视图要落盘时**只能写宿主当前这份 `Settings` 对象树**（`ICardHost.Config` / 宿主 Settings），并且必须确认卡片真的挂在这棵树的 `Cards` 列表里（引用相等）。写旧树的快照会把用户刚存的配置**整份回滚**。
- 宿主应用设置时会重建视图，出现"视图还活着但它的配置已废弃"很正常——此时宁可放弃这次落盘，也不要覆写。

### 6.3 原生互操作 / 线程（`UiKit/Media`、`src/Ui`）
- 这些类用手写代次计数（`_run`）+ 锁做"旧代晚归作废"：任何异步 / 线程回调回来必须先校验代次与 `_stop`，不得改动新代状态、不得对旧代报假失败。摘要：FfmpegPlayer / DesktopFrameSource / WaveOutPlayer 都是这个模式，新代码保持一致。
- 有界 Join：绝不允许在 UI 线程无界等线程；Join 超时后把收尾权移交给后台或所有者线程（"认领收尾" CAS 模式），且**绝不能双重释放**同一个句柄/buffer。
- 桌面取图（`DesktopFrameSource`）判死走 `EnterDead`（一次性带 HRESULT 的 WARN），`GlassFeed` 按 30s→60s→2min 封顶退避重试；不要改成每帧重建设备。

### 6.4 插件生命周期（`src/Cards/CardPluginLoader.cs`）
- `Unload` 只发请求；弱引用验证必须延后（`CardUnloadProbe.Verify()` 幂等，宿主在退出收尾调）。不要把 `GC.Collect` 判定塞进 Unload 调用栈，会误报泄漏。
- 影子复制目录下加载（源目录不锁文件），换插件后重启生效；`CleanupShadowCache` 在启动早期调用。
- manifest `apiVersion` 必须是 2，Kind 走 `IsSafeKind` 白名单；`author` 字段保留并已用于加载日志。详见 `DLL-CARD-PLUGIN-GUIDE.md`。

### 6.5 主题文件（`src/Core/ThemeFile.cs`）
- `foreground`/`background` 严格 **7 位 `#RRGGBB`**（透明度必须走 `backgroundOpacity`）；`accent`/`danger`/软色允许 9 位 `#AARRGGBB`。解析失败把错误串进返回值，不抛给 UI。

### 6.6 App 生命周期（`src/App.xaml.cs`）
- UI 线程异常白名单策略：只有 InvalidOperationException / COMException / Win32Exception / ObjectDisposedException（含内部异常链）可 `Handled=true`，且 5 次封顶；其它一律"不可信状态"→记录+提示+退出。
- 启动初始化（插件加载 / 配置读取）整体在 try 内，失败必须 Log.Error + 可见提示 + `Shutdown(1)`。

## 7. 自检（`src/SelfTest.cs`）

- `SelfTest.Run()` 汇总各段结论，失败数即进程退出码。各段独立 try，异常也算失败。
- 给存储/解析/竞态类改动**加对应断言**（已有范例：设置损坏恢复、主题校验、HigherSchema 只读、DPAPI Key 往返）。新增"上次修过 vX 就可能回退"的检查优先落在这里，而不是只靠手工验证。
- 颜色严格 7 位断言在 `validateOk` 段（`Settings.IsHexColor` 系列）；主题解析错误文案改动要同步检查相关断言。

## 8. 文档同步

改动以下区域时同步对应文档（英文/中文正文都在 docs/）：

| 改动 | 文档 |
| --- | --- |
| manifest / 插件 API | `DLL-CARD-PLUGIN-GUIDE.md` |
| 主题文件字段与校验规则 | `docs/theme-system.md` |
| 设置 / 卡片架构 | `docs/cards-architecture.md`、`docs/runtime-card-extensions.md` |
| 大版本行为变化 | `docs/version-history.md`（版本号也在各 csproj 的 `<Version>`，一起升） |

## 9. 提交纪律

- 一次改动聚焦一个主题；修审阅项时在提交说明里引用编号（如 `P0-2`）。
- 提交前自检清单：`dotnet build -c Release` 0 警告 0 错误 → `--selftest` 退出 0 → 若涉及 UI 行为，顺手跑一次对应 `--screenshot-*` 模式确认渲染没坏。
- 纯格式化 / 重排提交必须单独提交，并把提交哈希写入 `.git-blame-ignore-revs`；本地启用：`git config blame.ignoreRevsFile .git-blame-ignore-revs`。
- 不要顺手重排无关代码 / 大幅移动文件；本项目大量 P-Invoke 结构体偏移、反射字段名（`PluginManifest` 的 camelCase 即 JSON 键名）对口依赖原始布局。
- 修复问题时深挖问题原因而不是简单修复表面。

## 10. 显式风格

- 设置项要求上下文关联显示，即子设置项只在选择对应的设置项时显示，其余时段隐藏保存界面整洁。
- 设置项尽量不使用括号，关于设置项的小字说明要简洁明了。
- 输入栏的默认文字（“请输入文本”一类）要与输入内容大小位置一致。

## 11. 示例工程

- 如果你正在制作处于samples/中的独立项目，不要提交到主仓库，并从主仓库中排除该项目，作为一个独立项目独立提交。

## 12. 仍有效的约定（别"顺手清理"）

- 2.24.0 已清理 `GlassRuntimeState` 单源、Acrylic/Glass 双套模糊参数、`Dwm` 命名与 Chat 历史别名；
  新代码不得再引入旧字段 / 旧 mode 值（v6→v7 一次性迁移在 `SettingsLoader` 里）。
- 宿主与插件间的"宿主服务的 Config 永远是新树"约定、Cards↔UiKit 的渲染管线是大量测试与评审的凝练，改动前先读对应区域的注释。
