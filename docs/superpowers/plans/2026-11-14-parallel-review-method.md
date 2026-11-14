# 多 agent 并行审阅（样板作业）与 tests 入库 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 3 个并行 Worker 完成一次全仓只读审阅并产出可定级报告，随后把 `tests/` 纳入版本库、把方法与报告提交到新分支。

**Architecture:** Lead 建任务板并 spawn 3 个 durable teammate 并行取证；结果回收后做一轮由非作者执行的 P0/P1 交叉复核；Lead 实跑必查清单命令、去重定级、抽样测误报率，最后写报告。样板作业全程只读；写操作（`tests/` 入库、提交）在报告完成之后按任务 11–13 执行。

**Tech Stack:** Agent Teams（spawn_teammate / team_task_*）、git、dotnet 8、PowerShell 棘轮脚本。

**Spec:** `docs/superpowers/specs/2026-11-14-parallel-review-method-design.md`

## 全局约束

- 样板作业（任务 1–10）**只读**：不得修改任何源码、配置、`.gitignore`；发现 P0/P1 只记录。
- 每条 finding 必须有 `文件:行`、触发条件、可观察后果；无行号不进报告正文。
- 定级四级：P0 停线 / P1 本次修 / P2 排期 / P3 不排期。
- 并行上限：3 张写卡在途；样板只读，不受限。
- `--selftest` / `--screenshot` 单实例串行，一次一个；本作业不改代码，不跑。
- 中文产出；报告落 `docs/review/2026-11-14-parallel-review.md`。
- 覆盖口径：必查清单 7 项穷尽，其余抽样；报告不得宣称"全仓无 P0"。
- 时间盒：Worker 取证 ≤40 分钟，交叉复核独立一轮 ≤20 分钟，Lead 汇总 ≤20 分钟。

## Review Focus

- 未跟踪资产（`.tmp/`、`scroll-fix.png`、`samples/*`、第三方克隆）被漏判 → 误入库或误排除；由任务 3 的"`.gitignore` 对照全量"步骤兜住。
- findings 缺 `文件:行` 导致无法复核；由任务 3–5 的"逐条校验证据"步骤和任务 6 的复核前置检查兜住。
- 两个 Worker 报同一根因被重复计数；由任务 9 的"按根因合并"步骤兜住。
- P0 被时间盒压成 P1；由任务 9 的"P0 必须二次确认且不得降级无理由"步骤兜住。
- 误报率无法测或抽样不足；由任务 9 的"抽 20%（不足 5 条全量）"步骤兜住。

---

### Task 1: 冻结基线并确认环境

**Files:** 无（只读）

**Interfaces:**
- Produces: 基线记录（HEAD、分支、`git status --porcelain` 全文），供任务 3 与任务 9 引用

- [ ] **Step 1: 确认无运行实例**

Run: `tasklist | findstr /i MirrorpondHUD`
Expected: 无输出（有输出则先让用户关闭实例，再继续）

- [ ] **Step 2: 记录基线**

Run: `git rev-parse --short HEAD; git branch --show-current; git status --porcelain`
Expected: `b27e67d`、`review/2.45.1-fixes`、21 行 porcelain（2 修改 + 19 个未跟踪项；`-uall` 实为 36 个文件）；把三行输出存进报告附录

- [ ] **Step 3: 确认体裁文件未变**

Run: `git diff --stat; git diff -- .gitignore | head -5`
Expected: 仅 `.gitignore` 与 `docs/lyrics-research.md` 两处修改

### Task 2: 建任务板并 spawn 三个 Worker

**Files:** 无（任务板在 Agent Teams，不在仓库）

**Interfaces:**
- Consumes: 任务 1 的基线
- Produces: 三张任务卡（W1/W2/W3）的 ID；三个 teammate 名（`w1-hygiene`、`w2-visibility`、`w3-boundary`）

- [ ] **Step 1: 建三张任务卡**

用 `team_task_create` 各建一张，`write_scopes` 留空（只读），描述含：范围、必查项、findings 字段、40 分钟时间盒、产出即消息回传。三张卡内容分别按 spec §6.2 的 W1/W2/W3。

- [ ] **Step 2: 确认任务卡可领取**

Run: `team_task_list({ status: "pending" })`
Expected: 3 条，`ready: true`

- [ ] **Step 3: 并行 spawn 三个 Worker**

三条 `spawn_teammate` 在同一消息内发出，每个 prompt 自包含：只读、具体范围、findings 格式（`{id, level, file, line, 触发条件, 后果, 证据, 建议}`）、时间盒、回传方式（`send_message` 给 lead）。id 前缀分别为 `W1-`、`W2-`、`W3-`。

- [ ] **Step 4: 确认三人在跑**

Run: `list_agents({})`
Expected: 三个 teammate 状态 running

### Task 3: 回收并校验 W1（入库卫生与文档一致性）

**Files:** 无（结果进汇总表）

**Interfaces:**
- Consumes: W1 回传的 findings
- Produces: W1 的已校验 finding 列表（编号 `W1-01` 起）

- [ ] **Step 1: 等到 W1 回传或超时**

用 `wait_agent` 等变更（≤40 分钟）；超时则按 spec §6.4 记"待复核"，不延期。

- [ ] **Step 2: 逐条校验证据**

对每条 finding 核对：`文件:行` 是否可定位、触发条件是否可复现、是否给出去向建议（入库/排除/归位）。不合格的退回 W1 补证据（一次）。

- [ ] **Step 3: 归档**

把校验通过的 finding 写入汇总表（Lead 本地结构，最后并入报告）。

### Task 4: 回收并校验 W2（失败可见性与日志纪律）

**Files:** 无

**Interfaces:**
- Consumes: W2 回传的 findings
- Produces: W2 的已校验 finding 列表（`W2-01` 起）

- [ ] **Step 1: 等到 W2 回传或超时**（同 Task 3 Step 1）
- [ ] **Step 2: 逐条校验证据**（同 Task 3 Step 2）；额外核对：空 catch 计数必须来自 `tools/check-empty-catches.ps1` 实跑，不能是估算
- [ ] **Step 3: 归档**

### Task 5: 回收并校验 W3（边界、存储、线程与原生）

**Files:** 无

**Interfaces:**
- Consumes: W3 回传的 findings
- Produces: W3 的已校验 finding 列表（`W3-01` 起）

- [ ] **Step 1: 等到 W3 回传或超时**（同 Task 3 Step 1）
- [ ] **Step 2: 逐条校验证据**；额外核对：`SelfTest` 结论是否区分"静态读到"与"实跑观测"
- [ ] **Step 3: 归档**

### Task 6: P0/P1 交叉复核（独立一轮，20 分钟）

**Files:** 无

**Interfaces:**
- Consumes: W1–W3 的 P0/P1
- Produces: 每条 P0/P1 的复核结论（确认 / 降级 / 驳回 + 理由）

- [ ] **Step 1: 分配复核**

P0/P1 按"提出者以外的另一 Worker"分配，用 `send_message` 下发；关闭生产者自审。

- [ ] **Step 2: 收集结论**

Run: `wait_agent`（≤20 分钟）
Expected: 每条都有三选一结论；超时未回的条目单列「待复核」，不进正文定级

- [ ] **Step 3: 记驳回项**

驳回条目连同理由写入报告附录，不删除。

### Task 7: Lead 实跑必查清单命令（只读）

**Files:** 无

**Interfaces:**
- Produces: 7 项必查清单的实测输出，作为 W1–W3 结论的交叉证据

- [ ] **Step 1: 未跟踪资产对照**

Run: `git status --porcelain --untracked-files=all; git check-ignore -v <每个未跟踪路径>`
Expected: 每个未跟踪项都能解释"该入库/该忽略/该归位"；无法解释的记 finding

- [ ] **Step 2: 空 catch 棘轮实跑**

Run: `powershell -ExecutionPolicy Bypass -File tools/check-empty-catches.ps1`
Expected: 计数 ≤ 基线 31，退出 0；高于基线则记 P1

- [ ] **Step 3: Contracts 反向引用**

Run: `rg -n "using MirrorpondHUD(\.Ui|\.UiKit)|MirrorpondHUD\.Cards" src/Contracts`
Expected: 无输出；有输出即 P1

- [ ] **Step 4: SelfTest 调用点与实例前置检查**

Run: `rg -n "SelfTest\.Run|--selftest|selftest" src/App.xaml.cs src/**/SelfTest*.cs`
Expected: 能指出实例在跑时是否会跳过；跳过即 P1（复核 review-2.44.2 X-1）

- [ ] **Step 5: 版本记录覆盖**

Run: `git log --oneline -5; rg -n "2\.48\.0" docs/version-history2.md`
Expected: 2.48.0 的四个提交都在版本记录中

### Task 8: 去重、定级与误报率抽样

**Files:** 无

**Interfaces:**
- Consumes: 任务 3–7 的全部 finding
- Produces: 最终定级表（按根因合并、多来源引用）+ 误报率数值

- [ ] **Step 1: 按根因合并**

同一根因的 finding 合并，保留多来源编号（如 `S-01 / W2-3`）。

- [ ] **Step 2: 定级**

P0 必须"启动必崩/必丢数据/安全"，且二次确认；降级必须写理由。

- [ ] **Step 3: 误报率抽样**

抽 20% 的 P1（不足 5 条则全量），由 Lead 独立复核；驳回或降级比例即误报率，记入附录。

### Task 9: 写审阅报告

**Files:**
- Create: `docs/review/2026-11-14-parallel-review.md`

**Interfaces:**
- Consumes: 任务 8 的定级表、误报率、任务 1 的基线
- Produces: 报告文件（一页结论 + 数字表 + 分维度条目 + 附录）

- [ ] **Step 1: 写一页结论**

必须写明：必查清单 7 项全覆盖、抽样 N 个文件、误报率 X%、P0/P1 数量。

- [ ] **Step 2: 写数字表与分维度条目**

沿用 `docs/review/review-2.44.2.md` 的编号与表格口径（区域 × 文件 × P1/P2/P3）。

- [ ] **Step 3: 写附录**

含：基线记录、交叉复核记录、驳回项与理由、误报率抽样明细、未提交改动的处置建议（入库/排除/归位）。

### Task 10: 报告自检

**Files:** `docs/review/2026-11-14-parallel-review.md`

**Interfaces:**
- Consumes: 任务 9 的报告
- Produces: 通过自检的报告

- [ ] **Step 1: 证据抽核**

随机抽 5 条 finding 回代码定位 `文件:行`，全部命中才算过。

- [ ] **Step 2: 覆盖口径检查**

`rg -n "全仓无 P0|全仓无问题" docs/review/2026-11-14-parallel-review.md` 必须无输出。

- [ ] **Step 3: 编号唯一性**

`rg -o "W[123]-[0-9]+"` 去重后与条目数一致。

### Task 11: `tests/` 纳入版本库（写操作，样板作业之后）

**Files:**
- Modify: `.gitignore`（删除 `tests/` 与其注释行）
- Add: `tests/**`（8 个源码 `.cs` + 2 个 csproj + README）

**Interfaces:**
- Consumes: 无
- Produces: 可运行的 `tests\TestRunner\TestRunner.csproj`，供后续写卡当回归断言用

- [ ] **Step 1: 新建分支**

Run: `git checkout -b docs/parallel-review-method`
Expected: 切到新分支，工作区仍保留那 36 个未提交项（`-uall` 口径）

- [ ] **Step 2: 先验证 TestRunner 当前可跑**

Run: `dotnet run --project tests\TestRunner\TestRunner.csproj -c Release`
Expected: 退出 0；若失败，记录失败输出并停下，不得提交红测试

- [ ] **Step 3: 改 `.gitignore` 并纳入**

删除 `tests/` 行与 `# 测试文件` 注释；Run: `git add tests/ .gitignore`
Expected: `git status` 显示 `tests/` 下文件为待提交

- [ ] **Step 4: 提交**

Run: `git commit -m "chore: tests 纳入版本库，TestRunner 可作回归门禁"`
Expected: 提交成功，仅含 `.gitignore` 与 `tests/`

### Task 12: 提交方法与 AGENTS 引用

**Files:**
- Add: `docs/superpowers/specs/2026-11-14-parallel-review-method-design.md`
- Add: `docs/superpowers/plans/2026-11-14-parallel-review-method.md`（本计划自身）
- Modify: `AGENTS.md`（末尾加一节指向方法文档）

**Interfaces:**
- Consumes: Task 11 的分支
- Produces: 方法文档入库、AGENTS 有入口

- [ ] **Step 1: 写 AGENTS 引用节**

在 `AGENTS.md` 末尾加：`## 13. 并行作业`，一行说明"多 agent 并行审阅/实现的流程见 `docs/superpowers/specs/2026-11-14-parallel-review-method-design.md`"。

- [ ] **Step 2: 提交**

Run: `git add AGENTS.md docs/superpowers/specs/2026-11-14-parallel-review-method-design.md; git commit -m "docs: 多 agent 并行处理与审阅工作法"`
Expected: 提交成功

### Task 13: 提交审阅报告

**Files:**
- Add: `docs/review/2026-11-14-parallel-review.md`

**Interfaces:**
- Consumes: Task 10 通过自检的报告
- Produces: 报告入库

- [ ] **Step 1: 提交**

Run: `git add docs/review/2026-11-14-parallel-review.md; git commit -m "docs: 2026-11-14 并行审阅报告"`
Expected: 提交成功；`git log --oneline -3` 显示三次提交

- [ ] **Step 2: 收尾核对**

Run: `git status --short`
Expected: 仅剩与本次作业无关的未跟踪资产（`assets/*`、`samples/*`、`.tmp/`、`scroll-fix.png` 等），这些不提交
