# 外观主题方案（研究 + 建议）

> 目标：让「换一套外观」变成**替换一个文件**，而不是在设置页里逐个调二十几个字段。
> 范围边界：**主题只管静态的外观（颜色、字体、圆角、内边距、面板尺寸）**，不包含毛玻璃 / 桌面亚克力 ——
> 那是材质与取图实现（DXGI 桌面复制 / GDI 读屏 / DWM 底板），有 GPU、截屏排除、帧率与 CPU 开销，
> 属于「能力」而不是「外观」。同一个主题在有亚克力与没亚克力时都应该成立。

## 1. 现状盘点：外观参数散在哪

| 位置 | 内容 | 问题 |
|---|---|---|
| `settings.json` → `Style` | 25 个字段：字体、字号、加粗、前景 / 背景色、不透明度、内边距、行高、对齐、描边、背景图、固定宽度、卡片对齐、图标倍率、毛玻璃 4 项 | 外观和材质混在一起；没法整体导出 / 分享 |
| `settings.json` → `Overlay` | `CornerRadius`、`Shadow` | 圆角是外观，却不在 `Style` 里 |
| `settings.json` → `Cli` | `MaxWidth / MaxHeight / EntryMaxHeight / MaxEntries` | 面板尺寸属于主题的一部分 |
| `src/Ui/Theme.xaml` | 设置页自己的色板（`Accent`、`WindowBg`、`CardBg`、`BorderSoft`、`TextPrimary`、`TextSecondary`、`Danger`） | 与卡片外观无关，但换肤时容易漏掉 |
| `CliUi.cs` | 内部小面板底 `0.045`、描边 `0.10`、滑块拇指 `0.18 / 0.40`、状态行 `0.5`、危险色 `#E57B6E` | **硬编码**：换配色后仍然按旧前景色推导 |
| `CardTextBox.cs` | 输入框底 `0.07`、描边 `0.24`、选区 `0.35` | 同上 |
| `ChatCard.cs` / `CommandCard.cs` | 问题行 `0.72`、回答 `0.95` | 同上 |

结论：**颜色是「一个前景色 + 一堆写死的透明度」**。只换 `Foreground` / `Background` 两个值，其余灰阶会自动跟着走
（2.4.5 已经把所有派生灰阶收进了 `CardPalette`），但想让滑块、危险色、强调色各不相同就不行了。这是主题化要补的第一块。

## 2. 候选方案对比

| 方案 | 用户可读 / 可改 | 分享一个文件 | 需要重新编译 | 支持图片 / 字体 | 安全 | 实现成本 |
|---|---|---|---|---|---|---|
| A. 每个主题一个 XAML 资源字典 | 中（要懂 XAML） | 是（`.xaml`） | **是**（`pack://` 资源要编进去） | 是 | 低 | 高 |
| B. **单文件 JSON（推荐）** | 高 | 是（`.mptheme`） | 否 | 只有颜色 / 数值（图片另挂） | 高（只解析数据） | 低 |
| C. ZIP 包（`theme.json` + 资源） | 高 | 是（`.zip`） | 否 | 是（背景图 / 字体） | 中（要防路径穿越） | 中 |
| D. CSS 式变量 / 样式表 | 高 | 是 | 否 | 是 | 中（要写解析器） | 很高 |
| E. 只做「导出 / 导入 settings.json」 | 高 | 是（整个配置文件） | 否 | 是 | 高 | 极低 |

**推荐 B，并在第二步升级到 C 的目录结构**：

- B 是纯数据，解析失败可以整体回滚，不会把 WPF 资源字典注入进进程；
- `.mptheme` 就是 JSON，记事本可改、diff 友好、能贴进 issue 分享；
- C 只是在 B 的基础上把 `assets/`（背景图、字体）放进同名文件夹，主题文件里用相对路径引用；
- E 不能选：`settings.json` 里混着卡片内容、热键、API Key（DPAPI 密文）、位置、毛玻璃参数 —— 分享出去既泄露又误伤。
- A 不能选：XAML 主题必须编进程序集才能被 `pack://` 解析，等于每加一套主题就要发一次版本。

## 3. 主题文件格式草案（`.mptheme`，内容就是 JSON）

```json
{
  "schema": 1,
  "name": "午夜霓虹",
  "author": "MirrorpondHUD",
  "description": "深蓝底 + 冷白字，滑块更淡，适合深色壁纸",
  "appearance": {
    "colors": {
      "foreground": "#E6EDF3",
      "background": "#0D1117",
      "backgroundOpacity": 0.86,
      "accent": "#4C8DFF",
      "danger": "#E57B6E",
      "muted": 0.45,
      "border": 0.12,
      "panel": 0.045,
      "scrollbar": 0.18,
      "scrollbarHot": 0.40
    },
    "typography": {
      "fontFamily": "Microsoft YaHei UI",
      "monoFont": "Cascadia Mono, Consolas, Microsoft YaHei UI",
      "fontSize": 15,
      "bold": false,
      "iconScale": 1.15,
      "lineHeight": 0,
      "textAlign": "Left"
    },
    "shape": {
      "cornerRadius": 10,
      "padding": 14,
      "shadow": false
    },
    "outline": {
      "enabled": false,
      "color": "#000000",
      "thickness": 1.2
    },
    "cliPanel": {
      "maxWidth": 460,
      "maxHeight": 420,
      "entryMaxHeight": 140,
      "maxEntries": 12
    }
  }
}
```

### 字段映射（导入时写到哪）

| 主题字段 | 写入 | 备注 |
|---|---|---|
| `colors.foreground` / `background` / `backgroundOpacity` | `Style.Foreground` / `Background` / `BackgroundOpacity` | 现有字段 |
| `colors.accent` / `danger` | **新增** `Style.Accent` / `Style.Danger` | 现在 `#2F6FEB` 只在设置页、`#E57B6E` 硬编码在状态行 |
| `colors.muted` / `border` / `panel` / `scrollbar` / `scrollbarHot` | **新增** `Style.Muted` / `Border` / `Panel` / `Scrollbar` / `ScrollbarHot` | 把 `CliUi.cs`、`CardTextBox.cs`、`ChatCard.cs` 里的 0.45 / 0.10 / 0.045 / 0.18 / 0.40 变成可配；不填则按默认透明度推导 |
| `typography.fontFamily` / `fontSize` / `bold` / `iconScale` / `lineHeight` / `textAlign` | `Style.*` | 现有字段；`iconScale` 的语义在 2.4.5 改为「1.0 = 图标与文字等高」 |
| `typography.monoFont` | **新增** `Style.MonoFont` | 现在是 `CliPanelView` 里的常量 |
| `shape.cornerRadius` / `shadow` | `Overlay.CornerRadius` / `Overlay.Shadow` | 现有字段，属于外观 |
| `shape.padding` | `Style.Padding` | 现有字段 |
| `outline.*` | `Style.TextOutline` / `OutlineColor` / `OutlineThickness` | 现有字段 |
| `cliPanel.*` | `Cli.*` | 2.4.5 新增的四个字段 |

**故意不放进主题**：`Acrylic` / `GlassMode` / `GlassSource` / `GlassBlur` / `GlassFps`、卡片列表与内容、位置与显示器、
热键、`Behavior`（刷新率 / 启动动画）、`Data`、API Key。理由见开头：这些是能力与布署参数，不是外观；混进来会让
「套用主题」意外改掉性能与取图行为。

## 4. 加载、生效与回滚

```
选择 .mptheme ──► 读取 + 解析 JSON ──► schema 校验（只认 1，更高版本直接拒绝）
                ──► 逐字段校验（颜色 #RRGGBB / #AARRGGBB，数值范围与 Settings.Normalize 一致）
                ──► Clone 当前 Settings → 只覆盖 appearance 映射到的字段 → Normalize()
                ──► 应用到所有卡片窗口（与「保存并应用」同一条路径）
                ──► 失败：不改任何内存 / 磁盘状态，弹一行原因
```

- **导入**：菜单「外观 → 导入主题…」，文件对话框限 `*.mptheme;*.json`；
- **导出**：把当前 `Style` / `Overlay.CornerRadius` / `Overlay.Shadow` / `Cli` 写成一个 `.mptheme`（颜色用当前值），
  这样「调好一套再另存为主题」是零学习成本的；
- **热重载**（开发便利）：`MIRRORPOND_THEME_WATCH=1` 时监视当前主题文件，改动后 300 ms 内重新套用；
- **不落盘主题内容**：`settings.json` 只记 `Style.ThemeName`（用于设置页显示「当前主题」），实际值已经被展开写入，
  这样主题文件被删掉也不影响运行。

## 5. 优先级与冲突

1. 导入主题 = 覆盖外观字段；**卡片内容、位置、热键、毛玻璃设置一律不动**；
2. 「恢复默认」= 套用内置 `default.mptheme`（与出厂设置一致）；
3. 用户在设置页里手改外观字段后，`Style.ThemeName` 置空（显示「自定义」），避免显示成某个主题却已经不一致；
4. 主题里缺字段 = 保留当前值（增量套用），全部缺 = 等价于不改。

## 6. 示例主题

`docs/themes/` 下三份草案，可以直接作为模板改：

| 文件 | 观感 |
|---|---|
| `midnight.mptheme` | 深蓝黑底 + 冷白字，最接近当前默认 |
| `paper.mptheme` | 浅米底 + 墨黑字，日间 / 浅色壁纸 |
| `terminal-green.mptheme` | 近黑底 + 荧光绿字，复古终端 |

## 7. 分阶段落地

| 阶段 | 内容 | 规模 |
|---|---|---|
| 1（建议 2.4.6） | 新增 `Accent / Danger / Muted / Border / Panel / Scrollbar / ScrollbarHot / MonoFont` 字段；`CliUi.cs`、`CardTextBox.cs`、`CliPanelView` 改读这些字段；`.mptheme` 解析器 + 导入 / 导出两个按钮 | 约 300 行，无新依赖 |
| 2 | ZIP 包（`theme.json` + `assets/`，背景图 / 字体），相对路径 + 路径穿越校验 | 约 120 行 |
| 3 | 主题预览（套用前先在设置页里渲染一张缩略卡片）、主题目录扫描 | 约 150 行 |

风险与对策：

- **字体不存在**：`FontFamily` 回退到 `Microsoft YaHei UI` / `Consolas`（现在 `CardPalette.ResolveFont` 已经这么做了）；
- **浅色主题的可读性**：状态行 / 副文字用 `muted` 透明度，浅色底上 0.45 会偏淡，导入后可以在设置页里再微调；
- **滑块在浅色底上太淡**：`scrollbar` 默认 0.18 是按深色底调的，浅色主题建议写 0.28 左右（示例 `paper.mptheme` 就是 0.30）；
- **主题文件损坏**：整体回滚，不动已保存设置。

## 8. 为什么不做成「亚克力主题包」

用户明确要求「不把已有亚克力毛玻璃当作主题」，这条在技术上也是对的：

- 毛玻璃是**取图 + 模糊**的实现选择（DWM 底板 / DXGI 桌面复制 / GDI 读屏 / 兼容 PrintWindow），2.4.4 实测 4 张卡在
  10 帧/秒下约占单核 6–8%；把它塞进主题文件，等于「换个配色顺手把 CPU 开销也换了」；
- 桌面亚克力还会把卡片**排除在截屏之外**（截图 / 录像里看不到卡片），这是隐私与录制行为，不该由外观文件决定；
- 同一套配色在「无背景 / 纯色 / 毛玻璃 / 背景图」四种材质下都应该成立，所以主题只描述颜色与几何，材质单独在设置里选。
