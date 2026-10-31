# 运行时扩展卡片调研（`plugins/cards/*.dll`）

> 状态：调研 / 设计提案，未实现。
> 基线：v2.12.2（文件级代码审计 + 现有模块化评审结论）。
> 结论先行：技术上可做，但**不能直接把 DLL 丢进一个目录就加载**。当前的单程序集、
> 静态 `CardRegistry`、以及“未知 Kind 回退到默认卡”的归一化策略，会分别带来
> 契约版本、加载时序和配置丢失三个问题。建议先拆契约与加载时序，把插件当**可信本地扩展**，
> 第一版只支持“重启生效”，不要先做热插拔或沙箱。

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

- 不承诺热插拔：替换 DLL 后要求重启；
- 不把 `AssemblyLoadContext` 当沙箱；不加载来路不明的插件；
- 不做插件市场 / 在线下载 / 自动更新；
- 不把现有卡片改成独立进程；
- 不改变 `cards/<cardId>` 这个状态目录的现有语义（它是**状态**，不是插件目录）。

---

## 2. 现状审计（代码证据）

| 事实 | 代码位置 | 对运行时插件的含义 |
|---|---|---|
| 单程序集：契约、所有卡片实现、宿主、WPF 控件都在 `MirrorpondHUD.dll` | `MirrorpondHUD.csproj`（唯一项目） | 没有“只给插件看的契约程序集”；插件要么引用整个宿主，要么先拆契约 |
| 注册表是静态只读列表，第一次使用时初始化 | `src/Cards/CardRegistry.cs:15` `static readonly List<ICardModule> _modules = Discover();` | 插件必须在 `CardRegistry` 第一次被访问前加载；运行中无法追加 / 卸载 |
| 发现只扫当前程序集、要求 public 无参构造 | `src/Cards/CardRegistry.cs:81-101` | 不会扫目录；加载失败只写日志，不会出现在 UI |
| `Modules` 暴露的是可变 `IList` | `src/Cards/CardRegistry.cs:19` | 插件 / 调用方可以改内部列表；需要改成不可变快照 |
| 未知 Kind 会回退到 `_modules[0]` | `src/Cards/CardRegistry.cs:21-38`、`src/Cards/CardModel.cs:43` | 插件缺失时，配置里的 Kind 会被**改写成默认卡**，属于数据破坏 |
| 归一化在 Core 层调用 Cards 层 | `src/Core/ConfigStore.cs:4`、`:69-70` 调 `CardModel.Normalize` | “Core 不认识卡片”已经破例；拆契约前必须先解开这个环 |
| 卡片设置在 `Extras` 里按模块类型绑定 | `src/Core/Settings.cs:721-756`、`Json.BindNode<T>` | 插件类型不在时无法绑定；原始 JSON 必须原样保留，不能丢 |
| 状态目录是 `<settings.json 所在目录>/cards/<cardId>` | `src/Core/CardStateStore.cs:18-25` | 与 README 里的 `cards/*.dll` 命名冲突；插件目录应另起 `plugins/` |
| 添加卡片菜单直接遍历静态 `CardRegistry.Modules` | `src/Ui/SettingsWindow.xaml.cs:680-730` | 需要“加载失败 / 不兼容”的列表项与原因，而不是静默消失 |
| 富内容契约直接出现 `FrameworkElement` | `src/Cards/RichCard.cs:12-53` | 契约程序集必须面向 WPF（`net8.0-windows` + `UseWPF`），插件必须能在 UI/STA 线程创建控件 |
| 契约与实现没有 ApiVersion | `src/Cards/CardModule.cs` 全文 | 二进制不兼容时只能靠程序集版本 / 类型加载失败来发现 |
| README 明确写“运行期插件暂未实现” | `README.md:281`、`README.md:416` | 本文是补这缺口的前置调研 |

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

当前契约（`ICardModule`、`IRichCardModule`、`ICardSettingsModule` 等）和所有 WPF 控件都在
`MirrorpondHUD.dll` 里。有三种切法：

#### 方案 A：宿主程序集就是契约（第一版可用）

- 插件直接引用 `MirrorpondHUD.dll`，实现 `CardModuleBase`；
- 插件 ALC 对 `MirrorpondHUD` 程序集名返回 `null`，强制回落到默认 ALC，保证
  `typeof(ICardModule)` 是同一个 Type；
- 要求插件与宿主**精确版本匹配**（或至少同 ApiVersion）；
- 插件能直接使用 `CardButton`、`CardList`、`CardPalette` 等公开控件，开发体验最好；
- 缺点：契约 = 整个宿主公开面；宿主内部重构可能破坏插件；无法支持多个宿主版本。

**这是最短路径的 spike 方案，也是第一版可接受的折中。**

#### 方案 B：独立 `MirrorpondHUD.Contracts`（长期推荐）

把下面这些东西搬到独立程序集（命名空间可以保持不变，减少源码改动）：

- `ICardModule` / `IRichCardModule` / `CardModuleBase` / `RichCardModuleBase`；
- `ICardSettingsModule` / `ICardEditorSection` / `ICardSettingsValidator` / `ICardSummary`；
- `ICardStyleAware` / `ICardDropTarget` / `ICardDropFeedback` / `ICardHotkeySource/Target`；
- `ICardHost` / `ICardHostAware` / `ICardActivation`；
- `CardRenderContext` 以及它依赖的 DTO：`Settings` / `MetricsSnapshot` / `KeyHistory` /
  `CardSettings`（或把它们抽成只读接口 + 可序列化 DTO）；
- `CardHotkeyAction`、`ThemeTokens`、`StyleSettings` 等插件确实需要的公开类型。

宿主和插件都引用 `MirrorpondHUD.Contracts`；插件 ALC 对 contracts 程序集名返回 `null`，
由默认 ALC 提供，保证类型身份一致。

代价：

- `ConfigStore` 不能再直接调 `CardModel.Normalize`（Core → Cards 的环要拆）；
- WPF 控件（`CardButton` 等）要么留在宿主、插件额外引用宿主（回到 A），要么再抽一个
  `MirrorpondHUD.UiKit` 给插件共用；
- 这是一次真正的程序集重构，预估不是一两个下午能收口的。

#### 方案 C：只给“数据型插件”的窄接口

插件只返回字符串 / 简单图形 / 数据，不返回 `FrameworkElement`；这样契约可以很小，
甚至可以放到独立进程。但它无法复用现有富卡片能力，只能作为另一条产品线。

**建议：** 第一版用 A 做通链路；等插件生态出现、或宿主需要独立升级时，再做 B。
不要一开始就为了“纯粹”把 UiKit 拆散。

### 4.2 插件目录与清单

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
  "apiVersion": 1,
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

    public CardPluginLoadContext(string mainPath, string hostAssemblyName)
        : base("MirrorpondHUD.CardPlugin:" + Path.GetFileNameWithoutExtension(mainPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainPath);
        _hostAssemblyName = hostAssemblyName;
    }

    protected override Assembly Load(AssemblyName name)
    {
        // 契约 / 宿主必须由默认 ALC 提供，不能在插件 ALC 里再加载一份
        if (string.Equals(name.Name, _hostAssemblyName, StringComparison.OrdinalIgnoreCase))
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
- 插件自己的依赖走插件 ALC；契约 / 宿主走默认 ALC；
- 加载前用 `MetadataLoadContext` 做一次只读预检（类型名、引用、ApiVersion），可以避免
  一上来就把坏 DLL 执行起来；预检不是安全边界，只是失败隔离；
- 每个插件单独 try/catch，失败只影响该插件；设置页显示“加载失败 + 原因”。

### 4.4 兼容性：最关键的是“未知 Kind”

如果插件被卸载 / 加载失败，配置里仍然有 `Kind = "com.example.clock-plus"`。当前
`CardModel.Normalize` 会：

```csharp
card.Kind = CardRegistry.ResolveKind(card.Kind);   // 未知 → _modules[0].Kind
```

这会把插件卡片**静默改成默认卡**，用户配置和 `Extras` 都可能被后续保存覆盖。运行时插件
必须先改这条规则：

1. `ResolveKind` 对“非空但未知”的 Kind 不再回退；返回原值并让调用方处理；
2. `CardModel.Normalize` 保留未知 Kind，不调用模块元数据（不知道 `SupportsContent` 等）；
3. 卡片渲染 / 设置页遇到未知模块时显示“插件缺失 / 已禁用”的占位卡，允许用户移除或禁用，
   但不改写 Kind；
4. 如果插件重新出现，原卡片自动恢复（`Extras` 原样还在）；
5. `CardRegistry.Get` 不再“未知就返回第一张卡”；改用 `TryGet`，调用方显式处理 null。

这一条比 ALC 技术细节更重要：**插件生态最怕的是卸载插件后配置被宿主改写。**

版本策略：

- `ApiVersion` 整数，宿主硬校验；不兼容直接拒绝加载；
- 契约接口只做“加方法 / 加默认实现”的加法；改变已有签名必须提升 ApiVersion；
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
- 主题 / 字号 / 圆角通过宿主公开的 `StyleSettings` / `ThemeTokens` / `ApplyStyle` 契约拿；
  插件不要自己读注册表或硬编码颜色；
- 插件 XAML 的 `pack://` URI、资源字典必须随插件程序集打包；宿主不要假设能解析插件的
  BAML 资源；
- 插件的异常必须被宿主在创建 / 更新 / 可见性 / 拖放回调处兜底；一个插件崩溃不能拖垮叠层；
- 插件控件不要长期缓存 `CardSettings` / `Settings` 对象（宿主会换新对象），按现有契约
  每次用 `CardRenderContext.Config` / `ApplyStyle` 的新值。

### 4.7 卸载与热重载

第一版**不做**。原因：

- `CardRegistry` 静态引用、WPF 视觉树、`Application.Current.Resources`、事件订阅、
  `DispatcherTimer`、`CardJobQueue`、`ShellIcons` 缓存、后台线程都可能持有插件程序集；
- `AssemblyLoadContext.Unload()` 只是“请求卸载”，有引用就不会真正释放；
- 可收集 ALC 的验证需要 `WeakReference` + 强制 GC，且 WPF 控件很容易让插件“假卸载”；
- 卸载后还要销毁所有卡片窗口、设置编辑器、托盘菜单项，并清理状态。

第一版做法：

- 插件目录使用 **shadow copy**（复制到 `plugins-cache/<hash>/`）再加载，避免 DLL 被锁；
- 启用 / 禁用 / 更新插件后提示“重启生效”；
- 程序退出时统一释放；
- 真要做热重载，先写一个只加载 / 卸载“纯数据插件”的 spike，再评估 WPF 富卡片是否值得。

### 4.8 安全边界

`AssemblyLoadContext` **不是安全沙箱**。插件代码以当前用户权限运行，能读写文件、访问网络、
操作注册表、弹窗、伪造设置页。插件里返回的 WPF 控件也能画任意内容。

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
- 如实现卸载：卸载后 `WeakReference` 不再存活，且没有残留窗口 / 定时器 / 任务。

**安全 / 分发**

- 未签名插件在“仅签名”模式下被拒绝；
- 插件目录不可写时给出明确提示；
- x64 / ARM64 各自的原生依赖不会被错误加载。

---

## 7. 未决问题（需要产品拍板）

1. **信任模型**：只支持用户手动安装的可信插件，还是要做签名 / 白名单？如果要加载不可信代码，
   是否接受独立进程 + 数据型卡片的限制？
2. **契约边界**：第一版走 A（宿主即契约）还是直接做 B（独立 Contracts）？A 快但锁版本；
   B 干净但要拆 `ConfigStore` / UiKit。
3. **富卡片是否对插件开放**：如果只开放文字卡，契约可以小很多；如果开放 `IRichCardModule`，
   WPF、主题、卸载都会复杂。
4. **插件目录**：`<exe>/plugins/cards` 还是 `%LOCALAPPDATA%`？便携版和安装版是否各一套？
5. **热重载**：是否真的需要“替换 DLL 不重启”？如果只是开发期需要，可以只给开发者一个
   `--plugin-dev` 参数，不进入正式功能。
6. **版本策略**：`apiVersion` 整数是否够；是否还要 `minHostVersion` / `maxHostVersion`；
   插件市场是否需要签名链。
7. **配置兼容**：未知 Kind 的占位卡是否允许用户“移除 / 禁用 / 重新定位插件”，还是只读展示。

---

## 8. 参考

### 代码

- 注册与发现：`src/Cards/CardRegistry.cs`
- 契约：`src/Cards/CardModule.cs`、`src/Cards/RichCard.cs`、`src/Cards/CardSettingsModule.cs`、`src/Cards/CardHost.cs`
- 配置归一化：`src/Cards/CardModel.cs`、`src/Core/ConfigStore.cs`
- 设置与状态：`src/Core/Settings.cs`（`CardSettings.Extras`）、`src/Core/CardStateStore.cs`
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

运行时扩展卡片**值得做**，但第一版应该是“可信插件 + 重启生效 + 宿主版本锁定”，
并且先修 `Unknown Kind` 的配置保留问题；`AssemblyLoadContext` 只解决加载与依赖隔离，
不解决沙箱、卸载和契约演进。先把契约与配置兼容做对，再谈热插拔。
