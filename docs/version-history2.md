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
