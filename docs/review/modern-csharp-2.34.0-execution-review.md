# MirrorpondHUD 2.34.0 现代 C# 改造执行评估

> 评估对象：`feature/lucide-icons` @ `8d2b91e`（`6a3465d` 治理 A + `8d2b91e` 机械语法 B）
> 评估时间：2026-11-11 14:25–14:40
> 评估方式：提交审计 + 全量重建 + 既有 `--selftest` 日志核对 + `dotnet format --verify-no-changes` + 红线静态核验
> **总评：执行质量良好。A 完成、B 主体达成，公共契约 / 互操作 / XAML / 插件 ABI 四条红线全部守住；运行时风险低。剩余问题集中在格式基线、测试断言范围和文档与事实不一致，属于收尾级，不需要回滚。**
>
> **复核修复（`fca21f2`）**：P1 manifest 断言收窄到 `builtin.*` 并补用户插件正反例；P2 format style/whitespace 两项 verify 均为 0、`TrayIcon` IDE0090 已修、`out var` 实际落地 172 处；P3 补 `.git-blame-ignore-revs`、`LucideIcons.cs` generated_code、插件指南示例与示例 LangVersion 清理。剩余未处理项：`CardModule` 的 null 契约（复核报告明确为 Nullable 前置项，非本次回归）。

---

## 1. 改了什么

| 提交 | 内容 | 规模 |
| --- | --- | --- |
| `6a3465d` 治理 A | 新增 `Directory.Build.props`（`LangVersion=12.0` 等）、`.editorconfig`、`.gitattributes`；各 csproj 删重复属性；AGENTS §4 改为“现代 C# 默认 + 红线”；6 个 manifest 版本升至 2.34.0 | 19 文件，+121 / −136 |
| `8d2b91e` 机械语法 B | 全仓 file-scoped namespace、表达式体、target `new()`、`is null`、字符串插值；附 manifest 版本断言、version-history 记录 | 143 文件，+42,013 / −43,253 |

工作树另有与本次改造无关的本地项：`M .gitignore`、`?? docs/UI/liquid_glass.md`、`?? samples/ContourLine/`。

---

## 2. 验证结果（全部通过）

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 全量重建 `dotnet build -c Release -t:Rebuild` | **0 警告 / 0 错误**，6.0 s | 本次实测 |
| `--selftest` | **失败项: 0**；6 个内置插件自检通过 / 0 失败 | `bin/Release/mirrorpond.log` 14:20:39、14:23:13 两轮（B 提交于 14:21:54，14:23 那轮在 B 之后） |
| 插件加载 | 6/6 成功，版本 2.34.0 | 日志 `14:23:03` 加载完成行 |
| 配置未被破坏 | `settings.json` 5,340 字节、`SchemaVersion: 7`、字段完整 | 文件实读；未变成 `{}` |
| 版本一致性 | 9 个产品 csproj + 6 个 manifest 均 2.34.0 | 全量检索 |

---

## 3. 红线核验（全部未触碰）

| 红线 | 结果 | 证据 |
| --- | --- | --- |
| 序列化 DTO 字段 | ✅ 仍为 public field | `Settings` 105、`MetricsSnapshot` 27、`ThemeTokens` 10、`PluginManifest` 7、`AsyncProcess` 6、插件设置 DTO 7/7/12 |
| `Json` 反射协议 | ✅ `GetFields` / `SetValue` / `where T:class,new()` 未改 | `Json.cs` 仅 `is null`、插值、表达式体；`git diff -w` 无逻辑变化 |
| P/Invoke 布局 | ✅ 无字段/布局改动 | `DllImport` 115、`StructLayout` 29、`FieldOffset` 2；互操作文件的 `-w` diff 无结构体成员行 |
| XAML 绑定面 | ✅ `.xaml` 未改，事件方法名未改 | 仅 code-behind 表达式体化 |
| catch / 释放语义 | ✅ 152 个空 catch 保持不变；`using var` 0 处 | catch 块数量与改造前一致；`git diff -w` 中 catch 仅日志消息插值改写 |
| 插件 ABI | ✅ Contracts 公共形状无字段→属性变化 | 仅表达式体/target new；6 插件加载 0 失败 |
| Nullable / ImplicitUsings | ✅ 仍 disable；`#nullable` 0 处 | `Directory.Build.props` |

---

## 4. 改造完成度

| 项 | 改造前 | 现在 | 判断 |
| --- | --- | --- | --- |
| block namespace | 136 文件 | **0**（file-scoped 136） | ✅ 完成 |
| 显式 `get { }` | 439 处 | **6**（均为 `lock` 包体，无法表达式体化） | ✅ 合理保留 |
| 表达式体成员 | 0 | 约 394 | ✅ 主体完成 |
| 字符串插值 | 0 | 668 处 | ✅ 安全插值 |
| `is null` / `is not null` | 0 | 1,119 处；残留 `== null` 仅 1 | ✅ 完成 |
| 目标类型 `new()` | 0 | 109 处 | ✅ 按“类型明显”原则完成 |
| `out var` | 0 | **0** | ⚠️ 未执行（见 P2-2） |
| `using var` | 0 | **0** | ✅ 红线遵守 |
| `record` / `init` / `nullable` | 0 | **0** | ✅ 红线遵守 |

---

## 5. 发现的问题

### P1：manifest 版本断言会把用户插件算进去（会造成自检误失败）

`SelfTest.cs:1247-1275` 的 `CheckBundledManifestVersions()`：

```csharp
string root = Path.Combine(AppContext.BaseDirectory, "plugins", "cards");
string[] files = Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories);
if (files.Length < 6) return false;
// 然后要求每个 manifest.version == 宿主版本
```

问题：`plugins/cards` 正是**便携目录用户插件**的安装位置（指南 `DLL-CARD-PLUGIN-GUIDE.md:168-172`：`<exe>/plugins/cards/<id>/`）。只要用户安装一个 `version` 不等于宿主版本（例如 `1.0.0`）的第三方卡，`--selftest` 就会报“插件 manifest 版本不一致”而失败。当前机器只有 6 个 builtin，所以日志通过；这是环境依赖的假失败。

**修法**：只检查 `builtin.*` 目录（或按 6 个固定 id 白名单），用户插件不参与版本一致性检查；并补一个“伪造 `plugins/cards/user.card/1.0.0` 不影响 builtin 检查”的断言。

### P2：格式基线未闭合，`dotnet format` 当前不通过

1. `dotnet format whitespace --verify-no-changes`：**3,360 条**（3,315 `ENDOFLINE` + 45 `WHITESPACE`），涉及 **18 个文件**。主要集中：`GlassFeed.cs` 1,011、`Contour/ContourExtractor.cs` 703、`Contour/ContourGeometryBuilder.cs` 322、`Contour/ContourDecoration.cs` 280、`ScreenGeometry.cs` 228、其余 Contour/Overlay 文件。
2. 工作树行尾分布：`src` 下 138 个 CRLF、**11 个 LF、8 个 mixed**；`.gitattributes` 要求 `*.cs eol=crlf`，索引侧已正常（全 LF），但工作树未规范化。这是 A 加了属性却没有对存量做一次 renormalize 的典型残留。
3. `dotnet format style --verify-no-changes`：**1 条** `IDE0090`，位于 `src/Ui/TrayIcon.cs:291`（`new NotifyIconData` 可简化）。

**影响**：现在谁跑一次 `dotnet format`，都会再产生一批行尾/空白 diff；如果以后加 CI 的 format 门，会直接红。**修法**：把上述 18 个文件做一次行尾/空白规范化并单独提交（因索引已 LF，实际内容 diff 很小），修掉 TrayIcon 的 IDE0090；之后两条 verify 必须为 0。

### P2：`out var` 未执行，但提交说明和版本记录声称已做

全仓 `out var` 实际 **0 处**；`8d2b91e` 提交说明列了 `out var`，`docs/version-history.md:89` 与 `:3163` 也写了。当前代码里 138 个 `out` 参数仍是显式类型。

**处理**：二选一——补一个小批（138 处，机械、编译器可验证），或把提交说明/版本记录改成“`out var` 暂不采用（保留显式类型）”。不能保持“记录说做了、代码没做”。

### P2：`LucideIcons.cs` 被整文件重排（6,912 行）

该文件是 2,293 个图标码位常量的静态表，B 为它去掉命名空间缩进，产生 **6,912 / 6,914 行** diff（`git diff -w` 仅 7/9 行是真实内容变化）。评估报告里明确把它列为“最不值得做”。不影响运行，但制造了无意义的 blame/diff 噪声。

**修法**：保留现状也可以，但建议在 `.editorconfig` 给该文件加 `generated_code = true`，避免以后 `dotnet format` 再碰它；并把它纳入 blame 忽略。

### P3：缺少 `.git-blame-ignore-revs`

B 是 143 文件、4.2 万行改动，绝大多数是缩进/namespace 重排。没有 `.git-blame-ignore-revs`，`git blame` 会被整体刷掉。建议新增该文件写入 B 的提交哈希，并在 README/AGENTS 里说明 `git config blame.ignoreRevsFile .git-blame-ignore-revs`。

### P3：评估报告被本地 `.gitignore` 忽略，但 B 的提交信息引用了它

`8d2b91e` 的提交说明写“按 docs/review/modern-csharp-assessment.md 执行 B”，但 `docs/review/` 只被**工作树里未提交的 `.gitignore` 修改**忽略，其他机器/未来 clone 看不到这份依据。建议：把执行结论并入 `docs/version-history.md`（已有 2.34.0 条目），或提交一份精简的执行说明；不要把提交说明建立在本地不可见文件上。

### P3：文档/示例与新风格不一致

- `DLL-CARD-PLUGIN-GUIDE.md:194-197、257-260` 的示例仍是 `public override string Kind { get { return ...; } }` 旧写法；AGENTS §4 已改为现代默认，示例应同步（否则插件作者复制到的仍是旧风格）。
- `samples/ContourLine/ContourLine.csproj`（未跟踪的新示例）仍显式写 `<LangVersion>latest</LangVersion>`；提交时应删掉，交给 `Directory.Build.props` 统一为 12。

### P3：既有空语义问题未处理（非本次回归）

`CardModule.cs:128-129` 的 `Keys = keys; Config = config;` 仍无兜底，而 `ChatCard`/`InfoTextCard` 按“可能为 null”防御。B 触碰了该文件但未改。建议在将来启用 Nullable 前先统一契约，不要用 `!` 掩盖。

---

## 6. 风险判断

| 维度 | 等级 | 说明 |
| --- | --- | --- |
| 运行时行为 | **低** | 构建 0/0、自检失败项 0、6 插件通过、settings.json 完整；DTO/互操作/XAML/ABI 红线全部守住 |
| 用户插件场景 | **中** | manifest 版本断言范围过宽，便携目录用户插件会让自检误失败（P1） |
| 格式/工具链 | **中** | `dotnet format` 两项 verify 当前不通过；行尾未规范化，后续会持续产生噪声 diff（P2） |
| 变更可追溯性 | **中低** | 4.2 万行重排 + 无 blame-ignore；提交引用本地不可见报告；`out var` 记录不实（P2/P3） |
| 后续 Nullable 改造 | 未受影响 | 本次未动 Nullable/ImplicitUsings，红线验证给后续留下了干净基线 |

**结论**：2.34.0 可以进入合并/发布候选，但建议先修 **P1（manifest 断言白名单）** 与 **P2（format 基线 + `out var` 记录）**；这两项都是一小时内可完成的收尾，不需要回滚任何代码。

---

## 7. 建议的收尾顺序（约 0.5–1 人日）

1. `SelfTest.CheckBundledManifestVersions()` 只查 `builtin.*`（或固定 6 个 id）；补一条“用户插件目录不影响自检”的断言。
2. 修 `TrayIcon.cs:291` 的 IDE0090；对 18 个行尾/空白文件做一次规范化（单独提交），然后 `dotnet format style/whitespace --verify-no-changes` 必须为 0。
3. 修正 `docs/version-history.md` 与提交说明中的 `out var` 表述（或补 138 处 `out var`）。
4. 新增 `.git-blame-ignore-revs`（写入 `8d2b91e`），README/AGENTS 说明启用方式。
5. 更新插件指南示例；提交 `samples/ContourLine` 时删除显式 `LangVersion`。
6. 可选：把评估/执行结论并入 `docs/version-history.md`，或提交一份精简版；给 `LucideIcons.cs` 加 `generated_code = true`。

---

## 8. 复现命令

```powershell
git log --oneline -3
dotnet build -c Release -t:Rebuild
dotnet format style --verify-no-changes --no-restore
dotnet format whitespace --verify-no-changes --no-restore
Select-String -Path bin\Release\mirrorpond.log -Pattern '失败项|插件自检'
git ls-files --eol src | Select-String 'w/lf|w/mixed'
git diff -w 57ccc80..HEAD -- src/Contracts src/Ui/Native.cs src/Ui/GlassFeed.cs
```

> 附：本次评估未修改任何源码；工作树仍为评估前的 `.gitignore` 修改 + 2 个未跟踪项。
