# 终末地风格 M7 / M8 可行性调研

> 对应 [`endfield.md`](endfield.md) §7 的 M7（循环 / 呼出 / 消失动效风格化）与
> M8（动效与装饰模块化）。本文保留调研与方案取舍；**2.31.0 已按方案 A 落地**，
> 实现细节与性能代价见 [`endfield-m7-m8-implementation.md`](endfield-m7-m8-implementation.md)。
> 基线：MirrorpondHUD 2.30.0；M1–M6 已落地，见
> [`endfield-implementation.md`](endfield-implementation.md)。

## 0. 结论

| 项 | 判断 | 建议 |
|---|---|---|
| M7.1 一次性动效风格化（呼出 / 消失 / 补位 / 对齐） | **可行，成本低**；✅ 2.31.0 已实现 | 四档 `MotionProfile`，默认仍是现在的 160/180ms ease-out |
| M7.2 循环动效（呼吸块 / 扫描线 / 脉搏） | **可行但收益低、风险高**；✅ 2.31.0 已实现可关闭的 Breath / Scanline / Pulse | 默认关闭；不把循环做成整卡纹理动画 |
| M7.3 主题自定义任意缓动 / 时长 | **不建议开放** | 用一个有界档位表，不把 cubic-bezier 字符串暴露给主题 |
| M8 数据驱动的动效 / 装饰扩展 | **可行，中等成本**；✅ 2.31.0 已实现 | 用主题数据 + 固定目录（枚举 + 构建器），不要让主题携带可执行代码 |
| M8 插件式视觉扩展（第三方 DLL 提供动效 / 装饰） | **本期不建议** | ALC / 安全 / 性能 / API 兼容成本都高于收益 |

一句话：**M7 已做一个“有界档位”的版本；M8 已做数据目录，没有做代码插件。**
循环动效默认关闭，持续 GPU 合成代价写在实现记录里。

---

## 1. 现状取证

### 1.1 动效集中在三个地方

| 位置 | 现状 | 对 M7 的影响 |
|---|---|---|
| `src/Ui/CardMotion.cs` | 静态常量：补位 180ms、呼出 / 消失 160ms、位移 8 DIP、帧间隔 15ms；`Enabled` 受「减少动效」和截图抑制控制 | 参数集中，改造成本最低；但它是进程级静态，不能直接表达“每套主题不同” |
| `OverlayWindow.AnimateShowMotion / AnimateHideMotion / AnimateTo` | 窗口物理位置用 `DispatcherTimer` 逐帧 `SetWindowPos`；内容透明度用 WPF `DoubleAnimation` | 一次性动效已经是“有方向、可取消、可瞬时”的结构，适合加档位 |
| `CardStatus` | 忙碌时 90ms `DispatcherTimer` 轮换盲文字符；进度条 3px | 循环动效已有出口，但字符轮换会改变宽度（M6 已建议改分段方块） |
| `OverlayWindow._topmostTimer` | 每窗 2.5s 置顶 + 底板重摆 | 与动效无关，但说明“每窗一个定时器”是既有成本模型 |

### 1.2 现有的安全阀

- `Behavior.ReduceMotion`：主题 / 档位不能绕过；
- `CardMotion.SuppressForCapture()`：截图、`--selftest` 强制关闭；
- `CardMotion.Enabled` 是最终闸门，窗口动画与内容动画都读它；
- 动画取消：`AnimateTo` 会 `StopPositionMotion`，新的目标从当前物理位置重新开始；
- 布局协调器在动画中避免瞬移打断。

这些是 M7 能低风险落地的基础：**只要新档位仍走 `AnimateTo` / `CardMotion.Enabled`，就不需要重写动画调度。**

### 1.3 当前没有的能力

- 主题不能选择动效参数；
- 没有“分段方块脉冲”实现；
- 没有动效的截图模式（现有截图直接关闭动画，验收只能靠录屏 / 肉眼）；
- 没有按卡片类型区分动效（常驻文字卡、媒体卡、交互卡共用一套）。

---

## 2. M7：动效风格化

### 2.1 方案 A（推荐）：有界档位 + 主题可选

在 `StyleSettings` 增加一个动效档位（字符串，归一化），例如：

```csharp
public string MotionProfile = "Current"; // Current / Cut / Industrial / Off
```

`CardMotion` 从静态常量改为“按档位解析出的参数”：

| 档位 | 补位 / 对齐 | 呼出 | 消失 | 位移 | 说明 |
|---|---|---|---|---|---|
| `Current` | 180ms ease-out | 160ms 淡入 | 160ms 淡出 | 8 DIP | 默认，保持现有观感 |
| `Cut` | 90ms 线性 / 近似瞬时 | 90ms 淡入 | 90ms 淡出 | 0 | 工业“切入”，不缩放、不弹跳 |
| `Industrial` | 120ms ease-out | 120ms 淡入 | 120ms 淡出 | 2 DIP | 比 Current 更短、更克制，适合终末地主题 |
| `Off` | 0 | 0 | 0 | 0 | 等价于「减少动效」，但只影响动效档位 |

实现要点：

1. `StyleSettings.MotionProfile` + `Settings.NormalizeMotionProfile()`（未知值回落 `Current`）；
2. `ThemeFile` 增加 `appearance.motion.profile`（可选，缺省不改）；
3. `CardMotion.ApplySettings(settings)` 读取档位，暴露 `LayoutDurationMs / ShowDurationMs / HideDurationMs / ShowOffset / HideOffset`；
4. `OverlayWindow` 改为读 `CardMotion.*` 的实例值（现为 `const`，需要改成只读属性）；
5. `ReduceMotion` 仍然一票否决；截图抑制仍然优先；
6. 设置页「工业风格」组增加“动效档位”下拉（默认 Current）。

成本估算：0.5–1 天（含自检、截图、`--perf` 对比）。

风险：

- `CardMotion` 是静态类，多套主题同时存在时只能有一个全局档位（HUD 本来就是单进程一套设置，可接受）；
- `AnimateTo` 的 15ms `DispatcherTimer` 在 4K 下仍是物理像素逐帧 `SetWindowPos`，档位缩短时长反而降低开销；
- 如果将来做“每张卡独立动效”，需要把参数从静态类搬到 `CardSlot` / `OverlayWindow`，那是 M8 的范围。

验收：

- `--screenshot-*` / `--selftest` 仍强制瞬时；
- `ReduceMotion=true` 时四个档位都瞬时；
- 4K@300% 下呼出 / 消失不出现跨屏跳位（复用 L-13 的物理坐标路径）；
- `--perf` 的 GC / Gen2 不因档位变长而恶化。

### 2.2 方案 B：主题写任意时长 / 缓动

不建议。理由：

- WPF 的 `DoubleAnimation` 缓动函数不是字符串可安全表达的东西（cubic-bezier 需要解析 + 采样）；
- 任意时长会让“减少动效”和“截图抑制”出现更多组合，验收矩阵爆炸；
- 主题作者很难判断 37ms 和 43ms 的差别，但维护者要为此长期兼容。

如果确实需要更细的控制，建议只开放 `durationMs`（限制 60–400）与三个枚举档位，不开放任意曲线。

### 2.3 方案 C：循环动效

只建议做 `CardStatus` 的忙碌指示：

- 现：盲文字符 `⠋⠙⠹…` 每 90ms 轮换；
- 改：固定宽度的分段方块 `▮▮▯`，每 120–160ms 切换，字符宽度不变；
- 默认关闭（`MotionProfile=Current` 保持盲文，或新增 `BusyPulse` 布尔）；
- 卡片隐藏 / 卸载时停表（`CardStatus` 已有 `StopSpin`）。

不做的事情：

- 整卡扫描线 / 纹理位移：会触发每帧重绘，常驻叠层付不起；2.31.0 只做低幅度
  透明度呼吸（WPF 合成器动画，不跑托管每帧逻辑）；
- 玻璃背景循环动画：会与 10fps 取样竞争 UI 线程；
- 鼠标跟随高光：与点击穿透 / 锁定语义冲突。

---

## 3. M8：动效与装饰模块化

### 3.1 现状耦合点

| 耦合 | 位置 | 说明 |
|---|---|---|
| 动效参数静态 | `src/Ui/CardMotion.cs` | 进程级常量 + 两个布尔闸门 |
| 窗口动画硬编码 | `OverlayWindow` | 透明度 + 物理位移 + 15ms 定时器 |
| 装饰构建硬编码 | `OverlaySurface.BuildDecorationBrush` | `switch` 六个固定纹样 |
| 铭牌硬编码 | `CardHeader` | Code / Badge 的位置与字体 |
| 状态色 | `CardStatus` | 已改为读 `ThemeTokens.Success/Warning`，是 M8 的正面样本 |
| 内容换肤 | `ICardStyleAware` | 插件内容控件可原地换肤，但不参与窗口 / 装饰 |

### 3.2 方案 A（推荐）：主题数据 + 固定目录

在主题 schema 里增加数据段（仍 additive，schema 1 不变）：

```json
"appearance": {
  "motion": { "profile": "Industrial" },
  "industrial": { "monoReadouts": true, "cardCodes": true, "decoration": "Grid" }
}
```

代码侧只保留**有界目录**：

- `MotionProfile`：四个档位（见 M7）；
- `Decoration`：六个纹样（M6 已实现）；
- `CardCode` / `MonoReadouts`：布尔开关；
- 未来新增纹样 = 在 `BuildDecorationBrush` 里加一个 case + `NormalizeDecoration` 加一个值，**主题不能注入代码**。

优点：无 ALC / 安全 / 版本兼容问题；`DrawingBrush` 可冻结缓存；主题可 diff、可回滚。

缺点：新增纹样仍要改宿主代码；不能第三方扩展。

### 3.3 方案 B（不推荐）：`IThemeVisualProvider` 插件

设想：主题包携带 DLL，实现 `IThemeVisualProvider` 提供动效 / 装饰画刷。

不建议的原因：

1. 主题文件是用户可下载、可手改的，携带可执行代码会直接把信任边界从“数据”变成“程序”；
2. 需要新的 ALC / 签名 / 版本校验，与现有卡片插件 ALC 是两套生命周期；
3. WPF `Freezable` / `Drawing` 跨 ALC 传递容易踩线程亲和与冻结问题；
4. 性能不可控：第三方可以在 `OnRender` 里做任意事，HUD 是常驻叠层；
5. 主题格式刚稳定，过早开放插件 API 会锁死设计。

如果未来确实需要，建议做“官方签名主题包 + 预编译白名单”，而不是任意 DLL。

### 3.4 方案 C：把动效参数搬到每张卡

只有在“同一屏多张卡需要不同动效”时才值得。当前 HUD 是一套全局设置，且窗口动画由 `OverlayWindow` 统一调度；把参数搬到 `CardSlot` 会增加：

- 设置树字段（每卡 `MotionProfile`）；
- 布局协调器的动画冲突处理（不同卡不同时长）；
- 主题 / 设置窗的覆盖关系（全局档位 vs 每卡覆盖）。

建议等 M7.1 稳定后再评估，不作为本期目标。

### 3.5 建议路线

| 阶段 | 内容 | 成本 | 依赖 |
|---|---|---|---|
| M7.1 | `MotionProfile` 四档 + 主题字段 + 设置页下拉 | 0.5–1 天 | 无 |
| M7.2 | `CardStatus` 分段方块忙碌脉冲（默认关） | 0.5 天 | M7.1 |
| M8.1 | 装饰目录从 `switch` 抽到 `CardDecorationCatalog`（仍固定枚举） | 0.5 天 | M6 |
| M8.2 | 动效参数从 `CardMotion` 抽到 `MotionSettings`（可注入、可测试） | 1 天 | M7.1 |
| M8.3 | 主题 schema 增加 `motion`，做往返 / 校验 / 文档 | 0.5 天 | M7/M8.2 |

**本期不做 M8.3 之后的插件化。** 先让 M7.1 的真实使用数据决定是否值得继续。

---

## 4. 兼容性与风险清单

| 项 | 结论 |
|---|---|
| 主题 schema | additive 字段，旧主题不写就保持默认；`schema` 仍为 1 |
| 插件 API | 不变；M8 的目录化不碰 `ICardModule` / `ICardStyleAware` |
| 减少动效 | 所有档位最终仍受 `Behavior.ReduceMotion` 与截图抑制约束 |
| 多显示器 / 混合 DPI | 动效走 `OverlayWindow.SetPhysicalPosition`，不会回到 WPF `Left/Top` |
| 性能 | 一次性动效只在呼出 / 消失 / 补位时跑；循环动效只做低幅度透明度呼吸且默认关 |
| 可测试性 | `MotionSettings` 纯数据可做表驱动自检；装饰画刷可离屏渲染断言 |
| 回滚 | 档位默认 `Current`，主题不写 `motion` 即保持现状 |

## 5. 验收口径（如果后续实施）

1. `dotnet build -c Release` 0 警告 0 错误；
2. `--selftest` 退出码 0，新增：四档归一化、`ReduceMotion` 覆盖、主题往返、装饰未知值回落；
3. `--screenshot-cards` / `--screenshot-widgets` 目视：风格档位不改变排版；
4. 4K@300% 录屏：呼出 / 消失不跨屏跳位；
5. `--perf 15 --visible`：GC 分配与 Gen2 不高于 2.29.0 同配置基线（循环动效默认关）。
