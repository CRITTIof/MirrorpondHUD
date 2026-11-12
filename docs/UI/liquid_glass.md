# Apple Liquid Glass 调研与 MirrorpondHUD 应用建议

> 目的：把 Apple 在 WWDC25 发布的 Liquid Glass 拆到**可实现的像素与参数**粒度，并判断它对这个「常驻、鼠标穿透、零第三方依赖」的 WPF HUD 意味着什么。
>
> 边界：本文只讨论**材质与光学**。三条既有毛玻璃实现的工程细节见 [`glass-implementations.md`](../glass-implementations.md)，不重复；「材质属于能力、不属于主题」的划界见 [`theme-system.md`](../theme-system.md)。
>
> 状态：调研稿（2026-11-11，对应代码 2.30.0）。Apple 资料以 2026-11 可取的官方页面为准；Apple 没有公开 shader、数值参数和图层合成细节，本文第 3 节的公式是**可工作的近似**，不是对 Apple 实现的还原。

## 0. 结论先行

| 问题 | 结论 |
|---|---|
| 要不要跟进 Liquid Glass？ | 跟，但只跟**设计纪律 + 边缘光**；不跟「整张卡片实时折射」。 |
| 最高性价比的落地？ | **G0 边缘光玻璃**：1px 边缘渐变 + 顶部高光 + 底部内阴影 + 可读性 scrim。零抓屏、零新增 CPU、不影响截图。 |
| 想要真折射？ | **G1 准液态玻璃**：只在 `DesktopFeed` 上做 CPU 边带折射。默认关闭，接受「卡片不进截图」，并设性能闸门。 |
| 能不能用 GPU 着色器？ | 不能假设可用：本项目的叠层是分层窗口，仓库实测其渲染路径指向软件路径；PS 3.0 着色器在软件渲染下会被忽略，PS 2.0 会退化为软件着色。逐像素折射不适合把它当主路径。 |
| 默认开吗？ | **不开**。保持现有观感；G0 也先默认关，截图验收后再决定是否随 `Acrylic` 默认开启。 |
| 最大风险？ | 可读性下降、截图/录屏丢卡片、CPU 随卡片数线性上升。 |

一句话：**Liquid Glass 的核心不是「更高级的毛玻璃」，而是「边缘折射 + 镜面高光 + 自适应着色 + 功能层定位」。** 前两项可以低成本借，第三项要克制，第四项会直接改变本项目的信息层级——这一项比像素更重要。

## 1. 调研方法与证据分级

本文沿用 [`endfield.md`](endfield.md) 的做法，把「官方怎么说」「本文怎么推断」「本项目实测是什么」分开，避免把二手参数当成 Apple 规范。

| 级别 | 来源 | 能证明什么 | 不能证明什么 |
|---|---|---|---|
| 一手（官方） | [WWDC25 · Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/)、[HIG · Materials](https://developer.apple.com/design/human-interface-guidelines/materials)、[Adopting Liquid Glass](https://developer.apple.com/documentation/TechnologyOverviews/adopting-liquid-glass)、[Applying Liquid Glass to custom views](https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views)、SwiftUI / UIKit / AppKit API 文档 | 设计定位、光学与自适应行为、使用纪律、API 名称与可用平台 | 具体 shader、数值参数、图层合成顺序、精确折射曲线 |
| 一手（本项目） | `src/Ui/**`、`docs/performance-baseline.md`、`docs/glass-implementations.md` 中的实测 | 分层窗口的行为、取图链路、CPU/内存基线、降级路径 | Apple 材质本身的观感 |
| 二手 | WWDCNotes、社区复刻与逆向分析 | 交叉核对术语与实现思路 | 不能当作 Apple 官方参数；本文**没有**采用任何社区数值 |
| 本文推断 | 第 3 节的光学模型、第 5 节的参数与成本估算 | 给出可实现的工程路径 | 不是 Apple 原值；落地前必须实测 |

**没能拿到、因此不写进结论的东西：**

- WWDC25 219 的完整后半段转写（页面抓取被截断）；本文只引用已取到的原文。
- Apple 的 shader、渲染管线、图层数量与精确参数（官方未公开）。
- iOS 真机上的像素级对照（本机没有 Apple 设备与工具链）；观感判断来自官方视频/截图与文档描述。
- 社区复刻的正文（抓取失败）；本文不引用其参数，只在必要时说明「社区常见做法」不作为依据。

## 2. Liquid Glass 是什么

### 2.1 官方定义：一种会弯折光线的「数字超材质」

Apple 对它的定位不是「一种新的模糊」，而是对材质的一次改写：

> "Rather than trying to simply recreate a material from the physical world, Liquid Glass is a new digital meta-material that dynamically bends and shapes light."
>
> 不是去复刻现实世界的某种材质，而是一种会**动态弯折、塑造光线**的数字超材质。
>
> — [WWDC25 · Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/)

> "Whereas previous materials scattered light, this new set of materials dynamically bends, shapes, and concentrates light in real time."
>
> 过去的材质让光线**散射**；这套新材质实时地**弯折、塑造、聚集**光线。
>
> — 同上

这两句把 Liquid Glass 和「毛玻璃」划开了：毛玻璃是**低频化**背景（模糊 + 压暗/提亮），Liquid Glass 是在低频化之外，还让背景在边缘发生**几何位移**。前者只改变「清晰度」，后者改变「形状」。

### 2.2 五个可拆解的光学/行为要素

| 要素 | 官方依据 | 具体表现 | 对本项目的可迁移性 |
|---|---|---|---|
| **Lensing（透镜/折射）** | 「实时弯折、塑造、聚集光线」；「Liquid Glass 通过透镜效果在视觉上定义自己」 | 玻璃边缘附近的背景被压缩/弯曲，形成「透明物体有厚度」的直觉；中心区域基本不位移 | ⚠️ 可做，但只能在抓屏路径上做 CPU 近似；成本与截图代价见 §4 |
| **Specular（镜面高光）** | 「响应交互，瞬间 flex 并 energize with light」；「反射周围内容的颜色与光」 | 边缘出现随形状/光照变化的高光，通常集中在迎光侧；不是一圈均匀白边 | ✅ 低成本可做；WPF 渐变/边框即可拿到大部分观感 |
| **Adaptive tint / luminosity** | 「由多个分层组成，每层根据下方内容调整」；「文本滚动时阴影更明显」「着色量与动态范围会变化」「必要时单独切换着色深浅」 | 背景亮/暗、内容密/疏时，玻璃的 tint、亮度、阴影强度跟着变，保证文字可读 | ✅ 可做；但必须**有界**，否则主题 identity 会被壁纸吃掉 |
| **Shadow / edge definition** | 官方在图标层的描述里把 reflection/refraction/shadow/blur/highlights 并列（见 Adopting）；UI 材质是否同一套未公开 | 玻璃与背景之间有细阴影/细边，圆角处尤其明显；不是投影越重越像玻璃 | ✅ 可做；与现有 `DropShadowEffect` 是两件事，别混 |
| **Fluidity / morphing** | 「对象不是淡入淡出，而是通过逐渐调制光线的弯折与透镜来 materialize」；`GlassEffectContainer` 让多个形状融合/形变 | 出现/消失、控件切换时形状连续变化；触摸时控件「浮起」 | ❌ 不建议搬：HUD 没有触摸，卡片是独立 HWND，合并窗口会破坏拖动/穿透/置顶 |

### 2.3 官方 API 现状（截至 2026-11）

| 平台 | API | 关键能力 | 备注 |
|---|---|---|---|
| SwiftUI（iOS/iPadOS/Mac Catalyst/macOS/tvOS/watchOS 26.0） | [`View.glassEffect(_:in:)`](<https://developer.apple.com/documentation/swiftui/view/glasseffect(_:in:)>) | 在视图后方渲染 Liquid Glass 形状；默认 `.regular` + 胶囊；可指定圆角矩形等形状 | 系统组件自动采用；自定义组件用它 |
| SwiftUI | [`Glass`](https://developer.apple.com/documentation/swiftui/glass) | `.regular` / `.clear` / `.identity`；`.tint(_:)`；`.interactive(_:)` | `regular` 负责模糊 + 亮度调整；`clear` 高透，用于媒体背景 |
| SwiftUI | [`GlassEffectContainer`](https://developer.apple.com/documentation/swiftui/glasseffectcontainer) | 把多个玻璃形状合并渲染，支持相互融合/形变；`spacing` 控制靠近到多远开始融合 | 同一窗口内的控件组；跨 HWND 不适用 |
| SwiftUI | `glassEffectID` / `glassEffectUnion` / `glassEffectTransition` | 形变身份、合并、`matchedGeometry` / `materialize` 过渡 | 服务于动效与转场 |
| UIKit（iOS/iPadOS/Mac Catalyst/tvOS 26.0） | [`UIGlassEffect`](https://developer.apple.com/documentation/uikit/uiglasseffect) | `init(style:)`、`isInteractive`、`tintColor`；`UIGlassContainerEffect` 合并多个玻璃元素 | 通过 `UIVisualEffectView` 使用 |
| AppKit（macOS 26.0） | [`NSGlassEffectView`](https://developer.apple.com/documentation/appkit/nsglasseffectview) | `contentView`、`cornerRadius`、`style`、`effectIsInteractive`、`tintColor`；`NSGlassEffectContainerView` | 与 SwiftUI 版能力对应 |

**一个容易忽略的点**：这些 API 的可用性都是 **26.0**，不是「iOS 18 打补丁」。它是一次跨平台的设计语言换代，不是某个控件的新样式。

### 2.4 官方使用纪律：比「怎么画」更重要

HIG 和 Adopting 文档把「哪里不该用」写得非常明确。下面每条都直接影响本项目：

| 官方原文 | 翻译 | 对 HUD 的含义 |
|---|---|---|
| "Liquid Glass forms a distinct functional layer for controls and navigation elements — like tab bars and sidebars — that floats above the content layer..." | 玻璃是**控件与导航**的功能层，浮在内容层之上 | 卡片边框/标题/状态/按钮可以玻璃；正文读数不要整块玻璃 |
| **"Don't use Liquid Glass in the content layer."** | **不要在内容层用 Liquid Glass** | 聊天记录、命令行输出、文件列表是内容层——不要给它们上折射 |
| "Use Liquid Glass effects sparingly... Limit these effects to the most important functional elements in your app." | 克制使用，只给最重要的功能元素 | 一屏最多一两处「真玻璃」，不要每张卡都开 |
| "Only use clear Liquid Glass for components that appear over visually rich backgrounds." | `clear` 只用于媒体/视觉丰富背景上的控件 | 本项目没有「悬浮在视频上的控件」场景，`clear` 基本用不上 |
| "The regular variant blurs and adjusts the luminosity of background content to maintain legibility..." | `regular` 会模糊并调整背景亮度，保证文字可读 | 文字多的卡片只能用 `regular` 式（模糊 + 亮度调整 + scrim），不能用高透 |
| "If the underlying content is bright, consider adding a dark dimming layer of 35% opacity." | 背景亮时，加 35% 暗化层 | 本项目 `_tintLayer` 的透明度下限可以参考这个值 |
| "Limit the use of Liquid Glass effects onscreen at the same time." | 同一屏不要同时叠太多玻璃效果 | 与「一屏一处」一致；也是性能纪律 |
| "Avoid overcrowding or layering Liquid Glass elements on top of each other." | 不要玻璃叠玻璃 | CLI 面板、弹出菜单、设置窗内部不要再用玻璃 |
| "Test your interface with a variety of display and accessibility settings." | 用不同显示与无障碍设置测试 | 高对比度、关闭透明效果、远程桌面、减少动效都要有降级 |

> 一句话：**Apple 把「克制」写进了规范，而不是留给设计师自觉。** 这条对常驻 HUD 比对手机 App 更重要——用户一天看几百次，任何「闪」和「糊」都会被放大。

## 3. 技术拆解：一帧液态玻璃由什么组成

### 3.1 分层模型

Apple 没有公开图层顺序，但从官方描述可以拆出一个**能工作的五层模型**：

```text
┌──────────────────────────────────────────────┐
│ 5. 内容层（文字 / 控件）—— 永远不参与折射      │
├──────────────────────────────────────────────┤
│ 4. 可读性层（scrim / dimming）—— 动态，保对比度 │
├──────────────────────────────────────────────┤
│ 3. 颜色层（tint）—— 主题色 × 背景亮度           │
├──────────────────────────────────────────────┤
│ 2. 光学层（lensing + specular + 边缘阴影）      │
├──────────────────────────────────────────────┤
│ 1. 背景层（抓屏 → 缩小 → 模糊）                 │
└──────────────────────────────────────────────┘
```

| 层 | 作用 | 官方依据 | 本项目对应 |
|---|---|---|---|
| 1 背景 | 拿到玻璃**后面**的画面，低频化 | 「blurs content behind it」 | `GlassFeed` 的 `StretchBlt` + `GlassCapture.BoxBlur`；或 `GlassCapture` 的兼容路径 |
| 2 光学 | 边缘位移、镜面高光、细阴影 | 「bends/shapes/concentrates light」「specular」 | **建议新增**：`LiquidGlass` 处理器（G1）；G0 只用 WPF 渐变近似 |
| 3 颜色 | 玻璃的色调与明度 | 「reflects color and light」「adapts tint」 | 现有 `_tintLayer`（`OverlaySurface`） |
| 4 可读性 | 压暗/提亮，保证文字对比度 | 「regular blurs and adjusts luminosity」「35% dimming」 | 现有 `_tintLayer` 的 opacity；G1 再加动态 scrim |
| 5 内容 | 文字/控件 | 「Don't use Liquid Glass in the content layer」 | `TextCanvas` / `_contentLayer`，永远在最上层 |

**这个顺序解释了为什么「文字不会跟着背景一起扭」**：折射只发生在第 2 层，文字在第 5 层，两者不在同一次采样里。把折射烘进整张卡片的背景图、再让文字半透明地压上去，会得到「字也在玻璃里」的廉价效果——那不是 Liquid Glass，是贴图。

### 3.2 透镜的几何模型（本文推断，不是 Apple 原式）

设卡片内容区为宽 `w`、高 `h`、圆角 `r` 的圆角矩形。对输出像素 `p = (x, y)`：

1. 计算**有符号距离场** `d(p)`：内部为负，边界为 0，外部为正；
2. 取 `d` 的梯度 `n = normalize(∇d)`，即指向外侧的法线；
3. 只在边缘带内产生位移：

```text
edge(p) = smoothstep(-band, 0, d(p))        // d=-band → 0；d=0 → 1
offset(p) = n(p) * strengthPx * edge(p)^2
src(p) = p + offset(p)
```

- `band` 是边缘带宽度（全分辨率像素）；
- `strengthPx` 是边缘最大位移量（全分辨率像素）；
- `edge` 在边界处为 1，向内 `band` 像素处降为 0；
- 中心区域 `offset = 0`，背景**原样通过**——这正是「透镜只定义边缘、不糊住内容」的关键。

`p` 处的颜色取模糊图在 `src(p)` 处的双线性采样。位移的符号决定凸/凹：本文建议先按「边缘把背景向内压缩」实现（看起来像一块有厚度的凸透镜），截图后再决定是否反向。

**色散（chromatic aberration）**是可选的：对 R/B 通道用略微不同的位移比例采样，只在边缘带生效，幅度控制在亚像素级：

```text
offsetR = offset * (1 + k)
offsetB = offset * (1 - k)
k = chromatic * 0.8       // chromatic 已归一化 0..1；默认 0.15 → 0.12
```

它能让边缘有一点「宝石感」，但过头就会变成廉价的 RGB 分离。**默认应低于 1px 的可见差异。**

### 3.3 高光与阴影

用同一个 SDF 法线构造一个「假 3D 法线」：

```text
slope = 1.5
N = normalize(float3(n.x * slope, n.y * slope, 1))
L = normalize(float3(-0.45, -0.75, 0.50))     // 左上偏前的主光
spec = pow(saturate(dot(N, L)), 24) * specular * edge
```

- `n.y < 0`（上边缘）时点积为正，出现高光；下边缘自然为 0；
- `n.x < 0`（左边缘）也会亮，形成「左上迎光、右下背光」的非对称感；
- 指数 24 让高光集中在很窄的边缘，不会把整块玻璃洗白；
- 再叠加一条 1px 的 rim：`smoothstep(-1.5, 0, d)`，透明度约 0.15–0.25；
- 底部内阴影：`smoothstep(-8, 0, d) * saturate(n.y) * 0.10–0.14`，把玻璃「压」在背景上。

注意：**这不是投影**。投影是玻璃与桌面之间的外阴影（现有 `DropShadowEffect` 已经在做），这里说的是玻璃**内部**的边缘明暗，两者不要互相替代。

### 3.4 自适应着色：只调明度，不调色相（仅 G1）

Apple 的材质会「根据下方内容不断调整」，但本项目必须加一条约束：**主题 identity 不能被壁纸吃掉**。建议只做有界的明度/对比度适配：

1. 对抓到的背景图做 4×4 子采样，算平均亮度 `Y` 与局部方差 `V`（`V` 可当作「背景有多花」）；
2. `busy = smoothstep(0.015, 0.090, V)`；`bright = smoothstep(0.55, 0.85, Y)`；
3. `scrim = clamp(baseOpacity + 0.10*busy + 0.08*bright, 0.35, 0.92)`；
4. 主题 tint 与背景平均色做 **最多 8%** 的混合，模拟「反射周围颜色」，但不能改变色相；
5. 用阈值 + 滞回更新，避免每帧抖动。

`0.35` 参考 HIG 的「亮背景加 35% 暗化层」；本文把它当作文字卡片的保守下限。对文字卡片，`scrim` 不应低于这个值；对纯媒体卡片，可以走 `clear` 式（高透 + 35% 暗化），但本项目没有这种场景。

### 3.5 动效与形变：为什么 HUD 不该抄

Apple 的动效有三个层次：

- **materialize**：不是淡入淡出，而是逐渐调制透镜强度（官方原文）；
- **morph**：`GlassEffectContainer` 让多个形状在靠近时融合，控件切换时连续变形；
- **interactive**：触摸/指针按下时，玻璃「flex」并亮起来。

对应到本项目：

| 能力 | 是否可搬 | 原因 |
|---|---|---|
| materialize | ⚠️ 可做但没必要 | 卡片已有 `CardMotion` 的显示/隐藏动画；再加一层透镜调制会重复 |
| morph | ❌ 不可搬 | 每张卡是独立 HWND；合并成一个窗口会破坏拖动、鼠标穿透、置顶、截图排除 |
| interactive | ⚠️ 仅交互卡片可做 | 常驻 HUD 大多数时间没有指针悬停；不要为「可能被摸一下」付每帧成本 |

> 结论：**HUD 的动效预算应该花在「状态变化」上，而不是「材质活着」上。** 常驻叠层最怕的不是不闪，而是持续闪。

### 3.6 一帧的伪代码（边带处理）

真实实现只处理边缘带，中心像素直接复制。这样既符合光学模型（中心不位移），也把成本压到全图的 16%–30%。

```csharp
// 仅示意：项目未开启 AllowUnsafeBlocks，实际用 Span<byte> / 数组索引。
static void ProcessBand(byte[] src, int sw, int sh,
                        byte[] dst, int dw, int dh,
                        int margin, LiquidGlassParams p)
{
    // dst 是卡片输出图；src 比它四周各多 margin 像素，供边缘采样
    for (int y = 0; y < dh; y++)                     // 先按行拷出卡片区域（不含 margin）
        Buffer.BlockCopy(src, ((y + margin) * sw + margin) * 4,
                         dst, y * dw * 4, dw * 4);

    for (int y = 0; y < dh; y++)
    for (int x = 0; x < dw; x++)
    {
        float d = SdfRoundRect(x, y, dw, dh, p.Radius);
        if (d < -p.Band) continue;                   // 中心带外，跳过

        float edge = SmoothStep(-p.Band, 0f, d);      // 边界 1，向内降为 0
        if (edge <= 0f) continue;

        float2 n = SdfGradient(x, y, dw, dh, p.Radius);
        float2 off = n * (p.StrengthPx * edge * edge);

        // 双线性采样；src 坐标要加 margin
        byte3 c = SampleBilinear(src, sw, sh, x + margin + off.X, y + margin + off.Y);

        float spec = Specular(n, edge, p.Specular);
        float rim  = Rim(d, n, p.Rim);
        float shade = InnerShadow(d, n, p.InnerShadow);

        WriteBgr(dst, dw, x, y, c, spec, rim, shade);
    }
}
```

几个工程细节：

- **`margin` 至少 `strengthPx + blurRadius`**：只覆盖位移不够，模糊核也要有真实像素可读，否则边缘采样会越界或读到模糊边界；
- `SdfRoundRect` / `SdfGradient` 可以预计算成 `short`/`float` LUT（按 `w,h,r,band` 缓存），避免每帧求根号；LUT 只覆盖边缘带，内存约几十到几百 KB（按尺寸封顶）；
- 输出仍是 `Bgr32`（与现有 `GlassFeed` 一致），alpha 不参与合成；
- 把判同帧提前到处理之前：先比较模糊图与上一帧的模糊图，相同就跳过 `ProcessBand`；参数变化要强制失效；
- **不要每帧 new 数组**，复用 `CaptureBuffer` / 输出缓冲，延续 P-2 的做法。

### 3.7 哪些是 Apple 没说的

下面这些，本文**没有**可靠依据，任何「Apple 参数」的说法都应被质疑：

- 精确的折射曲线（是不是 SDF 梯度、几次方衰减、band 占多大比例）；
- 模糊核（高斯还是多次盒式、半径多少）；
- 高光指数、光源方向、rim 的精确透明度；
- 图层数量与合成顺序；
- 是否使用色散、色散的幅度；
- 不同平台（iOS/macOS）是否使用同一套参数。

所以第 5 节的参数表全部是**建议值**，不是「Apple 默认值」。正确做法是：先按这些值做出可比较的截图，再用 A/B 调到「看起来对」，而不是去找一份不存在的官方参数表。

## 4. 为什么不能直接搬到 Windows / WPF

### 4.1 平台能力对照

| 能力 | Apple（iOS/macOS 26） | Windows 11 DWM | 本项目（WPF / .NET 8） |
|---|---|---|---|
| 背景模糊 | 系统材质，GPU 实时 | Mica / Acrylic，DWM 合成 | 三条既有实现；`DesktopFeed` 自己抓屏 + CPU 模糊 |
| 边缘折射（lensing） | 有 | **没有**（Mica/Acrylic 只做散射） | 只能在抓屏图上做 CPU 近似（G1） |
| 逐像素材质控制 | 有 | 没有公开接口 | 通过 `BitmapSource` 自己画 |
| 交互高光 | 触摸/指针实时 | 无 | 可选，但 HUD 大多数时候无交互 |
| 形状融合/形变 | `GlassEffectContainer` | 无 | 跨 HWND 不可行 |
| 无障碍自动适配 | 系统统一处理 | 部分（透明效果/高对比度） | 现有 `GlassBackdrop.IsAvailable` + 自己补 |
| 截图中的表现 | 正常 | 正常 | `DesktopFeed` 下**卡片不进截图**（既有代价） |

Windows 这边的「材质」是**背景板**，不是「透镜」：`DWMSBT_MAINWINDOW` = Mica，`DWMSBT_TRANSIENTWINDOW` = Desktop Acrylic（最亮变体），`DWMSBT_TABBEDWINDOW` = Mica Alt，最低 Windows 11 Build 22621（[Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)）。它们只负责「窗口后面的背景怎么被模糊/着色」，没有逐像素折射，也没有「玻璃边缘弯折背景」的概念。

### 4.2 本项目的三条硬约束

#### 约束 1：叠层是分层窗口，不能假设硬件加速

卡片窗口是 `WindowStyle=None` + `AllowsTransparency=True` + `Background=Transparent`（[`OverlayWindow.xaml`](../../src/Ui/OverlayWindow.xaml#L6-L11)）。仓库里已经有两条实测结论：

- `MediaElement` 放进这种窗口，视频画面会被整块丢弃，只剩背景色；所以视频要另开一个**不透明**子窗口（[`VideoWindow.cs`](../../src/UiKit/Media/VideoWindow.cs#L15-L21)）；
- DWM 不会给分层窗口画系统材质，这是「系统亚克力必须垫一块非分层底板」的根因（[`GlassBackdrop.cs`](../../src/Ui/GlassBackdrop.cs#L14-L17)）。

第一条尤其说明：在该窗口上，WPF 的视频合成路径已经退化；至少不能假设它像普通窗口一样硬件加速。这并不意味着「WPF 完全不能跑着色器」。Microsoft 的 [`ShaderEffect` 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.shadereffect)写得很清楚：

- PS 2.0 着色器**在软件渲染下仍会运行**；
- PS 3.0 着色器**在软件渲染下不会运行**（即使硬件支持 PS 3.0）。

所以准确结论是：**在本项目的分层窗口路径上，自定义着色器要么退化为软件着色、要么直接被忽略；对逐像素折射来说，手写 CPU 路径更可控，也更容易做边带优化和自检。** 不要因为「WPF 有 ShaderEffect」就认为可以照搬 Apple 的 GPU 管线。

#### 约束 2：真折射必须抓屏，抓屏必须排除自己

`DesktopFeed` 的取图链路是：`DXGI` 桌面复制 / GDI `BitBlt` → 缩小 → 盒式模糊 → `BitmapSource` → `OverlaySurface.SetGlass`（[`GlassFeed.cs`](../../src/Ui/GlassFeed.cs#L525-L583)）。它必须给自己设 `WDA_EXCLUDEFROMCAPTURE`，否则会把上一帧卡片拍回玻璃里形成拖影（[`GlassFeed.cs`](../../src/Ui/GlassFeed.cs#L24-L25)）。

这带来一个**无解的产品代价**：只要 G1 在生效，卡片就不会出现在任何截图/录屏里。Windows 没有「只给我窗口后面的像素、但不包括我自己」的公开 API；任何自截屏方案都要排除自己，也就必然丢卡片。G1 继承这个代价，不会比 `DesktopFeed` 更好。

#### 约束 3：CPU 预算已经很紧

现有实测（[`glass-implementations.md`](../glass-implementations.md)）：

| 配置 | 单核 CPU |
|---|---|
| 4 张卡，DesktopFeed 10 fps | ≈ 6.8% |
| 4 张卡，5 fps / 15 fps | 3.4% / 7.7% |
| 5 张卡，系统亚克力 | 1.5–2.2% |
| 6 张卡，DesktopFeed 10 fps（性能基线） | 见 [`performance-baseline.md`](../performance-baseline.md) |

在这个基础上再加「全图逐像素折射」，5 张卡 10 fps 会非常吃紧。**边带处理**（只处理边缘 16%–30% 的像素）是把 G1 拉回可行区间的关键；如果不做边带，直接放弃 G1。

另外，项目没有开启 `AllowUnsafeBlocks`。逐像素代码要用 `Span<byte>` / 数组索引，边带处理能显著减少边界检查与分支成本；不要为了这个功能给整个宿主打开 unsafe。

### 4.3 三档保真度：G0 / G1 / G2

| 档位 | 做法 | 观感 | 新增成本 | 截图 | 依赖/风险 | 建议 |
|---|---|---|---|---|---|---|
| **G0 边缘光玻璃** | 在现有 tint 上加 1px 边缘渐变、顶部高光、底部内阴影、scrim | 拿到「玻璃有边」的 60%–70% 观感（主观估计）；没有真折射 | **0 抓屏、0 新增逐帧 CPU** | 正常 | 极低；只是 WPF 图层 | **先做** |
| **G1 准液态玻璃** | 在 `DesktopFeed` 的抓屏图上做 CPU 边带折射 + 高光 + 色散 + 自适应 scrim | 能看出背景在边缘被弯折；不是 Apple 原版，但方向对 | 抓屏沿用；新增约 1–2% 单核（2 张卡、10 fps，估算；超过 2% 触发 kill criteria） | **卡片不进截图/录屏** | 中；CPU、色散过头、GDI 回退会更慢 | 实验项，默认关 |
| **G2 真·GPU 合成** | 独立 D3D/DirectComposition 视觉树、swapchain 或 Win2D | 最接近 Apple | 高；破坏「玻璃画进卡片内部」的单次合成修复 | 取决于窗口结构 | 很高；与零依赖、单窗口、分层窗口都冲突 | **不做** |

> 关于 G2：不是技术上绝对不可能，而是**不值得**。本项目 2.4.4 花了很大力气把玻璃从独立底板窗口改到卡片内部，就是为了消除圆角外扩和顶部暗带；再为了 GPU 折射拆回多窗口合成，会把已经修掉的问题请回来。除非未来 WPF/.NET 给分层窗口提供可用的硬件合成接口，否则不应考虑。

### 4.4 一个反直觉结论：全卡玻璃不如半卡玻璃

Apple 的规范说「不要在内容层用 Liquid Glass」，而 HUD 卡片里最重要的东西恰恰是内容：读数、聊天记录、命令输出。把整张卡做成强折射，会同时踩三个坑：

1. **可读性**：文字压在弯曲的背景上，背景里的直线/文字会在边缘「游动」，眼睛会一直被边缘吸引；
2. **性能**：全卡逐像素 vs 边带逐像素，差 3–6 倍；
3. **层级**：整张卡都在「发光」，反而没有层级——HUD 需要的是「内容清楚，边框有质感」。

所以本文的建议是：**G1 只给低文本密度的「英雄卡」（时钟 / 硬件读数）整卡使用；聊天、命令行、文件中转站、媒体卡最多用 G0 的边缘光，正文保持标准 tint。** 这是对 Apple 规范的一种 HUD 映射，不是 Apple 对 HUD 的官方规定，但它同时满足可读性、性能和层级。

## 5. 应用到 MirrorpondHUD

### 5.1 先决定「哪里不上玻璃」

| 区域 | 上不上 | 理由 |
|---|---|---|
| 卡片外框 / 标题栏 / 状态行 / 底部动作条 | ✅ G0；英雄卡可 G1 | 这是「功能层」，玻璃在这里最像 Apple 的用法 |
| 时钟、硬件读数等低文本密度卡 | ✅ 可整卡 G1（默认仍关） | 文字少、信息以数字为主，折射不会干扰阅读 |
| AI 对话 / 命令行正文 | ❌ 不上 G1 | 内容层；长文本 + 滚动，折射会持续干扰 |
| 文件中转站列表 | ❌ 不上 G1 | 内容层；列表项需要稳定对比度 |
| 媒体卡 / 视频窗 | ❌ 不上 | 视频窗是独立不透明子窗口；再叠玻璃会变成「玻璃叠玻璃」 |
| 设置窗口 | ❌ 不上 | 它不是 HUD 的一部分；而且设置窗需要最高可读性 |
| 卡片内弹出菜单 / 下拉 / 输入面板 | ❌ 不上 | Apple 明确不建议玻璃叠玻璃；这些小面板用现有 tint 即可 |
| 卡片之间 | ❌ 不合并 | 独立 HWND；合并会破坏拖动/穿透/置顶 |

### 5.2 G0：边缘光玻璃（先做）

**目标**：在**不抓屏、不加逐帧 CPU** 的前提下，让现有毛玻璃/半透明卡片看起来「有玻璃边」，而不是一块均匀的色板。

**视觉规格（建议）**：

| 元素 | 规格 | 说明 |
|---|---|---|
| 1px 边缘 | 线性渐变：左上 `#FFFFFF` 28% → 中段 8% → 右下 `#000000` 12% | 用 `BorderBrush` 的 `LinearGradientBrush`；跟随 `CornerRadius` |
| 顶部高光 | 顶部 35% 高度，白色 14% → 透明 | 用 `Path`/`Border` + `OpacityMask` 画，避免半透明矩形盖住圆角 |
| 底部内阴影 | 底部 25% 高度，透明 → 黑色 10% | 让玻璃「压」在背景上；不要和 `DropShadowEffect` 混为一谈 |
| 文字 scrim | 维持现有 `_tintLayer`；建议文字卡片静态下限 0.35（可被用户设置覆盖） | G0 不抓屏，无法感知壁纸亮度；0.35 是静态保守值，不是动态适配 |
| 阴影 | 现有 `DropShadowEffect` 不动 | 外阴影是卡片与桌面之间，不是玻璃内部 |

**WPF 落点**：

- 在 [`OverlaySurface`](../../src/Ui/OverlaySurface.cs#L50-L55) 的图层顺序里新增 `_edgeLayer`：`glass → tint → image → edge → canvas → content`。这样边缘光盖在背景图之上、文字之下，不会糊住文字；
- `_edgeLayer.IsHitTestVisible = false`；`CornerRadius` 与卡片一致；
- 所有画刷 `Freeze()`；不要每帧重建；
- 新增 `Style.GlassEdge`（bool，默认 `false`）。先默认关，跑 `--screenshot-cards` 对比后再决定是否随 `Acrylic` 默认开；
- `Settings.Normalize()` 不需要新数值钳制（bool 而已），但 `SelfTest` 可以加一条「`GlassEdge=false` 时边缘层不可见」的断言。

**验收**：

- 100% / 150% / 300% DPI 下 1px 边不糊、不偏；
- 圆角处无缺口、无直角补角；
- 深色/浅色壁纸、深色/浅色主题各一张截图；
- 文字区域没有被高光/内阴影压到对比度；
- `--perf` 与改动前无差异（预期 0）。

### 5.3 G1：CPU 边带折射（实验）

**前置条件（缺一不可）**：

1. `Style.Acrylic = true` 且 `Style.GlassMode = DesktopFeed`；
2. 取图后端是 DXGI 桌面复制（GDI 回退时自动降级为普通 tint；若开了 G0 则是边缘光，并记录原因）；
3. 系统不是高对比度、不是远程桌面、「透明效果」未被关闭；
4. 卡片在屏幕内，且不是视频/媒体卡；
5. 用户显式打开「液态玻璃（实验性）」。

**管线改动**：

```text
TryGetScreenRect（卡片矩形，物理像素）
        │  向外扩 margin（≥ strengthPx + blurRadius）
        ▼
shared capture（整叠区域，含 margin）
        │  StretchBlt 到半分辨率
        ▼
BoxBlur（沿用现有）
        ▼
LiquidGlass.ProcessBand（只处理边缘带）
        │  输出卡片尺寸的 Bgr32
        ▼
BitmapSource.Create → OverlaySurface.SetGlass
```

关键实现点：

- **抓图区域外扩**：现在 `TryGetScreenRect` 只取卡片本身；边带折射需要卡片外的像素，必须把共享抓图区域四周扩 `margin`（至少 `strengthPx + blurRadius`）。共享抓图是按整叠区域算的，扩的时候要保证所有卡片的外扩并集都在里面；屏幕边缘的卡片要么裁剪采样、要么跳过 G1。
- **只处理边带**：中心像素直接复制模糊图；边带宽度 `band` 按 DIP × DPI 换算，并设上限（例如全分辨率 32px），避免 4K/300% 下带宽过大。
- **LUT**：按 `(w, h, radius, band)` 缓存 SDF/位移/高光掩码；尺寸或圆角变化才重建。LUT 只覆盖边带，内存可控。
- **no per-frame alloc**：复用 `CaptureBuffer` 和输出缓冲；`BitmapSource.Create` 仍然会分配，这是现有路径就有的，别再加新的每帧大对象。
- **判同帧**：沿用 `SameAsLastFrame`，但比较的是**处理前**的模糊图；背景没变就不重复处理。参数变化（强度/带宽）要主动失效。
- **输出 alpha**：仍是 `Bgr32`，alpha 不参与；玻璃层的裁剪由 `Border.CornerRadius` 完成。

**建议新增的文件/类型**：

| 位置 | 内容 |
|---|---|
| `src/Ui/LiquidGlass.cs`（新） | `LiquidGlassParams`、`LiquidGlass.ProcessBand`、SDF/采样/高光等纯函数 |
| `src/Ui/LiquidGlassPolicy.cs`（新，可选） | 纯决策：设置 + 环境 + 后端 → 是否允许 G1，返回原因。照 `GlassPolicy.Resolve` 的写法，便于自检 |
| [`GlassFeed.cs`](../../src/Ui/GlassFeed.cs#L525-L583) | `Render` 里增加 `margin`、调用 `ProcessBand`、输出裁剪 |
| [`GlassRuntime.cs`](../../src/Ui/GlassRuntime.cs#L104-L163) | 把 `LiquidGlass` 参数传入 `GlassFeed.Configure`；状态里带 `Liquid` 标记 |
| [`GlassRuntimeState.cs`](../../src/Ui/GlassRuntimeState.cs#L47-L71) | `Describe()` 追加「+ 液态透镜（实验）」；降级原因写进 `Reason` |
| [`Settings.cs`](../../src/Contracts/Core/Settings.cs#L339-L366) | 新增字段与钳制；默认值保持关闭 |
| `SettingsWindow.Appearance.cs` / `.Load.cs` / `.Save.cs` | 实验区 UI；只在 `DesktopFeed` 下可见；写明截图代价 |
| [`SelfTest.cs`](../../src/SelfTest.cs) | 纯函数断言 + 策略断言 |

**状态与失败可见**（遵循 AGENTS §5）：

- 启用成功：`Log.Info("液态玻璃已启用：边带 20px，强度 35%，色散 15%")`；
- 不可用：`Log.Warn("液态玻璃不可用：取图后端为 GDI，已降级为普通 tint / 边缘光")`，并把原因写进 `GlassRuntimeState.Describe()`；
- 运行中连续失败：沿用 `GlassFeed` 的全黑保险丝与 `GlassRuntime` 的降级路径，不要另起一套；
- 不要在每帧 `Log.Warn`；用现有 `SetState` 去重。

### 5.4 参数建议（不是 Apple 原值）

| 参数 | 建议默认 | 范围 | 作用 | 备注 |
|---|---|---|---|---|
| `LensStrength` | 35（百分比） | 0–100 | 边缘最大位移 = `12 * (LensStrength/100)` 全分辨率像素（35 → 4.2px） | 超过 6px 容易「哈哈镜」 |
| `LensBand` | 20px（全分辨率） | 12–32 | 折射影响的边缘带宽 | 半分辨率实现时除以 2；4K/300% 要封顶 |
| `BlurRadius` | 12px（G1 时） | 8–16 | 背景低频化 | 现有默认 18px 更适合纯毛玻璃；G1 要保留一点结构 |
| `Specular` | 35（百分比） | 0–100 | 镜面高光强度 | 指数 24；左上主光 |
| `Chromatic` | 15（百分比） | 0–50 | R/B 位移差 | 默认 <1px 可见差异；过头像廉价 RGB 分离 |
| `AdaptiveTint` | 开 | 开/关 | 根据背景亮度/方差调 scrim | 只在明度上动手，不改色相 |
| `GlassEdge` | 关（先） | 开/关 | G0 边缘光 | 与 G1 独立；G1 默认带 |
| `BlurFps` | 10 | 8–12 | 刷新率 | G1 不要超过 15；帧率线性涨 CPU |

**建议的 UI 暴露**：只给 3 个滑杆（折射强度、边缘高光、色散）+ 1 个开关（自适应着色），不要把 8 个参数全摊开。高级用户要调，改 `settings.json` 即可。

### 5.5 可读性与无障碍契约

这是本文最希望被写进代码约束的一节：

1. **文字永远在第 5 层**，不参与折射/高光/色散；
2. **文字卡片的 scrim 下限 0.35**；G1 在背景亮或花时自动上调，上限 0.92；
3. **只调明度，不调色相**：主题色相是 identity，不能被壁纸带跑；最多 8% 的背景色混合；
4. **高对比度 / 关闭透明效果 / 远程桌面 → 直接回退到不透明 tint**，不要「半透明硬撑」；
5. **减少动效**：`SystemParameters.ClientAreaAnimation = false` 时不做 hover/press 高光动画；只保留静态高光；
6. **不要用颜色表达状态**：现有 `CardStatus` 的语义色已经够用；玻璃 tint 不应抢占 `Accent`/`Danger` 的语义；
7. **对比度验收**：默认主题 + 默认 `backgroundOpacity` 下，正文对最坏背景 ≥ 4.5:1；不达标就抬 scrim，而不是加粗字体。

### 5.6 截图、录屏与隐私

- G1 继承 `DesktopFeed` 的 `WDA_EXCLUDEFROMCAPTURE`：**卡片不会出现在任何截图/录屏里**。这不是 bug，是「自己截屏」的必然代价；设置页必须明写，不能只写在文档里；
- G0 不抓屏，截图正常；如果用户只在意观感、不在意真折射，G0 是唯一不牺牲截图的选择；
- `--screenshot-cards` 是**离屏渲染**（[`Screenshots.cs`](../../src/Ui/Screenshots.cs#L15-L16)），没有真实桌面背景，玻璃层会是空的/黑的；G1 的视觉验收不能只靠它。建议加一个 `MIRRORPOND_GLASS_DEMO=1` 的合成棋盘格输入，专门给 `LiquidGlass` 做无头截图；
- 不要为了「截图里能看到卡片」而关闭截屏排除——那会把卡片拍进玻璃，形成拖影/递归。

### 5.7 主题、配置与 Schema 边界

- **材质不是主题**：[`theme-system.md`](../theme-system.md#L4-L6) 已经把毛玻璃划到「能力」；液态玻璃同样属于能力，参数写 `settings.json` 的 `Style`，**不要**加进 `.mptheme`；
- 主题只提供颜色/圆角/字体；材质读取这些令牌，但不由主题文件控制开关；
- 新增字段（`GlassEdge`、`LiquidGlass`、`LensStrength` 等）都是**带默认值的增量字段**，旧配置缺字段会走默认值，不需要像 v6→v7 那样做一次性迁移；
- 但要在 `Settings.Normalize()` / `StyleSettings.Normalize()` 里钳制范围，并在 [`SelfTest.cs`](../../src/SelfTest.cs) 加断言：非法值/越界值被夹回；`LiquidGlass=true` + `GlassMode=System` 时策略必须拒绝并给出原因；
- 版本记录在 [`version-history.md`](../version-history.md) 补一条；如果改了 `Contracts`，六张内置卡会一起重建，且没有热重载（AGENTS §3）。

### 5.8 反模式清单

| 不要 | 为什么 |
|---|---|
| 每张卡都开 G1 | Apple 明确要求克制；CPU 随卡片数线性上升；观感上也没有层级 |
| 给聊天/命令行正文上折射 | 内容层 + 长文本 + 滚动，最容易晕 |
| 玻璃叠玻璃（卡片内面板、弹出菜单再上玻璃） | Apple 明确不建议；对比度会塌 |
| 用 `clear` 变体压亮背景却不加 35% 暗化 | 文字直接不可读 |
| 每帧改 tint 色相 | 主题 identity 丢失、闪烁 |
| 用 PS 3.0 `ShaderEffect` 做折射 | 分层窗口软件渲染下会被忽略 |
| 为 G1 引入 Win2D / Vortice / 其它 NuGet | 违反零第三方依赖；且与单窗口合成冲突 |
| 把多张卡合并成一个窗口做形状融合 | 破坏拖动、鼠标穿透、置顶、截图排除 |
| 在 UI/宣传里叫「Apple Liquid Glass」 | 这是 Apple 的术语；本项目是独立近似，避免误导与商标风险 |
| 默认开启 G1 却不告诉用户截图会丢卡片 | 违反「失败必须可见」；这是产品级取舍，不能藏在文档里 |

## 6. 分阶段落地

| 阶段 | 内容 | 主要文件 | 规模（估） | 验收 |
|---|---|---|---|---|
| 0 | 本文：调研、决策、参数与边界 | `docs/UI/liquid_glass.md` | — | 评审通过；明确 G1 默认关 |
| 1 | **G0 边缘光**：`_edgeLayer` + `GlassEdge` 开关 + 截图对比 | [`OverlaySurface.cs`](../../src/Ui/OverlaySurface.cs)、[`Settings.cs`](../../src/Contracts/Core/Settings.cs)、`SettingsWindow.*`、[`SelfTest.cs`](../../src/SelfTest.cs) | 1–2 天 | 100/150/300% DPI 截图；圆角无缺口；`--perf` 无差异 |
| 2 | **光学核心**：`LiquidGlass.cs` 纯函数 + SDF/采样/高光/色散 + 自检 + 合成棋盘格截图 | `src/Ui/LiquidGlass.cs`（新）、[`SelfTest.cs`](../../src/SelfTest.cs) | 2–3 天 | 单测：中心位移=0、边缘方向正确、带宽外不变、参数钳制；合成图目视 |
| 3 | **G1 集成**：抓图外扩 margin、边带处理、策略/状态/日志、设置页实验项、截图警告 | [`GlassFeed.cs`](../../src/Ui/GlassFeed.cs)、[`GlassRuntime.cs`](../../src/Ui/GlassRuntime.cs)、[`GlassRuntimeState.cs`](../../src/Ui/GlassRuntimeState.cs)、`SettingsWindow.*` | 3–5 天 | `--perf` 新增 ≤2% 单核（2 张卡/10fps）；`--selftest` 0；GDI/高对比度自动回退；截图提示可见 |
| 4 | **自适应与无障碍打磨**（可选）：亮度/方差 scrim、高对比度/远程/减少动效、电池策略 | `LiquidGlass.cs`、`GlassRuntime.cs`、设置页 | 1–2 天 | 无障碍矩阵通过；无闪烁；对比度 ≥4.5:1 |

**统一收尾**（每个阶段都按项目纪律）：

```bat
dotnet build -c Release          :: 0 警告 0 错误
bin\Release\MirrorpondHUD.exe --selftest
bin\Release\MirrorpondHUD.exe --screenshot-cards glass-check
bin\Release\MirrorpondHUD.exe --perf 15 --visible
```

**Kill criteria（任一条触发就停在 G0）**：

- 2 张卡、10 fps 下新增 >2% 单核，或 4 张卡 >5%；
- 默认主题/默认不透明度下，正文对最坏背景 <4.5:1；
- 边缘折射让背景文字/直线出现「游动」或明显哈哈镜；
- 目标机器上 GDI 回退比例高（GDI 一轮 ~18ms，再加折射会拖慢拖动）；
- 截图丢失引发用户投诉，且设置页提示无法缓解；
- 现有三条毛玻璃路径出现任何回归。

## 7. 仍不确定、需要实测的问题

| 问题 | 为什么重要 | 怎么测 |
|---|---|---|
| Apple 的精确折射曲线/带宽/高光指数 | 决定「像不像」 | 无法拿到源码；只能按官方视频逐帧目视调参，不做「还原」承诺 |
| C# 逐像素边带在 4K/300% 下的真实成本 | 决定 G1 能否在高端屏用 | `--perf` + 合成棋盘格；记录 `ProcessBand` 耗时 |
| 分层窗口在不同 GPU/驱动上的软件渲染行为 | 决定 ShaderEffect 是否永远不可用 | 在多台机器上对比 PS2/PS3 效果；以实测为准 |
| 副屏 DXGI 回退（已知限制） | G1 在副屏可能直接掉到 GDI | 双屏 4K 实测；副屏默认禁用 G1 |
| 电池/节能模式下的收益 | 常驻 HUD 的长期成本 | `GetSystemPowerStatus` + `--perf` 长跑；必要时加「仅插电」开关 |
| 用户是否愿意用「截图里没有卡片」换真折射 | 产品取舍 | 设置页文案 + 用户反馈；默认关，不替用户决定 |
| G0 的边缘光是否已经足够 | 决定是否值得做 G1 | 盲测截图；如果 G0 已能满足，G1 可以不做 |

## 8. 参考资料

**一手（Apple）**

- [WWDC25 · Meet Liquid Glass（219）](https://developer.apple.com/videos/play/wwdc2025/219/) —— 设计原则、lensing、自适应、交互
- [WWDC25 · Get to know the new design system（356）](https://developer.apple.com/videos/play/wwdc2025/356/) —— 新设计系统的整体背景
- [HIG · Materials](https://developer.apple.com/design/human-interface-guidelines/materials) —— `regular` / `clear`、内容层禁令、克制使用、35% 暗化（2025-09-09 更新）
- [Adopting Liquid Glass](https://developer.apple.com/documentation/TechnologyOverviews/adopting-liquid-glass) —— 采用路径、自定义背景、玻璃叠玻璃、无障碍测试
- [Applying Liquid Glass to custom views](https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views) —— `glassEffect`、容器、形变、性能限制
- [SwiftUI · Glass](https://developer.apple.com/documentation/swiftui/glass) / [glassEffect(_:in:)](<https://developer.apple.com/documentation/swiftui/view/glasseffect(_:in:)>) / [GlassEffectContainer](https://developer.apple.com/documentation/swiftui/glasseffectcontainer)
- [UIKit · UIGlassEffect](https://developer.apple.com/documentation/uikit/uiglasseffect) / [AppKit · NSGlassEffectView](https://developer.apple.com/documentation/appkit/nsglasseffectview)

**一手（Microsoft / 平台能力）**

- [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type) —— Mica / Desktop Acrylic / Mica Alt 的定义与最低版本
- [WPF · ShaderEffect](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.shadereffect) —— PS 2.0 软件运行、PS 3.0 软件忽略、sampler 限制
- [WPF · Window.AllowsTransparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency) —— 分层窗口的前提

**本项目**

- [`glass-implementations.md`](../glass-implementations.md) —— 三条毛玻璃实现的实测、DXGI 取图、截图排除
- [`performance-baseline.md`](../performance-baseline.md) —— 2.27.0 / 2.29.0 的 CPU、分配、LOH 基线
- [`theme-system.md`](../theme-system.md) —— 材质与主题的边界
- [`endfield.md`](endfield.md) —— 本项目 UI 调研文档的写法与证据分级参考
- [`OverlayWindow.xaml`](../../src/Ui/OverlayWindow.xaml#L6-L11)、[`OverlaySurface.cs`](../../src/Ui/OverlaySurface.cs#L50-L55)、[`GlassFeed.cs`](../../src/Ui/GlassFeed.cs#L525-L583)、[`GlassRuntime.cs`](../../src/Ui/GlassRuntime.cs#L104-L163)

**二手（仅交叉核对，未采用其参数）**

- [WWDCNotes · Meet Liquid Glass](https://wwdcnotes.fline.dev/documentation/wwdc25-219-meet-liquid-glass/) —— 索引页；正文未完整取到，不引用具体结论

## 附录 A：术语速查

| 术语 | 含义 |
|---|---|
| Lensing | 透镜/折射效果：透明物体让背景在边缘发生几何位移，从而「看起来有厚度」 |
| Regular / Clear | Liquid Glass 的两个变体：`regular` 模糊 + 调亮度，适合文字多的地方；`clear` 高透，适合媒体背景 + 暗化层 |
| Specular | 镜面高光：迎光边缘的亮带，不是均匀白边 |
| Scrim | 可读性压暗/提亮层；本项目里是 `_tintLayer` 的动态透明度 |
| SDF | 有符号距离场；圆角矩形的内外距离，用来算法线与边缘带 |
| Band | 边缘带宽度；只在这个范围内做折射，中心原样通过 |
| G0 / G1 / G2 | 本文的保真度档位：边缘光 / CPU 边带折射 / GPU 真折射 |

## 附录 B：一页决策清单

- [ ] 只给「功能层」上玻璃；正文/列表/命令行不上折射。
- [ ] 一屏最多 1–2 张 G1；默认 0 张。
- [ ] 文字永远在最上层；文字卡片 scrim ≥0.35。
- [ ] 只调明度，不改色相；背景色混合 ≤8%。
- [ ] 高对比度 / 关闭透明效果 / 远程桌面 / GDI 回退 → 自动降级并记录原因。
- [ ] G1 设置页明写「卡片不会出现在截图/录屏」。
- [ ] `--perf` 新增 ≤2% 单核（2 张卡/10fps），否则回退 G0。
- [ ] 默认关闭；G0 先截图验收，再决定是否默认开。
