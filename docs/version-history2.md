# 版本修改记录（2.45.0 → ）

> 承接 [`version-history.md`](version-history.md)：那边记录到 v2.44.2（82 节，已超过 3000 行），此后不再追加，避免单文件继续膨胀。
> 统计口径与旧文档一致：`git diff --shortstat <上一版>..<本版>`，文件行的 `+ / -` 是实际增删行数；版本号唯一来源是 `Directory.Build.props` 的 `<Version>`。
> 状态：这些版本均在功能分支 `review/2.45.1-fixes` 上完成，**尚未合并 main、未打 tag、未发布 Release**。

## 0. 总览

| 版本 | 日期 | 提交 | 主题 | 改动规模 |
|---|---|---|---|---|
| v2.45.0 | 2026-11-12 | `38c07ac` `75e6ea0` | 2.44.2 审阅 P1 全量修复 + samples 排除列表 | 29 文件，+511 / −154 |
| v2.45.1 | 2026-11-12 | `82a955e`…`f7aa73a`（29 个提交，含合并） | 2.44.2 审阅 P2 87 项 + P3 62 项全量修复；构建门禁、日志异步化、插件 contractsVersion、工程改名 | 150 文件，+11,627 / −1,505 |
| v2.45.2 | 2026-11-13 | `d99dc2d` `20f7340` `6e2497a` 等 | Endfield 风格自绘对话框、行为页日志等级、粒子变化范围收窄、AGENTS 精简 | 24 文件，+1,862 / −216 |
| v2.45.3 | 2026-11-13 | `e86e446` `4f45ac0` `3e03c9d` `3d11541` 等 | 弹窗改设置窗圆角并区分内外明暗、取色器风格化、设置窗动效、粒子动态掩码、黑色 ICO 重制 | 24 文件，+2,168 / −189 |
| v2.45.4 | 2026-11-13 | `2e2a20b` `3777ba1` `1c28e5c` `80a7fe7` `40c499b` 等 | 玻璃冻结、焦点环、等高线低速卡顿、拖到底部吸回、信息卡偶发缺字 | 11 文件，+1,165 / −185 |
| v2.45.5 | 2026-11-13 | `52e209c` | 卡片折叠动效同步、信息卡省略号误判、DeepYellow 图标、底栏渐变、亮色分隔线、弹窗外描边与去符号 | 12 文件，+174 / −21 |
| v2.45.6 | 2026-11-13 | `ee51124` | 字体切换思源黑体（嵌入 Noto Sans SC）、行为页“仅桌面/置顶”显示模式 | 17 文件，+168 / −41（另新增 17.7 MB 字体） |
| v2.46.0 | 2026-11-13 | `b4f34a8` | 修复省略号误裁与仅桌面下富内容置顶；外观包（AppearancePack/Catalog/Runtime）前期脚手架 | 9 文件，+268 / −33 |

---

## 1. v2.45.0（P1 修复与排除列表，未合并 main）

### 背景

2.44.2 深度审阅给出 175 项问题（P1 26 / P2 87 / P3 62）。P1 集中在“失败被静默吞掉、进程可能被拖死、配置可能被覆盖”三类：
即时模式在实例已运行时静默返回 0、UI 异常 `Handled=false` 与 `Shutdown(1)` 自相矛盾、配置键大小写绕过只读保护、
`ExtrasAs` 返回副本导致插件改动不落盘、热键二次落盘制造假成功、托盘左键因常量写错永远无效、DXGI 被 OS 版本门控等。

### 改动

1. **App 生命周期**：`--selftest`/`--screenshot` 检测到已有实例直接退出 2；UI 异常改为 `Handled=true` 后 `Shutdown(1)`。
2. **配置存储**：`FindKey` 大小写不敏感（schemaversion 不再绕过只读）；只读提示改为“需重启”；自动落盘失败进 `LastLoadWarning`；`.broken-*` 只留 3 份；换行裁剪。
3. **契约健壮性**：`ExtrasAs<T>` 首次绑定即缓存并始终返回活引用；`TemplateEngine` 对齐宽度夹取 1024；`SecretStore.Protect` 失败返回 null + `LastError`；`Json` 未知键审计；`CardStateStore` 上限与保留设备名。
4. **设置/托盘/输入**：热键注册与落盘只做一次；`KeyboardHook.Dispose` 只在 Join 成功时释放；托盘 `NIN_SELECT/KEYSELECT` 常量修正并加 v3 鼠标回退；Explorer 重启后重建图标。
5. **玻璃/媒体/插件**：移除 DXGI 构建号门控（19/22 双下标 + `GetDesc` 校验）；`Frames` 加锁；30s 重试；`PrintWindow` 看门狗；`WaveOutPlayer` 读满整块；`RecycleBin` 加 `FOF_WANTNUKEWARNING`；`PdhQuery` 失败不再拖垮启动；DropStation 删除/大小移入工作线程。
6. **仓库**：`.gitignore` 更新排除列表（samples 等），源码裸 NUL 改为 `\0`。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest` 退出 0；`--selftest --isolated` 不碰用户配置。
- 该轮 26 条 P1 全部处理（20 条 `[x]`、6 条 `[~]` 需真机）。

### 风险 / 未验证

- 6 条 `[~]` 依赖真机：Win10/24H2 的 DXGI、截屏排除、挂起窗口看门狗、音频设备、网络盘回收站。
- 旧配置若处于只读状态，本轮只保证“不静默丢失”，仍需要用户重启后再编辑。

---

## 2. v2.45.1（P2/P3 全量审阅修复，未合并 main）

### 背景

P2 87 项与 P3 62 项覆盖玻璃线程、媒体状态机、文本布局、插件加载、配置语义与跨层治理。用户明确：项目仍在开发、
**以最终效果优先、不做旧版兼容垫片**。因此公共 API 直接改签名并同步所有调用点，而不是加 Obsolete 重载。

### 改动

1. **契约/核心**：`AsyncProcess` stdout 严格 UTF-8 失败回退 OEM、超时保留部分输出、Delay 可取消；自检按 30 个领域累加失败并输出 `FAIL:`；`Log` 改有界队列 + 单写线程 + 退出 `Flush(2s)`；主题 Id 改稳定文件标识并缓存；盘符统一大写/UNC 保留；损坏备份加毫秒；模板 NaN/∞ 回落 `n/a`；WelcomeText 按 text element 截断。
2. **设置/托盘**：开机自启失败透传设置窗；主题预览 160ms 防抖；判脏改副本读取 + 新 `ICardEditorPendingEdits`，放弃修改从 live 重载；热键 id 回收池、回调异常隔离；字体后台预热；CLI 逐框校验；截图参数按输出路径校验；托盘图标自有句柄、气泡失败可见。
3. **玻璃/渲染**：窗口候选快照 + pid 缓存移出 UI 线程；全黑熔断改由 GetDC/BitBlt 失败证据驱动；唤醒事件提前到互斥体之前；全局单一置顶调度；显示器设备名持久化；`GetExStyle` 改 `nint`、关键 Win32 返回值检查；复用 WriteableBitmap、隐藏停泵 + StateChanged 兜底。
4. **文本/布局**：等高线降频/复用几何、单网格上限 140、键帽区间常量同源；粒子缓冲复用与幂等 Finished；按 text element 隐藏/光标；工作区夹取与高度动画；截图原子写 + 深拷贝演示设置；排版缓存近似 LRU + 光标单独缓存。
5. **媒体**：GIF 续播/跳过原因实例化；音频退出原因上报；视频完成后状态收尾；分层窗口不透明度与 ffmpeg 合成降级；跨屏 DPI 换算；`SetSpeed` 返回 bool 并回滚显示；图片/GIF 后台解码；ffmpeg 后台查找；帧池与 WriteableBitmap 复用；CardDialog 焦点陷阱。
6. **插件**：`contractsVersion` 进 manifest 并校验；InfoText 显式 Preset；Media 设置探测去副作用；共享 `CardEditorNotifier`；中转站异步探针/链接记账/行复用与异步图标。
7. **跨层**：6 个同名 `plugin.csproj` 按程序集名重命名；`global.json` 固定 SDK 8.0.4xx；`TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + `AnalysisLevel=latest`；`publish.bat` 不再搬用户数据；`docs/review` 正文与清单入库；签名口令改环境变量；空 catch 从 141 降到 33 并加 CI 棘轮。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest` / `--selftest --isolated` 均退出 0；xUnit 12/12。
- `dotnet format --verify-no-changes`：通过；`--screenshot-cards/settings/widgets`：渲染正常。
- 清单 `docs/review/review-2.44.2-checklist.md`：P1 26/26、P2 87/87、P3 62/62（50 `[x]`、11 `[~]`、1 `[-]`）。
- 单 RID 发布实测：exe + 6 个 manifest（2.45.1）+ 用户 `settings.json` 原地保留。

### 风险 / 未验证

- `[~]` 共 42 条（P2 31 + P3 11），集中在多显示器/混合 DPI、音频热拔、视频 z-order、网络盘、锁屏熔断、托盘与字体环境，需要真机逐项确认。
- `ReadLoop`、`TryReadInto`、`ThemeFile.ApplyTo/TryParse` 仍是 `fuck-u-code` 的高复杂度热点，本轮只修分配与状态机，未做大函数拆分。

---

## 3. v2.45.2（Endfield 对话框、日志等级与粒子收窄，未合并 main）

### 背景

系统 `MessageBox` 与整体设计语言脱节；日志只能靠环境变量切 Debug，缺少运行期可调的“最小等级”；
粒子聚字在只变一两个字符时仍按整卡采样。另把 AGENTS.md 精简到可执行约束。

### 改动

1. **全部系统弹窗风格化**：新增 `UiKit.HudDialog`（宿主与插件复用），按 Endfield 规范（中性灰阶、信号黄填充 + 墨字、直角 + 右上 10px 切角、角色色方形标记、3px 竖条）替换 12 处 `MessageBox.Show`；支持 Ok / OkCancel / YesNo / YesNoCancel、Info/Warning/Error/Question、危险确认 `danger:true`、Enter/Esc/Tab 循环与 owner 居中；创建/显示失败会回退最小窗口并记录，不静默消失。浅色主题的强调条用 paper 版 `#B7A400`，避免亮黄在浅底不可见。
2. **日志最小等级**：「行为 → 日志等级」新增 Debug/Info/Warn/Error，默认 Info；`Log.SetMinLevel` 在配置加载与保存应用时同步，过滤发生在入队前；`MIRRORPONDHUD_DEBUG=1` 仍可强制 Debug。
3. **粒子变化范围收窄**：`TextCanvas` 新增只读的文本元素矩形查询（按 StringInfo 吸附，代理对/组合字符安全）；`ParticleTextLayer` 只对变化元素并集 + padding 建 RTB、PushTransform 到区域原点，像素预算仍按整卡算；`ChangedIndices` 对不等长文本先按行对齐、行内再取公共前后缀窗口，行数不同才退回整段。取不到区域、面积 ≥90%、T-7 裁剪模式等情况一律退回整卡并 Debug 记录。实测单字形变化采样像素 6355→840（13%），单行多字段变化收益较小（82–85%）。
4. **AGENTS.md 精简**：保留程序集边界、风格红线、失败可见、存储/线程/插件/主题约束与提交纪律，删除历史铺陈；新增日志等级与 `HudDialog` 约定，版本记录改指向本文。
5. 新增 `--screenshot-dialog` 自查模式，输出四种对话框变体供目检。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest --isolated` 退出 0；xUnit 12/12。
- 设置窗 `--screenshot-settings`：行为页“诊断 → 日志等级”渲染正常，默认“常规”。
- 日志过滤实测：临时把 `Behavior.LogLevel` 设为 `Error` 跑 `--smoke 2`，配置加载后的 INFO 全部消失，仅保留加载前的启动/插件日志；配置已恢复。
- 空 catch 棘轮修正注释误报后，真实代码计数 31，基线同步收紧。
- `--screenshot-dialog` 暗色/亮色各出图一次：直角 + 右上 10px 切角、3px 强调条、黄底墨字主按钮、
  描边次按钮、角色色标与关闭 × 均正常；浅色主题的强调条已改用 paper 版 `#B7A400`，避免亮黄在浅底不可见。
- 粒子真实冒烟（临时 TextEffect=Particle）：改动前 154×40 卡每轮固定 6355 px / 0.80 ms，改动后变化区域 840 px（13%）/ 0.54 ms；
  34 次收窄采样的整卡 alpha 墨迹包围盒全部落在区域内（0 次漏墨）；指标卡单行多字段同时变时仍为 82–85%，作为后续优化项。
- `--screenshot-cards` 确认文本/图标/键帽与 v2.45.2 版本号显示正常。

### 风险 / 未验证

- 对话框需真机确认多显示器/混合 DPI 下的居中与切角像素对齐、键盘焦点循环。
- 粒子包围盒采样在图标/键帽混排、跨行、代理对场景需真实卡片冒烟；取不到 bounds 时退回整卡，性能收益会回落。

---

## 4. v2.45.3（弹窗圆角与取色器、设置窗动效、粒子动态掩码、黑色 ICO，未合并 main）

### 背景

设置窗此前只有卡片列表的拖动 / 折叠动画，窗口开场、切页、折叠分组、左侧导航与明暗切换全是硬切，
与 [`endfield-motion.md`](UI/endfield-motion.md) 的「短、直、不过冲」和 [`endfield.md`](UI/endfield.md) §3.7 不一致。
同时，自绘弹窗还是切角风格、与设置窗 22px 圆角不统一；设置窗之外的弹窗（权限受限 / 托盘不可用 / 启动失败）应与设置窗区分、
统一明亮且不显示“！”“？”图标；取色仍调用 WinForms `ColorDialog`；粒子首播会把模板字面量和硬件图标一起卷进粒子；
黑色 ICO 在 16–48px 下发糊、边缘发灰。本轮一并处理。

### 改动

1. **新增 `src/Ui/SettingsMotion.cs`**：设置窗动效的唯一入口。时长令牌集中为常量
   （入场 200ms / 退场 120ms / 切页 180ms / 分组 200ms / 导航 140ms / 明暗 160ms / 状态行 150ms），
   缓动统一 `QuadraticEase(EaseOut)`（不过冲）；位移用的 `TranslateTransform`、窗口缩放用的 `ScaleTransform`
   按元素缓存在 `ConditionalWeakTable`，重复触发不重新分配。
2. **入场**：窗口变为可见时整窗 Opacity 0→1、Scale 0.985→1（`RenderTransformOrigin` 居中，不移动窗口位置），
   200ms；隐藏后重新 Show 同样播。缩放只作用于内容根，不影响 `UpdateFrameClip` 的圆角裁切。
3. **退场**：`OnClosing` 的隐藏分支先淡出 120ms，Completed 里再 `Hide()` + `ReleaseTransientState()`；
   动效关闭或窗口本就不可见时立即隐藏。淡出带作废令牌，淡出途中重新打开不会把新窗口藏掉；
   `CloseForReal` / `CloseForShutdown`（退出软件）不经过这条路径，完全不等待动画。
4. **切页**：`TabSelectionChanged` 对 `Tabs.SelectedContent` 淡入 180ms + 8px 水平位移归位。
   切走的页由 TabControl 模板（只呈现 `SelectedContent`）立刻摘掉，不会叠影；连点快速切换时
   后一次动画直接替换前一次时钟，最后一页为准。
5. **折叠分组**：`SettingsWindow.Sections.cs` 为每个 `Expander` 挂 `Expanded/Collapsed`，对模板部件 `Body`
   做 200ms 高度动画 + `ClipToBounds` 裁切：展开与收起都从**当前实际高度**起算（连点从半路折返），
   收起动画结束才 `ClearValue(Visibility)` 交回模板触发器真正折叠；结束时清掉本地高度，下次展开重新按内容量高度。
6. **左侧导航**：`SettingsNavItem` 模板新增 `HoverFill` 叠层，悬停 / 选中底色与 3px 指示条改为
   「不透明度 + `TranslateTransform`」140ms 过渡（指示条从 -3px 滑到 0），不再用触发器瞬间切 `Visibility`；
   只动不透明度和 Transform，不参与布局，所以文字位置不抖。正文字色 / 字重仍由触发器在选中那一刻切换
   （状态不只靠动效表达）。开场由 `NavList.Loaded` 直接落终态，不播选中动画。
7. **明暗切换**：`ToggleSettingsThemeClick` 换色后给 `FrameContent` 做 160ms 不透明度起伏（1→0.84→1）。
   只动内容不动整窗：窗口 `AllowsTransparency`，整窗变淡会直接透出桌面；底板上同时已换色，不会闪白 / 闪黑。
8. **状态行**：用 `DependencyPropertyDescriptor` 监听 `StatusText.Text`，文案更新时淡入 150ms
   （一次接管约 30 处赋值点，不必逐个改调用）。只淡入、从不淡出，失败 / 警告文案因此常显。
   描述符由窗口字段持有引用，避免弱缓存回收后失效。
9. **减少动效与截图**：`SettingsMotion.Enabled = !CardMotion.SuppressedForCapture && !ReduceMotion &&
   SystemParameters.ClientAreaAnimation`。任一开关命中就直接到终态——截图 / 自检复用卡片动效已有的
   `CardMotion.SuppressForCapture`，不新增全局开关；本窗口内勾选「减少动效」立即生效（不必等保存）。
   所有动画失败 / 被打断都靠 `FillBehavior.Stop` 与「先重置再播」回到终态，不会把窗口留在 Opacity 0 或错误高度。
10. **弹窗改设置窗圆角**：`HudDialog` 表面从「直角 + 单角切角」改为 22px 圆角 + 1px 线，内容按圆角几何裁切；
    按钮改 6px 圆角（设置窗控件同档），黄底墨字 / 透明描边 / 危险红保留；2×2 截图模式保留。
11. **内外弹窗分流**：`HudDialog.Show` 新增 `dark` / `showIcon`（默认外部明亮、无状态图标）。设置窗内 2 处
    （删除卡片、未保存修改）传 `dark: _settingsDark, showIcon: true`；窗外 10 处（App / Overlay / Tray / Station）
    保持默认，统一明亮风格且不画“！”“？”图标。明亮令牌用 `#F2F2EC/#FFFFFF/#151515/#626262/#E6E6E6`，
    强调条用 paper 版 `#B7A400`。
12. **取色器风格化**：新增 `UiKit.HudColorPicker`（22px 圆角、明暗两套、HSV 自绘色板 + 色相条 + Hex + RGB +
    当前/新色预览 + 6px 圆角按钮）；替换 `SettingsWindow.Theme.PickColor` 的 WinForms `ColorDialog`，6 个颜色按钮自动生效；
    非法输入红框提示且不关窗，Esc 取消 / Enter 确定；无 WinForms 依赖。
13. **粒子动态掩码**：`TemplateEngine.TryBuildDynamicMask(template, rendered)` 按 Render 同源规则解析
    `{{}}` 转义与占位符，用字面量顺序定位区分静态/动态字符；`OverlaySurface.PlayParticle` 与
    `ParticleTextLayer.ResolveChangedTargets` 都只把「动态且非图标/键帽」的字符交给粒子，首播同理；
    掩码不可用时保守退回“仅排除图标/键帽”并节流 Debug。模板字面量与 `Keycaps`/`Icons` 字形永不参与粒子。
14. **黑色 ICO 重制**：新增 `tools/make_black_ico.py`，用 Edge/Chrome 无头渲染 `MirrorpondHUDLogoBlack.svg`
    （透明底 1000×1000）→ alpha 包围盒裁剪 + 方形补齐 → 256px 母版生成 16/20/24/32/40/48/64/128/256 九档 ICO；
    同时刷新 `logo.ico`（浅色任务栏）与 `logo-black.png`，并加入 csproj Resource。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest --isolated` 退出 0。
- `--screenshot-settings` 默认与折叠态各出一组（5 页 × 2），与 v2.45.2 基线逐像素对比全部一致：
  - IDENTICAL  before/after.png (1120x1100)
  - IDENTICAL  before/after-tab1.png (1120x1100)
  - IDENTICAL  before/after-tab2.png (1120x1100)
  - IDENTICAL  before/after-tab3.png (1120x1100)
  - IDENTICAL  before/after-tab4.png (1120x1100)
  - IDENTICAL  before/after-collapsed.png (1120x1100)
  - IDENTICAL  before/after-collapsed-tab1.png (1120x1100)
  - IDENTICAL  before/after-collapsed-tab2.png (1120x1100)
  - IDENTICAL  before/after-collapsed-tab3.png (1120x1100)
  - IDENTICAL  before/after-collapsed-tab4.png (1120x1100)
- `dotnet format --verify-no-changes --include <6 个改动文件>`：无变更。
- 截图模式按设计只验证终态布局：所有新动效在 `CardMotion.SuppressForCapture` 下直接落终态，
  导航选中底块（0.78）与 3px 指示条位置、分组展开高度都与基线逐像素相同。
- `--screenshot-dialog` / `--dialog-light` / `--screenshot-colorpicker` / `--screenshot-settings` / `--screenshot-cards`
  全部出图并目检：22px 圆角干净、外部三格无图标且正文不偏移、设置窗格保留方形“?”、明亮纸白与暗金竖条正确、
  取色器暗/亮两套 Hex/RGB 与 HSV 游标正确；`--selftest` 两种模式退出 0、xUnit 12/12。
- 粒子真实冒烟（临时 `TextEffect=Particle`）：时钟卡首播变化 23 → 参赛 12、系统信息卡 27 → 参赛 12
  （模板字面量 + 2 个 Lucide 图标全部排除），后续 tick 5→5/2→2/1→1，无“掩码退回”日志。

### 风险 / 未验证

- 动效观感需真机目视：窗口淡入 + 0.985 缩放、连点切页、连点折叠折返、指示条滑入、明暗起伏、状态行淡入。
- 分组高度动画每帧重排当前页（外观页最长），低端机需真机确认无掉帧；若吃紧可退化为「淡入 + 位移」。
- `SystemParameters.ClientAreaAnimation` 跟随系统「显示动画」开关，需在关闭该开关的机器上确认全部停在终态。
- 多显示器 / 混合 DPI 下的窗口缩放入场未实测（缩放锚点在内容根中心，理论不受 DPI 影响）。
- 对话框/取色器需真机确认：125%/150%/175% 下 1px 线与 22px 圆角、跨屏 CenterOwner、暗窗里打开取色器的合成边缘、
  Esc/Enter/Tab 与拖动连续性；Station 危险红在明亮底的对比度。
- 粒子掩码在“模板与渲染不同源 / 字面量歧义”时按设计退回仅图标过滤；按键卡等非模板文本不在本轮冒烟范围，
  只经纯函数自检覆盖。

---

## 5. v2.45.4（玻璃冻结、焦点环、等高线卡顿、拖动吸附、信息卡缺字，未合并 main）

### 背景

用户实测报出五个问题：桌面取样毛玻璃下日志出现 `WriteableBitmap` 冻结错误、对话框主按钮焦点框不完整（暗色下不可见）、
等高线在低速率下明显卡顿、卡片拖到屏幕下部松手被吸回、信息与文本卡片偶发少字/缺行。五个都按根因修，不调表面参数。

### 改动

1. **玻璃 WritePixels 失败（日志第 18 行）**：根因是 G-17 复用 `WriteableBitmap` 后，`OverlaySurface.SetGlass`
   对 `ImageBrush` 执行 `Freeze()`；Freezable 会级联冻结作为子对象的位图，下一帧 `WritePixels` 抛
   `IsFrozen 必须为 false`，`ApplySettings` 捕获后回滚整份设置。修复：仅当图源不是 `WriteableBitmap` 时才冻结画刷。
2. **对话框焦点环**：旧环画在按钮外侧 2px，颜色按按钮填充取 `AccentInk`；暗色下外侧是深色面板，墨色环与背景同色，
   且外侧绘制可能被布局裁切。改为按钮内侧 1.5px 画 2px 环：主按钮用墨色（压在黄底上），其余用 `AccentOnSurface`
   （压在面板底色上），任何宿主都不会裁掉，明暗都有对比。
3. **等高线低速率卡顿**：根因是 T-4 先把相位量化到 2π/256，再叠 66ms 闸门；0.2x 时跨一格需约 195ms，
   即使 30fps 调用也只有约 5 次/秒重建。改为按速度自适应：出帧间隔 `clamp(33×speed, 33, 66) ms`，
   只有“跨一格时间 ≤ 出帧间隔”时才量化；单次生成 EMA 超 15ms 时退回 66ms 成本闸门（1s 防抖日志）。
   0.2x 实测 5.1→18.5–21.3 次/秒，单帧相位步长缩小约 6 倍；形变周期、几何/层级/线宽不变。
4. **拖到底部被吸回**：根因是三层叠加——松手锚点用 `Dialog.Overlay.Y - Overlay.Y`（含上次 Layout 的夹取误差）；
   松手 clamp 允许的落点（卡片底到工作区-40）与 Layout 口径（整叠底到工作区）不一致；拖动过程完全不受限，
   Layout 把整叠压回固定位置并把错误锚点落盘。修复：`CardLayoutCoordinator` 抽 `PlanStack`/`ClampCardTop`/
   `ClampCardLeft` 等纯函数，Layout、拖动中限制、松手换算共用同一口径；拖动中按目标屏实时夹取并重摆整叠，
   松手锚点 = `ClampCardTop(落点) − PlanStack.Tops[index]`，删除第二套 clamp。覆盖上下左右、多屏、DPI、右对齐、
   整叠/单卡超高；数值推演中每个落点跳动从 ±160 DIP 降到 0。
5. **信息卡偶发缺字/缺行**：两个根因：
   - 粒子隐藏集合只在 BeginPlay / 尺寸变化时更新；指标卡 120ms 原地换文本（长度/画布尺寸常不变）不会重算，
     旧 UTF-16 下标落到新文本上遮错字符；800ms 限频早退也不会清遮罩。修复：`OverlaySurface` 在不播新粒子的
     早退路径调用 `EnsureParticleMaskFresh`，`ParticleTextLayer` 每帧检查遮罩是否仍对应当前文本，失效就
     `ClearHiddenChars`（先恢复全部字符，下一轮再连同新粒子一起遮），采样失败也立即恢复。
   - `TextCanvas` 只按主屏工作区宽×0.97 计算 `_maxTextWidth`，`FixedWidth` 或实际 arrange 更窄时仍按自然宽度
     绘制、被窗口边缘硬切且无省略号；靠右锚点时布局只夹 Y 不夹 X。修复：按实际可用宽度/FixedWidth 计算裁剪宽，
     超宽走省略号，布局补横向夹取与诊断日志。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest --isolated` 与 `--selftest` 均退出 0；xUnit 12/12。
- `dotnet format --verify-no-changes`：通过；空 catch 棘轮通过。
- 等高线 `--perf`：0.2x 常规卡重建 18.5–21.3 次/秒（生成 0.2ms）；2.0x 9.0–10.6 次/秒（量化+66ms）；
  0.2x 大卡 20.5–21.2 次/秒（生成 5.2–6.6ms，冷帧 50.7ms 触发成本闸门后自恢复）。
- 拖动：纯函数自检覆盖底部/顶部/整叠超高/单卡超高/左右/DPI 往返/连续拖动收敛，新增「卡片布局夹取: OK」；
  数值推演 1000/700/900/600/1200 DIP 落点跳动均为 0。
- 信息卡：粒子遮罩同步与 TextCanvas 裁剪新增自检断言；真实进程冒烟受单实例/无输入注入限制，以纯逻辑断言 +
  代码路径诊断日志覆盖。
- 玻璃：DesktopFeed 切换路径已不再出现 `IsFrozen` 错误；对话框/取色器截图仍正常。

### 风险 / 未验证

- 焦点环最终观感需真机确认（截图模式无法获得真实键盘焦点）：暗/亮、125–175% DPI、Tab 循环。
- 拖动修复以纯函数 + 数值推演验证，真实鼠标连续拖动/多屏边界仍需人工拖一次；125%/175% 未逐值断言。
- 等高线 DispatcherTimer 本机实际约 19–21 tick/s，是“接近 30fps”的上限；大文本卡每次 Invalidate 仍有整卡文本重排成本（后续单独优化）。
- 信息卡的粒子/裁剪修复以静态路径与断言为主，长文本 + 粒子 + 底部夹取组合需真机复现确认。

---

## 6. v2.45.5（卡片折叠动效、信息卡省略号、DeepYellow 图标、底栏渐变、弹窗细节，未合并 main）

### 背景

设置页仍有几处手感与视觉不一致：设置分组有 200ms 高度动画，卡片设置体的折叠却是瞬间跳变；信息卡偶发把正常内容加省略号；
应用 exe 与亮色任务栏仍是旧黄色图标；设置页底部操作栏与滚动内容之间是硬切；亮色左右栏分隔线过深；
设置内弹窗和背景混在一起，且仍显示“？”“！”等状态符号。另确认设置页字体基线。

### 改动

1. **卡片折叠动效同步**：`SettingsMotion` 新增 `AnimateBody(FrameworkElement, bool)`，复用分组折叠的 200ms 高度动画、
   连点从当前高度折返、结束后清本地 Visibility 让绑定恢复 Collapsed；`SettingsWindow.Cards` 在
   `CardEditor.IsExpanded` 变化时通过 `CardBodyPanel` 名称找到卡片设置体并调用，不再瞬间显隐。
2. **信息卡省略号误判**：根因是 `TextCanvas.AvailableBlockWidth` 直接使用 `ActualWidth` 判裁剪。窗口是
   `SizeToContent`，文本变长后可能先 OnRender、后 Arrange，此时 `ActualWidth` 还是上一段文本的宽度，
   正常内容被提前加省略号。修复：新增 `_arrangedText/_arrangedWidth`，在 `ArrangeOverride` 记录；
   只有当前文本确实按这个宽度 Arrange 过，才用 ActualWidth 收紧，否则按 `LayoutWidthLimit`（固定宽度/屏宽上限）判。
3. **应用与任务栏图标**：`ApplicationIcon`、`OverlayWindow.Icon`、设置窗 Icon 统一 `MirrorpondHUDLogoDeepYellow.ico`；
   `TrayIcon` 亮色任务栏改用 DeepYellow，暗色任务栏保留白色线稿 `logo-light.ico`。
4. **底栏渐变**：右栏内容区底部加 100px 的 `WindowBg` 不透明度渐变（底部实心、向上透明），下边距 52px 对齐按钮上方；
   `IsHitTestVisible=False`，滚动内容自然淡出，不再被操作栏硬切。
5. **亮色分隔线**：新增 `Divider` 调色板键（暗 `#3A3F42` / 亮 `#E6E6DE`），左右栏 2px 分隔线改用该资源；
   `Theme.xaml` 同步注册，内置主题自检继续通过。
6. **弹窗外描边**：`HudDialogSurface.Wrap` 在 22px 表面外再套 1px 外描边 `OuterLine`（暗 `#5E5E5E` / 亮 `#C8C8C0`），
   2px 呼吸；对话框与取色器同时生效，浮在设置页/桌面上更容易区分。
7. **移除状态符号**：所有 `HudDialog` 的 `BuildBody` 固定使用 `HudDialogIcon.None`，不再绘制“？”“！”以及错误“×”方形标记；
   图标参数保留仅为调用签名兼容。
8. **设置页字体（回答）**：基线是 **Microsoft YaHei UI 13px**；版本号 `Cascadia Mono, Consolas` 10px；
   导航/按钮图标用 Lucide 字体（`Lucide.ttf`），键帽示例用 Reicon。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest --isolated` 退出 0；`dotnet format --verify-no-changes` 通过。
- `--screenshot-settings --settings-light` 与 `MIRRORPOND_SETTINGS_SCROLL=1`：亮色分隔线取样 `#E6E6DE`；
  内容页滚到底时卡片在按钮上方渐隐，不再硬切；卡片折叠动画代码复用分组同一实现。
- `--screenshot-dialog --dialog-light`：22px 圆角、外圈 1px 描边、无“？”/“！”符号；取色器同步。
- 用 `ExtractAssociatedIcon` 读取 `bin/Release/MirrorpondHUD.exe`：32×32 图标为 DeepYellow。
- 真实配置 `--smoke-visible` 后日志不再新增“Arrange 旧宽度”省略号裁剪（旧行是 13:21 之前的预览记录）。

### 风险 / 未验证

- 卡片折叠是 UI 动画，截图只验终态；连点折返与滚动位置手感需真机确认。
- 外描边在 125%/150% DPI 与多屏下的 1px 显示需真机确认；取色器宽高增加 4px 后仍居中。
- DeepYellow 图标在 Windows 深色任务栏的对比度依赖 `logo-light.ico` 分支，需真机看亮/暗任务栏切换。
- 信息卡省略号修复覆盖“先渲染后 Arrange”的时序；若窗口被外部真正压窄，仍会在 Arrange 后按实际宽度省略。

---

## 7. v2.45.6（思源黑体、仅桌面/置顶，未合并 main）

### 背景

微软雅黑随 Windows 分发，应用内嵌/默认使用有版权风险；字体改为思源黑体（SIL OFL 1.1，Noto Sans SC 同源字形）。
行为页原来只有“始终置顶”开关，用户需要“仅桌面”（贴在壁纸上、普通窗口下方）与“置顶”两种明确模式。

### 改动

1. **嵌入思源黑体**：`assets/fonts/NotoSansSC-VF.ttf`（17.7 MB，OFL）作为 Resource 编入；新增 `UiKit.HudFont`
   统一把逻辑名 `Noto Sans SC` / `思源黑体` / `Source Han Sans SC` 解析到 `pack://application:,,,/assets/fonts/#Noto Sans SC`，
   其它字体仍按系统字体解析，失败回退嵌入字体。
2. **默认与迁移**：`StyleSettings.FontFamily` 默认 `Noto Sans SC`；`Settings.Normalize` 把旧默认
   `Microsoft YaHei UI` / `微软雅黑` 迁移为思源黑体（用户显式选择的其它字体保留）；`Theme.xaml` 增加
   `AppFontFamily`，设置页样式、弹窗、卡片回退统一走 `HudFont`；字体下拉首项显示“思源黑体 / Noto Sans SC”。
3. **行为页显示模式**：把“始终置顶”复选框改为“显示模式”下拉，选项“仅桌面 / 置顶”，
   仍以 `Overlay.Topmost` 为单一持久化状态（置顶 = true，仅桌面 = false）；保存后 `ApplyDisplayMode` 生效。
4. **仅桌面实现**：`OverlayWindow.ApplyDisplayMode` 在 false 时 `Topmost=false` 并
   `SetWindowPos(HWND_BOTTOM, SWP_NOMOVE|NOSIZE|NOACTIVATE|NOOWNERZORDER)`；`EnsureTopmost` /
   `RefreshTopmostIfNeeded` 在仅桌面模式下直接返回，防止启动、换卡、命令结束或 2.5s 调度把窗口重新顶上去。
   失败走 `_windowPosFailure` 可见日志。
5. **设置页字体（回答）**：基线字体 `Microsoft YaHei UI` 13px 已改为嵌入 `Noto Sans SC` 13px；
   版本号仍 `Cascadia Mono, Consolas` 10px（不再回退雅黑）；图标为 Lucide，键帽为 Reicon。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest --isolated` 退出 0；`dotnet format --verify-no-changes` 通过。
- `--screenshot-settings` 字体明显切换为思源黑体（设置页与卡片），`--screenshot-cards` 正常。
- 临时 `Overlay.Topmost=false` 运行 `--smoke-visible`：无 `HWND_BOTTOM`/SetWindowPos 失败日志；行为页截图显示“显示模式：仅桌面”。
- 现有配置的 `Microsoft YaHei UI` 在 Normalize 后变成 Noto Sans SC。

### 风险 / 未验证

- NotoSansSC 是可变字体，WPF 只按默认/命名字重实例渲染；极粗/极细字重的视觉需真机确认。
- 字体增加 17.7 MB 安装包/内存映射；若要缩体，可在发布阶段做子集（需另加工具链）。
- “仅桌面”用 HWND_BOTTOM 实现：普通窗口会盖住它，但全屏游戏/桌面壁纸层级切换、多屏下 Z 序需真机确认；
  与 Wallpaper Engine 之类直接改 WorkerW 的软件不保证同一层。
- 切换显示模式后卡片叠层位置不变，只改 Z 序；设置页保存后立即生效，下次启动沿用。

---

## 8. v2.46.0（省略号误裁、富内容置顶、外观包前期，未合并 main）

### 背景

2.45.x 后仍有两个可见回归：信息卡正常内容偶发被省略号截断；切到“仅桌面”后媒体/富内容子窗口仍会跑到普通窗口上方。
同时按外观扩展方案先落地“外观包”骨架，为后续明日方舟 / 终末地武陵等风格减少每次改卡片的成本。

### 改动

1. **省略号误裁根因修复**：`TextCanvas.AvailableBlockWidth` 旧实现会拿 `ActualWidth` 收紧。
   窗口是 `SizeToContent`，文本变长时可能先 OnRender、后 Arrange，`ActualWidth` 还是上一条文本的宽度
   （日志里甚至出现隐藏布局的 `Arrange 1`），正常内容被提前加省略号。
   现改为**只看显式约束**（`FixedWidth` / 工作区上限）：无显式限制时不裁剪，有显式限制才按上限省略；
   删除 `_arrangedText/_arrangedWidth` 旧兜底与临时诊断日志。
2. **仅桌面下富内容置顶**：媒体视频子窗口在 owner 不置顶且没有非置顶锚点时会退回 `HWND_TOP`，
   把它抬到所有普通窗口上方。改为保持 owner 关系并返回，不再强行改 Z 序；`OverlayWindow.ApplyDisplayMode`
   在仅桌面成功后触发 `RaisedTopmost`，让富内容按“卡片不置顶”口径重新摆位。
3. **外观包前期脚手架**：
   - 新增 `AppearancePack`（id/名称/描述/主题 id/chrome 开关）与 `AppearanceCatalog`（内置包目录）、
     `AppearanceRuntime.TryApply`（克隆设置上先套主题文档，再落 chrome 与身份）。
   - `StyleSettings.AppearanceId` 持久化；设置页“外观”新增“外观包”下拉，内置“终末地·墨 / 终末地·纸”，
     选择后套用令牌与 chrome；手动改任一外观字段自动回到“自定义”。
   - 后续新增风格只需登记包（主题文档 + chrome），卡片渲染代码不变；导入 `.mppack`、settings.xaml 配方
     与外部包校验留到下一阶段。
4. **版本**：`2.46.0`。

### 验证

- `dotnet build -c Release`：0 警告 0 错误；`--selftest --isolated` 退出 0；`dotnet format --verify-no-changes` 通过。
- 真实 `--smoke-visible` 不再新增“超过可用宽”日志；信息卡/指标卡在用户配置下正常显示。
- 临时 `Overlay.Topmost=false` 的仅桌面路径无 `HWND_BOTTOM`/`SetWindowPos` 失败；视频子窗口回退逻辑改为不抢普通窗口层级。
- 外观页截图显示“外观包”下拉与“终末地·墨 / 纸”两项；切换后 `AppearanceId` 写入设置副本，保存生效。

### 风险 / 未验证

- 省略号修复后，若窗口确实被外部压窄且没有显式 FixedWidth，文字可能溢出窗口而不是显示“…”；
  这是用“不误裁”换“极端情况下可能溢出”，需真机确认卡片窗口能按内容增长。
- 富内容 z 序修复只覆盖视频子窗口；DropStation 拖拽浮层、系统弹窗等其它顶层窗口未纳入。
- 外观包目前是代码登记的内置目录，尚不支持外部 `.mppack`、settings.xaml 配方与卡片按 Kind 的 chrome 配方；
  这些是下一阶段工作。
