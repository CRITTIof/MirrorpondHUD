# 终末地 M2–M8 实现记录

> 对应 [`endfield.md`](endfield.md) §7 与本地规范镜像 `samples/EndfieldDesign`。
> 版本：2.35.0 首次落地，2.36.0 拆成可分别关闭的选项并补媒体 / 中转站 / 页脚入口。
> 本文记录**当前 M2–M8**；2.30 的旧 M2–M6（圆角 / 状态色 / 等宽 / 铭牌 / 纹样）是历史编号，
> 其中铭牌已在 2.33.0 删除，不要与这里混用。

## 1. 一张表

| 项 | 规范 | 2.35.0 实现 | 2.36.0 开关 |
|---|---|---|---|
| M2 零偏移阴影 | `0 0 26px` 黑约 28% | `OverlaySurface`：`ShadowDepth=0, Opacity=0.28` | `Style.Endfield.Shadow` |
| M3 选中态 | 左缘 3–4px 黄条 + 实心底 | `CardList` / `CardTable` / `CardTree` / `CardTimeline`：3px 强调条 + `Accent` 24% 底 | `Style.Endfield.SelectionBar` |
| M4 按钮 | 4px 微圆角、主行动实心、不围整圈 | `CardButton` 自绘底形；Primary 实心 + 对比字色，Normal 深色块，Danger 软红块 | `Style.Endfield.Buttons` |
| M5 进度 | 直角分段块 | `CardSegmentBar`（28 段 / 2px 间隔），`CardStatus` 使用 | `Style.Endfield.SegmentProgress` |
| M6 铭牌 | 编号 + 面包屑 + 系统页脚 | `CardMeta` + `CardHeader.Code` + `CardFooter` | `Style.Endfield.Meta` + `FooterText` |
| M7 角括号 | 2px / 12–14px / 外扩 4px | `CornerBrackets`：媒体异常、`CardStatus` 错误/警告 | `Style.Endfield.CornerBrackets` |
| M8 切角 | 45°、6/10/16、主行动与角标 | `CutCornerBorder` 用于 `CardHeader` 角标（6px）；`CardButton` Primary 用 10px 右上切角 | `Style.Endfield.CutCorners` |

关闭某一项时回落到 **2.34.0 的旧实现**，不是“关掉就没样式”：

- M2 关闭：`ShadowDepth=4`、`Opacity=0.55` 的顶部投影；
- M3 关闭：只保留 `Accent` 16% 选中底，不画左缘条，内容也不再空出 4px；
- M4 关闭：`CardButton` 回到细描边 + 悬停淡色模板，圆角跟随卡片（最大 7px）；
- M5 关闭：`CardStatus` 回到 3px 半圆端头进度；
- M6 关闭：所有卡片不显示编号 / 页脚，`CardFooter` 整块折叠，不留下空白条；
- M7 关闭：角括号不出现；
- M8 关闭：主行动按钮与角标回到圆角矩形。

## 2. 几何与文本原语

- [`src/UiKit/CornerBrackets.cs`](../../src/UiKit/CornerBrackets.cs)：自绘 8 条 2px 短线，臂长 13px；
  不参与命中测试；调用方用负 Margin 实现“外扩 4px”。
- [`src/UiKit/CutCornerBorder.cs`](../../src/UiKit/CutCornerBorder.cs)：`Decorator` + `StreamGeometry`
  画 45° 切角；只裁背景，不裁子内容，所以文字和焦点环不会被一起切掉。
- [`src/UiKit/CardSegmentBar.cs`](../../src/UiKit/CardSegmentBar.cs)：直角分段进度条，段数与间距可控。
- [`src/UiKit/CardFooter.cs`](../../src/UiKit/CardFooter.cs)：1px 发丝线 + 左右小字；两段都为空时整块折叠。
- [`src/UiKit/CardMeta.cs`](../../src/UiKit/CardMeta.cs)：按 `Settings.Cards` 顺序生成 `// 03 / `；
  页脚模板支持 `{version}` / `{monitor}` / `{code}` / `{name}`，空模板走
  `v{version} · MON {monitor}`。
- [`src/UiKit/EndfieldVisuals.cs`](../../src/UiKit/EndfieldVisuals.cs)：七个开关的唯一读取入口，
  旧配置缺 `Style.Endfield` 时统一回落到“全开”。

## 3. 信息 / 文本卡与页脚

- `infotext` 是纯文字卡：`CardMeta.Enabled` 对它始终返回 false，因此不显示页头页脚；
  普通 `metrics` / `clock` / `keys` / `text` 预设都走同一条规则。
- 富内容卡（CLI / 媒体 / 中转站）仍显示编号与页脚，但页脚内容可以自定义：
  「外观 → 终末地视觉 → 页脚内容」留空 = 默认；只改页脚时宿主走 `ApplyStyle`，
  聊天 / 命令行 / 媒体都会重新调用 `SetMeta`，不会停留在旧文案。
- 页脚模板最长 160 字符；未知占位符原样保留，方便用户发现拼写问题。

## 4. 媒体卡统一

媒体卡在 2.36.0 改成和对话卡同构的骨架：

`CardHeader`（编号 + 文件名 + 视频/图片角标）→ 分隔线 → 画面 → `CardStatus`
（播放/暂停、时间、倍速、音频备注、分段进度）→ `CardFooter`。

同时拉开标题、画面、状态、页脚之间的间距；正常画面不再常驻四角括号，
只有加载失败 / 空状态才显示 M7 角括号。

## 5. 验证

- `dotnet build -c Release`：**0 警告 0 错误**；
- `--selftest`：**退出码 0、失败项 0**（含 M6 开关、页脚模板、按钮/进度旧分支、中转站视图归一化）；
- `--screenshot-cards`：信息 / 文本卡不显示页头页脚；CLI / 中转站显示 `v2.36.0 · MON 01`；
- `--screenshot-cards` + `MIRRORPOND_ENDFIELD_OFF=1`：全部关闭后回到旧按钮 / 旧进度 / 无页脚；
- `--screenshot-settings`：外观页「终末地视觉」七个开关与页脚输入框可见；
- `--screenshot-station`：大图标 / 中图标 / 小图标 / 列表四种显示方式；
- 未做：真人点击手感、ARM64 实机、4K 下切角抗锯齿目视。

## 6. 已知边界

- 切角按钮的**点击区域仍是矩形**（规范要求如此，避免锐角缩点击区）；焦点环画在形状外侧。
- 角括号只用于媒体异常和状态错误/警告；列表 / 树 / 表使用 M3 的左缘条，避免同一选中态叠两种记号。
- “聊天行”当前没有可选中模型，M3 的选中条先覆盖列表 / 表格 / 树 / 时间线；CLI 记录面板仍按宿主样式。
- 中转站的大 / 中图标视图使用 `WrapPanel`，不再虚拟化；上限 500 条时由 `MaxEntries` 控制总量，
  超长列表仍建议用列表 / 小图标视图。
