# 版本修改记录（2.4.1 → 2.4.5）

> 范围：本文只记录**这一轮接手后我做的版本**（v2.4.1 起）。v2.4.0 及更早是既有历史，不在本文档里。
> 数据全部取自仓库真实提交（`git diff --shortstat <上一版>..<本版>`），文件行的 `+ / -` 就是实际增删行数。
> 所有版本都在本地 `main` 上，**没有推送**；`v2.4.1` 标签仍指向 `82a8f80`，之后的版本没有打标签。

## 0. 总览

| 版本 | 日期 | 提交 | 主题 | 改动规模 |
|---|---|---|---|---|
| v2.4.1 | 2026-10-25 | `40be56b` `76c9fa0` `39c2708` `82a8f80` | AI 对话卡改终端样式；API Key 改用 DPAPI；切窗口时玻璃跟手；视频窗也排除出截屏 | 15 文件，+1236 / −242 |
| v2.4.2 | 2026-10-25 | `1e589d0` `70343ef` | 玻璃取图改用 DXGI 桌面复制（GPU）；同期重画改同步、相同帧不重画 | 5 文件，+852 / −7 |
| v2.4.3 | 2026-10-28 | `aabe441` | 取图方式进设置（GPU / CPU）；修掉判同帧后 DIB 留未模糊原图导致的闪烁 | 9 文件，+269 / −22 |
| v2.4.4 | 2026-10-28 | `05857ce` | 模糊图改画进卡片内部，删掉底板窗口（修圆角外扩 / 顶部暗带）；模糊强度 / 帧率 / 取图方式进设置 | 9 文件，+1026 / −1503 |
| v2.4.5 | 2026-10-30 | `f4b2b8b` | AI 对话与命令行统一成 CLI 交互面板（每条记录独立小面板与滑块）；图标按墨迹居中；拖动时玻璃跟手；主题方案文档 | 23 文件，+1647 / −668 |

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

## 6. 不在版本里的东西

- 验收脚本、样张与测量脚本（`cmd-e2e.ps1`、`drag-glass.ps1`、`measure-*.py`、`shots/` 等）都在仓库外的 `hud-test/` 里，**没有提交**。
- 全程没有 `git push`；`v2.4.2` 起也没有打新标签。

## 7. 已知代价（跨版本）

- 桌面亚克力为了不拍到自己，会把卡片排除在截屏之外 —— 这个模式下截图 / 录屏里**看不到卡片**，观感只能靠肉眼与离屏渲染验证。
- 兼容实现每帧对身后窗口做 `PrintWindow`，拖动时最多 ~21 帧/秒；要更高只能走桌面亚克力（DXGI）。
- CLI 面板宽度不再按文字长度自动量宽（这是 2.4.5 的取舍：两块面板共用一套尺寸）。
- 图标默认值从 1.45 改到 1.15，旧配置会被迁移一次；想更小 / 更大可在「外观 → 图标字号」调。
