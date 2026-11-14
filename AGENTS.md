# AGENTS.md — AI 编码代理工作指南

## 1. 项目

- **MirrorpondHUD（镜潭一览）**：.NET 8 / WPF 桌面顶层卡片叠层，卡片（指标、时钟、按键、命令行、AI、媒体、文件中转站）统一由设置窗管理，默认鼠标穿透。
- 只跑 Windows 10/11；x64 + ARM64；多处 P/Invoke（DWM、DXGI、winmm、user32/gdi32）。
- **零第三方依赖**：生产代码不引入 NuGet，JSON / 原生互操作全手写。
- UI、注释、日志、提示一律简体中文。
- **开发阶段：以最终效果优先**，不为旧版配置/ABI 做兼容垫片；会破坏旧格式的改动直接改并同步文档。

## 2. 构建与验证

```bat
dotnet build -c Release                                  :: 必须 0 警告 0 错误
bin\Release\MirrorpondHUD.exe --selftest                 :: 退出码 = 失败领域数，必须 0
bin\Release\MirrorpondHUD.exe --selftest --isolated      :: 临时配置、跳过输入注入
dotnet run --project tests\TestRunner\TestRunner.csproj -c Release  :: 纯逻辑单测
run.bat / build.bat / publish.bat                        :: 运行 / 构建 / 发布
```

- 测试工程用 xunit（仅测试期依赖，fresh clone 需 `dotnet restore`；生产代码仍零 NuGet），说明见 `tests\README.md`。
- 配置与日志在 `bin\Release\`：`settings.json`、`mirrorpond.log`，排查先看这两处。
- UI 自查：`--screenshot-cards|--screenshot-settings|--screenshot-widgets|--screenshot-dialog <路径>`。
- `--selftest` / `--screenshot` 是立即模式：已有实例时**退出 2**，不伪造绿灯；并行跑多个会撞单实例互斥体，需串行。
- **改完代码必须 build + 至少一次 `--selftest --isolated`**；动了存储/解析/主题/契约/UI 按需加断言或截图。

门禁：
- `global.json` 固定 SDK **8.0.4xx**；`TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + `AnalysisLevel=latest`。`IDE0090` 在 `.editorconfig` 显式降为 suggestion。
- 空 catch 棘轮：`tools/check-empty-catches.ps1` 对比 `tools/empty-catch-baseline.txt`，只降不升。
- `publish.bat` 不删除 `bin/obj` 用户数据、不经 `%TEMP%` 中转；实例运行时明确报错退出。

## 3. 程序集边界（最容易踩坑）

| 路径 | 程序集 | 说明 |
| --- | --- | --- |
| 其余 `src/**` | `MirrorpondHUD`（WinExe） | App、Ui、Core、Cards 加载器、Metrics |
| `src/Contracts/**` | Contracts | Settings、Json、Log、ConfigStore 契约、ICard* 接口 |
| `src/UiKit/**` | UiKit | 主题化控件 + Media + HudDialog |
| `src/Plugins/<Name>/**` | 各自 `MirrorpondHUD.Cards.<Name>.csproj` | 6 张内置卡本身就是运行时 DLL 插件 |
| `tests/**` | 测试 | 不编进宿主 |

- Contracts 不得反向引用宿主/UiKit/插件。
- 插件经影子复制 + 自定义 ALC 加载：**无热重载**，改完重启进程；宿主代码不得 `using` 插件类型。
- 跨边界共用代码放 Contracts（数据/契约）或 UiKit（控件/媒体）。

## 4. 代码风格与红线

- 新代码用现代 C#：file-scoped namespace、表达式体、插值、`is null`、`new()`、`out var`；不强制重排旧文件。
- `Nullable=disable`、`ImplicitUsings=disable`；usings 手写；`_camelCase` 私有字段、PascalCase 公开字段。
- 注释写**为什么**（设计动机、失败路径、历史评审编号），中文。
- UI 颜色/字号必须走 `StyleSettings` + `ThemeResolver` 令牌，禁止硬编码颜色常量。

**不得改动：**
1. 序列化 DTO（Contracts 公共字段、`PluginManifest`、`Extras` DTO）：保持 public 实例字段 + 无参构造；`Json` 只反射字段。
2. P/Invoke 结构体的字段顺序/类型/可见性/`[StructLayout]`/`[FieldOffset]`。
3. XAML 的 `x:Class`、`x:Name`、事件方法名、资源键。
4. 用 `using (...)` 的既有释放时机；不要批量合并/删除空 catch（见 §5）。
5. 插件 ABI：Contracts/UiKit 公共成员形状变化必须评估 `apiVersion` 与 `contractsVersion`。

## 5. 失败必须可见（最高纪律）

- 禁止 `catch { }` / `catch { return null; }`。只允许：
  1. `catch (Exception ex) { Log.Warn/Error(...); }`；
  2. catch 后把原因写进用户可见状态；
  3. 真正良性竞态：收窄到具体异常类型并注释原因。
- 长周期循环失败用 `LogThrottle`：失败、恢复各记一条。
- 用户可操作失败透传 UI：`ConfigStore.LastSaveError` → 设置窗状态行；`ApplySettings` 返回 bool。
- 静默降级不算错误，但**降级原因必须进日志**。
- 释放路径统一 `SafeRelease.Run(action, "对象名")`。

## 6. 关键约束

### 6.1 设置存储
- 临时文件 → `File.Replace`（`.bak`）→ 失败回退 Copy+Move；**备份失败必须中止保存**。
- `SchemaVersion` 高于当前 → 只读，拒绝保存。
- `SettingsNormalizer`（= `CardModel.Normalize`）必须在首次 Load 前注入。

### 6.2 设置树
- 卡片落盘只能写宿主当前 `Settings` 树，且确认卡片仍在 `Cards` 列表（引用相等）。
- 视图配置已废弃时宁可放弃落盘，绝不写旧树回滚用户配置。

### 6.3 线程 / 原生
- 异步回调先校验代次 + `_stop`；旧代晚归作废，不报假失败。
- 禁止 UI 线程无界 Join；超时移交后台收尾；同一句柄/buffer 绝不双重释放。
- 桌面取图判死走 `EnterDead`（一次性 HRESULT WARN）；`GlassFeed` 按 30s→60s→2min 退避。

### 6.4 插件生命周期
- `Unload` 只发请求，弱引用验证延后（`CardUnloadProbe.Verify()` 幂等）。
- `CleanupShadowCache` 启动早期调用；影子目录加载，不锁源文件。
- manifest：`apiVersion` 必须是 2；`contractsVersion` 缺失视为当前、高于宿主拒绝、低于 Warn；Kind 走 `IsSafeKind`。

### 6.5 主题文件
- `foreground`/`background` 严格 7 位 `#RRGGBB`，透明度走 `backgroundOpacity`；`accent`/`danger`/软色允许 9 位 `#AARRGGBB`。
- 解析失败把原因串进返回值，不抛给 UI。

### 6.6 App 生命周期
- UI 异常白名单：InvalidOperation/COM/Win32/ObjectDisposed，最多 5 次；其它记录 + 提示 + 退出。
- 启动失败必须 `Log.Error` + 可见提示 + `Shutdown(1)`。

### 6.7 日志
- 最小等级在「行为 → 日志等级」：`Debug / Info / Warn / Error`（默认 Info）；`Log.SetMinLevel` 在配置加载与保存应用时同步。
- `MIRRORPONDHUD_DEBUG=1` 仍可强制 Debug；日志异步队列，退出 `Flush(2s)`。

## 7. 自检

- `SelfTest.Run()` 各段独立 try，失败数即退出码；新增领域要计入失败数并输出 `FAIL:` 行。
- 纯逻辑（JSON/设置/模板/原子写/主题）优先放 `tests/`；自检负责环境与互操作冒烟。
- 修过的回归点要加断言（损坏恢复、主题校验、只读高版本、DPAPI 往返、日志等级归一化等）。

## 8. 文档同步

| 改动 | 文档 |
| --- | --- |
| manifest / 插件 API | `DLL-CARD-PLUGIN-GUIDE.md` |
| 主题字段/校验 | `docs/theme-system.md` |
| 设置/卡片架构 | `docs/cards-architecture.md`、`docs/runtime-card-extensions.md` |
| 版本记录 | `docs/version-history2.md`（每次提交都追加；`version-history.md` 已过大，不再新增） |
| 大版本行为 | `docs/version-history2.md` + 主题/插件文档 |

- 版本号唯一来源：`Directory.Build.props` 的 `<Version>`；6 个 manifest 由 `tools/write-manifests.ps1` 构建生成，不要手改 version。

## 9. 提交纪律

- 一次改动聚焦一个主题；提交说明引用评审编号（如 `P2 K-17`）。
- 提交前：`dotnet build -c Release` 0/0 → `--selftest --isolated` 0 → 涉及 UI 跑对应截图 → `dotnet format --verify-no-changes`。
- 纯格式化提交单独提交并写入 `.git-blame-ignore-revs`。
- 不顺手重排无关代码 / 大移动文件（P/Invoke 布局与反射字段名对位置敏感）。
- 修复要挖根因，不做表面掩盖。

## 10. UI 风格

- 设置项上下文关联：子选项只在对应开关下显示。
- 设置项尽量不用括号；说明文字简洁。
- 输入框占位文字与输入内容同字号、同位置。
- 视觉规范：`samples/EndfieldDesign` + `docs/UI/endfield.md`（中性灰阶、信号黄、直角/切角/胶囊各有语义、角色色、层级靠字号字重与 3px 竖条）。
- 所有系统级确认/错误提示用 `UiKit.HudDialog`，不要 `MessageBox.Show`。

## 11. 示例工程

- `samples/` 下的独立项目不提交主仓库，并在 `.gitignore` 排除；各自独立维护。

## 12. 仍有效的约定

- 2.24.0 已清理 `GlassRuntimeState` 单源、Acrylic/Glass 双套模糊参数与旧 GlassMode 值；不得再引入旧字段/旧 mode。
- “宿主提供的 Config 永远是新树”；Cards↔UiKit 渲染管线是长期约束，改前先读注释。

## 13. 并行作业

- 多 agent 并行处理与审阅的流程见 `docs/superpowers/specs/2026-11-14-parallel-review-method-design.md`：写域互斥（公共文件归 Lead）、作者 ≠ 审阅者、build / `--selftest` / 截图由 Lead 串行收口。
- 审阅报告落 `docs/review/YYYY-MM-DD-<主题>.md`，定级 P0–P3；P0/P1 必须交叉复核后才进正文。
