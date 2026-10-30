# 主题的动效与控件样式：实现调研

> 这份文档研究的不是「再加几个设置项」，而是把**主题**当成包四层的整体：
>
> | 层 | 内容 | 现状 |
> | --- | --- | --- |
> | 启动动画 | 欢迎语打字 / 退格、光标样式、入场节奏 | 有，但参数写死在代码里 |
> | 基本外观 | 颜色、字体、字号、圆角、内边距、描边、背景图、宽度 | 有，散在 `Style` / `Overlay` / `Cli` |
> | 特殊外观 | 面板、按钮、滑块、输入框、滚动条这些控件的样式 | 有，但只有「跟随前景色推导的灰阶」，没有第二个形状 |
> | 行为动画 | 非常驻卡片的出现 / 消失、下方卡片让位 | **没有**，现在是瞬切 |
>
> 分工：**静态外观的字段映射与 `.mptheme` 文件格式见 [theme-system.md](theme-system.md)**（那份文档已经把颜色 / 字体 /
> 形状 / 面板尺寸的映射、导入导出、回滚写完了）。本文只补它没覆盖的部分：启动动画、控件样式、行为动画，
> 以及为了让这三样能跑，主题文件需要长出什么样的第二层结构。两份文档合起来才是一套完整的主题。

## 0. 结论先行

1. **出现 / 消失不要用 `Window.Opacity`，也不要逐帧移动窗口**。卡片是分层窗口，整窗 alpha 每帧都要重传整张位图
   （实测 320×72 每帧 2.81 ms、460×420 每帧 3.13 ms，和「只动内容」的 2.40 / 3.75 ms 同一量级，而且它只能淡、不能位移）；
   正解是**窗口先到位、只平移窗口内的整块内容 + 内容不透明度**，玻璃底板跟着窗口走、一帧都不用重采样。
2. **让位（下方卡片下移）只能靠逐帧移动窗口**，而 `OverlayWindow.ApplyPosition()` 里挂着 `CaptureGlass()`，
   也就是说「移动」在开了毛玻璃时等于「逐帧重采样玻璃」。兼容实现拖动时实测只有 21 帧/秒，所以让位动画必须
   配套**限帧 + 动画期间玻璃降级**，否则会从「顺滑让位」变成「卡顿 + CPU 飙高」。
3. **控件样式走「token + 受控模板注入」，不要做 XAML 主题字典**。本项目的按钮模板是运行时拼字符串解析出来的、
   CLI 面板是 C# 里 `new` 的，资源字典换肤根本够不着它们；而模板注入等于让主题文件带着可执行结构进来，
   必须白名单 + 默认关闭（XAML 官方文档对「不受信任的 XAML」定性是「当作不受信任的代码」）。

按这三条，主题第二层（`motion` / `controls` / `skin`）的实现量大约 **1200–1500 行**，分三个阶段，第一阶段可以
不碰模板、只做参数化 + 出现动画，约 450 行。

## 1. 范围与判据

判据（用来砍需求）：一个「主题」换完应该满足

- **整体一致**：四层都换，不会出现「配色换了、入场还是老样子」；
- **不换能力**：不动毛玻璃实现、取图方式、刷新率、热键、卡片内容 —— 这些是部署与性能参数（theme-system.md 已经这么定）；
- **可回滚**：套用失败一个字都不改，套用成功可一键回默认；
- **可分享**：一个文件带走，不依赖本机字体 / 图片以外的东西；
- **可降级**：系统关了动画、卡多了、玻璃开着时自动变朴素，而不是变卡。

明确**不在**这份调研里：卡片内容模板、布局（堆叠顺序 / 间距 / 对齐算法）、毛玻璃材质、设置页改版。

## 2. 硬约束（都来自现有代码）

### 2.1 每张卡一个分层窗口，整棵树走软件渲染

`AllowsTransparency = true` 的窗口在 WPF 里是**软件渲染**的，WPF 团队自己的说法是「窗口视觉树里的每一样东西都会
用软件渲染…积累起来很快」「请把它们做小」（[transparent windows in WPF… please make them small](https://learn.microsoft.com/en-in/archive/blogs/jaimer/transparent-windows-in-wpf-please-make-them-small)）。
这决定了：

- 动画面积越大越贵，**能只动内容就不要动整窗**，能裁小就不要铺满；
- 60 帧 / 秒是可能的（实测 5 张卡就是 60），但每帧 2–4 ms 是实打实的，不能同时开三四个动画；
- 硬件加速指望不上，`BitmapCache` 这类软件缓存手段反而有效（见 §4）。

### 2.2 位置由 `LayoutCards()` 统一算，窗口尺寸 = 内容尺寸

`OverlayController.LayoutCards()` 从锚点往下按 `Spacing` 累加，每张卡的 `X/Y` 写进 `slot.Dialog.Overlay` 再
`ApplyPosition()`。窗口高度来自内容（`CardHeight`，减去 10 DIP 的投影预留）。所以「让位」不是改一个容器的
`Canvas.Top`，而是**改 N 个窗口的坐标**；而「入场滑入」如果要整块卡滑动，就得给窗口留出滑动余量（否则内容会被
窗口边界裁掉）。

### 2.3 `ApplyPosition()` 里挂着玻璃重采样

```csharp
Left = left;
Top = top;
UpdateBackdrop();

if (_settings.Style.Acrylic) CaptureGlass();   // ← 每次移动都抓一次
```

兼容实现的 `CaptureGlass()` 是对身后每个窗口做 `PrintWindow` 再模糊；桌面亚克力是 `GlassFeed.RequestFrame()`。
这就是 2.4.5 里「拖动时 21 帧/秒（兼容）/ 34.8 帧/秒（DXGI）」的来源。**让位动画 = 逐帧移动 = 逐帧重采样**，
这是本文最重要的一条成本约束。

### 2.4 交互卡片与点击穿透

`UpdateClickThrough()` 靠 `WS_EX_TRANSPARENT` 决定「锁定时穿透」；交互卡片（AI 对话 / 命令行 / 按钮演示）本来就不穿透。
动画期间如果窗口在动，**命中测试会跟着窗口走**，鼠标可能点在空中或点到桌面；所以交互卡片的动画只能动内容不能动窗口，
或者动画期间临时把窗口设为不接收鼠标。

### 2.5 文本是自绘的，富内容是代码构建的

- `TextCanvas : FrameworkElement` 用 `FormattedText` 在 `OnRender` 里画文字和光标 —— 它的「打字动画」本质是
  **每 20 ms 改一次 `Text` 然后重画**，不是 `Storyboard`；
- `CliUi.cs` 里的 `CliEntryPanel` / `CliPanelView` 都是 `new` 出来的；
- `CardButton` / `CardTextBox` 的模板是 `XamlReader.Parse` 拼字符串生成的。

结论：**动画只能走「自己插值 + 每帧重画」这条路，样式注入只能走「运行时替换 Template / 改 token」这条路**，
指望 WPF 主题资源字典或 `Storyboard` 一统到底是不成立的。

## 3. 现状盘点

| 层 | 代码位置 | 现在有什么 | 缺什么 |
| --- | --- | --- | --- |
| 启动动画 | `OverlayController.BeginIntro/IntroTick/FinishIntro`（`_introTimer` 20 ms）、`TextCanvas.DrawCursor` | 4 个相位：欢迎语打字 → 停留 650 ms → 退格 → 内容打字；速度 = `Behavior.TypeSpeedMs`；光标 5 种样式 + 闪烁；只播第一张非交互卡 | 时长 / 停留 / 延迟 / 缓动全写死；只有一种形态；没有「每张卡依次入场」；没有淡入 / 滑入 |
| 基本外观 | `StyleSettings`（25+ 字段）、`OverlaySettings.CornerRadius/Shadow`、`CliSettings` | 见 theme-system.md §1 | `Accent / Danger / Muted / Border / Panel / Scrollbar / MonoFont` 这些 token（theme-system.md 阶段 1 已列出） |
| 特殊外观 | `CardButton`（3 语气 × 固定圆角底 + 描边）、`CardTextBox`（底 0.07 / 描边 0.24）、`CliUi.CliEntryPanel`（底 0.045 / 描边 0.10）、`CliScrollBars`（宽 7、拇指 0.18 / 0.40）、`Theme.xaml`（设置页 8 个画刷 + 7 处内联十六进制） | 全部硬编码透明度，按前景色推导 | 形状维度（填充 / 描边 / 幽灵）、密度、滑块粗细与拇指形状、面板层级与缩进、模板替换、设置页换肤 |
| 行为动画 | `OverlayController.ShowCard/HideCard/LayoutCards` | `Opacity=0 → Show → Opacity=1`，位置直接写目标值 | 出现 / 消失动画、让位动画、打断规则 |

另外两处**已经踩过的坑**值得提前避开：

- `OnSnapshot` 在启动动画期间会跳过内容刷新（`_phase != Live` 时只更新布局尺寸），不然打字动画会被数据刷新打断；
  新的行为动画要沿用同样的闸门，否则出现动画期间来一帧 CPU 数据就会「跳」一下；
- `ScheduleLayout()` 用 `DispatcherPriority.Render` 批量重排，动画期间每帧都会触发它 —— 必须让动画走**独立的插值
  状态**，而不是让 `LayoutCards()` 每帧直接写目标值。

## 4. 实测：分层窗口上几种动画方式的成本

**方法**：仓库外写的 PoC（`%TEMP%\hud-motion-poc`，不提交），5 个 `AllowsTransparency` 窗口，两种尺寸
（320×72 与 460×420），`CompositionTarget.Rendering` 驱动 150 帧，`Process.TotalProcessorTime` 差值算 CPU，
每次模式跑一遍、共 3 遍取中位；测量期间关掉 HUD。环境：Windows 11 26200、DPI 100%、`ClientAreaAnimation = True`。

| 方式 | 每帧 CPU 320×72 | 每帧 CPU 460×420 | 220 ms 入场（≈13 帧） | 帧率 | 备注 |
| --- | --- | --- | --- | --- | --- |
| 内容平移 + 内容不透明度 | 2.40 ms | 3.75 ms | 31 / 49 ms | 60 | 窗口不动 → **玻璃不重采样** |
| 同上 + `BitmapCache` | 1.88 ms | 2.29 ms | 24 / 30 ms | 60 | 大卡省 40%，小卡省 22% |
| 逐帧移动窗口（`Left/Top`） | 1.04 ms | 1.98 ms | 16 / 34 ms | 76–84 | 只是移动本身；真实成本还要加重采样 |
| 整窗 `Opacity` 淡入 | 2.81 ms | 3.13 ms | 37 / 41 ms | 60 | 分层位图每帧重传：小卡比动内容贵、大卡略便宜，但**只能淡、不能位移** |
| 静止基线（不变量） | 3.54 ms* | 1.15 ms* | — | 32 / 57* | *静止时 WPF 按需重绘，帧率与样本都偏低，只当参考 |

读数说明与局限（重要）：

- 单机、单次会话、3 遍取中位，**同一模式不同遍可以差 ±40%**（`window-move-big` 三次分别 34 / 15 / 63 ms），
  所以只能用来**排序**，不要当绝对预算；
- 「220 ms 入场」是按实测帧率折算的整段 CPU 毫秒，不是 wall time；
- 真实卡片还要叠上玻璃（§2.3）、视频子窗、背景图，PoC 里都没有；
- 唯一稳定的结论是：**移动窗口不重绘内容是最便宜的；整窗 alpha 不便宜；内容重绘居中，且面积越大越贵**。

由此定策略：**出现 / 消失 = 动内容**（窗口已经在最终位置），**让位 = 移动窗口但限帧 + 玻璃降级**。

## 5. 主题文件 v2

`theme-system.md` 定义的 `schema: 1` 只有 `appearance`。v2 在同一个文件里长三块新内容，全部**可选**（缺失 = 用默认值，
行为和今天一模一样）：

```json
{
  "schema": 2,
  "name": "午夜霓虹 · 动效版",
  "appearance": { "...": "见 theme-system.md §3，字段完全不变" },
  "motion":  { "...": "§6 / §9" },
  "controls": { "...": "§8" },
  "skin":    { "...": "§8.5" }
}
```

### 5.1 加载规则

- `schema: 1` 仍可导入（缺的三块按默认），**写出去一律 `schema: 2`**；
- 三块里任何一块校验失败 → **整份主题回滚**（沿用 theme-system.md §4 的策略，不做「半套用」）；
- `motion` / `controls` / `skin` 三块互不依赖：可以只带 `motion` 分享一个「只改动效」的主题；
- 每块内部再按字段做「缺省即不改」，方便增量覆盖。

## 6. 启动动画

### 6.1 现状拆解

```
WelcomeTyping（逐字，速度 = TypeSpeedMs）→ WelcomeHold（固定 650 ms）
  → WelcomeErase（逐字删）→ ContentTyping（逐字打真实内容）→ Live
```

写死的常量：停留 650 ms、计时器 20 ms、退格速度 = 打字速度、只能有一种形态、只播第一张卡。

### 6.2 参数化方案

把「形态」和「节奏」拆开：形态是预设名，节奏是相位数组，两者都能被主题覆盖。

```json
"motion": {
  "respectSystemSetting": true,
  "startup": {
    "preset": "typewriter",            // typewriter | fade | slide | none
    "stagger": { "enabled": false, "step": 60, "maxCards": 3 },
    "steps": [
      { "phase": "welcome-type",  "perChar": 28, "delay": 80,  "easing": "linear" },
      { "phase": "welcome-hold",  "duration": 650, "easing": "linear" },
      { "phase": "welcome-erase", "perChar": 18, "easing": "linear" },
      { "phase": "content-type",  "perChar": 12, "easing": "linear" }
    ],
    "cursor": { "style": "Block", "blink": true, "blinkMs": 530, "hideAfterMs": 0 }
  }
}
```

- `perChar`：每字符毫秒（0 = 用 `Behavior.TypeSpeedMs`，兼容老配置）；
- `duration` / `delay`：毫秒；`easing`：见 §10.2 的命名表；
- `preset` 只决定**默认 steps 与是否整卡动**，steps 里显式写的字段优先；
- `stagger`：多张常驻卡依次入场（每张延迟 `step` 毫秒，最多 `maxCards` 张），总时长超过 2500 ms 时自动取消
  stagger 只播第一张 —— 冷启动本来就有 PDH 预热，拖太长会显得启动慢。

### 6.3 实现要点

- 保留现有状态机骨架，只把常量换成 `MotionSpec`，并把 `_introTimer` 的 20 ms 改成「按 16 ms 节拍 + 按经过时间
  算字符数」（现在是后者，已经是防抖动的写法，别退化成每 tick 一个字）；
- **`SetLayoutText` 的预排版技巧必须保留**：先用完整内容定尺寸，再逐字显示，否则窗口会在打字过程中反复变宽；
- 光标（`TextCanvas.DrawCursor`）继续用自绘，主题的 `cursor.*` 直接喂给它；`blinkMs` 现在是 `TextCanvas` 里硬编码的
  `DispatcherTimer` 周期（530 ms），要一起参数化；
- 启动动画期间保持「不刷新数据、只更新布局尺寸」的闸门；
- `preset: "fade"` / `"slide"` 时不再逐字，而是把「整块卡」按 §9.2 的入场动画播一次，`steps` 里只留
  `welcome-hold` 的时长作为停留；
- 系统关了动画（§12）或 `preset: "none"` → 直接 `FinishIntro()`。

## 7. 基本外观

这一层的字段映射、导入导出、回滚已经在 [theme-system.md](theme-system.md) 里定稿，本文不重复，只补两件事：

### 7.1 主题层要新增的字段（动效与控件样式依赖）

| 字段 | 归到 | 谁用 |
| --- | --- | --- |
| `Accent` | `Style.Accent` | 主按钮、聚焦环、链接 |
| `Danger` | `Style.Danger` | 危险按钮、错误状态行（现在写死 `#E57B6E`） |
| `Muted` | `Style.Muted` | 状态行 / 副文字（现在写死 0.45–0.55） |
| `Border` | `Style.Border` | 面板描边（0.10 / 0.12 / 0.24 三处统一） |
| `Panel` | `Style.Panel` | 内部小面板底色（0.045） |
| `Scrollbar` / `ScrollbarHot` | `Style.*` | 滚动条拇指（0.18 / 0.40） |
| `MonoFont` | `Style.MonoFont` | CLI 面板等宽字体（现在是 `CliPanelView.MonoFont` 常量） |

这些字段同时也是 §8 控件样式的输入 —— 控件样式不再是「一个前景色 + 一堆写死 alpha」，而是「token 表」。

### 7.2 哪些东西**不**进主题

- 每张卡的 `Content` / `Spacing` / `Resident` / `Hotkey` / 位置：属于布局与行为；
- `Behavior`（刷新率、是否播启动动画、打字速度）：属于性能与偏好 —— 但**动效参数**（时长 / 缓动）归 `motion`，
  这是本文与 theme-system.md 的边界延伸：**开关在 Behavior，参数在主题**；
- 毛玻璃四项：见 theme-system.md §8。

## 8. 特殊外观：控件样式

### 8.1 现状

| 控件 | 现在怎么来的 | 想换形状要改什么 |
| --- | --- | --- |
| 按钮 `CardButton` | ctor 里 `XamlReader.Parse` 拼模板，3 种语气只是三组颜色 | 加语气 / 加形状都得改 ctor |
| 输入框 `CardTextBox` | 同上，模板里一个圆角 `Border` + `PART_ContentHost` | 同上 |
| CLI 小面板 `CliEntryPanel` | C# `new Border`，底 0.045、描边 0.10、圆角 `corner-3` | 改 C# |
| 滚动条 `CliScrollBars` | 拼字符串生成 `Style`（宽 7、拇指 0.18/0.40） | 改字符串 |
| 设置页 | `Theme.xaml` 8 个画刷 + `SettingsWindow.xaml` 7 处内联色 | 改 XAML 或换字典 |
| 滑块 | 设置页用系统 `Slider`（`Theme.xaml` 只给了基础样式），卡片里还没有滑块 | 需要先在卡片里有一个 |

### 8.2 参数化模型（token）

```json
"controls": {
  "density": "normal",                        // compact | normal | comfy
  "button": {
    "shape": "filled",                        // filled | outline | ghost
    "radius": 6, "border": 1, "padding": [12, 5], "minWidth": 64,
    "tones": {
      "normal":  { "fill": 0.10, "border": 0.28, "text": "foreground" },
      "primary": { "fill": "accent", "border": "accent", "text": "auto" },
      "danger":  { "fill": "danger", "border": "danger", "text": "auto" }
    }
  },
  "input":   { "fill": 0.07, "border": 0.24, "selection": 0.35, "radius": 4 },
  "panel":   { "fill": 0.045, "border": 0.10, "radiusDelta": -3, "padding": [8, 6], "gap": 6 },
  "scrollbar": { "width": 7, "thumb": 0.18, "thumbHot": 0.40, "radius": 3 },
  "slider":  { "track": 3, "thumb": "circle", "thumbSize": 12, "trackAlpha": 0.18, "thumbAlpha": 0.70 }
}
```

约定：

- 数值 0–1 表示「前景色的 alpha」，字符串表示 token 名（`"accent"` / `"danger"` / `"foreground"` / `"auto"`）；
- `density` 只影响三处内边距与行距的乘数（0.8 / 1.0 / 1.25），不做布局重排；
- `shape` 只有三档：`filled`（底色 + 描边，今天的样子）、`outline`（无底色、描边 0.55）、`ghost`（无底色无描边，
  hover 才出底色）—— 这三档覆盖了绝大多数主题诉求，又不用碰模板；
- 滑块要**先有才能被主题化**：现在卡片里没有滑块，`slider.*` token 是为 8.5 里设置页换肤和以后的卡片控件预留的，
  第一阶段可以不实现。

### 8.3 模板注入（受控 XAML）

「可换完整模板」是用户明确要的，但它是**唯一一个能让主题文件执行代码的入口**。官方文档对不受信任的 XAML 的定性是
「应将其一般视为不受信任的代码」，并明确它「有权构造对象并设置其属性…访问程序集中的类型、使用标记扩展、`x:Code` 块」
（[XAML 安全注意事项](https://learn.microsoft.com/zh-cn/dotnet/desktop/xaml-services/security-considerations)）。
所以设计成「默认关闭 + 四道校验」：

```
theme.controls.templates.button = "assets/button.xaml"
  ① 只读 64 KB 以内、UTF-8 的 .xaml
  ② XDocument 解析 + 白名单（元素 / 属性 / 命名空间）
  ③ 只允许 {TemplateBinding ...} 与固定 token 的 {DynamicResource ...}，禁止 x:Code / 事件 / x:Static / ObjectDataProvider
  ④ XamlReader.Parse 出来必须是 ControlTemplate 且 TargetType 匹配
  失败 → 用内置模板 + 日志一行，主题其余部分照常套用
```

白名单（初版，够画按钮 / 面板 / 滑块）：

- 元素：`ControlTemplate` `Border` `Grid` `StackPanel` `DockPanel` `ContentPresenter` `TextBlock` `Path` `Rectangle`
  `Ellipse` `ScrollViewer` `Thumb` `Track`
- 属性：`Background` `BorderBrush` `BorderThickness` `CornerRadius` `Padding` `Margin` `Width` `Height` `MinWidth`
  `MinHeight` `Opacity` `Foreground` `FontSize` `FontFamily` `FontWeight` `Text` `TextWrapping` `TextTrimming`
  `VerticalAlignment` `HorizontalAlignment` `Stretch` `Fill` `Stroke` `StrokeThickness` `Data` `RadiusX` `RadiusY` `SnapsToDevicePixels`
- 触发器只允许 `IsMouseOver` / `IsPressed` / `IsEnabled` / `IsKeyboardFocused` 四个属性触发器

还要加两条**布局护栏**：模板渲染后如果（a）同尺寸下高 / 宽超过内置模板的 2.5 倍，或（b）测量抛异常，则回退内置模板
并提示「模板与卡片尺寸不兼容」。否则一个 `Padding="80"` 的按钮就能把整叠卡片的布局挤爆。

风险与对策一览：

| 风险 | 对策 |
| --- | --- |
| 主题模板 = 任意对象构造 | 白名单 + 默认关闭（设置页里一个显式开关「允许主题携带控件模板」） |
| 模板破坏布局 / 点击区 | 尺寸护栏 + 动画期间不允许换模板 |
| 模板里的 `DynamicResource` 指向不存在的 key | 只认固定 token 名单，别的直接拒绝 |
| 解析慢 / 每次重开都解析 | 按（文件哈希 + 控件类型）缓存，`Freeze()` 能冻的都冻 |
| 分享主题被恶意改过 | 导入时展示「这份主题带模板」的显式确认，和 theme-system.md 的「整份回滚」共用一套错误处理 |

### 8.4 为什么不做 XAML 资源字典主题

不是审美问题，是**够不着**：`CardButton` / `CardTextBox` 的模板是运行时 `XamlReader.Parse` 生成的，`CliEntryPanel`
是 `new` 出来的，`TextCanvas` 干脆自己 `OnRender`。换成资源字典方案要先把这三种控件全部改写成「模板 + 样式」的
正规 WPF 控件（估计 600–800 行重构），然后每个新卡片的控件都得跟着建一遍。`theme-system.md` 里也已经论证过
「XAML 主题要 `pack://` 编进程序集」。所以资源字典方案的成本 / 收益在这个项目里不成立。

### 8.5 设置窗口换肤

设置页现在是固定浅色：`Theme.xaml` 8 个画刷 + `SettingsWindow.xaml` 里 7 处内联十六进制（5 个色值）+ C# 里
13 处 `Color.FromRgb`。三条路：

| 方案 | 做法 | 优点 | 缺点 |
| --- | --- | --- | --- |
| A. **改画刷实例**（推荐） | 保留 `Theme.xaml` 里那 8 个 `SolidColorBrush` 对象，运行时改 `.Color` | `StaticResource` 拿的是同一个实例，改色会自动刷新，**不用动 72 处引用**；一行代码换肤 | 只能改颜色与透明度，改字号 / 圆角还得走 DynamicResource；要求画刷没被 `Freeze()` |
| B. 换合并字典 + 重建窗口 | 换 `App.Resources.MergedDictionaries` 再重建 `SettingsWindow` | 什么都能换 | 已加载的树不会更新（静态资源已解析），等于「必须关掉重开设置窗口」；丢状态 |
| C. 全部改 `DynamicResource` | 逐处替换 | 最正统 | 72 处引用 + 7 处内联色要动；动态查找有开销；本文档范围外的大改 |

推荐 **A + 把 7 处内联色提成 token**（内联色是 A 方案的唯一漏洞：它们不引用画刷）。设置页主题块：

```json
"skin": {
  "settings": {
    "mode": "follow",                 // follow（跟随卡片主题）| light | dark | custom
    "accent": "#2F6FEB",
    "windowBg": "#F6F7F9",
    "cardBg": "#FFFFFF",
    "border": "#E2E5EA",
    "text": "#1B1F24",
    "textMuted": "#6B7280",
    "danger": "#D14343"
  }
}
```

`mode: "follow"` 时由 `appearance.colors` 推导（深色卡 → 深色设置页），浅色主题下要过对比度门槛：正文
≥ 4.5:1、副文字 ≥ 3:1（`Muted` 透明度不够时自动加深，而不是直接采样）。

### 8.6 浅色主题的两个坑（theme-system.md 已提过，这里给量化）

- 滑块 / 描边在浅色底上偏淡：深色主题的 `scrollbar 0.18` 在浅色底上建议 ≥ 0.28（`paper.mptheme` 用的 0.30）；
- `accent` 在浅色底上要换更深的色阶（`#2F6FEB` 在 `#F6F7F9` 上 4.6:1 刚好过线，`#4C8DFF` 只有 3.1:1，不够）。

## 9. 行为动画：出现 / 消失 / 让位

### 9.1 现状

```csharp
// ShowCard：窗口先 Show、量高度，再 Opacity = 1
slot.Window.Opacity = 0;  slot.Window.Show();  ...  slot.Window.Opacity = 1;

// HideCard：直接隐藏 + 重排
slot.Shown = false; slot.Window.Hide(); LayoutCards();

// LayoutCards：直接把目标 X/Y 写进 Overlay 再 ApplyPosition()
```

没有任何中间状态，所以「非常驻卡片呼出」是瞬现，「下方卡片让位」也是瞬间跳。

### 9.2 出现 / 消失（动内容，不动窗口）

方案：**窗口按最终位置与「最终尺寸 + 上下各 16 DIP 余量」放置**，把整块卡（底板 + 内容，即 `OverlaySurface`
的内层 Grid）套一个 `TranslateTransform` + `Opacity`，从 `(0, -12)` / 0 插到 `(0, 0)` / 1；消失反向。

为什么这么绕：卡片窗口的大小等于卡片大小（§2.2），直接平移内容会被窗口边界裁掉；留 16 DIP 余量后窗口是透明的
（`AllowsTransparency`），视觉上就是「卡片从上方 12 px 滑进来 FADE」。

- 余量只在「这张卡启用了入场动画」时加：给根 `Grid` 加 16 DIP 的 `Margin`，`SizeToContent` 会把窗口一起撑大；
  动画结束后把 `Margin` 归零，窗口自己收回去，避免常驻卡白占 32 DIP（注意 `ResetStableSize()` 是文本测量的
  稳定尺寸开关，和窗口尺寸无关，别拿它当收窗口用）；
- 底板（`_tintLayer`）和玻璃层都在内层 Grid 里，会一起平移 —— 玻璃纹理跟着卡片走，220 ms 内看不出来，
  而且**不触发重采样**（窗口没动）；
- 投影（`Overlay.Shadow`，预留 10 DIP）与 16 DIP 余量叠加，注意改成 `max(shadowMargin, 16)` 而不是相加；
- 交互卡片（AI 对话 / 命令行）也走这条路：窗口不动就没有命中测试漂移问题，动画期间把 `IsHitTestVisible`
  设为 false，结束后恢复，避免点到一个正在淡入的面板。

参数：

```json
"motion": {
  "appear":    { "preset": "slide-fade", "duration": 220, "delay": 0, "easing": "fluent-enter",
                 "offset": { "x": 0, "y": -12 }, "opacity": { "from": 0, "to": 1 },
                 "scale": { "from": 1, "to": 1 }, "mode": "content" },
  "disappear": { "preset": "fade", "duration": 140, "easing": "fluent-exit",
                 "offset": { "x": 0, "y": 0 }, "opacity": { "from": 1, "to": 0 },
                 "mode": "content" }
}
```

`mode` 三档：`content`（推荐，窗口不动）、`window-alpha`（整窗淡入，实测不便宜，仅当 `scale`/`offset` 都是 0 时的
等价替代）、`none`。`scale` 保留字段但默认 1 —— 分层窗口上缩放文字会发虚，只建议 0.98→1 这种轻微值。

### 9.3 让位

出现 / 消失时，**它下面的卡片要下移 / 上移**（`height + spacing`），这只能逐帧移动窗口。

算法（改 `LayoutCards` 的职责划分）：

```
LayoutCards()             // 只算目标值，不直接写窗口
  ├─ 计算每个 slot 的 TargetX / TargetY（现有逻辑，原样保留）
  └─ 有动画在跑 → 交给 MotionEngine 插值；否则直接写到 Overlay + ApplyPosition()

MotionEngine.Tick()
  ├─ 对每个 reflow 中的 slot：Y = Lerp(fromY, TargetY, eased(t))
  ├─ 写 Overlay.Y → ApplyPosition()（内部会 CaptureGlass，见下）
  └─ 到点后吸附到 TargetY，标记完成
```

成本与降级（关键）：

| 场景 | 移动的卡数 | 每帧代价 | 处置 |
| --- | --- | --- | --- |
| 无毛玻璃 | N（下面所有卡） | 0.5–2 ms/卡（§4 移动列） | 30–60 帧/秒，按 N 自动降帧（N≥4 → 30 帧/秒） |
| 系统亚克力（DWM 底板） | N | 移动不带重采样（底板独立窗口跟着走） | 保持 30 帧/秒 |
| 桌面亚克力（DXGI 自截屏） | N | 每帧 `RequestFrame`，2.4.5 实测拖动 34.8 帧/秒 | 限到 30 帧/秒，`maxFps` 由主题给 |
| 兼容（`PrintWindow`） | N | 拖动实测 21 帧/秒、单卡 5.6–6.3 ms | **动画期间隐藏玻璃层**（换成纯色底，动画结束再打开）；或干脆退化成 `opacity` 单次淡出 |

这就是 §0 结论 2 的落地方式：`motion.reflow.duringGlass = "dim" | "freeze" | "skip"` 三档，
`dim` = 动画期间 `Surface.ClearGlass()`、结束后 `UpdateGlass()`（推荐），`freeze` = 保持最后一帧玻璃不动（会露馅），
`skip` = 弧过动画直接跳（最省）。

护栏：

- **只对用户触发的显示 / 隐藏做让位动画**。数据驱动的卡片高度变化（CLI 面板长高、媒体换文件）每 500 ms 就可能来一次，
  动画会让整叠一直晃 —— 这类变化保持瞬切；
- 拖动卡片期间禁止让位动画（`_dragging`）；
- 启动动画期间禁止（`_phase != Live`）；
- 锁定（`Overlay.Locked`）时仍然可以动 —— 锁定只影响命中测试，不影响程序移动；
- 让位动画期间不允许「再触发一次」：新目标来了就从**当前值**接上（§9.4）。

### 9.4 中断与队列

- 每个 slot 每个属性（X / Y / 内容 offset / opacity）最多一条动画轨道，新动画来了**从当前值接上**（不是从 `from` 重来），
  时长按剩余距离等比缩短（避免「点了两下变成 440 ms」）；
- 消失动画播到一半又被呼出 → 反向插值，不重启；
- 动画期间改设置（`ApplySettings`）→ 直接吸附到目标值并清空轨道；
- 动画期间销毁卡片 → 轨道丢弃，不做清理动画。

### 9.5 不做动画的场景

| 场景 | 原因 |
| --- | --- |
| 数据驱动的高度变化 | 500 ms 一次，动画等于一直晃 |
| 启动动画期间 | 已有闸门，且入场动画会与打字抢内容 |
| 拖动中 / 拖动结束的落位 | 用户手已经在拖，动画会「抢」 |
| 视频子窗口移动 | 视频是独立 HWND（`VideoWindow`），要逐帧 `SetWindowPos` 才能跟上，成本另算 |
| 设置页预览 | 设置页是普通窗口，用 WPF 自带动画即可，不必共用引擎 |

### 9.6 帧预算与降级矩阵

| 条件 | 行为 |
| --- | --- |
| `SystemParameters.ClientAreaAnimation == false` | 全部动画设为 `none`（§12） |
| 单帧总耗时 > 12 ms | 本帧跳过非必要轨道（opacity 先跳、offset 后跳） |
| 可见卡片 > 6 | stagger 关闭、让位降到 20 帧/秒、出现动画时长 ×0.7 |
| 兼容玻璃开启 | 让位动画走 `duringGlass: dim` |
| 电池供电 / 远程桌面 | 直接 `none`（可选，P2） |

## 10. 运行时架构

### 10.1 组件

```
src/Ui/Motion/
  MotionSpec.cs     主题里的 motion / controls 数据模型 + 校验（~250 行）
  MotionEasing.cs   名字 → Func<double,double>（KeySpline 驱动 + 命名表，~120 行）
  MotionClock.cs    唯一一个 CompositionTarget.Rendering 订阅 + 轨道表 + 帧预算（~200 行）
  CardMotion.cs     每张卡的轨道：appear / disappear / reflow（~200 行）
  ControlStyle.cs   token → CardButton / CardTextBox / CliEntryPanel / CliScrollBars 的样式（~250 行）
  ThemeSkin.cs      设置页换肤（画刷实例改色，~120 行）
```

关键约定：

- **全局只订阅一次 `CompositionTarget.Rendering`**（现在是每张卡各自 `DispatcherTimer`，GIF、视频进度、玻璃各有一个），
  空闲时（无轨道）立刻退订，避免常驻空转；
- `KeySpline` 的 `GetSplineProgress(t)` 直接给到 `cubic-bezier` 语义，和 Fluent 的
  `cubic-bezier(0,0,0,1)`（进入）/ `(1,0,1,1)`（离开）一一对应（[Timing and easing](https://learn.microsoft.com/en-us/windows/apps/design/motion/timing-and-easing)）；
- 时长默认值直接学 Fluent：入场 250 ms / 快速 167 ms / 极快 83 ms，本项目取 220 / 140 / 80；
- 所有插值用 `Stopwatch` 的**经过时间**算 t，不用帧计数（掉帧时不会变慢）。

### 10.2 缓动命名表

| 名字 | 映射 | 用途 |
| --- | --- | --- |
| `linear` | 线性 | 打字、光标 |
| `fluent-enter` | `cubic-bezier(0, 0, 0, 1)` | 出现、让位 |
| `fluent-exit` | `cubic-bezier(1, 0, 1, 1)` | 消失 |
| `soft` | `cubic-bezier(0.4, 0, 0.2, 1)` | 通用 |
| `back` / `elastic` / `bounce` | WPF `BackEase` / `ElasticEase` / `BounceEase` | 主题想要「弹一下」时用 |
| `cubic-bezier(x1,y1,x2,y2)` | `KeySpline` 直接解析 | 兜底，任意曲线 |

`KeySpline` 的四个参数与 CSS `cubic-bezier` 同义（[KeySpline 类](https://learn.microsoft.com/zh-cn/dotnet/api/system.windows.media.animation.keyspline)），
所以主题作者可以照抄 CSS 的缓动值。

### 10.3 埋点

日志只在新功能上打三类行，便于回测（沿用现有 `Log.Info` 风格）：

```
动效：入场 卡片=系统信息 方式=content 时长=220ms 缓动=fluent-enter 帧=13 单帧均值=2.4ms
动效：让位 卡片=3 张 目标位移=+108dip 方式=window-move 帧率=28 玻璃=dim
动效：降级 原因=帧预算(14.2ms>12ms) 跳过=opacity
```

## 11. 校验、回滚、热重载、迁移

与会 theme-system.md §4 同一套机制，这里只列差异：

| 项 | 规则 |
| --- | --- |
| `schema` | 接受 1 和 2；写出一律 2 |
| `motion.*.duration` | 0–2000 ms；`perChar` 4–500 ms；`delay` 0–5000 ms |
| `motion.reflow.maxFps` | 5–60；默认按玻璃实现自动选（§9.3） |
| `controls.*` 数值 | 0–4（alpha 0–1、像素 0–48） |
| 模板路径 | 相对主题文件目录，禁止 `..`、禁止绝对路径、禁止 UNC（沿用 theme-system.md 的 ZIP 方案约束） |
| 失败 | 整份回滚，不改内存 / 磁盘；日志一行原因 + 设置页提示 |
| 热重载 | `MIRRORPOND_THEME_WATCH=1` 时 300 ms 防抖重载；动效和模板一起重载 |
| 迁移 | `schema: 1` 的 `docs/themes/*.mptheme` 不需要改，缺块即默认 |

## 12. 无障碍与系统偏好

- 读 `SystemParameters.ClientAreaAnimation`（系统「显示动画」开关的 WPF 暴露，[文档](https://learn.microsoft.com/fr-be/dotnet/api/system.windows.systemparameters.clientareaanimation)），
  为 `false` 时强制 `preset: none` + `appear/disappear.mode: none` + 让位瞬切；
- 主题里 `motion.respectSystemSetting = false` 只能由用户在设置页显式关掉（默认 true），不放进主题文件当默认行为；
- 快速连点不应产生「越点越长」的动画（§9.4 的等比缩短就是为此）；
- 打字节拍 28 ms ≈ 35 字/秒，低于这个值视作「可能引发不适」，主题里给下限 16 ms 并提示。

## 13. 分阶段落地

| 阶段 | 内容 | 规模 | 验收 |
| --- | --- | --- | --- |
| P0 | `MotionSpec` + `MotionEasing` + `MotionClock` + `CardMotion`（只做出现 / 消失，`mode: content`）+ 启动动画参数化（preset + steps + cursor.blinkMs）+ token 化 `Accent/Danger/Muted/Border/Panel/Scrollbar/MonoFont` | ~450 行 | 5 张卡入场仍 60 帧/秒、单帧 ≤ 4 ms；启动动画可切 `typewriter/fade/none`；老配置行为不变 |
| P1 | 让位动画（`window-move` + 限帧 + `duringGlass: dim`）+ 控件形状 token（button `filled/outline/ghost`、panel、scrollbar）+ 设置页换肤（画刷实例 + 内联色收敛） | ~500 行 | 兼容玻璃下让位 ≥ 15 帧/秒；浅色设置页正文对比度 ≥ 4.5:1 |
| P2 | 受控模板注入（白名单 + 尺寸护栏 + 显式开关）+ stagger + 主题热重载 + 主题预览缩略图 | ~350 行 | 恶意模板（`x:Code`、事件、绝对路径）被拒且不影响其余套用 |
| 不做 | 资源字典换肤、整叠做成单窗口、视频子窗跟随动画、卡片内容布局动画 | — | — |

## 14. 风险清单

| 风险 | 触发条件 | 影响 | 对策 |
| --- | --- | --- | --- |
| 让位动画 + 兼容玻璃 → 掉帧 | 开毛玻璃、≥3 张卡、频繁显示隐藏 | 20 帧/秒以下、CPU 25%+ | `duringGlass: dim` 默认开；限帧 30；卡片多时降到 20 |
| 整窗 alpha 成本被低估 | 主题选了 `mode: window-alpha` | 每帧 3 ms/卡，5 张卡 15 ms | 只在 `offset/scale` 为 0 时允许；否则强制 `content` |
| 交互卡片动画期间误点 | 出现 / 消失途中点卡片 | 点到空处或点穿到桌面 | 动画期间 `IsHitTestVisible=false`，结束恢复 |
| 窗口余量导致常驻卡变白边 | 忘了把根 `Grid` 的动画余量归零 | 卡片之间多出 32 DIP 空隙、拖动落点偏 | 动画结束统一归零；布局与命中用 `CardHeight` / `CardWidth`（已扣掉投影预留），不用 `ActualWidth` |
| 模板注入被滥用 | 导入别人的主题 | 任意对象构造 / 布局炸裂 | 白名单 + 默认关闭 + 尺寸护栏 + 导入确认 |
| 数据驱动高度变化也动画 | 没加闸门 | 整叠每 500 ms 晃一次 | 只有用户触发的显示隐藏才动画 |
| DPI / 亚像素 | 125% / 150% 缩放 | 文字发虚、边框错位 | 位置取整到设备像素；入场 offset 用整数 DIP |
| 动画与启动动画抢内容 | 冷启动立刻呼出非常驻卡 | 文字跳、光标闪错 | `_phase != Live` 时入场动画直接 `none` |
| 主题动效过慢 | 用户把 duration 写到 2000 | 一次呼出 2 秒，像卡了 | 上限 2000 ms + 设置页提示 |

## 15. 不做的部分

- **资源字典 / XAML 主题包**：控件是 C# 构建的，够不着（§8.4）；且需要重编译才能加主题。
- **把整叠卡片合成一个窗口再做统一动画**：会推翻「一卡一窗口」的隔离（每卡独立玻璃、独立命中测试、独立
  `Topmost`、视频子窗），收益只有动画省事，代价是整套架构重写。
- **视频子窗口跟随入场动画**：视频是独立 HWND，跟随要逐帧 `SetWindowPos`，而视频本身已经在用第二条 ffmpeg
  进程 + winmm（2.4.5 的既有成本），动画期间只会更抖。视频卡出现 / 消失走 `fade` 且不移动。
- **卡片内容（文字行）逐行动画**：`TextCanvas` 是自绘的，逐行入场要在 `OnRender` 里做遮罩动画，成本高观感收益低。
- **毛玻璃进主题**：theme-system.md §8 已论证，本文沿用。

## 16. 参考

- 分层窗口全软件渲染、动画要「做小」：[transparent windows in WPF… please make them small](https://learn.microsoft.com/en-in/archive/blogs/jaimer/transparent-windows-in-wpf-please-make-them-small)
- 逐帧渲染的官方做法（本文 `MotionClock` 的基础）：[如何：使用 CompositionTarget 以每帧间隔呈现](https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)
- 缓动曲线与时长 token（Fluent 的 250 / 167 / 83 ms、`cubic-bezier(0,0,0,1)`）：[Timing and easing](https://learn.microsoft.com/en-us/windows/apps/design/motion/timing-and-easing)
- `KeySpline`（与 CSS `cubic-bezier` 同义）：[KeySpline 类](https://learn.microsoft.com/zh-cn/dotnet/api/system.windows.media.animation.keyspline)
- 不受信任 XAML 的定性（模板注入的风险依据）：[XAML 安全注意事项](https://learn.microsoft.com/zh-cn/dotnet/desktop/xaml-services/security-considerations)
- 系统动画开关：[SystemParameters.ClientAreaAnimation](https://learn.microsoft.com/fr-be/dotnet/api/system.windows.systemparameters.clientareaanimation)
- 静态外观字段与 `.mptheme` 格式：[theme-system.md](theme-system.md)
- 玻璃各实现的实测代价：[glass-implementations.md](glass-implementations.md)
