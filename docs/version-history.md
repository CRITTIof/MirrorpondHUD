# 版本修改记录（2.4.1 → 2.16.0）

> 范围：本文只记录**这一轮接手后我做的版本**（v2.4.1 起）。v2.4.0 及更早是既有历史，不在本文档里。
> 数据全部取自仓库真实提交（`git diff --shortstat <上一版>..<本版>`），文件行的 `+ / -` 就是实际增删行数。
> 版本状态：`main` 已推送到 GitHub；`v2.4.2`–`v2.4.5` 为附注标签（分别指向 `70343ef` / `aabe441` / `05857ce` / `f4b2b8b`），
> v2.4.5 已发布 Release（附件为两个 requires-dotnet 单文件），v2.4.0 的 Release 已删除、其 tag 保留。
> `v2.5.0`–`v2.9.2` 均为附注标签，**已随 2.9.2 的推送同步到远端**（`v2.6.0` = `9c1bafa`、`v2.6.1` = `d3ac63d`、
> `v2.6.2` = `1b138e7`、`v2.9.2` = `754115e`）；`v2.4.1` 也已补推。`v2.9.3`–`v2.12.2` 已于 2.12.2 推送并发布 Release；`v2.13.0`–`v2.13.6` 目前只在本地 / 功能分支创建。
> `v2.14.0`–`v2.14.4` 为功能分支 `feature/runtime-card-loading` 上的提交（分别为 `dbe8207` / `730fd39` /
> `f67a255` / `5f8933c` / `ef70cea`，**未发布、未合并**）；`v2.14.5`（`a0fe549`）/ `v2.14.6`（`3e6abc6`）
> 已推送到 `origin/feature/runtime-card-loading`；
> `v2.14.7`（`abcbcec`）/ `v2.14.8`（`633e9d6`）已随文档同步提交 `d73d2ec` 推送到同一分支（未发布、未合并）；
> `v2.14.9`（`aba873a`）已推送；`v2.15.0`（`78d3381`）已推送（文档同步提交 `2eba9b4`）；`v2.16.0`（代码 `6406d96`，文档同步 `d1fd414`）**已推送并快进合并到 `main`**；`v2.17.0`（`27b8bbd`）已提交到分支 `feature/data-core`；`v2.18.0`（`1e1c6ac`）为本次提交（同一分支，**尚未推送、未合并 main**），未打 tag、未发布 Release。
> `v2.14.1` / `v2.14.2` / `v2.14.4` 的规模用对应提交的 `git show --shortstat`；`v2.14.3` / `v2.14.5` / `v2.14.6`
> 按当时提交前工作区的 `git diff --shortstat` 计算；`v2.14.7` / `v2.14.8` 用对应提交的 `git show --shortstat`；
> `v2.14.9` 用提交前工作区的 `git diff --shortstat -- MirrorpondHUD.csproj src`；`v2.15.0` 用 `git show --shortstat 78d3381`；`v2.16.0` 用 `git show --shortstat 6406d96`；`v2.17.0` 用 `git show --shortstat 27b8bbd`；`v2.18.0` 用 `git show --shortstat 1e1c6ac`（文档同步另计）。

## 0. 总览

| 版本 | 日期 | 提交 | 主题 | 改动规模 |
|---|---|---|---|---|
| v2.4.1 | 2026-10-25 | `40be56b` `76c9fa0` `39c2708` `82a8f80` | AI 对话卡改终端样式；API Key 改用 DPAPI；切窗口时玻璃跟手；视频窗也排除出截屏 | 15 文件，+1236 / −242 |
| v2.4.2 | 2026-10-25 | `1e589d0` `70343ef` | 玻璃取图改用 DXGI 桌面复制（GPU）；同期重画改同步、相同帧不重画 | 5 文件，+852 / −7 |
| v2.4.3 | 2026-10-28 | `aabe441` | 取图方式进设置（GPU / CPU）；修掉判同帧后 DIB 留未模糊原图导致的闪烁 | 9 文件，+269 / −22 |
| v2.4.4 | 2026-10-28 | `05857ce` | 模糊图改画进卡片内部，删掉底板窗口（修圆角外扩 / 顶部暗带）；模糊强度 / 帧率 / 取图方式进设置 | 9 文件，+1026 / −1503 |
| v2.4.5 | 2026-10-30 | `f4b2b8b` | AI 对话与命令行统一成 CLI 交互面板（每条记录独立小面板与滑块）；图标按墨迹居中；拖动时玻璃跟手；主题方案文档 | 23 文件，+1647 / −668 |
| v2.5.0 | 2026-10-30 | `c4e68e2` | 模块化评审文档；修命令行打开时其它卡片停更、富内容每次应用设置都重建、每卡一份整份配置副本 | 5 文件，+183 / −30，另新增 2 篇文档（500 + 582 行） |
| v2.6.0 | 2026-10-30 | `9c1bafa` | 卡片设置搬进 `CardSettings.Extras`（schema 5）；模块自带设置区块与校验；命令行卡试点 | 15 文件，+470 / −306，另新增 2 个源文件（45 + 259 行） |
| v2.6.1 | 2026-10-31 | `d3ac63d` | P1-3：旧 Kind 别名与预设归一化下沉到模块，`CardModel` 不再认识 `InfoTextCard` | 10 文件，+146 / −22 |
| v2.6.2 | 2026-10-31 | `1b138e7` | 媒体卡 / 对话卡设置搬进 `Extras`（schema 6）；模块自带设置区块（`MediaCardSettingsView` / `ChatCardSettingsView`）与摘要 | 19 文件，+347 / −846，另新增 4 个源文件（728 行） |
| v2.6.3 | 2026-10-31 | `7733b37` | 输入能力拆分（`HandlesOwnDrag`）+ 拖放通道（`ICardDropTarget`），为画板 / 中转站铺路 | 8 文件，+169 / −12 |
| v2.7.0 | 2026-10-31 | `cba71fd` | 阶段 B 收尾：`ICardHost` 宿主服务与 `ICardHostAware` 生命周期；`CardStateStore` 每卡状态存储（原子写 + 路径校验） | 7 文件，+220 / −23，另新增 2 个源文件（165 行） |
| v2.7.1 | 2026-10-31 | `3ab0fd0` | P0-5：一帧一份共享变量表 + 模块声明刷新间隔（时钟 1 s / 按键 250 ms / 系统信息每帧） | 9 文件，+194 / −32 |
| v2.8.0 | 2026-10-31 | `9cbfbae` | 阶段 C 第一步：内容 / 样式签名拆两维；新增 `ICardStyleAware`，媒体卡支持「只换样式不重建」 | 9 文件，+179 / −33 |
| v2.8.1 | 2026-10-31 | `dea72fa` | 阶段 C 收尾：`GlassPolicy` 纯决策 + `IGlassBackdrop` 抽象，两块底板搬出窗口 | 7 文件，+126 / −161，另新增 1 个源文件（189 行） |
| v2.9.0 | 2026-10-31 | `ccda52a` | 外观重写前置 P1：令牌层（`ThemeTokens` / `ThemeResolver`），按钮与 CLI 面板改用令牌 | 8 文件，+162 / −24，另新增 1 个源文件（73 行） |
| v2.9.1 | 2026-10-31 | `80ccb9d` | 外观重写前置 P2：`CliPanelView.ApplyStyle` 就地换肤，对话 / 命令行卡片支持「换外观不重建」 | 8 文件，+225 / −30 |
| v2.9.2 | 2026-10-31 | `754115e` | 外观重写前置 P3：`.mptheme` 解析 / 校验 / 导入导出（套用副本、失败不动当前设置），自检加两项 | 8 文件，+916 / −9（含新增 `src/Core/ThemeFile.cs` 668 行） |
| v2.9.3 | 2026-10-31 | `6805bbe` | P4 设置页主题入口 + 离屏预览；#7 克隆失败禁止保存；#4 `ICardActivation` 激活契约 | 13 文件，+594 / −36（含新增 `src/Core/ThemeCatalog.cs` 69 行） |
| v2.9.4 | 2026-10-31 | `055b3f9` | 修 AI 对话 / 常驻媒体卡外观不随设置变化：CLI 面板跟随新 Settings 与 monoFont，宿主换肤时重画底层 | 11 文件，+165 / −29 |
| v2.9.5 | 2026-10-31 | `856b7bd` | 源码遗留 S1+S2：单一 `ColorCode` 解析，删掉 6 处复制；非法颜色回落到 fallback 而不是变白 | 15 文件，+158 / −132（含新增 `src/Core/ColorCode.cs` 53 行） |
| v2.9.6 | 2026-10-31 | `c9842a9` | 源码遗留 S3+S4：删死字段 `_live`；两个 `async void` 加顶层兜底，异常不再逸出崩进程 | 7 文件，+82 / −12 |
| v2.9.7 | 2026-10-31 | `69cb3b4` | 源码遗留 S7+S12：对话上下文跟随 `Cli.MaxEntries`；设置页 / 校验保证最多一张交互卡 | 8 文件，+132 / −13 |
| v2.10.0 | 2026-10-31 | `0605fee` | 共享控件第一梯队（上）：`CardList` 主题化列表（虚拟化 / 空态 / 激活）+ `CardStatus`（转圈 / 进度 / 错误态） | 8 文件，+630 / −9（含新增 `CardList.cs` 206 行、`CardStatus.cs` 218 行） |
| v2.10.1 | 2026-10-31 | `d994c6e` | 共享控件第一梯队（下）：`CardHeader`/`CardToolbar`、`CardInput`、`CardDropOverlay` + 宿主拖入/拖出反馈 | 12 文件，+543 / −26（含新增 `CardHeader.cs` 126 行、`CardInput.cs` 144 行、`CardDropOverlay.cs` 101 行） |
| v2.10.2 | 2026-10-31 | `0dd75b6` | 第二梯队（上）：`CardRenderContext.Host` 透传 + `ClipboardService` + 串行可取消 `CardJobQueue` | 10 文件，+463 / −23（含新增 `CardJobQueue.cs` 177 行、`ClipboardService.cs` 129 行） |
| v2.10.3 | 2026-10-31 | `ab77ad9` | 第二梯队（下）：`ShellIcons`（扩展名图标 + STA 缩略图）+ `DragSource`（拖出阈值 / 文件 / 文本） | 8 文件，+457 / −18（含新增 `ShellIcons.cs` 294 行、`DragSource.cs` 70 行） |
| v2.11.0 | 2026-10-31 | `ecf4984` | 文件中转站 M1：拖入 / 引用 / 复制、列表、持久化、后台复制队列、设置区块 | 11 文件，+1106 / −25（含新增 `src/Cards/DropStationCard.cs` 914 行） |
| v2.11.1 | 2026-10-31 | `1f7e7f6` | 按钮视觉与 HUD 统一：幽灵描边替代实心胶囊；中转站清空降为普通语气并在空列表时禁用 | 9 文件，+128 / −77 |
| v2.12.0 | 2026-10-31 | `fded63a` | 文件中转站 M2：行内拖出、剪贴板粘贴、复制进度 / 速度 / 取消、移动模式、丢失清理；按钮与列表留白对齐 AI 面板 | 10 文件，+530 / −57 |
| v2.12.1 | 2026-10-31 | `aead3aa` | 文件中转站 M3 列表管理：搜索过滤、Ctrl/Shift 多选与批量操作、自动清理丢失项、显示图标开关 | 9 文件，+463 / −91 |
| v2.12.2 | 2026-10-31 | `78ed0a1` | 文件夹递归收纳：目录作为一条列表项，Copy / Move 保留子目录与空目录；运行时扩展卡片调研文档 | 9 文件，+757 / −106 |
| v2.13.0 | 2026-10-31 | `4956352` | 拆分宿主上帝类第一步：`CardSlot`/`CardHost`、`CardLayoutCoordinator`、`OverlayIntro` | 9 文件，+541 / −379 |
| v2.13.1 | 2026-10-31 | `5eb0bea` | 拆分宿主上帝类第二步：`HotkeyRouter` 独立，热键注册 / 回退 / 分发移出控制器 | 7 文件，+345 / −230 |
| v2.13.2 | 2026-10-31 | `44670f4` | 拆分宿主上帝类第三步：`TrayController`、`CommandModeController` 独立 | 8 文件，+394 / −187 |
| v2.13.3 | 2026-10-31 | `1802dcf` | 评审报告 P1-1..P1-10：配置修复 / 原子写 / Key 备份 / 聊天卡回滚 / 中转站生命周期 / 媒体与采样线程 / URL 安全 | 21 文件，+1640 / −448 |
| v2.13.4 | 2026-10-31 | `eaa479c` | 评审报告 P2 高价值项：句柄/定时器泄漏、热键旧绑定、命令输出上限与日志脱敏、主题 1MB、队列累积、音量副作用、PDH 状态、ffmpeg 代次 | 16 文件，+1046 / −170 |
| v2.13.5 | 2026-11-01 | `7920969` | 2.13.4 审阅意见：中转站孤儿/合并、热键路由、Chat 快照、DPAPI/备份、版本号、Replace 回退、命令脱敏、解码器切换、媒体 Join | 20 文件，+1199 / −204 |
| v2.13.6 | 2026-11-01 | 本次提交 | 功能分支 `feature/runtime-card-loading`：运行时卡片插件（manifest + ALC + 启动早期注入 + 示例插件 + 未知 Kind 保留） | 14 文件，+496 / −30 |
| v2.14.0 | 2026-11-01 | 本次提交 | 运行时插件完善：影子复制、缺失插件「插件不可用」占位卡、manifest / Kind 校验与加载报告、`--plugin-dev` 纯文字卡热重载、延迟 ALC 弱引用验证、设置页引用释放、`publish.bat` 插件备份 | 10 文件，+865 / −98，另新增 2 个源文件（38 + 327 行） |
| v2.14.1 | 2026-11-01 | `730fd39` | 方案 B 第一步：拆出独立 `MirrorpondHUD.Contracts` 契约程序集；宿主 `TypeForwardedTo` 兼容旧插件；插件 ALC 对宿主与 Contracts 都返回 `null`；示例插件改引 Contracts | 22 文件，+103 / −9（15 个契约源码文件原样迁出，另新增契约工程 / `TypeForwards.cs`） |
| v2.14.2 | 2026-11-01 | `f67a255` | 方案 B 第二步：`ConfigStore.SettingsNormalizer` 委托注入 `CardModel.Normalize`，拆掉 Core→Cards 编译依赖，行为不变 | 4 文件，+20 / −6 |
| v2.14.3 | 2026-11-01 | 本次提交 | 方案 B 收尾：版本 2.14.3；`ThemeTokens` / `ThemeResolver` 迁入 Contracts（宿主类型转发）；SelfTest 契约身份断言；最终验证 | 5 文件，+30 / −10（文档同步另计） |
| v2.14.4 | 2026-11-01 | `ef70cea` | 删除宿主 `TypeForwards`，插件 API 升到 2：`apiVersion=1` 旧插件被明确拒绝、宿主不崩；示例 manifest 0.2.0 / apiVersion=2 | 5 文件，+5 / −57 |
| v2.14.5 | 2026-11-01 | 本次提交 | 方案 B 程序集边界完成：拆出 `MirrorpondHUD.UiKit`（8 个共享 WPF 控件迁入、只依赖 Contracts），宿主 ProjectReference；插件 ALC 对宿主 / Contracts / UiKit 都走默认 ALC；三工程版本 2.14.5 | 11 文件，+23 / −14，另新增 2 个文件（UiKit 工程 31 行 + AssemblyInfo 4 行）；文档同步另计 |
| v2.14.6 | 2026-11-01 | 本次提交 | 低风险清理：删 `CardPluginHandle.EntryType` 与 `SettingsMigration` 无调用重载；新增「插件引用宿主程序集」明确拒绝；过期注释 / 失效文档路径清理；版本 2.14.6 | 8 文件，+26 / −19（文档同步另计） |
| v2.14.7 | 2026-11-01 | `abcbcec` | 删除旧 Kind 别名与旧 Schema 迁移（开发阶段无用户）：clock/metrics/keys/text 不再转 infotext，旧 Schema 按损坏处理 | 11 文件，+30 / −434 |
| v2.14.8 | 2026-11-01 | `633e9d6` | Contracts 瘦身：内置卡设置 Command/Media/Chat、DPAPI SecretStore、默认内容迁回宿主，Contracts 只留通用契约 | 11 文件，+351 / −358（含新增 `BuiltInCardSettings.cs` 316 行、`CardDefaults.cs` 21 行） |
| v2.14.9 | 2026-11-01 | 本次提交 | 最终审阅修复：`CardSettings.Kind` 默认值 `metrics`→`infotext`、README 设置示例改 `Extras.command/media/chat`（P1×2）、旧迁移/旧 Kind 注释与 SelfTest 夹具清理（部分 P2）；版本 2.14.9 | 10 文件，+18 / −25（源码部分，文档同步另计） |
| v2.15.0 | 2026-11-01 | `78d3381` | UiKit 第一优先表单控件（Slider/Toggle/Select/Number/Color/Segmented/FormRow/SettingsForm）+ 根目录 DLL 卡片插件开发指南；SelfTest 表单控件回归 | 6 文件，+1365 / −4 |
| v2.16.0 | 2026-11-01 | `6406d96` | 6 张内置卡全部 DLL 插件化（宿主内置 0）；新增宿主服务契约；UiKit 媒体控件化；插件 API 仍 2 | 60 文件，+1568 / −1557 |
| v2.17.0 | 2026-11-01 | `27b8bbd` | 第二优先数据展示控件第一批：CardTable（虚拟化 / 排序 / 选中）、CardBadge、CardTag、CardMetricTile、CardGauge、CardProgressRing；版本三件套 + 6 插件同步 2.17.0 | 18 文件，+1315 / −17 |
| v2.18.0 | 2026-11-01 | `1e1c6ac` | 第二优先第二批：CardSparkline / CardLineChart / CardBarChart / CardTree / CardTimeline；版本三件套 + 6 插件同步 2.18.0 | 19 文件，+2049 / −16 |

---

## 1. v2.4.1（`82a8f80`，标签 `v2.4.1`）

### 背景

AI 对话卡还是「输入框 + 聊天记录」的普通表单样式，Key 以明文写进 `settings.json`；桌面亚克力切窗口时玻璃明显慢半拍。

### 改动

1. **AI 对话卡改成命令行（CLI）样式**：第一行模型名 + 右侧 `[单次] / [轮询]`，一条细横线，下面 `>` 提示符输入行 + 等宽字体记录（用户行暗、回复行亮）；卡片上不再放按钮。
2. **单次 / 轮询、清空上下文移到「热键」页**（`ICardHotkeySource` / `ICardHotkeyTarget`；`chat.toggle-mode` 默认 `Ctrl+Alt+Shift+M`，`chat.clear` 默认留空），改模式立刻落盘；回答期间输入框只读而不禁用，焦点留在提示符后面。
3. **API Key 用 Windows DPAPI 加密**（`crypt32`，当前用户 + 附加熵），`settings.json` 里只落 `dpapi:<base64>`；旧明文读入时自动加密并回写；设置页不回显。
4. **修切换玻璃实现后旧底板滞留**：系统亚克力 ↔ 桌面亚克力切换时先 `Retire` 另一块，三种实现互斥；`BackdropActive` 改成派生属性。
5. **视频窗一起排除出截屏**：视频窗是 owner 为卡片的独立顶层窗口，之前只排除了卡片窗口，桌面亚克力下截图里会剩一块视频画面。
6. **切窗口时玻璃跟手**：加全局 `WinEvent` 钩子（`EVENT_SYSTEM_FOREGROUND / MINIMIZE / MOVESIZEEND / SWITCHEND` + `OBJECT_SHOW/HIDE`，跳过本进程）即时补帧，40 ms 后再补一帧；整叠卡片的并集区域只从屏幕读一次进共享 DIB，各卡从内存 DC 里拉伸自己那块。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/ChatCard.cs` | +295 / −146 | 终端版式、热键动作、只读而非禁用、点卡片聚焦 |
| `src/Core/SecretStore.cs` | +147 / 新增 | DPAPI 加解密、`dpapi:` 前缀判定、解不开的识别 |
| `src/Core/ConfigStore.cs` | +18 / −1 | 读到旧明文 Key 时加密并立刻回写 |
| `src/Core/Settings.cs` | +77 / −1 | `ApiKey` / `PlainApiKey` / `HasApiKey` / `ApiKeyUnreadable`、对话热键默认值 |
| `src/Ui/AcrylicBackdrop.cs` | +333 / −26 | 旧底板回收、WinEvent 补帧、并集取屏、视频窗排除 |
| `src/Ui/CardEditor.cs` | +67 / −6 | Key 掩码、清除按钮、失焦 / 保存时提交 |
| `src/Ui/OverlayWindow.xaml.cs` | +58 / −37 | 三种实现互斥、`RetireSystem/RetireAcrylic`、排除重设 |
| `src/Ui/SettingsWindow.xaml` | +18 / −5 | Key 输入框与提示、清除按钮 |
| `src/Ui/SettingsWindow.xaml.cs` | +28 / 新增 | Key 提交 / 掩码状态、自检入口不变 |
| `src/SelfTest.cs` | +90 / −4 | 新增「Key加密」「对话热键」两项自检 |
| `docs/cards-architecture.md` | +35 / −9 | 终端样式与 Key 存储的说明 |
| `docs/glass-implementations.md` | +6 / −1 | 视频窗排除 |
| `docs/chat-card.png` | 新增 | AI 对话卡截图 |
| `README.md` | +63 / −5 | AI 卡片、Key 加密、玻璃实现 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 |

### 实测

| 指标 | 改前 | 改后 |
|---|---|---|
| 屏幕变化 → 玻璃跟上 | 163 ms（中位）/ 229 ms（最坏） | **30 ms（中位）/ 49 ms（p90）** |
| CPU（4 张卡） | 4.2% | 4.8%（多出来的是并集区域那一次屏读） |

---

## 2. v2.4.2（`70343ef`）

### 背景

「桌面亚克力模式定期规律地从上到下变深一下」+「能否用 GPU 加速」。测量结论：模糊只占 0.61 ms，**慢的是取图**——GDI 读屏一次要等 DWM 合成，18.6 ms 且堵 UI 线程。

### 改动

1. **新增 `src/Ui/DesktopFrameSource.cs`**：手写 vtable 调用 COM（不引 NuGet），`IDXGIOutput1::DuplicateOutput` + `CopySubresourceRegion`（只拷卡片整叠区域）+ `Map` + `RtlMoveMemory`。
2. **同期重画改成同步绘制**：`GetDC(hwnd)` + `StretchBlt` 直接贴，少一个「已失效未重画」的中间状态；帧与上一帧差 ≤2 就不重画。
3. **安全网**：按系统生成号选 `DuplicateOutput` 的 vtable 下标（26200 = 22，更早 = 19，未验证的走 GDI）、`GetDesc` 验收、首帧黑帧预热、`ACCESS_LOST` 重建、闲置 5 秒释放、`MIRRORPOND_GLASS_SOURCE=gdi|dup`。
4. 取图便宜后刷新从 200 ms 提到 100 ms；新增两个诊断开关 `MIRRORPOND_GLASS_TRACE` / `MIRRORPOND_RAW_TRACE`。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Ui/DesktopFrameSource.cs` | +498 / 新增 | DXGI 桌面复制、vtable 索引、区域拷贝、失败回退、诊断 |
| `src/Ui/AcrylicBackdrop.cs` | +236 / −3 | 改用 `DesktopFrameSource`、同步绘制、判同帧、共享 DIB |
| `docs/glass-implementations.md` | +116 / −2 | §4.3：实测数字、风险、验证方式 |
| `README.md` | +1 / −1 | 版本号与说明 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 |

### 实测

| 指标 | GDI | DXGI 桌面复制 |
|---|---|---|
| 一轮取图 | 18.6 ms（等 vsync，堵 UI 线程） | **0.1 ms（桌面没变）/ 1–1.5 ms（新帧）** |
| 玻璃落后屏幕 | 46 ms（中位） | **16 ms（中位）/ 52 ms（p90）** |
| CPU（4 张卡） | 4.8% | 7.6–9.8%（帧率翻倍带来的） |
| 内存 | — | +13 MB |

采样图与 GDI 路径逐字节一致（190×46 取样 BMP 差 0）。

---

## 3. v2.4.3（`aabe441`）

### 背景

- 取图方式只在环境变量里能切，用户改不了；
- 仍偶发「玻璃闪一下 / 突然变清晰」。

### 改动

1. **取图方式进设置**：「设置 → 外观 → 高级（实验性）→ 取图方式」：自动 / GPU 加速（DXGI）/ CPU 读屏（GDI）；`Settings.Style.GlassSource` 参与持久化与归一化，环境变量仍然优先。
2. **修闪烁根因**：`FinishFrame` 把模糊图写进 DIB 的动作在「与上一帧相同」的提前返回**之后**，桌面静止时 DIB 里留的是**未模糊的原图**，任何 `WM_PAINT` 都会把它贴出去。改法：先无条件写模糊图，只跳过重画；底板没动时 `SWP_NOREDRAW`；贴完 `ValidateRect`。
3. 设置页根据玻璃实现显示 / 隐藏对应行，并写了「为什么自动模式可能退回 CPU」的提示。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Core/Settings.cs` | +30 / 新增 | `GlassSource` 字段、`NormalizeGlassSource`、归一化夹取 |
| `src/Ui/DesktopFrameSource.cs` | +16 / −6 | `TryCreate(preferred, out source, out reason)`：按设置决定，环境变量优先 |
| `src/Ui/AcrylicBackdrop.cs` | +102 / −13 | 先写模糊图再判重画、`SWP_NOREDRAW`、`ValidateRect` |
| `src/Ui/OverlayWindow.xaml.cs` | +4 / 新增 | 把 `GlassSource` 传给取样层 |
| `src/Ui/SettingsWindow.xaml` | +15 / 新增 | 取图方式下拉、提示行 |
| `src/Ui/SettingsWindow.xaml.cs` | +40 / 新增 | 状态联动、tag 映射、提示文案 |
| `docs/glass-implementations.md` | +58 / 新增 | §4.4：根因、修法、清晰度对比表 |
| `README.md` | +3 / −2 | 设置项说明 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 |

### 实测

同一块区域两种路径的「清晰度」（高频能量，越小越糊）：

| 路径 | 清晰度 |
|---|---|
| `WM_PAINT` 贴出来的（闪烁那一帧） | 21–28 |
| 新取的一帧（正常） | 0.56–1.26 |

差 20–25 倍，正好是「原图 vs 模糊图」，确认了根因。

---

## 4. v2.4.4（`05857ce`）

### 背景

两个观感问题：**真亚克力圆角外多出一圈更大的透明圆角**、**每张卡片顶部周期性突然变深**。根因是同一个：模糊图原先画在一块**独立的底板窗口**上，和卡片是两个被 DWM 分别合成的窗口（半径不同 → 露边；差 1–2 px → 顶部露一条底板）。

### 改动

1. **删掉底板窗口**：`src/Ui/AcrylicBackdrop.cs`（1424 行）删除，新增 `src/Ui/GlassFeed.cs`。取样循环（DXGI / GDI、并集一次读、缩小、方框模糊）保留，最后一跳从「GDI 贴窗口 DC」改成 **`BitmapSource` → 卡片自己的玻璃层**（`OverlaySurface.SetGlass`，兼容实现早就在用的通路）。
2. 顺带删掉 `DWMWA_SYSTEMBACKDROP_TYPE`、DWM 圆角、「底板要排除自己」那套逻辑。
3. **模糊强度 / 刷新帧率 / 取图方式进设置**（外观 → 高级（实验性））；默认 18 px / 10 帧 / 自动。
4. `PixelFormats.Bgr32`（不是 Bgra32）：屏幕 DC 数据 alpha 全 0，用 Bgra32 会整块透明。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Ui/GlassFeed.cs` | +860 / 新增 | 卡片内玻璃：取样、缩放、方框模糊、判同帧、写入 `Surface.SetGlass`、可用性判断、追踪 |
| `src/Ui/AcrylicBackdrop.cs` | −1424 / 删除 | 底板窗口整体移除 |
| `src/Ui/OverlayWindow.xaml.cs` | +34 / −49 | `Attach` / `Detach`、排除截屏、玻璃激活 / 收尾、兼容定时器保留 |
| `src/Core/Settings.cs` | +10 / −1 | `GlassBlur` / `GlassFps` 字段与夹取 |
| `src/Ui/SettingsWindow.xaml` | +32 / −4 | 模糊强度 / 刷新帧率滑杆、取图方式、提示 |
| `src/Ui/SettingsWindow.xaml.cs` | +29 / −20 | 行显隐、数值读写、提示文案 |
| `docs/glass-implementations.md` | +56 / 新增 | §4.5：根因、改法、CPU 实测、验证日志 |
| `README.md` | +4 / −4 | 桌面亚克力描述与设置入口 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 |

### 实测

- 4 张卡、20 秒平均、占单核：**5 帧 3.4% / 10 帧约 6–8% / 15 帧 7.7%**，基本随帧率线性；比 2.4.3 底板方案（10 帧 4.4%）高，因为现在每帧要经 WPF 重画卡片。
- 启动日志：`玻璃画面已画进卡片：157x30（模糊半径 9px，取图后端 DXGI 桌面复制）`；`EnumWindows` 里已没有 `MirrorpondHUD.AcrylicBackdrop` 窗口。
- 圆角 / 顶部暗带属于结构问题，改后**不可能再出现**（同一窗口、同一次合成、圆角由卡片自己的 `CornerRadius` 裁剪）。

---

## 5. v2.4.5（`f4b2b8b`）

### 背景（用户一次提了 7 条）

文字不居中、Reicon 字形偏高；拖动时玻璃不刷新；命令行面板外观要和 AI 面板一致并删掉多余外观设置；每条问答 / 命令要独立小面板 + 独立滑块；滑块要与卡片风格统一（模块化）；AI 与命令行归为「CLI 交互面板」，最大宽度 / 高度 / 小面板高度进设置；做一份外观主题调研。

### 改动

1. **新增 `src/Ui/Controls/CliUi.cs`**：`CliPanelView`（标题行 / 分隔线 / 记录区 / 输入行 / 状态行）、`CliEntryPanel`（每条记录的内部小面板，自带滑块与高度上限）、`CliScrollBars`（按前景色生成并缓存 `ScrollBar` 样式，挂到子树 `Resources`，只影响该面板）、`CardPalette`（取色 / 派生灰阶）、`ICliFocusTarget`（`Begin` / `FocusInput` / `Cancel`）。
2. **命令行面板从 `TextCanvas` 自绘改成富内容面板**：删掉窗口里的隐藏输入框与宿主自绘历史（`OverlayController` −177 行），宿主只负责唤起、抢前台、把焦点交给输入行；工具 / 目录 / 超时仍属于每张命令行卡片。
3. **每条命令 / 每轮问答一块内部小面板**，各自滚动；整块超过上限再用外层滑块。
4. **新设置「外观 → CLI 交互面板」**：最大宽度 460 / 最大高度 420 / 内部小面板最大高度 140 / 最多保留 12 条；删除已失效的「超长单行截断 / 单行字符数」与 AI 卡片的独立宽度。
5. **`TextCanvas` 图标与居中**：所有 run 排同一条正文基线；图标 / 键帽按 `BuildGeometry` 的真实墨迹中心对齐（带缓存）；行高把图标占位算进去；整块文字在卡片内水平 + 垂直居中。`IconScale` 语义改为「1.0 = 与文字等高」，默认 1.45 → 1.15，`SchemaVersion 4` 一次性迁移。
6. **拖动时玻璃跟手**：截屏排除节流到 2 秒一次（原来每个鼠标移动都 `EnumWindows` + `SetWindowDisplayAffinity`，把取样定时器挤掉）；拖动中按合成帧补取样（`DispatcherPriority.Render`，`GlassFeed.RequestFrame` 按后端 16 / 55 ms 节流），松手再补一帧。
7. **主题调研**：`docs/theme-system.md` + 三份示例主题（`.mptheme`，内容为 JSON）。
8. **验收工具**：`--screenshot-cards` 现在包含命令行卡，`MIRRORPOND_CLI_DEMO=1` 时给 CLI 面板塞样张；`--screenshot-settings` 支持加高窗口与 `MIRRORPOND_SETTINGS_SCROLL=1` 滚到底；命令面板补执行日志。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Ui/Controls/CliUi.cs` | +513 / 新增 | 面板骨架、内部小面板、滑块样式、取色、焦点接口 |
| `src/Ui/TextCanvas.cs` | +122 / −27 | 墨迹对齐、行高含图标、整块居中、基线统一、缓存 |
| `src/Cards/ChatCard.cs` | +65 / −299 | 改成 `CliPanelView` 子类，每轮问答一块小面板 |
| `src/Cards/CommandCard.cs` | +167 / −5 | 改成富内容面板（`CommandView`），命令执行与日志 |
| `src/Ui/OverlayController.cs` | +25 / −177 | 删掉自绘命令行与历史 / 输入 / 取消令牌，改为唤起 + 交焦点 |
| `src/Ui/OverlayWindow.xaml` | +1 / −21 | 删掉隐藏 `CommandInput` 输入框 |
| `src/Ui/OverlayWindow.xaml.cs` | +75 / −53 | `BeginCommandInput` 改为激活 + 交焦点；截屏排除节流；拖动补帧；`GLAS` 追踪 |
| `src/Ui/OverlaySurface.cs` | +3 / 新增 | `ContentElement`（把焦点交给 CLI 面板） |
| `src/Ui/GlassFeed.cs` | +22 / 新增 | `RequestFrame(force)`：拖动即时补帧，按后端节流 |
| `src/Core/Settings.cs` | +61 / −10 | `CliSettings`（宽 / 高 / 小面板高 / 条数）、`IconScale` 默认值与语义、命令行截断默认关 |
| `src/Core/SettingsMigration.cs` | +17 / 新增 | v3 → v4：旧默认 1.45 迁移到 1.15 |
| `src/Ui/CardEditor.cs` | −38 | 删掉 `ChatWidth`、`Truncate`、`MaxLineChars` 等已失效绑定 |
| `src/Ui/SettingsWindow.xaml` | +40 / −14 | 新增 CLI 尺寸组、删截断类行、AI 宽度行、图标提示改写 |
| `src/Ui/SettingsWindow.xaml.cs` | +52 / 新增 | CLI 尺寸框读写 / 校验 / 提示 |
| `src/Ui/Screenshots.cs` | +80 / −1 | 命令行卡进截图、CLI 样张、设置页加高 / 滚到底 |
| `docs/theme-system.md` | +165 / 新增 | 主题方案：现状、方案对比、`.mptheme` 格式、映射、流程、风险 |
| `docs/themes/midnight.mptheme` | +45 / 新增 | 示例主题 |
| `docs/themes/paper.mptheme` | +45 / 新增 | 示例主题（浅色） |
| `docs/themes/terminal-green.mptheme` | +45 / 新增 | 示例主题（复古终端） |
| `docs/cards-architecture.md` | +45 / −6 | §9 CLI 交互面板；命令行字段与生命周期更新 |
| `docs/glass-implementations.md` | +34 / 新增 | §4.6：拖动刷新的两个原因与实测 |
| `README.md` | +24 / −16 | CLI 面板、图标语义、设置项、文档索引、版本号 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 |

### 实测

| 项 | 改前 | 改后 |
|---|---|---|
| 文字块左右留白（422×60 卡） | 16 / 62 | **16 / 16** |
| 文字块上下留白 | 15 / 6 | **14 / 12** |
| 图标 vs 文字字形高 | 16 vs 11 px，中心差 3.5 px | 13 vs 11 px，**中心同一行 `cy=12.5`** |
| 拖动中取帧（桌面亚克力） | 9.2 帧/秒（按设置的 10） | **34.8 帧/秒**（间隔 28.7 ms） |
| 拖动中取帧（兼容实现） | 5.6–6.3 帧/秒/卡 | **21.1 帧/秒**（被拖那张） |
| AI 面板宽度 | 380（每卡设置） | 460（CLI 面板设置，可改） |

- `--selftest`：**失败项 0**。
- F9 端到端（桌面亚克力模式）：`命令行模式：前台=True，键盘焦点在面板输入行=True` → `命令行完成：退出码 0，输出 5 字`。
- 注：这个模式下卡片被排除在截屏之外，所以观感验收用的是离屏渲染（`--screenshot-cards`）与端到端抓图，不是屏幕截图。

---

## 6. v2.5.0（本次提交，标签 `v2.5.0`）

### 背景

做了一轮模块化现状评审（`docs/modularity-review.md`），查出宿主 4 处可直接修的问题：
命令行一开其它卡片停更、富内容契约 `UpdateContent` 从未接线、每次应用设置都无条件重建全部富内容、
每张卡片各持一份整份配置的 JSON 深拷贝。本次只修这 4 条，不动架构。

### 改动

1. **命令行打开时其它卡片不再停更**：`RenderNow` 里的全局 `_commandMode` 栅栏删掉，
   只靠循环里的 `IsInteractive` 跳过交互卡自己。
2. **富内容按需重建**：新增 `ContentInputs` + `ContentSignature()`（字符串快照，存进 `CardSlot.ContentSignature`），
   `SyncCards` 比较签名——内容相关输入没变就 `RefreshSettings` + `UpdateContent`，变了才 `ReleaseContent` + `CreateContent`。
   签名不含 `Overlay.X/Y/Monitor/Locked`（拖动时每帧都在变），含 `Style` / `Cli` / 圆角 / 投影。
   用字符串快照而不是比较对象，是因为设置页直接改实时 `Settings`，应用那一刻拿不到“改前值”。
3. **`UpdateContent` 真正被调用**（原来是死契约）：`MediaCard.UpdateContent` 的同路径重绑保留用户暂停状态，
   换解码器重开后按原状态 `Play()` / `Pause()`；`ApplyVideoSettings` 改走 `Play()` / `Pause()`，让底部状态行同步。
4. **每卡配置不再整份深拷贝**：`BuildDialog()` 改为浅拷贝（`Style` / `Cli` / `Data` / `Behavior` / `Hotkeys` / `Cards` 共享引用），
   只有 `Overlay` 独立复制（窗口会改自己的 X / Y / 锁定）；删掉 JSON 深拷贝的 `CloneSettings`。
5. **新增文档**：`docs/modularity-review.md`（评审 + P0 实施记录 + 对文件中转站 / 快捷画板 / 外观重写的支撑评估）；
   `docs/theme-motion.md`（主题动效与控件样式调研，此前已在工作区、README 已引用，随本次一并入库）。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Ui/OverlayController.cs` | +116 / −21 | 内容签名 + 按需重建 + 配置浅拷贝 + 去掉渲染栅栏 |
| `src/Media/MediaView.cs` | +13 / −3 | 同路径重绑保留播放 / 暂停状态 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 2.4.5 → 2.5.0 |
| `README.md` | +2 / −0 | 设计资料索引（评审文档、主题动效调研） |
| `docs/modularity-review.md` | 新增 500 行 | 评审 + P0 实施记录 + 三个方向的支撑评估 |
| `docs/theme-motion.md` | 新增 582 行 | 主题动效与控件样式调研 |

### 实测

- `dotnet build -c Release`：**0 警告 0 错误**。
- `--selftest`：**失败项 0**（5 个卡片模块注册、热键、键盘钩子、采样全部正常）。
- `--smoke 6 --smoke-visible`：退出码 0，5 张卡片建出，启动动画与毛玻璃正常。
- 未覆盖：应用设置时的签名比较与 `UpdateContent` 调用（没有 CLI 入口能驱动 `ApplySettings`），
  手工回归清单见 `docs/modularity-review.md` §7 / §8。

---

## 7. v2.6.0（本次提交，未打标签）

### 背景

评审文档里的 P1-1 / P1-2：新增一张**带设置项**的卡片，要同时改 `Settings.cs`、`CardModel`、`CardEditor`、
`SettingsWindow.xaml` 四处（其中三个是 700 ~ 1500 行的巨型文件），「加一个类即可」只在没有设置项的卡片上成立。
本次把「卡片自己的设置」整体交还给模块，并用命令行卡做试点。

### 改动

1. **`CardSettings` 不再认识具体卡片**：删掉 `Command` 字段，改为 `Extras`（`Dictionary<string, object>`）+
   `ExtrasAs<T>(key)` / `SetExtras(key, value)`。`Json` 增加 `Dictionary<string, object>` 绑定分支，
   模块设置按原样落盘、由模块自己强类型解析。
2. **配置结构 v4 → v5**：`SettingsMigration` 把每张**命令行卡片**旧的 `Command` 子对象搬进 `Extras.command`
   （其它卡片以前也各带一份拷贝，但从没被读过，不写进新配置）。迁移后立刻回写。
3. **三个模块契约**（`src/Cards/CardSettingsModule.cs`）：
   `ICardSettingsModule`（补默认值 / 归一化，`CardModel` 调用）、`ICardEditorSection`（模块自带设置区块）、
   `ICardSettingsValidator`（模块自带校验，宿主只拼卡片名）。
4. **命令行卡试点**：设置改从 `Extras.command` 读写；`CommandCardEditor.cs` 用代码搭出原来的设置面板
   （工具 / 自定义命令 / 工作目录 / 返回内容上限 / 提示符预览）；校验（自定义模板不能为空）也搬进模块。
5. **设置页瘦身**：`CardEditor` 删掉命令行的 8 个属性，`SettingsWindow.xaml` 删掉 70 行面板，
   改成一个 `ContentControl` 绑定模块区块；`SettingsWindow.xaml.cs` 删掉两个已无引用的目录选择处理器。
6. **测试与文档**：`--selftest` 新增「卡片设置=OK」（Extras 读写 + 模块区块创建）；
   `cards-architecture.md` 更新 JSON 示例与「带设置的卡片」模板；`modularity-review.md` §8.2 记录本轮实施。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/CardSettingsModule.cs` | 新增 45 行 | 三个模块设置契约 |
| `src/Cards/CommandCardEditor.cs` | 新增 259 行 | 命令行卡自己的设置区块 |
| `src/Core/Settings.cs` | +46 / −4 | `Extras` + 读写助手，schema 5 |
| `src/Core/Json.cs` | +5 / −0 | `Dictionary<string, object>` 绑定 |
| `src/Core/SettingsMigration.cs` | +71 / −16 | v4 → v5 迁移（只搬命令行卡） |
| `src/Cards/CommandCard.cs` | +50 / −8 | 实现三个契约，设置读写走 `Extras` |
| `src/Cards/CardModel.cs` | +11 / −2 | 改为调用模块的 `InitializeCard` |
| `src/Ui/CardEditor.cs` | +48 / −102 | 删命令行属性，加模块区块宿主 |
| `src/Ui/SettingsWindow.xaml` | +4 / −74 | 70 行面板 → 一个 `ContentControl` |
| `src/Ui/SettingsWindow.xaml.cs` | +10 / −44 | 传 `_working`、模块校验、删无用处理器 |
| `src/Ui/OverlayController.cs` | +8 / −8 | 签名改用 `Extras`，`CommandOf` 走模块 |
| `src/SelfTest.cs` | +38 / −12 | Extras 往返 + 模块区块断言 |
| `docs/cards-architecture.md` | +53 / −7 | JSON 示例、Extras 说明、带设置卡片模板 |
| `docs/modularity-review.md` | +57 / −22 | §8.2 实施记录与状态更新 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 2.5.0 → 2.6.0 |

### 实测

- `dotnet build -c Release`：**0 警告 0 错误**。
- `--selftest`：**失败项 0**，逻辑自检新增「卡片设置=OK」。
- **迁移实测**（用真实的 schema 4 配置启动）：日志 `设置已从 SchemaVersion 4 升级到 5（卡片自己的设置搬进 Extras）`；
  迁移后 `SchemaVersion=5`、5 张卡片 Id 全部保留、`Style` / `Hotkeys` 完全一致、
  只有命令行卡获得 `Extras.command`（`Shell=cmd` 等旧值保留）、旧的 `Command` 键消失。
- `--smoke 4 --smoke-visible`：退出码 0，无异常。
- 未覆盖：设置页里**真实点击**模块区块（布局与交互需要手工确认），清单见 `docs/modularity-review.md` §8.4。

> 注意：schema 5 的配置用 v2.5.0（schema 4）的旧 exe 打开会忽略 `Extras`，命令行卡的设置会显示成默认值。
> 回退到旧版本前先把 `settings.json` 备份（迁移时也会自动留一份 `.bak`）。

---

## 8. v2.6.1（本次提交，未打标签）

### 背景

评审文档 P1-3：`CardModel.Normalize` 里还硬编码着旧 Kind 名单（`metrics` / `clock` / `keys` / `text`）
和 `module is InfoTextCard` 判断，模型层因此认识具体模块——加一种卡片就要再改一次 `CardModel`。

### 改动

1. **新增 `ICardLegacyAlias`**（`src/Cards/CardSettingsModule.cs`）：模块自己声明接管过哪些旧 Kind；
   `CardRegistry.TryResolveAlias` 统一解析，宿主不再维护名单。
2. **`InfoTextCard` 实现该接口**（接管 metrics / clock / keys / text），并把「预设按内容反推」搬进
   `InitializeCard`（`ICardSettingsModule` 的数据面契约）。
3. **`CardModel.Normalize` 去掉具体类型判断**：改成「别名解析 → 模块 `InitializeCard`」，行为与旧版一致。
4. **自检**新增旧 Kind 用例：`clock` / `text` → `infotext`，预设按内容恢复成 `clock` / `text`。
5. **文档**：`cards-architecture.md` 补 `ICardLegacyAlias`；`modularity-review.md` 标记 P1-3 完成。

### 一个容易踩的细节

`CardSettings.Preset` 的默认值是 `metrics`，**不是空**。所以旧配置里 clock / keys / text 卡片的预设
从来不是靠 Kind 直接赋值，而是靠「内容恰好等于某个预设的默认内容」反推（`InferPreset`）。
本次只是把这段逻辑从宿主搬进模块，行为保持不变；正因如此，自检用例必须用**真实默认内容**才能验证。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/CardSettingsModule.cs` | +12 / −0 | `ICardLegacyAlias` 契约 |
| `src/Cards/CardRegistry.cs` | +29 / −0 | `TryResolveAlias` 统一解析旧 Kind |
| `src/Cards/InfoTextCard.cs` | +19 / −1 | 认领旧 Kind，预设归一化进 `InitializeCard` |
| `src/Cards/CardModel.cs` | +7 / −7 | 删掉硬编码名单与 `is InfoTextCard` |
| `src/SelfTest.cs` | +20 / −0 | 旧 Kind 迁移用例 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 2.6.0 → 2.6.1 |
| `README.md` | +1 / −1 | 版本记录区间 |

### 实测

- `dotnet build -c Release`：**0 警告 0 错误**。
- `--selftest`：**失败项 0**（`卡片模块=OK`，其中包含新增的旧 Kind 别名用例）。
- 未覆盖：无新增运行时路径，本次只改启动期归一化逻辑。

---

## 9. v2.6.2（本次提交，未打标签）

### 背景

v2.6.0 只把**命令行卡**的设置搬进了 `CardSettings.Extras`，媒体卡与对话卡仍是 `CardSettings` 上的 typed 字段，
设置页里也还留着两块手写面板。本次把这两张卡一起迁完：宿主不再认识任何一张具体卡片的设置。

### 改动

1. **配置结构 v5 → v6**：`SettingsMigration` 泛化成 `UpgradeCardExtras`，
   把 `Command` / `Media` / `Chat` 分别搬进 `Extras.command` / `media` / `chat`（只搬对应种类的卡片）。
2. **`CardSettings` 去掉 `Media` / `Chat` 字段**：`MediaCard.MediaOf` / `ChatCard.ChatOf` 负责读写 `Extras`。
3. **设置面板整体搬进模块**：新增
   `MediaCardSettingsView.xaml(.cs)`（约 250 行）与 `ChatCardSettingsView.xaml(.cs)`（约 200 行），
   属性名与 XAML 绑定保持原样，视觉零差异；`SettingsWindow.xaml` 删掉两块面板（约 190 行）与 6 个处理器。
4. **新增 `ICardSummary`**：卡片标题右侧的摘要由模块给（命令行 = 工具 + 目录、媒体 = 文件名、对话 = 模型名），
   宿主不再按 Kind 分支算文案。
5. **待提交草稿**：`CardEditor.CommitPendingEdits()` 在保存前遍历模块区块，把还没失焦的 API Key 草稿加密收下
   （旧实现是 `CardEditor` 上的 view-model 方法，随属性一起搬走了）。
6. **自检**：媒体 / 对话断言改走 `Extras`；Key 的 UI 行为改为针对 `ChatCardSettingsView` 验证。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/MediaCardSettingsView.xaml(.cs)` | 新增 | 媒体设置区块（原 `CardEditor` 属性 + `SettingsWindow` 面板） |
| `src/Cards/ChatCardSettingsView.xaml(.cs)` | 新增 | 对话设置区块（含 Key 草稿 / 清除逻辑） |
| `src/Cards/MediaCard.cs` | 设置 / 区块 / 校验 / 摘要四契约，读写 `Extras.media` | |
| `src/Cards/ChatCard.cs` | 设置 / 区块 / 摘要契约，读写 `Extras.chat` | |
| `src/Cards/Settings.cs` 等 | `CardSettings` 去字段、schema 6 | |
| `src/Ui/CardEditor.cs` | 删媒体 / 对话属性与面板，加 `ICardSummary` 与 `CommitPendingEdits` | |
| `src/Ui/SettingsWindow.xaml(.cs)` | 删两块面板与 6 个处理器、媒体校验 | |
| `src/Core/SettingsMigration.cs` | 泛化 `UpgradeCardExtras`（Command / Media / Chat） | |

### 实测

- `dotnet build -c Release`：**0 警告 0 错误**。
- `--selftest`：**失败项 0**（含媒体 / 对话 `Extras` 往返、`ChatCardSettingsView` 的 Key 行为）。
- **迁移实测**（真实 schema 5 配置）：日志 `设置已从 SchemaVersion 5 升级到 6`；迁移后
  `SchemaVersion=6`、旧的 `Media` / `Chat` 键全部消失、对话卡出现 `Extras.chat`，
  `BaseUrl` / `Model` / `Mode` / `SystemPrompt` / `TimeoutSeconds` 与迁移前逐字段相同，`ApiKey` 仍是 `dpapi:` 密文，
  卡片 Id / Style / Hotkeys 全部未变。
- 设置页按 F10 打开：`设置窗口已打开`，无 UI 异常。
- 未覆盖：媒体卡设置区块的**实际点击**（当前配置里没有媒体卡），需要手工加一张媒体卡确认布局与「选择…/检测」按钮。

---

## 10. v2.6.3（本次提交，未打标签）

### 背景

评审 §9 的结论：下一步要做**快捷画板**与**文件中转站**，但宿主只有「键盘独占」一个输入概念
（`IsInteractive` 同时表示单槽、抑制钩子、不穿透），而且按住未锁定的卡片就会拖动整叠——
画板没法「画」，中转站也没法接住拖进来的文件（全项目没有一处 `AllowDrop`）。

### 改动

1. **拆出 `HandlesOwnDrag`**（`ICardModule` / `CardModuleBase`，默认 `false`）：
   为 true 时 `OverlayWindow` 关掉整卡拖动，鼠标事件完全交给内容。
   `IsInteractive` 保留原意（独占键盘 / 命令行单槽）；`NeedsMouseInput` 继续表示「内容要鼠标」。
2. **新增 `ICardDropTarget`**（`CanDrop` / `Drop`）：模块只声明「收哪些、收到后做什么」，
   窗口层 `AllowDrop` / `DragOver` / `Drop` 由宿主在 `WireInputCapabilities` 里统一注册，
   拖放判定与处理都包了异常兜底。
3. **接线时机**：建卡与每次应用设置都会重新接线，卡片种类不变也能同步能力变化。
4. **锁定语义明确**：锁定 = 点击穿透（`WS_EX_TRANSPARENT`），收不到拖放；要拖入先解锁。
5. **文档**：`cards-architecture.md` 增加「输入能力」四条对照表；`modularity-review.md` 把 P1-11 与拖放通道标记完成。

### 说明

- 本轮**没有**加 `NeedsKeyboardFocus`：它现在没有真实消费者，加进去就是新的死契约；
  等真有「要键盘但不要命令行单槽」的卡片再拆。
- 拖放只做到「宿主通道 + 模块接口」；中转站需要的文件图标 / 缩略图（`IShellItemImageFactory`）与
  每卡状态存储仍按 §9.3 排后续。

### 实测

- `dotnet build -c Release`：**0 警告 0 错误**。
- `--selftest`：**失败项 0**。
- `--smoke 4 --smoke-visible`：退出码 0，启动与建卡无异常。
- 未覆盖：画板 / 拖放的实际鼠标交互（当前没有使用这两条能力的卡片），
  需要第一个用 `HandlesOwnDrag` / `ICardDropTarget` 的模块落地时手工验证。

---

## 11. v2.7.0（本次提交，未打标签）

### 背景

评审 §9 的阶段 B 还剩两项：**P1-8**（模块没有生命周期与宿主服务）和 **P1-12**（每卡状态存储）。
文件中转站和快捷画板都需要它们：一个要监听目录、记住文件列表；一个要持有笔迹草稿和撤销栈。

### 改动

1. **`ICardHost`**（`src/Cards/CardHost.cs`）：每张卡片一份的宿主服务——`Config`、`StateDirectory`、
   `ReadState` / `WriteState` / `DeleteState`、`RequestRender()` / `RequestLayout()`。
2. **`ICardHostAware`**：模块在卡片建卡 / 应用设置时 `Attach(host)`，删卡 / 退出时 `Detach(host)`。
   `OverlayController` 在 `CreateSlot`、`SyncCards`、删除循环、`Dispose` 四处接线，并处理异常。
   约定写进接口注释：**模块是单例，不能把 host 存进字段**，跨次状态放进内容控件或状态文件。
3. **`CardStateStore`**（`src/Core/CardStateStore.cs`）：每卡状态落在
   `<settings.json 目录>\cards\<卡片 Id>\`，与配置一起便携；按「临时文件 + 替换」原子写；
   卡片 Id 只允许字母 / 数字 / `-_`，文件名禁止目录分隔符与 `..`（防路径穿越）。
4. **自检**新增「状态存储=OK」：写入 / 回读 / 拒绝路径穿越 / 删除 / 不留 `.tmp`。

### 说明

- 这是**基础设施**：目前还没有模块实现 `ICardHostAware`。第一个消费者是文件中转站（文件列表）
  与快捷画板（草稿 / 撤销栈）。存储行为已由自检覆盖，Attach/Detach 接线在第一个消费者落地时会实际走到。
- 没有把状态塞进 `settings.json`：配置会越读越大，而且和 `Normalize` 的“补默认值、夹紧范围”打架。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/CardHost.cs` | 新增 | `ICardHost` / `ICardHostAware` 契约 |
| `src/Core/CardStateStore.cs` | 新增 | 每卡状态目录、原子写、路径校验 |
| `src/Ui/OverlayController.cs` | 每槽 `CardHost`，Attach / Detach 四处接线，`RequestRender` / `RequestLayout` 转发 |
| `src/SelfTest.cs` | 新增状态存储用例 |
| `MirrorpondHUD.csproj` / `README.md` | 版本号与文档区间 |

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**（`状态存储=OK`）。
- 未覆盖：Attach / Detach 的实际调用（还没有模块实现 `ICardHostAware`）。

---

## 12. v2.7.1（本次提交，未打标签）

### 背景

评审 P0-5：`RenderNow` 每 120 ms 遍历所有文字卡片，每张卡各建一份 48 键变量表（含 `DateTime.Now`、`GetTickCount64`），
同帧内各卡片的时间还可能差一秒；而时钟这种每秒才变一次的卡片也在按 8 帧/秒重算模板。

### 改动

1. **一帧一份变量表**：`RenderNow` 里按需构建一次 `Variables.Build(...)`，用 `ReadOnlyDictionary` 包一层，
   通过新的 `CardRenderContext(..., sharedVariables)` 构造重载传给每张卡片。
   这样同帧所有卡片的 `{time}` / `{uptime}` 完全一致，也省掉重复的字典构建与字符串格式化。
2. **模块声明刷新节奏**：`ICardModule.RefreshIntervalMs(CardSettings)`（默认 0 = 每帧）。
   `InfoTextCard` 按预设给出：系统信息 0（跟着采样走）、按键 250 ms、时钟 / 文本 1000 ms。
   宿主用 `CardSlot.LastRenderUtc` 做节流；卡片刚显示、设置刚应用时重置，保证立刻画一帧。
3. **只读约束**：共享变量表是只读包装，模块顺手改写会抛异常而不是串到别的卡片（接口注释里也写明按只读对待）。
4. **自检**：新增「渲染节流=OK」（共享字典实例被复用 + 三个预设的间隔符合预期）。

### 说明

- 节流只作用于**文字卡片**；富内容（图片 / 视频 / CLI 面板）本来就跳过逐帧刷新。
- 系统信息卡的间隔保持 0：采样间隔可由用户调到 100 ms，固定节流会让数字看起来"卡住"。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/CardModule.cs` | 共享变量表构造重载 + `RefreshIntervalMs` 契约与默认实现 |
| `src/Cards/InfoTextCard.cs` | 按预设声明刷新间隔 |
| `src/Ui/OverlayController.cs` | 每帧建一次变量表、节流、只读包装、显示/应用时重置时间戳 |
| `src/SelfTest.cs` | 渲染节流用例 |
| `MirrorpondHUD.csproj` / `README.md` | 版本号与文档区间 |

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**（`渲染节流=OK`）。
- `--smoke 5 --smoke-visible`：退出码 0，5 张卡片正常创建与刷新。
- 未做量化性能测量：收益主要在「少建 N−1 份变量表 + 时钟卡降频」，这两个都是代码级结论。

---

## 13. v2.8.0（本次提交，未打标签）

### 背景

评审 §9 阶段 C 的第一项：`ContentSignature` 把「内容」和「样式」混在一个签名里，导致换主题 / 改字号
也会重建全部富内容——画板笔迹、对话历史、视频进度全丢。外观重写要靠这条先修好。

### 改动

1. **签名拆两维**：`ContentSignature`（`Kind` / `Content` / `Extras`）与 `StyleSignature`
   （`Style` / `Cli` / `CornerRadius` / `Shadow`）分别存进 `CardSlot`。
2. **三分支决策**（`SyncCards`）：内容变 → 重建；只样式变 → 若模块实现 `ICardStyleAware` 就地换肤，
   否则回退重建（CLI 面板的样式是构造期固化的）；都没变 → 只 `UpdateContent` 刷新数据。
3. **新增 `ICardStyleAware`** 与 `MediaView.ApplyStyle(style, cornerRadius)`：
   媒体卡换肤只重算文件名 / 进度条与圆角裁剪，视频不重启、动图不回第一帧。
4. **自检**：媒体卡实现 `ICardStyleAware` 且 `MediaView.ApplyStyle` 可安全调用。

### 边界（有意保留）

`ChatView` / `CommandView` 的 `CliPanelView` 把配色、字号、圆角固化在构造期（`readonly` 字段 + 一堆子控件），
本轮**没有**重写它，所以改外观时对话卡仍会重建——会丢历史。要彻底解决，需要把 `CliPanelView` 的样式改成
可重新套用（或把对话历史迁到状态文件），属于后续工作。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Ui/OverlayController.cs` | 两种签名 + 三分支决策 |
| `src/Cards/CardSettingsModule.cs` | `ICardStyleAware` 契约 |
| `src/Media/MediaView.cs` | `ApplyStyle(style, cornerRadius)` |
| `src/Cards/MediaCard.cs` | 实现 `ICardStyleAware` |
| `src/SelfTest.cs` | 换肤断言 |

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**。
- 未覆盖：真实换肤的观感与「视频不重启」需要手工确认（当前配置里没有媒体卡）。

---

## 14. v2.8.1（本次提交，未打标签）

### 背景

评审 P2-1：三种毛玻璃实现（系统亚克力 / 桌面亚克力 / 兼容取样）的**选择与生命周期都堆在 `OverlayWindow`**——
`UpdateBackdrop` 里按字符串 if/else，配上一堆 `_systemActive` / `_acrylicActive` / `_lastExclude` 字段，
新增一种材质只能继续往里加分支。

### 改动

1. **`GlassPolicy` 纯决策**（`src/Ui/GlassBackdrops.cs`）：输入「是否开毛玻璃 / 模式 / 两块底板的可用性」，
   输出 `GlassBackdropKind`（None / SystemAcrylic / DesktopAcrylic / CompatCapture）。不碰窗口、不做 IO。
2. **`IGlassBackdrop`**：`IsActive` / `IsUnavailable` / `Apply(window, out reason)` / `Retire(window)`。
3. **两块底板各自成类**：
   - `SystemAcrylicBackdrop`：持有 `GlassBackdrop`（accent 底板窗口），tint / follow / 复用 / 释放；
   - `DesktopAcrylicBackdrop`：持有 `GlassFeed` 的接入、截屏排除节流（原来在窗口里的 `_lastExclude`）、
     不可用标记。
4. **窗口只做接线**：`UpdateBackdrop` 按策略结果先 Retire 不该用的、再 Apply 该用的；
   `BackdropActive`、`UpdateGlassCapture`、`QueueDragFrame`、`ReleaseBackdrop` 改为查询两块底板的状态。
5. **自检**新增「毛玻璃策略=OK」（6 种组合的决策表）。

### 实测

三种模式分别用 `--smoke 4 --smoke-visible` 跑过（改完再恢复原配置）：

| 模式 | 日志 | 结果 |
|---|---|---|
| `Dwm`（桌面亚克力） | `毛玻璃背景已启用（桌面亚克力：截屏 + 模糊后画进卡片，卡片不进截图）` | ✅ 退出码 0 |
| `System`（系统亚克力） | `毛玻璃改用系统亚克力底板` + `毛玻璃背景已启用（系统亚克力底板…）` | ✅ 退出码 0 |
| `Compat`（兼容取样） | `毛玻璃背景已启用（15 帧/秒，模糊 5px）` | ✅ 退出码 0 |

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**（`毛玻璃策略=OK`）。
- 未覆盖：拖动时的补帧观感、切换实现后旧底板不滞留（需要肉眼确认，逻辑与改造前一致）。

---

## 15. v2.9.0（本次提交，未打标签）

### 背景

按 `docs/theme-rewrite-prerequisites.md` 的 P1：外观的颜色令牌没有单一来源——强调色硬编码在 `CardButton`，
面板/描边/次要文字靠「前景色 × alpha」现算，主题重写会把这些散落的常量搬进更大的文件。

### 改动

1. **令牌与解析**（`src/Ui/ThemeTokens.cs`）：`ThemeTokens`（Accent / Danger / Muted / Border / Panel / Scrollbar / ScrollbarHot / MonoFont）
   + `ThemeResolver.Resolve(style)`。**空值按前景色派生**，派生 alpha 刻意等于旧常量（0.45 / 0.10 / 0.045 / 0.18 / 0.40），
   所以默认观感逐像素不变。
2. **配置字段**：`StyleSettings` 新增 8 个字符串令牌；`Accent` / `Danger` 默认 `#2F6FEB` / `#D14343`，其余为空（= 派生）。
   `Settings.Normalize` 用 `Settings.IsColorToken` 校验 `#RRGGBB` / `#AARRGGBB`，非法值回落。
3. **接线**：
   - `CardButton` 的强调 / 危险色改读令牌，删掉硬编码 `ThemeAccent`；
   - `CliScrollBars` 改为接收「常态 / 悬停」两块颜色（来自令牌），缓存键跟着变；
   - `CliPanelView` / `CliEntryPanel` 的内部小面板底色、描边、次要文字改用 `Panel` / `Border` / `Muted`。
4. **未接线（留给 P2）**：`MonoFont` 令牌已经能存，但 `CliPanelView` 的等宽字体仍是静态字段，
   等 P2 的「就地换肤」一起改成实例级。

### 实测

- `dotnet build -c Release`：0 警告 0 错误（输出到临时目录验证；`bin\Release` 当时被你运行中的实例锁住）。
- `--selftest`：**待补**（同一原因：单实例互斥量被占用，跑不了自检）。
- 默认观感不变由「派生值 = 旧 alpha 常量」保证；`Accent` / `Danger` 的默认值与旧硬编码相同。

---

## 16. v2.9.1（本次提交，未打标签）

### 背景

按 `theme-rewrite-prerequisites.md` 的 P2：`CliPanelView` 的配色 / 圆角 / 内边距 / 字号在构造期写死，
换主题会走「销毁 + 重建」——AI 对话的历史和命令行的记录全丢。

### 改动

1. **样式字段可变**：`CliPanelView` 的 `Foreground` / `Corner` / `Pad` / `FontSize` / `Tokens` / `StyleSet` 去掉 `readonly`；
   标题 / 徽标 / 分隔线 / 输入 / 状态 / 记录区这些宿主 Border 存成字段，换肤时能重新套。
2. **`CliPanelView.ApplyStyle(style, cli)`**：重算令牌、字号、内边距、圆角，重新套用标题与徽标颜色、
   分隔线与各 Host 的 Margin、输入框（`CardTextBox.ApplyStyle`）、状态行（保留「是否报错」状态）、
   两个滚动条（`CliScrollBars.Apply` 令牌色）以及记录区高度上限；子类可以通过 `OnApplyStyle()` 追加自己的控件。
3. **`CliEntryPanel.ApplyStyle`**：内部小面板重套底色 / 描边 / 圆角 / 滚动条；
   每段文字在 `AddText` 时把自己的 `alpha` 与字号偏移存进 `Tag`，换肤时按新前景重算颜色与字号。
4. **两张 CLI 卡片实现 `ICardStyleAware`**：`ChatCard` / `CommandCard` 把宿主的「回退重建」切到「就地换肤」。
5. **自检**新增「CLI 换肤=OK」：两张卡片都实现契约；`CliEntryPanel` 换肤后文字颜色 / 字号按新样式重算。

### 边界

- 主题换了「等宽字体」（`MonoFont` 令牌）时，已有文字块的 `FontFamily` 仍指向静态 `CliPanelView.MonoFont`，
  本轮没有把它改成实例级——换字体仍需要重建卡片。颜色 / 字号 / 圆角 / 内边距都已经能就地换。
- 记录区已有的文字块不会重排布局（只改字号与颜色），极端字号下记录面板高度可能略有出入，滚动条仍可用。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**（`CLI 换肤=OK`）。
- `--smoke 4 --smoke-visible`：退出码 0，对话卡正常渲染（走 `CliPanelView` 新令牌路径）。
- 未覆盖：真实换肤的观感（把 Accent / Padding / FontSize 改大再应用，确认对话历史仍在）需要手工确认。

---

## 17. v2.9.2（本次提交，本地标签 `v2.9.2`）

### 背景

按 `theme-rewrite-prerequisites.md` 的 P3：外观只有 `settings.json` 里的一组字段，没有「存成文件 / 分享 / 回滚」的能力。
`docs/themes/` 下的三份 `.mptheme` 只是草案，没有解析器——导入、导出、坏文件回滚都无从谈起。

### 改动

1. **新增 `src/Core/ThemeFile.cs`**（约 670 行）：`ThemeDocument` + `ThemeFile`。
   格式对齐 `docs/theme-system.md` §3：`schema`（接受 `formatVersion` 别名，缺省按 1）+
   `name / author / description` + `appearance{colors / typography / shape / outline / cliPanel}`。
2. **解析与校验**：用仓库自带的 `Json.Parse`（不引新依赖）；未知键忽略；颜色必须是 `#RRGGBB` / `#AARRGGBB`；
   数值范围与 `Settings.Normalize` 对齐（字号 6~96、圆角 0~60、内边距 0~80、透明度 0~1、CLI 尺寸与条数等）；
   错误信息带 JSON 路径（如 `appearance.colors.muted 透明度必须在 0 ~ 1 之间`）；高版本 schema 直接拒绝。
3. **软色两种写法**：数字 0~1 = 按**前景色**派生 alpha（兼容三份示例主题的 `"muted": 0.45`）；
   字符串 = 直接写颜色（浅色主题可以让描边 / 滑块偏离前景色）。`ApplyTo` 先落前景色再派生软色。
4. **导入 = 套用到副本**：`TryImport(path, source, out applied, out error)` 先克隆 `Settings`，
   只覆盖主题里出现过的外观字段，最后 `Normalize()`；任何一步失败都返回原因、`applied` 为 null，源设置原样不动。
   克隆失败直接报错，**不**退回 `new Settings()`（避免「一次异常 = 全默认值」）。
5. **导出**：`Export(settings, name)` 字段顺序固定、空软色不写（继续按前景派生），便于 diff；
   `TryExport` 以 UTF-8 无 BOM 落盘。
6. **自检**新增两行：「主题文件」验证导出 → 解析 → 套用 → 再导出一致、数字透明度按前景派生、
   并自动解析 `docs/themes/*.mptheme`；「主题文件校验」验证高版本 / 坏颜色 / 越界 / 缺 `appearance` / 坏 JSON 均被拒绝，
   坏文件不碰当前设置、好文件导入也不改源对象。

### 边界

- **没有 UI**：导入 / 导出按钮、主题列表与预览属于 P4（2.9.3），本轮只提供可被 UI 调用的读写层。
- 不包含背景图 / 字体资源包；ZIP + `assets/` 相对路径校验按 `theme-system.md` §7 属于第 2 阶段。
- 导出时空的软色令牌不写进文件，所以把 A 主题（派生软色）导进一个已经显式设过软色的配置时，
  那些软色会保留目标配置的值（增量语义；文档 §5.4 已注明）。
- `.mptheme` 的 `schema` 与 `settings.json` 的 `SchemaVersion` 无关，配置仍是 schema 6。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**（`主题文件=OK`、`主题文件校验=OK`；三份示例主题全部解析通过）。
- `--smoke 4 --smoke-visible`：退出码 0。

---

## 18. v2.9.3（本次提交，本地标签 `v2.9.3`）

### 背景

三件事一起收尾：外观重写最后一块 UI（P4：设置页主题入口 + 预览），以及评审表里两个应尽快修的项——
**#7 设置副本克隆失败会静默重置**（数据丢失风险）与 **#4 宿主直接调具体卡**（做画板 / 中转站前必须解耦）。

### 改动

1. **内置主题编进程序集**（`src/Core/ThemeCatalog.cs`）：`docs/themes/*.mptheme` 作为 `EmbeddedResource`，
   发布版不再依赖 `docs` 目录；`ThemeCatalog.Load()` 解析出名称 / 描述 / `ThemeDocument`。
2. **外观页顶部「主题」区**：内置主题下拉 + 导入 / 导出 / 还原默认 + 右侧缩略预览。
   选中、导入、还原都只改 `_working` 副本，点「保存并应用」才落盘；取消 / 关闭则完全不变。
3. **离屏预览**：用 `OverlaySurface` 渲染一张 280 DIP 的样张（2× 位图），颜色 / 字体 / 圆角 / 投影 / 内边距都跟着主题走；
   手动拖外观滑块时同步刷新。
4. **`Style.ThemeName`**：应用 / 导入主题时写入；手动改外观自动清空并显示「自定义」，避免显示成某个主题却已经不一致。
   字段新增不改 schema（仍是 6）。
5. **#7 克隆防护**：`SettingsWindow.TryClone` 失败返回 `null` + 原因并写日志；
   加载失败时设置窗口不打开、保存被禁用；保存后刷新副本失败也保留旧副本，绝不用 `new Settings()` 顶替。
6. **#4 激活契约**：新增 `ICardActivation`（`OnActivated` 返回日志摘要、`OnDeactivated` 收起回调）；
   `CommandCard` 自己解释工具 / 目录，`OverlayController` 删除 `CommandOf` 与对 `CommandSettings` 的引用。

### 边界

- 主题列表只含三套内置主题 + 文件导入，没有主题目录扫描与 ZIP 资源包（仍属 `theme-system.md` §7 的第 2、3 阶段）。
- `MonoFont` 仍不是实例级字体：换等宽字体仍需重建卡片（P2 遗留）。
- 「还原默认」只重置外观三块（`Style` / 圆角与投影 / `Cli`），不动卡片、热键与位置。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**；新增「内置主题」「卡片激活」「设置副本」三行均 OK。
- `--smoke 4 --smoke-visible`：退出码 0。
- `--screenshot-settings`：外观页主题区布局正常，缩略预览按当前外观渲染（无预览异常日志）。

---

## 19. v2.9.4（本次提交，本地标签 `v2.9.4`）

### 背景

两处“改了外观但卡片不变”：
1. **AI 对话卡**的外观不随外观设置变化（圆角、面板宽度、部分颜色、等宽字体仍是旧值）；
2. **常驻媒体卡**在保存后外观完全不变（底色、背景不透明度、圆角、投影）。

### 根因

1. `CliPanelView` 在构造期把 `Settings` 与 `CliSettings` 存成 `readonly`（`HostSettings` / `Cli`），
   `ApplyStyle` 只接收 `StyleSettings` + `CliSettings`，圆角仍读 `HostSettings.Overlay.CornerRadius`；
   保存设置时宿主会换成**新的 `Settings` 对象**，面板继续指向旧对象，圆角 / CLI 高度上限 / 宽度都是旧值。
   另外 `ApplyStyle` 从未更新 `Width`，改 CLI 面板宽度也不会生效。
2. 等宽字体是 `static readonly CliPanelView.MonoFont`，主题里的 `monoFont` 只是存下来、从未接线——
   导入浅色 / 终端主题后 CLI 面板字体不会变。
3. 宿主“只换样式”的分支只调了 `slot.Window.RefreshSettings(dialog)`——它只换 `_settings` 引用、更新穿透与菜单，
   **没有重新 `Apply` 卡片底层的 `OverlaySurface`**。富内容卡的底色 / 背景不透明度 / 圆角 / 投影
   都是创建时那次 `Surface.Apply` 画的，所以媒体卡（以及对话卡的外层）一直保留旧外观。

### 改动

1. **`CliPanelView.ApplyStyle(Settings)`**：整份设置传进来，更新 `HostSettings` / `StyleSet` / `Cli` /
   `Corner` / `Width` / `MonoFamily`，再套 chrome、每条记录、输入框与滚动条；`ChatCard` / `CommandCard` 调用点同步。
2. **等宽字体实例化**：`MonoFamily` 从 `ThemeTokens.MonoFont` 取，构造与换肤时都更新，
   标题 / 徽标 / 提示符 / 输入框 / 状态行 / 已有记录文字一起换（`CliEntryPanel` 自己保存 `_mono`）。
3. **`OverlayWindow.RefreshSurface(Settings)`** + **`OverlaySurface.Text`**：只重画卡片底层，文字与富内容原样保留。
4. **宿主换肤分支**：`RefreshSettings` 之后调用 `RefreshSurface`，再调模块 `ApplyStyle`。
5. **自检「换肤跟随设置」**：验证 `ApplyStyle` 后绑定的是新 `Settings` 对象、圆角 / 宽度 / monoFont 跟着变；
   以及 `OverlaySurface` 重画后文字保留、圆角更新。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：**失败项 0**（新增「换肤跟随设置」OK）。
- `--smoke 4 --smoke-visible`：退出码 0。
- 未覆盖：真实窗口里改字号 / 圆角 / 底色后，对话与媒体卡的观感需要手工确认；
  自检已覆盖“换肤跟随新设置对象”与“底层重画保留内容”两个根因点。

---

## 20. v2.9.5（本次提交，本地标签 `v2.9.5`）

### 背景

`docs/legacy-audit.md` 的 S1 + S2：颜色字符串在 7 个地方各解析一遍，而且“非法输入”分别返回
白 / 前景色 / false；`CardButton.TryColor` 之类写在 `catch` 里的 fallback 因为底层吞异常而**永远不可达**，
设置页把前景色改成 `nope` 时预览会静默变白。

### 改动

1. **新增 `src/Core/ColorCode.cs`**：唯一解析入口，只认 `#RRGGBB` / `#AARRGGBB`，返回 `bool + ARGB`；
   `Settings.IsColorToken` 也改为委托它。
2. **`OverlaySurface.ParseColor(hex, fallback)`**：默认重载仍回白色（取色预览要能画），带 fallback 的重载供业务用。
3. **删掉 6 处复制**：`CardButton.TryColor`、`CardTextBox.TryColor`、`ButtonsCard.TryColor`、
   `MediaView.ParseColor`、`ThemeResolver.Parse`、`ThemeFile.TryParseColor`；
   `CardPalette.TryColor` 保留 API 但只做委托。
4. **自检**：`ColorCode` 的 6/8 位解析、`#12345` 拒绝；非法前景色下 `Muted` 仍按默认前景派生，
   而不是变成白色；「外观令牌」结果行注明“非法值回落而不是变白”。

**行为变化**：`MediaView` 不再接受颜色名（`ColorConverter` 的 `Red` / `Gray`）；配置与设置页只允许十六进制，
所以正常路径不受影响，非法值统一回落到各调用方给的 fallback。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「外观令牌」带新的非法色断言）。
- `--smoke 4 --smoke-visible`：退出码 0。

---

## 21. v2.9.6（本次提交，本地标签 `v2.9.6`）

### 背景

`docs/legacy-audit.md` 的 S3 + S4：`SettingsWindow._live` 只赋值、从不读；
`ChatView.Send` 与 `CommandView.RunCommand` 是 `async void`，`try` 只包住 `await` 段，
之前的 `AddEntry` / `SetStatus` / `_spin.Start` 等 UI 调用一旦抛异常，会从 `async void` 逸出到 Dispatcher，
默认行为是结束进程。

### 改动

1. **删掉 `_live`**（`SettingsWindow`）：构造函数里的赋值一起删，减少一个“看起来像同步源”的死字段。
2. **`ChatView.Send` / `CommandView.RunCommand` 拆成两层**：
   - `async void Send() / RunCommand()` 只负责 `try { await SendCore(); } catch (Exception ex) { ... }`；
   - 原来的方法体（含既有的 `try/finally`）原样搬进 `SendCore() / RunCommandCore()`。
3. **兜底行为**：
   - 对话：重置 `_busy`、停转圈、释放 `_pending`、状态行显示错误、写日志；
   - 命令行：重置 `_running`、取消并释放进行中的命令、恢复输入框、状态行显示错误、写日志。

这样即使异常发生在“准备阶段”，界面也会回到可再次提交的状态，而不是静默崩掉。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0。
- `--smoke 4 --smoke-visible`：退出码 0。

---

## 22. v2.9.7（本次提交，本地标签 `v2.9.7`）

### 背景

`docs/legacy-audit.md` 的 S7 + S12：

- 对话卡的历史上限写死 12 条，记录区却按 `Cli.MaxEntries` 保留——把条数调到 30 后，界面有 30 块、
  发给模型的只有 12 条，用户看不出这个隐式上限；
- `FindCommandCard` 取第一张 `IsInteractive` 卡，第二张会被静默忽略，没有任何提示。

### 改动

1. **`ChatView.ContextMessageLimit(maxEntries)`**：每条记录（一轮问答）算两条消息，上限 = `MaxEntries × 2`，
   至少 2 条；`TrimHistory` 改用它。默认 `MaxEntries = 12` 时上下文从 6 轮变成 12 轮，与界面保留的
   记录数一致（长对话的 token 消耗会相应增加，调小「CLI 面板最多保留」即可收紧）。
2. **`CardModel.CountInteractive`**：统一的“交互卡计数”；`Normalize` 发现多于一张时写警告日志。
3. **设置页拦住第二张**：添加卡片的菜单里，已有交互卡时把交互模块置灰并给出提示；
   `AddCard` 与 `ValidateSettings` 都会返回「只能有一张交互卡片（键盘独占）」并列出名字。
4. **自检**：「对话上下文」验证 0/1/3/30 条设置下的上限；「交互卡唯一」验证默认配置正好一张、
   再加一张命令行会数出两张。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「对话上下文」「交互卡唯一」均 OK）。
- `--smoke 4 --smoke-visible`：退出码 0。

---

## 23. v2.10.0（本次提交，本地标签 `v2.10.0`）

### 背景

为文件中转站、剪贴板历史、待办这类“多条目卡片”准备共享控件。此前只有按钮 / 输入框 / CLI 面板，
列表、空状态、状态行、进度与转圈每张卡都得重写一遍，而且很容易各写一套颜色。

本版先做两个地基：`CardList` 与 `CardStatus`。

### 改动

1. **`src/Ui/Controls/CardList.cs`**：
   - 基于 `ListBox`，开启 `VirtualizingStackPanel` 的回收虚拟化；
   - 行模板换成自定义 `ControlTemplate`（圆角、无系统白框），悬停 / 选中由主题令牌上色；
   - 空状态（细边框 + 居中提示）、双击 / 回车激活、`SelectedItem/Index`、`ScrollToEnd`；
   - `ApplyStyle(style, cli)` 重套字体 / 颜色 / 行高 / 滚动条；行内容由 `ItemTemplate` 或直接放控件决定。
2. **`src/Ui/Controls/CardStatus.cs`**：图标 + 文字 + 可选进度条；
   `SetText(text, kind)` / `SetBusy(bool)` / `SetProgress(0..1)`，错误态用 `Danger` 令牌，忙碌态带 braille 转圈。
3. **自检**新增「共享控件」：列表空态 / 计数 / 选中；状态行忙碌 / 进度 / 错误态。
4. **`--screenshot-widgets <png>`**：离屏渲染一张控件预览（列表 / 空态 / 进度 / 错误），用于目检。

### 边界

- 列表要虚拟化时请给 `CardList` 设固定 / 最大高度（放在无高度约束的 `StackPanel` 里会退化为全量测量）。
- 行内拖出、右键菜单、多选模板尚未封装；需要时用 `CardList.Inner`（底层 `ListBox`）接。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「共享控件」OK）。
- `--screenshot-widgets`：选中行为主题色圆角、空状态有细边框、错误态红字，观感正常。

---

## 24. v2.10.1（本次提交，本地标签 `v2.10.1`）

### 背景

第一梯队剩下四块：标题 / 工具栏、输入框、拖入高亮，以及让高亮真正可用的“拖入 / 拖出”宿主回调。

### 改动

1. **`CardHeader` + `CardToolbar`**：标题 + 徽标 + 右侧动作按钮；按钮统一间距与换肤，
   文字按钮即可（需要图标时调用方自己换 `Content`）。
2. **`CardInput`**：占位符、单行回车提交 / 多行 Ctrl+Enter 提交、错误描边；
   底子仍是 `CardTextBox`，提交事件返回原始文本。
3. **`CardDropOverlay`**：拖入时的覆盖层；底色是“卡片底色 + 强调色 22%”的**不透明**混合，
   避免透出底下的空状态文字；支持接受 / 拒绝两种文案与颜色。
4. **`ICardDropFeedback` + 宿主接线**：`OverlayWindow.EnableDrop` 新增可选的拖入状态回调，
   `DragEnter` / `DragLeave` / `Drop` 各通知一次；`OverlayController.WireInputCapabilities` 把它转给模块。
   卡片实现该接口后即可 `overlay.Show(n) / Hide()`。
5. **自检**新增「共享控件（交互）」：Header 按钮可点、Input 提交、Overlay 显隐；
   `--screenshot-widgets` 的预览扩展到标题栏 / 输入框 / 拖入高亮。

### 边界

- 反馈只覆盖“进入 / 离开 / 放下”三次状态，不提供每次 `DragOver` 的数据；需要计数由卡片自己在 `CanDrop` 里缓存。
- 覆盖层是控件，放哪里、盖多大由卡片自己决定（通常铺满列表区域）。
- 拖出（`DoDragDrop`）仍未封装；需要时先做“行内拖动 vs 整卡拖动”的 spike。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「共享控件」「共享控件（交互）」均 OK）。
- `--screenshot-widgets`：标题栏 / 按钮 / 输入框 / 拖入覆盖层显示正常，覆盖层不再叠字。

---

## 25. v2.10.2（本次提交，本地标签 `v2.10.2`）

### 背景

第一梯队控件已经就绪，但内容控件拿不到 `ICardHost`（`RequestLayout` 只能靠窗口尺寸自动触发）；
剪贴板与“复制大文件”这类后台任务也还是每张卡各写一套。本版补三件基础设施。

### 改动

1. **`CardRenderContext.Host`**：新增可空 `ICardHost Host` 与三参数构造重载；
   宿主在 `EnsureContent`、`RenderCardText`、`SyncCards` 构造上下文时传 `slot.Host`。
   内容控件可以保存它来 `RequestLayout` / `RequestRender` / 读写状态目录；文字渲染与截图路径为 null。
   模块本身仍然不存 host（单例约束不变）。
2. **`src/Ui/ClipboardService.cs`**：文本 / 文件列表 / 图片的读写，统一重试
   `CLIPBRD_E_CANT_OPEN`（被别的程序占用），图片会 Freeze。同步方法要求 UI 线程（STA）。
3. **`src/Core/CardJobQueue.cs`**：`CardJobQueue`（默认 1 并发，串行）+ `CardJob`
   （Pending / Running / Succeeded / Failed / Canceled、`IProgress<double>`、`Task`、取消）。
   队列线程安全，可在任意线程 `Enqueue` / `CancelAll`。
4. **自检**：「渲染上下文」验证 host 透传；「后台任务」验证成功 / 失败 / 取消三种结局。

### 边界

- 剪贴板没有写自检（不想覆盖你正在用的剪贴板内容）；失败会写日志并返回 false。
- `CardJob` 的状态/进度线程安全，但不负责把事件切回 UI 线程；调用方用 `Task` 或自己 `Dispatcher`。
- 图片剪贴板只读不写（`TryGetImage`），写图片等有实际需求再加。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「渲染上下文」「后台任务」均 OK）。
- `--smoke 4 --smoke-visible`：退出码 0。

---

## 26. v2.10.3（本次提交，本地标签 `v2.10.3`）

### 背景

第二梯队剩下平台相关的两块：文件图标 / 缩略图，以及“从卡片拖出到资源管理器”的最小助手。
两者都容易各写一套 P/Invoke 与鼠标阈值，收进共享层。

### 改动

1. **`src/Ui/ShellIcons.cs`**：
   - 图标：`SHGetFileInfo` 默认走 `SHGFI_USEFILEATTRIBUTES`（按扩展名，不碰磁盘 / 网络盘），
     按“扩展名 + 尺寸”缓存；需要 exe / 快捷方式专属图标时传 `extensionOnly: false`。
   - 缩略图：`IShellItemImageFactory`，在**常驻 STA 线程**上执行，网络盘 / 慢文件不会卡 UI；
     `Task<ImageSource>` 返回，失败 / 无缩略图返回 null。
   - 图标 / 缩略图缓存都有上限，`ClearCache()` 可整体失效。
2. **`src/Ui/DragSource.cs`**：`ShouldStart(press, current)` 阈值判断 +
   `BeginFiles` / `BeginText`（OLE `DoDragDrop`，必须在 UI 线程；阻塞到拖放结束）。
3. **预览图**：`--screenshot-widgets` 的列表行接入真实 Shell 图标（zip / png / txt / mp4）。
4. **自检**新增「Shell / 拖出」：图标缓存一致、缩略图请求不挂、拖出阈值与空输入。

### 边界

- 图标默认按扩展名，和资源管理器里 exe 的自定义图标可能不同；需要时用 `extensionOnly: false`（会碰磁盘）。
- 缩略图缓存键是“路径 + 尺寸”，同一路径换文件后可能显示旧图，调 `ShellIcons.ClearCache()`。
- 拖出只提供 `DoDragDrop` 本身；与整卡拖动的冲突仍未解决（见 `file-transfer-station.md` §7.2 的 spike）。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「Shell / 拖出」OK）。
- `--screenshot-widgets`：四行文件图标正常显示。

---

## 27. v2.11.0（`ecf4984`，本地标签 `v2.11.0`）

### 背景

`docs/file-transfer-station.md` 的 M1：把文件从资源管理器拖进卡片、暂存成列表，支持引用与复制两种收纳方式。
前置（拖放通道、每卡状态、共享控件、Host 上下文、后台任务、Shell 图标）都已经在 v2.10.x 补齐。

### 改动

1. **`src/Cards/DropStationCard.cs`**（单文件）：模块 + `StationSettings` + `StationView` + `StationEditor`。
   - 引用模式只记路径；复制模式写入接收目录，重名自动 `名字 (2).扩展名`；
   - 列表用 `CardList`：扩展名图标、名称 / 大小、丢失标记、双击打开、右键菜单；
   - 打开 / 在资源管理器中显示 / 复制路径 / 从列表移除 / 删除接收目录内的文件（二次确认）；
   - 每卡 `items.json`（`ICardHost` / `CardStateStore`），重启恢复并标记丢失文件；
   - 复制走 `CardJobQueue` 串行后台执行，失败 / 取消删除半成品；状态行走 `CardStatus` 进度；
   - 设置区块：模式 / 接收目录（文件夹选择）/ 最多保留；`Validate` 拦住“复制但没有目录”。
2. **`ICardDropFeedback` 增加 `content` 参数**：模块需要拿到当前卡片内容控件才能显示拖入高亮。
3. **自检**：「文件中转站」覆盖模块能力、设置归一化 / 校验、拖放判定，以及临时文件拖入 / 去重 / 缺失拒绝。
4. **`--screenshot-widgets`** 追加一张真实的中转站空状态卡片，用于目检布局。

### 边界

- 拖出、移动模式、文件夹递归、剪贴板添加、缩略图、自动清理、搜索 / 多选都还没有（M2 / M3）；
- 复制队列不持久化：重启后未完成的复制不会续传；
- 锁定（点击穿透）时收不到拖放，这是既有语义，卡片会保持“把文件拖进来”的提示；
- Reference 模式默认，不产生任何文件操作；Copy 模式要求目录已存在（设置页选择文件夹会创建）。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「文件中转站」OK）。
- `--smoke 4 --smoke-visible`：退出码 0。
- `--screenshot-widgets`：中转站空状态渲染正常（标题 / 计数 / 空列表 / 状态行）。

---

## 28. v2.11.1（`1f7e7f6`，本地标签 `v2.11.1`）

### 背景

2.11.0 的中转站标题行用了 `CardButton` 的实心样式：`清空` 是饱和红底 + 粗体，`打开目录` 是灰底胶囊，放在细描边、低对比的卡片里像另一套 UI；而 `清空` 只清列表、不删文件，本来也不该用危险红。

### 改动

1. **共享按钮 `CardButton` 改成幽灵描边样式**：透明底 + 1 px 前景色描边 + 0.84 明度文字；主操作 / 危险操作不再实心填充，只把描边和文字染成强调色 / 拒绝色；悬停 / 按下叠一层很淡的色调，键盘焦点换强调色描边，禁用时整块（文字 + 描边）降到 0.38。
2. **尺寸收紧**：内边距 10×3、最小高度 22、取消 64 px 最小宽度、字号跟随卡片小一号、字重回到 Normal，不再比标题更抢眼。
3. **中转站**：`清空` 改为普通语气；列表为空时禁用；标题行补 6 px 下边距，按钮不再贴住列表顶边。
4. **自检**：新增“按钮文字必须使用按钮自己的前景色”，防止模板继承再次把字画丢。
5. **文档**：`docs/cards-architecture.md` 的按钮示例与说明同步。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「共享控件（交互）」OK）。
- `--smoke 4 --smoke-visible`：退出码 0。
- `--screenshot-widgets` / `--screenshot-cards`：细描边按钮、禁用态、与列表间距渲染正常。

---

## 29. v2.12.0（`fded63a`，本地标签 `v2.12.0`）

### 背景

M1 只是“把文件拖进来、存住”。M2 补齐日常中转真正会用的动作：从列表拖回资源管理器、从剪贴板收文件、复制时能看到进度并取消、把“收纳”升级成“移动”，以及清理已经丢失的条目。同时按反馈把按钮与列表的文字留白对齐 AI 对话小面板。

### 改动

1. **行内拖出**：行上 `PreviewMouseLeftButtonDown` 手动选中并标记 `Handled`，窗口的整卡拖动因此不会抢走事件；移动超过 6 px 才 `DoDragDrop`（只允许 Copy），拖出后不移除列表项。双击打开改为按 `ClickCount` 自己处理，右键先把命中行选中。
2. **剪贴板**：标题行新增「粘贴」，用 `ClipboardService.TryGetFiles` 读取资源管理器复制进来的文件；没有文件只提示，不弹窗。
3. **复制进度 / 速度 / 取消**：状态行显示 `当前/总数 · 文件名 百分比 · 速度`，每 120 ms 刷新一次；新增「取消」按钮取消排队与进行中任务；失败 / 取消删除半成品，源文件保留。`CardStatus` 重复收到 Busy 文字时不再重置转圈帧。
4. **移动模式**：设置新增「移动到接收目录」，先复制、成功后再删源；跨盘统一走复制 + 删除，保证进度与取消；删源失败保留目标文件并给警告，不回滚。摘要与校验同步支持 Move。
5. **丢失清理**：列表右键新增「移除 N 个丢失项」，只清列表，不删文件。
6. **外观**：`CardButton` 内边距 12×6、最小高度 28，`CardList` 行内边距 8×6，标题行下边距 8，和 AI 对话面板的 8 / 6 留白一致。
7. **自检**：新增移动模式归一化 / 校验 / 摘要、冲突命名、真实 Copy / Move 临时文件、丢失项清理；这两条复制链路会在自检里跑一次 WPF 消息泵等待完成。

### 边界

- 文件夹递归、缩略图、自动清理、搜索 / 过滤、多选、复制队列持久化还没做；
- 拖放效果仍固定 Copy，Shift / Ctrl 的 Move 语义需要宿主契约从 `bool` 扩成 `DragDropEffects`；
- 复制过程中对单条 pending 项执行删除 / 清空，后台任务与列表的一致性还需收口。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「文件中转站」OK；真实 Copy / Move / 丢失清理均通过）。
- `--smoke 4 --smoke-visible`：退出码 0。
- `--screenshot-cards` / `--screenshot-widgets`：三按钮标题行、28 px 按钮、进度 / 速度状态行渲染正常，无溢出。

---

## 30. v2.12.1（`aead3aa`，本地标签 `v2.12.1`）

### 背景

M2 之后，中转站能收、能拖出、能移动，但列表一长就只能靠眼睛找；多个文件也只能一个个处理。M3 先做“列表管理”这一组：搜索过滤、多选与批量操作、加载时自动清理丢失项，同时把 `CardInput` 的实时文本变化和 `CardList` 的扩展选择能力补进共享控件。

### 改动

1. **搜索 / 过滤**：列表上方新增 `CardInput`，按文件名 / 路径实时过滤；Esc 清空；徽标显示 `可见/总数`；无匹配时显示「没有匹配的文件。」；列表最大高度预留搜索行。
2. **多选**：`CardList` 增加 `SelectionMode` / `SelectedItems`，中转站切到 `Extended`。Ctrl 点选、Shift 按过滤后的可见列表连选；右键点在已选行上保留多选，点未选行只选该行；`Rebuild` 重建列表时保留选中项。
3. **批量操作**：复制选中路径（多行）、移除选中项、删除选中文件（只允许接收目录内，一次确认）、多文件拖出；右键菜单标题显示选中数量。
4. **自动清理 / 图标**：设置新增「加载列表时移除丢失项（只清列表）」与「显示文件图标」；自动清理只在加载状态时执行，不碰磁盘。
5. **共享控件**：`CardInput.TextChanged` 支持实时搜索；`CardList.SelectionMode` / `SelectedItems` 供其他卡片复用。
6. **自检**：搜索 `alpha` / `beta` 过滤到 1 条、徽标 `1/2`，无匹配 `0/2`，清空恢复 2 条；自动清理丢失项后保留存在项；原有 Copy / Move / 冲突命名 / 手动清理继续覆盖。

### 边界

- 缩略图、文件夹递归、每卡热键、复制队列持久化还没做；
- 拖放效果仍固定 Copy，Shift / Ctrl 的 Move 语义需要宿主契约从 `bool` 扩成 `DragDropEffects`；
- 复制过程中对单条 pending 项执行删除 / 清空，后台任务与列表的一致性还需收口。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（「文件中转站」OK；搜索 / 自动清理 / Copy / Move 均通过）。
- `--smoke 4 --smoke-visible`：退出码 0。
- `--screenshot-cards` / `--screenshot-widgets`：中转站空态与共享控件渲染正常。

---

## 31. v2.12.2（`78ed0a1`，本地标签 `v2.12.2`）

### 背景

M2 时文件夹仍被拒绝；M3 把列表管理补上后，文件夹成为最后一个明显的阻塞点。v2.12.2 把文件夹作为一条列表项接入现有引用 / 复制 / 移动、拖出、删除和多选模型，并补一份运行时扩展卡片的调研文档。

### 改动

1. **目录项模型**：`StationItem.IsDirectory` 持久化；`PathExists()` 统一判断文件 / 目录；打开、定位、拖出、缺失检查、复制路径都按目录处理。
2. **递归复制**：文件夹生成一条复制计划（枚举文件 + 目录，跳过重解析点），先建目标目录树（含空目录），再按字节进度复制；取消 / 失败递归删除半成品。目标目录名冲突自动 `Folder (2)`。
3. **递归移动**：复制成功后 `Directory.Delete(source, true)`；删源失败保留目标并警告。接收目录位于源文件夹内部时拒绝，避免自我嵌套。
4. **删除 / 右键**：接收目录内的文件夹可连同内容删除，二次确认；菜单文案区分文件 / 文件夹。
5. **文案**：拖入覆盖层从「N 个文件」改成「N 项」；空状态改为「文件 / 文件夹」。
6. **自检**：带子目录 / 空目录的 Copy、Move，Reference 目录项，文件夹名冲突 `My.Folder (2)`。
7. **调研文档**：新增 `docs/runtime-card-extensions.md`，审计单程序集 / 静态 Registry / 未知 Kind 回退 / `cards` 状态目录冲突，给出 ALC、契约边界、manifest、卸载与安全的分阶段方案。

### 边界

- 符号链接 / junction 跳过，不跟随；不做文件夹缩略图；
- 复制队列仍不持久化，重启后未完成的文件夹复制不会续传；
- 复制中删除 pending 条目的后台一致性仍未收口；
- 运行时插件只做了调研，没有实现加载器。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0（新增文件夹 Copy / Move / Reference / 目录名冲突；原 Copy / Move / 搜索 / 清理继续通过）。
- `--smoke 4 --smoke-visible`：退出码 0。
- `--screenshot-cards` / `--screenshot-widgets`：空态与共享控件渲染正常。

---

## 32. v2.13.0（`4956352`，本地标签 `v2.13.0`）

### 背景

`OverlayController` 是评审里的“上帝类”：1772 行里同时有卡片模型、堆叠布局、启动动画、热键、托盘、命令行和玻璃。版本策略从本次起固定为 **2.13.x**，在明确要求之前不升 2.14；2.13.0 先做不改变行为的第一步拆分。

### 改动

1. **`CardSlot` / `CardHost` 独立**：移到 `src/Ui/CardSlot.cs`，控制器的嵌套类型定义清掉，布局 / 动画可以直接使用卡片运行时模型。
2. **`CardLayoutCoordinator`**：接管 `Layout` / `Schedule` / `LayoutIfNeeded` / `SyncHeight` / 左右对齐；控制器只决定何时调用。
3. **`OverlayIntro`**：接管欢迎语 / 内容逐字动画、停留 / 擦除、计时器与临时卡片状态；控制器只调用 `Begin` / `Reset` / `Finish` / `IsRunning` / `IsCard`。
4. **控制器瘦身**：`OverlayController.cs` 从 1772 行降到 1434 行；`OverlayController` 仍负责生命周期编排和其余子系统。
5. **行为不变**：没有改设置、配置、卡片协议或公开 API；只移动职责。

### 边界

- 控制器仍是“上帝类”，下一步继续按 2.13.x 拆 `SyncCards` / `ApplySettings` / 热键 / 命令行 / 托盘；
- `SettingsWindow` 与 `CliUi` 本轮不动；
- 没有做运行时插件加载器（仍只有调研文档）。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0。
- `--smoke 4 --smoke-visible`：退出码 0；`--smoke 10` 日志出现「启动动画结束」。
- 行数：`OverlayController` 1772 → 1434；`CardSlot.cs` 94、`CardLayoutCoordinator.cs` 145、`OverlayIntro.cs` 225。

---

## 33. v2.13.1（`5eb0bea`，本地标签 `v2.13.1`）

### 背景

评审报告把 `OverlayController` 的拆分建议列为 `CardStackHost` / `RenderScheduler` / `IntroAnimator` / `HotkeyRouter` / `TrayController` / `CommandModeController`。2.13.0 已完成卡片状态、布局和启动动画；2.13.1 继续拆出边界最清晰的 `HotkeyRouter`。

### 改动

1. **新增 `src/Ui/HotkeyRouter.cs`**：全局热键、卡片呼出热键、卡片通用动作热键的注册 / 回退 / 分发全部移入；占用时自动 `Ctrl+Alt+` 回退并写回实际组合键。
2. **`IHotkeyHost` + 私有适配器**：控制器只暴露动作（显示 / 设置 / 锁 / 找卡 / 呼卡 / 保存 / 刷新设置窗 / 托盘气泡），不把内部 `CardSlot` 抬成公开契约。
3. **控制器调用点收口**：启动时创建路由器并 `Register`；应用设置后重新 `Register`；释放时先退订再释放 `HotkeyManager`。
4. **控制器瘦身**：`OverlayController.cs` 从 1434 行降到 1253 行；热键路由器 265 行。
5. **行为不变**：热键组合、回退策略、卡片动作转发、托盘提示都与拆分前一致。

### 边界

- `OverlayController` 仍是“上帝类”（1253 行），下一步继续按 2.13.x 拆 `SyncCards` / `ApplySettings` / 命令行 / 托盘；
- `SettingsWindow`、`CliUi`、`DropStationCard`、`SelfTest` 本轮不动；
- 运行时插件仍只有调研文档，没有加载器。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0；热键自检（F9/F10/F8）正常。
- `--smoke 10 --smoke-visible`：退出码 0；日志显示 F8/F9/F10/F11、Ctrl+Alt+Shift+M、Ctrl+Delete 注册成功，启动动画结束、退出正常。

---

## 34. v2.13.2（`44670f4`，本地标签 `v2.13.2`）

### 背景

评审报告建议把 `OverlayController` 拆成 `CardStackHost` / `RenderScheduler` / `IntroAnimator` / `HotkeyRouter` / `TrayController` / `CommandModeController`。2.13.0 完成状态、布局、动画，2.13.1 完成热键；2.13.2 继续把托盘和命令行会话抽出去。

### 改动

1. **`TrayController`**：托盘创建、菜单事件、可见性 / 命令热键同步、位置日志、启动气泡、托盘不可用时打开设置的兜底入口全部移入；`TrayHostAdapter` 只暴露状态与动作。
2. **`CommandModeController`**：交互卡的进入 / 退出、激活回调、`ICliFocusTarget` 收尾、键盘抑制、命令行打开 / 关闭日志移入；控制器只提供显示 / 隐藏 / 重绘 / 键盘抑制。
3. **控制器调用点收口**：`ToggleCommandLine` / `ExitCommandLine` 变成薄包装；`RefreshVisibility` 用 `_command.IsActive` / `_command.Card`；卡片被删时 `_command.Reset()`。
4. **控制器瘦身**：`OverlayController.cs` 从 1253 行降到 1162 行；`TrayController.cs` 111 行、`CommandModeController.cs` 155 行。
5. **行为不变**：托盘菜单、启动气泡、命令行 F9 呼出 / 收起、键盘抑制与日志都保持原样。

### 边界

- `OverlayController` 仍是“上帝类”（1162 行），剩余主要是 `SyncCards` / `ApplySettings` 卡片同步；
- `SettingsWindow`、`CliUi`、`DropStationCard`、`SelfTest` 本轮不动；
- 运行时插件仍只有调研文档。

### 实测

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0；热键 F9/F10/F8 正常。
- `--smoke 10 --smoke-visible`：退出码 0；托盘显示、热键注册、启动动画结束正常。
- 15 秒可见运行中自动按两次 F9：日志出现「快捷命令行已打开（卡片=命令行…）」「命令行模式：键盘焦点在面板输入行=True」「快捷命令行已关闭，命令与返回已清空」。

---

## 35. v2.13.3（本次提交，本地标签 `v2.13.3`）

### 背景

`MirrorpondHUD-2.12.2-review.md` 列出 10 条 P1：配置 null / 损坏恢复 / 原子写 / 明文 Key 备份、聊天卡旧配置回滚、中转站视图生命周期、WaveOut 释放竞态、外部进程阻塞、PDH 并发、API URL 安全。2.13.2 之前只做了上帝类拆分，这些数据与线程安全项都没有动。

### 改动

1. **配置健壮性（P1-1/P1-2/P1-3/P1-4）**
   - `Settings.Normalize()` 对 `Style/Overlay/Cli/Data/Behavior/Hotkeys/Cards` 判空；`ConfigStore.Load` 归一化包 `try`，失败用默认值并提示。
   - `SettingsMigration.Load(json, out needsSave, out corrupted)`：损坏 JSON / 绑定失败标 `corrupted`，不再被当成旧版本迁移后静默保存默认值。
   - `ConfigStore.Save` / `CardStateStore.Write` 改临时文件 + `File.Replace`（失败回退备份+覆盖）+ `Flush(true)`；读取回退 `.bak`；损坏主文件备份 `.broken-*`；从 `.bak` 恢复时保留好备份。
   - 明文 API Key 迁移前检测，迁移保存时不生成含明文的 `.bak`，并清理历史明文备份；DPAPI 加密失败清空 Key 而不是明文落盘。
2. **聊天卡配置回滚（P1-5）**：`ChatCard.UpdateContent` / `ApplyStyle` 刷新 `ChatView` 缓存的 `Settings` / `ChatSettings` / `ICardHost`；`Persist` 只写宿主当前卡片，旧视图不写盘。
3. **中转站生命周期（P1-6）**：`StationView : IDisposable`，`Dispose` 取消复制队列、停 `CardStatus` 定时器、丢弃已销毁视图的后台回调；`Persist` 加锁并按 `Item.Id` 读-合并-写，避免旧视图整表覆盖新视图。
4. **WaveOut 释放竞态（P1-7）**：`Stop` 只在 `Join` 成功后释放；超时把 buffer/header/handle 所有权交给 Feed 线程（CAS 只收尾一次）；`Feed` 在 `Read` 后、复制前、`waveOutWrite` 前复检 `_stop`。
5. **外部进程阻塞（P1-8）**：新增 `Core.AsyncProcess`（stdout/stderr 并行异步读、整体超时、`Kill(true)`、有界收尾）；FfmpegPlayer / Decoders / NvidiaSmi 改异步或后台路径；`Decoders.Detect()` getter 只读缓存 + 后台预热；设置页「检测」改 `async`。
6. **PDH / 共享 List 并发（P1-9）**：删除 `MetricEngine` 共享 `_buffer` 改局部 List；PDH 访问统一 `_pdhGate` / `PdhQuery._sync`；`UpdateSettings` 只置 `_adapterDirty`，适配器解析留在采样线程。
7. **API URL 安全（P1-10）**：`Configured` 用 `Uri.TryCreate`；带 Key 只允许 https，回环允许 http；非 http 拒绝；提示区分“未配置 / 地址不安全”。

完整对照与验证记录见 [review-p1-fixes.md](review-p1-fixes.md)。

### 验证

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0；新增「配置修复」「接口安全」，覆盖 null 块、损坏标记、旧 schema、状态 `.bak` 回退、https/回环 http/公网 http/ftp。
- `--smoke 15 --smoke-visible`：退出码 0；媒体卡、采样、托盘、热键、启动动画正常。
- 临时目录手工验证：损坏 `settings.json` → `.broken-*` + 从 `.bak` 恢复且保留 `.bak`；明文 API Key 迁移后主文件含 `dpapi:`、`.bak` 删除、无明文。

### 剩余

- 设置窗打开期间持有深拷贝，热键改 Chat 模式后再应用设置仍可能被旧快照覆盖（需设置窗协调）；
- WaveOut 在音频管道永久阻塞时延迟释放（避免 UAF 的有意取舍）；
- `Decoders` 首次冷启动提示可能短暂显示“没找到”，后台预热后再读恢复；
- P2/P3 未在本轮处理。

---

## 36. v2.13.4（`eaa479c`，本地标签 `v2.13.4`）

### 背景

2.12.2 评审报告的 P2 有 18 条。本轮只取有实际影响的 10 条：句柄/定时器泄漏、热键失败丢旧绑定、命令输出无上限、命令原文进日志、主题导入无上限/UI 整读、任务队列累积与 Dispose 竞态、播放覆盖系统音量、PDH 合法状态被丢、ffmpeg 重启代号。结构/测试工程/供应链项不动。

### 改动

1. **P2-2 句柄泄漏**：`ProcessIntegrity.Level` 在 `finally` 关闭 `OpenProcessToken` 句柄。
2. **P2-3 热键旧绑定**：`HotkeyManager.Register` 先解析并尝试新注册，成功后才摘旧动作；失败保留旧绑定；共享组合键不重复注册。
3. **P2-4 窗口定时器**：`OverlayWindow.Closed` 先停 `_topmostTimer` / `_glassTimer` 并置空，再释放底板，关闭窗口不再被定时器钉住。
4. **P2-5 命令输出上限**：stdout/stderr 各保留尾部 1 MB（环形缓冲 + 读空管道），`CommandResult` 增加截断标志；显示层只切尾部行并追加“输出已截断”提示；修正尾部 N 行的 off-by-one。
5. **P2-6 日志脱敏**：命令行执行日志只记工具、目录、命令长度和 `DescribeCommand` 脱敏摘要（Authorization/Bearer/token/password 等 → `***`，≤80 字）；完整命令只留在界面。
6. **P2-7 主题导入**：1 MB 上限 + 拒绝 `\\.\` 设备/管道路径；设置页导入改后台读/解析、回 UI 套用，防重入且设置已变时丢弃过期结果。
7. **P2-10 任务队列**：已结束 job 只保留最近 16 个；`Dispose` 延迟到 in-flight 归零再释放信号量，`acquired` + ODE 兜底，不再无限累积或抛 ObjectDisposedException。
8. **P2-11 系统音量**：默认 100% 完全不写设备音量；显式 <100 才临时改，先保存 `waveOutGetVolume` 原值并在收尾恢复；读不到原值则不覆盖。
9. **P2-17 PDH 状态**：接受 `PDH_CSTATUS_VALID_DATA=0` 与 `PDH_CSTATUS_NEW_DATA=1`，其它状态才跳过。
10. **P2-18 ffmpeg 代次**：`_run` generation + `BeginRun`/`IsCurrent`；读帧、重开、进程安装、Kill 全代次校验；旧线程晚归不杀新进程/不报假失败，过期音频进程也收掉。

完整对照与未纳入项见 [review-p2-fixes.md](review-p2-fixes.md)。

### 验证

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0；新增/扩展「命令安全」（尾部 N 行 / 截断提示 / 脱敏）、「主题文件校验」（超 1 MB 拒绝）、「后台任务」（20 任务保留上限 + 入队即 Dispose）。
- `--smoke 15 --smoke-visible`：退出码 0；媒体卡、采样、托盘、热键、启动动画正常。
- 并行 harness（仓库外，已删除）：3.3 MB 输出精确尾部截断、超时/取消、脱敏、UTF-8 边界、主题超限/管道拒绝、正常往返 19/19。

### 剩余风险

- P2-10：work 永不结束时信号量延迟到进程退出才释放；
- P2-11：<100 音量仍会临时改设备级音量（有恢复），强杀时可能来不及恢复；
- P2-18：重启瞬间仍可能有一帧旧代回调微竞态，旧读线程卡管道时 Join 窗口与原来一致；
- P2-7：特殊路径若 Read 永久阻塞只挂后台线程，UI 不卡。

---

## 37. v2.13.5（`7920969`，本地标签 `v2.13.5`）

### 背景

`MirrorpondHUD-2.13.4-review.md` 指出 8 处“头痛砍头”/半修与多条 P1/P2。本轮修 P1 与“先修”高风险项：中转站孤儿文件与合并截断、热键路由抵消旧绑定、Chat 设置窗旧快照、DPAPI/备份顺序、缺版本号/溢出、Replace 回退备份失败、命令脱敏、解码器切换、媒体 UI Join。

### 改动

1. **中转站**：销毁后复制完成按 Id 合并进 `items.json`（不碰已销毁 UI）；合并按 `AddedUtc` 旧→新排序、从头部裁最旧；写盘成功后才更新加载基线。
2. **热键路由**：`HotkeyRouter.Register` 去掉 `UnregisterAll`，逐动作先注册新键、失败保留旧键并提示；`DetachStale` 清理删卡/停用动作。
3. **Chat 设置窗**：保存前按“用户编辑优先、未编辑字段取 live”合并 Chat Mode / 呼出热键 / 全局热键 / 卡片动作热键；恢复默认值跳过合并。
4. **配置**：缺 `SchemaVersion` 按卡片形状识别当前格式；版本号范围校验；未来版本只读且 `Save` 拒绝覆盖；空/缺失主文件尝试 `.bak`；DPAPI 失败保留旧 Key；保存成功后再清洗 `.bak/.broken/.tmp`；`Save` 串行化；Replace 回退备份失败中止。
5. **状态存储**：主文件非合法 JSON 时回退 `.bak`；写入串行化 + 备份失败中止。
6. **命令脱敏**：补空白分隔 `--token value` / `--password value` / `-u user:pass` 等。
7. **媒体**：同路径切解码器显式重启；Ffmpeg 读线程 Join 移后台；WaveOut Stop 后台有界收尾 + 自然 EOF 释放；`FindFfmpeg` 缓存；首启显示“正在检测…”；窗口关闭后不写玻璃画面。
8. **ChatView**：异常后恢复输入；实现幂等 `IDisposable`。

完整对照见 [review-2.13.4-fixes.md](review-2.13.4-fixes.md)。

### 验证

- `dotnet build -c Release`：0 警告 0 错误。
- `--selftest`：失败项 0；配置修复新增缺版本/溢出/未来只读/非 chat Extras 加密/坏状态 `.bak`。
- `--smoke 15 --smoke-visible`：退出码 0；热键全部注册、启动/退出正常。
- 临时目录实测：未来版本只读且文件未变；主文件缺失/为空从 `.bak` 恢复；无版本当前格式补写且保留 chat/media/dropstation；明文 Key 的 `.bak/.broken/.tmp` 全部清洗。

### 剩余

- API 局域网 HTTP opt-in、主题导入超时、归一化整份重置、指标次级项、SelfTest 隔离/独立测试工程、结构债未做；
- 热键失败恢复依赖进程内状态；中转站 Move + 销毁后完成只补状态不删源；媒体 Stop 后台收尾最多约 1s。

---

## 38. v2.13.6（功能分支 `feature/runtime-card-loading`，未合并）

### 背景

v2.12.2 的调研文档提出 `plugins/cards/*.dll` 运行时扩展卡片；本轮先做 P0/P1 可信本地插件 spike：
验证独立 ALC 加载、启动早期注入、示例插件进入添加菜单、未知 Kind 不破坏配置。

### 改动

1. **`CardRegistry`**：静态发现改为可初始化快照；新增 `Initialize(external modules)`，内置 + 插件按 Order 合并、Kind 冲突拒绝；未知 Kind 不再回退改写，`ResolveKind` 保留原值。
2. **`CardPluginLoader`**：扫描 `plugins/cards/<id>/manifest.json`；校验 `apiVersion=1`、assembly/entryType；独立可收集 ALC + `AssemblyDependencyResolver`；宿主程序集交回默认 ALC 保证契约 Type 一致；坏插件只记日志。
3. **启动时序**：`App` 在截图 / 配置归一化前调用 `CardRegistry.Initialize(CardPluginLoader.LoadAll())`，失败不阻止启动。
4. **示例插件**：`samples/RuntimeCardExample`（`CardModuleBase` 文字卡 + manifest + 构建/安装说明）。
5. **未知 Kind 保留**：`CardModel.Normalize` 对未注册 Kind 保留 Kind / Content / Extras，等插件恢复后自动可用；自检覆盖。
6. **安全边界**：`plugins/` 不跟仓库；ALC 不是沙箱，只加载可信本地插件；第一版不做热插拔。

### 验证

- `dotnet build -c Release`：0 警告 0 错误（2.13.6）。
- `--selftest`：失败项 0；覆盖未知 Kind 保留。
- 示例插件安装到 `bin/Release/plugins/cards/example-clock` 后启动：日志显示 7 个模块（内置 6 + 插件 1）。
- `--screenshot-cards` 用临时配置加入 `Kind=example-clock`：生成 8 张卡截图，首行「插件时钟 · HH:mm:ss」。
- 删除插件后重启：配置中的 `Kind=example-clock` 与文件字节保持不变。

### 剩余

- 插件缺失时 UI 仍会回退默认卡渲染（配置已保留，但不显示占位卡）；
- 没有签名 / 白名单 / shadow copy / 热重载；ALC 不是安全沙箱；
- 插件必须与宿主同目标框架、同 API 版本，第一版锁定 `apiVersion=1`。

---

## 39. v2.14.0（功能分支 `feature/runtime-card-loading`，本次提交）

### 背景

v2.13.6 的 spike 只做到「重启加载可信本地插件」：插件缺失时 `Kind` 能被保留，但界面仍会回退成默认卡；
插件 DLL 被宿主锁住，开发时只能反复重启；没有清单校验、没有加载报告，也没有热重载。独立风险复核
（`%TEMP%\hot-reload-review\report.md`）指出影子复制回退、Kind 校验、设置页引用、ALC 回收证据与发布数据保护等方向。
本轮把**生产默认路径**补齐（仍是重启生效），并把 `--plugin-dev` 做成只在开发期使用的纯文字卡热重载。

### 改动

1. **插件影子复制**：加载前把 `<exe>/plugins/cards/<id>/`（或 `%LOCALAPPDATA%\MirrorpondHUD\plugins\cards\<id>`）整目录复制到
   `%LOCALAPPDATA%\MirrorpondHUD\plugin-cache\<id>-<时间戳>-<随机>` 再加载，源目录不再被锁，运行中可覆盖 / 删除 DLL，
   下次加载用新文件；启动时清理上一轮残留（日志「已清理插件影子缓存 N 个」）。`LocalAppData` 不可用时影子根退到 `%TEMP%`；
   复制失败则回退源目录直接加载（WARN，源 DLL 被锁，热重载可能不可用），坏插件不影响宿主启动。
2. **缺失插件占位卡**：`CardRegistry.Get` 对未注册且不是旧别名的 `Kind` 返回按原 Kind 渲染的 `UnavailableCardModule`
   （「插件不可用：<kind>」+ 重新安装提示）。占位模块不进入 `Modules`，因此不会出现在「添加卡片」菜单；
   `Kind` / `Content` / `Extras` 原样保留、不写回配置；插件装回后 dev 模式自动热重载恢复，生产模式下次启动恢复。自检新增占位断言。
3. **manifest / Kind 校验**：`manifest.json` ≤ 256 KB；`id` ≤ 100 字符且只允许字母数字与 `.-_`；`apiVersion` 必须为 1；
   `assembly` 只接受裸 DLL 文件名；`entryType` 非空、不含空白，入口类型必须存在、非抽象 / 接口、实现 `ICardModule` 且有 public 无参构造；
   模块 `Kind` ≤ 64 字符、只允许字母数字与 `.-_`，并拒绝内置 Kind 与旧别名
   （`infotext` / `command` / `media` / `chat` / `dropstation` / `buttons` / `metrics` / `clock` / `keys` / `text`）。
   任一校验失败只拒绝该插件并写明原因，不阻止宿主启动。
4. **加载报告与重复拒绝**：新增 `CardPluginLoadReport`（成功句柄 + 失败原因 + `HasErrors`）；同批扫描中重复 `id`、重复 `Kind`、
   空 Kind 一律拒绝；启动日志输出「运行时插件加载完成：成功 N 个，失败 M 个」。
5. **`--plugin-dev` 纯文字卡热重载**：监听便携目录与 `%LOCALAPPDATA%` 插件目录，文件变化 700 ms debounce；
   先完整加载并校验新模块，任一目录有加载错误就整轮放弃、保留旧模块；只接受纯文字卡，拒绝 `IRichCardModule`、
   `IsInteractive`、`ICardLegacyAlias`、`ICardEditorSection`；顺序为「换注册表 → 刷新卡片（关闭设置窗并释放编辑器、
   重注册热键）→ 卸载旧 ALC」；失败不改写配置、不退出宿主。
6. **延迟回收验证**：卸载请求后不在同一消息轮判定，交给 800 ms `DispatcherTimer`（Background 优先级）做 3 轮
   `GC.Collect()` + `WaitForPendingFinalizers`，再检查模块 / ALC 上下文弱引用；日志区分「均已回收」「模块已回收、上下文延迟回收」
   「仍有模块引用」三种结果，验证后清理影子目录。新增可选 `ICardModuleLifetime.OnPluginUnloading`（additive，`apiVersion` 保持 1），
   在 Unload 前让插件停线程 / 解绑事件。
7. **设置页释放与刷新**：`SettingsWindow.ReleaseCardEditorsForPluginReload` 逐个 `CardEditor.ReleaseModule()`、
   摘掉 `CardList.ItemsSource` 并清空编辑器列表，`CloseForReal` 复用同一路径；`OverlayController.ReloadPluginCards`
   只重建模块引用发生变化的卡片，文字卡直接重画、不重建窗口。
8. **发布与退出**：`publish.bat` 在 `rd /s /q bin\Release` 前后备份 / 恢复 `bin\Release\plugins`（连同 settings / log），
   重发布不再删用户插件；退出时先 `ReplaceExternal(空)` 摘注册表引用再卸载当前句柄，第二实例早退不再误报插件清理日志。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/CardPluginLoader.cs` | +592 / −60 | manifest / Kind 校验、影子复制与回退、加载报告、重复拒绝、卸载探针 |
| `src/Cards/CardRegistry.cs` | +112 / −28 | 「插件不可用」占位模块、`ReplaceExternal` |
| `src/Ui/OverlayController.cs` | +63 / 新增 | `ReloadPluginCards`：关设置窗、按 Kind 重绑卡片、重注册热键 |
| `src/App.xaml.cs` | +28 / −2 | 启动清影子缓存 + 加载报告、`--plugin-dev` 启动监听、退出顺序 |
| `src/Ui/CardEditor.cs` | +23 / −5 | `ReleaseModule`，热重载前摘掉编辑器对模块的引用 |
| `src/Ui/SettingsWindow.xaml.cs` | +16 / 新增 | `ReleaseCardEditorsForPluginReload` / `CloseForReal` |
| `src/SelfTest.cs` | +12 / 新增 | 缺失插件占位卡断言 |
| `src/Cards/CardModule.cs` | +10 / 新增 | 可选生命周期接口 `ICardModuleLifetime` |
| `src/Cards/UnavailableCardModule.cs` | +38 / 新增 | 按原 Kind 渲染的「插件不可用」占位模块 |
| `src/Ui/PluginDevReloader.cs` | +327 / 新增 | 目录监听、debounce、安全与失败策略、延迟弱引用验证 |
| `publish.bat` | +8 / −2 | 备份 / 恢复 `bin\Release\plugins` |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本号 2.13.6 → 2.14.0 |

文档同步更新：`README.md`、`docs/runtime-card-extensions.md`、`docs/runtime-card-hot-reload.md`、`samples/RuntimeCardExample/README.md`。

### 实测

- `dotnet build MirrorpondHUD.csproj -c Release`：**0 警告 0 错误**。
- `--selftest`：**失败项 0**（新增「插件不可用」占位卡断言）。
- 热重载 v1 → v2：在 app 副本目录用测试插件（`Kind=hotreload-test`）覆盖 DLL + manifest，日志依次出现
  「检测到插件目录变化，开始热重载」→「已加载卡片插件 Hot Reload Test …（文字卡，影子副本）」→「运行时插件模块已替换」→
  「已刷新 1 张卡片」→「插件热重载完成」；卡片 marker 从 v1 变为 v2 新模块实际渲染的文本。
- 运行中移走插件目录：日志「未发现运行时卡片插件」「运行时插件模块已替换：内置 6，外部 0」，卡片显示「插件不可用：hotreload-test」；
  `settings.json` 的 SHA-256 前后一致，`Kind` / `Extras` 未被改写；放回插件目录后 dev 模式自动加载恢复（生产模式重启恢复）。
- ALC 回收：最新一轮日志「插件 ALC 已卸载（模块 / 上下文弱引用均已回收）」；部分轮次出现
  「插件模块已回收，ALC 上下文仍在卸载（运行时延迟回收；开发模式可接受）」，见残余风险。
- 影子复制：日志出现「已清理插件影子缓存 1 个」「已加载卡片插件 …（…，影子副本）」；运行中覆盖 / 删除源插件 DLL 无 sharing violation。

### 风险与边界

- **生产默认仍是「重启生效」**：设置页不提供热重载 / 卸载按钮；`--plugin-dev` 只是开发者开关，不是正式功能。
- **ALC 不是安全沙箱**：插件以当前用户权限运行，影子复制不改变信任模型；宿主只加载用户明确安装的可信本地插件。
- **富内容卡拒绝热重载**：`IRichCardModule`、交互卡、带 `ICardEditorSection` 或 `ICardLegacyAlias` 的插件在 dev 下都会拒绝并提示重启；
  富内容卡片的生产加载保持现状。
- **插件 static 可变状态 / 后台线程 / `DispatcherTimer` / 事件订阅**仍可能钉住 ALC；`ICardModuleLifetime` 只是 best-effort，
  弱引用只能事后发现、不能预防。
- **设置窗会被热重载强制关闭，未保存的编辑会丢失**（设置页控件会钉住旧 ALC）。
- 若 `ReloadPluginCards` 在换完注册表后抛异常，旧句柄会保留到重启（不卸载，避免视觉树引用已卸载类型）；
  整轮扫描中任一加载错误会放弃本轮并保留旧模块。
- 上下文回收存在延迟；不能把「`Unload()` 调用成功」当作回收成功。
- 影子复制是**全量复制、无签名 / 哈希 / 白名单**：磁盘双份、启动复制耗时、无完整性校验；旧缓存依赖下次启动清理。
- 插件加载在 UI 线程：坏插件构造函数可挂起启动；`FileSystemWatcher` 错误只记日志、不重建监听。
- `Extras` 若被插件类型实例（如属性 DTO）钉住，仍可能阻碍回收；插件 API 建议只放 JSON 原语 / public field。

### `--plugin-dev` 使用方式

```bat
:: 1) 构建宿主（示例插件：dotnet build samples\RuntimeCardExample\RuntimeCardExample.csproj -c Release）
dotnet build -c Release

:: 2) 把插件放到 plugins/cards/<id>/（或 %LOCALAPPDATA%\MirrorpondHUD\plugins\cards\<id>/）
::    示例：copy samples\RuntimeCardExample\bin\Release\RuntimeCardExample.dll bin\Release\plugins\cards\example-clock\

:: 3) 以开发模式启动
bin\Release\MirrorpondHUD.exe --plugin-dev

:: 需要自动退出 / 收集日志时可配合 --smoke
bin\Release\MirrorpondHUD.exe --plugin-dev --smoke 90 --smoke-visible
```

- 监听 `<exe>/plugins/cards` 与 `%LOCALAPPDATA%\MirrorpondHUD\plugins\cards`（目录不存在会自动创建，两个根都监听）。
- 改完 `manifest.json` 或 DLL 保存即可，700 ms debounce 后自动重载；日志会说明成功或拒绝原因。
- 只支持纯文字卡；富内容卡、交互卡、带设置区块或旧 Kind 别名的插件会提示「请重启宿主」，不会替换。
- 热重载不写配置；重载期间设置窗会被关闭。生产使用不要带 `--plugin-dev`，装 / 换插件后重启。

---

## 40. v2.14.1（`730fd39`，功能分支 `feature/runtime-card-loading`）

### 背景

v2.13.6 / v2.14.0 的运行时插件走的是方案 A：插件直接引用 `MirrorpondHUD.dll`，宿主把整个公开面当契约。
这能最快跑通链路，但契约 = 整个宿主，宿主内部重构就可能破坏插件，也无法同时支持多个宿主版本；
扩展调研（`docs/runtime-card-extensions.md` §4.1）与模块化评审都建议长期走方案 B。本轮做方案 B 的第一步：
把契约与 DTO 拆到独立程序集，并用类型转发保住旧插件的二进制。

### 改动

1. **新增 `MirrorpondHUD.Contracts` 契约程序集**：`net8.0-windows` + `UseWPF` 类库，命名空间保持不变
   （`MirrorpondHUD.Cards` / `MirrorpondHUD.Core` / `MirrorpondHUD.Metrics` / `MirrorpondHUD.Text`），减少源码改动。迁出 15 个源码文件：
   - Cards 契约：`CardHost` / `CardModule` / `CardSettingsModule` / `RichCard`
     （`ICardModule` / `IRichCardModule` / `CardModuleBase` / `RichCardModuleBase` / `ICardHost` / `ICardSettingsModule` 等）；
   - 设置与 DTO：`Settings`（含 `OverlaySettings` / `StyleSettings` / `CliSettings` / `DataSettings` / `BehaviorSettings` /
     `HotkeySettings` / `CardActionHotkey` / `CommandSettings` / `MediaSettings` / `CardSettings` / `ChatSettings`）；
   - 渲染上下文与采样：`CardRenderContext`、`MetricsSnapshot`（含 `DriveSample` / `NvidiaSample`）、`KeyHistory`；
   - 模板与变量：`TemplateEngine` / `Variables` / `KeyNames`；
   - 基础工具：`Log` / `Json` / `SecretStore` / `ColorCode` / `Icons` / `Keycaps`。
2. **宿主引用与旧插件兼容**：宿主 csproj 改为 `ProjectReference` 契约工程，并排除 `src\Contracts\**` 的默认编译；
   新增 `src/TypeForwards.cs`，对 43 个契约类型做 `TypeForwardedTo`。因此 2.13.6 / 2.14.0 编译、
   `AssemblyRef` 仍指向 `MirrorpondHUD` 的旧插件二进制无需重编译即可加载，`typeof(ICardModule)` 与宿主仍是同一个 Type。
3. **插件 ALC 同时对两个程序集放行**：`CardPluginLoadContext.Load` 对宿主 `MirrorpondHUD` 与契约
   `MirrorpondHUD.Contracts` 都返回 `null`，两者都交给默认 ALC；插件自己的依赖仍走插件 ALC。
4. **示例插件改引 Contracts**：`samples/RuntimeCardExample` 从 `HintPath` 引用宿主 DLL 改成对 `src/Contracts` 的
   `ProjectReference`（`Private=false`，不把契约 DLL 复制进插件目录）。
5. **ThemeCatalog 资源查找修正**：主题资源编在宿主程序集，`Settings` 迁出后原来的 `typeof(Settings).Assembly`
   会指到 Contracts，改为 `typeof(ThemeCatalog).Assembly`。
6. **可见性收口**：`Log` / `Json` 仍是 Contracts 内部实现，用 `InternalsVisibleTo("MirrorpondHUD")` 只向宿主开放；
   WPF 共享控件与 `ThemeTokens` 留在宿主（`ThemeTokens` / `ThemeResolver` 在 2.14.3 迁入，见 §42）。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +27 / 新增 | 契约程序集工程（WPF / net8.0-windows / 版本 2.14.1） |
| `src/TypeForwards.cs` | +50 / 新增 | 43 个契约类型的宿主类型转发 |
| `src/Contracts/...`（15 个文件） | 0 / 0 | 契约、设置 DTO、渲染上下文、采样、模板与基础工具原样迁出 |
| `src/Cards/CardPluginLoader.cs` | +8 / −3 | 插件 ALC 对宿主 + Contracts 都返回 `null` |
| `src/Core/ThemeCatalog.cs` | +2 / −1 | 主题资源改用 `typeof(ThemeCatalog).Assembly` |
| `src/Contracts/AssemblyInfo.cs` | +4 / 新增 | `InternalsVisibleTo("MirrorpondHUD")` |
| `MirrorpondHUD.csproj` | +9 / −1 | 引用 Contracts、排除其源码、版本 2.14.0 → 2.14.1 |
| `samples/RuntimeCardExample/RuntimeCardExample.csproj` | +3 / −4 | 插件改引 Contracts 工程 |

### 实测

- `dotnet build MirrorpondHUD.csproj -c Release`：**0 警告 0 错误**；示例插件同样 0 / 0。
- `--selftest`：**失败项 0**（占位卡等既有断言保持通过）。
- 旧插件路径：2.13.6 / 2.14.0 编译、引用 `MirrorpondHUD.dll` 的插件二进制通过类型转发加载成功。
- 新插件路径：只引用 `MirrorpondHUD.Contracts` 的示例插件加载成功（日志「已加载卡片插件 Example Clock 0.1.0 …」）。
  两条路径均实测 selftest 通过。

### 风险

- **类型转发只增不减**：删除 / 改名 / 改签名一个已转发的类型，会让旧插件在加载时失败；必须提升 `apiVersion` 并给出迁移说明。
- **两程序集版本要同步**：宿主与 Contracts 版本号保持一致（2.14.x），发布时不能只升一个。
- **WPF 共享控件未拆**：`CardButton` / `CardList` / `CardInput` 等仍在宿主；插件若要用只能额外引用宿主，
  回到方案 A 的耦合。`ThemeTokens` / `ThemeResolver` 在 2.14.3 才迁入 Contracts。
- **契约内部实现边界**：`Log` / `Json` 仍靠 `InternalsVisibleTo` 给宿主用，不是稳定公开 API，插件不要直接依赖。

---

## 41. v2.14.2（`f67a255`，功能分支 `feature/runtime-card-loading`）

### 背景

方案 B 第一步只搬了类型，`ConfigStore`（Core 层）仍直接调 `CardModel.Normalize`（Cards 层）：
每次 Load / Save 都要为卡片归一化反向引用 Cards，Core→Cards 的编译依赖还在，契约边界没有真正闭合。
扩展文档的审计表把这条列为“拆契约前必须先解开的环”。

### 改动

1. **归一化钩子**：`ConfigStore` 新增 `public static Action<Settings> SettingsNormalizer` 与私有 `NormalizeCards`；
   `Load`（正常 / 失败回退两条路径）与 `SaveCore`（`Save` 内部）里三处 `CardModel.Normalize(settings)` 改调钩子（钩子为空时跳过）。
2. **宿主注入**：`App.OnStartup` 在插件加载、`CardRegistry.Initialize` 之后、第一次 `ConfigStore.Load` 之前
   注入 `ConfigStore.SettingsNormalizer = CardModel.Normalize`。
3. **行为不变**：卡片归一化（未知 Kind 保留、旧别名、默认字段）仍由 `CardModel` 完成；只是调用方向反转成
   Core 提供委托、宿主注入。
4. 自检在 2.14.3 的契约身份断言里检查 `ConfigStore.SettingsNormalizer != null`。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Core/ConfigStore.cs` | +16 / −4 | 去掉 `using MirrorpondHUD.Cards`，新增 `SettingsNormalizer` 委托与 `NormalizeCards` |
| `src/App.xaml.cs` | +2 / 新增 | 启动早期注入 `CardModel.Normalize` |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本 2.14.1 → 2.14.2 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.1 → 2.14.2 |

### 实测

- `dotnet build MirrorpondHUD.csproj -c Release`：**0 警告 0 错误**。
- `--selftest`：**失败项 0**。
- 卡片归一化仍生效：损坏 / 迁移配置在 Load / Save 后的 Kind、旧别名与默认字段行为与 2.14.1 一致。

### 风险

- 静态可变委托是进程级状态：任何绕过 `App` 的测试宿主 / 工具都必须自己注入，否则卡片归一化被跳过。
- 注入时机敏感：必须早于第一次 `ConfigStore.Load`，否则首次加载漏归一化；2.14.3 自检只断言“已注入”，不覆盖时序。
- 委托指向宿主方法，不参与插件 ALC 隔离；插件不能依赖它。

---

## 42. v2.14.3（功能分支 `feature/runtime-card-loading`，本次提交）

### 背景

2.14.1 把契约 / DTO 搬进 `MirrorpondHUD.Contracts` 后，`ThemeTokens` / `ThemeResolver` 仍留在宿主的
`src/Ui/ThemeTokens.cs`：插件拿得到 `StyleSettings` 却用不到令牌解析，而且 `ThemeResolver.Resolve` 依赖宿主的
`OverlaySurface.ParseColor`，无法跟着契约走。本次把该文件整体迁入 Contracts（`ThemeResolver` 改用 Contracts 内
`ColorCode.TryParse`），补上版本号与契约身份自检，并做最终验证。WPF 共享控件仍留宿主，方案 B 第二阶段不变。

### 改动

1. **ThemeTokens 迁入 Contracts**：`src/Ui/ThemeTokens.cs` → `src/Contracts/Ui/ThemeTokens.cs`，
   `namespace MirrorpondHUD.Ui` 保持不变；`ThemeResolver.Resolve` 的 `OverlaySurface.ParseColor(...)` 改为
   Contracts 内 `ColorCode.TryParse`（新增本地 `ParseColor` 帮助方法），去掉对宿主实现的依赖。
2. **宿主类型转发**：`src/TypeForwards.cs` 增加 `ThemeTokens`、`ThemeResolver` 两个 `TypeForwardedTo`（累计 45 个），
   旧二进制引用 `MirrorpondHUD.Ui.ThemeTokens` 时仍由默认 ALC 解析到 Contracts 里的同一个 Type。
3. **版本号**：宿主 csproj 与 Contracts csproj 均为 **2.14.3**。
4. **SelfTest 契约身份断言**：新增 `contractsOk`，断言 `ICardModule` / `CardRenderContext` / `Settings` 所在程序集是
   `MirrorpondHUD.Contracts`、`CardRegistry` 是 `MirrorpondHUD`、且 `ConfigStore.SettingsNormalizer != null`，并入卡片自检。
5. **最终验证**：构建 0 / 0、`--selftest` 失败项 0；旧插件（2.13.6 / 2.14.0 编译、引用宿主）与新示例插件
   （只引用 Contracts）两条加载路径均实测通过。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Contracts/Ui/ThemeTokens.cs` | +15 / −8 | 从 `src/Ui/ThemeTokens.cs` 迁入；改用 Contracts 内 `ColorCode` |
| `src/TypeForwards.cs` | +2 / 新增 | `ThemeTokens` / `ThemeResolver` 类型转发 |
| `src/SelfTest.cs` | +11 / 新增 | 契约身份断言（程序集身份 + 归一化钩子） |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本 2.14.2 → 2.14.3 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.2 → 2.14.3 |

文档同步更新：`README.md`、`docs/version-history.md`、`docs/runtime-card-extensions.md`、
`docs/runtime-card-hot-reload.md`、`samples/RuntimeCardExample/README.md`（规模另计）。

### 实测

- `dotnet build MirrorpondHUD.csproj -c Release`：**0 警告 0 错误**；示例插件 0 / 0。
- `--selftest`：**失败项 0**，含新增契约身份断言（2.14.3 冒烟日志 `版本=2.14.3.0`）。
- 旧插件加载：2.13.6 / 2.14.0 编译、引用 `MirrorpondHUD.dll` 的二进制经 `TypeForwardedTo` 加载成功，selftest 通过。
- 新插件加载：只引用 `MirrorpondHUD.Contracts` 的 `ExampleClockCard` 加载成功
  （日志「已加载卡片插件 Example Clock 0.1.0」，模块数内置 6 + 外部 1）。
- `--plugin-dev --smoke` 冒烟中示例插件经历热重载 / 模块替换，日志正常（`%TEMP%\contract-hot\mirrorpond.log`）。

### 风险

- **UiKit 共享控件未拆**：`CardButton` / `CardList` / `CardInput` / `CardHeader` / `CardDropOverlay` / `CardStatus` /
  `CardTextBox` / `CliUi` 仍在宿主；插件要用这些控件只能额外引用宿主，方案 B 第二阶段再抽 `MirrorpondHUD.UiKit`。
- **旧二进制兼容靠类型转发**：2.13.6 / 2.14.0 插件不需要重编译，但前提是契约类型继续以转发形式存在；
  将来删类型 / 改签名必须先提升 `apiVersion`。
- **ThemeTokens 迁移是二进制破坏点**：`ThemeTokens` / `ThemeResolver` 的物理程序集变了，靠宿主转发兼容；
  直接 `typeof(ThemeTokens).Assembly` 做判断的第三方代码会看到 `MirrorpondHUD.Contracts`。
- **版本一致性**：宿主与 Contracts 必须同为 2.14.3；若只升一个，自检的契约身份断言仍过，但发布包会混版本。

---

## 43. v2.14.4（`ef70cea`，功能分支 `feature/runtime-card-loading`）

### 背景

2.14.1–2.14.3 用宿主 `TypeForwardedTo` 保住旧插件（2.13.6 / 2.14.0 编译、引用 `MirrorpondHUD.dll` 的二进制）：
契约虽然搬到了 `MirrorpondHUD.Contracts`，旧二进制仍能解析到同一个 Type。但项目判断插件总量少、
不以兼容性为目标，继续收紧边界更划算：删掉兼容层，把插件 API 从 1 升到 2，旧插件不再承诺可加载。

### 改动

1. **删除 `src/TypeForwards.cs`**（−52 行）：宿主不再对契约类型做 `TypeForwardedTo`；契约类型只存在于
   `MirrorpondHUD.Contracts`，引用宿主 `MirrorpondHUD.dll` 的旧插件二进制不再保证可加载。
2. **插件 API 升到 2**：`CardPluginLoader.SupportedApiVersion` 从 1 改为 2，`manifest.json` 的
   `apiVersion` 必须为 2。`apiVersion=1` 的旧 manifest 会被明确拒绝，日志为
   「卡片插件 API 版本不兼容: <id> api=1（宿主支持 2）」，只影响该插件、宿主照常启动。
3. **示例 manifest 升版**：`version` 0.1.0 → 0.2.0，`apiVersion` 1 → 2。
4. 版本号：宿主 csproj 与 Contracts csproj 从 2.14.3 → 2.14.4。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/TypeForwards.cs` | 0 / −52 | 删除类型转发兼容层 |
| `src/Cards/CardPluginLoader.cs` | +1 / −1 | `SupportedApiVersion` 1 → 2 |
| `samples/RuntimeCardExample/manifest.json` | +2 / −2 | version 0.2.0、apiVersion 2 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本 2.14.3 → 2.14.4 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.3 → 2.14.4 |

### 实测

- 构建通过；`--selftest`：**失败项 0**。
- 新插件（只引用 Contracts、`apiVersion=2`）加载成功。
- 旧插件（`apiVersion=1`、2.14.3 及之前编译）被明确拒绝：日志「卡片插件 API 版本不兼容: … api=1（宿主支持 2）」，
  宿主不崩溃，其余插件与卡片不受影响。

### 风险

- **破坏性变更**：2.14.3 及之前编译的插件必须重编译（改为引用 Contracts）并把 `apiVersion` 改成 2，否则不加载；
  这是有意的取舍（插件量少，不再维护类型转发兼容层）。
- `TypeForwardedTo` 删除后，用 `typeof(...).Assembly` 判断程序集的第三方代码会直接看到
  `MirrorpondHUD.Contracts`，不再有宿主转发兜底。
- 插件 API 从 1 到 2 的迁移目前只有本文档与扩展文档说明，设置页 / 日志没有“请升级插件”的用户指引。

---

## 44. v2.14.5（功能分支 `feature/runtime-card-loading`，本次提交）

### 背景

2.14.1–2.14.4 已把契约 / DTO 拆到 Contracts、移除旧插件兼容层，但 8 个共享 WPF 控件
（`CardButton` / `CardList` / `CardStatus` / `CardHeader` / `CardInput` / `CardTextBox` / `CardDropOverlay` / `CliUi`）
仍留在宿主 `src/Ui/Controls`：插件想复用这些公开控件只能引用宿主，等于回到方案 A 的耦合。
本轮把它们抽成独立 `MirrorpondHUD.UiKit`，方案 B 的主要程序集边界完成。

### 改动

1. **新增 `MirrorpondHUD.UiKit` 程序集**：`net8.0-windows` + `UseWPF` 类库，`ProjectReference` Contracts
   （不引用宿主）；工程文件 `src/UiKit/MirrorpondHUD.UiKit.csproj`，版本 2.14.5。
2. **8 个共享控件迁入**：`src/Ui/Controls/*.cs` → `src/UiKit/*.cs`，命名空间保持 `MirrorpondHUD.Ui.Controls`
   不变；控件里的 `OverlaySurface.ParseColor(...)` 全部改用 `CardPalette.TryColor(...)`。
3. **颜色解析去宿主依赖**：`CardPalette.TryColor`（`src/UiKit/CliUi.cs`）改为直接调用 Contracts 的
   `ColorCode.TryParse`，不再依赖宿主 `OverlaySurface`；UiKit 只依赖 Contracts。
4. **宿主引用 UiKit**：宿主 csproj 保持 `ProjectReference` Contracts，并新增 UiKit 引用，同时排除 `src\UiKit\**` 的默认编译。
5. **内部可见性**：`src/UiKit/AssemblyInfo.cs` 用 `InternalsVisibleTo("MirrorpondHUD")` 让宿主卡片与自检
   访问 `CliPanelView` 等内部绑定状态；插件侧只用公开控件 API。
6. **插件 ALC 放行 UiKit**：`CardPluginLoadContext.Load` 除宿主 / Contracts 外，对 `MirrorpondHUD.UiKit`
   也返回 `null`，由默认 ALC 提供，保证插件与宿主拿到同一份控件类型。
7. 版本号：宿主 / Contracts / UiKit 三个 csproj 均为 2.14.5。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/UiKit/MirrorpondHUD.UiKit.csproj` | 31 行 / 新增 | UiKit 类库工程（WPF / net8.0-windows / 引用 Contracts） |
| `src/UiKit/AssemblyInfo.cs` | 4 行 / 新增 | `InternalsVisibleTo("MirrorpondHUD")` |
| `src/UiKit/CardButton.cs` 等 8 个文件 | +11 / −9 | 从 `src/Ui/Controls` 迁入；颜色解析改用 `CardPalette.TryColor` / `ColorCode` |
| `src/Cards/CardPluginLoader.cs` | +5 / −3 | 插件 ALC 对宿主 / Contracts / UiKit 都返回 `null` |
| `MirrorpondHUD.csproj` | +6 / −1 | 引用 Contracts + UiKit、排除 UiKit 源码、版本 2.14.4 → 2.14.5 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.4 → 2.14.5 |

文档同步更新：`README.md`、`docs/version-history.md`、`docs/runtime-card-extensions.md`、
`docs/runtime-card-hot-reload.md`、`samples/RuntimeCardExample/README.md`（规模另计）。

### 实测

- `dotnet build MirrorpondHUD.csproj -c Release`：**0 警告 0 错误**（Contracts / UiKit / 宿主三工程）。
- `--selftest`：**失败项 0**（共享控件、外观令牌等既有断言 OK）。
- `--smoke 8 --minimized`：exit 0。
- 显式引用 `MirrorpondHUD.UiKit` 的临时插件加载验证通过：插件只引用 Contracts + UiKit，UiKit 由默认
  ALC 提供（宿主 ProjectReference 保证已加载），与宿主拿到同一份控件类型。

### 风险

- **控件二进制边界**：8 个控件换到 `MirrorpondHUD.UiKit`，插件的 `AssemblyRef` 从宿主变成 UiKit；
  与 2.14.4 的 API 2 配套，旧插件本来就需要重编译。
- **UiKit 插件加载只是临时验证**：用临时插件跑通了加载，尚未固化成示例 / 回归用例；
  后续可补一个固定的 UiKit 示例插件。
- **自检仍是宿主内断言**：`--selftest` 在宿主进程里跑，能验证控件行为与契约身份，不能替代插件 ALC 场景。
- 版本三件套（宿主 / Contracts / UiKit）必须同步为 2.14.5，发布时不能只升一个。

---

## 45. v2.14.6（功能分支 `feature/runtime-card-loading`，本次提交）

### 背景

2.14.5 已把共享 WPF 控件拆到 `MirrorpondHUD.UiKit`，插件程序集边界完成并推送（`a0fe549`）。
按 2.14.5 clean 副本的两份审阅报告（`%TEMP%\review-clean\plugin.md`、`architecture.md`），
本轮只做**低风险清理**与一处让错误更早暴露的加载期校验，不动契约结构、不做用户配置迁移：

- 删死代码 / 无调用重载；
- 插件仍引用宿主程序集时明确拒绝（2.14.4 删除类型转发后，这种插件本就不该再被容忍）；
- 清理过期注释与失效的本地文档路径。

### 改动

1. **删 `CardPluginHandle.EntryType`**（`src/Cards/CardPluginLoader.cs`）：该属性与构造参数只写不读，
   加载日志用的是局部变量 `entryType`；删除属性、构造参数与调用点传参，行为不变。
2. **删 `SettingsMigration.Load(string, out bool)` 无调用重载**（`src/Core/SettingsMigration.cs`）：
   2-out 版本没有任何调用点（3-out / 4-out 版本仍被 `ConfigStore` / `SelfTest` 使用）。
3. **插件引用宿主程序集的显式拒绝**（`CardPluginLoader.LoadOne`）：`LoadFromAssemblyPath` 之后枚举
   `assembly.GetReferencedAssemblies()`，只要引用宿主 `MirrorpondHUD` 就拒绝该插件（丢弃 ALC 与影子目录）：
   日志「卡片插件引用了宿主程序集，已拒绝: <id>（请改为引用 MirrorpondHUD.Contracts）」，
   错误原因「插件引用了宿主程序集 MirrorpondHUD，必须改为引用 MirrorpondHUD.Contracts」。
   这是 2.14.4 删除 `TypeForwardedTo` 后的收口：插件必须只引用 Contracts（需要控件时加 UiKit）。
4. **过期注释 / 失效路径清理**：
   - `CardPluginLoader` 的 ALC 注释改为「宿主 / Contracts / UiKit」（2.14.5 已加 UiKit，注释漏写）；
   - `GlassBackdrop` 去掉不存在的 `docs/blockhelm-glass.md` 引用（该文件被 `.gitignore` 永久忽略）；
   - `ThemeFile` / `DragSource` 去掉失效的 `docs/theme-system.md` / `docs/file-transfer-station.md` 路径
     （设计文档在独立仓库，不随主仓库发布）。
5. 版本号：宿主 / Contracts / UiKit 三个 csproj 均为 2.14.6。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Cards/CardPluginLoader.cs` | +19 / −5 | 删 `CardPluginHandle.EntryType`；新增插件引用宿主的拒绝；ALC 注释更新 |
| `src/Core/SettingsMigration.cs` | 0 / −7 | 删除无调用的 `Load(string, out bool)` 重载 |
| `src/Ui/GlassBackdrop.cs` | +2 / −2 | 注释去掉不存在的 `docs/blockhelm-glass.md` |
| `src/Core/ThemeFile.cs` | +1 / −1 | 注释去掉失效的 `docs/theme-system.md` 路径 |
| `src/Ui/DragSource.cs` | +1 / −1 | 注释去掉失效的 `docs/file-transfer-station.md` 路径 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本 2.14.5 → 2.14.6 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.5 → 2.14.6 |
| `src/UiKit/MirrorpondHUD.UiKit.csproj` | +1 / −1 | 版本 2.14.5 → 2.14.6 |

文档同步更新：`README.md`、`docs/version-history.md`（另计）。

### 实测

- 构建：宿主 / Contracts / UiKit **0 警告 0 错误**。
- `--selftest`：**失败项 0**。
- 引用宿主的测试插件（host-ref）被明确拒绝，日志出现「卡片插件引用了宿主程序集，已拒绝…」；宿主照常启动，不崩溃。
- 正常示例插件（只引用 `MirrorpondHUD.Contracts`、`apiVersion=2`）加载成功。
- 2.14.5 clean 审阅的验证保留：干净副本构建 / `--selftest` / `--smoke` / API2 示例加载 / API1 拒绝 /
  旧 Kind 运行时路径 / legacy schema 迁移，合计 **30/30 PASS**（脚本与日志见 `%TEMP%\review-clean\`）。

### 审阅发现与未处理项

2.14.5 clean 副本有两份审阅报告：`%TEMP%\review-clean\plugin.md`（不必要的兼容层与历史遗留）与
`%TEMP%\review-clean\architecture.md`（契约边界 / 架构）。2.14.6 只采纳低风险项，以下**未处理**，留待后续版本：

- **旧 Kind 别名链（metrics / clock / keys / text）仍是活运行时路径**：老配置每次启动都靠运行时别名渲染，
  直接删会让这些卡片显示「插件不可用」。需要先做 **SchemaVersion=7 迁移**（把旧 Kind 直接写成
  `infotext` + 正确 `Preset`，并在归一化改变 Kind 时落盘），保留至少一个版本后再删别名链。
- **`SettingsMigration` 旧结构迁移必须保留**：schema 0/1/2/3–5 → 6 是老用户唯一迁移路径；已知小缺陷是旧
  `clock` / `text` 自定义内容迁移后 `Preset` 可能落成 `metrics`（按「系统信息」显示，内容仍正常）——建议随
  Schema 7 一起修。
- **Contracts 瘦身（P1-1）未做**：`CommandSettings` / `MediaSettings` / `ChatSettings` / `SecretStore` /
  `Settings.Normalize` 的 chat 策略 / `Default*Content` 等内置卡专用内容仍在契约程序集，插件 ABI 面被污染；
  需要单独的公开面清单与迁移 PR。
- 审阅另列、本版未处理：插件 `Extras` 只支持 public field 序列化（属性值会静默丢失，建议文档化并做 JSON 往返纯化）、
  三工程版本分散 / 插件 ABI 无显式契约版本常量、`ReservedKinds` 与内置 Kind 清单双源、
  README `SchemaVersion` 长期写作 3/4（本次已改为 6）、`PluginManifest.author` 去留、`docs/*.md` 发布策略。

### 风险

- **新校验只覆盖编译引用**：`assembly.GetReferencedAssemblies()` 看不到反射 / 动态加载宿主的极端写法；
  但合法插件本来就不该引用宿主，这类插件仍会因缺少 `TypeForwardedTo` 在加载 / 类型解析时失败。
- **清理不改变运行行为**：删除的是死属性 / 无调用重载与注释路径；`SettingsMigration` 的 3-out / 4-out 版本保留，
  旧配置迁移路径不变。
- **未处理项仍是后续风险**：旧 Kind 删除 / Schema 7 / Contracts 瘦身都需要版本化迁移与回归，不能顺手删。
- 版本三件套（宿主 / Contracts / UiKit）必须同步为 2.14.6。

---

## 46. v2.14.7（`abcbcec`，功能分支 `feature/runtime-card-loading`）

### 背景

到 2.14.6 为止，项目一直保留两层历史兼容：旧 Kind 别名（`metrics` / `clock` / `keys` / `text` 自动转成
`infotext`）与 `SettingsMigration` 的 0/1/2/3/4/5 旧 Schema 迁移。2.14.5 的 clean 审阅把它们列为「必须保留」，
前提是“有用户”；但项目仍在开发阶段、没有用户，这两层兼容只会持续拖累结构演进。本轮按「无用户、可修改」
直接把它们删掉，让旧配置失效。

### 改动

1. **删旧 Kind 别名链**：删除契约 `ICardLegacyAlias`（`src/Contracts/Cards/CardSettingsModule.cs`）、
   `InfoTextCard.LegacyKinds`、`CardRegistry.TryResolveAlias`、`CardModel.Normalize` 的旧 Kind 归一；
   `clock` / `metrics` / `keys` / `text` 不再自动改写成 `infotext`（未知 Kind 保留，显示「插件不可用」占位，
   不写回配置）。
2. **删旧 Schema 迁移**：`SettingsMigration` 移除 0/1/2/3/4/5 迁移与 `LegacySettings` / `LegacyCard` /
   `LegacyHotkeys` / `UpgradeV2Command` / `UpgradeCardExtras` / `LooksLikeCurrentFormat` 等；
   旧 `SchemaVersion` 直接按损坏处理（备份 `.broken-*` 后使用默认设置），当前 `SchemaVersion=6` 与
   **缺版本号的当前格式**仍接受，更高版本仍只读。
3. **插件保留 Kind 收紧**：`CardPluginLoader.ReservedKinds` 只保留 6 个内置 Kind，旧 Kind 别名从插件占用清单移除；
   `PluginDevReloader` 去掉 `ICardLegacyAlias` 拒绝项。
4. **SelfTest 更新**：删除旧 Kind 迁移用例；旧 `SchemaVersion=1` 的期望从“迁移成功”改为“按损坏拒绝
   （`corrupted=true`、`needsSave=false`）”；缺口版本号 / 未来版本用例保留。
5. 版本号：宿主 / Contracts / UiKit 三个 csproj 均为 2.14.7。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Core/SettingsMigration.cs` | +17 / −333 | 删除 0–5 旧 Schema 迁移与 legacy DTO / 升级辅助 |
| `src/Cards/CardRegistry.cs` | 0 / −34 | 删除 `TryResolveAlias` 与 `Get` 的旧别名回退 |
| `src/Contracts/Cards/CardSettingsModule.cs` | 0 / −11 | 删除 `ICardLegacyAlias` 契约 |
| `src/Cards/InfoTextCard.cs` | +4 / −10 | 去掉 `LegacyKinds` 实现与旧 Kind 迁移注释 |
| `src/Cards/CardModel.cs` | 0 / −10 | 删除旧 Kind → `infotext` 归一 |
| `src/Cards/CardPluginLoader.cs` | +2 / −3 | `ReservedKinds` 去掉 4 个旧别名 |
| `src/Ui/PluginDevReloader.cs` | 0 / −6 | 去掉 `ICardLegacyAlias` 拒绝项 |
| `src/SelfTest.cs` | +4 / −24 | 删旧 Kind 用例；旧 Schema=1 期望改为按损坏拒绝 |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本 2.14.6 → 2.14.7 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.6 → 2.14.7 |
| `src/UiKit/MirrorpondHUD.UiKit.csproj` | +1 / −1 | 版本 2.14.6 → 2.14.7 |

### 实测

- 构建：宿主 / Contracts / UiKit **0 警告 0 错误**。
- `--selftest`：**失败项 0**（旧 Kind 用例已删除；旧 Schema=1 的期望改为按损坏拒绝并被覆盖）。
- 行为由自检覆盖：旧 `SchemaVersion` 按损坏处理并回落默认设置；缺版本号当前格式仍接受；
  `Kind=clock` 等旧 Kind 不再被改写成 `infotext`，而是按未知 Kind 显示占位卡。

### 审阅发现与未处理项

- 2.14.5 clean 审阅把旧 Kind 别名链与 SettingsMigration 旧迁移列为「必须保留」，前提是存在用户数据；
  项目确认开发阶段无用户，本版直接删除、不再维护迁移层。将来若引入用户，需要另做导入 / 迁移工具。
- 未处理：无用户迁移路径（这是有意的破坏性变更）；Contracts 瘦身留到 2.14.8。

### 风险

- **旧配置直接失效**：0/1/2/3/4/5 旧 Schema 与旧 Kind 配置不再兼容，启动时按损坏备份并回落到默认设置；
  旧 Kind 卡片显示「插件不可用」占位，配置本身不被改写但不会自动恢复。
- 删除迁移层后回退性差：如果未来需要兼容旧版本，得重新设计迁移 / 导入路径。
- 旧 Kind 从插件保留清单移除后，插件理论上可以占用 `clock` 等 Kind；现有配置中的同名旧卡片会显示占位，
  属开发阶段可接受的取舍。

---

## 47. v2.14.8（`633e9d6`，功能分支 `feature/runtime-card-loading`）

### 背景

2.14.5 审阅的 P1-1（契约边界污染）指出：`MirrorpondHUD.Contracts` 里混着内置卡专用内容——
`CommandSettings` / `MediaSettings` / `ChatSettings`、DPAPI `SecretStore`、内置卡默认内容常量、
`Settings.Normalize` 里的 chat 加密策略等。它们让插件 ABI 面被内置卡实现细节绑住。2.14.7 删掉旧配置兼容后，
2.14.8 继续按「无用户、可修改」把这些内置实现迁回宿主，让 Contracts 只保留插件真正需要的通用契约。

### 改动

1. **内置卡设置迁回宿主**：`CommandSettings` / `MediaSettings` / `ChatSettings` 从
   `src/Contracts/Core/Settings.cs` 迁到新文件 `src/Core/BuiltInCardSettings.cs`（命名空间保持 `MirrorpondHUD.Core`）。
2. **DPAPI 迁回宿主**：`SecretStore` 从 `src/Contracts/Core/SecretStore.cs` 移到 `src/Core/SecretStore.cs`
   （rename，0 行变化）；Contracts 不再包含密文实现。
3. **默认内容迁回宿主**：4 个内置卡默认内容常量迁到新文件 `src/Cards/CardDefaults.cs`（internal）；
   `CardModel` / `InfoTextCard` / `Ui/Catalog` / `Ui/Screenshots` 改用 `CardDefaults`。
4. **Settings 只留通用配置**：`src/Contracts/Core/Settings.cs` 删除内置卡 DTO / 默认内容 / chat 加密遍历；
   保留 `Settings` / `OverlaySettings` / `StyleSettings` / `CliSettings` / `DataSettings` / `BehaviorSettings` /
   `HotkeySettings` / `CardActionHotkey` / `CardSettings`。`Settings.Normalize` 不再对任意卡片 `Extras` 做 chat
   加密（对话卡自己的 `ChatCard.InitializeCard` / `ChatOf` 仍调用 `ChatSettings.Normalize`，DPAPI 仍生效）。
5. **SelfTest / 版本**：旧 Schema=1 期望已在 2.14.7 改为按损坏拒绝（本版延续该预期）；版本号三工程均为 2.14.8。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Core/BuiltInCardSettings.cs` | +316 / 新增 | `CommandSettings` / `MediaSettings` / `ChatSettings` 迁回宿主 |
| `src/Core/SecretStore.cs` | 0 / 0（rename） | DPAPI 从 `src/Contracts/Core/SecretStore.cs` 迁回 |
| `src/Cards/CardDefaults.cs` | +21 / 新增 | 4 个内置卡默认内容常量 |
| `src/Contracts/Core/Settings.cs` | 0 / −345 | 删除内置卡 DTO、默认内容与 chat 加密遍历 |
| `src/Cards/CardModel.cs` | +3 / −3 | 默认内容改用 `CardDefaults` |
| `src/Cards/InfoTextCard.cs` | +5 / −5 | 预设 / 默认内容改用 `CardDefaults` |
| `src/Ui/Catalog.cs` | +2 / −1 | 预设面板内容改用 `CardDefaults` |
| `src/Ui/Screenshots.cs` | +1 / −1 | 演示内容改用 `CardDefaults` |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本 2.14.7 → 2.14.8 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.7 → 2.14.8 |
| `src/UiKit/MirrorpondHUD.UiKit.csproj` | +1 / −1 | 版本 2.14.7 → 2.14.8 |

### 实测

- 构建：宿主 / Contracts / UiKit **0 警告 0 错误**。
- `--selftest`：**失败项 0**（旧 Schema=1 按损坏拒绝的预期已生效）。
- `--smoke 8 --minimized`：exit 0。

### 审阅发现与未处理项

- P1-1「Contracts 只放插件需要的类型」已完成主要部分：Contracts 现在只有插件契约 / 通用 DTO（`Settings` 通用配置、
  `CardSettings`、`MetricsSnapshot`、`KeyHistory`、`CardRenderContext`）、`ThemeTokens` / `ThemeResolver`、
  `Log` / `Json` 内部实现与 `ColorCode` / `Icons` / `Keycaps` 等工具。
- 仍留在 Contracts 的通用但偏宿主的类型：`Settings`（含 `Overlay` / `Style` / `Cli` / `Data` / `Behavior` / `Hotkey` /
  `CardSettings`）与 `ThemeTokens` / `ThemeResolver`；如果继续收紧，需要单独公开面清单与 API 版本规划。
- 审阅另列、本版未处理：插件 `Extras` 只支持 public field 序列化（属性值会静默丢失）、三工程版本分散 /
  插件 ABI 无显式契约版本常量、`ReservedKinds` 硬编码 6 个内置 Kind（2.14.7 已去旧别名）、
  `PluginManifest.author` 去留、`docs/*.md` 发布策略、single-file publish 下的插件加载未实测。
- 兼容性结论：旧插件 2.14.4 起不兼容、插件 API=2；旧配置 2.14.7 起不兼容（旧 Kind / 旧 Schema 不再迁移）。

### 风险

- **程序集身份变化**：`CommandSettings` / `MediaSettings` / `ChatSettings` / `SecretStore` 从 Contracts 迁回宿主，
  直接引用 Contracts 这些类型的第三方代码会编译失败；插件本就不应使用内置卡 DTO，属预期收缩。
- **`Settings.Normalize` 不再清洗任意 `Extras` 的 chat 明文**：对话卡自己的 `InitializeCard` / `ChatOf` 仍做 DPAPI
  归一化；若第三方把 chat DTO 塞进非 chat 卡，宿主不再兜底加密（当前无用户，可接受）。
- 版本三件套（宿主 / Contracts / UiKit）必须同步为 2.14.8。

---

## 48. v2.14.9（功能分支 `feature/runtime-card-loading`，基于 `d73d2ec`，本次提交）

### 背景

2.14.8 的最终复核报告（`%TEMP%\review-clean\final.md`）结论：**55/55 PASS、无 P0**，
但列出 P1 × 2（`CardSettings` 契约默认值/注释、README 配置块说明）与 P2 × 11（注释 / 文档 / 测试夹具残留）。
本轮按“无用户、可修改”继续收尾，只修 P1 两项与部分低风险 P2，不改任何运行行为。

### 改动

1. **P1-1：`CardSettings` 契约修正**：`Kind` 默认值从已删除的 `"metrics"` 改为 `"infotext"`；删除残留的
   “媒体卡片配置”旧注释（2.14.8 把 `MediaSettings` 迁回宿主时留下的），改为“卡片配置（宿主通用模型；插件卡片同样使用）”。
2. **P1-2：README 设置示例对齐实际结构**：`settings.json` 示例从顶层 `Command` / `Media` 改为
   `Extras.command` / `Extras.media`（英文段同步为 `Extras.command` / `Extras.media` / `Extras.chat`），
   说明文字同步；自 2.14.0（schema 5/6）起这些块本来就在 `Extras` 里。
3. **P2 注释 / 夹具清理**：
   - `Settings.CurrentSchemaVersion` 注释改为“2.14.7 起不再迁移历史 Schema，旧文件按损坏备份后使用默认设置”；
   - `ConfigStore` 保存注释去掉已删除的旧迁移描述，只保留明文 Key / 损坏恢复处理；
   - `CommandSettings.Truncate` 注释改为当前语义（默认关、开启后按 `MaxLineChars` 截断，不再是“只为旧配置保留”）；
   - `CardPluginLoader` 类注释补“宿主 / Contracts / UiKit 三程序集”，Kind 冲突错误与日志去掉“旧别名”措辞；
   - `InfoTextCard.InferPreset` 注释改为当前用途（按内容纠回预设 / 未知归系统信息）；
   - `UnavailableCardModule` 占位文案改为「插件不可用：<kind>，请安装对应插件后重启 MirrorpondHUD（旧 Kind 已不再兼容）」，
     避免对已删除旧 Kind 给出“重新安装插件”的错误指引；
   - `SelfTest` 夹具把已删除的旧 Kind（`text` / `metrics`）改为 `infotext`（需要时带 `Preset`），注释同步。
4. 版本号：宿主 / Contracts / UiKit 三个 csproj 均为 2.14.9。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Contracts/Core/Settings.cs` | +3 / −8 | `CardSettings.Kind` 默认 `infotext`；删媒体卡注释、更新 Schema 注释 |
| `src/Cards/CardPluginLoader.cs` | +3 / −3 | ALC 注释补 Contracts / UiKit；Kind 冲突日志去“旧别名” |
| `src/Cards/InfoTextCard.cs` | +2 / −2 | `InferPreset` 注释更新 |
| `src/Cards/UnavailableCardModule.cs` | +1 / −1 | 占位文案区分“插件缺失 / 未知类型” |
| `src/Core/BuiltInCardSettings.cs` | +1 / −2 | `Truncate` 注释改为当前行为 |
| `src/Core/ConfigStore.cs` | +1 / −2 | 保存注释去掉旧迁移描述 |
| `src/SelfTest.cs` | +4 / −4 | 夹具旧 Kind `text` / `metrics` → `infotext`（+`Preset`） |
| `MirrorpondHUD.csproj` | +1 / −1 | 版本 2.14.8 → 2.14.9 |
| `src/Contracts/MirrorpondHUD.Contracts.csproj` | +1 / −1 | 版本 2.14.8 → 2.14.9 |
| `src/UiKit/MirrorpondHUD.UiKit.csproj` | +1 / −1 | 版本 2.14.8 → 2.14.9 |

文档同步更新：`README.md`（设置示例中英文 + 版本索引）、`docs/version-history.md`（另计；这些未计入上面的源码规模）。

### 实测

- 构建：宿主 / Contracts / UiKit **0 警告 0 错误**；示例插件 0 / 0。
- `--selftest`：**失败项 0**。
- `--smoke 8 --minimized`：exit 0。
- 2.14.8 最终复核的 55/55 PASS 保留；2.14.9 只改默认值 / 注释 / 夹具 / 文档，不改变运行行为。

### 审阅现状与剩余项

最终复核报告（`%TEMP%\review-clean\final.md`）确认：无 P0；`ICardLegacyAlias` / `TryResolveAlias` /
`LegacySettings` / `TypeForwardedTo` / `Settings.Default*Content` 等旧兼容路径在 `src` 已 0 处。
2.14.9 已修 P1-1、P1-2，以及 P2-1（Schema 注释）、P2-2（ConfigStore 注释）、P2-3 的 `Truncate` 部分、
P2-4（占位文案）、P2-6（ALC 注释 / 旧别名日志）、P2-7（InferPreset 注释）、P2-8 的 SelfTest 夹具部分。

**未处理（留待后续）**：

- P2-3 剩余：`ChatSettings.Width` / `AnswerLines` 的“只为旧配置保留”注释（字段在 schema 6 旧文件中仍可能出现）。
- P2-5：`samples/RuntimeCardExample/README.md` 仍把 `ICardLegacyAlias` 列为热重载拒绝条件（该类型 2.14.7 已删）；
  本次只允许改 README / version-history，示例 README 待下次同步。
- P2-8 剩余：SelfTest 中“2.14.1 方案 B：契约 / DTO 必须在独立 Contracts 程序集”的注释仍需改成只断言契约类型在 Contracts。
- P2-9：README 的 `docs/*.md` 链接在 clean 快照中不可达（独立仓库策略，需产品确认是否改完整 URL / 注明）。
- P2-10：`AutoStart.CleanLegacy()` 每次启动清理改名前的注册表 Run 值（无害，可保留为防御）。
- P2-11：审阅包中的 `bin/obj` 产物（`.gitignore` 已忽略，不影响仓库）。
- 复核报告明确未验证：真实旧用户升级回放、未来 Schema（>6）文件级只读、网络 / 媒体端到端、UI 交互、
  `--plugin-dev` 热重载、docs 独立仓库内容。

### 风险

- **契约默认值变化**：`CardSettings.Kind` 默认从 `"metrics"` 改为 `"infotext"`；依赖默认值的第三方代码现在会落到
  `infotext`（而不是显示占位卡），更符合当前契约，但仍属默认行为变化。
- 注释 / 文案 / 测试夹具改动不影响运行行为；旧插件与旧配置兼容性仍按 2.14.4 / 2.14.7 的结论（均不兼容，API=2）。
- 版本三件套（宿主 / Contracts / UiKit）必须同步为 2.14.9。

---

## 49. v2.15.0（功能分支 `feature/runtime-card-loading`，`78d3381`，本次提交）

### 背景

插件开发者要写设置/交互面板时，此前只能自己拼原生 WPF 控件，样式和卡片主题不一致。2.15.0 补齐第一优先的 UiKit 表单控件，并在仓库根目录提供完整的 DLL 卡片插件开发指南。

### 改动

1. `src/UiKit/CardFormControls.cs` 新增 `CardSlider`、`CardToggle`、`CardSelect`、`CardNumberInput`、`CardColorPicker`、`CardSegmentedControl`、`CardFormRow`、`CardSettingsForm`；统一实现 `ICardFormControl.ApplyStyle(StyleSettings, cornerRadius)`。
2. 控件颜色全部走 `ThemeTokens` / `CardPalette`，跟随卡片前景色、强调色和字体；`CardSettingsForm` 支持声明式 `AddSlider` / `AddToggle` / `AddSelect` / `AddNumber` / `AddColor` / `AddSegmented`。
3. 根目录新增 `DLL-CARD-PLUGIN-GUIDE.md`：工程引用 Contracts/UiKit、manifest、文字卡/富卡、设置 Extras、表单控件、主题换肤、宿主服务、热键/拖放、生命周期、热重载、调试与安全边界。
4. `SelfTest` 新增「表单控件」回归（控件实例化、样式套用、默认值/选中状态、表单行数）。
5. 版本号：宿主 / Contracts / UiKit 均 2.15.0；插件 API 仍为 2。

### 文件

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/UiKit/CardFormControls.cs` | +886 | 8 个表单控件与 `ICardFormControl` |
| `src/SelfTest.cs` | +31 / −1 | 表单控件回归 |
| `DLL-CARD-PLUGIN-GUIDE.md` | +445 | 根目录插件开发指南 |
| `MirrorpondHUD.csproj` / Contracts / UiKit | +1/−1 each | 2.14.9 → 2.15.0 |

### 实测

- 宿主 / Contracts / UiKit 构建：**0 警告 0 错误**。
- `--selftest`：失败项 0；新增「表单控件」OK。
- `--smoke 8 --minimized`：exit 0。

### 风险 / 未验证

- 表单控件是新增 UiKit API，插件需引用 UiKit；API 版本仍为 2。
- 表单控件只做了 selftest 级实例化/样式验证，尚未做逐控件截图级视觉验收；`CardSelect` 的弹出层在真实卡片上的键盘/滚轮行为需要后续人工验证。
- 主仓库 docs 保留；审阅副本仍按“无历史、只留 themes”生成。

---

## 50. v2.16.0（`6406d96`，分支 `feature/dll-card-migration`，已合并 main）

> 状态：代码 `6406d96` + 文档 `d1fd414` 已推送到 `origin/main`；未打 tag、未发布 Release。

### 背景

到 2.15.0，6 张内置卡片仍编在宿主程序集里，宿主同时承担「卡片实现」与「插件宿主」两种角色：
除 `CardSettingsModule` 的五个契约外，宿主还要认识信息与文本 / 命令行 / 媒体 / 对话等具体类型，
`CardEditor`、`SettingsWindow`、`Screenshots`、`ConfigStore` 都有按卡片类型的分支，卡片想换实现只能改宿主。
2.16.0 把 6 张内置卡全部迁成独立 DLL 插件，与用户插件走同一条加载路径；宿主只保留加载器 / 注册表 / 契约 /
共享控件，并补一组通用宿主服务契约，让「宿主不认识具体卡片」真正落地。本版**不改插件 API 版本**（仍为 2）。

### 改动

1. **6 张内置卡 DLL 化**：`src/Cards` 里的卡片实现迁到 `src/Plugins/<Name>/`，各自独立 `plugin.csproj` 与
   `manifest.json`，程序集 `MirrorpondHUD.Cards.<Name>.dll`，manifest id `builtin.*`；`CardPluginLoader` 新增
   `IsBundled`（id 以 `builtin.` 开头），内置插件不参与 `--plugin-dev` 热重载替换。
2. **宿主构建 / 发布复制内置插件**：宿主 csproj 用 `ReferenceOutputAssembly="false"` 的 `ProjectReference` 构建
   6 个插件工程，`CopyBundledCardPlugins` 复制到 `<exe>/plugins/cards/builtin.*/`；
   `CopyBundledCardPluginsToPublish` 再复制到 single-file 发布目录。启动时统一走 `CardPluginLoader.LoadAll()`，
   日志「已注册卡片模块 6 个（内置 0，运行时插件 6）」。
3. **新增宿主服务契约**（`src/Contracts/Cards/CardHostServices.cs`）：`ICardShellService`（剪贴板 / 文件图标 /
   拖出 / 打开 / 定位）、`ICardEditorCommit`（设置区块草稿提交）、`ICardScreenshotProvider`（截图前准备）、
   `ICardPresetSource` + `CardPreset`（通用预设下拉）、`ICardRuntimeSettingsMerge`（设置窗保存时合并运行时字段）、
   `ICardSelfTest`（插件自带回归）；宿主实现 `src/Ui/CardShellService.cs`。`ICardHost` 增加
   `bool SaveSettings()` 与 `ICardShellService Shell`。`SecretStore` 迁入 Contracts（DPAPI 插件与宿主共用），
   `Json` / `Log` / `AsyncProcess` 转为 public 供插件使用。
4. **宿主去具体卡片化**：`CardEditor` 改用 `ICardPresetSource` / `ICardEditorCommit`；`SettingsWindow` 用
   `ICardRuntimeSettingsMerge`；`Screenshots` 用 `ICardScreenshotProvider`；`ConfigStore` 删除旧明文 Key 清理；
   `CardPluginLoader` 删除保留 Kind 清单（内置卡也是普通插件）；`CardRegistry` 新增 `ClearExternal()` 供退出时
   静默摘引用；`SelfTest` 重写并遍历 `ICardSelfTest`，新增「插件自检」项。
5. **UiKit 媒体控件**：`MediaView` 迁入 UiKit 并更名 `CardMediaView`（`src/UiKit/Media/CardMediaView.cs`），
   同目录含 `GifAnimator` / `FfmpegPlayer` / `VideoWindow` / `WaveOutPlayer` / `Decoders` / `MediaSettings`
   （命名空间 `MirrorpondHUD.Ui.Controls.Media`）；`MediaCard` 插件改用它。UiKit 工程启用 `UseWindowsForms`
   （动图解码用 `System.Drawing.Bitmap`）。
6. **清理**：删除宿主 `src/Core/BuiltInCardSettings.cs`（315 行，内置卡设置随卡片进插件）；`AsyncProcess` 从
   `src/Metrics/NvidiaSmi.cs` 的内部类抽到 `src/Contracts/Core/AsyncProcess.cs`（public，媒体 / 采样 / 插件共用）；
   `.gitignore` 放行 `src/Plugins/`（忽略插件工程的 bin/obj）。
7. 版本号：宿主 / Contracts / UiKit / 6 个插件工程均为 **2.16.0**；插件 API 仍为 **2**（新增契约成员均为加法，
   旧插件无需改 manifest）。

### 内置插件表

| 目录 | 程序集 | manifest id | entryType | 卡片 |
|---|---|---|---|---|
| `src/Plugins/InfoText` | `MirrorpondHUD.Cards.InfoText.dll` | `builtin.infotext` | `MirrorpondHUD.Cards.InfoTextCard` | 信息与文本（文字卡，4 预设） |
| `src/Plugins/Buttons` | `MirrorpondHUD.Cards.Buttons.dll` | `builtin.buttons` | `MirrorpondHUD.Cards.ButtonsCard` | 按钮演示（富内容） |
| `src/Plugins/Command` | `MirrorpondHUD.Cards.Command.dll` | `builtin.command` | `MirrorpondHUD.Cards.CommandCard` | 命令行（富内容） |
| `src/Plugins/Chat` | `MirrorpondHUD.Cards.Chat.dll` | `builtin.chat` | `MirrorpondHUD.Cards.ChatCard` | AI 对话（富内容） |
| `src/Plugins/Media` | `MirrorpondHUD.Cards.Media.dll` | `builtin.media` | `MirrorpondHUD.Cards.MediaCard` | 媒体（富内容） |
| `src/Plugins/DropStation` | `MirrorpondHUD.Cards.DropStation.dll` | `builtin.dropstation` | `MirrorpondHUD.Cards.DropStationCard` | 文件中转站（富内容） |

### 文件

`git show --shortstat 6406d96`：**60 文件，+1568 / −1557**。主要文件：

| 文件 | 行数 | 说明 |
|---|---|---|
| `src/Plugins/*/`（6 组） | 新增 | 6 个插件工程：卡片实现 / manifest / plugin.csproj / 设置与预设 |
| `src/Contracts/Cards/CardHostServices.cs` | +110 / 新增 | 6 个通用宿主服务契约 |
| `src/Contracts/Core/AsyncProcess.cs` | +149 / 新增 | 公共后台进程工具（从 NvidiaSmi 抽出，public） |
| `src/Contracts/Cards/CardHost.cs` | +9 | `ICardHost.SaveSettings()` 与 `Shell` |
| `src/Contracts/Core/SecretStore.cs` | 0 / 0（rename） | DPAPI 从宿主迁入 Contracts |
| `src/UiKit/Media/*` | 新增 / 迁入 | `CardMediaView` / `MediaSettings` / 播放与解码实现 |
| `src/Ui/CardShellService.cs` | +65 / 新增 | `ICardShellService` 宿主实现 |
| `src/SelfTest.cs` | +225 / −867 | 重写为 1105 行，新增插件自检遍历 |
| `MirrorpondHUD.csproj` | +63 / −1 | 引用 6 插件工程、复制到 plugins/cards、版本 2.16.0 |
| `src/Core/ConfigStore.cs` | +5 / −82 | 删除旧明文 Key 清理与具体卡片类型依赖 |
| `src/Cards/CardPluginLoader.cs` | +13 / −16 | 删除保留 Kind、新增 `IsBundled` / `LoadAll(bool)` |
| `src/Cards/CardRegistry.cs` | +19 / −2 | `ClearExternal()`、空注册表安全回退 |
| `src/Ui/CardEditor.cs` / `SettingsWindow.xaml.cs` / `Screenshots.cs` | 见 diff | 改用通用预设 / 提交 / 截图 / 运行时合并契约 |
| `src/Core/BuiltInCardSettings.cs` | 0 / −315 | 删除（内置卡设置随插件走） |

### 实测

- clean build：宿主 / Contracts / UiKit / 6 个插件工程 **0 警告 0 错误**。
- `--selftest`：退出 0、**失败项 0**；「插件自检」= 6 个模块自检通过 / 0 失败；逻辑自检 / 表单控件 / Key 加密等既有项保持 OK。
- `--smoke 8 --minimized`：exit 0，创建 5 张默认卡（infotext×3 + command + media）。
- `--screenshot-cards` / `--screenshot-settings` / `--screenshot-widgets`：均成功。
- single-file publish：发布目录 6 个插件齐全，发布版 `--selftest` 退出 0。
- 干净环境启动日志：`已注册卡片模块 6 个（内置 0，运行时插件 6）`（用户自装插件会增加运行时插件数）。

### 审阅与未验证

- 未做真实媒体播放（图片 / 动图 / 视频 / ffmpeg 路径）、键鼠交互与设置页逐控件点击的人工验收；
- `--plugin-dev` 热重载只对用户文字卡生效，内置插件不参与（内置富卡改代码需重启）；
- `publish.bat` 已改为恢复时跳过 `builtin.*`（内置插件由构建目标重新生成）；安装包 / 覆盖升级场景仍未验证；
- single-file 发布只验证了「发布目录插件齐全 + 发布版 --selftest」，未做安装包 / 覆盖升级场景。

### 风险

- **插件 API 仍为 2**：新增契约成员都是加法，旧插件无需改 manifest；但插件 ABI 面继续扩大（6 个宿主服务契约 +
  `ICardHost` 新成员），后续改动需按 API 版本策略处理。
- **内置卡也是插件**：内置卡与用户插件同路径加载，某个内置插件加载失败时会显示占位卡；发布必须保证
  `plugins/cards/builtin.*` 随 exe 分发，否则「添加卡片」菜单为空。
- **SecretStore / Json / Log / AsyncProcess 进入 Contracts**：插件可用的公共实现变多，但也扩大了契约程序集职责；
  `ConfigStore` 不再清理旧明文 Key（旧迁移已删除，属有意取舍）。
- `.gitignore` 仅放行 `src/Plugins/`，插件工程 bin/obj 仍被忽略。
- 版本一致性：宿主 / Contracts / UiKit / 6 个插件工程必须同步为 2.16.0。

---

## 51. v2.17.0（`27b8bbd`，分支 `feature/data-core`）

### 背景

2.16.0 把 6 张内置卡迁成 DLL 插件后，插件能拿到的共享控件仍只有表单 / 媒体 / 列表 / 状态行。
监控、行情、日志、任务、文件列表类卡片需要高密度数据展示，如果每个插件都自己拼 `ListView` / `Canvas`，
会出现虚拟化、排序、选中态、主题色、空态各写一套的问题。2.17.0 补齐**第二优先：数据展示控件**的第一批，
全部放在 `MirrorpondHUD.UiKit`，插件只需引用 UiKit。

### 改动

1. **CardTable**：`src/UiKit/CardDataControls.cs`。表头 + 虚拟化行（`ListCollectionView` + 回收式 `ListBox`），
   支持列宽、点击表头升/降序、单选 / 多选、双击 / 回车激活、空态、`ScrollToEnd`；行数据用 `CardTableItem`
   或自定义 `ItemsSource` + 列 `Value` / `SortKey`。
2. **CardBadge / CardTag / CardMetricTile**：同文件，统一走 `CardBadgeTone` 与主题令牌；指标块支持标题、
   主数值 + 单位、趋势、说明、图标。
3. **CardGauge / CardProgressRing**：同文件，`FrameworkElement` 自绘（`DrawingContext`），支持范围、格式、
   标签、单位与主题换色；不依赖宿主。
4. **SelfTest**：新增 `CardDataSelfTest`，覆盖表格排序 / 选中 / 清空 / 空态、徽标 / 标签 / 指标块 /
   仪表 / 圆环的布局与离屏渲染；宿主 `--selftest` 新增「数据控件」一行。
5. **版本**：宿主 / Contracts / UiKit / 6 个插件工程与 manifest 统一 2.17.0；插件 API 仍为 2（纯 UiKit 加法）。

### 验证

- `dotnet build MirrorpondHUD.csproj -c Release`：0 警告 0 错误；
- `--selftest`：失败项 0，新增「数据控件: OK（Table 排序 / 选中 / 空态；Badge / Tag / Metric / Gauge / Ring）」；
- 离屏预览：`bin/Release/data-controls-preview.png`（760×520）确认表格、指标块、徽标、仪表、圆环的深色主题观感；
- 6 个内置插件仍从 `plugins/cards` 加载成功。

### 风险 / 未验证

- 表头与纵向滚动条同时出现时，星号列宽会因滚动条占位产生几像素偏差；固定列宽不受影响；
- 表格只做基础文本单元格，没有单元格模板 / 编辑 / 列拖拽；需要时由插件在 `ItemsSource` 上自行扩展；
- 图表 / 树 / 时间线属于 2.18.0，浮层 / 交互属于 2.19.0，本版不包含；
- 未做真实键鼠操作（表头点击 / 多选拖拽）的人工验收，仅有布局与离屏渲染自检。

---

## 52. v2.18.0（`1e1c6ac`，分支 `feature/data-core`）

### 背景

2.17.0 补了表格、指标块、仪表和圆环，但历史趋势、进程树、命令 / 任务时间线仍只能由插件自己画 `Canvas`。
2.18.0 补齐第二优先的第二批：**图表 + 层级 / 时间线**，全部进 UiKit。

### 改动

1. **CardSparkline / CardLineChart / CardBarChart**：`src/UiKit/CardChartControls.cs`。
   `CardSparkline` 画无轴趋势线；`CardLineChart` 支持多序列、网格、Y 轴刻度、X 轴标签、图例和面积；
   `CardBarChart` 支持分组柱状、负值基线、圆角柱。全部用 `FormattedText`（含 DPI）+ 冻结 Pen/Brush，
   空数据 / NaN / Infinity / 极小尺寸安全。
2. **CardTree / CardTimeline**：`src/UiKit/CardTreeTimeline.cs`。CardTree 是主题化 TreeView
   （自绘 ▸/▾、选中 / 悬停背景、空态、顶层虚拟化）；CardTimeline 是虚拟化 ListBox，左侧时间、
   圆点 + 竖线、右侧标题 / 详情，支持语气色和双击 / 回车激活。
3. **SelfTest**：新增 `CardChartSelfTest`，覆盖空数据、NaN、Add/SetValues、AutoScale、树展开/选中、
   时间线 Count/ScrollToEnd 和离屏渲染；宿主 `--selftest` 新增「图表 / 树 / 时间线」一行。
4. **版本**：宿主 / Contracts / UiKit / 6 个插件工程与 manifest 统一 2.18.0；插件 API 仍为 2。

### 验证

- `dotnet build MirrorpondHUD.csproj -c Release`：0 警告 0 错误；
- `--selftest`：失败项 0，「图表 / 树 / 时间线: OK（Sparkline / Line / Bar / Tree / Timeline 离屏渲染）」；
- 离屏预览：`bin/Release/charts-preview.png`（780×680）确认深色主题下的折线、柱状、树、时间线观感；
- 6 个内置插件仍从 `plugins/cards` 加载成功。

### 风险 / 未验证

- 没有独立“成功色”令牌：`CardTimelineTone.Success` 复用 Accent，Warning 用前景色；
- `CardTree` 只保证顶层虚拟化，深层节点仍用默认面板；超大深层树性能未验证；
- 直接修改 live `Series` / `Values` List 不自动重绘，需要 setter / `SetValues` / `Add` / `ApplyStyle`；
- 图例超长截断不换行；X 轴标签按宽度抽稀，不保证每个点都有标签；
- 图表 / 树 / 时间线只做离屏渲染，未做真实鼠标展开 / 滚动 / 双击的人工验收。

---

## 53. 不在版本里的东西

- 验收脚本、样张与测量脚本（`cmd-e2e.ps1`、`drag-glass.ps1`、`measure-*.py`、`shots/` 等）都在仓库外的 `hud-test/` 里，**没有提交**。
- 发布：`main` 与标签已推送到 [github.com/CRITTIof/MirrorpondHUD](https://github.com/CRITTIof/MirrorpondHUD)；
  [v2.9.2 Release](https://github.com/CRITTIof/MirrorpondHUD/releases/tag/v2.9.2) 只有两个附件
  （`MirrorpondHUD-x64-requires-dotnet.exe`、`MirrorpondHUD-arm64-requires-dotnet.exe`，均需 .NET 8 桌面运行时，已自签名）；
  更早的 Release 正文与附件保留原样，v2.4.0 的 Release 已删除（tag `v2.4.0` 保留，历史仍可检出）。
- 仓库追踪的文件共 117 个，`bin/` `obj/` `publish/` `certs/`、`settings.json` / 日志、探针与一次性 PoC 工程都在 `.gitignore` 里，没有被追踪。

## 54. 已知代价（跨版本）

- 桌面亚克力为了不拍到自己，会把卡片排除在截屏之外 —— 这个模式下截图 / 录屏里**看不到卡片**，观感只能靠肉眼与离屏渲染验证。
- 兼容实现每帧对身后窗口做 `PrintWindow`，拖动时最多 ~21 帧/秒；要更高只能走桌面亚克力（DXGI）。
- CLI 面板宽度不再按文字长度自动量宽（这是 2.4.5 的取舍：两块面板共用一套尺寸）。
- 图标默认值从 1.45 改到 1.15，更早版本的旧配置会被迁移一次（2.14.7 起迁移层已删除，此条只适用于 2.14.6 及之前）；想更小 / 更大可在「外观 → 图标字号」调。
