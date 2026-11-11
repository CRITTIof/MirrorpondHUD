# 终末地风格 M1–M6 落地记录

> 对应 [`endfield.md`](endfield.md) §7 的 M1–M6。全部以**选项**形式实现：
> 默认设置保持原有观感；只有选中「终末地 · 墨 / 纸」主题或手动打开开关时才生效。
> 版本：2.30.0。M7 / M8 的实现见
> [`endfield-m7-m8-implementation.md`](endfield-m7-m8-implementation.md)，
> 调研见 [`endfield-m7-m8-research.md`](endfield-m7-m8-research.md)。

## 0. 一张表看选项

| 项 | 入口 | 默认 | 说明 |
|---|---|---|---|
| M1 主题 | 外观 → 主题下拉 | 自定义 | 新增 `docs/themes/endfield-ink.mptheme`、`endfield-paper.mptheme`，随程序集内嵌 |
| M2 直角 | 外观 → 圆角 | 10 | `cornerRadius = 0` 现在真的得到 0，不再被控件最小值抬回 4–6px |
| M3 状态色 | 外观 → 状态色 → 成功 / 警告 | 空 | 空 = 旧行为（成功=强调色，警告=前景 0.9） |
| M4 等宽读数 | 外观 → 工业风格 → 读数使用等宽数字 | 关 | 只把 0–9 的数字串换成 `MonoFont`，中文 / 单位 / 图标不变 |
| M5 铭牌 | 外观 → 工业风格 → 卡片铭牌 | 关 | `CardHeader` 显示两位编号，徽标改等宽大写；目前用于文件中转站 |
| M6 装饰 | 外观 → 工业风格 → 装饰纹样 | 无 | 网格 / 扫描线 / 45° 斜纹 / 半调网点 / 等高线，低透明度、不参与命中测试 |

两套主题已经带上 `industrial` 段（M4/M5/M6 开启，墨版网格、纸版扫描线），
所以“选中主题”得到的是完整风格；不想用其中某项时，在设置页单独取消即可。
主题文件只写数据，不携带任何可执行代码。

## 1. M1：两套 `.mptheme`

新增：

- `docs/themes/endfield-ink.mptheme` —— 墨版：`#101110` 底、`#F5F5F0` 字、`#FFFA00` 信号黄、圆角 0、无阴影；
- `docs/themes/endfield-paper.mptheme` —— 纸版：`#E8E8E2` 底、`#101110` 字、`#6B5D00` 暗金强调、圆角 0、无阴影。

主题目录由 `MirrorpondHUD.csproj` 的 `EmbeddedResource Include="docs\themes\*.mptheme"` 自动内嵌，
不需要改代码；`ThemeCatalog` 会按 `Name` 排序出现在设置页主题下拉里。
自检的「内置主题」从“至少 3 份”改为“至少 5 份”，并继续解析全部文件。

主题里的新字段：

```json
"colors": { "success": "#4FBF5C", "warning": "#FFB020" },
"industrial": {
  "monoReadouts": true,
  "cardCodes": true,
  "decoration": "Grid"
}
```

## 2. M2：圆角下限跟随配置

新增 `CardPalette.Corner(radius, minimum)`：

```csharp
public static double Corner(double radius, double minimum)
{
    if (radius <= 0.01) return 0.0;
    return Math.Max(minimum, radius);
}
```

替换的调用点：

- `CardDropOverlay`（6 → 跟随）
- `CardOverlayControls`（Modal 6 / Popover 6 / Toast 8）
- `CardDataControls`（Badge 2 / Gauge 6 / Ring 4）
- `CardTextBox`（2）
- `CliUi` 的内部小面板（`corner - 3`，最小 3）
- `CardStatus` 的 3px 进度条：直角主题下圆头变 0，变成分段方块

`CardChartControls` / `CardTreeTimeline` / `CardButton` / `CardMediaView` 原本就允许 0，未改。
`CardStatus.ApplyStyle` 增加可选 `cornerRadius` 参数，StationView 与截图传入当前圆角。

## 3. M3：`Success` / `Warning` 令牌

- `StyleSettings.Success` / `Warning`，空串合法；`Normalize` 会把非法值回落为空；
- `ThemeTokens.Success` 空 = `Accent`，`ThemeTokens.Warning` 空 = 前景 0.9，**与旧行为逐值一致**；
- `CardStatus` 的 Success 图标、Warning 图标与 Success 进度条改读令牌；
- 设置页「状态色」两个文本框，空串合法，非法值内联标红；
- 主题文件 `colors.success` / `colors.warning`，导出时非空才写入，避免把空串当非法颜色。

默认不填，所以现有用户不选主题时状态色完全不变。

## 4. M4：等宽读数

- `TextCanvas` 新增 `MonoFont` / `MonoNumbers`；
- 启用时 `KindOf` 把 `0–9` 归类为 `RunKind.Number`，`CreateRun` 用 `MonoFont` 排版；
- 数字 run 仍走正文基线，不参与键帽 / 图标的墨迹居中；
- 关闭时 `KindOf` 完全不产生 Number run，输出与改动前一致；
- `OverlaySurface.Apply` 从 `ThemeTokens.MonoFont` 设置，设置页开关控制 `MonoNumbers`；
- 缓存快照包含 `MonoFont` / `MonoNumbers`，切换选项会整表作废。

限制：只覆盖 `0–9`；小数点、`%`、`/`、单位仍走正文字体。当前指标卡的小数点宽度差异很小，
主要收益是数字本身等宽；若以后要覆盖标点，需要把 `.%/` 等也纳入 Number run（会改变排版，需单独评估）。

## 5. M5：`CardHeader` 铭牌

- `CardHeader` 新增 `Code`（两位编号，如 `03`），显示在标题左侧；空 = 不显示；
- 徽标统一转大写，启用铭牌时用 `MonoFont`，并设置 `Typography.NumeralAlignment = Tabular`；
- `StationView.ApplyStyle` 在 `Style.IndustrialHeader` 打开时，按卡片在 `Settings.Cards` 里的位置填 `01` 起编号；
- `Screenshots.CaptureWidgets` 在演示里填 `03`，用于回归截图。

只有使用 `CardHeader` 的卡片（当前是文件中转站）有可见编号；信息卡 / 命令行 / 媒体卡没有
`CardHeader`，不会被强行加一行编号。

## 6. M6：装饰纹样

- `StyleSettings.Decoration`：`None / Grid / Scanline / Hatch / Halftone / Contour`，未知值回落 `None`；
- `OverlaySurface` 增加一个不参与命中测试的 `Border` 装饰层，位于底色 / 背景图之上、文字 / 富内容之下；
- 用冻结的 `DrawingBrush` 平铺：
  - `Grid` 24px 1px 线；
  - `Scanline` 4px 横线；
  - `Hatch` 12px 45° 斜线；
  - `Halftone` 8px 圆点；
  - `Contour` 96px 波形等高线 + 两个偏心闭合环（不是同心圆，平铺接缝连续）；
- 整层 `Opacity = 0.32`，颜色取 `ThemeTokens.Border`（默认前景 0.10 alpha）；
- 圆角跟随卡片 `CornerRadius`；`None` 时整层折叠，不产生绘制。

设计取舍：纹样是“坐标系暗示”，不是背景纹理；32% 是截图对比后压下来的值，
再亮就会和文字抢注意力。斜纹只建议用在危险 / 施工语义，不作为默认背景。

## 7. 验证

- `dotnet build -c Release`：**0 警告 0 错误**；
- `--selftest`：**退出码 0、失败项 0**；新增/更新：
  - 状态色空令牌精确等于旧派生值；
  - `CardPalette.Corner(0, 6) == 0`，负值也归 0；
  - 非法 `Success` / `Warning` / `Decoration` 归一化；
  - 主题往返包含 `success` / `warning` / `monoReadouts` / `cardCodes` / `decoration`；
  - 非法 `decoration` 主题被拒绝；
  - 内置主题至少 5 份；
- `--screenshot-cards` / `--screenshot-settings` / `--screenshot-widgets`：退出码 0；
  墨版截图确认：圆角 0、网格装饰、等宽数字、`03 文件中转站` 铭牌、黄色进度条；
- 设置页显示 v2.30.0，工业风格组的三项开关与状态色输入可见。

## 8. 已知边界

- M2 只修“控件自己的最小圆角”；视频窗口的 GDI region、媒体控件内部的 1.5px 小圆角仍按各自设计走，
  不属于卡片外框；
- M4 只覆盖 TextCanvas 的数字 run，富内容控件（CLI 面板、列表）里的数字仍按控件自己的字体；
- M5 的编号只在 `CardHeader` 消费者里生效；信息卡没有标题栏，不会被加编号；
- M6 的 `DrawingBrush` 是静态平铺，不随卡片尺寸缩放；4K 下 1px 线会按 DPI 缩放，视觉上仍细；
- 两套主题的 `success` / `warning` 是本文按对比度选的，未做完整 WCAG 全组合复算；
- 主题仍只描述外观，不改变材质 / 取图后端 / 交互复杂度。
