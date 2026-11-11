# 终末地 M2–M8 实现记录

> 对应 [`endfield.md`](endfield.md) §7 与本地规范镜像 `samples/EndfieldDesign`。
> 版本：2.35.0（在 2.34.0 现代 C# 基线上实现）。
> 本文记录**当前 M2–M8**；2.30 的旧 M2–M6（圆角 / 状态色 / 等宽 / 铭牌 / 纹样）是历史编号，
> 其中铭牌已在 2.33.0 删除，不要与这里混用。

## 1. 一张表

| 项 | 规范 | 实现 |
|---|---|---|
| M2 零偏移阴影 | `0 0 26px` 黑约 28% | `OverlaySurface`：`ShadowDepth=0, Opacity=0.28` |
| M3 选中态 | 左缘 3–4px 黄条 + 实心底 | `CardList` / `CardTable` / `CardTree` / `CardTimeline` 的 item 模板：3px 强调条 + `Accent` 24% 底 |
| M4 按钮 | 4px 微圆角、主行动实心、不围整圈 | `CardButton` 改为 `OnRender` 画底形；Primary 实心 + 对比字色，Normal 深色块，Danger 软红块 |
| M5 进度 | 直角分段块 | 新增 `CardSegmentBar`（28 段 / 2px 间隔），`CardStatus` 使用 |
| M6 铭牌 | 编号 + 面包屑 + 系统页脚 | `CardMeta` + `CardHeader.Code` + `CardFooter`；纯文字卡由 `OverlaySurface` 画，富内容卡由 CLI / 媒体 / 中转站自带 |
| M7 角括号 | 2px / 12–14px / 外扩 4px | `CornerBrackets`：媒体画面四角、`CardStatus` 错误/警告状态；只用于 2 处 |
| M8 切角 | 45°、6/10/16、主行动与角标 | `CutCornerBorder` 用于 `CardHeader` 角标（6px）；`CardButton` Primary 用 10px 右上切角 |

## 2. 几何原语

- [`src/UiKit/CornerBrackets.cs`](../../src/UiKit/CornerBrackets.cs)：自绘 8 条 2px 短线，臂长 13px；
  不参与命中测试；调用方用负 Margin 实现“外扩 4px”。
- [`src/UiKit/CutCornerBorder.cs`](../../src/UiKit/CutCornerBorder.cs)：`Decorator` + `StreamGeometry`
  画 45° 切角；只裁背景，不裁子内容，所以文字和焦点环不会被一起切掉。
- [`src/UiKit/CardSegmentBar.cs`](../../src/UiKit/CardSegmentBar.cs)：直角分段进度条，段数与间距可控。
- [`src/UiKit/CardFooter.cs`](../../src/UiKit/CardFooter.cs)：1px 发丝线 + 左右小字。
- [`src/UiKit/CardMeta.cs`](../../src/UiKit/CardMeta.cs)：按 `Settings.Cards` 顺序生成 `// 03 / `，
  页脚只用真实版本与显示器序号，不编造延迟 / UID。

## 3. 与旧外观设置的关系

M2–M8 是**默认视觉语言**，不新增开关；仍受现有主题 / 颜色令牌控制：

- `Style.Accent` 决定选中条、切角角标、主行动实心块；
- `Style.Foreground` 决定次级按钮、角括号、段进度轨道；
- `Style.Success/Warning/Danger` 决定状态色；
- `Overlay.Shadow` 只决定是否有阴影；开启时现在是零偏移环境光；
- `Style.CornerRadius` 不再强行套到按钮：按钮固定 4px 微圆角，主行动用切角；
- `Style.MotionProfile` 等原有动效设置不变。

## 4. 验证

- `dotnet build -c Release`：**0 警告 0 错误**；
- `--selftest`：**退出码 0、失败项 0**（含上述控件自检）；
- `--screenshot-cards`：纯文字卡显示 `// 01 / 标题` 和 `v2.35.0 · MON 01`；CLI / 中转站显示编号与页脚；
  中转站选中行左缘黄条、分段进度、切角角标可见；
- `--screenshot-widgets`：共享控件自检截图覆盖角标、选中条、分段进度、角括号；
- 未做：真人点击手感、ARM64 实机、4K 下切角抗锯齿目视。

## 5. 已知边界

- 切角按钮的**点击区域仍是矩形**（规范要求如此，避免锐角缩点击区）；焦点环画在形状外侧。
- 角括号只用于媒体画面和状态错误/警告；列表 / 树 / 表使用 M3 的左缘条，避免同一选中态叠两种记号。
- 纯文字卡的编号/页脚来自宿主渲染层；富内容卡由各自视图画，避免在内容层上重复叠字。
- “聊天行”当前没有可选中模型，M3 的选中条先覆盖列表 / 表格 / 树 / 时间线；CLI 记录面板仍按宿主样式。
