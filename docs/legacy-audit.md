# 源码遗留审计（2026-10-31，基线 v2.9.4）

> 目的：在修完「AI 对话 / 常驻媒体卡外观不跟随设置」之后，按**同一类失效模式**把源码再扫一遍，
> 确认还有没有类似“史山”：构造期捕获状态、双重真相、静态可变缓存、静默失败、过期文档。
>
> 范围：`src/` 全部 C# 与 `docs/` 中仍在描述“现状”的段落；行号以 v2.9.4（`055b3f9`）为准。
> 结论：**没有发现与 2.9.4 完全同源的未修问题**；另有 13 条独立遗留，其中 2 条属于正确性（S1 / S4）。
> 修复进度：**S1 + S2 已在 v2.9.5 修，S3 + S4 已在 v2.9.6 修**；S7 + S12 排 v2.9.7；其余仍只记录。

## 0. 结论摘要

| 编号 | 问题 | 类型 | 优先级 | 状态 |
|---|---|---|---|---|
| S1 | 非法颜色静默变成白色，`TryColor` 的 fallback 实际不可达 | 正确性 | P1 | ✅ v2.9.5 |
| S2 | 颜色解析 / 校验有 7 份实现，非法输入行为各不相同 | 结构 + 正确性 | P2 | ✅ v2.9.5 |
| S3 | `SettingsWindow._live` 是死字段（只赋值、从不读） | 死代码 | P3 | ✅ v2.9.6 |
| S4 | `async void Send / RunCommand` 在 `try` 之前抛异常会直接崩进程 | 正确性 / 稳定性 | P1 | ✅ v2.9.6 |
| S5 | `OverlaySurface.BrushCache` 无上限 | 资源 | P3 | 未修 |
| S6 | `ButtonsCard` 不做就地换肤（构造期捕获 `Style`），外观一变就重建、丢掉演示状态 | 一致性 | P2 | 未修 |
| S7 | AI 对话历史条数写死 `12`，与 `Cli.MaxEntries` 脱钩 | 行为 | P2 | 未修 |
| S8 | `Style.ThemeName` 进了样式签名，只改主题名也会触发全部卡片换肤 | 轻微抖动 | P3 | 未修 |
| S9 | `.mptheme` 的数值范围与 `Settings.Normalize` 各写一份 | 漂移风险 | P3 | 未修 |
| S10 | 评审文档仍说「CLI 面板换外观会重建」 | 文档过期 | P2 | ✅ v2.9.4 |
| S11 | `OverlayController` / `SettingsWindow` 超过 1600 行 | 结构 | P3 | 未修 |
| S12 | `FindCommandCard` 取“第一张交互卡”，第二张交互卡会被静默忽略 | 语义隐式 | P2 | 未修 |
| S13 | `DesktopBackground` 只按路径缓存壁纸，同路径换图不会刷新 | 边缘 | P3 | 未修 |

> 已知项（`modularity-review.md` §6.1 的 #1 / #3 / #5 / #6）不在本文重复：默认卡硬编码、迁移层按 Kind 分支、
> 注册表静默回退、单程序集命名空间环——它们的结论没有变化。

## 1. 与 2.9.4 同源的检查

2.9.4 修的是三类问题，逐类复查：

### 1.1 构造期捕获 `Settings` / `StyleSettings`

`grep -rn "readonly Settings\|readonly StyleSettings\|Settings _settings\|StyleSettings _" src/`：

| 位置 | 是否同类问题 | 说明 |
|---|---|---|
| `CliPanelView`（`HostSettings` / `Cli`） | **是，已在 2.9.4 修** | 改为可变字段 + `ApplyStyle(Settings)` 整份跟随 |
| `ButtonsCard._style` | 否（但见 S6） | 不实现 `ICardStyleAware`，样式变化走重建，新实例拿新 `Style` |
| `MediaView._style` | 否 | `ApplyStyle(style, corner)` 每次覆盖 |
| `CardEditor._settings` | 否 | 只在设置窗口生命周期内使用，不参与运行时换肤 |
| `OverlayController._settings` / `OverlayWindow._settings` | 否 | 保存时整体替换，`SyncCards` / `RefreshSettings` 会更新 |

结论：同类问题只有 `CliPanelView` 一处，已修；没有第二个“持有旧配置的运行时控件”。

### 1.2 “只换样式”路径漏掉某一层

复查 `SyncCards` 的三个分支：

- 内容与样式都没变 → `UpdateContent`；
- 只样式变 + 模块实现 `ICardStyleAware` → `RefreshSettings` + `RefreshSurface` + 模块 `ApplyStyle`；
- 其余 → `ReleaseContent` 后重建，`Window.Apply / ApplyRich` 会完整重画。

结论：支持就地换肤的模块已经覆盖窗口层（`OverlaySurface`）与内容层（`CliPanelView` / `MediaView`）；
不支持就地换肤的模块走重建，不存在“只改了一半”的分支。

### 1.3 静态可变状态

`static readonly` 清单里只有注册表（`CardRegistry`）、常量表（预设 / 键名 / 星期）、缓存（画刷 / 滚动条 / 字体墨迹）、
共享 `HttpClient` 与 ffmpeg 探测缓存；**没有发现把“某张卡片的运行状态”放进静态字段**的情况。

`HttpClient` 是共享的、`Timeout = Infinite`，超时由每次请求自己的 `CancellationTokenSource` 控制（对话卡），
行为正确。

## 2. 新发现的问题

### S1 非法颜色静默变成白色（P1，✅ v2.9.5）

`OverlaySurface.ParseColor`（`src/Ui/OverlaySurface.cs:334`）内部 `catch { }` 后 **返回 `Colors.White`**。
于是：

- `CardButton.TryColor` / `ButtonsCard.TryColor` 的 `try { ParseColor } catch { fallback }` **永远不会走到 fallback**；
- `ThemeResolver.Parse` 同样把 fallback 写在 catch 里，对非法值实际返回白色；
- 而 `Settings.Normalize` 会把非法令牌清成空串（派生），两条路径的结果不一致。

最直观的复现：设置页预览里把前景色输入框改成 `nope`，预览会变白，而不是保持上一帧的颜色。
这不会写盘（`Normalize` 会拦），但属于“静默错误值”，与 2.9.4 同一类。

**修复（v2.9.5）**：新增 `src/Core/ColorCode.cs`（`TryParse` → bool + ARGB），
`OverlaySurface.ParseColor(hex, fallback)` 委托它；`CardPalette.TryColor` 保留但只做委托。
新增自检：非法前景色下 `Muted` 仍按默认前景派生，不再变白。

### S2 颜色解析 / 校验有 7 份实现（P2，✅ v2.9.5）

| 实现 | 非法输入 | 备注 |
|---|---|---|
| `OverlaySurface.ParseColor` | 白色 | 吞异常 |
| `CardButton.TryColor` | 实际白色 | catch 不可达 |
| `CardTextBox.TryColor` | 实际白色 | 与上者逐行相同 |
| `ButtonsCard.TryColor` | 实际白色 | 与上者逐行相同 |
| `MediaView.ParseColor` | 前景色 `#E6EDF3` | 自己又写了一套 `ColorConverter`（还认颜色名） |
| `ThemeResolver.Parse` | 实际白色 | catch 不可达 |
| `ThemeFile.TryParseColor` | `false` | 唯一返回失败的，但只输出 ARGB int |
| `Settings.IsColorToken` | `false` | 只做格式校验，不解析 |

同一个 `#AARRGGBB` 在“解析失败”上有三种语义（白 / 前景 / false），新增调用方很容易选错。

**修复（v2.9.5）**：只保留 `ColorCode`（Core）+ `OverlaySurface.ParseColor(hex, fallback)`（UI）+ `OverlaySurface.ParseColor(hex)`
（默认白，取色预览用）；删除 6 处复制，`CardPalette.TryColor` 与 `Settings.IsColorToken` 改为委托。
`MediaView` 不再接受颜色名（配置本来只允许十六进制）。

### S3 `_live` 死字段（P3）

`SettingsWindow` 的 `readonly Settings _live` 只在构造函数里赋值，之后从未读取（`grep -n "_live"` 只有两行）。
要么删掉，要么接成“窗口打开期间外部配置变了就提示重载”。当前是纯噪音。

**修复（v2.9.6）**：字段与赋值一起删除。

### S4 `async void` 没有顶层兜底（P1）

`ChatView.Send` 与 `CommandView.RunCommand` 都是 `async void`。`await` 段虽然包在 `try/catch/finally` 里，
但 `try` 之前还有 `AddEntry()`、`TrimEntries()`、`SetStatus()`、`_spin.Start()` 等 UI 调用；
其中任何一处抛异常，都会从 `async void` 逸出到 `Dispatcher`，默认行为是**结束进程**。

**建议**：整个方法体包一层 `try { ... } catch (Exception ex) { SetStatus("! " + ex.Message, true); }`，
或在 `App` 里挂 `DispatcherUnhandledException` 做兜底日志（不吞掉，但至少先落盘再决定）。

**修复（v2.9.6）**：`Send / RunCommand` 拆成 `async void` 外壳 + `Core()`；
外壳只做 `try { await Core(); } catch { 重置状态 + 状态行 + 日志 }`，准备阶段抛异常也不会再逸出。

### S5 `BrushCache` 无上限（P3）

`OverlaySurface.BrushCache` 以 `颜色|透明度` 为 key 永久缓存，没有上限；对比：
`CliScrollBars` 的 `Cache` 超过 16 清空、`TextCanvas.InkCache` 超过 1024 清空。
主题 / 取色器可以产生任意多的颜色，长会话下会缓慢增长。
**建议**：与 `CliScrollBars` 一致，超过 256 清空（清空后一帧内会重建，代价可忽略）。

### S6 `ButtonsCard` 不参与就地换肤（P2）

`ButtonsCard` 在构造期捕获 `_style`，且没有实现 `ICardStyleAware`。改外观会走重建分支，
按钮计数、输入框里未提交的文字、回显内容都会丢。它是演示卡，优先级不高，但和“换外观不重建”的承诺不一致。
**建议**：要么实现 `ApplyStyle`（把按钮 / 输入框 / 文案按新令牌重套），要么在文档里明确写“演示卡不保证状态”。

### S7 AI 对话历史写死 12 条（P2）

`ChatView.TrimHistory()` 里 `const int maxMessages = 12`，而记录区条数用的是 `Cli.MaxEntries`（默认 12，可调）。
把 `MaxEntries` 调到 30 后：界面上保留 30 块问答，但发给模型的上下文只有最近 12 条，用户无法从设置里看出这个上限。
**建议**：要么从 `Cli.MaxEntries` 推导（`MaxEntries * 2`），要么单独加「上下文条数」设置并在设置页写明。

### S8 `ThemeName` 进入样式签名（P3）

`StyleSignature()` 直接序列化整个 `StyleSettings`，其中包含 2.9.3 新增的 `Style.ThemeName`。
只改主题名（外观值完全没变）也会让 `styleKey` 变化，触发所有卡片走一次换肤。
不丢内容，但属于无意义抖动。**建议**：签名用投影（排除 `ThemeName`），或把 `ThemeName` 挪出 `StyleSettings`。

### S9 主题校验范围与 `Normalize` 各写一份（P3）

`ThemeFile.TryParse` 里的字号 6–96、圆角 0–60、内边距 0–80、CLI 尺寸范围是**复制**自 `Settings.Normalize` 的。
两份表以后改一处忘另一处，会出现“主题文件能导入、设置页却把它夹回去”的漂移。
**建议**：把范围抽成 `SettingsLimits` 常量（或一张表），两边共用；`ThemeFile` 的测试补一条“边界值与 Normalize 一致”。

### S10 文档过期（P2，本轮修）

审计时 `docs/modularity-review.md` 有三处仍说 CLI 面板换外观会重建（审计时第 468 / 500 / 510 行，本轮已改为已完成）：

- 「`CliPanelView`（对话 / 命令行）仍回退重建，因为样式是构造期固化的。」
- 样式热更新契约一行写「CLI 面板仍回退重建」。
- 阶段 C 遗留同样说法。

v2.9.1 起 CLI 面板已支持就地换肤，v2.9.4 补齐了 Settings 跟随与 `monoFont`；本轮已把这些“现状”改成已完成，
`docs/version-history.md` 里 2.8.0 / 2.9.1 的历史记录保持原样（那是当时的事实）。

### S11 巨型文件（P3）

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Ui/OverlayController.cs` | 1760 | 卡片同步、布局、热键、托盘、玻璃、命令行都在一个类里 |
| `src/Ui/SettingsWindow.xaml.cs` | 1677 | 五个页签 + 卡片列表 + 主题 + 颜色/字体/热键 |
| `src/Ui/Controls/CliUi.cs` | 659 | 面板骨架 + 输入框 + 条目面板 + 滚动条 |

不建议现在拆；下次要动其中一块时按功能切（例如把 `SyncCards` / `LayoutCards` 抽成 `CardLayoutCoordinator`，
把外观页的控件事件抽成 partial）。拆之前先把 S1/S2 做完，否则颜色逻辑会跟着搬两次。

### S12 `FindCommandCard` 取第一张交互卡（P2）

`OverlayController.FindCommandCard()` 返回 `_cards` 里第一张 `IsInteractive` 的卡片，F9 / 托盘 / 卡片菜单都走它。
现在只有命令行卡声明 `IsInteractive`，所以没问题；但将来如果画板 / 中转站也要“键盘独占”，
第二张会被静默忽略，而且没有任何提示。
**建议**：要么把“命令行入口”绑定到具体动作（而不是第一张），要么在 `CardModel.Normalize` 里校验
“最多一张 `IsInteractive`”，冲突时在设置页报错。后者更便宜。

### S13 壁纸缓存只看路径（P3）

`DesktopBackground` 的静态缓存命中条件是“路径相同”（`_cachedPath`），不比较文件时间 / 大小。
同路径覆盖壁纸（幻灯片、动态壁纸工具）时，叠层背景不会更新，直到路径变化或程序重启。
**建议**：缓存键加上 `LastWriteTimeUtc + Length`，或监听 `SystemEvents.UserPreferenceChanged`。

## 3. 建议的清理顺序

| 顺序 | 内容 | 理由 |
|---|---|---|
| 1 | S1 + S2 合并做（Core 颜色解析 + 删除 5 份复制） | 正确性问题，且一次改动消灭 4 条重复 |
| 2 | S4（async void 兜底）+ S3（删死字段） | 小、独立、降低崩溃风险 |
| 3 | S7（历史条数）+ S12（单交互卡校验） | 行为可预期，避免“设置了却没生效” |
| 4 | S6 + S8 + S5 | 一致性与资源，等下次动对应文件时顺手 |
| 5 | S9 + S11 + S13 | 结构 / 边缘，排到对应功能（主题、布局、壁纸）时做 |
| 6 | S10 | 本轮已修 |

## 4. 自检可以补的回归点

- 非法颜色：`ThemeResolver.Resolve(new StyleSettings { Foreground = "nope" })` 应回落到默认前景，而不是白色；
  `CardButton.TryColor("nope", fallback)` 应返回 fallback。
- 对话历史：`Cli.MaxEntries = 30` 时，上下文保留条数应与设置一致（或明确写死并在文档里说明）。
- 颜色解析一致性：同一输入在 `ThemeFile` / `ThemeResolver` / `OverlaySurface` 三处结果一致。

## 5. 审计命令（可复跑）

```bash
grep -rn "readonly Settings\|readonly StyleSettings\|Settings _settings\|StyleSettings _" src
grep -rn "async void" src
grep -rn "ParseColor\|TryColor\|IsColorToken\|TryParseColor" src
grep -rn "static readonly" src
grep -rn "Cache.Count\|Cache.Clear" src
grep -rn "仍回退重建\|构造期固化\|回退到重建" docs README.md
```

## 6. 修复记录

| 版本 | 内容 |
|---|---|
| v2.9.5 | S1 + S2：`ColorCode` 单一解析；非法颜色回落到 fallback（不再变白）；删除 6 处复制 |
| v2.9.6 | S3 + S4：删 `_live` 死字段；`Send / RunCommand` 加 `async void` 顶层兜底 |
