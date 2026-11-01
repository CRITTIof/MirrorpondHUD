# 运行时扩展卡片（`plugins/cards/*.dll`）

> 状态：2.14.5 把「可信本地插件 + 重启生效」保持为生产路径，并完成方案 B 的主要程序集边界
> （2.14.1 拆 `MirrorpondHUD.Contracts`、2.14.2 拆 Core→Cards、2.14.3 迁 `ThemeTokens`、
> 2.14.4 删除类型转发并把插件 API 升到 2、2.14.5 拆出 `MirrorpondHUD.UiKit`）；
> 分支 `feature/runtime-card-loading` 上另有 dev-only 纯文字卡热重载（未推送、未发布、未合并）。
> 已实现：`plugins/cards/<id>/manifest.json` + 独立可收集 ALC + `CardRegistry.Initialize` 启动早期注入；
> 影子复制到 `%LOCALAPPDATA%\MirrorpondHUD\plugin-cache`（失败回退源目录）；缺失插件「插件不可用」占位卡
> （保留 Kind / Content / Extras、不写回配置）；manifest 大小 / id / apiVersion / assembly / entryType / Kind 校验
> （拒绝内置与旧别名 Kind）；加载报告与重复 id / Kind 拒绝；`--plugin-dev` 纯文字卡热重载（拒绝富内容 /
> 交互 / 设置区块 / 旧别名）；设置页引用释放；示例见 `samples/RuntimeCardExample`。
> 已实现（方案 B，2.14.1–2.14.5）：独立 `MirrorpondHUD.Contracts` 契约程序集（15 个契约源码文件迁出）与
> `MirrorpondHUD.UiKit` 共享控件程序集（8 个控件迁出）；插件只引用 Contracts（可选 UiKit）、不引用宿主；
> 插件 ALC 对宿主 / Contracts / UiKit 都走默认 ALC；插件 API=2；旧插件（2.14.3 及之前编译 / `apiVersion=1`）
> 不再兼容，需重编译。
> 未实现：签名 / 哈希 / 白名单；富内容卡热重载；安全沙箱；插件市场 / 自动更新；插件 API 版本迁移 UI。
> 生产 UI 不提供热插拔。
> 基线：v2.12.2（文件级代码审计 + 现有模块化评审结论）；v2.13.6 完成 spike，2.14.0 补齐生产路径与 dev 热重载，
> 2.14.1–2.14.5 完成方案 B 的程序集边界与 API 2。
> 结论先行：技术上可做，但**不能直接把 DLL 丢进一个目录就加载**。单程序集、静态 `CardRegistry` 与
> “未知 Kind 回退默认卡”的归一化策略会分别带来契约版本、加载时序和配置丢失三个问题；2.14.0 已用
> 「影子复制 + 占位卡 + 清单校验 + 加载报告」补上，2.14.1–2.14.5 再完成契约 / UiKit 程序集边界与 API 2 收紧，
> 但 `AssemblyLoadContext` 只解决加载与依赖隔离，不解决沙箱、卸载和契约演进。仍建议把插件当**可信本地扩展**，
> 生产保持“重启生效”。

---

## 1. 目标与非目标

### 目标

- 用户把一个第三方卡片插件放进插件目录，重启后它出现在「添加卡片」菜单里；
- 插件可以贡献文字卡（`ICardModule`）或富内容卡（`IRichCardModule`），并复用现有的
  设置区块、摘要、校验、热键、拖放、样式、宿主服务等契约；
- 插件缺失 / 版本不兼容 / 加载失败时，宿主照常启动，**已有配置和状态不被破坏**；
- 插件拥有稳定的标识（Kind / 设置键 / 状态目录），升级宿主后配置仍能读回；
- 安装 / 更新 / 卸载路径明确，且对“不可信代码”有清晰的安全边界说明。

### 非目标（第一版）

- 生产不承诺热插拔：替换 DLL 后要求重启；开发期只有 `--plugin-dev` 的纯文字卡热重载；
- 不把 `AssemblyLoadContext` 当沙箱；不加载来路不明的插件；
- 不做插件市场 / 在线下载 / 自动更新；
- 不把现有卡片改成独立进程；
- 不改变 `cards/<cardId>` 这个状态目录的现有语义（它是**状态**，不是插件目录）。

---

## 2. 现状审计（代码证据）

| 事实 | 代码位置 | 对运行时插件的含义 |
|---|---|---|
| 单程序集（2.12.2 基线）：契约、所有卡片实现、宿主、WPF 控件都在 `MirrorpondHUD.dll`；2.14.1 起契约 / DTO 拆到 `MirrorpondHUD.Contracts`、2.14.5 起 8 个共享控件拆到 `MirrorpondHUD.UiKit`（见 §4.1） | `MirrorpondHUD.csproj`、`src/Contracts/MirrorpondHUD.Contracts.csproj`、`src/UiKit/MirrorpondHUD.UiKit.csproj` | 插件只引用 Contracts / UiKit；宿主不再做类型转发（2.14.4 删除） |
| 注册表是静态只读列表，第一次使用时初始化 | `src/Cards/CardRegistry.cs:15` `static readonly List<ICardModule> _modules = Discover();` | 插件必须在 `CardRegistry` 第一次被访问前加载；运行中无法追加 / 卸载 |
| 发现只扫当前程序集、要求 public 无参构造 | `src/Cards/CardRegistry.cs:81-101` | 不会扫目录；加载失败只写日志，不会出现在 UI |
| `Modules` 暴露的是可变 `IList` | `src/Cards/CardRegistry.cs:19` | 插件 / 调用方可以改内部列表；需要改成不可变快照 |
| 未知 Kind 会回退到 `_modules[0]` | `src/Cards/CardRegistry.cs:21-38`、`src/Cards/CardModel.cs:43` | 插件缺失时，配置里的 Kind 会被**改写成默认卡**，属于数据破坏 |
| 归一化在 Core 层调用 Cards 层 | `src/Core/ConfigStore.cs:4`、`:69-70` 调 `CardModel.Normalize`（2.14.2 已改为 `SettingsNormalizer` 委托注入） | “Core 不认识卡片”的环已解开；宿主在启动早期注入归一化钩子 |
| 卡片设置在 `Extras` 里按模块类型绑定 | `src/Core/Settings.cs:721-756`、`Json.BindNode<T>` | 插件类型不在时无法绑定；原始 JSON 必须原样保留，不能丢 |
| 状态目录是 `<settings.json 所在目录>/cards/<cardId>` | `src/Core/CardStateStore.cs:18-25` | 与 README 里的 `cards/*.dll` 命名冲突；插件目录应另起 `plugins/` |
| 添加卡片菜单直接遍历静态 `CardRegistry.Modules` | `src/Ui/SettingsWindow.xaml.cs:680-730` | 需要“加载失败 / 不兼容”的列表项与原因，而不是静默消失 |
| 富内容契约直接出现 `FrameworkElement` | `src/Cards/RichCard.cs:12-53` | 契约程序集必须面向 WPF（`net8.0-windows` + `UseWPF`），插件必须能在 UI/STA 线程创建控件 |
| 契约与实现没有 ApiVersion | `src/Cards/CardModule.cs` 全文 | 二进制不兼容时只能靠程序集版本 / 类型加载失败来发现 |
| README 曾写“运行期插件暂未实现”（v2.12.2 调研基线时） | `README.md:281`、`README.md:416` | 本文是补这缺口的前置调研；2.14.0 已同步 README，2.14.1–2.14.3 再补契约程序集 / 类型转发说明 |

§2 是 2.12.2 基线的代码审计（行号按当时源码）；表中标注了 2.14.x 的落地变化（契约程序集 / 归一化钩子 / UiKit / 插件 API 2），未标注的行是基线状态，部分已在后续版本变化，看现状以各节说明与源码为准。

### 2.1 已存在的结构性问题

`docs/modularity-review.md` 已经指出过两点，本文直接沿用：

- 单程序集 + 命名空间环：`Core` 引用 `Cards`，`Cards` 又引用 `Core`，`CardRegistry.Discover`
  用 `typeof(CardRegistry).Assembly`（评审 §5 / §9）；
- 设置页、宿主、自检都直接依赖 `CardRegistry` 的静态 API（评审 P1-10 相关）。

这意味着运行时插件**不是一个 `Assembly.LoadFrom` 就能完成的功能**；它首先是一次契约边界整理。

---

## 3. 候选方案对比

| 方案 | 富 WPF 界面 | 隔离性 | 卸载 / 热更新 | 对现有代码改动 | 结论 |
|---|---|---|---|---|---|
| 编译期模块（现状） | 原生 | 无（同进程） | 重新编译 | 无 | 安全、简单，但不是运行时扩展 |
| 进程内 `AssemblyLoadContext` | 原生，`FrameworkElement` 可直接用 | 无安全隔离，只有依赖隔离 | 可收集 ALC，但 WPF 引用很容易阻止卸载 | 中—大（拆契约 / Registry 改造） | **推荐作为第一版**，可信插件 |
| MEF / `System.Composition` | 原生 | 无 | 同样需要 ALC，MEF 不解决卸载 | 中 | 只解决发现 / 依赖注入，不解决本项目核心问题，暂不引入 |
| 外部进程 + IPC | 难：进程外不能直接返回 `FrameworkElement`；可视化需嵌入 HWND / WebView | 强（进程 / 权限可分开） | 天然可重启 | 大 | 适合“数据型插件”，不适合现有富卡片契约 |
| Lua / JS / C# 脚本 | 受限于脚本层 UI 能力 | 取决于沙箱实现 | 容易重载 | 大 | 与现有 WPF 控件模型不匹配，暂不考虑 |
| WebView / HTML 卡片 | 独立技术栈 | 中 | 容易 | 大 | 等于另立一套卡片体系，不是本调研目标 |
| COM / 原生 DLL | 取决于封装 | 中 | 取决于宿主 | 大 | 与 .NET 契约混用成本高，不推荐 |

**推荐路线：进程内 ALC + 可信本地插件 + 重启生效。** 如果以后需要加载不可信插件，
再单独做“外部进程 / 数据型插件”通道，不要把它和 WPF 富卡片混在一个模型里。

---

## 4. 推荐架构

### 4.1 程序集边界：先做 A，长期做 B

当前代码分成三块：契约 / DTO 在 `MirrorpondHUD.Contracts`，8 个共享 WPF 控件在 `MirrorpondHUD.UiKit`，
宿主实现（卡片、窗口、设置页等）仍在 `MirrorpondHUD.dll`。演进路径：

#### 方案 A：宿主程序集就是契约（2.13.6 / 2.14.0 的旧路径）

- 插件直接引用 `MirrorpondHUD.dll`，实现 `CardModuleBase`；
- 插件 ALC 对 `MirrorpondHUD` 程序集名返回 `null`，强制回落到默认 ALC，保证
  `typeof(ICardModule)` 是同一个 Type；
- 要求插件与宿主**精确版本匹配**（或至少同 ApiVersion）；
- 插件能直接使用 `CardButton`、`CardList`、`CardPalette` 等公开控件，开发体验最好；
- 缺点：契约 = 整个宿主公开面；宿主内部重构可能破坏插件；无法支持多个宿主版本。

2.14.1 起新插件不再走这条路径；2.14.4 起宿主删除 `TypeForwardedTo` 兼容层，引用宿主的旧插件二进制
不再兼容，必须重编译为引用 Contracts / UiKit。**方案 A 只保留为历史实现。**

#### 方案 B：独立 `MirrorpondHUD.Contracts` + `MirrorpondHUD.UiKit`（2.14.5 已完成）

**状态：2.14.5 已完成 Contracts + UiKit 程序集拆分，兼容层已在 2.14.4 移除。**

已迁到 Contracts 的内容（命名空间保持不变，减少源码改动）：

- `ICardModule` / `IRichCardModule` / `CardModuleBase` / `RichCardModuleBase`；
- `ICardSettingsModule` / `ICardEditorSection` / `ICardSettingsValidator` / `ICardSummary`；
- `ICardStyleAware` / `ICardDropTarget` / `ICardDropFeedback` / `ICardHotkeySource/Target`；
- `ICardHost` / `ICardHostAware` / `ICardActivation`；
- `CardRenderContext` 以及它依赖的 DTO：`Settings` / `MetricsSnapshot` / `KeyHistory` / `CardSettings`；
- `CardHotkeyAction`、`ThemeTokens` / `ThemeResolver`（2.14.3）、`StyleSettings` 等插件确实需要的公开类型；
- 基础工具 `Log` / `Json`（Contracts 内部实现，`InternalsVisibleTo` 只向宿主开放）。

已迁到 UiKit 的 8 个共享控件（2.14.5，命名空间 `MirrorpondHUD.Ui.Controls` 不变）：
`CardButton` / `CardList` / `CardStatus` / `CardHeader` / `CardInput` / `CardTextBox` / `CardDropOverlay` / `CliUi`
（含 `CardPalette`）。UiKit 只依赖 Contracts，不依赖宿主。

**插件构建方式（2.14.5 起）**：插件工程引用 `src/Contracts/MirrorpondHUD.Contracts.csproj`；需要共享 WPF 控件时
再加 `src/UiKit/MirrorpondHUD.UiKit.csproj`（可选，`Private=false`）。两者都不要复制 DLL 进插件目录，
也**不要引用宿主 DLL**。`dotnet build samples\RuntimeCardExample\RuntimeCardExample.csproj -c Release` 会自动构建
Contracts（示例不需要 UiKit）。示例见 `samples/RuntimeCardExample`。

**兼容性（2.14.4 起）**：宿主不再做 `TypeForwardedTo`。插件 `apiVersion` 从 1 升到 2；`apiVersion=1` 的
旧 manifest 被拒绝（日志「卡片插件 API 版本不兼容: … api=1（宿主支持 2）」），2.14.3 及之前编译、
引用 `MirrorpondHUD.dll` 的旧插件二进制也不再加载。旧插件需要重编译：改引 Contracts（需要控件时加 UiKit）
并把 `apiVersion` 改成 2。插件 ALC 对宿主 / Contracts / UiKit 三个程序集都返回 `null`，由默认 ALC 提供。

**剩余代价**：

- 版本三件套（宿主 / Contracts / UiKit）必须同步发版（2.14.x），自检会断言契约类型的程序集身份；
- 插件 API 1 → 2 的迁移目前只有文档说明，没有设置页提示或自动迁移工具；
- 显式引用 UiKit 的插件加载已用临时插件验证通过；尚未固化成示例 / 回归用例。

#### 方案 C：只给“数据型插件”的窄接口

插件只返回字符串 / 简单图形 / 数据，不返回 `FrameworkElement`；这样契约可以很小，
甚至可以放到独立进程。但它无法复用现有富卡片能力，只能作为另一条产品线。

**建议：** 方案 B 的主要程序集边界（Contracts + UiKit）已在 2.14.1–2.14.5 完成、插件 API=2；
后续重点是签名 / 白名单 / 沙箱与插件 API 迁移体验，不再拆程序集。

### 4.2 插件目录与清单

2.14.5 起插件工程引用 `src/Contracts/MirrorpondHUD.Contracts.csproj`，需要共享 WPF 控件时再加
`src/UiKit/MirrorpondHUD.UiKit.csproj`（可选，`Private=false`），都不引用宿主；
`apiVersion` 必须为 2（2.14.4 的破坏性变更）。插件目录与 manifest 结构不变。

#### 目录

不要用 `cards/`：

- `CardStateStore` 已经把 `<settings.json 目录>/cards/<cardId>/` 用作**每卡状态**；
- README 里写的 `cards/*.dll` 会和状态目录混在一起，卸载 / 清理时容易误删。

建议：

```text
<exe 目录>/plugins/cards/<plugin-id>/
  manifest.json
  <plugin>.dll
  <plugin>.deps.json
  <plugin>.runtimeconfig.json   // 可选
  <依赖>.dll
```

如果 exe 目录不可写（安装到 Program Files），跟随 `ConfigStore` 的回落规则放到
`%LOCALAPPDATA%\MirrorpondHUD\plugins\cards\`。插件目录本身不应可被其他用户写。

#### manifest

```json
{
  "id": "com.example.clock-plus",
  "name": "Clock Plus",
  "version": "1.2.0",
  "apiVersion": 2,
  "minHostVersion": "2.12.1",
  "entryType": "ClockPlus.ClockCard",
  "assembly": "ClockPlus.dll",
  "author": "example",
  "sha256": "…"
}
```

| 字段 | 用途 |
|---|---|
| `id` | 稳定插件标识；用于日志 / 启用状态 / 卸载，不等同于卡片 `Kind` |
| `apiVersion` | 契约版本；宿主只加载自己支持的整数版本 |
| `minHostVersion` | 可选的人类可读下限；真正校验仍以 `apiVersion` 为准 |
| `entryType` | 程序集里的类型全名，必须实现 `ICardModule` |
| `assembly` | 主 DLL 文件名，禁止 `..` / 绝对路径 |
| `sha256` | 可选；开启签名 / 校验时使用 |
| `author` / `version` | 设置页展示与排错 |

**2.14.5 实际校验（`CardPluginLoader.LoadOne`）**

- `manifest.json` ≤ 256 KB；JSON 读取 / 解析失败或为空直接拒绝；
- `id` ≤ 100 字符，只允许字母数字与 `.` `-` `_`，不能用 `.` / `..`；
- `apiVersion` 必须等于 2（`SupportedApiVersion`）；`apiVersion=1` 会被拒绝，日志「卡片插件 API 版本不兼容: <id> api=1（宿主支持 2）」；
- `assembly` 必须是不含路径分隔符 / 非法字符的 `.dll` 裸文件名，且文件必须存在；
- `entryType` 非空、不含空白；类型必须存在、非抽象 / 接口、实现 `ICardModule`，且有 public 无参构造函数；
- 模块 `Kind` ≤ 64 字符、只允许字母数字与 `.` `-` `_`，并拒绝内置 Kind 与旧别名
  （`infotext` / `command` / `media` / `chat` / `dropstation` / `buttons` / `metrics` / `clock` / `keys` / `text`）；
- 同批扫描拒绝重复 `id`、重复 `Kind`、空 `Kind`；失败只影响该插件，不阻止宿主启动。

`minHostVersion` / `sha256` 仍是规划字段（当前 manifest 解析器没有这两个字段，写了会被忽略）；签名 / 哈希校验尚未实现。

### 4.3 加载时序与发现

当前 `CardRegistry` 是静态初始化；而 `ConfigStore.Load()` 会调用 `CardModel.Normalize`，
后者会访问 `CardRegistry`。所以**插件加载必须发生在 `ConfigStore.Load` 之前**，否则
`CardRegistry` 已经定型。

建议把启动顺序改成：

```csharp
// App.OnStartup，越早越好
var plugins = CardPluginLoader.LoadAll(pluginRoot);   // 只加载，不碰配置
CardRegistry.Initialize(plugins.Modules);             // 一次性构建快照
Settings settings = ConfigStore.Load();               // 此时 Normalize 已能看到插件
```

`CardRegistry` 改为：

```csharp
public static void Initialize(IEnumerable<ICardModule> external)
{
    // 内置模块 + 插件模块；Kind 冲突直接拒绝插件并记录原因
    // 构建不可变快照，不再暴露可变 List
}
```

发现流程（示意）：

```csharp
sealed class CardPluginLoadContext : AssemblyLoadContext
{
    readonly AssemblyDependencyResolver _resolver;
    readonly string _hostAssemblyName;
    readonly string _contractAssemblyName;
    readonly string _uiKitAssemblyName;

    public CardPluginLoadContext(string mainPath, string hostAssemblyName)
        : base("MirrorpondHUD.CardPlugin:" + Path.GetFileNameWithoutExtension(mainPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainPath);
        _hostAssemblyName = hostAssemblyName;
        _contractAssemblyName = typeof(ICardModule).Assembly.GetName().Name;
        _uiKitAssemblyName = typeof(MirrorpondHUD.Ui.Controls.CardButton).Assembly.GetName().Name;
    }

    protected override Assembly Load(AssemblyName name)
    {
        // 宿主 / 契约 / UiKit 都必须由默认 ALC 提供，不能在插件 ALC 里再加载一份
        if (string.Equals(name.Name, _hostAssemblyName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name.Name, _contractAssemblyName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name.Name, _uiKitAssemblyName, StringComparison.OrdinalIgnoreCase))
            return null;

        string path = _resolver.ResolveAssemblyToPath(name);
        return path == null ? null : LoadFromAssemblyPath(path);
    }
}
```

要点：

- **不要用 `Assembly.LoadFrom`**：它会锁文件、进入默认上下文，且不利于卸载；
- 用 `LoadFromAssemblyPath` / `LoadFromStream`，配合 `AssemblyDependencyResolver`；
- `AssemblyDependencyResolver` 需要插件的 `.deps.json`（类库项目默认会生成）；
- 插件自己的依赖走插件 ALC；宿主 / 契约 / UiKit（`MirrorpondHUD` / `MirrorpondHUD.Contracts` / `MirrorpondHUD.UiKit`）都走默认 ALC；
- 2.14.5 起 `CardPluginLoadContext` 同时记录宿主 / 契约 / UiKit 三个程序集名，`Load` 对三者都返回 `null`，
  否则插件会拿到第二份 `ICardModule` 或第二份控件类型，与宿主不是同一个 Type；
- 加载前用 `MetadataLoadContext` 做一次只读预检（类型名、引用、ApiVersion），可以避免
  一上来就把坏 DLL 执行起来；预检不是安全边界，只是失败隔离；
- 每个插件单独 try/catch，失败只影响该插件；设置页显示“加载失败 + 原因”。

### 4.4 兼容性：最关键的是“未知 Kind”

如果插件被卸载 / 加载失败，配置里仍然有 `Kind = "com.example.clock-plus"`。2.13.6 之前的
`CardModel.Normalize` / `CardRegistry.ResolveKind` 会把未知 Kind 回退成 `_modules[0].Kind`：

```csharp
card.Kind = CardRegistry.ResolveKind(card.Kind);   // 旧行为：未知 → _modules[0].Kind
```

这会把插件卡片**静默改成默认卡**，用户配置和 `Extras` 都可能被后续保存覆盖。2.13.6 起已改成：

1. `ResolveKind` 对“非空但未知”的 Kind 不再回退；返回原值；
2. `CardModel.Normalize` 保留未知 Kind，不调用模块元数据；
3. 2.14.0 起，卡片渲染 / 设置页遇到真正未知的外部 Kind 时显示「插件不可用」占位卡（`UnavailableCardModule`），
   不改写 Kind / Content / Extras；
4. 如果插件重新出现，原卡片自动恢复（`Extras` 原样还在）；dev 热重载下放回插件即恢复，生产模式重启恢复；
5. `CardRegistry.Get` 不再“未知就返回第一张卡”，旧的 `metrics` / `clock` / `keys` / `text` 仍按 `ICardLegacyAlias` 找回宿主模块。

这一条比 ALC 技术细节更重要：**插件生态最怕的是卸载插件后配置被宿主改写。**

版本策略：

- `ApiVersion` 整数，宿主硬校验；不兼容直接拒绝加载；
- 契约接口只做“加方法 / 加默认实现”的加法；改变已有签名必须提升 ApiVersion；
- **2.14.4 起不再做类型转发**：2.14.1–2.14.3 曾用宿主 `TypeForwardedTo` 兼容旧二进制，2.14.4 删除；
  引用宿主 `MirrorpondHUD.dll` 的旧插件必须重编译（改引 Contracts / UiKit）并把 `apiVersion` 升到 2；
- 不使用“靠运行时异常猜兼容”；
- 设置页 / 日志里显示插件版本、宿主版本、拒绝原因。

### 4.5 设置与状态

- `CardSettings.Extras` 是 `Dictionary<string, object>`，从磁盘读进来是 JSON 节点；
  `ExtrasAs<T>` 在插件类型可用时才绑定，插件缺失时**不要清空原始节点**；
- `ICardSettingsModule.InitializeCard` / `ICardSettingsValidator` 仍由插件实现；
- 每卡状态继续走 `ICardHost` / `CardStateStore`，路径仍是 `cards/<cardId>/`；
  插件不要自己往这个目录写 DLL；
- 插件 `Kind` / 设置键 / 状态文件名一旦发布就视为稳定 API；
- 设置 schema 变更由插件在 `InitializeCard` 里做迁移；宿主不解释插件字段；
- 插件缺失时设置页只显示“插件不可用”，不要为了渲染编辑器去构造未加载的程序集类型。

### 4.6 UI、线程与 WPF

- 契约含 `FrameworkElement`，插件必须目标 `net8.0-windows` + `UseWPF`；
- 插件 UI 在宿主 UI 线程创建；插件自己起线程要切回 `Dispatcher`；
- 主题 / 字号 / 圆角通过公开契约 `StyleSettings` / `ThemeTokens` / `ApplyStyle` 拿；
  插件不要自己读注册表或硬编码颜色；
- 共享 WPF 控件（`CardButton` / `CardList` / `CardStatus` / `CardHeader` / `CardInput` / `CardTextBox` /
  `CardDropOverlay` / `CliUi`）在 `MirrorpondHUD.UiKit`（2.14.5 起），插件要用时引用该程序集，
  不要把 UiKit DLL 复制进插件目录；
- 插件 XAML 的 `pack://` URI、资源字典必须随插件程序集打包；宿主不要假设能解析插件的
  BAML 资源；
- 插件的异常必须被宿主在创建 / 更新 / 可见性 / 拖放回调处兜底；一个插件崩溃不能拖垮叠层；
- 插件控件不要长期缓存 `CardSettings` / `Settings` 对象（宿主会换新对象），按现有契约
  每次用 `CardRenderContext.Config` / `ApplyStyle` 的新值。

### 4.7 卸载与热重载

2.14.0 的结论（2.14.1–2.14.5 的程序集拆分不改变它）：**生产仍不做热插拔，dev 期只对纯文字卡开放热重载**（`--plugin-dev`）。
ALC 卸载、引用链与实现验收见 [runtime-card-hot-reload.md](runtime-card-hot-reload.md)。

生产边界：

- 插件目录使用 **shadow copy**（复制到 `%LOCALAPPDATA%\MirrorpondHUD\plugin-cache/<id>-<时间戳>-<随机>/`）再加载，
  源 DLL 不再被锁；复制失败回退源目录（此时源 DLL 被锁，热重载可能不可用）；
- 启用 / 禁用 / 更新插件后仍提示“重启生效”，设置页不提供热重载 / 卸载按钮；
- 程序退出时先摘注册表引用、再统一 `Unload` 当前句柄；
- 富内容卡（`IRichCardModule`）不参与热重载。

dev 热重载（`--plugin-dev`，只接受纯文字卡）会：

- 监听 `<exe>/plugins/cards` 与 `%LOCALAPPDATA%\MirrorpondHUD\plugins\cards`，变化 700 ms debounce；
- 先完整加载新模块，任一扫目录有错误就放弃本轮、保留旧模块；
- 拒绝 `IRichCardModule`、`IsInteractive`、`ICardEditorSection`、`ICardLegacyAlias`；
- 换注册表 → 刷新卡片（关闭设置窗并释放编辑器引用、重注册热键）→ 卸载旧 ALC → 延迟 800 ms 做弱引用验证；
- 失败不改写配置、不退出宿主；`settings.json` 的 `Kind` / `Extras` 始终保留。

仍不能整体热重载的原因（针对富内容卡与全量热插拔；与 2.13.6 调研一致）：

- `CardRegistry` 静态引用、WPF 视觉树、`Application.Current.Resources`、事件订阅、
  `DispatcherTimer`、`CardJobQueue`、`ShellIcons` 缓存、后台线程都可能持有插件程序集；
- `AssemblyLoadContext.Unload()` 只是“请求卸载”，有引用就不会真正释放；
- 可收集 ALC 的验证需要 `WeakReference` + 强制 GC，且 WPF 控件很容易让插件“假卸载”；
- 卸载后还要销毁所有卡片窗口、设置编辑器、托盘菜单项，并清理状态。

### 4.8 安全边界

`AssemblyLoadContext` **不是安全沙箱**。插件代码以当前用户权限运行，能读写文件、访问网络、
操作注册表、弹窗、伪造设置页。插件里返回的 WPF 控件也能画任意内容。

2.14.5 现状：影子复制、占位卡、manifest 校验和加载报告都只是**可靠性**措施，不是安全措施；2.14.1–2.14.5 的程序集拆分不改变信任模型；
签名 / 哈希 / 白名单仍未实现，宿主也不校验插件来源。因此仍只建议加载用户明确安装、来源可信的本地插件，
不要把“影子复制 + 清单校验”误解为可以加载不可信代码。

建议的第一版策略：

- 只从**用户明确安装**的本地目录加载，不自动下载；
- 发布方对插件 DLL 签名（Authenticode）或提供 `sha256`，宿主可配置“只加载已签名 / 已列入白名单”的插件；
- 设置页首次加载时显示来源、版本、哈希，让用户确认；
- 插件目录权限限制为当前用户；
- 日志不打印敏感路径 / 内容；
- 如果未来要加载不可信插件：用独立进程 + 受限令牌 + IPC，只支持“数据型卡片”，不要复用
  进程内 `FrameworkElement` 契约。

### 4.9 分发与平台

- 插件目标框架必须与宿主一致：`net8.0-windows`、WPF、x64 / ARM64 对应；
- 原生依赖（Shell Icons、媒体库等）按架构分目录，`AssemblyDependencyResolver` 只解决托管依赖，
  原生依赖需要 `LoadUnmanagedDll` / `NativeLibrary.SetDllImportResolver`；
- 不要启用 trimming / NativeAOT（WPF 与反射契约都不兼容）；
- 插件包建议 zip：`manifest.json` + DLL + 依赖 + README + 哈希；
- 宿主升级时先做“契约版本 + 插件清单”检查，再决定是否加载。

---

## 5. 分阶段落地

| 阶段 | 内容 | 预估 | 退出条件 |
|---|---|---|---|
| P0 spike | 用方案 A 加载一个示例插件；验证类型身份、`CardModuleBase` 实例化、WPF 控件创建；记录 ALC 卸载是否可行 | 1–2 天 | 示例卡片能出现在控制台 / 临时宿主里 |
| P1 契约与注册表 | 决定 A / B；拆 `CardRegistry` 为可初始化快照；加载时序前移；插件目录 + manifest + 失败日志 | 3–5 天 | 坏插件不阻止宿主启动；插件模块进入添加菜单 |
| P2 配置与界面 | 未知 Kind 保留 + 占位卡；插件缺失时保留 Extras；设置页显示插件状态 / 拒绝原因；shadow copy | 3–5 天 | 卸载插件后配置不被改写；重装恢复 |
| P3 安全与发布 | 签名 / 哈希 / 白名单；版本校验；安装 / 卸载文档；x64 / ARM64 测试 | 2–4 天 | 未签名策略可配置；不一致版本被拒绝 |
| P4 热重载（可选） | 收集式 ALC 卸载、窗口 / 编辑器 / 任务清理、WeakReference 验证 | 3–5 天 | 只对纯数据插件先达成；富卡片可不承诺 |
| P5 外部进程（可选） | 数据型插件 IPC；权限隔离 | 大 | 明确产品需要再做 |

**建议先做 P0–P2，不把 P4/P5 写进第一版承诺。** 这样能尽快验证“插件能不能用”，
又不会在 WPF 卸载上无限投入。

截至 2.14.5 的状态：P0 spike 与 P1 契约 / 注册表已在 2.13.6 落地；P1 的「方案 A 还是 B」在 2.14.1 选定 B，
2.14.1–2.14.3 完成 Contracts / Core→Cards / ThemeTokens 拆分，2.14.4 删除类型转发并升插件 API 2，
2.14.5 拆出 UiKit；P2 的未知 Kind 保留、占位卡、shadow copy、加载报告已在 2.14.0 落地
（设置页暂只显示「插件不可用」，不展示插件状态 / 拒绝原因）。
P3 未做：签名 / 哈希 / 白名单、x64 之外的平台验证。P4 只做了 dev-only 纯文字卡热重载（`--plugin-dev`），
富内容卡不承诺；P5 未做。显式引用 UiKit 的临时插件加载验证通过（未固化为示例）。

---

## 6. 验收清单

**加载 / 发现**

- 示例插件出现在添加菜单，Kind 与内置卡片不冲突；
- 插件目录不存在 / 为空时，宿主行为与现在完全一致；
- manifest 缺字段、DLL 缺失、入口类型不存在、构造函数抛异常：只记录该插件失败，宿主正常；
- 两个插件声明同一个 Kind：后者被拒绝，错误可读。

**配置 / 状态**

- 插件卡片写入 `settings.json` 后，卸载插件：`Kind` 不被改写，`Extras` 不丢；
- 重新安装插件：卡片恢复，设置与状态文件仍可读；
- 插件升级 `apiVersion` 不匹配：拒绝加载并保留配置；
- 插件设置 schema 变更：由插件迁移，失败时回退默认值且不破坏原始 JSON。

**UI / 生命周期**

- 插件富卡片能创建 WPF 控件，主题 / 字号变化能走 `ApplyStyle`；
- 插件异常不会让叠层崩溃；
- 禁用插件后重启：卡片不出现在添加菜单，已有卡片显示“插件不可用”；
- dev 热重载（`--plugin-dev`）：卸载后旧 `WeakReference` 最终不再存活（允许上下文延迟回收），且没有残留窗口 / 定时器 / 任务。

**安全 / 分发**

- 未签名插件在“仅签名”模式下被拒绝；
- 插件目录不可写时给出明确提示；
- x64 / ARM64 各自的原生依赖不会被错误加载。

---

## 7. 未决问题（需要产品拍板）

1. **信任模型**：只支持用户手动安装的可信插件，还是要做签名 / 白名单？如果要加载不可信代码，
   是否接受独立进程 + 数据型卡片的限制？
2. **契约边界**（2.14.5 已完成）：选择 B（独立 `MirrorpondHUD.Contracts` + `MirrorpondHUD.UiKit`）；
   2.14.1 拆 Contracts、2.14.2 拆 Core→Cards、2.14.3 迁 ThemeTokens、2.14.4 删类型转发升 API 2、2.14.5 拆 UiKit。
   剩余问题是插件 API 版本迁移指引与 UiKit 插件端到端验证。
3. **富卡片是否对插件开放**：如果只开放文字卡，契约可以小很多；如果开放 `IRichCardModule`，
   WPF、主题、卸载都会复杂。
4. **插件目录**（2.14.0 已定）：便携 `<exe>/plugins/cards` 与用户级 `%LOCALAPPDATA%\MirrorpondHUD\plugins\cards`
   两个根目录都会扫描；影子缓存固定在 `%LOCALAPPDATA%\MirrorpondHUD\plugin-cache`。
5. **热重载**（2.14.0 已定）：生产保持“替换 DLL 后重启生效”；开发期 `--plugin-dev` 只对纯文字卡热重载，
   不进入正式 UI。富内容卡的热插拔仍未承诺。
6. **版本策略**：`apiVersion` 整数是否够；是否还要 `minHostVersion` / `maxHostVersion`；
   插件市场是否需要签名链。
7. **配置兼容**：未知 Kind 的占位卡是否允许用户“移除 / 禁用 / 重新定位插件”，还是只读展示。

---

## 8. 参考

### 代码

- 注册与发现：`src/Cards/CardRegistry.cs`
- 契约程序集：`src/Contracts/MirrorpondHUD.Contracts.csproj`；契约源码 `src/Contracts/Cards/`、`src/Contracts/Core/`、`src/Contracts/Ui/`、`src/Contracts/Metrics/`、`src/Contracts/Text/`
- 共享控件程序集（2.14.5 起）：`src/UiKit/MirrorpondHUD.UiKit.csproj`、`src/UiKit/CliUi.cs`（`CardPalette`）、`src/UiKit/CardButton.cs` 等 8 个文件
- 契约：`src/Contracts/Cards/CardModule.cs`、`src/Contracts/Cards/RichCard.cs`、`src/Contracts/Cards/CardSettingsModule.cs`、`src/Contracts/Cards/CardHost.cs`
- 类型转发：2.14.1–2.14.3 用 `src/TypeForwards.cs`，**2.14.4 已删除**（不再兼容引用宿主的旧二进制）
- 主题令牌（2.14.3 起在 Contracts）：`src/Contracts/Ui/ThemeTokens.cs`
- 配置归一化：`src/Cards/CardModel.cs`、`src/Core/ConfigStore.cs`（`SettingsNormalizer` 注入在 `src/App.xaml.cs`）
- 设置与状态：`src/Contracts/Core/Settings.cs`（`CardSettings.Extras`）、`src/Core/CardStateStore.cs`
- 插件加载：`src/Cards/CardPluginLoader.cs`（宿主 / Contracts / UiKit 走默认 ALC）
- 设置页装配：`src/Ui/SettingsWindow.xaml.cs`
- 既有评审：[modularity-review.md](modularity-review.md)（单程序集 / 命名空间环 / 静态注册表）

### 外部资料

- [Understand AssemblyLoadContext](https://learn.microsoft.com/dotnet/core/dependency-loading/understanding-assemblyloadcontext)
- [Create a .NET Core application with plugins](https://learn.microsoft.com/dotnet/core/tutorials/creating-app-with-plugin-support)
- [Collectible AssemblyLoadContext and unloadability](https://learn.microsoft.com/dotnet/standard/assembly/unloadability)
- [AssemblyDependencyResolver](https://learn.microsoft.com/dotnet/api/system.runtime.loader.assemblydependencyresolver)
- [Inspect assembly contents using MetadataLoadContext](https://learn.microsoft.com/dotnet/standard/assembly/inspect-contents-using-metadataloadcontext)
- [Pack URIs in WPF](https://learn.microsoft.com/dotnet/desktop/wpf/app-development/pack-uris-in-wpf)
- [System.Composition (MEF 2)](https://learn.microsoft.com/dotnet/standard/mef/)

---

## 9. 一句话总结

2.14.5 已按「可信插件 + 重启生效 + Contracts / UiKit 双程序集」把运行时扩展卡片做成生产路径：
2.14.1–2.14.3 拆出 `MirrorpondHUD.Contracts` 并迁移 ThemeTokens，2.14.4 删除旧插件类型转发、插件 API 升到 2，
2.14.5 拆出 `MirrorpondHUD.UiKit`（8 个共享 WPF 控件）；影子复制、占位卡、manifest / Kind 校验与加载报告
让坏插件只影响自己；`--plugin-dev` 的纯文字卡热重载只是开发者工具。富内容卡热插拔、沙箱、签名 / 白名单仍未做，
UiKit 插件加载已用临时插件验证通过。`AssemblyLoadContext` 只解决加载与依赖隔离，不解决沙箱、卸载和契约演进。
