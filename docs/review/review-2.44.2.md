# 代码审阅：MirrorpondHUD v2.44.2（2026-11-12）

> 基线：`feature/lucide-icons` 分支 `4261fd3`（2.44.2），工作树干净。
> 范围：`src/` 全部 153 个手写 `.cs`（约 54,700 行）+ 9 个 `csproj` + 6 个 `manifest.json` + XAML（2,332 行）+ 构建/发布脚本 + `AGENTS.md`。
> 口径：**只读静态审阅 + 一次 Release 构建**。未在真机跑 UI、未跑 `--selftest`、未用真实 ffmpeg/waveOut 复现（见 §1.3）。
> 用户明确授权：把 `AGENTS.md` §2–§7 当作**可质疑的声明**而非不可审视的规则；本报告 §6 对它们逐条审计。

---

## 0. 一页结论

### 0.1 总评

这是一个**工程质量明显高于同规模个人项目**的桌面应用：`Build` 0 警告属实、零第三方 NuGet 依赖属实、`AtomicFile` 的持久化语义（`WriteThrough` + `Flush(true)` + 备份失败中止）写得比很多商业代码更严谨、玻璃子系统在 2.24 做过"单一数据源"重构、自检覆盖 28 个领域且部分断言质量很高（主题往返整串字符串相等）。

但它同时存在**系统性的"纪律没有执行器"问题**：

- 规则说"不允许吞错"，仓库里仍有 **138 处 `catch { }`**；
- 规则说"失败必须可见"，但 **117 处 P/Invoke 里大量 `BOOL` 返回值从不检查**，`catch` 兜不住 Win32 的 `FALSE`；
- 规则说"改完必须跑 `--selftest`"，但**应用在运行时 `--selftest` 会静默返回 0（什么都不跑）**；
- 规则说"手工代次计数防旧代晚归"，但**玻璃采集链路一个代次变量都没有**，只有一个 `bool _glassBusy`；
- 规则说"备份失败必须中止保存"，实现是对的，但**报错函数自己会抛异常**。

结果是：**26 条 P1 里约 20 条是"状态/时序/可见性"缺陷**，而不是算法错误。它们都不是"不会写"，而是"没人守"。

### 0.2 数字

| 区域 | 文件 | 行数 | P1 | P2 | P3 | 小计 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 契约与核心（Contracts/Core/SelfTest） | 29 | 14,411 | 6 | 15 | 8 | 29 |
| 设置窗 / 热键 / 托盘 / 输入（Ui 设置面） | 29 | 8,172 | 4 | 8 | 9 | 21 |
| 叠层 / 玻璃 / 渲染（Ui 运行时） | 15 | 7,397 | 5 | 11 | 3 | 19 |
| 文本 / 粒子 / 等高线 / 布局 | 16 | 4,930 | 3 | 10 | 8 | 21 |
| UiKit 控件库与媒体 | 35 | 12,684 | 3 | 19 | 20 | 42 |
| 插件 / 加载器 / 指标 / 中转站 | 29 | 8,242 | 4 | 15 | 8 | 27 |
| **区域小计** | **153** | **55,836** | **25** | **78** | **56** | **159** |
| 跨层补充（本报告 §5、§6 新发现，已与区域去重） | — | — | 1 | 9 | 6 | 16 |
| **合计** | — | — | **26** | **87** | **62** | **175** |

> 计数口径：以"独立条目"为单位；`X-8`（日志同步）、`X-9`（空 catch 汇总）、`X-7`（测试策略）与区域条目同根，正文合并陈述、不重复展开，但保留在跨层计数里。行数按各区域子报告口径（含少量 XAML 与生成文件），与 §1.1 的 54,707 行手写 C# 存在约 1k 行口径差。

- **P0 = 0**：没有"启动必崩 / 必丢数据 / 无鉴权远程执行"级别的缺陷。
- **待实测**约 10 条，均已标注，主要集中在 Win10 旧版、RDP、多显示器混合 DPI、真实 ffmpeg/waveOut 行为。
- 历史项（`docs/review/review-2.26.0.md`）复核：47 个可核对项中 **已修 15、部分修 8、仍未修 24**（见附录 A）。仍未修的 24 项里，`L-6`（卸载探针）的结论比 2.26.0 更糟——现在会稳定误报"模块 6/6 未回收"。

### 0.3 最值得先做的 12 件事（按 ROI 排序，不是按严重度）

| # | 事项 | 编号 | 工时量级 | 收益 |
| --- | --- | --- | --- | --- |
| 1 | 托盘左键失效：`NIN_SELECT` 常量写成 `WM_APP+400`（应为 `WM_USER+0`） | S-01 | 10 分钟 | 每天被点的入口复活 |
| 2 | 回收站删除补 `FOF_WANTNUKEWARNING`：网络盘/超配额时不再静默永久删除 | P-1 | 10 分钟 | 数据安全 |
| 3 | 中转站：删源移出 UI 线程 + 清空/移除时取消在途任务 | P-2、P-3 | 0.5–1 天 | 大数据不冻界面、不产生孤儿文件 |
| 4 | `TryReadVersion` 改用 `FindKey`：阻断 `"schemaversion"` 大小写绕过只读保护 | C-1 | 20 分钟 | 不覆盖用户未来版本配置 |
| 5 | 只读加载后允许"另存为"或明确告知"本次会话无法保存" | C-3 | 2 小时 | 避免用户改了一整轮才发现全丢 |
| 6 | ffmpeg 强制输出 `W×H`（加 `pad`）并校验实际帧尺寸 | K-1 | 1 小时 | 非 16:9 视频不再整段错位 |
| 7 | `WaveOutPlayer` 短读补满 + EOF 先 `waveOutReset` | K-2、K-3 | 2 小时 | 消除爆音、避免退出崩溃 |
| 8 | 玻璃后端改能力探测 + `_frameSourceTried` 可复位 | G-1、G-2 | 半天 | 默认不再被 build 号钉死在 18ms GDI |
| 9 | `GlassCapture.Frames` 改为单采集线程或加锁 | G-3 | 半天 | 多卡/远程桌面下玻璃不再随机坏 |
| 10 | 保存链路只落盘一次、只重建一次，返回结构化 `ApplyResult` | S-02、S-04 | 1 天 | 保存状态不再说谎、界面不抖 |
| 11 | 立即模式（`--selftest`/`--screenshot*`）先于单实例互斥；截图模式返回失败码 | X-1、T-2 | 2 小时 | 让验证链重新可信 |
| 12 | 修 `DescribeIoError` 的换行裁剪 + 模板对齐上限 | C-2、C-4 | 1 小时 | 消除两条"错误路径自身崩溃" |

> 前 12 项合计约 **4–6 人日**；做完后 26 条 P1 会消掉 **18 条**，且都是用户可感知的。

### 0.4 三条主干结论

1. **纪律缺执行器。** 项目把大量约束写在 `AGENTS.md` 和注释里，但构建链上只有"0 警告"这一条真的会失败。没有 CI、没有 analyzers、没有 `TreatWarningsAsErrors`、没有测试工程；唯一自动验证是 1,855 行自检，而它可被运行时实例"假通过"。**规则只对读过 AGENTS 的人有效。**
2. **并发模型靠注释维系，且覆盖不均。** `FfmpegPlayer` 有 16 处代次/收尾逻辑（做得好）；`GlassFeed`/`GlassRuntime`/`DesktopFrameSource`/`WaveOutPlayer` 是 **0 处**。26 条 P1 里 20 条落在"谁在哪个线程、失败有没有人看"上。
3. **历史项是点状修复。** 2.26.0 复盘后 47 个可核对项里 24 项原样、8 项半修。根因没动就会换形态复发：`L-6`（探针不可信）从"3–4/6 误报"变成"6/6 必报"；`L-17`（Join 超时）原封不动；`L-22`（二次落盘）还长出了"保存状态说谎"的次生缺陷。

---

## 1. 审阅范围、方法与证据口径

### 1.1 规模

| 项目 | 数值 |
| --- | --- |
| 手写 `.cs` 文件 | 153 |
| C# 行数（不含 `obj/` `bin/`） | 约 54,700 |
| 最大文件 | `LucideIcons.cs` 6,927；`SelfTest.cs` 1,855；`CardMediaView.cs` 1,230 |
| XAML | 2,332 行（设置窗 1,360、主题 740） |
| 程序集 | 9 个（宿主 + Contracts + UiKit + 6 插件），全部 `RootNamespace=MirrorpondHUD` |
| 第三方 NuGet | **0**（`dotnet list package` 实测"未找到任何包"） |
| P/Invoke 声明 | 117 处，散落在 20 个文件 |

### 1.2 我实际验证了什么（不是转述）

| 验证项 | 方法 | 结论 |
| --- | --- | --- |
| "0 警告 0 错误" | `dotnet build -c Release -v:m` | **成立**（21.75s，0 warnings / 0 errors） |
| "零第三方依赖" | `dotnet list package --include-transitive` | **成立** |
| 空 `catch` 数量 | 正则全仓统计 | **138 处 / 42 文件**（分布见 §5.2） |
| 代次计数覆盖 | 按文件统计 `_run`/`_generation` | `FfmpegPlayer` 16 处；`GlassFeed`/`GlassRuntime`/`DesktopFrameSource`/`WaveOutPlayer` **0 处** |
| `--selftest` 假通过 | 读 `App.OnStartup` 执行顺序 | **成立**：互斥检查在立即模式之前，第二实例 `Shutdown(0)` |
| `NIN_SELECT` 常量 | 对照 Win32 定义 | **成立**：`WM_APP+400` ≠ `WM_USER+0` |
| SchemaVersion 大小写绕过 | 读 `SettingsLoader` 三条分支 | **成立**：`TryReadVersion` 是全链路唯一精确取键处 |
| `DescribeIoError` 裁剪 bug | 读源码字节 + 推演 | **成立**：统一减 `Environment.NewLine.Length`(2)，匹配的后缀却有长度 1 的 |
| `RecycleBin.cs` 裸 NUL | `python` 定位 + `git diff --no-index` | **成立**：git 视该文件为二进制（`Bin 6131 -> 6132`），今后改动不进 diff |
| 主题键集合一致性 | 解析 XAML + `SettingsTheme.Palette` | 37 个画刷 / 33 个调色板键，菜单 5 键由 `HudMenuStyle` 逐实例注入（不是缺陷） |
| 卡片与设置窗默认强调色 | 读默认值 | **分叉**：卡片 `#2F6FEB`（蓝），设置窗 `#F2E900`（黄） |

### 1.3 没做什么（限制）

- **没有真机行为验证**：Win10 1909/2004、RDP、混合 DPI、ARM64 均未实测；涉及这些的结论标"待实测"。
- **没有跑 `--selftest` 与五种截图**：自检有真实副作用（§C-17、§X-1），在他人工作机上跑会改配置、注入按键，故只做静态推演。
- **没有用真实 ffmpeg/waveOut 复现**：媒体层的 K-1/K-2/K-3 由 API 语义与代码路径确定，具体表现标"高度可能"。
- **没有逐条核验 2,293 个 Lucide 码位**、没有审 theme 文件每个字段的范围校验（抽读 20 余处，未见单点缺陷）。

### 1.4 严重度与置信度定义

| 级别 | 含义 |
| --- | --- |
| **P0** | 启动即崩、必丢数据、远程可利用的安全漏洞 |
| **P1** | 功能错误 / 数据或配置静默损坏 / 可稳定复现的崩溃或冻结；有条件触发 |
| **P2** | 性能、健壮性、可观测性、可维护性的明显问题；或低频 P1 |
| **P3** | 小缺陷、死代码、文档漂移、风格与一致性 |

**置信度**：`确定` = 代码路径可完全证明；`高度可能` = 语义确定但需一次实测确认；`待实测` = 依赖具体机器/驱动/外部进程行为。

### 1.5 怎么读这份报告

- **§2 P1**：逐条给位置、关键证据、机制、影响、修复。建议先读这一节。
- **§3 P2**：按主题合并（配置、线程、互操作、性能、插件、控件、媒体、布局），每条 3–5 行。
- **§4 P3**：表格，只给位置、一句话问题、一句话建议。
- **§5 跨层结构性问题**：不属于任何单点，但决定后续成本（测试、日志、构建、主题、契约、死代码）。
- **§6 `AGENTS.md` §2–§7 审计**：用户特别授权，逐条对照"规则原文 / 实际落地 / 成本 / 建议改写"。
- **§7 修复路线图**、**§8 附录**（历史项总表、命令、子报告索引）。

各区域原始报告保留了每条问题的完整代码证据与补丁片段，位于 `docs/review/.work/`（该目录被 `.gitignore` 忽略，可随时删除）：

| 子报告 | 内容 |
| --- | --- |
| `.work/contracts-core.md` | 契约 / JSON / 配置 / 主题 / 日志 / 自检（29 条） |
| `.work/ui-settings.md` | 设置窗 11 个 partial / 热键 / 托盘 / 输入（21 条） |
| `.work/ui-glass.md` | 叠层窗口 / 玻璃合成 / DXGI / 渲染调度（19 条） |
| `.work/ui-text.md` | TextCanvas / 粒子 / 等高线 / 布局动画（21 条） |
| `.work/uikit.md` | 控件库 27 文件 + 媒体 8 文件（36 条） |
| `.work/plugins.md` | 6 张卡 / 加载器 / 指标 / 中转站（27 条） |

---

## 2. P1 问题（26 条）

> 编号沿用各区域报告的 ID（C=契约层、S=设置窗、G=玻璃、T=文本/布局、K=UiKit/媒体、P=插件、X=本报告跨层补充），便于与 `.work/` 原始报告对照。

### 2.1 数据与配置安全（6 条）

#### C-1【P1】`schemaversion` 大小写不一致会绕过"高版本只读"保护，把新版配置静默降级覆盖

- **位置**：`src/Core/SettingsLoader.cs:261`（唯一精确取键处）、`:68-72`（补写分支）、`:172-182`（`FindKey` 大小写不敏感）、`src/Core/ConfigStore.cs:206-211`（`migrated` 触发自动落盘）
- **证据**：

```csharp
// SettingsLoader.cs:171-182 —— 全链路都用 FindKey（忽略大小写）
static string FindKey(Dictionary<string, object> map, string key)
{
    if (map.ContainsKey(key)) return key;
    foreach (string existing in map.Keys)
        if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)) return existing;
    return null;
}

// SettingsLoader.cs:256-262 —— 唯独这里不用
if (root is null || !root.TryGetValue("SchemaVersion", out var raw) || raw is null)
    return true;                        // → hasVersion = false

// SettingsLoader.cs:68-72
if (!hasVersion) { needsSave = true; Log.Warn("设置缺少 SchemaVersion，按当前格式补写版本号"); }
```

- **机制**：`TryReadVersion` 是全链路唯一按精确大小写取键的地方。键写成 `"schemaversion"` 时 `hasVersion=false`：既不走 `version > CurrentSchemaVersion` → 只读分支，也不走 `<` → 迁移分支，直接落到"按当前格式反序列化"，`needsSave=true` 触发 `ConfigStore.Load` 末尾的自动 `Save`，把整棵树重写成 `SchemaVersion=7`。
- **影响**：手改配置 / 外部工具重写键名大小写，或从更高版本降级且键名大小写不同时，未来版本新增字段在绑定阶段被静默丢弃，随后原文件被覆盖；`.bak` 只有一代，之后再保存一次原文即彻底消失。日志文案读起来像正常兼容处理，不是"降级覆盖"。
- **修复**：

```csharp
string key = FindKey(root, "SchemaVersion");     // 与其它读取保持一致
if (key is null || root[key] is null) return true;
```

并把"存在任意大小写变体版本键但解析失败"按 `corrupted` 处理，而不是"补写"。补自检：`Load("{\"schemaversion\":999,...}")` 必须 `futureFlag=true`。
- **置信度**：确定（代码路径）；触发频率取决于用户是否手改配置。

#### C-2【P1】`DescribeIoError` 在"报错"路径里自己会抛异常，保存失败可能升级成进程退出

- **位置**：`src/Core/ConfigStore.cs:366-368`、`src/Ui/SettingsApplier.cs:392-398`
- **证据**：

```csharp
// ConfigStore.cs:366-368
string message = string.IsNullOrEmpty(ex.Message) ? "未知错误" : ex.Message;
while (message.EndsWith("\r\n") || message.EndsWith("\n") || message.EndsWith("\r"))
    message = message.Substring(0, message.Length - Environment.NewLine.Length);  // 固定减 2

// SettingsApplier.cs:392-398 —— 没有 try
public bool SaveSettings()
{
    bool saved = ConfigStore.Save(_host.Settings);
    ...
}
```

- **机制**：三种换行后缀共用一个裁剪长度。Windows 上 `Environment.NewLine.Length == 2`：
  - 以孤立 `\n` 或 `\r` 结尾的消息会被多删一个真实字符（错误文案被吃掉末位）；
  - 消息恰好只有一个字符 `"\n"` 时 `Substring(0, -1)` 抛 `ArgumentOutOfRangeException`，异常从 `SaveCore` 的 `catch` 逃出 `Save`，`SettingsApplier.SaveSettings` 没有 try，最终落到 `DispatcherUnhandledException`。
- **影响**：本该"提示保存失败"的路径变成"退出应用"；即使不抛，错误串也会被截断，降低可诊断性。
- **修复**：按命中的后缀长度裁剪（`EndsWith("\r\n") ? 2 : 1`），并给 `SaveSettings` 包一层 try；补一条自检：`DescribeIoError` 对 `"\n"`、`"\r"`、`"x"`、`""` 均不抛。
- **置信度**：确定。

#### C-3【P1】只读加载后提示"可重新保存"，但整个会话内保存必然被拒 → 用户改动全部丢失

- **位置**：`src/Core/ConfigStore.cs:94-99`（提示）、`:312-317`（拒绝）、`:76`（`LastLoadReadOnly` 只在 `Load` 开头复位）
- **证据**：

```csharp
// :94-99
LastLoadReadOnly = true;
LastLoadWarning = $"...本次只读加载默认设置；不会覆盖原文件。处理后可重启或重新保存。";
// :312-317
if (LastLoadReadOnly) { LastSaveError = "配置处于只读加载状态...拒绝覆盖原文件"; return false; }
```

- **机制**：`LastLoadReadOnly` 是进程级静态字段，只在 `Load()` 开头重置。用户在本次会话中修好文件权限、点"保存并应用"，`Save` 仍然拒绝；而启动弹窗明确写着"可重新保存"。
- **影响**：用户以为已保存，实际所有改动只存在于内存；退出即丢。设置窗只会显示"保存失败"，但不会告诉用户"必须重启"。
- **修复**：二选一并写进文案——(a) 只读态下提供"另存为/导出配置"；(b) 设置窗进入只读模式并禁用保存按钮 + 明确"重启后生效"；或 (c) 保存前重新探测可写性并允许用户确认覆盖。
- **置信度**：确定。

#### C-4【P1】模板对齐参数没有上限：`{x,-2147483648}` 抛 `OverflowException`，`{x,2147483647}` 申请 2 GB

- **位置**：`src/Contracts/Text/TemplateEngine.cs:84`、`:117-120`
- **证据**：

```csharp
if (!int.TryParse(alignPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out alignment)) return false;
...
int pad = Math.Abs(alignment);          // int.MinValue → OverflowException
if (text.Length >= pad) return text;
return alignment > 0 ? text.PadLeft(pad) : text.PadRight(pad);   // 1e9 → 巨型分配
```

- **机制**：`Content` 是用户可编辑字段，对齐参数直接来自模板文本，没有范围检查。`Math.Abs(int.MinValue)` 必抛；`PadLeft(2_000_000_000)` 直接尝试分配约 4 GB。
- **影响**：用户输入即可让卡片渲染线程抛未捕获异常，或触发 `OutOfMemoryException`。这是本次审阅里唯一"日常输入即可触发"的崩溃路径。
- **修复**：`pad = Math.Clamp(Math.Abs((long)alignment), 0, 1024)`；超限按 0 处理并 `Log.Warn` 一次。补两条自检（极值不抛、超限被夹）。
- **置信度**：确定。

#### C-5【P1】损坏恢复后的"自动落盘失败"被丢弃，且 `.broken-*` 每次启动新增一个、永不清理（历史项 L-18 仍未修）

- **位置**：`src/Core/ConfigStore.cs:203-211`（返回值无人接收）、`:388-399`（备份命名）、`src/App.xaml.cs:143`（只消费 `LastLoadWarning`）
- **证据**：

```csharp
if (!LastLoadReadOnly && !brokenBackupFailed)
{
    bool keepBackup = !recovered;
    if (migrated || recovered || corrupted || empty)
        Save(settings, keepBackup);          // 返回值无人接收，LastLoadWarning 不追加
}
```

- **机制**：恢复路径（损坏 / 从 `.bak` 拉起 / 迁移）最后会尝试立刻落盘，但 `Save` 的 bool 被丢弃。若此时磁盘满 / 只读，用户只看到"已从备份恢复"，以为已经修好；下次启动重复同一流程，并再生成一个 `.broken-<时间戳>`。全仓没有任何清理 `.broken-*` 的代码。
- **影响**：恢复失败不可见；配置目录随启动次数累积残留文件。
- **修复**：检查返回值，失败时把原因追加进 `LastLoadWarning` 并抬成 `Log.Error`；启动时清理超过 N 个或早于 M 天的 `.broken-*`（保留最近 3 个）。
- **置信度**：确定。

#### C-6【P1】`CardSettings.ExtrasAs<T>` 有时返回副本、有时返回活引用，插件改完可能静默不落盘

- **位置**：`src/Contracts/Core/Settings.cs:940-968`
- **证据**：

```csharp
public T ExtrasAs<T>(string key) where T : class, new()
{
    object raw = null;
    if (Extras is not null && !string.IsNullOrEmpty(key)) Extras.TryGetValue(key, out raw);
    if (raw is null) return new T();
    var typed = raw as T;
    if (typed is not null) return typed;                  // ← 活引用（刚 SetExtras 过）
    try { T parsed = Json.BindNode<T>(raw); return parsed ?? new T(); }   // ← 副本（磁盘读入）
    catch (Exception ex) { Log.Warn(...); return new T(); }
}
```

- **机制**：`Extras` 从磁盘反序列化后是节点树，第一次 `ExtrasAs` 返回"绑定出的副本"；若调用方紧接着 `SetExtras`，同一会话下一次 `ExtrasAs` 返回的是**同一个活对象**。插件"取设置 → 改 → 保存"在两种会话下语义不同：副本会话中，如果插件忘了写回，改动丢失且无日志。
- **影响**：配置静默丢失，且"时好时坏"（取决于本次是否已 `SetExtras`），是最难自查的一类。内置六卡目前都走 `*Of()`（读后立即 `SetExtras`）所以没暴露；第三方插件没有这个约定。
- **修复**：统一语义——要么始终返回副本并要求显式 `WriteExtras`，要么始终返回活引用并在文档里规定"必须写回"。推荐前者 + 提供 `TryGetExtras<T>(out T)` 的清晰 API。
- **置信度**：确定。

### 2.2 设置窗、托盘与桌面集成（4 条）

#### S-01【P1】托盘左键单击永远无效：`NIN_SELECT` / `NIN_KEYSELECT` 常量算错

- **位置**：`src/Ui/TrayIcon.cs:37-38`、`:125-132`、`:288-297`
- **证据**：

```csharp
const int WM_APP = 0x8000;
const int NIN_SELECT = WM_APP + 400;      // 0x8190 ← 错
const int NIN_KEYSELECT = WM_APP + 401;   // 0x8191 ← 错
const int WM_CONTEXTMENU = 0x007B;
...
switch (message) { case NIN_SELECT: case NIN_KEYSELECT: OpenSettings?.Invoke(); break; }
```

- **机制**：`NOTIFYICON_VERSION_4` 下 Shell 发的是 `NIN_SELECT = WM_USER + 0 (0x0400)`、`NIN_KEYSELECT = WM_USER + 1`。写成 `WM_APP + 400/401`（0x8190/0x8191）后 `case` 永不命中。回调消息参数解析（`LOWORD=通知码`、`HIWORD=图标 id`）本身是对的，所以只有左键坏、右键菜单正常。
- **影响**：托盘左键"打开设置"是文档与气泡都宣传的功能，实际点了没反应；若 `NIM_SETVERSION` 失败退回 v3，连右键也会失效。
- **修复**：

```csharp
const int WM_USER = 0x0400;
const int NIN_SELECT = WM_USER + 0;
const int NIN_KEYSELECT = WM_USER + 1;
// 兜底：同时处理 v3 的 WM_LBUTTONUP / WM_RBUTTONUP
```

- **置信度**：确定。

#### S-02【P1】保存链路的二次落盘会让"保存失败"变成假消息或假成功（L-22 的次生后果）

- **位置**：`src/Ui/HotkeyRouter.cs:121-125`、`src/Ui/SettingsApplier.cs:148-155`、`src/Ui/SettingsWindow.Save.cs:142-149`、`src/Core/ConfigStore.cs:352`
- **证据**：

```csharp
// SettingsApplier.Apply
bool saved = SaveSettings();          // 第一次落盘
_host.UpdateEngine();
_host.RebuildHotkeys();               // 路由器可能再落盘一次
// HotkeyRouter.Register
if (saved) { _host.SaveSettings(); _host.ReloadSettings(); }
// ConfigStore.cs:352 —— 成功即清空错误
LastSaveError = "";
```

- **机制**：`Apply` 取第一次 `SaveSettings()` 的结果，但热键路由在注册成功后会再 `SaveSettings()` 一次。第二次成功会把 `LastSaveError` 清空，于是设置窗可能报"已保存"而第一次其实失败；反过来，第二次失败会覆盖第一次的成功状态，界面报"保存失败、重启回滚"而磁盘上已经是新配置。
- **影响**：违反 `AGENTS §5` 的 P0-2 纪律——用户无法从界面判断配置到底写没写盘。
- **修复**：一次操作只允许一次落盘；`Apply` 返回结构化结果 `ApplyResult { Saved, SaveError, HotkeyWarning, AutoStartError }`，设置窗按优先级拼接状态行。若热键注册需要"先落盘再注册"，则把顺序改成"注册→一次落盘→失败回滚内存"。
- **置信度**：确定。

#### S-03【P1，历史项 L-17 仍未修】`KeyboardHook.Dispose` 在 `Join` 超时后仍释放 `_ready`，后台线程可抛 `ObjectDisposedException` 终止进程

- **位置**：`src/Input/KeyboardHook.cs:159-174`、`:116`
- **证据**：

```csharp
// 工作线程
_hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
...
_ready.Set();
// 释放方
if (thread is not null && thread.IsAlive) { try { thread.Join(1500); } catch { } }  // 返回值丢弃
try { _ready.Dispose(); } catch { }                                                 // 无条件释放
```

- **机制**：`Join(1500)` 超时后线程仍卡在 `SetWindowsHookEx`/消息循环里，随后 `_ready.Dispose()`；线程恢复执行到 `_ready.Set()` 时抛 `ObjectDisposedException`。该线程是裸 `Thread`，没有 Dispatcher 兜底，异常直接终止进程。
- **影响**：退出时偶发"无提示崩溃"；在杀软/输入法接管 `WH_KEYBOARD_LL` 的机器上概率更高。
- **修复**：只有 `Join` 返回 true 才释放 `_ready`；超时则把释放权移交给线程末尾（"认领收尾"CAS），或干脆不 `Dispose`（`ManualResetEventSlim` 的泄漏代价远小于崩溃）。
- **置信度**：确定（异常路径）；触发概率取决于环境。

#### S-04【P1，历史项 L-22 仍未修】一次"保存并应用"把整棵卡片列表重建三次，模块编辑区块被反复创建/丢弃

- **位置**：`src/Ui/SettingsWindow.Save.cs:139`、`src/Ui/SettingsWindow.Load.cs:178`、`src/Ui/SettingsWindow.xaml.cs:288-295`、`src/Ui/HotkeyRouter.cs:123-124`
- **证据**：

```csharp
// 链路：Apply → RebuildHotkeys → 路由器 ReloadSettings → Reload → LoadUi（重建①）
if (saved) { _host.SaveSettings(); _host.ReloadSettings(); }
public bool Reload(Settings live) { if (!LoadUi(live)) return false; ... }
// 回到保存流程再建一次
RebuildCardList();                                    // Save.cs:139（重建②）
var editor = new CardEditor(config, CardRegistry.Get(config.Kind), _working, expanded);  // 每次 new
_section = source.CreateEditor(_config, _settings, RefreshSummary);
```

- **机制**：保存触发热键重注册 → 路由器 `ReloadSettings` → 设置窗 `Reload` → `LoadUi` 重建一次；保存流程末尾又 `RebuildCardList` 再建一次；`LoadUi` 内部对每个卡片 `new CardEditor`，而 `CardEditor` 会调用插件 `CreateEditor`。
- **影响**：保存后界面抖动；插件 `CreateEditor` 被重复调用（有副作用的编辑器会重复初始化）；`_working` 被替换后旧的 `CardEditor._config` 指向废弃对象，正是 `ui-settings` 报告里"滑块改到废弃树"的根因。
- **修复**：保存后不重建（只刷新摘要）；`ReloadSettings` 只更新数据不触发 `LoadUi`；把"重建"收敛到一个显式入口，并加 `Debug.Assert` 防重入。
- **置信度**：确定。

### 2.3 玻璃与渲染平台（5 条）

#### G-1【P1】DXGI 桌面复制被 OS 生成号门控：`Auto` 在 Win10 与 Win11 24H2 及更早一律退回 18 ms 的 GDI 读屏

- **位置**：`src/Ui/DesktopFrameSource.cs:161`（默认值 `src/Contracts/Core/Settings.cs:593`）
- **证据**：

```csharp
int build = Environment.OSVersion.Version.Build;
bool force = mode == "Gpu";
if (build < 26200 && !force)
{
    reason = $"Windows 生成号 {build} 的 DXGI 桌面复制 vtable 布局没实测过（取图方式选「GPU 加速」可以强制尝试）";
    return false;
}
```

- **机制**：模块用**硬编码 vtable 下标**（旧 19 / 新 22）调用 `IDXGIOutput1::DuplicateOutput`，只在自己那台 build 26200 的机器上验证过，于是把"未实测"编码成"直接不用"。默认 `GlassSource="Auto"` 即 `force=false`，Win10（19041–19045）与 Win11 21H2–24H2（22000–26100）全部落到 GDI `BitBlt`。
- **影响**：选"桌面取样"的用户玻璃明显滞后、拖动跟手变差、UI 线程每轮约 18 ms（10 fps 下约 18% UI 时间）。后端选择只进一条 `Info`，`GlassRuntimeState.Describe()` 也不显示实际后端，设置窗看不出在跑 GDI。
- **修复**：门控改为能力探测——先建 duplication，用已有 `GetDesc` 宽高校验布局，失败 `Log.Warn` 带 build/hr 后回退；`Auto` 在 19041+ 默认尝试。若不改探测，至少把 `Backend` 字段加进 `GlassRuntimeState` 并在设置窗显示。
- **置信度**：确定（门控与默认值）；GDI 体感待实测。

#### G-2【P1】`TryCreate` 失败一次后 `_frameSourceTried` 永不复位——连改设置都救不回来，只能重启进程

- **位置**：`src/Ui/GlassFeed.cs:715`（复位点 `:734-739`）
- **证据**：

```csharp
715| if (_frameSource is not null || _frameSourceTried) return _frameSource;
716| _frameSourceTried = true;
...
734| static void ReleaseFrameSource()
735| {
736|     if (_frameSource is null) return;      // ← 创建失败时在这里早退
739|     _frameSourceTried = false;             // ← 因此永远执行不到
```

- **机制**：首次探测失败（例如 G-1 的 build 门控）后 `_frameSourceTried=true` 且 `_frameSource=null`；`ReleaseFrameSource` 第一行就 `return`，复位语句永远不会执行。用户改成"GPU 加速"后，`_frameSourceTried` 仍是 true，本次进程不会再试。
- **影响**：一次降级变成终身降级；用户只能重启。与 `L-20`（背景图失败永久缓存）是同一个反模式。
- **修复**：`ReleaseFrameSource` 先复位标志再判空，或把标志与"末次尝试时间/配置指纹"绑定（配置变化时清零）。
- **置信度**：确定。

#### G-3【P1】`GlassCapture.Frames` 是静态 `List`，多张卡并发 `Grab` 时无锁读写——注释里的"只在 UI 线程"假设不成立

- **位置**：`src/Ui/GlassCapture.cs:128`（写 `:455-465`、`:504`）；`src/Ui/GlassRuntime.cs:295`
- **证据**：

```csharp
static readonly List<Frame> Frames = new(6);
...
Frames.RemoveAll(f => f.Window == window);
Frames.Add(new Frame { ... });
if (Frames.Count > 8) Frames.RemoveAt(0);

// GlassRuntime：_glassBusy 是每个窗口一个实例字段
bool _glassBusy;
_glassBusy = true;
Task.Run(() => { image = GlassCapture.Grab(layers, left, top, width, height, blur); ... });
```

- **机制**：`_glassBusy` 只能阻止同一窗口重入。N 张卡 = N 个 `GlassRuntime`，兼容模式下 N 个线程池线程同时进入 `Grab`，对同一个 `static List<Frame>` 做遍历/`RemoveAll`/`Add`/`RemoveAt`。`List<T>` 无锁并发写入会导致内部数组与 `_size` 不一致。
- **影响**：典型现场是 RDP（系统亚克力不可用、走兼容模式）且 ≥2 张卡：玻璃时好时坏，日志偶发"毛玻璃取样失败"，最坏缓存永久错乱。属于"静默降级为卡顿"。
- **修复**：最小改动给 `Frames` 的所有访问加一把静态锁；更好的是把采集串行化到**一个**采集线程，`FindLayers`/`Grab`/`Frames`/`DesktopBackground` 都归它所有，锁自然消失（与 G-6 一起做）。
- **置信度**：确定（无锁并发访问）；可观测后果的程度待实测。

#### G-4【P1】`SetWindowDisplayAffinity` 的返回值被丢弃：截屏排除失败时玻璃会拍到自己，零日志

- **位置**：`src/Ui/GlassFeed.cs:274`（另见 `:300-302`）
- **证据**：

```csharp
try { SetWindowDisplayAffinity(hwnd, exclude ? WdaExcludeFromCapture : WdaNone); }
catch (Exception ex) { Log.Warn($"设置截屏排除失败: {ex.Message}"); }
...
try { SetWindowDisplayAffinity(hwnd, exclude ? WdaExcludeFromCapture : WdaNone); }
catch { }
```

- **机制**：`WDA_EXCLUDEFROMCAPTURE (0x11)` 需要 Win10 2004+；更早系统返回 `FALSE`（P/Invoke 声明为 `int`），两处调用都不用返回值，`catch` 只可能捕获 `DllNotFoundException`。
- **影响**：排除失败后 GDI `BitBlt`/DXGI 复制会把卡片连同上一帧玻璃一起拍进去，玻璃又画在卡片里 → 拖动时自反馈拖影/整块发亮，日志 0 条线索。也会被老板键/反作弊软件拦截。
- **修复**：检查返回值 + `Marshal.GetLastWin32Error()`（补 `SetLastError=true`），失败时 `LogThrottle` 记一条并把"排除是否成功"作为桌面取样的前置条件；不成立就降级到系统亚克力/兼容。
- **置信度**：待实测（需 Win10 旧版或安装拦截类软件）。

#### G-5【P1】`PrintWindow` 没有超时，被挂起的 `_glassBusy` 会把兼容玻璃永久冻住且不报错

- **位置**：`src/Ui/GlassCapture.cs:502`；`src/Ui/GlassRuntime.cs:293,311`
- **证据**：

```csharp
// GlassCapture：同步 SendMessage(WM_PRINT)，无超时
if (!PrintWindow(window, memoryDc, PW_RENDERFULLCONTENT)) { ... }

// GlassRuntime：busy 只在投递回来的委托里复位
_glassBusy = true;
Task.Run(() => {
    image = GlassCapture.Grab(layers, left, top, width, height, blur);
    ...
    _window.Dispatcher.BeginInvoke(new Action(() => { _glassBusy = false; ... }));
});
```

- **机制**：`PrintWindow` 通过 `WM_PRINT` 同步请求目标窗口绘制，目标进程消息循环不响应时会一直阻塞在跨进程 `SendMessage`。`_glassBusy` 只在 `BeginInvoke` 的委托里复位，而那个委托永远排不上。此后每个 tick 都在 `if (_glassBusy) return;` 处直接返回。
- **影响**：卡片后面压着一个挂起的应用（无响应对话框、崩溃但未回收的进程、Explorer 卡住）时，玻璃冻结在最后一帧、定时器空转、日志 0 条；唯一恢复途径是重启。
- **修复**：加看门狗与代次（与 G-3 的单一采集线程合并）：`Task.WhenAny(grab, Task.Delay(2000))`，超时作废本次并复位 `_glassBusy`，`LogThrottle` 记一条；连续 N 次超时把该目标从 layer 列表熔断。
- **置信度**：待实测（阻塞时长取决于目标进程），但"无超时"由代码确定。

### 2.4 媒体（3 条）

#### K-1【P1】ffmpeg 侧输出尺寸与读帧缓冲尺寸不保证一致，非 16:9 / 奇数高视频会整段画面错位

- **位置**：`src/UiKit/Media/CardMediaView.cs:644`、`src/UiKit/Media/FfmpegPlayer.cs:126`、`:215-218`、`:523-524`
- **证据**：

```csharp
// CardMediaView：height 可能是奇数
int height = Math.Max(16, (int)Math.Round(width * aspect));
// FfmpegPlayer：再把高度取偶（可能减 1）
_width  = Math.Max(16, width  - width  % 2);
_height = Math.Max(16, height - height % 2);
psi.ArgumentList.Add($"scale={_width}:{_height}:force_original_aspect_ratio=decrease");
// 读线程按 _width×_height 解释每一帧
var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, buffer, width * 4);
```

- **机制**：`force_original_aspect_ratio=decrease` 的语义是"保持源宽高比装进 W×H 盒子"，**不保证输出正好 W×H**；源宽高比与盒子有一点差异（2.4:1、21:9、1918×1080，或高度被取偶减 1）ffmpeg 就输出更小尺寸，而读线程永远按 `_width * _height * 4` 切帧。
- **影响**：每帧多/少若干行 → 画面斜切/滚屏，`Position = _frames / _fps` 随之漂移，无异常无日志。16:9/4:3 恰好幸运通过。
- **修复**：`-vf "scale=W:H:force_original_aspect_ratio=decrease,pad=W:H:(ow-iw)/2:(oh-ih)/2:color=black"`；同时在启动前从 `Probe` 解析实际输出尺寸，与缓冲不一致直接 `RaiseFailed`。验证命令：`ffmpeg -i <非16:9> -vf scale=1280:532:force_original_aspect_ratio=decrease -f rawvideo -pix_fmt bgra -frames:v 1 - | wc -c`。
- **置信度**：高度可能（ffmpeg 语义确定，具体像素需实测）。

#### K-2【P1】`WaveOutPlayer` 自然 EOF 收尾未 `waveOutReset`/未等最后一缓冲播完，`UnprepareHeader` 失败被吞后仍 `FreeHGlobal`

- **位置**：`src/UiKit/Media/WaveOutPlayer.cs:341`、`:502`、`:545`
- **证据**：

```csharp
finally { if (_stop) ClaimCleanup(handle); else ReleaseResourcesOnEof(handle); }

void ReleaseResourcesOnEof(IntPtr handle)
{
    lock (_sync) { if (_stop || Volatile.Read(ref _resources) == 0) return; _stop = true; ... }
    ClaimCleanup(handle);                 // 没有 waveOutReset，没有等 WHDR_DONE
    Log.Info("音频自然播放结束，已释放音频设备与缓冲");
}

void ClaimCleanup(IntPtr handle)
{
    try { waveOutUnprepareHeader(handle, header, headerSize); } catch { }  // 返回值被忽略
    Marshal.FreeHGlobal(header);                                           // 失败也照放
    try { waveOutClose(handle); } catch { }
}
```

- **机制**：自然播完时缓冲区可能仍在播放队列里（`WHDR_DONE` 尚未置位），`UnprepareHeader` 会返回 `WAVERR_STILLPLAYING`；代码忽略返回值继续 `FreeHGlobal` 释放 WAVEHDR 与其后的数据缓冲区，驱动之后仍可能写这块内存。
- **影响**：退出/切歌时偶发崩溃或音频驱动异常；`waveOutClose` 失败也被吞。
- **修复**：EOF 路径先 `waveOutReset`（同步停止并回收所有 buffer），再逐个 `UnprepareHeader` 并检查返回码；失败 `Log.Warn` 且不要 `FreeHGlobal` 未回收的 buffer。
- **置信度**：高度可能（winmm 语义确定；崩溃概率取决于驱动时序）。

#### K-3【P1】`WaveOutPlayer.Feed` 把短读当整块，尾部零填充 → 周期性插入静音，音频总长被拉长

- **位置**：`src/UiKit/Media/WaveOutPlayer.cs:302`
- **证据**：

```csharp
int read = pcm.Read(buffer, 0, buffer.Length);   // 管道短读是正常现象
if (_stop) break;
if (read <= 0) break;
if (read < buffer.Length) Array.Clear(buffer, read, buffer.Length - read);  // 补零
...
Marshal.Copy(buffer, 0, target, buffer.Length);  // 整块 6144 字节写进声卡
```

- **机制**：匿名管道/`ffmpeg` stdout 的短读是常态（不是 EOF）。代码把短读尾部补零并整块提交，等于在音频流里周期性插入静音；同时音频总样本数被拉长，与视频帧时钟逐渐偏离。
- **影响**：规律性"爆音/卡顿感"，长视频 A/V 漂移；只有 EOF 尾块才应该补零。
- **修复**：用文件里已有的 `ReadFull` 循环读满；`ReadFull` 返回 false（真 EOF）时才补零并提交尾块。
- **置信度**：确定（短读语义）+ 高度可能（可听感）。

### 2.5 文件与插件（4 条）

#### P-1【P1】"移到回收站"缺 `FOF_WANTNUKEWARNING`：无法进回收站的文件会被静默永久删除

- **位置**：`src/Plugins/DropStation/RecycleBin.cs:65`；承诺文案 `src/Plugins/DropStation/StationFileActions.cs:141-146`
- **证据**：

```csharp
op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);
...
? "确定把这个文件夹及其内容移到回收站（可从回收站还原）？\n\n" + recyclable[0].Name
```

- **机制**：`FOF_ALLOWUNDO` 只是"尽可能走回收站"。目标卷没有回收站（网络共享、部分可移动盘）、超出配额或路径超 `MAX_PATH` 时 Shell 会退化为永久删除；此时未置 `FOF_WANTNUKEWARNING (0x4000)`，又叠加 `FOF_NOCONFIRMATION`，Windows 那条"无法放入回收站，是否永久删除？"的警告被一并抑制。类注释只讨论了 `MAX_PATH` 会以非 0 返回码失败，没覆盖"允许 Undo 失败但操作成功"。
- **影响**：用户看到"可从回收站还原"确认后，文件**不可还原**，状态行仍显示"已移到回收站 N 项"，唯一线索是回收站里找不到。
- **修复**：加 `FOF_WANTNUKEWARNING`；同时把文案改成条件式（检测卷是否支持回收站，或不承诺"可还原"）。
- **置信度**：高度可能（flag 语义确定；哪些卷必然退化需真机确认）。

#### P-2【P1】Move 模式在 UI 线程递归删源：大目录冻结叠层且不可取消

- **位置**：`src/Plugins/DropStation/StationCopyController.cs:314-337`（删除）、调用链 `:101-109`
- **证据**：

```csharp
_dispatcher.BeginInvoke(new Action(() => { ... CompleteCopy(item, job, context.Target); }));
...
else if (item.IsDirectory) Directory.Delete(item.Path, true);
else File.Delete(item.Path);
```

- **机制**：复制本体已修到线程池（B-2），但"删源"留在完成回调里，而完成回调是 `BeginInvoke` 到 UI 线程执行的。`Directory.Delete(path, true)` 会递归枚举再逐个删，耗时与文件数/杀软强相关；同一回调里还有 `SafeSize(target)` 与 `Persist()` 两处同步 IO。
- **影响**：Move 一个几万文件的目录 → 进度 100% 后整叠卡住数秒到数十秒，期间"取消"无效（`_copyDone` 已递增、`IsBusy` 已假）。不静默（用户看到卡死），但日志里只有"复制完成"，无法归因。
- **修复**：把删源与 `SafeSize` 挪进工作线程（`RunCopy` 内、token 保护下），完成回填只做 UI 与落盘；取消语义变为"复制完成但未删源"，文案区分三种状态。
- **置信度**：确定。

#### P-3【P1】清空 / 移除与在途复制竞态仍在：目标已落盘但列表里没有（孤儿文件）

- **位置**：`src/Plugins/DropStation/StationView.cs:589-611`；回填 `src/Plugins/DropStation/StationCopyController.cs:307-312,344-358`
- **证据**：

```csharp
foreach (StationItem item in selected) _items.Remove(item);   // 只动列表
...
_items.Clear();
Persist();
// 完成回调：
item.StoredPath = target;
item.Name = Path.GetFileName(target);
...
_sink.CopyPersistAndRebuild();
```

- **机制**：B-16 的修复只覆盖"视图已销毁"（`_sink.IsDisposed` → 单条合并）。视图存活时清空/移除只动 `_items` 与磁盘列表，既不 `CancelAll` 也不记录"这条被用户丢弃"；复制任务继续跑完，把 `StoredPath` 写到已脱离列表的 `item` 上，`Persist(_items)` 不含该项。
- **影响**：收纳大文件过程中点"清空" → 接收目录里出现 N 个用户没有记录的文件；Move 模式下源也没了，"两个地方都找不到"。
- **修复**：清空/移除时取消对应在途任务；完成回调检查条目是否仍在列表，命中"已移除"走单条合并并在状态行用 Warning 明示。
- **置信度**：确定（必然发生的时序）。

#### P-4【P1】`PdhQuery` 构造抛异常会把整个宿主按"启动失败"关掉

- **位置**：`src/Metrics/PdhQuery.cs:58-66` → `src/Metrics/MetricEngine.cs:63-67` → `src/Ui/OverlayController.cs:631-636` → `src/App.xaml.cs:165-177`
- **证据**：

```csharp
uint rc = PdhOpenQueryW(null, IntPtr.Zero, out _query);
if (rc != 0) { _query = IntPtr.Zero; throw new InvalidOperationException($"PdhOpenQuery 失败: 0x{rc:X8}"); }

public MetricEngine(Settings settings) { _settings = settings; _pdh = new PdhQuery(); }   // 无 try
_engine = new MetricEngine(_settings);                                                    // StartEngine 无 try
```

- **机制**：PDH 打开失败（计数器库损坏、`perfmon` 服务被禁用、受限账户、精简系统镜像）是可降级条件，但代码选择抛异常；`StartEngine` 没有 try，异常被 `App.OnStartup` 的外层 try 捕获 → 弹"启动失败"并 `Shutdown(1)`。
- **影响**："指标读不到"升级成"整个 HUD 起不来"，信息文本/按键/命令行等与 PDH 无关的卡片也一起不可用。与 `AGENTS §5` 的"GPU 指标不可用 → 降级"既定处理不一致。
- **修复**：`PdhQuery` 增加失败态而不是抛异常；`MetricEngine.Start()` 在 `_pdh` 未打开时记一条 Warn 并照常起线程（采样返回"数据不可用"）。
- **置信度**：确定。

### 2.6 布局与验证链（4 条）

#### T-1【P1】右对齐卡片松手后整叠横向回跳：`EndDrag` 用 slot 的旧坐标副本算右边缘

- **位置**：`src/Ui/CardLayoutCoordinator.cs:99-103`、`:154-164`、`:193-197`
- **证据**：

```csharp
// EndDrag：slot.Dialog.Overlay 是 BuildDialog 的副本，拖动期间不更新
_rightEdge = slot.Dialog.Overlay.X + slot.Window.CardWidth;
_lastAnchorX = _settings().Overlay.X;
LayoutCore(true, null, false);
// 重新布局
bool reset = alignChanged || anchorChanged || cardsChanged || !_widthsComplete;
if (widthsComplete && reset) _rightEdge = settings.Overlay.X + stackWidth;
...
x = Math.Max(0, _rightEdge - slot.Window.CardWidth);
```

- **机制**：拖动只更新宿主锚点，`slot.Dialog.Overlay.X` 仍是拖动前的值；`EndDrag` 用它算出旧的 `_rightEdge`，同时把 `_lastAnchorX` 更新成新锚点，导致 `anchorChanged=false`、`reset=false`，`_rightEdge` 不被重算。
- **影响**：右对齐模式下**每次**拖动松手，整叠横向跳回原位置——用户最常做的操作之一。
- **修复**：`_rightEdge = _settings().Overlay.X + StackWidth()`（一行 + 抽共用函数），并补一条 `EndDrag` 自检断言。
- **置信度**：确定。

#### T-2【P1】五种截图模式全部吞异常，进程退出码恒为 0

- **位置**：`src/Ui/Screenshots.cs:100-238`、`:311-314`、`:522-525`、`:674-677`、`:822-825`；`src/Ui/CommandLineMode.cs:43-76`
- **证据**：

```csharp
try { Settings settings = ConfigStore.Load(); ... }
catch (Exception ex) { Log.Error("生成卡片组截图失败", ex); }   // 返回 void
...
Screenshots.CaptureCards(shotCards);
return true;                // exitCode 仍是 0
```

- **机制**：`Capture*` 是 `void` + `catch → Log.Error`；`exitCode` 只在 `--selftest` 分支赋值。路径不可写、插件失败、0 张卡、磁盘满都退出 0。
- **影响**：`AGENTS §2` 把 `--screenshot*` 列为 UI 自查手段；CI/脚本会把"没生成产物"当通过，视觉验收本身不可信。
- **修复**：`Capture*` 返回 `bool` 或抛；`CommandLineMode` 把失败映射成非 0；"0 张卡"也判失败；输出文件不存在时补一条显式错误。
- **置信度**：确定。

#### T-3【P1】自绘文本层三处空 catch，把字体 / 几何 / DPI 失败静默降级

- **位置**：`src/Ui/TextCanvas.cs:65-74`、`:662-670`、`:796-804`
- **证据**：

```csharp
try { _keycapFamily = new FontFamily(new Uri("pack://application:,,,/"), "./assets/keycaps/#ReiconFont"); }
catch { _keycapFamily = new FontFamily("Microsoft YaHei UI"); }
```

- **机制**：Reicon 字体加载失败时静默回退 YaHei，键帽 PUA 字符会显示成方框；`BuildGeometry` 失败 → 图标基线不微调；DPI 查询失败 → 按 1.0 排版。三处都没有日志。
- **影响**："键帽全是方框"这类最难查的显示问题没有任何线索，直接违反 `AGENTS §5`。
- **修复**：包成 `Log.Warn` 一次性记录（含异常），或 `LogThrottle` 状态转移。
- **置信度**：确定。

#### X-1【P1，跨层补充】`--selftest` / `--screenshot*` 在已有实例运行时静默"假通过"（exit 0）

- **位置**：`src/App.xaml.cs:72-111`（单实例门）、`:133-136`（立即模式）；`src/Ui/CommandLineMode.cs:22-90`
- **证据**：

```csharp
// App.OnStartup
72| _mutex = new Mutex(true, "Local\\MirrorpondHUD.SingleInstance", out var createdNew);
73| if (!createdNew)
    {
        ... 最多 30 × 100ms 等唤醒事件 ...
110|        Shutdown(0);
111|        return;
    }
...
133| if (CommandLineMode.TryRunImmediate(args, out var immediateExitCode))
134| {
135|     Shutdown(immediateExitCode);
136|     return;
137| }
```

- **机制**：立即模式（自检、五种截图、冒烟）在**单实例检查之后**才执行。HUD 是常驻桌面应用，开发/验收时它通常正开着；此时 `MirrorpondHUD.exe --selftest` 走第二实例分支，给已运行实例发唤醒信号，然后自己 `Shutdown(0)` 退出。
- **影响**：**验证链出现假绿灯**。`AGENTS §2` 要求"改完必须跑一次构建 + `--selftest`"，但只要忘了关掉正在运行的 HUD，`--selftest` 就返回 0 且一项都没跑；截图同理（不生成文件、退出码 0）。这与 T-2 叠加，意味着"用退出码判断验收是否通过"在当前实现里是不可靠的。
- **修复**：先解析 `args` 中的立即模式；若命中，**绕过**单实例互斥直接执行（或用独立互斥名），并在日志里写明"立即模式，跳过单实例检查"；第二实例分支在带立即模式参数时返回非 0 并提示"请先退出正在运行的实例"。
- **置信度**：确定。

---

## 3. P2 问题（87 条，按主题归并）

> 每条给"位置 — 机制 — 修复要点"。完整代码证据见 `.work/` 对应区域报告。

### 3.1 配置、序列化、主题与日志（11 条）

- **C-7** `src/Contracts/Core/Json.cs:345-350` — 绑定只遍历目标字段，`map` 里多出来的键（拼错、旧版本、未来版本）从不检查；错类型字段也只 `catch` 后跳过。配置层没有任何"这个键没人认识"的反馈。**修**：绑定期统计未消费键，超阈值 `Log.Warn` 前 5 个；长期给 `Settings` 一份显式 key 白名单。
- **C-8** `src/Contracts/Core/Json.cs:48,66,82-101` — `Serialize` 对 `Dictionary<string,string>`、`char`、`DateTime`、`Type` 等合法 C# 类型产 `{}` 而不报错。**修**：兜底分支 `fields.Length == 0` 且非已知标量时 `Log.Warn`（限频）；给不可序列化类型加显式标记。
- **C-9** `src/Contracts/Core/Log.cs:154` — 写失败冷却窗口内所有日志（含"恢复"记录）被直接丢弃；两个实例同时写同一文件时第二个必然失败。历史项 `O-1` 仍未修。**修**：冷却期内把记录转入内存环形缓冲，恢复时补写一条"×× 条日志在冷却期被丢弃"；`Log` 初始化时按进程 id 分文件名。
- **C-10** `src/Contracts/Core/Settings.cs:42-56` vs `:96-119` — 数值 `Clamp` 全部静默，颜色回退却 `Log.Warn`，同一份 `Normalize` 两套可观测性标准。**修**：抽 `Clamped(field, v, min, max)`，越界 `Log.Warn` 一次。
- **C-11** `src/Core/ThemeFile.cs:421` — 主题导出用非原子 `File.WriteAllText`，同层已有 `AtomicFile`。**修**：`AtomicFile.Write(path, text, createBackup:false)`。
- **C-12** `src/Core/CardStateStore.cs:36-52,163-172` — `Read` 无大小上限；`IsSafeFileName` 不拦 `CON`/`NUL` 等 Windows 保留设备名。**修**：4 MB 预检；保留名黑名单（大小写不敏感）。
- **C-13** `src/Contracts/Core/AsyncProcess.cs:34,86-90,109-120` — stdout 固定 UTF-8（中文系统下 `cmd` 输出乱码）；超时后丢弃已读输出；每次调用留一个未取消的 `Task.Delay`。**修**：编码可配 + 超时保留部分输出 + `using` 取消 `Delay`。
- **C-14** `src/Contracts/Core/SecretStore.cs:55,81-85` — 无前缀即当明文直通；`Protect` 失败返回 `""`，与"用户清空 Key"不可区分。**修**：失败返回 `null`，让调用方显式处理；对明文形态打一次 Warn。
- **C-20** `src/Contracts/Cards/CardModule.cs:21-22`、`Icons.cs:54-95` — `ICardModule.Icon` 允许任意字符串，但运行期只认 `Icons` 登记的 32 个码位，第三方插件用其它合法 Lucide 图标会静默落到正文字体。**修**：注册时校验并 Warn，或在契约文档里写明"只支持登记表内的图标"。
- **C-21** `src/Core/ConfigStore.cs:18` — `SettingsNormalizer` 是 `public static` 可变委托，任何插件都能替换它（能改宿主的配置归一化）。**修**：改 `internal` + `InternalsVisibleTo`。
- **P-7** `src/Contracts/Core/Json.cs:84,345` — 只反射公共字段：第三方插件按常规写属性 DTO，序列化得 `{}`、反序列化不回填、全程无日志。历史项 `B-4` 仍未修。**修**：最低限度对"零公共字段但存在可写属性"的类型打 Warn（`AGENTS §4.1` 的红线可以保留，但失败必须可见）。

### 3.2 自检体系（3 条）

- **C-15** `src/SelfTest.cs`（`RunUnitChecks`/`ReportUnitChecks`）— 24 个领域结果被压成 0/1，与"退出码 = 失败数"的约定不符；热键段、设备枚举段失败根本不计数。**修**：按领域累加失败数；报告输出 `FAIL` 行数。
- **C-16** `src/SelfTest.cs:313-342` — 自检每次在用户 `cards/` 目录下留一个空目录（同文件另一段 :1400 有 `finally` 清理，这段没有）。**修**：复制 `try/finally` 模板；这也是两段重复代码应合并的信号。
- **C-17** `src/SelfTest.cs:79,131-141,160-170,1814-1826` — 自检读写真实 `settings.json`、装全局键盘钩子并 `SendInput` 注入 F13、注册用户真实热键、需要真实 Dispatcher。**修**：加 `--selftest --isolated`（配置重定向临时目录、跳过输入注入与热键、允许无桌面跑纯逻辑段）。

### 3.3 线程、UI 冻结与响应性（11 条）

- **G-6** `src/Ui/GlassRuntime.cs:286`、`GlassCapture.cs:137,246,270` — 兼容取样每帧在 UI 线程枚举全表窗口、逐候选开进程句柄（历史 `P-3` 未修），每卡一个定时器放大 N 倍。**修**：`FindLayers` 移进单一采集线程；pid→进程名加缓存。
- **G-13** `src/Ui/OverlayWindow.xaml.cs:163`、`GlassBackdrop.cs:356` — 每张卡一个 2.5 s 置顶定时器，每次置顶都无条件重摆底板（历史 `P-5` 未修）。**修**：共享定时器统一遍历；`Follow` 比对 rect/z 未变即返回。
- **S-10** `src/Ui/SettingsWindow.Theme.cs:261-289` — 主题预览在每次 `UiChanged` 全量离屏渲染，保存/关闭也触发（历史 `P-7` 部分未修）。**修**：40 ms 合并器 + `_readingUi` 期间跳过。
- **S-11** `src/Ui/SettingsWindow.xaml.cs:335-350`、`Load.cs:436-446` — 关闭窗口的"脏检查"会 `CommitPendingEdits`（API Key 加密落盘）+ 原地 `Normalize`；用户选"放弃"也不撤销。**修**：拆分"判脏"与"提交+归一化"。
- **P-11** `src/Plugins/DropStation/StationStore.cs:70-76` — 加载时逐条同步 `File.Exists`，网络路径/离线盘会卡 UI（历史 `B-14` 未修）。**修**：存在性检查挪后台线程逐条回填（带代次）。
- **P-12** `src/Metrics/MetricEngine.cs:69-99` — `Start()` 在 UI 线程 `Thread.Sleep(250)` 预热（历史 `P-6` 未修）。**修**：预热移进采样线程第一轮。
- **P-17** `src/Plugins/DropStation/StationView.cs:378-407` — 每次 Rebuild 全量重建行并同步取图标，500 条上限直接打满 UI 线程。**修**：搜索改为过滤现有行 `Visibility`；`AddPaths` 只追加新行；长期上虚拟化。
- **T-4** `src/Ui/Contour/ContourDecoration.cs:92-97`、`ContourGeometryBuilder.cs:109-115` — 等高线 30 fps × 每卡 × 每级全网格从零重建，相位闸门实际失效，常态占一核。**修**：相位量化 + 降频 + 复用上一帧几何。
- **T-5** `src/Ui/ParticleTextLayer.cs:332-361` — 粒子每轮播放都在 UI 线程整块 `RenderTargetBitmap` + 全尺寸字节数组 + 上万候选点。**修**：RTB/像素缓冲按尺寸缓存，候选点池化。
- **K-12** `src/UiKit/Media/CardMediaView.cs:400,428,449`、`GifAnimator.cs:55` — 图片/GIF 全部在 UI 线程同步解码，GIF 还被解两遍，无像素预算。**修**：解码挪 `Task.Run`（`BitmapDecoder` 后台可用，`Freeze()` 回 UI）；GIF 只解一次并设总像素上限。
- **K-15** `src/UiKit/Media/Decoders.cs:263,320` — `FindFfmpeg` 首次调用在 UI 线程同步扫 PATH，断开的网络盘可冻结数秒。**修**：缓存命中直接返回；未命中返回 null 并后台预热。

### 3.4 玻璃、原生互操作与多屏（9 条）

- **G-7** `src/Ui/OverlaySurface.cs:629` — 背景图同路径不重载；加载失败后永久缓存失败状态（历史 `L-20` 未修）。**修**：缓存键加 `LastWriteTimeUtc + Length`；失败清键。
- **G-8** `src/Ui/DesktopBackground.cs:147` — 壁纸只按路径缓存，同路径换图/主题切换不更新；`WallpaperStyle` 语义不全（历史 `S13` 未修）。**修**：同一套"时间戳+长度"缓存键；补齐 0/2/6/10/22 样式语义。
- **G-9** `src/Ui/GlassFeed.cs:139` — `Detach` 不释放 `DesktopFrameSource`，玻璃关掉后 D3D 设备与桌面复制对象一直活着。**修**：`Detach` 时 `ReleaseFrameSource()` 并重置计数。
- **G-10** `src/Ui/GlassFeed.cs:213,365,707` — 副屏/越界矩形下每 5 秒重建 D3D 设备并重复刷日志；拖动补帧仍按"快速后端"算间隔（历史 `L-2` 后半）。**修**：把"越界"与"源故障"分开记账，越界时保鲜 `_frameSourceIdle`。
- **G-11** `src/Ui/GlassFeed.cs:438,812-821` — 全黑保险丝把"内容本来就是黑的"当成"读不到屏幕"，3 轮（约 300 ms）整叠降级并进入 3 s→15 s 振荡。**修**：判据改为"采集层是否失败"（`BitBlt` 返回值 / `TryReadInto` 失败），不看像素全黑。
- **G-12** `src/Ui/OverlayController.cs:651` — 第二实例唤醒事件仍在 `Start()` 最后创建，冷启动窗口期双击静默退出（历史 `L-5` 未修）。**修**：拿到互斥后立即创建事件。
- **G-14** `src/Ui/ScreenGeometry.cs:88` — `Overlay.Monitor` 用 `AllScreens` 枚举序号当持久身份，越界时静默钳制到最后一台。**修**：持久化 `DeviceName` 并反解；越界 Warn。
- **G-15** `src/Ui/GlassCapture.cs:145` — `FindLayers` 只过滤 `IsWindowVisible`，不查 DWM cloaked：其他虚拟桌面/挂起 UWP 的内容可能被采样显示。**修**：`DwmGetWindowAttribute(DWMWA_CLOAKED)` 非 0 直接跳过。
- **G-16** `src/Ui/OverlaySurface.cs:583` vs `GlassFeed.cs:41` / `GlassBackdrop.cs:31` — 投影留白三种口径：实际 `Margin=14`（ambient 默认开），采样与底板按 10 DIP、兼容取样按全窗。默认配置下就会多出 4 DIP 灰边/采样错位。**修**：收成一个共享函数，三处同源。

### 3.5 插件、文件与设置项（17 条）

- **P-5** `src/App.xaml.cs:209-229`、`src/Ui/CardSlot.cs:15` — 退出时 `_plugins=null` 但 `_controller` 仍持有 `_cards[].Module`，卸载探针必然报"模块 6/6 未回收"（历史 `L-6`，比 2.26.0 更糟）。**修**：验证前 `_controller=null`；上下文仍活改成一次性 Info 并写明"Extras 持有插件类型是预期"。
- **P-6** `src/Contracts/Core/Settings.cs:940-968` — `Extras` 存插件类型实例（钉住 ALC），`ExtrasAs` 是"读即写"（历史 `M-3` 未修）。**修**：方案 A 接受"退出不回收"并删掉探针判定、写进插件指南；方案 B `Extras` 只存节点，拆 `ReadExtras`/`WriteExtras`。
- **P-8** `src/Cards/CardPluginLoader.cs:568-578` — `Discard` 跳过 `ICardModuleLifetime`，而全仓零实现者（历史 `B-8` 未修）。**修**：补 lifetime 调用；给中转站/聊天卡做示范实现并写进指南。
- **P-9** `src/App.xaml.cs:123-129` — 插件加载失败只进日志，用户在设置页看不到"你装的插件没加载"。**修**：加载摘要送到托盘气泡或设置页状态行。
- **P-10** `src/Plugins/Chat/ChatCardSettingsView.xaml.cs:159-176` — 设置页提示用 `StartsWith("http")`，运行时闸门是 `IsSafeEndpoint`（历史 `B-9` 未修）。**修**：复用 `IsSafeEndpoint`。
- **P-13** `src/Metrics/MetricEngine.cs:132-136` — 采样循环异常不节流，持续坏掉时每 100–500 ms 一条 `Log.Error`（同文件 :149 已有 `LogThrottle`）。**修**：复用节流器，失败/恢复各一条。
- **P-14** `src/Plugins/DropStation/StationCopyController.cs:164-196` — 收纳目录时符号链接/联接点被静默跳过，Move 仍递归删源 → "收纳成功"但内容不全。**修**：逐条记账并 Warn；计划里出现 reparse point 时拒绝 Move 删源。
- **P-15** `src/Plugins/DropStation/StationFileActions.cs:289-300,317-339`（待实测）— `IsInside`/`UniqueTarget` 只做字符串归一化，不解析 reparse point，junction 可让复制越出接收目录。**修**：父链 reparse 检查，发现联接即放弃并 Warn。
- **P-16** `src/Plugins/Command/CommandRunner.cs:137-164` — `SplitCommandLine` 不识别未加引号的含空格 exe 路径，会静默跑成另一个程序或直接失败。**修**：优先吃引号，其次最长存在路径前缀；记录解析结果。
- **P-18** `src/Metrics/NvidiaSmi.cs:117-134,86-90` — 多卡只取第一张；超时分支不走节流。**修**：解析全部行（越界回落 + 一次 Warn）；超时并入节流。
- **P-19** `src/Plugins/DropStation/StationFileActions.cs:242-251` — 大小/速度格式化用当前区域性，非英文区域显示 `1,5 MB`。**修**：`CultureInfo.InvariantCulture`。
- **S-05** `src/Ui/SettingsWindow.Load.cs:126-131,509-517`、`SettingsDeviceCatalog.cs:30-57` — 设备枚举失败时下拉框回退到第一项，保存会静默改写 GPU/网卡/刷新率选择。**修**：`Fill*Combo` 返回"枚举是否可信"，不可信时写回原值。
- **S-06** `src/Ui/AutoStart.cs:49-81` — 写注册表失败只进日志，设置窗仍报"已保存并应用"。**修**：`TrySet(out string error)`，状态行追加"开机自启未生效"。
- **S-07** `src/Ui/AutoStart.cs:49,69-72` — `--autostart` 忽略"启动后最小化"，UI 仍展示为可用；`Set(bool,bool)` 第二参是死参。**修**：UI 置灰并说明，或真正实现该参数。
- **S-08** `src/Ui/SettingsWindow.xaml:368-376` — "预设"下拉框写死 4 项，与插件 `Presets` 靠索引硬对齐，越界静默 `return`。**修**：改绑插件数据源。
- **S-09** `src/Ui/HotkeyManager.cs:227-253` — 允许注册无修饰键的全局热键，单按 F5 就吃掉全系统 F5。**修**：至少一个修饰键（或 F13+）；加载时回填非法值为 `Ctrl+Alt+原键` 并 Warn。
- **S-12** `src/Ui/TrayIcon.cs:314-320` — Explorer 重启后无条件重加托盘图标，忽略用户已关闭托盘。**修**：拆"期望/实际"两个状态。

### 3.6 媒体状态机与性能（11 条）

- **K-4** `src/UiKit/Media/WaveOutPlayer.cs:194,211,312` — 历史 `L-11` 只修一半：4 槽全失败才返回 false，1–3 槽失败仍返回 true，坏槽首次写入即终止整条音轨。**修**：任一槽失败即视为失败，或记录每槽可用性、只轮转可用槽。
- **K-5** `src/UiKit/Media/WaveOutPlayer.cs:374` — `LastErrorName` 是 `static volatile string`，多张媒体卡并发启动时错误信息互相覆盖。**修**：改实例字段。
- **K-6** `src/UiKit/Media/WaveOutPlayer.cs:326,337,518` — 运行期音频死亡（热拔/写失败）不上报 UI；EOF 回收还打印"自然播放结束"，与真实原因相反。**修**：记录 `_exitReason`，非正常 EOF 打 Warn 并通知卡片。
- **K-7** `src/UiKit/Media/FfmpegPlayer.cs:510,106`、`CardMediaView.cs:1027` — 非循环视频自然播完后状态不收尾：`IsRunning` 恒 true、播放/暂停键失效、`Completed` 无人订阅。**修**：EOF 清 `_process` 并触发 `Completed`；卡片订阅后重置按钮。
- **K-8** `src/UiKit/Media/FfmpegPlayer.cs:682,702` — 暂停状态下去掉静音会立刻播放音频，视频仍冻结。**修**：`LaunchAudio` 后若暂停立即 `PauseAudio()`。
- **K-9** `src/UiKit/Media/VideoWindow.cs:123,150` — 视频子窗每次位置变化都重建圆角 region 并强制整窗重绘（历史 `P-9` 未修）。**修**：位置变化只 `SetWindowPos`，region 仅在尺寸/DPI 变化时重建。
- **K-10** `src/UiKit/Media/CardMediaView.cs:284,361,811` — 系统解码路径忽略媒体不透明度：`_host.Opacity` 管不到独立视频子窗口。**修**：`SetLayeredWindowAttributes` 同步 alpha。
- **K-11** `src/UiKit/Media/CardMediaView.cs:282,338,738` — `L-4` 的修复依赖 `previous` 与 `_settings` 不是同一实例，而 `ExtrasAs<T>` 恰恰会返回同一实例 → 差量失效，静音/速度/循环/自动播放可能不生效。**修**：记录"已应用值"或无条件调用幂等 setter。
- **K-13** `src/UiKit/Media/CardMediaView.cs:316` — 文件在播放中被删除/替换没有感知，也没有可见降级。**修**：进度 tick 里每约 2 s 检查一次存在性，失败显示"文件已被移动或删除"。
- **K-14** `src/Contracts/Core/AsyncProcess.cs:136`、`FfmpegPlayer.cs:987` — stdout 无上限；`ExtractImage` 对超大图片/坏文件是无界内存（解压炸弹）。**修**：`RunAsync` 加 `maxStdOutBytes`（默认 8 MB）；解图加尺寸上限。
- **K-16** `src/UiKit/Media/CardMediaView.cs:613`、`FfmpegPlayer.cs:523` — 逐帧 `BitmapSource.Create` + 每帧一次 `Dispatcher.BeginInvoke`，高帧率视频造成 GC 与 Dispatcher 队列压力。**修**：只保留最新帧（`Interlocked.Exchange` + 单次通知）。

### 3.7 文本、布局与纯算法（8 条）

- **T-6** `src/Ui/ParticleTextLayer.cs:196,427-471` — 动画中卡片尺寸变化时，重采样用的是"上一句"的变化字符集，会隐藏错字符。**修**：保存播放时的文本快照，重采样前重算遮罩。
- **T-7** `src/Ui/TextCanvas.cs:480-487,608-628` — TextCanvas 永不换行、永不省略，单行超长内容把卡片撑到屏外。**修**：`MaxTextWidth` + `TextTrimming.CharacterEllipsis`，或在 `MeasureOverride` 夹取。
- **T-8** `src/Ui/TextCanvas.cs:994-1006,883-908` — 逐字隐藏与光标定位按 UTF-16 code unit 切分，代理对/组合字符会被切半。**修**：下标提升到 text element（`StringInfo.ParseCombiningCharacters`/`Rune`）。
- **T-9** `src/Ui/CardLayoutCoordinator.cs:193-208` — 卡片堆叠不夹取到工作区，卡片多了会排到屏幕外且无法拖回。**修**：`LayoutCore` 末尾整体夹取（只平移锚点），并同步设置窗。
- **T-10** `src/Ui/CardLayoutCoordinator.cs:213-229,65-72` — 高度变化走无动画布局，并清掉拖动后定下的 `_alignOverride`。**修**：`LayoutIfNeeded` 走 `LayoutAnimated`；`_alignOverride` 只在 `BeginDrag` 清。
- **T-11** `src/Ui/Screenshots.cs:225-230`（待实测）— 截图 2× RTB 与自绘文本的 `PixelsPerDip` 不一致，字形与屏上不同。**修**：给 `TextCanvas` 注入 `PixelsPerDipOverride`，截图按真实 DPI 传入。
- **T-12** `src/Ui/Contour/ScalarGrid.cs:29-84` — 等高线是纯计算，但生产路径绕开了可测 API（`ScalarGrid.Build`/`Chain`），自检零断言，两套网格尺寸推导已漂移。**修**：builder 复用 `ScalarGrid.Build`；补 3 条纯计算断言。
- **T-13** `src/Ui/Contour/ContourExtractor.cs:526-583` — Chaikin 按 2^n 倍点数分配，点数组池只按"个数"限制 → LOH 常驻。**修**：轮数按点数自适应；池按总字节预算淘汰。

### 3.8 控件库与可访问性（6 条）

- **K-23** `src/UiKit/CardDataControls.cs:269,297,341` — `CardMetricTile.Delta` 赋值会吃掉 `Trend` 箭头，`SetValue` 每帧刷新时箭头消失。**修**：存原始文本，`UpdateTrend` 用 `marker + raw`。
- **K-24** `src/UiKit/CardDataControls.cs:83,161` — `CardBadge`/`CardTag` 的 `Tone` setter 把算出的 `CornerRadius` 反馈回 `ApplyStyle`，圆角逐次漂移。**修**：存原始 `_cornerRadius`。
- **K-25** `src/UiKit/**`（0 命中）— 整个控件库 0 个 `AutomationProperties`/`AutomationPeer`，自绘控件对读屏/自动化完全不可见。**修**：给有信息量的控件加 `Name/HelpText`，自绘控件重写 `OnCreateAutomationPeer`。
- **K-26** `src/UiKit/CardDataControls.cs:386` 等 — 控件没有 `DependencyProperty`：Value/Text/Tone 全是普通 CLR 属性，不能绑定、无 `AffectsRender` 语义，主题只能手工 `ApplyStyle`。**修**：至少把视觉输入（Value/Min/Max/Tone/Text）DP 化。
- **K-27** `src/UiKit/CardOverlayControls.cs:1032` — 静态 `CardToast.Show` 用 `new StyleSettings()` 套样式，永远拿不到宿主主题。**修**：加重载接收当前样式；旧重载标注"默认主题"。
- **K-28** `src/UiKit/CardOverlayControls.cs:113,193` — `CardDialog` 没有焦点陷阱，Tab 可把焦点移到遮罩后的控件。**修**：模态期间禁用其它子元素或 `KeyboardNavigation.TabNavigation=Cycle`。

### 3.9 死代码与 API 陷阱（2 条）

- **C-18** `src/Contracts/Core/LucideIcons.cs`（6,927 行）— 2,293 项字典 + 2,293 项数组是两份手工重复清单，而 `Get/TryGet/Names` 在生产代码中**零调用**；静态构造在首次绘制时于 UI 线程构建。**修**：删掉零调用 API 或改成生成代码；数组由字典投影。
- **C-19** `src/Contracts/Core/Keycaps.cs:16-21` — `IsKeycap` 是死代码，且其"按区间判断"与 `Icons` 里"不能按区间判断"的结论正面矛盾。**修**：删除，或在注释里写明为何两者不同。

### 3.10 跨层补充（9 条）

> 这些是横跨多个区域的工程/治理问题，不属于任何单一模块；详细论证见 §5、§6。

- **X-2** `src/App.xaml.cs:55-63` — UI 异常处理里 `args.Handled = false; Shutdown(1);` 自相矛盾：`Handled=false` 会让 CLR 继续终止进程（并可能触发 Windows Error Reporting），`Shutdown(1)` 的退出码不会生效。**修**：`Handled=true` 后再 `Shutdown(1)`，或 `Environment.Exit(1)`；保留 MessageBox 与日志。
- **X-3** `src/Plugins/DropStation/RecycleBin.cs:58` — 源码里内嵌了一个裸 NUL 字节（`$"{path}<0x00>"`），功能上等价于 `"\0"`，但 git 把整个文件判为二进制（实测 `Bin 6131 -> 6132`），今后任何修改都不进 diff，`grep`/`file` 也会跳过。**修**：改成 `"\0"` 转义，CI 加"源文件不得含 NUL"检查。
- **X-4** 版本号在 **15 处**手工同步（根/Contracts/UiKit/6 个插件 csproj + 6 个 manifest），另有 README 与版本历史引用。**修**：`Directory.Build.props` 单一 `<Version>`，manifest 由 MSBuild 生成或校验；把 `SelfTest.CheckBundledManifestVersions` 提前成构建目标。
- **X-5** `publish.bat:20-96` — 发布前 `rd /s /q bin obj`，把用户 `settings.json`/日志/插件暂存到 `%TEMP%` 再恢复；中途失败/断电时用户数据只在 TEMP，应用运行时删除会静默失败（`2>nul`）导致后续发布混架构。**修**：用独立 `-o publish\...` 输出目录，不销毁工作目录；用户数据迁到 `%LOCALAPPDATA%`。
- **X-6** 仓库无 `.github/`、无 `TreatWarningsAsErrors`、无 `global.json`；`.editorconfig` 的 IDE 规则默认不进构建（`EnforceCodeStyleInBuild` 未开）。"0 警告"是人工纪律而不是门禁。**修**：CI 开 `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + `AnalysisLevel=latest`；`global.json` 固定 SDK feature band。
- **X-7** 没有测试工程，唯一自动验证是 1,855 行自检；它不可隔离、不可并行、不可 CI、不可 mock（C-15/C-16/C-17、T-12）。**修**：抽 `tests/MirrorpondHUD.Tests`（net8.0，不依赖 WPF）覆盖纯逻辑；自检只保留环境冒烟。
- **X-8** `src/Contracts/Core/Log.cs:148-199` — 日志在全局锁内同步 `File.AppendAllText` + 轮转 `File.Move`，任何线程（含 UI/渲染）打日志都要等磁盘；调用点为 Debug 6 / Info 111 / Warn 276 / Error 62。**修**：内存队列 + 单写线程，退出 `Flush(2s)`；崩溃路径保留同步降级。
- **X-9** 全仓 **138 处** `catch { }`（媒体 44、Ui 52、插件 14、Core 13、Cards 4、Metrics 4、SelfTest 3、UiKit 控件 2…），与 `AGENTS §5` 的绝对禁令直接冲突；其中 Dispose 路径的空 catch 是**正确**的，`TextCanvas`/`WaveOutPlayer`/`FfmpegPlayer` 的才是危险的。**修**：`SafeRelease(Action, string what)` 统一合法空 catch，其余必须日志；CI 加 grep + allowlist。
- **X-10** 主题色有 **4 套来源**（`Theme.xaml` 37 个 brush、`SettingsTheme.Palette` 33 对、`HudMenuStyle.Light/Dark`、卡片 `ThemeTokens`），改一个颜色要改 2–3 处；且默认强调色已分叉（卡片 `#2F6FEB` 蓝 vs 设置窗 `#F2E900` 黄）。当前调色板全部用 `DynamicResource`，替换 brush 实例能工作，但新增 `StaticResource` 会立刻踩坑。**修**：XAML 单一色源 + `SettingsTheme` 只改 `Color`；自检断言键集合一致。

---

## 4. P3 问题（62 条）

| 编号 | 位置 | 问题 | 建议 |
| --- | --- | --- | --- |
| C-22 | `src/Core/ThemeCatalog.cs:23-66,51` | `Load` 每次全量重解析，`Id` 直接用品名 | 加缓存；Id 用稳定文件标识 |
| C-23 | `src/Contracts/Core/Log.cs:148-199,202-223` | 日志在调用线程同步落盘（含轮转 `File.Move`） | 见 §5.2 异步化 |
| C-24 | `src/Core/SettingsLoader.cs:97-111` | v6→v7 迁移静默丢弃另一套模糊参数 | 迁移时记一条 Info 汇总丢弃字段 |
| C-25 | `src/Contracts/Core/Settings.cs:469-483` | `NormalizeDrives` 给非盘符路径补反斜杠；盘符大小写不同会重复 | 只接受 `X:\` 形态；统一大写 |
| C-26 | `src/Core/ConfigStore.cs:395-396` | `.broken-*` 同秒内多次失败互相覆盖 | 加毫秒或序号后缀 |
| C-27 | `src/Core/ThemeFile.cs:766-773` | `effect` 接受 `Contour` 但错误文案只列 4 种；导出时非法软色静默丢弃 | 文案与校验表同源 |
| C-28 | `src/Contracts/Text/Variables.cs:59-64` | `NaN` 直接交给模板，显示 "NaN" | 格式化前判 NaN/Inf 并回落占位符 |
| C-29 | `src/Contracts/Core/Settings.cs:88` | 按 `char` 截断文本，可能切断代理对 | 按 text element 截断 |
| S-13 | `src/Ui/HotkeyManager.cs:78,158-172` | 热键 id 单调增长不复用，逼近 `0xBFFF` 后注册必失败且日志归因"被占用" | 空闲 id 回收池 |
| S-14 | `src/Ui/SettingsWindow.xaml.cs:507-538` | "关闭"仍是 `Hide`，模块区块/预览位图/两份深拷贝一直被持有 | 关闭时释放区块、重新加载时重建 |
| S-15 | `src/Ui/ShellIcons.cs:231-235` | `IShellItemImageFactory.GetImage` 丢 HRESULT，失败与"无缩略图"不可区分 | 检查 HRESULT 并节流日志 |
| S-16 | `src/Ui/TrayIcon.cs:351-372` | 托盘回退图标是共享 `SystemIcons.Application`，`Dispose` 会释放它 | `Icon.FromHandle` 后克隆，或持有独立副本 |
| S-17 | `src/Ui/TrayIcon.cs:334-349` | 气泡失败路径全空 catch/忽略返回值，固定 256 字符截断 | 检查返回码；截断显式加省略号 |
| S-18 | `src/Ui/SettingsWindow.Appearance.cs:161-192` | 字体列表首次在 UI 线程全量枚举；筛选框每键入换 `ItemsSource` | 启动预热 + 复用集合 |
| S-19 | `src/Ui/SettingsWindow.Appearance.cs:76-101` | `ApplyCliBoxes` 非法输入静默回退；任一框失焦重写四个框 | 逐框校验 + 只写变更框 |
| S-20 | `src/Ui/CommandLineMode.cs:57-62` | `--screenshot-settings` 的"标题"实为输出路径，未校验 | 解析为绝对路径并确认目录可写 |
| S-21 | `src/Ui/HotkeyManager.cs:91-103` | 热键消息回调无异常隔离，`OnHotkey` 抛错从 `WndProc` 逸出（待实测） | try/catch + `Log.Error` |
| G-17 | `src/Ui/GlassFeed.cs:559` | 每帧新建 `BitmapSource` 并整块拷贝（`P-2` 半修） | 复用 `WriteableBitmap` + `Lock/Unlock` |
| G-18 | `src/Ui/OverlayWindow.xaml.cs:284` 等 | 本区域 40 处裸 catch，关键 Win32 返回值集体不检查，`GetExStyle` 的 int 截断 | 统一 `GetExStyle` 返回 `nint`；返回值检查 |
| G-19 | `src/Ui/OverlayController.cs:831`、`GlassRuntime.cs:255` | 退出时可能打伪 WARN；隐藏后泵空转、每帧读环境变量 | 停机判定收口；环境变量缓存 |
| T-14 | `src/Ui/Contour/ContourExtractor.cs:512-518` | `ContourPath.Level/LevelIndex` 恒为 0 | 填真实层级或删字段 |
| T-15 | `src/Ui/Contour/ScalarGrid.cs:26-44` | 网格上限两份（512 vs 140），生产用后者 | 单一常量 |
| T-16 | `src/Ui/TextCanvas.cs:55,80-81` | 键帽码位区间注释 83 与实现 87 不一致 | 注释与常量同源 |
| T-17 | `src/Ui/CardDragAdorner.cs:60-79` | 拖动层硬编码颜色；每帧新建 Pen/Brush/Geometry | 用主题令牌并缓存 |
| T-18 | `src/Ui/Screenshots.cs:828-837` | PNG 非原子写；设置窗截图按整 DIP 截断 | 走 `AtomicFile`；按 DPI 取整 |
| T-19 | `src/Ui/Screenshots.cs:102-120` | 截图模式直接在运行中的 `Settings` 上改演示参数（待实测污染） | 深拷贝一份再改 |
| T-20 | `src/Ui/ParticleTextLayer.cs:154-157` | 粒子提前返回路径不回发 `Finished` | 统一收尾事件 |
| T-21 | `src/Ui/TextCanvas.cs:557,749-750` | 排版缓存上限 12，被光标前缀键反复驱逐（`P-1` 残余） | 光标键不计入缓存或单独缓存 |
| K-17 | `GifAnimator.cs:28` | "动图过大"提示被换肤覆盖；`LastSkipReason` 是 static，双卡串提示 | 改实例字段，状态行优先 Warning |
| K-18 | `GifAnimator.cs:144-150` | GIF 暂停后按播放从头重播 | `Play(bool restart=false)` 默认续播 |
| K-19 | `WaveOutPlayer.cs:273,286,407`；`VideoWindow.cs:67` 等 | winmm 返回码被忽略 + `catch {}`，Unprepare/Close/SetVolume 失败不可见 | 检查返回码并节流日志 |
| K-20 | `VideoWindow.cs:210-214` | `HWND_TOPMOST` 把视频窗抬到所有置顶窗口之上 | 用 owner 句柄作 `hWndInsertAfter` |
| K-21 | `CardMediaView.cs:805-811` | 跨不同缩放屏幕时子窗位置/圆角偏（待实测） | `TransformToDevice` 逆变换 + `DpiChanged` |
| K-22 | `FfmpegPlayer.cs:484` | `-re` 回退锁 1×，UI 仍显示用户设置倍速 | `SetSpeed` 返回 bool 并回滚显示 |
| K-29 | 16 处 `XamlReader.Parse` | 每次换肤重解析模板，颜色串错误时 `XamlParseException` 直崩 UI 线程 | 缓存模板 + `TryParse` 回退 |
| K-30 | `CardButton.cs:178` 等 | 未 Freeze 的 `SolidColorBrush`（含生产控件 `CardTextBox`/`CardList`） | 统一走 `CardPalette.Brush` |
| K-31 | `CardStatus.cs:111,267` | 90 ms 转圈定时器无停止出口 | `Unloaded` 停表 |
| K-32 | `CardTreeTimeline.cs:276-297` | `SelectedItem` setter 同步 `UpdateLayout()`，布局回调重入风险（待实测） | `BeginInvoke` 延后 |
| K-33 | `CliUi.cs:260,332-336` | 换肤丢失 per-block bold；构造对 `Style==null` 无防护 | 存 Bold；`settings?.Style ?? new` |
| K-34 | `CardFormControls.cs:166` 等 | `ToString(ValueFormat)` 无校验，非法格式抛 `FormatException` | 复用图表侧的安全格式化 |
| K-35 | `CardDataControls.cs:1084,947` | 表格排序混合类型抛 `ArgumentException`；列重建双 Refresh（待实测） | 类型不同按字符串比较 |
| K-36 | `CardChartControls.cs:353,507,701` | `Series/Values/Labels` 直接暴露内部 `List`，改列表不重绘 | 返回只读集合 |
| K-37 | `CardList.cs:166,148` | `ApplyStyle(style, cli)` 的 `cli` 从未被读 | 删参数或真正使用 |
| K-38 | `CardIcon.cs:39,42` | `Size` 不同步 `FontSize`，运行期改大小无效 | setter 内同步 + `InvalidateMeasure` |
| K-39 | `CardSegmentBar.cs:87-94` | `OnRender` 里改 `_segments`，渲染副作用改写公开属性 | 局部变量 + Arrange 更新 |
| K-40 | `CardTextStyle.cs:57-83` | 每次换肤为每个 TextBlock 新建 `DropShadowEffect` | 复用 Effect；跳过空子树 |
| K-41 | `HudMenuStyle.cs:51,76,83` | 每次 `Apply` 重复订阅，`LayoutUpdated` 永不解绑 | 附加属性防重入；`Opened` 挂 `Closed` 解 |
| K-42 | `CardOverlaySelfTest.cs:193` | 自检只验"不崩/尺寸"，不验颜色与对比度；清理只吞 `InvalidOperationException` | 补对比度断言；宽 catch 计入失败 |
| P-20 | `src/Cards/CardRegistry.cs:78-97` | 空 Kind 仍硬编码 `"infotext"`；零模块时每次调用新建占位模块 | 用 `UnavailableCardModule` 单例 |
| P-21 | `src/Cards/CardRegistry.cs:19-25` | 双检锁的 `_initialized` 不是 `volatile`（历史 `L-16`） | `volatile` 或 `Lazy<T>` |
| P-22 | 本区域 32 处空 catch | 其中 6 处吞掉了需要留痕的失败 | 见 §5.2 分类治理 |
| P-23 | `InfoTextCard.cs:105-109` | 用内容字符串相等反推预设，手改内容会静默换预设 | 记录显式 Preset，内容只做兜底 |
| P-24 | `MediaCardSettingsView.xaml.cs:220-265` | `DecoderHint` 是带副作用的绑定 getter，每次刷新同步 `File.Exists` 并可能启动探测 | 改为显式刷新方法 |
| P-25 | 四份设置视图 | `Raise/commit` 复制粘贴，只有 Chat 实现 `ICardEditorCommit` | 抽基类或共享 helper |
| P-26 | `CardPluginLoader.cs:356-362` | 只校验 `manifest.apiVersion`，不校验插件编译所依据的 Contracts 版本 | manifest 加 `contractsVersion` 并校验 |
| P-27 | `CardJobQueue.cs:1-6` | 位置与文档不符；`StationCopyController.cs:126` 注释与事实相反 | 修正文档/注释 |
| X-11 | 20 个文件 / 117 处 `DllImport` | `Native.cs` 的集中声明未被遵守；`GetWindowLongPtrW` 等在 `Native.cs` 与 `MediaNative.cs` 重复声明 | 单文件 P/Invoke + 可空性/句柄包装 |
| X-12 | `src/Ui/Controls/`、`src/Text/`、6 个 `plugin.csproj`、9 个 `RootNamespace` | 空目录残留；六个工程同名导致构建日志不可辨；所有程序集同根命名空间 | 清空目录；工程按卡片改名；`RootNamespace` 按程序集区分 |
| X-13 | `.gitignore` 第 34 行附近 | `docs/review/` 被忽略，审阅报告无法入库/评审/在 PR 引用 | 改为跟踪，或独立仓库 + 链接 |
| X-14 | `sign.ps1:27` | PFX 口令硬编码（`MirrorpondHUDDev!2026`），并向 CurrentUser 存储写入自签名证书 | 参数/环境变量；文档说明信任流程 |
| X-15 | `src/Contracts/MirrorpondHUD.Contracts.csproj:4` | `UseWPF=true`；契约里出现 `Color`（19 处）、`FrameworkElement`（16 处）、`Brush`/`Pen`/`Size`/`CornerRadius` | 拆出不依赖 WPF 的纯数据/纯逻辑契约 |
| X-16 | `src/App.xaml.cs:76-104` | 第二实例在 UI 线程 `Thread.Sleep(100)` × 30（最多 3 s）等唤醒事件 | 短超时 + 异步/后台等待，或先建事件再建互斥 |

---

## 5. 跨层结构性问题（不属于单点，但决定后续成本）

### 5.1 验证体系：26 条 P1 没有一个会被构建拦住

**事实**

- 仓库没有测试工程、没有 `.github/`、没有 CI。
- 唯一的自动验证是 `--selftest`（1,855 行 / 28 段），且：
  - 应用在运行时它会**静默返回 0**（X-1）；
  - 24 个领域结果被压成 0/1，失败数不准确（C-15）；
  - 读写真实配置、注入按键、注册真实热键、需要真实桌面（C-17）；
  - 纯计算区域（等高线 1,300 行）零断言（T-12）。
- 五种 `--screenshot*` 全部吞异常、退出码恒 0（T-2），视觉验收同样不可信。

**为什么这是结构性问题**

本次 159 条区域问题里，至少 40 条是"一个 20 行的单元测试就能永久挡住"的类型：`Json` 属性 DTO 往返、`SettingsLoader` 版本边界、`TemplateEngine` 极值、`ConfigStore` 只读态、`ContourExtractor` 网格边界、`WaveOutPlayer.Feed` 短读。它们今天只能靠人读代码发现，而人读 5.5 万行的成本远高于写 40 个测试。

`AGENTS §7` 用"自检"承接了测试职责，但自检的四个硬约束（不可隔离、不可并行、不可 CI、不可 mock）决定了它只能做**环境冒烟**，做不了回归。`contracts-core` 的结论是准确的：**自检承担了它不该承担的职责**。

**建议（按投入排序）**

1. 抽出 `tests/MirrorpondHUD.Tests`（`net8.0`，**不引用 WPF**）：覆盖 `Json`、`Settings`/`SettingsLoader`、`ThemeFile`、`TemplateEngine`、`Variables`、`ConfigStore`/`AtomicFile`、`Contour` 纯算法。这批代码本来就不依赖 WPF（除了 `Settings` 的可空性），迁移成本最低。
2. 给 `ConfigStore`/`Log`/`SecretStore`/`CardStateStore` 加可注入的路径/时钟接口（或 `internal` + `InternalsVisibleTo`），让测试用临时目录。
3. CI 三步：`dotnet build -c Release`（0 警告）→ `dotnet test` → `--selftest --isolated`（新增，见 C-17）；截图任务单独跑并断言产物存在。
4. `--selftest` 只保留"必须真实环境"的段（PDH、nvidia-smi、键盘钩子、热键、真实 DPI），其余迁进单元测试。

### 5.2 可观测性与"失败必须可见"

**事实**

| 指标 | 数值 |
| --- | --- |
| `catch { }`（完全空） | **138** 处 / 42 文件 |
| `catch (Exception …)` | 232 处 |
| `LogThrottle` 引用 | 9 处（定义 + 少量采样/玻璃） |
| 日志调用 | Debug 6 / Info 111 / Warn 276 / Error 62 |
| `SetLastError = true` 声明 | 27 处 |
| `Marshal.GetLastWin32Error()` 使用 | **10** 处 |
| `DllImport` 声明 | 117 处 / 20 文件 |

**三个具体后果**

1. **`catch` 兜不住 Win32 返回值。** `SetWindowDisplayAffinity`（G-4）、`waveOutUnprepareHeader`/`waveOutClose`（K-19）、`SHFileOperation` 之外的 Shell 调用、`GetExStyle`（G-18）都是"返回 FALSE/错误码但不抛异常"，`catch` 与 `try` 在这里是装饰。27 处 `SetLastError` 只配了 10 处错误读取，其余返回值无人看。
2. **`catch { }` 的分布证明规则本身有问题。** 138 处里最多的是 `OverlayController.Dispose` 的 13 处"关窗/停定时器/释放 COM"——这些地方吞异常是**正确**的；而 `TextCanvas` 的字体回退（T-3）、`FfmpegPlayer` 的 13 处、`WaveOutPlayer` 的 10 处才是危险的。`AGENTS §5` 用一条绝对禁令同时覆盖两类，结果是要么违规、要么写一堆噪音日志。
3. **日志本身是同步 IO。** `Log.Write` 在全局锁里 `File.AppendAllText` + 轮转 `File.Move`（C-23）。UI 线程上任何一次 `Warn` 都要等磁盘；日志量为 276 个 Warn 调用点时，这不是理论问题。崩溃路径反而没问题（无缓冲，逐条落盘），这是设计取舍的正确一面。

**建议**

- 增加 `SafeRelease(Action, string what)` 辅助：只允许它内部空 catch，并强制带 `what` 用于 Debug 日志。138 处收敛成"Dispose 用 `SafeRelease`，其它必须 Log"。
- 给 P/Invoke 立一条规则：**任何返回 `BOOL`/`HRESULT`/错误码的调用，要么检查，要么注释写明"此处失败可忽略"**；用 Roslyn analyzer 或 CI grep + allowlist 落地。
- `Log` 改为"内存队列 + 单写线程"，进程退出时 `Flush(2s)`；保留同步降级路径用于崩溃处理器。
- 热路径统一 `LogThrottle`（状态转移 + 恢复记录），把 276 个 Warn 收敛到有状态的十几条。

### 5.3 线程模型：代次纪律只覆盖了 1/4 的异步面

| 组件 | 代次/取消原语 | 结论 |
| --- | --- | --- |
| `FfmpegPlayer` | `_run` 16 处 + `ClaimCleanup` + `DetachJoin` | 做得好，可作为模板 |
| `WaveOutPlayer` | 0 处 | 靠 `_stop` 布尔 + 锁；K-2/K-3 是其后果 |
| `GlassFeed`/`GlassRuntime`/`DesktopFrameSource` | 0 处 | 只有 `_glassBusy`；G-3/G-5 是其后果 |
| `ContourDecoration` | `_inFlight` + 代次（ui-text 报告确认） | 基本正确 |
| 全仓 `CancellationToken` | 16 次 | 绝大多数异步工作用 `bool _stop` |

**UI 线程上的同步工作清单**（每条都有对应的 P1/P2）：

读盘/枚举：`Directory.Delete(...,true)`（P-2）、`File.Exists` × N（P-11）、图标提取（P-17）、背景图加载（G-7/G-8）、`FindFfmpeg` 扫 PATH（K-15）、图片/GIF 解码（K-12）。
原生采集：`BitBlt` 每轮 ~18 ms（G-1）、`EnumWindows` + 开进程句柄（G-6）、`PrintWindow`（G-5 在后台，但结果是 UI 线程等）。
计算：等高线 30 fps 全网格（T-4）、粒子 RTB + 上万元素（T-5）、逐帧 `BitmapSource.Create`（K-16）。
等待：`MetricEngine.Start` 的 `Thread.Sleep(250)`（P-12）、第二实例最多 3 s 重试（X-16）。

`AGENTS §6.3` 只写了"绝不在 UI 线程无界等线程"，但真正的负载不是 Join，而是**每帧同步原生调用**。这条规则需要改写为："采集、解码、递归文件操作、外部进程探测不得在 UI 线程执行；UI 线程只做合成与提交。"

另外：**20 个 `DispatcherTimer` 实例**里，至少有 3 个没有停止出口或随对象生命周期悬空（`CardStatus` K-31、`TrayController` 气泡、`G-13` 每卡置顶）。UI 线程上的定时器数量本身也是设计信号——它们应该收敛为 1–2 个调度器。

### 5.4 构建、发布与版本：只有"0 警告"是真的门禁

| 项 | 现状 | 风险 |
| --- | --- | --- |
| 构建 | `dotnet build -c Release` 实测 0/0 | 好，但**不是强制**：没有 `TreatWarningsAsErrors`，也没有 CI |
| 分析器 | 未启用 `AnalysisLevel`/`EnableNETAnalyzers` 显式配置（SDK 默认） | 大量 CA 规则未跑；`.editorconfig` 的 IDE 规则默认不进构建（`EnforceCodeStyleInBuild` 未开） |
| SDK 版本 | **无 `global.json`** | 换机器/升级 SDK 时行为漂移；`LangVersion=12.0` 只锁语法不锁 SDK |
| 版本号 | **15 处手工同步**：9 个 `csproj` + 6 个 `manifest.json`（另有 README / version-history） | 漏一处就版本不一致；`SelfTest.CheckBundledManifestVersions` 能兜底，但只在自检真跑时生效（X-1） |
| 发布 | `publish.bat` 先 `rd /s /q bin obj`，再把用户 `settings.json`/日志/插件复制到 `%TEMP%`，四次发布后重建并恢复 | 中途失败/断电时用户配置只在 TEMP；应用运行时删除静默失败（`2>nul`），后续 publish 可能混架构；备份未校验哈希 |
| 源码编码 | `RecycleBin.cs:58` 内嵌裸 NUL | **git 视该文件为二进制**（实测 `Bin 6131 -> 6132`），今后所有改动不进 diff、grep/file 跳过 |
| 签名 | `sign.ps1` 硬编码 PFX 口令 `MirrorpondHUDDev!2026` | `certs/` 已 gitignore，风险有限；但口令入库是坏习惯，且脚本会向 CurrentUser 证书存储写入自签名证书 |

**建议**

1. `Directory.Build.props` 加 `<Version>` 单一来源，`manifest.json` 由 MSBuild 生成（或 `CheckBundledManifestVersions` 扩展为构建目标）；`global.json` 固定 SDK feature band。
2. `TreatWarningsAsErrors` 仅在 CI 打开（本地保留可读性），并加 `EnforceCodeStyleInBuild=true` + `AnalysisLevel=latest`，先用 `<NoWarn>` 收掉存量，再逐批清。
3. `publish.bat` 改为独立输出目录（`-o publish\...` 本来就不会碰 `bin`），不要 `rd /s /q bin obj`；用户数据改为"启动时自动迁移到 `%LOCALAPPDATA%`"，不再随构建目录走。
4. 把 `RecycleBin.cs:58` 的裸 NUL 改成 `"\0"` 转义（行为不变），并在 CI 加一条"源文件不得含 NUL 字节"的检查。

### 5.5 主题系统：四套色源、两套规范化、一个默认值分叉

**现状**

| 层 | 载体 | 键/值数量 | 消费方 |
| --- | --- | --- | --- |
| 设置窗外壳 | `src/Ui/Theme.xaml` | 37 个 `SolidColorBrush`（暗色基线） | 设置窗 XAML（全部 `DynamicResource`） |
| 设置窗明暗 | `src/Ui/SettingsTheme.cs` | 33 对 `(Dark, Light)` 颜色 | 运行时替换 `Application.Resources` 的 brush 实例 |
| 托盘/卡片右键菜单 | `src/UiKit/HudMenuStyle.cs` | `Light`/`Dark` 两组元组（与 XAML 默认值逐字相同） | 每个 `ContextMenu` 自己的资源字典 |
| 卡片内容 | `StyleSettings` → `ThemeResolver.ThemeTokens` | 用户配置 / `.mptheme` | TextCanvas、UiKit 控件 |
| 主题文件 | `ThemeFile` + `ThemeDocument.ApplyTo` | 颜色/字体/形状/动效/工业参数 | 导入导出、内置 5 套 `.mptheme` |

**问题**

- **改一个颜色要改 2–3 处。** XAML 的 37 个 brush 与 `SettingsTheme.Palette` 的 33 个键必须手工镜像（当前一致，但没有断言守护）。`Menu*` 5 键由 `HudMenuStyle` 逐实例注入，所以不在 Palette 里——这是有意的，但"哪些键该在 Palette、哪些不该"没有任何文档或自检。
- **默认值已经分叉。** 卡片默认强调色 `#2F6FEB`（蓝，`Settings.cs:111/600`），设置窗外壳强调色 `#F2E900`（黄，`Theme.xaml:5`）。新装用户看到的卡片是蓝色强调、设置窗是信号黄。这可能是 2.44 品牌改造的遗留，但没有任何注释或文档说明。
- **`SettingsTheme.Apply` 替换的是 brush 实例，不是 `Color`。** 当前所有调色板引用都是 `{DynamicResource}`（实测 0 处 `StaticResource` 调色板引用），所以能工作；但任何未来新增的 `StaticResource` 都会在切主题后保持旧色——这是 WPF 常见陷阱，且与 `SettingsTheme` 的注释（"套用 StaticResource 的控件会立即跟着变"）**相反**。
- **`Settings.Normalize` 与 `ThemeDocument.ApplyTo` 是两套并行规范化**（`contracts-core` S5）：新增一个主题字段要同时改 5 个位置，`C-27` 的文案漂移就是这么来的。

**建议**：以 XAML 为唯一色源 → `SettingsTheme` 只改 `SolidColorBrush.Color`（并要求 brush 全部 `Freezable` 之外的动态引用）；`HudMenuStyle` 从同一字典取色；卡片 `ThemeResolver` 的默认值从同一张表生成；自检加"XAML 键集合 = Palette 键集合 ∪ 菜单键集合"的断言，并把默认强调色统一（或写清为什么不同）。

### 5.6 契约与 ABI：一个整数承担了 13 个可选接口的版本协商

- `manifest.apiVersion` 只有一个整数 `2`，但 `ICardModule` 之外还有 `ICardDefaultProvider`、`ICardModuleLifetime`、`ICardHotkeySource/Target`、`ICardDropTarget`、`ICardDropFeedback`、`ICardActivation`、`ICardScreenshotProvider`、`ICardPresetSource`、`ICardRuntimeSettingsMerge`、`ICardSelfTest`、`ICardSettingsModule`、`ICardEditorSection`、`ICardSettingsValidator`、`ICardSummary`、`ICardStyleAware` 等十余个可选接口，全靠 `is/as` 探测。任何**修改既有接口形状**的演进（而不是新增接口）对旧插件都是 `MissingMethodException`/`TypeLoadException`，`apiVersion=2` 表达不了"我按 2.44 的宿主编译"。
- `Json` 只认公共字段（C-7/P-7），而"DTO 必须是公共字段"是 `AGENTS §4.1` 的红线——两条规则互相强化：框架不提供编译期保障，作者只能靠文档。
- `Extras` 在内存里存插件类型实例（P-6），使 ALC 永远不可回收；卸载探针因此必然误报（P-5）。
- `Contracts.csproj` 是 `UseWPF=true`，契约里出现 `Color`（`ThemeTokens`）、`FrameworkElement`（`ICardHost` 等 16 处）、`Size`/`Brush`/`Pen`/`CornerRadius`。**契约层无法在非 WPF 宿主里引用，也无法在没有 STA 的测试里跑**——这直接解释了为什么"单元测试"只能写成 `--selftest`。
- 两个 `AssemblyInfo.cs` 都对 `"MirrorpondHUD"` 开 `InternalsVisibleTo`，把内部实现暴露给宿主；`UiKit` 的 `CliPanelView.BoundSettings` 就是靠它给自检用的。
- 9 个程序集全部 `RootNamespace=MirrorpondHUD`，同名命名空间跨程序集共存（目前无 `CS0433`，但类型名冲突会以编译错误形式突然出现）。

**建议**：manifest 增加 `apiRevision`（兼容小版本）；把纯数据/纯逻辑（Json、Settings DTO、ThemeFile、TemplateEngine）拆成**不依赖 WPF** 的 `Contracts.Core`，`MirrorpondHUD.Contracts` 只保留需要 WPF 的视图契约；维护一份 "public surface = plugin ABI" 清单；`InternalsVisibleTo` 收敛到一个明确的 `MirrorpondHUD.TestHost`。

### 5.7 死代码与"没有消费者"的代码

| 对象 | 规模 | 消费者 |
| --- | --- | --- |
| UiKit 的 overlay/data/chart/form/tree/icon 控件 | **5,027 行**（占 UiKit 44%） | 仅 `samples/UiKitGallery` 与 `--selftest`，插件引用 **0** |
| 4 个 UiKit 自检 | 608 行 | 仅 `src/SelfTest.cs` |
| `LucideIcons.Get/TryGet/Names` | 6,927 行文件的查询 API | 生产代码 **0 调用**（仅自检与注释） |
| `Keycaps.IsKeycap` | ~20 行 | **0 调用**，且与 `Icons` 的结论矛盾 |
| `src/Ui/Controls/`、`src/Text/` | 空目录 | — |

历史项 `M-2`（"UiKit 约 5 千行没有生产消费者"）在 2.26.0 就提出过，2.44.2 仍然成立且可以精确复现。这不是"写多了"的问题，而是：这些控件随宿主发布、参与 `apiVersion` 承诺、增加编译与维护面，却没有任何真实调用点；它们会持续吸收"顺手改一下"的成本，也会让新读者误以为它们是生产路径。

**建议**：二选一——(a) 拆成 `MirrorpondHUD.UiKit.Gallery`，主工程不引用；(b) 在文件头与文档标注 "Experimental，不保证 ABI"，并在 `UiKit.csproj` 里隔离。同时删除零调用的 Lucide 查询 API 与 `Keycaps` 死代码，清掉空目录。

### 5.8 仓库与文档治理

- **`docs/review/` 被 `.gitignore` 忽略**（`.gitignore` 第 34 行附近）。审阅报告无法入库、无法被他人 review、无法在 PR 里引用；本次交付落在该目录后 `git status` 为空。若希望审阅结论参与决策，应改为跟踪或放到独立仓库。
- **六个插件工程都叫 `plugin.csproj`**，构建输出日志里全是 `plugin -> ...`，无法从工程名判断是哪个卡；MSBuild 依据目录区分，但人读日志/Debug 时会歧义。
- `samples/ParticleToText`、`samples/ContourLine` 在 `.gitignore` 里但文件仍在磁盘（`AGENTS §11` 的意图是"独立项目独立提交"，目前是本地副本）。
- `sign.ps1` 的自签名证书会写入 `CurrentUser` 存储并（`TrustOnly` 分支）信任；`certs/` 已忽略，但硬编码口令仍建议改为参数/环境变量。
- 正面项：`.gitattributes` 的换行策略、`app.manifest` 的 `PerMonitorV2` + `longPathAware`、`Directory.Build.props` 固定 `LangVersion`、版本历史文档的详细程度，都高于同类项目。

---

## 6. `AGENTS.md` §2–§7 审计（用户特别授权）

> 审计立场：这些条款大多有真实的历史成因（P0/P1 编号可追溯），不是拍脑袋写的。问题不在"写错了"，而在**它们被当成不可质疑的规则，却没有配套的执行器**，于是最好的那部分落不了地，最严的那部分逼出了形式主义。

### 6.1 逐条对照

| 条款 | 声称 | 实际 | 判定 |
| --- | --- | --- | --- |
| §2 构建 0 警告 | "必须做到 0 警告 0 错误" | 实测成立；但无 `TreatWarningsAsErrors`、无 CI、无 `global.json` | **成立，但不是门禁** |
| §2 `--selftest` 退出码=失败数 | "必须为 0" | 应用在运行时静默返回 0（X-1）；24 领域压成 0/1（C-15） | **不可信的信号** |
| §2 `--screenshot*` UI 自查 | "无头截图，用于 UI 自查" | 五种模式全吞异常、退出码恒 0（T-2） | **不可信的验收** |
| §3 多程序集边界 | `<Compile Remove>` 划界、契约不反向引用 | 成立；但 6 个同名 `plugin.csproj`、9 个同 `RootNamespace`、版本 15 处 | **可用，易错** |
| §3 插件同一条 loader 路径 | 内置卡与用户插件走同一路径 | 成立；但加载失败 UI 无出口（P-9） | **成立，可见性缺一半** |
| §4 现代 C# + Nullable 关闭 | 新代码现代风格、可空性自行处理 | 风格部分落地；可空缺口集中在契约层（S4），且 `#nullable` 被禁止局部试点 | **合理折中，但代价集中** |
| §4.1 DTO 必须是公共字段 | "改形状会静默丢配置" | 描述了事实，但框架没有编译期/加载期保障（C-7/P-7） | **准确，但只是警告** |
| §5 失败必须可见 | "不允许 `catch { }`" | 仍有 138 处；Win32 `BOOL` 返回值无人检查（G-4/G-18/K-19） | **未执行** |
| §5 长周期循环用 `LogThrottle` | 状态转移 + 最小间隔 | 全仓 9 处引用；采样/玻璃关键路径仍有不节流（P-13、G-10） | **部分执行** |
| §6.1 保存语义 | 临时文件→Replace→回退；备份失败中止 | `AtomicFile` 实现正确（我逐行核对）；但错误路径自身会抛（C-2） | **实现好，边界破** |
| §6.2 设置树引用纪律 | 只能写宿主当前树 | 宿主没有强制 API，靠插件自觉；`ExtrasAs` 读即写（C-6/P-6） | **靠自觉** |
| §6.3 代次计数 | "任何异步回调用代次 + `_stop`" | `FfmpegPlayer` 做到；玻璃/WaveOut **0 处**（G-3/G-5/K-2） | **覆盖 1/4** |
| §6.4 插件生命周期 | Unload 只发请求、弱引用延后验证 | 结构在；但探针必然误报 6/6（P-5）、`ICardModuleLifetime` 零实现（P-8） | **形似神不似** |
| §6.5 主题文件校验 | 7 位 `#RRGGBB` 严格 | 成立，自检覆盖；但主题字段要改 5 处（S5） | **成立** |
| §6.6 App 生命周期 | 白名单 + 5 次封顶，否则退出 | 白名单在；但 `args.Handled=false; Shutdown(1)` 自相矛盾（X-2） | **意图对，实现反** |
| §7 自检替代测试 | 各段独立 try、失败数=退出码 | 1855 行 28 段；但不可隔离/CI/mock，纯计算零断言（S3/T-12） | **职责过载** |

### 6.2 三个反模式

**反模式一：绝对禁令 vs 正确例外。**

`§5` 禁止一切 `catch { }`，但 `Dispose`/窗口关闭/COM 释放恰恰需要它（`OverlayController.Dispose` 13 处）。结果是：要么违反规则，要么为每处写一条无意义的日志。**规则越绝对，执行越形式化。** 建议改成三类清单（见 §5.2），并用 `SafeRelease` 提供"合法的空 catch"。

**反模式二：用"人工必须做"代替门禁。**

"必须 0 警告""必须跑自检""提交前必须自检"都是人工纪律。在没有 CI、没有测试工程、自检还会假通过的前提下，这些条款的约束力等于"读到 AGENTS 的人的自觉"。**建议**：凡是"必须"，就落到 CI；落不到 CI 的，明确写"依赖人工，不可作为合并条件"。

**反模式三：把workaround写进规则，然后忘记它的前提。**

- `§6.3` 的手工代次是 `async`/`Task` 时代之前的写法；`FfmpegPlayer` 用得对，但玻璃链路根本没写。真正的现代替代是 `CancellationToken` + 请求对象（`CaptureRequest(gen, rect, params)`）。
- `§4.1` 的"DTO 必须公共字段"是为了迁就自研 `Json`；正确做法是让 `Json` 支持属性（或至少报警），而不是把限制升格为永久红线。
- `§2` 的"零第三方依赖"本身没问题（`ArgumentList` 防注入、`AtomicFile` 手工实现都做得好），但硬编码 vtable 下标是**零依赖的错误用法**——`[ComImport]` 接口声明同样零依赖，且不用按 build 号门控（G-1）。

### 6.3 建议改写（保留意图，换掉执行方式）

1. **§2** 改为："`Release` 构建 0 警告（CI 强制）；`--selftest` 退出 0 且日志含 `失败项: 0`；`--screenshot*` 产物必须存在。立即模式在已有实例运行时不得静默返回 0。"
2. **§3** 增加："新增目录必须显式声明归属；程序集名不得重复；版本号单一来源；契约层不得依赖 WPF（UI 契约除外）。"
3. **§4** 改为："新文件默认启用 Nullable；存量按程序集分批迁移；DTO 允许属性，`Json` 必须支持或警告。"
4. **§5** 改为三类："Dispose 用 `SafeRelease`；用户可操作失败必须进 UI；采样/渲染失败必须 `LogThrottle`。Win32 返回值必须检查或注释豁免。" 并加 CI 检查。
5. **§6** 每条隐性约束配一个断言或自检；"靠自觉"的条款要么加 API 强制（如 `ICardHost.SaveCurrentTree()` 校验引用相等），要么降级为"建议"。
6. **§7** 改为："纯逻辑必须有单元测试；`--selftest` 只覆盖环境与互操作；CI 跑测试，不跑桌面自检。"

### 6.4 补充实测：语法现代化其实已经完成，"没现代化"的是另外几层

为回答"未现代化代码怎么办"，对当前工作树做了全量语法统计（153 个 `.cs`，排除 `obj/bin`）：

| 指标 | 结果 | 判读 |
| --- | --- | --- |
| `file-scoped namespace` | **151 / 153**（另 2 个是无命名空间的 `AssemblyInfo.cs`） | 已全仓完成 |
| 块式 `namespace x {` | **0** | 已清零 |
| `this.` 显式引用 | **0** | 已清零 |
| `get { return … }` 块体属性 | **0** | 已清零 |
| `record` / `#nullable` / switch 表达式 / `using var` | **0 / 0 / 0 / 0** | 未采用或刻意禁用 |
| `out var` vs `out 显式类型` | **255 / 43** | 以现代写法为主 |
| `is null` / `is not null` vs `== null` | **1,338 / 1** | 以现代写法为主 |
| `var` 局部变量 | 743 | — |
| `string.Format` | **3** | 基本被插值取代 |
| `dotnet format --verify-no-changes` | **退出 0** | 格式基线已闭合（2.34 执行评估中的 P2 已解决） |
| 2.34 机械语法批 | **139 文件 / +42,013 / −43,253 行** | 全仓级重写已经发生过一次 |

结论：**"未现代化的语法"在当前的 MirrorpondHUD 里几乎不存在**。2.34.0 已经做过一次全仓重写，2.35–2.44 新增的文件（粒子、终末地、设置窗分片）也沿用了同一套写法。真正还没"现代化"的是以下四层，且都不是格式化能解决的：

1. **可空性**：`Nullable=disable`，0 处 `#nullable`。这是政策选择，不是风格遗漏；它有一个明确的缺陷代价（C-6/C-8/C-14 的根因都是"可空返回靠注释"）。
2. **异步/取消模型**：11 处 `async void`、16 处 `CancellationToken`，大量 `bool _stop` + 手工代次；玻璃/WaveOut 是 0 处代次（G-3/G-5/K-2）。这是并发范式，不是语法。
3. **序列化与 ABI**：DTO 公共字段 + `new()` 反射、P/Invoke 布局、XAML 绑定名——这些"看起来老"的形状是**契约**，`AGENTS §4.1` 已把它们列为红线，正确做法是文档化而不是重写。
4. **WPF 控件 API**：0 个 `DependencyProperty`、0 个 `AutomationPeer`、手工 `ApplyStyle`（K-25/K-26）。这是控件库设计，不是 C# 风格。

因此不建议再做第二轮"现代化"：2.34 的 4.2 万行重写已经证明它不产生缺陷收益，却会让 `git blame`、评审与 `.git-blame-ignore-revs` 付出成本。真正值得投入的是：`dotnet format --verify-no-changes` 接进 CI（现在就能过，成本近零）、在纯逻辑子集启用 Nullable、在玻璃/WaveOut 重写时用 `CancellationToken` + 请求对象替代 `bool _stop`，以及把红线写成一份 public-surface/ABI 清单。

一个反直觉但值得记录的观察：本次审阅中缺陷密度最高的区域是 2.40 之后新写的设置窗、粒子与终末地主题，而最"老"、最不现代的 `AtomicFile`、`ConfigStore`、`FfmpegPlayer` 恰恰是质量最高的代码。**在这个仓库里，"现代化程度"与"缺陷密度"没有正相关，甚至略负相关。** 风格不是质量杠杆；测试、门禁、失败可见与并发原语才是。

---

## 7. 修复路线图

> 原则：先修"用户每天能感知 + 改动面小"的，再修"并发/状态"根因，最后动结构。每批结束都必须能通过 §7.4 的验证。

### 7.1 第一批：热修（0.5–1 人日，10 项）

| 项 | 改动 | 验证 |
| --- | --- | --- |
| S-01 | `NIN_SELECT = WM_USER + 0`，补 v3 的 `WM_LBUTTONUP` 兜底 | 点托盘左键打开设置；右键菜单仍正常 |
| P-1 | 加 `FOF_WANTNUKEWARNING` | 对网络盘/超配额目录删除，确认出现系统警告 |
| C-1 | `TryReadVersion` 改用 `FindKey`；`schemaversion` 变体按 corrupted | 新单测：`{"schemaversion":999}` → 只读/不落盘 |
| C-2 | 按命中后缀长度裁剪；`SaveSettings` 包 try | 单测：`"\n"`、`"\r"`、`"x"`、`""` 均不抛 |
| C-3 | 只读态禁用保存或提供"另存为" | 手工把 settings.json 设只读，改设置后看到明确提示 |
| C-4 | `Math.Clamp` 对齐上限 1024 | 单测：`int.MinValue`/`int.MaxValue` 模板不抛 |
| K-3 | `Feed` 用 `ReadFull`，仅 EOF 尾块补零 | 长音频听不到周期爆音；A/V 不漂移 |
| T-2 | `Capture*` 返回 bool 并映射退出码 | 缺产物时退出码非 0 |
| X-1 | 立即模式先于单实例互斥；带立即模式时第二实例返回非 0 | 应用运行时跑 `--selftest`，退出码非 0 且日志写明"请先退出" |
| P-4 | `PdhQuery` 失败态；`StartEngine` try | 禁用 Performance Counter 服务后启动，HUD 仍起来、指标卡显示不可用 |

### 7.2 第二批：正确性与并发（3–5 人日，12 项）

| 项 | 改动 | 验证 |
| --- | --- | --- |
| P-2、P-3 | 删源/SafeSize 移入工作线程；清空/移除先取消在途任务；回填校验条目仍在列表 | 大目录 Move 时 UI 不冻；中途清空后 `items.json` 与接收目录一致 |
| G-1、G-2 | 能力探测替代 build 门控；`_frameSourceTried` 可复位（绑定配置指纹） | Win10/24H2 上选"桌面取样"走 DXGI；改设置后不重启即生效 |
| G-3、G-5 | 单一采集线程 + 请求代次 + 看门狗超时 | RDP + 2 张卡连续 10 分钟，玻璃不随机坏、无卡死 |
| G-4 | 检查 `SetWindowDisplayAffinity` 返回值，失败降级并记日志 | Win10 2004 以下不出现自拍拖影 |
| S-02、S-04 | 一次操作只落盘一次；保存后不重建；`ApplyResult` 结构化返回 | 保存日志只出现一次"设置已保存"；`CreateEditor` 一次 |
| S-03 | 仅 `Join` 成功才 `Dispose`；超时移交收尾权 | 退出 100 次无 `ObjectDisposedException` |
| K-1、K-2 | `pad=W:H` 强制尺寸；EOF 先 `waveOutReset` 并检查返回码 | 21:9 视频不歪斜；非循环视频播完不崩 |
| X-2 | `Handled=true` + `Shutdown(1)`（或 `Environment.Exit(1)`） | 注入一个不可恢复 UI 异常，退出码为 1、不出现 WER |
| C-5 | 检查自动落盘返回值；启动清理 `.broken-*` | 磁盘满时启动提示"恢复失败"；残留不超过 3 个 |
| P-5、P-6 | 退出前断开控制器链；`Extras` 语义二选一并写进指南 | 卸载日志不再误报 6/6（或明确说明"预期不回收"） |

### 7.3 第三批：结构性（1–2 周，8 项）

1. **测试工程 + CI**（§5.1）：先覆盖 6 个纯逻辑模块，再接 CI 三步。
2. **日志异步化 + 失败分类**（§5.2）：`SafeRelease` + 返回值检查规则 + `LogThrottle` 统一。
3. **主题单一源**（§5.5）：XAML → Palette → 菜单 → 卡片 token 同源，自检断言键集合。
4. **版本单一源 + 发布整改**（§5.4）：`Directory.Build.props` 取版本、manifest 生成、`publish.bat` 不再清 `bin`。
5. **`Json` 支持属性 + 未知键审计**（C-7/P-7/C-8），并评估把纯逻辑拆出 WPF 契约。
6. **UiKit 画廊拆分/标记**（§5.7）与零调用 API 清理。
7. **空 catch 治理**（§5.2）：138 → 只剩 `SafeRelease` 的合法空 catch，并加 CI 检查。
8. **改写 `AGENTS.md` §2–§7**（§6.3），把"人工必须"换成"CI 必须"。

### 7.4 每批的验收口径

```bat
dotnet build -c Release -v:m                 :: 0 警告 0 错误
bin\Release\MirrorpondHUD.exe --selftest     :: 退出码 0，日志含 "失败项: 0"
:: 立即模式回归：应用运行中再跑一次，必须退出码非 0
bin\Release\MirrorpondHUD.exe --selftest     :: 期望非 0（说明不再假通过）
```

外加：第一批每条都要有对应的手工复现步骤（见上表）；第二批必须补至少一条自检或单元测试；第三批完成后，CI 上 `dotnet test` 与 `--selftest --isolated` 都必须是绿的。

---

## 8. 附录

### A. 历史项状态总表（`review-2.26.0.md` 复核，只列与本次结论相关的项）

| 编号 | 结论 | 证据 |
| --- | --- | --- |
| L-1 保存清空主题名 | **已修** | `SettingsWindow.Theme.cs:52` 的 `_readingUi` 守卫 |
| L-2 副屏玻璃取样/GDI 回退 | **部分未修** | G-10（每 5 s 重建 + 刷日志；补帧仍按快速后端） |
| L-3 共享 DIB 重建后帧缓存不失效 | **已修** | `GlassFeed.cs:786,803` `InvalidateDestination` |
| L-4 ffmpeg 路径下四个设置被忽略 | **修了但可能被抵消** | K-11（`previous` 与 `_settings` 同一实例） |
| L-5 第二实例冷启动竞态 | **未修** | G-12 |
| L-6 卸载探针结论不可信 | **未修且更糟** | P-5（现在稳定报 6/6） |
| L-7 保存链路无兜底 | **已修（UI 层）** | `Save.cs:103-115` + `SettingsApplier` 回滚 |
| L-8 AutoStart 静默回滚 | **已修** | `MergeRuntimeBool` |
| L-9 Load 把读失败当损坏 | **已修** | `ConfigStore.cs:94-105` `readIoFailed` 分支 |
| L-10 异常配额终身预算 | **部分未修** | `_swallowedUiExceptions` 仍是终身；`Handled=false` 见 X-2 |
| L-11 WaveOut.Start 失败仍返回成功 | **部分修** | K-4（1–3 槽失败仍 true） |
| L-12 光标定时器无卸载出口 | **已修** | `TextCanvas.cs:104-121` |
| L-16 双检锁缺 volatile | **未修** | P-21 |
| L-17 Join 超时后释放 `_ready` | **未修** | S-03 |
| L-18 配置自动修复落盘被忽略 | **未修** | C-5 |
| L-19 `CardStateStore.Delete` 绕过写锁 | **已修** | `CardStateStore.cs:125` |
| L-20 背景图失败永久缓存 | **未修** | G-7 |
| L-21 设置窗 Hide 不释放 | **未修** | S-14 |
| L-22 二次落盘 + 整树重建 | **未修且长出新缺陷** | S-02 / S-04 / S-13 |
| P-1 TextCanvas 每帧排版两遍 | **已修，残余缓存驱逐** | T-21 |
| P-2 GlassFeed 每帧 byte[] | **半修** | G-17（`byte[]` 复用，`BitmapSource` 仍每帧新建） |
| P-3 兼容取样每帧枚举窗口 | **未修** | G-6 |
| P-4 GIF 每帧常驻 | **已修预算** | K-17（提示仍不稳） |
| P-5 每卡置顶定时器 | **未修** | G-13 |
| P-6 MetricEngine UI 线程 Sleep | **未修** | P-12 |
| P-7 设置页每键重建 | **部分未修** | S-04 / S-10 |
| P-8 媒体状态行重建 Inlines | **已修** | UiKit 报告确认 |
| P-9 视频子窗拖动重建 region | **未修** | K-9 |
| P-11 Json 反射无字段缓存 | **未修** | C-7/P-7 同文件；`GetFields` 每调用一次 |
| O-1 日志冷却窗口丢恢复记录 | **未修** | C-9 |
| S-1 空 catch（152 处） | **未修（138 处）** | §5.2 |
| S-3 双实例写同一日志 | **未修** | C-9 |
| S-13 壁纸缓存 | **未修** | G-8 |
| M-1 宿主硬编码卡片 id | **部分修** | 默认卡改 `ICardDefaultProvider`；`CardRegistry`/`OverlaySurface` 仍有硬编码 |
| M-2 UiKit 无生产消费者 | **仍成立** | 5,027 行 + 608 行，插件引用 0 |
| M-3 Extras 钉住 ALC | **未修** | P-6 |
| B-1 中转站根路径逃逸 | **已修（主体）** | `StationFileActions.cs:270-339` 等 |
| B-2 首个收纳任务同步执行 | **已修** | `CardJobQueue.cs:118-121` |
| B-3 CommandView 释放别人的 CTS | **已修** | `CommandCard.cs:307-368` |
| B-4 / B-7 / B-9 / B-12 | **B-4、B-9、B-12 未修；B-7 已修** | P-7 / P-10 / P-27；`StationStore.cs:55-66` |
| B-8 Discard 跳过 lifetime | **未修** | P-8 |
| B-13 RefreshDecoders async void | **已修** | 外壳 + Core 拆分 |
| B-14 Load 逐条同步 Exists | **未修** | P-11 |
| B-16 清空与在途复制竞态 | **半修** | P-3（只修了"视图销毁"分支） |

### B. 可复跑命令

```bat
:: 构建（0 警告验收）
dotnet build -c Release -v:m

:: 依赖核实（应为空）
dotnet list MirrorpondHUD.csproj package --include-transitive

:: 自检（注意：应用运行时会被静默跳过，见 X-1）
bin\Release\MirrorpondHUD.exe --selftest
```

```bash
# 纪律统计（Git Bash / MSYS）
grep -rn "catch\s*{\s*}" --include=*.cs src | wc -l          # 138
grep -rn "async void"    --include=*.cs src | wc -l          # 11
grep -rn "new DispatcherTimer" --include=*.cs src | wc -l    # 20
grep -rn "CancellationToken"   --include=*.cs src | wc -l    # 16
grep -rn "LogThrottle"         --include=*.cs src | wc -l    # 9

# 裸 NUL 导致的"二进制源文件"（应为空输出）
git diff --no-index --stat /dev/null src/Plugins/DropStation/RecycleBin.cs
# 期望：Bin 6131 -> 6132 bytes（即 git 把它当二进制）
```

### C. 覆盖范围与限制

- **逐行/分段读完**：`src/Contracts`、`src/Core`、`SelfTest.cs`、`src/Ui` 的 15 个运行时文件与 29 个设置面文件、`src/UiKit` 35/35、`src/Plugins` 29/29。
- **只做了针对性检查**：`LucideIcons.cs` 的 2,293 个码位（只审初始化/查找/内存）、`ThemeFile.cs` 的逐字段 Range 校验（抽读 20 余处）、XAML 1,360 行的绑定细节。
- **未覆盖**：`samples/**`（独立工程）、`tools/*.py`、`bin/obj`、`.proto`、`.superpowers`。
- **未实测**：Win10 1909/2004、RDP、混合 DPI、ARM64、多 GPU、真实 ffmpeg/waveOut；相关结论均标"待实测/高度可能"。
- **未运行**：`--selftest`、五种 `--screenshot*`（原因见 §1.3）。

### D. 区域原始报告

六份区域报告保留了每条问题的完整代码证据、修复片段与置信度标注，位于 `docs/review/.work/`（该目录被 `.gitignore` 忽略）：

`contracts-core.md`（29）· `ui-settings.md`（21）· `ui-glass.md`（19）· `ui-text.md`（21）· `uikit.md`（42）· `plugins.md`（27）。

---

## 结语

这个项目最可贵的地方，是它已经**知道自己哪里容易坏**：`AtomicFile` 的备份语义、`ConfigStore` 对 I/O 失败与内容损坏的区分、`FfmpegPlayer` 的代次收尾、版本历史里逐条记录的"风险 / 未验证"——这些都是真实工程判断，不是模板套话。

它现在缺的不是更多规则，而是**让规则会失败的机制**：一条 CI、一个测试工程、一次退出码、一个分析器。把 `AGENTS.md` 里 10% 的"必须"变成构建会拦住的东西，剩下 90% 的注释才有意义。

P1 清单里超过一半的修复量在半天以内。先做那 12 件（§0.3），这个应用的"手感"会立刻不一样。

