# 版本修改记录（2.4.1 → 2.7.0）

> 范围：本文只记录**这一轮接手后我做的版本**（v2.4.1 起）。v2.4.0 及更早是既有历史，不在本文档里。
> 数据全部取自仓库真实提交（`git diff --shortstat <上一版>..<本版>`），文件行的 `+ / -` 就是实际增删行数。
> 版本状态：`main` 已推送到 GitHub；`v2.4.2`–`v2.4.5` 为附注标签（分别指向 `70343ef` / `aabe441` / `05857ce` / `f4b2b8b`），
> v2.4.5 已发布 Release（附件为两个 requires-dotnet 单文件），v2.4.0 的 Release 已删除、其 tag 保留。
> `v2.5.0` 为附注标签：**只在本地创建，尚未推送、也还没有 Release**。
> `v2.6.0`（schema 5）、`v2.6.1`（P1-3）与 `v2.6.2`（schema 6）目前只有提交、**都没有打标签**；建议手工回归后再发版。

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
| v2.7.0 | 2026-10-31 | 本次提交 | 阶段 B 收尾：`ICardHost` 宿主服务与 `ICardHostAware` 生命周期；`CardStateStore` 每卡状态存储（原子写 + 路径校验） | 7 文件，+220 / −23，另新增 2 个源文件（165 行） |

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

## 12. 不在版本里的东西

- 验收脚本、样张与测量脚本（`cmd-e2e.ps1`、`drag-glass.ps1`、`measure-*.py`、`shots/` 等）都在仓库外的 `hud-test/` 里，**没有提交**。
- 发布：`main` 与标签已推送到 [github.com/CRITTIof/MirrorpondHUD](https://github.com/CRITTIof/MirrorpondHUD)；
  [v2.4.5 Release](https://github.com/CRITTIof/MirrorpondHUD/releases/tag/v2.4.5) 只有两个附件
  （`MirrorpondHUD-x64-requires-dotnet.exe`、`MirrorpondHUD-arm64-requires-dotnet.exe`，均需 .NET 8 桌面运行时，已自签名）；
  v2.4.0 的 Release 已删除（tag `v2.4.0` 保留，历史仍可检出）。
- 仓库追踪的文件共 89 个，`bin/` `obj/` `publish/` `certs/`、`settings.json` / 日志、探针与一次性 PoC 工程都在 `.gitignore` 里，没有被追踪。

## 13. 已知代价（跨版本）

- 桌面亚克力为了不拍到自己，会把卡片排除在截屏之外 —— 这个模式下截图 / 录屏里**看不到卡片**，观感只能靠肉眼与离屏渲染验证。
- 兼容实现每帧对身后窗口做 `PrintWindow`，拖动时最多 ~21 帧/秒；要更高只能走桌面亚克力（DXGI）。
- CLI 面板宽度不再按文字长度自动量宽（这是 2.4.5 的取舍：两块面板共用一套尺寸）。
- 图标默认值从 1.45 改到 1.15，旧配置会被迁移一次；想更小 / 更大可在「外观 → 图标字号」调。
