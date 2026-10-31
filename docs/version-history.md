# 版本修改记录（2.4.1 → 2.9.6）

> 范围：本文只记录**这一轮接手后我做的版本**（v2.4.1 起）。v2.4.0 及更早是既有历史，不在本文档里。
> 数据全部取自仓库真实提交（`git diff --shortstat <上一版>..<本版>`），文件行的 `+ / -` 就是实际增删行数。
> 版本状态：`main` 已推送到 GitHub；`v2.4.2`–`v2.4.5` 为附注标签（分别指向 `70343ef` / `aabe441` / `05857ce` / `f4b2b8b`），
> v2.4.5 已发布 Release（附件为两个 requires-dotnet 单文件），v2.4.0 的 Release 已删除、其 tag 保留。
> `v2.5.0`–`v2.9.2` 均为附注标签，**已随 2.9.2 的推送同步到远端**（`v2.6.0` = `9c1bafa`、`v2.6.1` = `d3ac63d`、
> `v2.6.2` = `1b138e7`、`v2.9.2` = `754115e`）；`v2.4.1` 也已补推。`v2.9.3`–`v2.9.6` 目前只在本地创建，尚未推送。

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
| v2.9.6 | 2026-10-31 | 本次提交 | 源码遗留 S3+S4：删死字段 `_live`；两个 `async void` 加顶层兜底，异常不再逸出崩进程 | 7 文件，+82 / −12 |

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

## 22. 不在版本里的东西

- 验收脚本、样张与测量脚本（`cmd-e2e.ps1`、`drag-glass.ps1`、`measure-*.py`、`shots/` 等）都在仓库外的 `hud-test/` 里，**没有提交**。
- 发布：`main` 与标签已推送到 [github.com/CRITTIof/MirrorpondHUD](https://github.com/CRITTIof/MirrorpondHUD)；
  [v2.9.2 Release](https://github.com/CRITTIof/MirrorpondHUD/releases/tag/v2.9.2) 只有两个附件
  （`MirrorpondHUD-x64-requires-dotnet.exe`、`MirrorpondHUD-arm64-requires-dotnet.exe`，均需 .NET 8 桌面运行时，已自签名）；
  更早的 Release 正文与附件保留原样，v2.4.0 的 Release 已删除（tag `v2.4.0` 保留，历史仍可检出）。
- 仓库追踪的文件共 107 个，`bin/` `obj/` `publish/` `certs/`、`settings.json` / 日志、探针与一次性 PoC 工程都在 `.gitignore` 里，没有被追踪。

## 23. 已知代价（跨版本）

- 桌面亚克力为了不拍到自己，会把卡片排除在截屏之外 —— 这个模式下截图 / 录屏里**看不到卡片**，观感只能靠肉眼与离屏渲染验证。
- 兼容实现每帧对身后窗口做 `PrintWindow`，拖动时最多 ~21 帧/秒；要更高只能走桌面亚克力（DXGI）。
- CLI 面板宽度不再按文字长度自动量宽（这是 2.4.5 的取舍：两块面板共用一套尺寸）。
- 图标默认值从 1.45 改到 1.15，旧配置会被迁移一次；想更小 / 更大可在「外观 → 图标字号」调。
