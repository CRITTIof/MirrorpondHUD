# 模块化现状评审与优化清单

> 范围：只审「可扩展结构」——新增一种卡片、新增一种设置项、新增一个指标、替换一种毛玻璃实现分别要动哪里；
> 评审部分不改代码；实际落地的改动与验证状态记在 §8。基线 v2.4.5（`f4b2b8b`），`src/` 下 55 个 `.cs` + 4 个 XAML，约 2.08 万行。
> 结论一句话：**契约层够用，宿主层有 5 处可直接修的问题，真正的扩展瓶颈在「设置模型 + 设置页」**——
> 新增一张带设置项的卡片要改 5 个代码文件，其中 3 个是 700 ~ 1500 行的巨型文件。
> 读法：P0 是能立刻动手的小修（不改架构），P1 是需要设计的重构，P2 是可选结构优化。
> **实施状态：P0-1 ~ P0-4 已提交（v2.5.0）；P1-1、P1-2、P1-3、P1-8、P1-11、P1-12 与拖放通道已实现（v2.6.1 ~ v2.7.0）；P0-5 暂缓；手工回归待做。**

## 0. 评审方法

| 做法 | 说明 |
|---|---|
| 通读 | `Cards/` 全部模块、`Ui/OverlayController.cs`（宿主，1396 行）、`Ui/CardEditor.cs`、`Ui/SettingsWindow.xaml(.cs)`、`Core/Settings.cs`、`Core/SettingsMigration.cs`、`Ui/OverlayWindow.xaml.cs` 的玻璃部分 |
| 交叉验证 | `grep` 统计按 Kind / 按具体类型的判断；用脚本比对 `Text/Variables.cs` 与 `Ui/Catalog.cs` 的键集合；`git log` 对照历史上因结构问题产生的修复提交 |
| 未做 | 运行时性能剖析。文中性能数字都标注为「代码级估算」，给的是数量级与依据，不是实测 |

---

## 1. 现在的结构

### 1.1 分层与依赖方向

箭头 = 该层引用了它指向的命名空间（统计自 `using` 语句）：

```
Cards   → Core / Ui.Controls / Media
Ui      → Core / Cards / Media / Ui.Controls
Core    → Cards            ← ConfigStore 调 CardModel.Normalize
Media   → Core / Ui
Metrics → （无）
Text    → （无）
Input   → （无）
```

`Metrics` / `Text` / `Input` 是干净的叶子层；`Core`、`Cards`、`Ui`、`Media` 之间构成一个环
（`Cards → Ui`、`Ui → Cards`、`Core → Cards → Core`、`Media → Ui → Media`）。
这是「运行期插件 `cards/*.dll` 暂未实现」（README §已知问题）的结构性原因：契约与实现没分程序集，
`CardRegistry` 也只扫描 `typeof(CardRegistry).Assembly`。

### 1.2 现有的扩展点

全项目只有 5 个接口，其中 4 个属于卡片体系：

| 扩展点 | 位置 | 是否「加一个类即可」 |
|---|---|---|
| 卡片模块注册 | `src/Cards/CardRegistry.cs:52` 反射扫描本程序集 | ✅ 是 |
| 富内容（任意 WPF 内容） | `src/Cards/RichCard.cs:12` `IRichCardModule` | ✅ 是（无设置项时） |
| 「添加卡片」菜单 / 默认内容 / 热键页分组 | `src/Ui/SettingsWindow.xaml.cs:662`、`1270` 遍历 `CardRegistry.Modules` | ✅ 是 |
| 卡片自己的通用热键 | `src/Cards/CardModule.cs:110` `ICardHotkeySource` / `ICardHotkeyTarget` | ✅ 是（粒度见 P1-6） |
| 卡片自己的设置项 | 无 | ❌ 否，见 P1-1 |
| 毛玻璃实现 | 无接口，`OverlayWindow` 里按字符串分派，见 `src/Ui/OverlayWindow.xaml.cs:365-472` | ❌ 否，见 P2-1 |
| 采样指标 / 模板变量 | 无接口，`MetricEngine` 写死 + `Variables` 手写 | ❌ 否，见 P2-3 |

### 1.3 「新增一张带设置项的卡片」实际要动的地方

| 文件 | 改什么 | 规模 |
|---|---|---|
| `src/Cards/NewCard.cs` | 模块本体 | 新增 1 个文件 |
| `src/Core/Settings.cs` | 新增 `NewCardSettings` 类、`CardSettings` 加字段、`Normalize` 里夹紧 | 811 行 |
| `src/Cards/CardModel.cs` | `Normalize` 里加该子对象的 `Normalize()` 调用（参照 `:59` `:62` `:65`） | 152 行 |
| `src/Ui/CardEditor.cs` | 设置页 view-model 属性 + `Visibility`（参照 `:373` `:599`） | 775 行 |
| `src/Ui/SettingsWindow.xaml` | 设置区块 + 面板可见性（参照 `:190` `:264` `:360`） | 1021 行 |
| 可选 | `Core/SettingsMigration.cs`（旧配置迁移）、`docs/cards-architecture.md`、README | — |

对照：**无设置项的文字卡只改 1 个文件**。这就是「模块化」与文档承诺之间的落差所在。

---

## 2. 值得保留的设计

1. **注册表驱动、没有 `switch(kind)`**：添加菜单、默认内容、热键分组、设置页列表全部遍历 `CardRegistry.Modules`，
   宿主里查不到按 Kind 的分支（唯一例外见 P1-3）。
2. **富内容契约把「窗口」留在宿主**：`IRichCardModule` 只返回 `FrameworkElement`，卡片不碰窗口、不碰叠层位置，
   `OnVisibilityChanged` / `OnZOrderChanged` 处理播放与子窗口层级。方向正确。
3. **面板公共件抽得干净**：`src/Ui/Controls/CliUi.cs` 把命令行卡与 AI 卡共用的 `CliPanelView` / `CliEntryPanel` /
   `CliScrollBars` / `CardPalette` 收在一起（513 行），两张卡各 187 / 478 行，没有复制面板骨架。
4. **配置序列化是反射式的**：`Core/Json.cs` 按公共字段读写，加设置字段不用改序列化代码（只需在 `Normalize` 里夹紧）。

---

## 3. P0：宿主侧的具体问题（建议先修，不动架构）

五条都在 `Ui/OverlayController.cs` 里，除了 P0-2 需要动 `MediaView`。

### P0-1 命令行一打开，其他常驻卡片停止刷新

- **现象**：命令行 / 交互卡打开后，时钟卡、CPU 卡的数字冻结，直到命令行收起。
- **证据**：`src/Ui/OverlayController.cs:581` —— `RenderNow()` 开头 `if (_commandMode) return;`，
  而循环里本来就有 `if (!slot.Shown || slot.Module.IsInteractive) continue;`（`:585`），
  这一层全局栅栏是多余的。
- **影响**：可见的功能缺陷。两张卡片同时显示时，交互卡一开，另一张就定格。
- **改法**：删掉全局 `return`，只靠循环内的 `IsInteractive` 跳过交互卡自己。
- **量级**：1 行；风险低（交互卡本来就走富内容，不参与文字刷新）。

### P0-2 `UpdateContent` 是从未接线的死契约

- **现象**：`IRichCardModule.UpdateContent`（`src/Cards/RichCard.cs:25`）声明为「配置没变、只想刷新数据时调用」，
  `MediaCard` 也实现了（`src/Cards/MediaCard.cs:57`），但宿主**从未调用**——全项目只有定义和实现两处。
- **影响**：
  1. 模块作者看到契约会照着实现一个永远不会执行的方法（现在 `MediaCard` 就是这样）；
  2. 所有设置变化都只能走「销毁 + 重建」，而重建会丢运行时状态 —— 这正是 P0-3 的根因；
  3. 文档（`docs/cards-architecture.md` §2）已经写了这个行为，属于文档与实现不一致。
- **改法**（两步，缺一不可）：
  1. 宿主：内容相关设置未变时调 `UpdateContent`，变了才 `ReleaseContent` + `CreateContent`；
  2. `MediaView.Bind`：同路径重绑时保留用户暂停状态。现在 `:306` 无条件 `_playing = _settings.Autoplay`，
     而 `ApplyVideoSettings`（`:651`）只在 `Autoplay` 属性变化时才真的 Play/Pause，
     于是「用户按热键暂停 → 改任意设置 → `_playing` 变回 true，但视频窗还停着」状态不一致。
- **量级**：宿主约 20 行 + `MediaView` 约 5 行；风险中（要确认哪些字段算「内容相关」）。

### P0-3 每次应用设置都无条件重建全部富内容

- **现象**：设置页按「应用」时，所有已存在的富内容卡片都被 `ReleaseContent` 后重建——改字体、改名字、改热键也会重开视频、重新解码图片。
- **证据**：`src/Ui/OverlayController.cs:1010` —— `SyncCards` 对每张已存在的卡片先 `ReleaseContent(slot)`，
  再 `ShowCard` → `EnsureContent` → `CreateContent`。
- **影响**：应用设置的耗时可感知（视频要重启 ffmpeg、重新贴子窗口，图片要重新解码）；
  AI 对话卡的历史记录被清空（文档已写明「上下文只在内存里，卡片隐藏、重建或重启就清空」，但改字幕颜色不该触发它）。
- **改法**：依赖 P0-2。先做字段级比对（`Kind` / `Command` / `Media` / `Chat` 是否变），
  只重建真正变化的卡片；其余调 `UpdateContent`。
- **量级**：约 40 行；风险中（比对不全会漏刷新，建议给每张卡加「内容签名」字符串）。

### P0-4 每张卡片持有一份完整 Settings 的 JSON 深拷贝

- **现象**：`CardSlot.Dialog` 是整份配置的深拷贝（`BuildDialog` → `CloneSettings` → `Json.Serialize` + `Deserialize`），
  每张卡一份；启动建 n 份、每次应用设置重建 n 份。
- **证据**：`src/Ui/OverlayController.cs:146`（创建）、`:176-192`（克隆实现）、`:1007`（应用时重建）。
- **影响**：内存与耗时随卡片数线性增长（含 `Cards` 列表本身也被复制），且克隆绕过了 `Normalize`，
  两份配置容易再次漂移。目前卡片不多（默认 5 张）所以还能忍受。
- **改法**：窗口只需要 style / overlay 相关字段，传一个不可变快照即可；或至少改为浅拷贝 + 共享引用。
- **量级**：约 30 行；风险中（窗口读 `_settings` 的地方要逐个确认）。

### P0-5 每帧每卡新建渲染上下文，变量表重复构建

- **现象**：`RenderNow` 每 120 ms 遍历一次卡片，每张卡新建 `CardRenderContext` 并各自构建变量表（48 个键）；
  `CardModule.cs:46` 的注释说「同一帧内的多张卡片可以复用」，实际没有复用。
- **证据**：`src/Ui/OverlayController.cs:113`（120 ms 定时器）、`:204`（`RenderCardText` 里新建 context）、
  `src/Text/Variables.cs:20`（每次调用都重建整张表，含 `DateTime.Now` 与 `GetTickCount64`）。
- **影响**：
  1. 一致性：同一帧内各卡片的 `{time}` / `{uptime}` 各取各的时间，跨秒边界时两张卡可能差 1 秒（概率低但存在）；
  2. 浪费：6 张卡时约 50 次/秒的 48 键字典构建 + 约 20 次字符串格式化，全部在 UI 线程。
     **这是代码级估算，量级远小于一次文本重排，不是卡顿源**；真正的收益是下面的结构性解锁；
  3. 挡住了「脏检查 + 按卡刷新节奏」：现在 8.3 Hz 全量重渲染，文本没变时靠 `TextCanvas.Text` 的字符串比较空转
     （`src/Ui/TextCanvas.cs:115`）。
- **改法**：一帧构建一次变量表，`CardRenderContext` 加一个共享变量表的构造重载，各卡只换 `Config`；
  变量表只读（必要时包只读视图），避免模块改写串卡。顺手给模块加可选的 `RefreshIntervalMs`，为 P1 铺路。
- **量级**：约 40 行；风险低（行为除了时间一致性外不变）。

---

## 4. P1：真正的扩展性瓶颈（需要设计）

### P1-1 `CardSettings` 是胖聚合，没有「模块自己的设置」（已实施，schema 5）

`src/Core/Settings.cs:634` 的 `CardSettings` 固定塞了 `Command` / `Media` / `Chat` / `Preset` 四个具体子对象，
都属于特定模块。后果：新模块的设置无处安放，只能继续往这个类加字段，并且同时被
`CardModel.Normalize`（`:59-65`）、`CardEditor`、`SettingsWindow.xaml` 三处感知。

**已做**：`CardSettings.Command` 删除，改为 `Extras`（`Dictionary<string, object>`）+ `ExtrasAs<T>` / `SetExtras<T>`；
命令行卡的设置搬进 `Extras.command`（v4 → v5 迁移），`CardModel` 改为调用模块的 `ICardSettingsModule.InitializeCard`。
`Media` / `Chat` 仍保留 typed 字段，按同一模式逐个迁移（有 `SettingsMigration` 兜底）。
`Extras` 不参与 `Normalize` 的夹紧，由模块自己负责——这是「谁的设置谁管」的边界。

### P1-2 设置页按 Kind 硬编码（已实施，命令行卡试点）

`src/Ui/CardEditor.cs`（775 行）的 view-model 同时装着媒体卡 20 个属性、AI 卡 10 个属性，
XAML 用三块 `Visibility` 面板切换（`src/Ui/SettingsWindow.xaml:190` / `:264` / `:360`），
`CardEditor.cs:111` / `:375` / `:599` 还按 `Kind == "media"` / `"chat"` 判断。

**已做**：`ICardEditorSection`（模块自带设置区块）+ `ICardSettingsValidator`（模块自带校验）。
命令行卡的 8 个属性与 70 行 XAML 面板删掉，改由 `src/Cards/CommandCardEditor.cs` 提供，宿主只留一个 `ContentControl`。
`CardEditor` 现在只画通用字段；媒体 / 对话面板在它们迁移到 `Extras` 时按同一方式移出。

### P1-3 模型层认识具体模块（已实施，v2.6.1）

`src/Cards/CardModel.cs` 原来有 `if (module is InfoTextCard)`、硬编码的 legacy Kind 名单
（metrics / clock / keys / text），以及三个子对象的 `Normalize()` 调用。

**已做**：新增 `ICardLegacyAlias`——模块自己声明接管过哪些旧 Kind，`CardRegistry.TryResolveAlias` 统一解析；
预设反推搬进 `InfoTextCard.InitializeCard`。`CardModel` 现在只剩 Id / 名称 / 间距 / 常驻这些通用规则，
不再认识任何具体模块。（`Media` / `Chat` 的 typed 归一化随 P1-1 的后续迁移一起下沉，见 §8.2。）

### P1-4 截图 / 文档生成认识具体实现

`src/Ui/Screenshots.cs:134` 用 `view as MediaView`，`:143` 用 `Kind == "command"` 决定样张内容，
`:140` 用 `view as CliPanelView`。宿主外围逻辑耦合了具体卡片实现。

**建议**：契约加 `PrepareForSnapshot()` / `CreateDemoContent()`，由模块自己决定「怎么展示、示例内容是什么」。

### P1-5 注册表降级太静默

`src/Cards/CardRegistry.cs:24`：未知 Kind 一律退回 `_modules[0]`（模块为空时还会越界）；
`BuildLookup`（`:41`）重复 Kind 静默覆盖。降级到 infotext 时原 `Content` 仍被保留并渲染，
所以「配置来自更新版本 / 模块被移除」会显示一张语义错乱的卡，而不是一张「未知卡片」。

**建议**：重复 Kind 记 Warning（并在添加菜单里去重）；未知 Kind 保留原值、渲染占位卡；
`Get` 返回 null 而不是猜一个。

### P1-6 热键动作是「按 Kind 全局」的

`src/Cards/MediaCard.cs:20` 的 `media.play-pause` 登记为「对所有媒体卡生效」，宿主在 `OverlayController.cs:1282` 遍历所有同类卡片逐个执行，
无法绑定到某一具体卡片，也无法让两张媒体卡用不同绑定；模块动作名与 `card:<id>` 混在同一个
`Hotkeys.CardActions` 列表（`Settings.cs:425`）里，没有跨模块撞名检查。

**建议**：动作名强制 `kind.` 前缀并校验；如需更细粒度，再引入 `card:<id>:action`。

### P1-7 变量表有两个真相源，且已经漂移

代码里 48 个变量，设置页提示清单 `src/Ui/Catalog.cs:25` 只有 35 个。
**16 个变量在设置页查不到**：全部 `*_icon`（`cpu_icon` / `mem_icon` / `ram_icon` / `rom_icon` / `disk_icon` /
`net_down_icon` / `net_up_icon` / `gpu_icon`）、`net_down_bps` / `net_up_bps` / `disk_read_bps` / `disk_write_bps` /
`net_total` / `mem_free` / `vram_pct` / `gpu_util`。（`Catalog` 里另有 `free_C` 等按盘符动态生成的条目，不算漂移。）

**建议**：一张「变量声明表」（名字 + 说明 + 取值器），`Catalog` 与 `Variables.Build` 都由它生成，
并加一条断言键集合一致的测试。这也顺带解决 P2-3。

### P1-8 模块没有生命周期与宿主服务（已实施，v2.7.0）

注册表里模块是单例，`CreateContent` 之外拿不到日志、配置、重绘请求、定时器；
需要状态的模块只能把状态挂在 `FrameworkElement` 上（`CommandView` 就是这样）。

**已做**：`ICardHost`（每张卡一份：`Config` / `StateDirectory` / 状态读写 / `RequestRender` / `RequestLayout`）
+ `ICardHostAware`（建卡与应用设置时 `Attach`，删卡与退出时 `Detach`）。
模块仍是单例，接口注释里明确「不能把 host 存进字段」。第一个消费者是中转站 / 画板。

### P1-9 `InfoTextCard` 内部又造了一套「子种类」

`src/Cards/InfoTextCard.cs:29` 用 `CardSettings.Preset` 在一种模块里装下 4 种子种类
（metrics / clock / keys / text，各带默认内容与图标），于是项目里并存「Kind 扩展」与「Preset 扩展」两套模型。

**建议**：要么拆回 4 个模块（用 `SettingsMigration` 做 Kind 映射，配置兼容），
要么把「变体」提升成契约能力（`ICardVariantSource`）并在设置页统一渲染。否则第三种卡片会再发明一遍。

### P1-10 分层有环，模块抽不出去

见 §1.1。`Core → Cards`（`ConfigStore.Load` 调 `CardModel.Normalize`）与 `Cards → Ui.Controls` 是最该先拆的两条。

**建议**：先拆 `Cards.Abstractions`（契约 + `CardSettings` + `Variables` 接口），
`ConfigStore` 改成「加载后由宿主调 `CardModel.Normalize`」；然后才有条件做 `cards/*.dll` 插件加载。

---

## 5. P2：可选结构优化

### P2-1 毛玻璃实现没有接口

`src/Ui/OverlayWindow.xaml.cs` 里 73 处 `Backdrop` / `Glass` 提及，6 个手工生命周期方法
（`RetireSystemBackdrop:365` / `RetireAcrylicBackdrop:374` / `UpdateSystemBackdrop:385` /
`UpdateAcrylicBackdrop:421` / `HideBackdrops:472` / `CaptureGlass:554`），三种实现靠字符串
（`Dwm` / `System` / 兼容）在两个方法里分派（`UpdateBackdrop:330`、`QueueDragFrame:661`）。

**建议**：`IGlassBackdrop`（`Attach` / `Detach` / `Frame` / `Dispose`）+ 按 mode 取实现，
`Retire*` 变成实现内部状态。这能顺带消灭「切换实现后旧底板滞留」这类 bug 的土壤
（历史上修过一次：`40be56b`）。

### P2-2 采样源写死在一个引擎里

`src/Metrics/MetricEngine.cs` 把 PDH 计数器路径、`NvidiaSmi`、`DriveInfo`、`NetworkInterface` 全写在一个类里；
新增一个指标要同步改 `MetricsSnapshot` + `MetricEngine` + `Variables` + `Catalog` + 文档五处。

**建议**：至少采用 P1-7 的声明表，把「变量名 → 取值」集中；若要再进一步，再抽 `IMetricSource`。

### P2-3 没有测试工程

仓库只有 `--selftest`（`src/SelfTest.cs`，538 行）在真实机器上冒烟，依赖本机 PDH / 键盘钩子 / 热键占用，
因此不能在 CI 跑，也测不到纯逻辑。

**建议**：新建 `tests/`（xunit），先把纯逻辑覆盖起来：注册表去重与降级、Kind 迁移、`CardModel.Normalize`、
模板渲染（`{a:0.0}` / `{a,8}` / `{{}}` 转义 / 未知变量）、`Variables` 与 `Catalog` 键一致、`Json` 往返。
`SelfTest` 保留为环境冒烟。

---

## 6. 文档承诺 vs 实现

| 出处 | 承诺 | 现状 |
|---|---|---|
| README §已知问题 | 「加一种卡片只需在 `src/Cards` 新增一个类」 | 只对无设置项的文字卡成立；带设置项要改 5 个代码文件（§1.3） |
| `docs/cards-architecture.md` §2 | 「`UpdateContent`（设置没变时刷新数据）」 | 宿主从不调用（P0-2） |
| `src/Cards/CardModule.cs:46` | 「变量表按需构建一次，同一帧内的多张卡片可以复用」 | 每张卡各建一次（P0-5） |
| README §已知问题 | 「运行期插件 `cards/*.dll` 暂未实现」 | 属实，且受分层环限制（P1-10） |

---

## 7. 落地路线

### 阶段 1：止损（P0，预计半天）

实际顺序：**P0-1 → P0-2 → P0-3 → P0-4 → P0-5**（P0-3 依赖 P0-2；P0-4 动的地方最多，放最后）。
已完成 P0-1 ~ P0-4，P0-5 暂缓。

验收清单（对应实现后的真实行为）：

- 打开命令行后，另一张常驻卡片的 `{cpu}` / `{time}` 仍在刷新；
- 媒体卡暂停后改**名字 / 热键 / 间距**，视频不重启、仍保持暂停（改字号 / 颜色 / 毛玻璃仍会重建，属现状，见 §8 边界）；
- 改**非外观设置**（名字 / 命令 / 模型 / 间距）时，AI 对话卡的历史记录不丢；
- 改**非外观设置**时，应用设置不再出现「视频闪一下 / 图片重新解码」；
- 拖动卡片、F8 锁定、托盘唤出后用起来和以前一致（浅拷贝没有影响位置与锁定状态）；
- `--selftest` 全绿。

### 阶段 2：设置扩展点（P1-1 + P1-2 + P1-3，预计 1 ~ 2 天）

**P1-1 / P1-2 已完成**（`Extras` + 三个模块契约 + 命令行卡试点，见 §8.2）；剩下的 P1-3 是把 `CardModel` 里
`module is InfoTextCard` 这类具体类型判断也下沉到模块。
验收：新增一张「带两个设置项的新卡片」，**只新增 1 个模块文件 + 1 个设置区块类**，不改 `Settings.cs` / `CardEditor` / XAML
（`docs/cards-architecture.md` §2 末尾给了模板）。

### 阶段 3：结构收敛（P1-10 + P2-1 + P1-7，预计 2 ~ 3 天）

拆 `Cards.Abstractions`、去 `Core → Cards` 依赖、毛玻璃抽象、变量声明表 + 测试工程。

---

## 8. 实施记录（P0-1 ~ P0-4、P1-1、P1-2）

> 状态：P0 已提交（v2.5.0 / `c4e68e2`）；P1-1、P1-2 已实现（未提交，配置结构升到 schema 5）。P0-5 暂缓。

### 8.1 P0：宿主侧四修（v2.5.0）

| 文件 | 改动 |
|---|---|
| `src/Ui/OverlayController.cs` | P0-1 删掉 `RenderNow` 里的全局 `_commandMode` 栅栏；P0-2/P0-3 新增 `ContentInputs` + `ContentSignature()`，`CardSlot.ContentSignature` 存字符串快照，`SyncCards` 按快照决定「刷新数据」还是「重建控件」；P0-4 `BuildDialog()` 改为浅拷贝 + `CopyOverlay`，删除 `CloneSettings` |
| `src/Media/MediaView.cs` | P0-2 同路径重绑保留 `_playing`；换解码器重开视频后按原状态 `Play()` / `Pause()`；`ApplyVideoSettings` 改走 `Play()` / `Pause()`，让 `_playing` 与底部状态行同步 |

几个实现决定：

1. **为什么签名用字符串快照**：设置页在 `Clone` 出来的副本上改（`SettingsWindow._working`），点「应用」时副本整个替换掉 `_settings`，
   旧的 `CardSettings` 实例随之丢弃——所以“改前值”只能靠建卡时存下的字符串快照，不能靠比较对象。
   （早先这里写成“设置页改的是实时对象”，不准确，已更正。）
2. **签名包含哪些输入**：`Kind`、`Content`、`Extras`（P1 起替代 `Command`）、`Media`、`Chat`，加上共用的 `Style`、`Cli`、`Overlay.CornerRadius`、`Overlay.Shadow`。
   不含 `Overlay.X/Y/Monitor/Locked`——拖动时每帧都在变，含进去会退化成“永远重建”。
3. **边界（有意保守）**：`Style` 整体进签名，所以改字号 / 颜色 / 毛玻璃仍会重建富内容，与旧行为一致；
   要细化成「只影响显示的视觉子集」需要给富内容加“换样式不重建”的接口（§9.2 结论 2 的 `contentKey` / `styleKey` 拆分）。
   同理，媒体 / 对话设置变化仍走重建——`CommandView` / `ChatView` 没实现增量更新，不能假设它们能接住新值。
4. **`BuildDialog` 共享哪些对象**：`Style` / `Cli` / `Data` / `Behavior` / `Hotkeys` / `Cards` 共享引用，只有 `Overlay` 独立复制。
   窗口只写自己的 `Overlay.X/Y/Locked`（`LayoutCards`、`SetLocked`），不会写共享对象，所以共享是安全的。

### 8.2 P1-1 / P1-2：卡片自己的设置（schema 5 → 6，v2.6.0 ~ v2.6.2）

**契约**（`src/Cards/CardSettingsModule.cs`，每个接口各管一件事）：

| 接口 | 职责 |
|---|---|
| `ICardSettingsModule.InitializeCard` | 数据面：补默认值、夹紧范围；`CardModel` 建卡 / 每次归一化时调用 |
| `ICardEditorSection.CreateEditor` | 界面面：模块自己画设置区块，宿主只提供一个 `ContentControl` |
| `ICardSettingsValidator.Validate` | 校验：返回错误文案，宿主拼上卡片名 |
| `ICardSummary.Summary` | 摘要：设置页卡片标题右侧那行小字（不实现则用宿主的「常驻 / 呼出 F9」） |

**配置**：`CardSettings` 依次去掉 `Command`（v2.6.0）、`Media` / `Chat`（v2.6.2），统一改走
`Extras`（`Dictionary<string, object>`）+ `ExtrasAs<T>` / `SetExtras<T>`。
`Json.Bind` 增加了 `Dictionary<string, object>` 分支；`SettingsMigration` 的 `UpgradeCardExtras`
负责把旧子对象按卡片种类搬进 `Extras.command` / `media` / `chat`。

**试点与推广**：

1. v2.6.0：命令行卡先走通——`CommandCardEditor.cs` 用代码搭界面，`SettingsWindow.xaml` 的 70 行面板与
   `CardEditor` 的 8 个属性删除，改成一个 `ContentControl`。
2. v2.6.2：媒体卡与对话卡按同一模式迁移——设置面板整块搬成 `MediaCardSettingsView.xaml` /
   `ChatCardSettingsView.xaml`（属性名与绑定不变，视觉零差异），`SettingsWindow.xaml` 再删 190 行、
   `SettingsWindow.xaml.cs` 删 6 个处理器；API Key 的「未失焦草稿」改由 `CardEditor.CommitPendingEdits()`
   遍历模块区块提交。

**实测**：

- `dotnet build -c Release`：0 警告 0 错误（两次）。
- `--selftest`：失败项 0（「卡片设置=OK」覆盖 Extras 读写、模块区块创建、Key 的 UI 行为）。
- 迁移实测：schema 4 → 5（v2.6.0）与 schema 5 → 6（v2.6.2）都用真实配置跑过；
  对话卡的 `BaseUrl` / `Model` / `Mode` / `SystemPrompt` / `TimeoutSeconds` 与迁移前逐字段相同，
  `ApiKey` 仍是 `dpapi:` 密文，卡片 Id / Style / Hotkeys 未变。
- 设置页按 F10 打开无异常。
- 未覆盖：媒体卡设置区块的**实际点击**（当前配置里没有媒体卡），需要手工加一张媒体卡确认布局与按钮。

### 8.3 P0 验证状态（v2.5.0）

| 项目 | 结果 |
|---|---|
| `dotnet build -c Release` | ✅ 0 警告 0 错误（已重建到 `bin\Release`） |
| `MirrorpondHUD.exe --selftest` | ✅ 退出码 0，「失败项: 0」；5 个卡片模块注册正常，热键 / 键盘钩子 / 采样全 OK |
| `MirrorpondHUD.exe --smoke 6 --smoke-visible` | ✅ 退出码 0，无异常：5 张卡片建出、启动动画走完、毛玻璃与热键正常注册 |
| 手工回归 | ⏳ 待做，清单见 §7 阶段 1 验收 |

> 说明：`--smoke` 覆盖的是「启动 + 建卡（浅拷贝 / 签名）」这条路径；
> **应用设置时的签名比较与 `UpdateContent` 接线只能在手工回归里验证**（没有 CLI 入口能驱动 `ApplySettings`）。

### 8.4 手工验证时的观察点

- **P0-3「不重建」的硬信号**：视频卡正在播放时，只改卡片**名字 / 热键 / 间距**再应用，
  `mirrorpond.log` 里**不应**再出现 `视频窗已创建：` 或 `ffmpeg 解码：` 这类行（它们是重建才会打的）。
- **P0-2「暂停态保留」**：视频卡按媒体热键暂停 → 改名字 → 应用，画面应保持暂停，
  底部状态行仍是「播放」图标，而不是自己重新播起来。
- **P0-1「不再冻结」**：F9 打开命令行后，旁边的系统信息卡 / 时钟卡数字应继续变化（旧版会定格）。
- **P0-4「位置/锁定不受影响」**：拖动卡片、F8 锁定、托盘「显示 / 隐藏」应和以前一致。
- **P1-2「设置区块」**：设置 → 内容 → 命令行卡，应看到与旧版一致的工具 / 自定义命令 / 工作目录 / 返回内容 / 提示符预览；
  改工具后卡片摘要右侧的文字应跟着变（`onChanged` → `RefreshSummary`）。

### 8.5 下一步待办

1. 按 §7 阶段 1 的手工清单走一遍（运行 `run.bat` 即可用新构建）。
2. 手工打开设置页确认 P1-2 的模块区块布局。
3. 决定后续顺序：P1-3（把 `CardModel` 里 `is InfoTextCard` 的类型判断下沉）或 P0-5（一帧一份变量表）。

---

## 9. 对后续三个方向的支撑评估

> 背景：项目后期要做**文件中转站**、**快捷画板**，并会**深度重写外观**。
> 本节只回答一件事：§3 ~ §5 的优化项对这三件事是“前置条件”“明显受益”还是“正面冲突”，
> 以及哪些工作是评审清单里**没有、但这三件事必然需要**的。

### 9.0 三件事各自的压力点

| 方向 | 对契约的压力 |
|---|---|
| 文件中转站 | **异步资源 + 拖放 + 自己的设置 + 自己的持久状态**：要监听目录、要接住拖进来的文件、要记住列表 |
| 快捷画板 | **连续输入 + 易失状态**：笔迹只存在内存里，重建一次就没了；还要能在卡片上“画画”而不是“拖卡片” |
| 外观深度重写 | **运行时换肤**：主题编辑器每改一次样式，都不能把画板笔迹、对话历史、视频播放状态清掉 |

### 9.1 对应矩阵

| 功能 | 强依赖（不做就会返工） | 明显受益 | 冲突 / 阻碍（要先改） | 评审未覆盖、需新增 |
|---|---|---|---|---|
| 文件中转站 | P1-1 `Extras`、P1-2 设置区块、P1-8 生命周期与服务 | P0-2 / P0-3（列表、缩略图不闪）、P1-6 按卡动作、P1-4 截图样张 | 拖放通道已就绪（v2.6.3）；锁定时整卡穿透，收不到 drop；`IsInteractive` 语义过载 | Shell 图标 / 缩略图、每卡状态存储 |
| 快捷画板 | P1-1 / P1-2 / P1-8、**样式热更新（新）**、P0-2 / P0-3（已完成） | P1-6 按卡动作（撤销 / 清空 / 保存）、P1-4 | 拖动冲突已由 `HandlesOwnDrag` 解决（v2.6.3）；锁定=穿透时仍画不了（产品语义） | 分层窗口 + 大量墨迹的性能验证 |
| 外观深度重写 | **样式热更新契约（新）**、P2-1 毛玻璃抽象、P1-2 设置页拆分 | P0-4（`Style` 共享引用，每卡不再各存一份）、P1-10 分层（主题可独立成层） | **P0-3 的「`Style` 进签名 → 重建」边界**：主题编辑器一拖滑块就重建全部富内容；`Style` 是共享引用，编辑时必须先改副本 | 样式令牌层（`Accent` / `Danger` / `Muted` / `Border` / `Panel` / `MonoFont`）、替换 `CardButton.ThemeAccent` 之类硬编码 |

### 9.2 四个关键结论

**结论 1：P1-1 + P1-2 不是“以后再说”，是三条路的共同前置。**（已完成，见 §8.2）
三个方向各自都需要“模块自带设置 + 自带设置 UI”。不做 `Extras` 与 `ICardEditorSection` 的代价是具体的：
`CardSettings` 会再长出 `FileStation` / `Canvas` 两个胖字段，`CardEditor` 再长两套属性，
`SettingsWindow.xaml` 再长两块 `Visibility` 面板——§1.3 的“新增一张卡要改 5 个文件、其中 3 个是 700 ~ 1500 行”
会变成**每个新功能 6 ~ 7 个文件、3 个巨型文件**，而且两条新功能会把 `CardEditor` / XAML 推过 2000 行。
命令行卡已经走通这条路：模块自带设置（`Extras.command`）+ 自带区块（`CommandCardEditor.cs`），宿主两处大文件都没有为它再长。

**结论 2：P0-3 的保守边界会与外观重写正面冲突，需要把签名拆成两维。**
现在 `ContentSignature()`（`OverlayController.cs:239`）把 `Style` 整体算进“内容输入”，
理由是富内容没有“只换样式不重建”的能力。做深度换肤时这正好是最不能接受的：
主题编辑器每动一次颜色 / 字号 / 圆角，所有富内容卡片全部重建——画板的笔迹、AI 对话的历史、视频的播放位置全没了。

建议在外观重写启动**之前**把签名拆成两维：

| 维度 | 包含 | 变化时 |
|---|---|---|
| `contentKey` | `Kind`、`Content`、`Command`、`Media`、`Chat` | 重建内容（`CreateContent`） |
| `styleKey` | `Style`、`Cli`、`CornerRadius`、`Shadow` | 只刷新样式（`ApplyStyle`），**不重建** |

配套给富内容契约加 `ApplyStyle(content, context)`（`UpdateContent` 现在只在“完全没变”时被调用，
还接不住“样式变了、内容没变”的场景）。这也是 P0-2 的自然延伸：先把 `UpdateContent` 接上（已做），
再加 `ApplyStyle`（新）。

**结论 3：输入模型需要拆分，否则会逼出新的 `Kind == "canvas"` 特判。**
`IsInteractive` 现在同时承担四个含义：键盘独占、单槽（`_commandCard`）、强制非常驻、抑制全局键盘钩子
（`CardModule.cs:31`、`CardModel.cs:55`、`OverlayController` 的 `RefreshVisibility`）。
而两条新功能要的是：

| 需求 | 命令行卡 | 文件中转站 | 画板 |
|---|---|---|---|
| 收键盘输入 | 要（独占） | 偶尔（Ctrl+V） | 要（Ctrl+Z） |
| 收鼠标 | 要 | 要 | 要 |
| 独占单槽 | 要 | 不要（可多张） | 不要 |
| 卡片拖动 | 不要（`_commandMode` 已挡） | 要 | **要能区分“画”与“拖”** |

建议拆成 `NeedsMouseInput` / `HandlesOwnDrag` 两个新能力（`IsInteractive` 继续表示“键盘独占”，新增 P1-11），
宿主按能力组合决定：是否穿透、是否允许拖动、是否抢焦点。不拆的话，画板只能靠加特判实现，
正是 P1-3 要消灭的东西。

**结论 4：拖放是宿主能力，不是卡片能力。**
文件中转站要接住从资源管理器拖进来的文件，需要 `AllowDrop` + `DragEnter` / `DragOver` / `Drop`，
还要决定“锁定（点击穿透）时是否仍接受拖放”。这些都属于窗口层；模块只应声明“我收哪些格式、收到后做什么”。
建议加 `ICardDropTarget`（声明可接受的扩展名 / 类型 + 处理方法），由宿主统一注册。
顺带说明现存的语义冲突：**锁定 = 点击穿透**（`UpdateClickThrough`，`OverlayWindow.xaml.cs:273`），
穿透窗口收不到 drop，所以“锁定的中转站能不能拖”必须先定产品语义。

### 9.3 评审清单之外、这三件事必然需要的新工作

| 新增项 | 说明 | 大致量级 |
|---|---|---|
| `P1-11` 输入能力拆分 | `NeedsMouseInput` / `HandlesOwnDrag`（`IsInteractive` 继续表示键盘独占），宿主按能力组合 | ✅ v2.6.3 已完成 |
| `P1-8` 宿主服务 / 生命周期 | `ICardHost`（状态目录、重画 / 布局请求）+ `ICardHostAware`（Attach / Detach） | ✅ v2.7.0 已完成 |
| `P1-12` 每卡状态存储 | 中转站的列表、画板的草稿不该塞进 `settings.json`；给模块一个每卡目录 + 原子写 | ✅ v2.7.0 已完成（`CardStateStore` + `ICardHost`） |
| 样式热更新契约 | `ApplyStyle` + 签名拆两维 | 中，建议外观重写前完成 |
| 拖放通道 | `AllowDrop` + `ICardDropTarget` + 锁定语义 | ✅ v2.6.3 已完成（宿主统一注册，模块只声明收什么） |
| Shell 集成 | 文件图标 / 缩略图（`SHGetFileInfo` 或 `IShellItemImageFactory`），供中转站与未来的媒体卡共用 | 中 |

### 9.4 建议的实施顺序（相对原 §7 的调整）

1. **阶段 A（前置，现在）**：P1-1 `Extras` + P1-2 `ICardEditorSection`。
   三条路共同吃这个红利，越晚做返工越多。
2. **阶段 B（能力补齐，已完成）**：P1-8 生命周期 / 服务 + `P1-11` 输入拆分 + `P1-12` 状态存储 + 拖放通道 —— v2.6.3 / v2.7.0。
3. **阶段 C（外观重写前）**：签名拆两维 + `ApplyStyle` + P2-1 毛玻璃抽象。
4. **阶段 D（功能落地）**：文件中转站（A + B）→ 快捷画板（A + B + C）→ 外观重写（A + C + P2-1）。

原 §7 的阶段 2（设置扩展点）优先级**上调**：它从“可选重构”变成“两条新功能的开工条件”。

### 9.5 两个建议先做技术验证（spike），再排期

1. **画板的输入与性能**：分层窗口（`AllowsTransparency`）里放 `InkCanvas`，验证
   ① 按下鼠标是“画”还是“拖卡片”；② 大墨迹量下的延迟；③ 锁定 / 穿透状态下能否画。
   现状证据：`OnMouseLeftButtonDown`（`OverlayWindow.xaml.cs:621`）只判断 `Locked` 与 `_commandMode`，
   内容若不把事件标记 `Handled`，就会拖动整卡。按钮演示卡能点，是因为控件自己吃掉了鼠标按下事件，
   但 `InkCanvas` 是否在所有情况下都这样，需要实测。
2. **主题热切换**：先在现有 `MediaCard` / `ChatCard` 上手工验证“只换颜色、不重建”能否保住播放状态与对话历史，
   再决定 `ApplyStyle` 的粒度（整块样式 vs 逐 token 增量）。

### 9.6 不是模块化问题、但会决定成败的几条

这三件事各自还有一些与“模块化”无关、却会直接卡住进度的风险。记在这里，功能设计时逐条过：

| 方向 | 风险 | 说明 |
|---|---|---|
| 文件中转站 | 大文件 / 网络盘拖放会卡 UI | 复制、移动要能在后台跑并支持取消；`CommandRunner` 已有异步 + 超时的范式可参照 |
| 文件中转站 | 列表持久化的体积 | 不要把文件清单塞进 `settings.json`（会撑大配置、和 `Normalize` 打架），见 `P1-12` |
| 文件中转站 | 拖放与“锁定=穿透”的语义冲突 | 锁定状态收不到 drop；要么允许锁定卡片接受拖放（需要另开穿透例外），要么在 UI 上明确“锁定后不可拖入” |
| 快捷画板 | 分层窗口里的绘制延迟 | 卡片窗口是 `AllowsTransparency` 分层窗口，大量墨迹重绘的表现要实测，别先按普通 `InkCanvas` 的经验排期 |
| 快捷画板 | 保存 / 导出 | PNG 编码、DPI、多显示器缩放都要处理；撤销栈只存在内存里，卡片被重建就丢（所以结论 2 对它也重要） |
| 外观重写 | `TextCanvas` 的图标居中依赖 `FormattedText` 墨迹测量 | 渐变、描边、字距这类深度样式可能破坏现有的“按墨迹居中”逻辑（`InkKey` 缓存按字体 / 字号 / 字重做 key，样式维度一多要重新设计） |
| 外观重写 | `StyleSettings` 的消费面 | 12 个文件直接读 `Style` 字段（其中 5 个是 UI 控件与媒体）；换 token 体系时这是必须一次性改完的面 |

---

## 10. 附录

### A. 复核命令

```bash
# 按 Kind / 具体类型的判断（除 Settings.Normalize 的枚举归一化外都应消灭）
grep -rn 'Kind ==\|module is \|as MediaView\|as CliPanelView' src --include=*.cs

# 变量表与设置页提示清单的漂移
grep -o 'map\["[a-z_0-9]*"\]' src/Text/Variables.cs | sort -u | wc -l
grep -c 'Name = "' src/Ui/Catalog.cs

# 每张卡一份配置克隆的调用点
grep -n 'CloneSettings\|BuildDialog' src/Ui/OverlayController.cs
```

### B. 变量漂移全清单（代码有、设置页提示没有）

`cpu_icon`、`mem_icon`、`ram_icon`、`rom_icon`、`disk_icon`、`net_down_icon`、`net_up_icon`、`gpu_icon`、
`net_down_bps`、`net_up_bps`、`disk_read_bps`、`disk_write_bps`、`net_total`、`mem_free`、`vram_pct`、`gpu_util`（16 个）。

### C. 相关提交

| 提交 | 与本文的关系 |
|---|---|
| `40be56b` | 修「切玻璃实现后旧底板滞留」→ P2-1 |
| `05857ce` | 删底板窗口、模糊画进卡片 → P2-1 的接口边界 |
| `f4b2b8b` | CLI 面板统一 → 印证 `CliUi.cs` 这层公共件是有效的抽象 |
