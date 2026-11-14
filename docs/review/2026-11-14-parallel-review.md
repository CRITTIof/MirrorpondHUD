# 并行审阅报告：未提交改动 + 全仓红线抽检（2026-11-14）

> 基线：`b27e67d`（2.48.0），分支 `review/2.45.1-fixes`，工作树含 2 处修改 + 36 个未跟踪文件。
> 快照说明：本报告描述 `b27e67d` + 当时工作树；W1-05（`tests/` 忽略）与 W1-11（20→36 口径）已在本分支后续提交（`df38559` / 方法提交）修复，读时以树为准。
> 方法：`docs/superpowers/specs/2026-11-14-parallel-review-method-design.md`；3 个 Worker 并行只读取证，Lead 实测交叉证据 + 去重定级。
> 口径：**只读**。未 build、未跑 `--selftest`/`--screenshot`/`--smoke`，所有运行期行为结论均为静态阅读。

---

## 0. 一页结论

### 0.1 总评

**P0 = 0，P1 = 0**（2 条初判 P1 经独立复核降级为 P2，见附录 B），P2 = 8，P3 = 12。仓库工程纪律的"硬骨架"是干净的：Contracts 反向引用 0 条、序列化 DTO 与 P/Invoke 结构体在 2.44.2→HEAD 窗口内**零布局变动**、原子写与高版本只读成立（含 C-1 大小写绕过的回归断言）、代次与 `SafeRelease` 收尾在 4 条链路上无晚归/双重释放、manifest 三档校验实现正确、空 catch 棘轮实测 31/31 未上升。

问题集中在**"纪律缺执行器"与"入库卫生"**两处：

1. `catch { return 字面量; }` 这类 §5 明令禁止的形态全仓 36 处（34 处为 v2.44.2 存量），而唯一的空 catch 棘轮只匹配空方法体，对它们计数为 0——X-9 的"部分修"残留。
2. 临时产物（`.tmp/` 18 项，其中含森空岛登录二维码点阵）与 1.1 MB 调试截图**未被忽略**，`git add -A` 会直接入库；`samples/arknights-ui/` 是带 `.git` 的第三方克隆，会被当 embedded repo 加入。
3. `docs/lyrics-card-review.md` 与实现脱节：它列的 P0-3/4/5/6/7 早已随 `b41b4ab` 修掉，行号全部失效，读者会去修已经修过的缺陷。

### 0.2 数字

| 来源 | P0 | P1 | P2 | P3 | 交付数 |
| --- | ---: | ---: | ---: | ---: | ---: |
| W1 入库卫生与文档一致性 | 0 | 0 | 5 | 7 | 12 |
| W2 失败可见性与日志纪律 | 0 | 1 | 4 | 5 | 10 |
| W3 边界、存储、线程与原生 | 0 | 1 | 0 | 2 | 3 |
| 交叉复核后（合并去重） | **0** | **0** | **8** | **12** | **20** |

- 合并：W1-01 与 W1-03 并为"临时产物未排除"；W2-01/02/03 并为"X-9 残留"一条（保留三来源编号）；W1-10、W1-12 为确认无误的反证记录，不计缺陷。
- 误报率：按 spec §5.5 抽样 P2 20%（2/10，因 P1 为 0），命中 2/2，**误报率 0%**（明细见附录 C）。

### 0.3 最值得先做的 6 件事

| # | 事项 | 编号 | 量级 |
| --- | --- | --- | --- |
| 1 | `.gitignore` 补 `.tmp/` 与 `scroll-fix.png`，`samples/arknights-ui/` 加排除；避免误入库 | W1-01/02/03 | 10 分钟 |
| 2 | `tests/` 入库（删 `.gitignore` 的 `tests/` 行）并跑一次 TestRunner | W1-05 | 已排入 Task 11 |
| 3 | 棘轮补第二条正则覆盖 `catch { return X; }`（基线改两行），并修正反向激励提示 | W2-01/03 | 1 小时 |
| 4 | `--smoke`/`--perf` 在已有实例时拒绝执行并退出 2，同步 `AGENTS.md §2` 契约 | W3-01 | 30 分钟 |
| 5 | `SettingsApplier.SaveSettings()` 五处丢弃返回值补可见失败 | W2-05 | 1 小时 |
| 6 | `docs/lyrics-card-review.md` 逐条标注已修/未修，去掉失效行号链接 | W1-04 | 1 小时 |

## 1. 覆盖范围与口径

### 1.1 实际做了什么

| 项 | 范围 | 方式 |
| --- | --- | --- |
| 未提交改动 | 2 修改 + 36 未跟踪（`.tmp/` 18、`assets/` 10、`docs/` 6、`samples/arknights-ui/` 1、`scroll-fix.png` 1） | `git status -uall` 全量逐项对照 `.gitignore`；`git add -n` 实测会被加入的路径 |
| 必查清单 7 项 | spec §6.3 全部 7 项 | 全覆盖，Lead 与 Worker 双跑（见附录 A） |
| 代码红线 | 失败可见性/日志纪律；边界/DTO/P-Invoke/存储/线程/manifest | 全仓 `rg` 8 组模式 + 逐文件读实现；W3 逐行核了 13 个互操作文件与 2.44.2→HEAD 的 diff |
| 未覆盖 | 运行期行为（build/selftest/截图）、XAML 名键、主题令牌硬编码 | 明确未做；本报告不作相关结论 |

### 1.2 定级

P0 启动必崩/必丢数据/安全；P1 用户可感错误或验证链说谎；P2 健壮性/可维护性/明显退化；P3 风格与文档瑕疵。本文无 P0/P1。

## 2. 分维度条目

### 2.1 入库卫生与文档一致性（W1）

**P2｜W1-01 + W1-03 临时产物未被忽略，`git add -A` 会入库**
- `W1-01`：`.gitignore` 无 `.tmp/` 规则，实测 18 个产物（`lyrics.json`、`smtc/` 临时工程、15 张截图、`qr_cs.txt`/`qr_py.txt`）。后两者是同一份森空岛 `scan_login` 二维码点阵（1155 字节，`cmp` 相同）——**潜在凭据类产物**，有效期未知，按"潜在"而非"已泄露"定级。
- `W1-03`：仓库根 `scroll-fix.png`，1,127,054 字节，调试截图，无忽略规则。
- 证据：`git add -n -A .` 输出 `add '.tmp/lyrics.json'` 等 18 行 + `add 'scroll-fix.png'`。
- 建议：补 `.tmp/` 规则；`scroll-fix.png` 排除或归位 `.tmp/shots/`。

**P2｜W1-02 `samples/arknights-ui/` 是第三方克隆，未排除**
- 带 `.git`（remote `mashirozx/arknights-ui`，HEAD `8fb68d3`）；`git add -n` 给 `warning: adding embedded git repository`——外层仓库只存 gitlink，clone 后内容缺失。
- 注意：`git check-ignore` 对带尾斜杠的该路径会报 `.gitignore:49` 命中，但工作树该行为空、`git add` 实际会加入——**判忽略以 `git add -n` 为准**（方法备注，见附录 D）。
- 建议：加 `samples/arknights-ui/`（AGENTS §11）。

**P2｜W1-04 `docs/lyrics-card-review.md` 与实现脱节**
- 声称"LyricsCard.cs 工作树未提交改动 +111/−27"，实际该文件 clean，改动已随 `b41b4ab`/`6bf49d7` 提交；文件 539→689 行，`:263`/`:527` 等行号全失效。
- 已修：P0-3/4/5/6/7——P0-5/3/4/6 见 `LyricsCard.cs:205` 的注释，P0-7 见 `:457`/`:489` 的 `status == 404`/`429` 实现行（非注释）；P0-2 已修（`:333/:351`）。
- 未修：P0-1（`OnVisibilityChanged` 无命中）、P0-8（`:43 NeedsMouseInput => true`）、P0-9（无 `SetMeta`）、P2-4（`:686` UA 仍写 `MirrorpondHUD/2.46.6`）。**注意 P0-x 是旧文档编号，按本报告口径相当于 P2/P3，不代表本文有 P0。**
- 建议：逐条标注"已修（提交号）/未修"，行号改符号引用。

**P2｜W1-05 `tests/` 仍被 `.gitignore:48` 忽略**
- 与"纳入版本库"的裁决冲突，8 个源码 `.cs` + 2 个 csproj + README 无法入库。已在 Task 11 处理。

**P3**
- `W1-06`：`assets/` 下 10 个未跟踪设计件无归属，全仓 grep 零引用；建议 `AI对话卡片.*` 入库，`EndfieldCradDesign*`（6 个，拼写 Crad→Card）与 `contour-map.svg` 归位 `samples/` 或并入 `docs/UI/`。需你裁决归属。
- `W1-07`：`version-history2.md` 总览表缺 v2.47.0/v2.48.0 两行，§17 正文无四个提交哈希，无法回溯。
- `W1-08`：§17 引用未跟踪的 `docs/lyrics-card-review.md`，交叉引用悬空。
- `W1-09`：审阅文档落在 `docs/` 根，未按 spec §5.8 放 `docs/review/`。
- `W1-11`：方法文档 §6.1 写"20 个未跟踪项"，实测 36 条（`-uall` 口径）。
- `W1-10`（记录，无需处理）：BOM 移除在 git 2.56 下无功能影响，保留修正。
- `W1-12`（反证记录）：`samples/netease-lyrics-research/` 等新增排除规则均有效。

### 2.2 失败可见性与日志纪律（W2）

**P2｜W2-01 + W2-03 + W2-02（X-9 残留）：棘轮只覆盖空体，`catch { return X; }` 36 处无门禁**
- `tools/check-empty-catches.ps1:38` 的 `catch\s*(\([^)]*\))?\s*\{\s*\}` 只匹配空方法体；实跑 31/31 通过（与基线一致）。
- 全仓 `catch { return X; }` 精确计数 **36 处**（34 处为 v2.44.2 存量）；34~36 差异来自多语句/嵌套形态口径。
- 无其它门禁覆盖：`.editorconfig` 无 CA1031；`TreatWarningsAsErrors` 反证分析器未命中；`tests/`、`--selftest` 不扫源码。
- 反向激励：把 `catch { }` 改成 `catch { return null; }` 会让计数下降，脚本 `:75-77` 还提示"已少于基线，请更新基线"——等于奖励另一种被禁止的写法。**这是本条的真正风险，不是"假绿灯"**（脚本只声称数空 catch，未谎报 §5 合规）。
- 样例：`src/Plugins/Command/CommandCardEditor.cs:265`（`TryStyle` 静默返回 null）、`src/Plugins/DropStation/StationFileActions.cs:224`（非法路径变 null，用户操作被无声丢弃）、`:319`（访问被拒被报告成"目标不安全"）。
- 建议：加第二条棘轮（`return|continue|break|yield`），基线改两行；修正"少于基线"的提示语义。

**P2｜W2-04 9 个 `LogThrottle` 只有失败没有恢复**
- 代表：`GlassCapture.cs:303/319/398/433`（4 个每帧调用的辅助函数）、`CardLayoutCoordinator.cs:279/300`、`StationView.cs:739/747`、`RenderScheduler.cs:125`、`ScreenGeometry.cs:272`。
- 违反 AGENTS §5"失败、恢复各记一条"；恢复时日志与用户都看不到"已恢复"。
- 建议：各成功分支补 `Success()`（`_enumFailure` 在 `GlassCapture` 已有 Success，`ScreenGeometry` 无）。

**P2｜W2-05 `SaveSettings()` 返回值被丢弃 5 处**
- `SettingsApplier.cs:243`（设置窗锚点）、`:448`（拖拽落盘）、`:498`（开机自启）、`OverlayController.cs:837`（锁屏）、`HotkeyRouter.cs:128`（热键回退）。
- 失败只进 `Log.Warn`（`:462`），用户以为"已记住"，重启回滚且无 UI 提示；对照正确用法 `ChatCard.cs:429`。
- 建议：失败经状态行/托盘气泡可见，`Log.Warn` 带上 `ConfigStore.LastSaveError`。

**P3**
- `W2-06`：`Log.cs:131/144/153/342/357/363/366/368` 自指场景 9 处吞错——日志系统坏了没有出口；建议旁路到 stderr/`PathResolutionError`。
- `W2-07`：`CardJobQueue.cs:57/173` 良性竞态缺注释（同文件 `:259` 有）。
- `W2-08`：12 处释放路径裸空 catch 未走 `SafeRelease.Run`（`AsyncProcess`、`KeyboardHook`、`PdhQuery`、`MetricEngine`、`SelfTest`、`ChatCard`、`CardPluginLoader:208`）。
- `W2-09`：`MediaCardSettingsView.xaml.cs:349/379` 对话框初始目录失败静默（外层有 Warn，内层无）。
- `W2-10`：`CliUi.cs:806` 输入框聚焦失败静默，建议 Debug 级。

**正向确认（无 finding）**：`LastSaveError → 设置窗状态行`链路贯通；ffmpeg 缺失降级有 Warn + 用户可见提示；GlassFeed 退避的失败/恢复成对。

### 2.3 边界、存储、线程与原生（W3）

**P2｜W3-01 `--smoke`/`--perf` 在已有实例时不执行、静默退出 0**
- `CommandLineMode.cs:27-37` 的 `HasImmediateMode` 只含 `--selftest`/`--screenshot*`；已有实例时第二实例走 `App.xaml.cs:135 Shutdown(0)`，冒烟/性能采样一个不跑，且会 `activation.Set()` 误唤醒正在运行的 HUD。
- 原判 P1（假绿灯）被复核降级：`--smoke`/`--perf` 的退出码本就无条件 0（`CommandLineMode.cs:191/216`），不承载成败语义；真正缺的是日志证据，而 `--perf` 静默不产基线数据会被误记成"无差异"（数据风险）。
- 建议：新增 `HasTimedEvidenceMode` 只用于该分支，已有实例时 `Shutdown(2)`；同步 `AGENTS.md §2` 契约（Lead 独占写）。

**P3**
- `W3-02`：`CommandLineMode.cs:32` 忽略大小写、`:72` 精确匹配——`--SelfTest` 在无实例时落回 GUI 常驻，脚本拿不到退出码。
- `W3-03`：`ConfigStore.cs:393-394` 先 Warn"回退到备份+覆盖"、`:396-401` 再 Error"已中止"，日志自相矛盾（行为正确）。

**零缺陷确认（逐行证据）**
- Contracts 无任何 `ProjectReference`，`using` 仅 `System.*` + 自身命名空间；`MirrorpondHUD.Cards` 是契约命名空间声明，非反向引用。
- 2.44.2→HEAD `StructLayout`/`FieldOffset`/结构体成员 **0 变动**；`Settings.cs` 公共字段纯新增；`PluginManifest` 形状未变；Contracts 公共 API 仅新增（`ICardEditorPendingEdits`、`ThemeTokens.Progress`），不破坏 ABI。
- 原子写：备份失败真中止（`AtomicFile.cs:131-136` → `ConfigStore.cs:396-402`）；`SchemaVersion` 高版本只读且拒存（`SettingsLoader.cs:34-41`、`ConfigStore.cs:375-380`）；C-1 大小写绕过已修且有自检断言（`SelfTest.cs:1731-1734`）。
- 代次/释放：`GlassFeed` 全 UI 线程且先清字段再释放；`GlassRuntime._captureGen` 停旧代；`WaveOutPlayer` `_cleanupClaimed` CAS + `_handoffThread`；无双重释放。
- manifest：`apiVersion != 2` 拒绝；`contractsVersion` 缺失=当前、解析失败拒绝、高于拒绝、低于 Warn；`Kind` 走 `IsSafeKind`。
- X-1 已修（`App.xaml.cs:88-95`），并计入 `SelfTest.cs:250-255` 的失败数累加修复。

## 附录

### A. 必查清单实测（7/7）

| # | 项 | 结果 |
| --- | --- | --- |
| 1 | `git status` 全量对照 `.gitignore` | 36 未跟踪逐项判定；`.tmp/`、`scroll-fix.png` 未忽略；`samples/*` 已覆盖 |
| 2 | 歌词文档与代码一致 | `lyrics-research.md` 引用文件存在性全 OK；`lyrics-card-review.md` 脱节（W1-04） |
| 3 | token/cred 是否被跟踪 | 无；未跟踪的 `qr_*.txt` 为潜在凭据类产物（W1-01） |
| 4 | 棘轮实跑 | 31 处，基线 31，exit 0；但只覆盖空体（W2-01） |
| 5 | Contracts 反向引用 | 0 条 |
| 6 | `SelfTest.Run()` 前置与实例检查 | 调用点 `CommandLineMode.cs:87`；实例保护 `App.xaml.cs:88-95`；`--smoke/--perf` 缺口（W3-01） |
| 7 | `version-history2.md` 覆盖 2.48.0 | §17 内容覆盖四提交；总览表缺行（W1-07） |

### B. 交叉复核记录

| 初判 | 复核者 | 结论 | 理由摘要 |
| --- | --- | --- | --- |
| W2-01 P1（棘轮漏计 catch-return = 验证链说谎） | W3 | **降 P2** | 脚本只声称"空 catch"统计，输出与实现一致，未谎报 §5 合规；属 X-9 未完整落地；36 处中 34 处为存量，无"刷低基线"发生。保留"反向激励"风险 |
| W3-01 P1（--smoke 假绿灯） | W2 | **降 P2** | `--smoke`/`--perf` 退出码本就无条件 0，不承载成败语义；排除是设计语义（"定时退出"）；不在固定门禁链内。真实后果是 `--perf` 空证据与误唤醒 |

无驳回项。

### C. 误报率抽样（P1 为 0，按 §5.5 抽 P2 20%）

抽样池 = 8 条 P2 + 2 条由 P1 降级而来的 P2 = 10 条，抽 20% 即 2 条。

| 抽样 | 复核动作 | 结论 |
| --- | --- | --- |
| W2-04 | 逐变量统计 `Failure`/`Success`；核 `StationView.cs:739/747` | 属实 |
| W2-05 | 核 5 处调用点与声明；对照 `ChatCard.cs:429` | 属实 |

误报率 0/2 = 0%。

### D. 未提交改动处置建议

| 条目 | 建议 |
| --- | --- |
| `.gitignore` 修改 | 入库（BOM 移除无功能影响）+ 补 `.tmp/`、`samples/arknights-ui/`、`scroll-fix.png` 规则 |
| `docs/lyrics-research.md` 修改 | 入库 |
| `.tmp/**`（18） | 排除（补规则） |
| `assets/**`（10） | `AI对话卡片.*` 入库；其余归位 `samples/` 或 `docs/UI/`（待裁决） |
| `docs/UI/arknights.md`、`endfield-wuling.md`、`docs/arknights-account-data-research.md` | 入库 |
| `docs/lyrics-card-review.md` | 先改内容（W1-04），再归位 `docs/review/2026-11-13-lyrics-card.md` |
| `samples/arknights-ui/` | 排除 |
| `scroll-fix.png` | 排除或归位 `.tmp/shots/` |
| `docs/superpowers/{specs,plans}/*.md` | 入库（Task 12/13） |

### E. 方法备注（本次作业发现）

- `git check-ignore` 对带嵌套仓库与尾斜杠的路径给出**误导性命中**（`.gitignore:49`，而该行为空）；判"会不会入库"必须用 `git add -n`。已回写方法文档 §5.4。
- 未跟踪项计数口径：`git status --porcelain` 会把目录折叠成 1 行，须用 `-uall` 才得真实条数（36，不是 20）。已回写方法文档 §6.1。
