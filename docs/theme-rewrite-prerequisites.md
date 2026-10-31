# 外观重写的前置

> 范围：把「深度重写外观」拆成**可独立验收**的前置步骤。本文只谈工程前置与契约，不定最终视觉。
> 基线：v2.8.1。关联：[theme-system.md](theme-system.md)（`.mptheme` 草案）、[theme-motion.md](theme-motion.md)（动效调研）、
> [modularity-review.md](modularity-review.md) §9.2（外观重写的冲突点）。

## 0. 一句话结论

外观重写的难点不在“换颜色”，而在三件事：**令牌没有单一来源**、**CLI 面板不能就地换肤**、**没有主题文件的读写与回滚**。
这三件不先做，重写会把硬编码搬到一个更大的文件里。

## 1. 现状盘点

| 项 | 现状 | 位置 |
|---|---|---|
| 外观字段 | `StyleSettings` 有 **25 个** 字段（字体 / 颜色 / 描边 / 背景图 / 毛玻璃 / 圆角 / 对齐…） | `src/Core/Settings.cs` |
| 消费面 | `grep -rl "StyleSettings\|\.Style\." src` 命中 **12 个文件**（含 `Settings.cs` 定义与 `SelfTest`）；实际消费 10 个：`OverlaySurface` / `OverlayWindow` / `SettingsWindow` / `CardButton` / `CardTextBox` / `CliUi` / `MediaView` / `ButtonsCard` / `OverlayController` / `SettingsMigration` | `grep -rln` |
| 硬编码颜色 | `CardButton.ThemeAccent = "#2F6FEB"`（强调色回退）、`Theme.xaml` 的 `Accent / Danger / TextSecondary` | `CardButton.cs:149`、`Theme.xaml` |
| 派生灰阶 | `CardPalette` 只按「前景色 + alpha」派生（0.18 / 0.28 / 0.45 / 0.95…），没有独立令牌 | `src/Ui/Controls/CliUi.cs` |
| CLI 面板 | `CliPanelView` 的 `Foreground` / `Corner` / `Pad` / `FontSize` 是 **readonly**，配色在构造期写到标题 / 记录 / 输入 / 状态 / 滚动条上 | `src/Ui/Controls/CliUi.cs:208+` |
| 换肤能力 | 签名已拆内容 / 样式两维（v2.8.0）；`ICardStyleAware` 目前只有 `MediaCard` 实现 | `modularity-review.md` §8.2 |
| 主题文件 | 只有三份示例（`docs/themes/*.mptheme`），**没有解析器、没有导入 / 导出、没有回滚** | `docs/theme-system.md` 是方案稿 |
| 设置页 | 只有「外观」参数页，没有「主题」入口与预览 | `src/Ui/SettingsWindow.xaml` |

## 2. 前置要解决的 5 件事

### P1 令牌层（单一来源）

**目标**：颜色 / 字体不再靠「前景色 + alpha」猜，也不再散落硬编码。

**改动**：

- 新增 `ThemeTokens`（8 个令牌，见 §3），由 `ThemeResolver.Resolve(StyleSettings)` 计算：
  字段为空时**按现有规则派生**（保证旧配置外观完全不变），填了就用填的。
- `CardPalette` 改为优先取令牌：`Accent` / `Danger` / `Muted` / `Border` / `Panel` / `Scrollbar` / `ScrollbarHot` / `MonoFont`；
  没有令牌时才回退到旧的 alpha 派生。
- 替换两处硬编码：`CardButton.ThemeAccent`、`Theme.xaml` 的 `Accent`（设置窗口预览与卡片按钮共用同一令牌）。
- `Settings.Normalize` 校验令牌是合法 `#RRGGBB`，非法值回落默认（不回写，保持配置干净）。

**验收**：旧配置打开后逐像素与 v2.8.1 一致（令牌全部走派生）；只改 `Accent` 时按钮 / 滑块热区 / 选中态变，其他不变。

**状态（v2.9.0）**：✅ 已实现。`ThemeTokens` / `ThemeResolver` 在 `src/Ui/ThemeTokens.cs`；
`CardButton` 的 Accent/Danger、`CliScrollBars`（常态 / 悬停）、`CliPanelView` / `CliEntryPanel` 的
Panel / Border / Muted 都已改读令牌。`Theme.xaml`（设置窗口浅色 chrome）留到 P4 一起做，
`MonoFont` 令牌留到 P2 接线（CLI 面板字体是实例级改造）。

**风险**：低。纯读取路径改动，不碰布局与窗口。

### P2 CLI 面板可换肤（`ApplyStyle` 补全）

**目标**：换主题 / 字号 / 圆角时，**AI 对话与命令行卡片不重建**——否则对话历史、命令记录全丢。

**改动**：

- `CliPanelView` 的 `Foreground` / `Corner` / `Pad` / `FontSize` 由 `readonly` 改为可变，
  新增 `public void ApplyStyle(StyleSettings style, CliSettings cli)`：重新套用标题、徽标、提示符、状态行、每条记录面板、
  输入框（`CardTextBox`）与滚动条（`CliScrollBars.Apply`）。
- `ChatView` / `CommandView` 保持自己的消息 / 记录列表（它们本来就在字段里，不重建就不会丢）。
- `ChatCard` / `CommandCard` 实现 `ICardStyleAware`，把宿主的分支从“回退重建”切到“就地换肤”。

**验收**：对话里先问几轮 → 改字号 / 强调色 → 应用：历史还在、输入框还是同一个、滚动条颜色跟着变。

**状态（v2.9.1）**：✅ 已实现。`CliPanelView.ApplyStyle(style, cli)` 重套标题 / 徽标 / 分隔线 / 输入 / 状态 / 滚动条 /
记录区高度；`CliEntryPanel.ApplyStyle` 重套内部面板与已有文字（每段文字的 alpha 与字号偏移存在 `Tag` 里）。
`ChatCard` / `CommandCard` 已实现 `ICardStyleAware`，宿主走「只样式变 → 就地换肤」。
**未做**：`MonoFont` 令牌还没变成实例级字体，换等宽字体仍需要重建。

**风险**：中。`CliUi.cs` 是 500 行的公共件，子控件多；需要逐个确认“哪些颜色是构造期写的”。

### P3 主题文件（`.mptheme`）

**目标**：外观可以存成文件、导入导出、坏了能回滚。

**改动**：

- 解析：JSON → `ThemeDocument`（`formatVersion` + `name` + 令牌 + 允许覆盖的 `Style.StyleSettings` 字段子集 + 字体 + 圆角）。
- 校验：颜色格式、数值范围、未知键忽略、`formatVersion` 不高于当前支持；路径穿越与 `assets/` 相对路径校验（ZIP 阶段）。
- 导入流程：读文件 → 解析校验 → **应用到副本** → 设置页预览 → 用户确认 → 落盘；任一步失败整体回滚，不动已保存设置。
- 导出：把当前 `Style` + 令牌写成 `.mptheme`，字段顺序稳定（便于 diff）。
- 与 schema 的关系：`.mptheme` 有自己的 `formatVersion`，**不参与** `settings.json` 的 `SchemaVersion`；导入只是往 `Style` 写值。

**验收**：导入 `docs/themes/midnight.mptheme` 与 `paper.mptheme` 外观正确切换；导入一个坏文件报错且当前外观不变；导出再导入得到同样的外观。

**风险**：中。字段映射表要与 `theme-system.md` 的草案对齐，避免两份格式。

### P4 设置页「主题」入口 + 预览

**目标**：不靠改 JSON。

**改动**：

- 外观页顶部加「主题」区：内置主题列表 + 导入 / 导出 / 还原默认。
- 预览：复用离屏渲染（`Screenshots.CaptureCards` 的同一套 `OverlaySurface` + `TextCanvas`），
  不弹真窗口、不动用户当前叠层。
- 选中主题只改 `_working` 副本；点「保存并应用」才生效（与现有设置页一致）。

**验收**：选主题 → 预览变化 → 取消 → 外观完全不变。

**风险**：低。复用既有预览路径。

### P5 自检与验收自动化

**目标**：换肤这类“看起来对”的改动要有回归网。

**改动**（`--selftest` 增项）：

- 令牌完整性：`ThemeResolver` 对空 / 非法 / 自定义值都有确定结果；旧默认下令牌等于派生值。
- 换肤不重建：`MediaCard` / `ChatCard` / `CommandCard` 都实现 `ICardStyleAware`；
  `CliPanelView.ApplyStyle` 可重复调用且不抛异常。
- 主题文件往返：`ThemeDocument` 序列化 → 反序列化 → 字段一致；坏输入被拒绝。

**验收**：`--selftest` 失败项 0，且新增三项各自有独立结果行。

**风险**：低。

## 3. 令牌表

| 令牌 | 类型 | 默认 | 用途 | 回退（为空时） |
|---|---|---|---|---|
| `Accent` | `#RRGGBB` | `#2F6FEB` | 主操作按钮、选中态、滑块热区 | 现有硬编码 |
| `Danger` | `#RRGGBB` | `#D14343` | 危险按钮 / 错误文字 | `Theme.xaml` 的 Danger |
| `Muted` | `#RRGGBB` + alpha | 前景 0.45 | 时间戳、提示、次要文字 | `CardPalette` 0.45 派生 |
| `Border` | `#RRGGBB` + alpha | 前景 0.12 | 面板 / 输入框描边 | 旧派生值 |
| `Panel` | `#RRGGBB` + alpha | 前景 0.05 | 内部小面板底色 | 旧派生值 |
| `Scrollbar` | `#RRGGBB` + alpha | 前景 0.18 | 滚动条常态（浅色主题建议 0.28） | 旧派生值 |
| `ScrollbarHot` | `#RRGGBB` + alpha | 前景 0.28 | 滚动条悬停 / 拖动 | 旧派生值 |
| `MonoFont` | 字体名 | `Cascadia Mono, Consolas` | CLI 等宽字体 | `CliPanelView.MonoFont` |

> 设计原则：**令牌为空 = 保持 v2.8.1 的观感**。这样令牌层可以先合入而不改任何视觉，主题重写再逐个填。

## 4. 兼容与回滚

- **字段新增不改 schema 版本**：全部带默认 / 派生值，旧配置读入即用；`SchemaVersion` 只在“结构语义变化”时升
  （例如以后真的删掉某个 `Style` 字段）。
- **主题导入不回写未知键**：`ThemeDocument` 只写它认识的字段，配置里其它内容（卡片、热键、毛玻璃选择）不受影响。
- **回滚**：`ConfigStore.Save` 已经会留 `settings.json.bak`；导入失败时不调用保存，界面上的 `_working` 副本丢弃即可。
- **材质不归主题管**：毛玻璃是取图 / 合成实现（DWM / DXGI / GDI / 兼容），不是配色，继续留在「高级（实验性）」里
  （理由见 `theme-system.md` §8）。

## 5. 建议的实施顺序

| 版本 | 内容 | 前置依赖 |
|---|---|---|
| 2.9.0 | P1 令牌层（只读派生 + 替换两处硬编码）+ P5 令牌自检 | 无 |
| 2.9.1 | P2 `CliPanelView.ApplyStyle` + 两张 CLI 卡片实现 `ICardStyleAware` | P1 |
| 2.9.2 | P3 `.mptheme` 解析 / 导出 / 校验 + 往返自检 | P1 |
| 2.9.3 | P4 设置页主题入口 + 离屏预览 | P2、P3 |

这样每一步都能独立交付：P1 不改观感，P2 不改配置格式，P3 不做 UI，P4 才把三块拼起来。

## 6. 验收清单（手工，最终外观重写的“完成”定义）

1. 旧配置打开：与 v2.8.1 逐像素一致（令牌全派生）。
2. 只改 `Accent`：按钮、滑块热区、选中态变；文字、背景、圆角不变。
3. 对话里问两轮 → 改字号 / 主题 → 应用：**历史保留**、滚动条与输入框跟着变。
4. 导入 `midnight.mptheme` / `paper.mptheme`：卡片与设置页同时变；取消则完全还原。
5. 导入损坏文件：提示错误，当前外观不变，`settings.json` 不被动。
6. 导出 → 清空 → 导入：外观与导出前一致。
7. `--selftest`：失败项 0（令牌、换肤、主题往返三项均为 OK）。

## 附录：仓库历史文件审计（2026-10-31）

> 目的：确认所有历史版本里没有夹带与程序无关的文件，给外观重写一个干净的基线。

| 检查项 | 结果 |
|---|---|
| 当前跟踪文件 | 100 个；`src/` 55 个 `.cs` + 4 个 XAML，`docs/` 文档与截图，`assets/` 图标与字体，根目录工程 / 脚本 |
| 构建产物 / 日志 / 证书 / 机器配置 | **从未提交**（`bin/` `obj/` `publish/` `certs/` `*.user` `settings.json` `mirrorpond.log` 全部在 `.gitignore` 或历史里查无此文件） |
| 敏感文件 | 未发现密钥 / 明文 API Key / 私钥被提交（密钥只在运行时生成并 DPAPI 加密） |
| 非编译文件（当前） | `tools/make_logo.py`（把 SVG 几何渲染成多尺寸 `.ico` 的开发脚本）、`docs/*.png`（文档截图）、`docs/themes/*.mptheme`（主题格式示例） |
| 历史大文件 | 仅文档截图，最大 `docs/media-cards.png` 778 KB；无大二进制累积 |
| 历史删除的文件 | `WindowOverlay.csproj`（v2.0.0 改名）、四张旧文字卡模块（合并成 `InfoTextCard`）、`AcrylicBackdrop.cs`（被 `GlassBackdrop` 取代）、`docs/glass-options.md`、`docs/liquid-glass.md`（调研笔记，后来进 `.gitignore`）——都属于被取代的旧实现，不是垃圾 |
| 断链 | 被删的图片（`command-line.png` / `settings-command.png` / `intro-cursor.png`）在 README 与文档里已无引用 |
| 旧标签里的“遗留” | `v1.3.0` / `v1.4.0` 含旧工程名；`v2.0.0`–`v2.1.1` 含当时的研究笔记与旧卡片模块。这是不可变历史，**不建议**为此改写历史（需要 `filter-repo`，会改变所有提交哈希） |

**建议**（不影响功能）：

1. README 的「目录结构」补一行 `tools/`，说明它是开发脚本、不参与构建——避免以后被当成遗留文件误删。
2. 如果将来要发布源码包，可以用 `git archive` 排除 `docs/` 截图；主仓库不必清理。
