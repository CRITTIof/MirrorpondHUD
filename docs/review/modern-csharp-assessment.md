# MirrorpondHUD 改用“现代 C# 风格”的风险与收益评估

> 评估对象：MirrorpondHUD 2.33.0（`feature/lucide-icons` @ `57ccc80`，工作树另有 2 项修改 + 2 项未跟踪）
> 评估日期：2026-11-11
> 评估方式：源码静态审计 + 临时副本实测构建 + 独立探针程序验证运行时语义；**未修改本仓库源码**
> 结论：**不建议“全面现代化 / 大爆炸重排”。推荐先做治理项（A），机械语法子集按需渐进（B），Nullable 单独立项试点；Contracts 的反射字段契约与 P/Invoke 布局是红线。**

---

## 0. 结论摘要

1. **这不是“语言能力”问题。** 项目所有 csproj 已经是 `LangVersion=latest`（当前 SDK 10.0.403 → C# 14），现代语法随时可用；真正要改的是“源码风格 + 类型系统语义”，成本来自 5 万行存量、反射契约、互操作布局与插件 ABI，而非编译器。
2. **全面改造与仓库现有硬约束正面冲突。** `AGENTS.md` §4 把“老式 C#”定为硬约束（显式 `get { }`、不用表达式体、public 字段 DTO、`Nullable=disable`、`ImplicitUsings=disable`）；全面改造需要连带重写 §4/§8/§9、插件指南、架构文档并把 Schema/契约升版。
3. **“字段式 DTO”是本项目的事实序列化协议，不是风格偏好。** `Json.cs` 只反射 public 实例字段（`Json.cs:84`、`:347-351`）且要求无参构造（`:130`）；实测把 DTO 字段改成属性后 `Json.Serialize` 输出 `{}`，反序列化静默保留默认值——会直接造成配置丢失、脏检查失效、渲染签名恒等、插件 manifest 失效。
4. **实测阻断面**：`Nullable=enable` 产生 **1535 条警告**（按位置去重 902 处、涉及 107/166 个文件）；`ImplicitUsings=enable` 产生 **227 个编译错误**（UiKit 149 + 宿主 78，全部 CS0104 类型二义），是硬阻断而非“慢慢修”。
5. **真实收益有限且集中在两处**：构建确定性治理（固定 `LangVersion`、收敛重复配置、补 `.editorconfig`/`.gitattributes`）与未来可选的 Nullable 分阶段引入；其余语法项主要是观感，收益上限约 **−750 行 / −9.6% 字符**，代价是 **4.9 万行重排 diff** 和 git blame/bisect 断层。

**建议决策：**

| 方向 | 结论 | 理由 |
| --- | --- | --- |
| A. 治理项（固定 LangVersion、Directory.Build.props、editorconfig/gitattributes、版本断言） | **Go，立即** | 零运行时语义、可单提交回滚、解决真实确定性风险 |
| B. 机械语法子集（新文件/触碰文件采用；批量重排另议） | **有条件 Go** | 收益观感为主；批量改造须先冻结 worktree、接受 blame 断层 |
| C. 全面现代化（Nullable 全开 + record/属性化 DTO + 分析器） | **No-Go（当前）** | 撞 Contracts 反射/插件 ABI/0 警告纪律，28–45 人日且无已发布基线可对比 |
| C-. Nullable 绿区试点（仅纯逻辑层） | **可选，2–4 人日** | 唯一有真实缺陷发现价值的现代特性，但需绕开契约与互操作 |

---

## 1. 评估方法与证据来源

| 手段 | 内容 |
| --- | --- |
| 基线构建 | `dotnet build -c Release`（增量 1.78 s，0 警告 0 错误） |
| 副本实验 | 将工作树复制到 `%TEMP%\mchud-modern\repo`，在副本中分别以 `-p:Nullable=enable`、`-p:ImplicitUsings=enable`、`-p:LangVersion=12.0` 全量重建（`-t:Rebuild`），不污染本仓库 |
| 运行时探针 | 临时 console 工程引用 `MirrorpondHUD.Contracts`，验证字段/属性/record 在 `Json` 中的真实行为 |
| 静态审计 | 逐文件统计 + `grep` 对 138 个 `src/**/*.cs`（排除 `obj/bin`）分类；解析 439 个 catch 块体 |
| 旁证 | 队友分别做了改造量清点、Nullable/ImplicitUsings 风险分析、治理与插件生态评估（笔记见附录 C） |

**口径说明**：本报告“行数”指文件总行数（含空行）。“唯一警告”按 `(文件,行,列,诊断码)` 去重；MSBuild 汇总数会因 WPF 标记编译（临时 `*_wpftmp.csproj` + 正式工程）重复上报同一诊断，故两个数字并列。

---

## 2. 现状基线（决定改造成本的事实）

| 维度 | 事实 |
| --- | --- |
| 规模 | `src` 138 个 `.cs` / 45,363 行；含 `samples` 共 166 个 / 50,265 行。其中 `LucideIcons.cs` 6,920 行是图标码位静态表 |
| 程序集 | 宿主 + Contracts + UiKit + 6 个内置插件（各自 csproj，运行时 ALC 加载）；9 个产品 csproj + 3 个 sample |
| 语言/构建 | 全部 `LangVersion=latest`（12 个 csproj 含 samples）、`Nullable=disable`、`ImplicitUsings=disable`；无 `Directory.Build.props` / `.editorconfig` / `.gitattributes` / `global.json` / CI |
| 风格采用度 | block namespace 136 文件（另 2 个 AssemblyInfo 无命名空间），file-scoped **0**；`get { }` **439 处**（其中 376 处是单行 `get { return ...; }`；行级统计 382，差值来自跨行访问器），自动属性 133；表达式体成员 **0**（301 处 `=>` 中 285 是 lambda）；record/init/required/`is not`/`??=`/switch 表达式/out var **全 0**；字符串插值 **0** |
| 字符串拼接 | `"..." + ...` 约 1,156 处 / 98 文件；`string.Format` 仅 3 处 |
| 互操作 | `[DllImport]` 115 处、`[StructLayout]` 29 处（21 个宿主文件）、`[FieldOffset]` 2 处、`IntPtr` 418 处；x64 + ARM64 |
| 空 catch | 解析 439 个 catch 块，**152 个块体真正为空**（46 文件；FfmpegPlayer 13、OverlayController 13、WaveOutPlayer 10、CommandRunner 9、Log 9） |
| 注释 | 约 4,607 行（约 10%），含 148 处 P0-x/P1-x 历史评审编号引用 |
| 版本纪律 | `<Version>` 需 15 个文件同步（8 个产品 csproj + 6 个 manifest + README）；`v2.23.0–v2.33.0` 均未合 main、未打 tag、未发 Release |
| worktree | 另有 4 个 worktree：`feature/data-charts` 落后 HEAD **48** 个提交，`main` 落后 **21**，`feature/overlays` 已 prunable |

---

## 3. 为什么“纯风格改造”在本项目不纯：四条硬耦合

### 3.1 JSON 反射只认 public 字段（最高危）

```csharp
// src/Contracts/Core/Json.cs
84:  var fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance); // 序列化
130: public static T Deserialize<T>(string json) where T : class, new()
337: inst = Activator.CreateInstance(target);                                            // 必须无参构造
347: foreach (var f in target.GetFields(BindingFlags.Public | BindingFlags.Instance))     // 反序列化
350:     f.SetValue(inst, Bind(f.FieldType, raw));
```

`Settings.cs` 头注释即写明“字段名即 JSON 键名”；`DLL-CARD-PLUGIN-GUIDE.md:1046` 也要求插件设置 DTO 用 public field。**这不是历史包袱，而是当前协议。**

**探针实测（临时工程，引用本仓库 Contracts）：**

```
field  serialize : { "A": 1, "B": "x" }
prop   serialize : {}                       ← 属性完全不入 JSON
record serialize : {}                       ← record 的属性同样不入 JSON
prop   deserialize: A=1 B=x（期望 7 / y）   ← 反序列化静默忽略，保留默认值
```

把 record 改成正位置参数形式更糟：`Json.Deserialize<T>` 的 `where T : class, new()` 约束会直接编译失败：

```
error CS0310: "RecDto"必须是具有公共的无参数构造函数的非抽象类型，
才能用作泛型类型或方法"Json.Deserialize<T>(string)"中的参数"T"
```

**受污染的不止配置文件**（同一反射协议被当成通用工具复用）：

| 调用点 | 用途 | 字段变属性的后果 |
| --- | --- | --- |
| `ConfigStore.cs:346` | 保存 `settings.json` | 保存出 `{}`，用户配置被清空（`.bak` 尚在，但下次加载回落默认） |
| `SettingsLoader.cs:43/58/74/159` | 加载/Schema 迁移/导出 | 迁移与导出丢失全部字段 |
| `ThemeFile.cs:271/504` | 主题应用前的设置克隆与导出 | 主题应用丢掉当前设置；Selftest 主题往返会失败（此路径有断言） |
| `SettingsWindow.xaml.cs:198/215` | “干净快照”与未保存修改判定 | 快照恒为 `{}`，`IsDirty()` 恒 false → 关窗不提示，编辑静默丢失 |
| `SettingsWindow.xaml.cs:354` | `TryClone` 深拷贝 | 克隆出默认设置，调用方拿到空壳 |
| `RenderScheduler.cs:216-262` | `ContentInputs`/`StyleInputs` 序列化当变更签名 | 所有卡片签名恒等 → 该重画的判断失效（陈旧内容或全量重建） |
| `CardPluginLoader.cs:338` | `PluginManifest`（camelCase 字段即 manifest 键） | manifest 解析成全默认值，`apiVersion`/`entryType` 丢失，插件加载失败 |
| `SettingsWindow.Cards.cs:225` | 卡片设置编辑器深拷贝 | 编辑器拿到空配置 |

体量：Contracts 公共字段 **152 个**（`Settings` 105、`MetricsSnapshot` 27、`ThemeTokens` 10、`AsyncProcess` 6、`SecretStore` 2、`KeyNames` 2）；插件侧还有 58 个公共字段 + `PluginManifest` 7 个。

### 3.2 P/Invoke 布局依赖字段声明

`[StructLayout]` 29 处、`[FieldOffset]` 2 处（`SelfTest.cs:36` 明确 `LayoutKind.Explicit, Size = 40`）、`IntPtr` 418 处，涉及 DWM/DXGI/winmm/user32/gdi32/pdh/crypt32。封送器只认 **public 字段**：把互操作结构体改成属性、`record struct`（正位置参数生成的是属性 + 私有后备字段）或调整字段顺序/加字段，会静默改变布局或让封送读到 0；这类错误在 x64 上可能“看起来能跑”，到 ARM64 才炸。因此互操作结构体属于**禁止转换区**，并且不能借“格式化”之名重排。

### 3.3 WPF 生成入口与生成字段

- 根工程用 `<ApplicationDefinition Include="src\App.xaml" />`（`MirrorpondHUD.csproj:36`），入口 `Main` 由 XAML 生成（`obj/Release/src/App.g.cs`）。**top-level statements 不可用**，会与生成的 `Main` 冲突。
- `x:Name` 字段由 `.g.cs` 生成且未初始化（如 `OverlayWindow.g.cs` 的 `internal OverlaySurface Surface;`）。生成代码没有 `#nullable disable`，但实测 **`.g.cs` 内 0 条 nullable 警告**——Nullable 开启的噪声主要来自手写代码，不是生成代码（这是好消息）。
- 5 个 XAML code-behind（`App`、`OverlayWindow`、`SettingsWindow`、Chat/Media 设置视图）+ `SettingsWindow` 的 8 个 partial：事件处理方法名被 XAML 绑定，不能机械改名/移动。

### 3.4 插件二进制契约

`DLL-CARD-PLUGIN-GUIDE.md:7-9` 明确：插件 API 版本为 2，**2.14.4 起不兼容更早编译的插件**，旧插件必须重编译并引用 `MirrorpondHUD.Contracts`；Contracts/UiKit 由宿主默认 ALC 提供（`CardPluginLoader.cs:695-697`），程序集非强命名。

- **`<Version>` 升级本身不破坏已编译插件**（常见误判）：ALC 按简单名绑定，不看版本。
- **真正的破坏是公共成员形状**：public 字段 → 属性会让已编译插件里的 `ldfld` 变成 `MissingFieldException`；`record`/`init`/`required`/主构造会同时破坏字段反射与对象初始化语义。
- 保护阀是 `apiVersion=2` 校验（`CardPluginLoader.cs:155`，`:365-369` 拒绝并保留用户配置），但升到 3 意味着 6 个内置 manifest + 指南 + 全部用户插件重编译，且没有自动升级路径。
- `CardPluginLoader.cs:486` 只把 `manifest.version` 写日志、不做校验，版本漏改是静默的。
- **区分“改形状”与“加注解”**：`Nullable` 的 `?` / `[NotNullWhen]` 只产生 `NullableAttribute` / `NullableContextAttribute` 元数据，IL 签名、方法表、字段布局不变，对已编译插件**二进制兼容**，不需要重编译、不需要升 `apiVersion`。真正要禁止的是 public 字段 → 属性、record、`required`、`init` 这类**成员形状**变更。这条区分决定了“Nullable 可以试点，DTO 封装化不可以”。

---

## 4. 逐项收益/风险矩阵

评级：收益/风险均为“针对本项目”的工程判断；证据列只列关键项。

| # | 现代 C# 特性 | 收益 | 风险 | 关键证据/影响面 | 建议 |
| --- | --- | --- | --- | --- | --- |
| 1 | 固定 `LangVersion=12.0` | **中**：构建确定，SDK 8 也能编 | 低（实测 0/0） | 12 个 csproj 现为 `latest`；C# 12 全量重建 0 警告 0 错误 | **做（A）** |
| 2 | `Directory.Build.props` 收敛公共属性 | 中：消除 9 份重复配置 | 低 | 无该文件；各 csproj 重复 TargetFramework/Nullable/LangVersion | **做（A）** |
| 3 | `.editorconfig` + `.gitattributes` | 中：锁编码/行尾/缩进，防整文件翻转 | 低（只锁格式，不开风格诊断） | 两者均缺；git 已报 LF→CRLF；version-history 记为“长期项” | **做（A，先不开 IDE 风格规则）** |
| 4 | file-scoped namespace | 低：去一级缩进，−164 行（0.3%） | 低（编译期），但 **49,058 行重排 diff** | 136/138 个 src 文件是 block ns；git blame/bisect 断层 | 新文件/触碰文件用；**不批量** |
| 5 | 表达式体属性/方法 | 低：属性仅 −9 行（376 个已是单行 `get { return ...; }`），方法约 −330 行 | 低，但**违反 `AGENTS.md:46`** | 表达式体成员现为 0；改它要改治理文档 | 先改政策，再只在新代码用 |
| 6 | 字符串插值 | **中**：可读性收益最大（1,156 处拼接、3 处 Format） | 低–中：11 处字面量含 `{}` 需转义；多行拼接需逐条核对 | 日志/UI 中文长串为主；0 行数变化 | 渐进（触碰文件时），不批量 |
| 7 | `is null` / 目标类型 `new()` / `out var` | 低：零行变化，约 −6k 字符 | 低（无 operator 重载，语义等价） | `==/!= null` 1,205 处；`out` 138 处；目标类型 new 实际 0 | 自然使用即可 |
| 8 | `using (...)` → `using var` | 低（−66 行） | **中：Dispose 时机后移到作用域末尾** | 33 处 / 23 文件；原生句柄、锁、代次收尾区域语义敏感 | 逐点人工审；**禁止批量** |
| 9 | `switch` → switch 表达式 | 低（约 −180 行） | 中：无 `_` 分支时运行时抛异常；arm 副作用/fallthrough 需审查 | 66 个 switch 语句，36 个可候选（CardMotion/Json/Settings/TextCanvas） | 逐点，有默认分支才用 |
| 10 | `ImplicitUsings=enable` | 低 | **硬阻断：227 个 CS0104 编译错误** | UiKit 149（14 文件）+ 宿主 78；受影响程序集为根、UiKit、Command、DropStation（后两个插件也开 `UseWindowsForms`），WPF-only 的 Contracts/Chat/Buttons/InfoText/Media 不受影响；`Color/Brush/FontFamily/Panel/Point/MessageBox/Application` 等 WPF↔Drawing/WinForms 二义 | **禁止** |
| 11 | `Nullable=enable` | **中–高**（长期可发现真实 NRE） | **高：1535 警告 / 902 个位置 / 107 文件** | 见 §5；修法约需 `?` 500–600 处、`[NotNullWhen]` 10 处、空守卫 250–300 处，`!` 应 <50；Contracts 自身 0 条 CS8618、无 XAML，可作第一阶段 | 单独立项，分阶段/试点 |
| 12 | DTO 字段 → 属性 | 低（“封装更现代”） | **极高：配置静默丢失 + 插件 ABI 断裂** | 探针实测 `{}`；`Json.cs:84/130/347`；152 个 Contracts 公共字段 | **禁止** |
| 13 | `record` / `init` / `required` / 主构造 | 低（DTO 已是一行一字段） | **极高：CS0310 编译失败或静默默认值 + 二进制不兼容 + 值语义错误** | 位置 record 触发 `new()` 约束；非位置 record 序列化 `{}`；`required` 被 `Activator.CreateInstance` 绕过 | **禁止** |
| 14 | top-level statements | 无（本应用无场景） | 不可行：与生成的 `Main` 冲突 | `ApplicationDefinition` 生成入口 | 不适用 |
| 15 | `[LibraryImport]` / `nint` | 低–中（裁剪/性能/可读性） | 高：封送语义差异、`unsafe`/`partial`、ARM64 需实机验证 | 115 个 DllImport 是独立互操作工程，不属于“代码风格” | 另立项，不与风格混做 |
| 16 | SDK 分析器/风格诊断 | 中（真实缺陷，如 CA2000/CA1031） | 中：会打破“0 警告”门；152 个空 catch 会先爆 | 当前无 editorconfig、无 CI；`catch { }` 是部分 teardown 的故意写法；`AnalysisMode=All` 的 CA1051/CA1002/CA2227 会直接命中 public-field DTO，StyleCop/Roslynator 需 PackageReference（违反零依赖） | 单独立项，只开最小规则集，先清基线再开 |

---

## 5. 实测数据（副本构建）

| 实验 | 结果 | 说明 |
| --- | --- | --- |
| 基线 `dotnet build -c Release` | **0 警告 / 0 错误**，1.78 s（增量） | 现有纪律成立 |
| `-p:Nullable=enable` 全量重建 | **0 错误，1535 条警告**；按位置去重 902 处；**107/166 文件** | 构建成功，不会阻断，但远超“0 警告”门槛 |
| `-p:ImplicitUsings=enable` | **失败**：UiKit 149、宿主 78 个 CS0104 | 全部“类型二义”类错误，不是可容忍警告 |
| `-p:LangVersion=12.0` 全量重建 | **0 警告 / 0 错误**，5.62 s | 证明固定语言版本零风险 |
| 生成 `.g.cs` 的 nullable 警告 | **0 条** | 噪声源是手写代码，不是 XAML 生成代码 |

**Nullable 警告构成（按位置去重，共 902）：**

| 诊断码 | 数量 | 典型含义 |
| --- | --- | --- |
| CS8625 | 245 | null 字面量传给非空参数（大量是 WPF/原生 API 允许 null 的参数） |
| CS8618 | 180 | 字段未在构造函数初始化（多为延迟赋值字段） |
| CS8603 | 174 | 可能返回 null（TryGet/查找类 API 的既有契约） |
| CS8600 | 169 | null 赋给非空局部/返回值 |
| CS8604 | 83 | 可能 null 实参 |
| CS8601 | 21 | 可能 null 赋给非空成员 |
| CS8622 | 11 | 委托参数可空性不匹配（WPF 事件） |
| CS8602 | 9 | 解引用可能 null |

**分布（按位置去重）：** `src/Ui` 304、`src/UiKit` 264、`src/Plugins` 122、`src/Core` 71、`src/Cards` 54、`src/Contracts` 41、`SelfTest` 16、`src/Metrics` 14、`App` 11、`src/Input` 5。单文件最多：`CardPluginLoader.cs` 44、`ThemeFile.cs` 39、`CardTreeTimeline.cs` 36、`CardDataControls.cs`/`FfmpegPlayer.cs`/`CardFormControls.cs` 各 31。

**ImplicitUsings 二义类型（去重）：** Color 68、Brush 17、FontFamily 15、Panel 10、KeyEventArgs 7、Size 6、Point 5、ListBox 4、Image 4、MouseEventArgs 2、Pen 2、SelectionMode 2、ComboBox/Button/TextBox 各 1（UiKit）；宿主另见 DragDropEffects、IDataObject、DragEventArgs、MessageBox、Application、Clipboard 等。

**两点归属确认：**

1. **180 条 CS8618 全部来自手写字段**，不是 XAML 生成字段：单文件最多的是 `ThemeFile.cs` 14、`DesktopFrameSource.cs` 11、`CardDataControls.cs` 9、`CardSlot.cs` 9，`SettingsWindow` 侧是 `_allFonts`/`_selectedFont`/`_dragEditor`/`_settingsWindow` 等手写字段；按 `VersionText|Tabs|SearchBox` 检索生成字段名，命中 0。因此**不需要**为 `.g.cs` 配置 `NoWarn` 或 editorconfig 豁免，Nullable 的噪声只来自手写代码。
2. **现有 1,205 处 `== null` 判空显著压低了风险**：实测 `CS8602`（解引用可能为 null）只有 9 条，`CS8600` 169 条也多为“null 赋给非空局部”。这说明本项目的防御式风格已经在运行期做了大量空值处理，Nullable 的主要成本是机械标注（CS8625/CS8618/CS8603），不是救火。
3. **CI 口径建议固定为去重后的 902**：1535 是 MSBuild 汇总值，`*_wpftmp` 标记编译会把同一诊断重复上报；按 `(文件,行,列,诊断码)` 去重后 902 才是可跟踪的存量。

**契约空语义的一个待确认点（非风格问题）**：`CardRenderContext` 构造函数对 `Settings`/`Snapshot` 做了 null 兜底（`CardModule.cs:126-127`），但 `Keys = keys; Config = config;`（`:128-129`）直接赋值；与此同时 `ChatCard.cs:75/94`、`InfoTextCard.cs:113` 等插件按“`Config` 可能为 null”防御。这种不一致应在加 Nullable 注解前先定契约（谁保证非空），否则注解只会把含糊固化成 `!`。

---

## 6. 收益量化（诚实口径）

| 项 | 量化收益 | 是否“真收益” |
| --- | --- | --- |
| file-scoped namespace | −164 行（0.3%），去掉 4 空格缩进约 −19.5 万字符（≈8.9%） | 否，纯粹观感；代价 4.9 万行 diff |
| 表达式体成员 | 属性 −9 行、方法约 −330 行，合计 −340 行（0.68%） | 否；违反现行 AGENTS §4，且 `get { return x; }` 已是单行 |
| 字符串插值 | 0 行，约 −8~10k 字符 | 部分：中文长日志可读性确实提升，但不改变行为 |
| `is null`/`tnew`/`out var` | 0 行，约 −6k 字符 | 否，纯口味 |
| `using var`/switch 表达式 | −66 / −180 行 | 否，且有语义风险 |
| **合理收益上限** | **约 −750 行（−1.5%）、−9.6% 字符** | 无性能收益、无 API 收益 |
| 固定 LangVersion + props + editorconfig | 构建确定性、跨 SDK/机器一致、编码/行尾稳定 | **是**（唯一确定的正收益） |
| Nullable | 若认真注解：把 null 契约写进类型系统，长期减少 NRE；若用 `!` 消警告：只剩噪声 | **潜在真收益**，但需 902 个位置逐点判断，不能自动修 |
| record | 对“一行一字段”的 DTO 几乎无样板收益；值语义与本项目“设置树引用纪律”冲突 | 否，负收益 |
| 分析器 | CA2000/CA1031 能暴露真实资源/吞错问题（现有 152 个空 catch） | 是，但属于“质量工程”，应独立于风格改造 |

---

## 7. 风险清单（按严重度）

| 级别 | 风险 | 触发方式 | 防护 |
| --- | --- | --- | --- |
| **P0** | 用户配置被写成 `{}` 或回落默认 | DTO 字段→属性/record/init；`Json.Serialize` 只认字段 | Contracts 公共 DTO 冻结；加“序列化非空字段数”断言 |
| **P0** | 已安装插件运行时报 `MissingFieldException` | Contracts 公共成员形状变化 | 契约 ABI 版本门；apiVersion 升 3 才允许 |
| **P0** | 插件 manifest 解析失败（全部字段默认） | `PluginManifest` 改属性/record | 保持 public 字段 + camelCase；加载前校验必填 |
| **P1** | 原生句柄释放时机变化导致泄漏/双重释放/UAF | `using (...)` 批量改 `using var` | 互操作/代次/锁区域禁止批量；逐点审查 |
| **P1** | 封送布局在 ARM64 上错位 | 互操作结构体重排/属性化/record struct | `StructLayout` 文件白名单，禁止重排 |
| **P1** | “0 警告”门被打破，纪律失效 | 直接开 Nullable/分析器 | 先建基线抑制（`NoWarn`/`.editorconfig`），再逐项目清零 |
| **P2** | 空语义含糊被注解固化成 `!` | `CardRenderContext.Keys/Config` 无兜底但插件按可能 null 防御；先加注解后定契约 | 先统一契约，再加注解 |
| **P2** | git blame/bisect 断层、分支合并冲突 | 全仓重排 4.9 万行 | 单程序集小批提交；`.git-blame-ignore-revs`；先冻结/作废落后 worktree |
| **P2** | 混合风格长期漂移 | 部分文件现代、部分老式，无自动约束 | 先定 `.editorconfig` 与 AGENTS 新边界，再渐进 |
| **P2** | 设置脏检查失效（关窗不提示） | `SettingsWindow` 的 JSON 快照 | 若动 DTO，必须补 UI 层断言/人工回归 |

---

## 8. 分档方案

### A. 治理项（建议立即做，4–6 人日）

1. 12 个 csproj 的 `LangVersion=latest` → `12.0`（实测 0/0；C# 11+ 特性当前 0 处，可证安全）。
2. 新增 `Directory.Build.props` 收敛 `TargetFramework`/`Nullable`/`ImplicitUsings`/`LangVersion`/`DebugType` 等重复项；版本号仍按现有 15 文件纪律走。
3. 新增 `.editorconfig`（`charset=utf-8`、`end_of_line=crlf`、`indent_size=4`，**先不开** IDE 风格诊断）+ `.gitattributes`（防 CRLF/LF 整文件翻转）。
4. `SelfTest` 增加 `manifest.version == 宿主 Version` 断言（当前 `CardPluginLoader.cs:486` 只记日志不校验）。
5. 更新 `AGENTS.md`：区分“硬约束（契约/互操作）”与“风格偏好（可渐进）”。

**回滚**：每项一个提交，纯配置/文档，`revert` 即恢复，无运行时语义。

### B. 机械语法子集（有条件 Go，10–15 人日）

只做“编译器可验证 + 不碰公共形状”的部分：

- ① file-scoped namespace；② 纯 `get { return ...; }` → 表达式体；③ 单 return 方法体；④ 非日志/非模板的字符串拼接 → 插值。

**红线（不做）**：Contracts 全部 public 字段、`StructLayout`/`DllImport`、`PluginManifest`、XAML `x:Class`/`x:Name` 与事件处理方法名、`catch { }` 清理块（152 个空 catch 中相当一部分是 teardown 的故意写法，如 `CardPluginLoader.cs:186/208`）、`using (...)` → `using var`。

**Go 条件**：A 完成；`feature/data-charts`/`feature/overlays` 明确作废或先合并；接受一次 blame 断层；每批 ≤1 个程序集，批次门 = `build 0/0 + --selftest 0`；有第二人审。顺序：Contracts 非 DTO → UiKit → 暂停复盘 → 逐插件 → 宿主；若出现一次“build 绿但行为变”，立即终止后续批次。

**回滚**：每程序集一提交，单批 `revert`。

### C. 全面现代化（当前 No-Go，28–45 人日）

包含 Nullable 全开（15–25）、分析器（2–4）、record/init（3–5）、文档与全量回归（3–5）。阻断理由：Contracts 冻结截断收益上限；apiVersion 3 生态断代；无 CI/无测试工程撑不住噪声治理；`v2.25–v2.33` 未发布，没有重构前后可对比的基线。

**替代方案 C-（可选，2–4 人日，按实际范围可能上浮）**：Nullable 分阶段引入，而不是“绿区文件级”一刀切。

- **先修语义**：`CardRenderContext` 的 `Keys`/`Config` 契约、`Json` 绑定失败时的 null 默认值，先把“谁保证非空”定清楚。
- **第一阶段 Contracts**：它没有 XAML、自身 0 条 CS8618、注解对插件二进制兼容，是零生成代码风险的最优起点；但 **DTO 字段必须保持非空**（`Settings` 字段全有初始化器，若标成 `string?` 会让 5 个程序集成百上千处 `.Trim()/.Length` 变 CS8602）。
- **之后按 UiKit → Plugins → 宿主**推进；P/Invoke 句柄不变量（“非 0 / 谁释放”）用 Nullable 表达不了，不强行标注；WPF 生成字段无需豁免。
- 若只想快速试水，可先只对 `src/Core`（除 ConfigStore）、`src/Contracts/Text`、`src/Metrics` 的纯解析/逻辑层逐文件 `#nullable enable`。

---

## 9. 若执行 B/C 的验证矩阵

| 门 | 命令/检查 | 参考耗时 |
| --- | --- | --- |
| 构建 | `dotnet build -c Release`（0 警告 0 错误） | 增量 2 s / 全量 6–8 s |
| 自检 | `bin\Release\MirrorpondHUD.exe --selftest`（退出码 0） | 约 8–10 s（日志时间戳；本轮未重跑，避免注入键盘事件） |
| UI 渲染 | `--screenshot-cards` / `--screenshot-settings` / `--screenshot-widgets` | ≈2 s/张 |
| 插件加载 | 启动后查 `mirrorpond.log` 的 6 个 builtin 加载行 | <0.5 s |
| 配置往返 | 改设置→保存→重启→核对 `settings.json` 非 `{}`、关键字段保留 | 人工 1–2 min |
| 互操作/ARM64 | x64 + ARM64 各跑一次冒烟/截图（有 ARM64 设备时） | 视发布流程 |

**建议补充的断言**（比手工回归更可靠）：把 `SelfTest` 的 JSON 往返断言从“5 个字段”扩到全部持久化字段（105 个）；加“序列化结果中 public 字段数 == 类型声明字段数”的契约断言，防止将来有人把 DTO 改成属性。

---

## 10. 最终建议（一页话）

- **不要**把“改成现代 C#”当成一个整体项目。它是 16 个可独立决策的特性，其中 4 个在本项目是硬阻断（ImplicitUsings、DTO 属性化、record、top-level），2 个是语义风险（`using var`、switch 表达式），真正值得付费的是 **治理确定性** 和 **未来的 Nullable**。
- **先做 A（4–6 人日）**：固定 LangVersion、收敛 csproj、补 editorconfig/gitattributes、版本断言。这是唯一“投入小、确定有收益、可完全回滚”的部分。
- **B 只在两种情况下做**：① 你打算长期维护并接受一次 blame 断层；② 落后 48 个提交的 worktree 已作废/合并。否则“新文件用现代语法、老文件不动”是成本最低的折中，但需要在 AGENTS.md 里写清楚边界，避免风格漂移。
- **C 当前不做**。如果未来要做，前置条件是：CI（构建+自检+截图）、Contracts 序列化层改造方案（支持字段+属性或显式转换器）、apiVersion 3 决策、至少 2 人维护。

---

## 附录 A：复现命令

```powershell
# 基线
dotnet build -c Release

# 副本实验（不污染工作区）
robocopy . $env:TEMP\mchud-modern\repo /E /XD .git bin obj publish certs .proto .vs
cd $env:TEMP\mchud-modern\repo
dotnet build -c Release -t:Rebuild -p:Nullable=enable          # → 1535 警告 / 0 错误
dotnet build -c Release -t:Rebuild -p:ImplicitUsings=enable    # → UiKit 149 个 CS0104
dotnet build -c Release -t:Rebuild -p:LangVersion=12.0         # → 0 警告 0 错误

# 关键静态统计（排除 obj/bin）
Get-ChildItem src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
  Select-String -Pattern '\bget\s*\{' | Measure-Object
```

## 附录 B：口径与限制

- 构建数据来自 2026-11-11 的工作树副本（`src` 138 文件）；工作树在评估期间由用户提交过（HEAD 从 `b109f29` → `57ccc80`），`AGENTS.md` 于 13:36 被实时修改（§10 新增一条），治理文档是活目标。
- 未运行 `--selftest`（其会注入键盘事件），自检耗时取自既有 `mirrorpond.log` 时间戳。
- 未做 ARM64 实机验证；ARM64 风险来自互操作布局推理，不是实测。
- “位置级去重 902”与“MSBuild 汇总 1535”的差异来自 WPF 临时工程/正式工程重复编译与同一位置多条参数级诊断；两者都保留在报告中，避免误读为“只有 902 条”。后续若接入 CI，建议口径固定为去重后的 902。
- 已确认 180 条 CS8618 均来自手写字段（按生成字段名检索命中 0），因此 `.g.cs` 不需要 `NoWarn` 豁免；该结论基于当前 26 个 `.g.cs` 与本次构建日志。

## 附录 C：队友分析笔记（本机临时目录）

- `%TEMP%\mchud-modern\style-scanner.md`：分目录改造量、三类文件清单、收益估算。
- `%TEMP%\mchud-modern\nullable-analyst.md`：Nullable/ImplicitUsings/分析器风险与标注成本。
- `%TEMP%\mchud-modern\governance-analyst.md`：治理/插件生态/验证成本、三档方案与人日。

## 11. 执行结果（2026-11-11，v2.34.0）

按用户确认执行 **A+B**，C 未做：

- A：`Directory.Build.props`、`LangVersion=12.0`、`.editorconfig`、`.gitattributes`、
  manifest 版本断言、`AGENTS.md` 现代风格条款；
- B：全仓 `src/**` + 两个跟踪示例的 file-scoped namespace、表达式体、目标类型 `new()`、
  `out var`、`is null`、安全字符串插值；`dotnet format` 做 Roslyn 可验证项，
  临时 Roslyn 重写器补 `== null` / 简单拼接；
- 红线保持：Contracts 公共字段、`PluginManifest`、`[StructLayout]` / `[FieldOffset]` /
  `[DllImport]` 布局、XAML `x:Name` / 事件名、catch 体、插件 `apiVersion=2`；
- 未做：Nullable、ImplicitUsings、record/init/required、DTO 属性化、分析器、`using var`、
  top-level statements；
- 验证：`dotnet build -c Release` 0/0，`--selftest` 0，三套 `--screenshot-*` 0，
  `--smoke 7 --smoke-visible` 0；manifest 版本断言通过；
- 代价：约 152 文件、4 万行级别的重排 diff（主要是 namespace 去一级缩进），
  git blame/bisect 会断层，已按“治理/语法独立提交”的原则处理；
- 未验证：ARM64 实机、外部已编译插件的二进制回归、Nullable 噪声基线。
