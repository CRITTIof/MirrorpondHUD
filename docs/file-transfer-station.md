# 文件中转站：调研与设计（2026-10-31）

> 目标：在现有卡片框架里做一个**文件中转站**——把文件从资源管理器拖进卡片、暂存成列表，
> 之后可以打开 / 在资源管理器中定位 / 拖出 / 删除。本文回答：它要什么、接哪些现有契约、
> 有哪些坑、分几步做。
>
> 定位：**本地暂存**，不是 PC ↔ 手机的局域网传输。后者需要 HTTP 服务、防火墙、配对与二维码，
> 属于另一张卡片 / 另一个方向（本文 §12 只留一句边界，不展开）。
>
> 相关：[cards-architecture.md](cards-architecture.md)（模块契约与 `ICardDropTarget` 模板）、
> [modularity-review.md](modularity-review.md) §9（中转站的压力点与风险清单）、
> [legacy-audit.md](legacy-audit.md)（当前遗留）。

## 0. 一句话结论

**可行，且大部分前置已经就绪**：拖放通道（`ICardDropTarget`）、每卡状态存储（`ICardHost` / `CardStateStore`）、
模块自带设置（`Extras` + `ICardEditorSection`）都在 v2.6.3–v2.7.0 完成了。
真正要新做的是：列表控件 + 文件操作（引用 / 复制 / 移动）+ 图标 / 缩略图 + 一个小的契约缺口
（内容控件拿不到 `ICardHost`，列表变长后无法 `RequestLayout`）。

建议按 M1（引用 + 复制）→ M2（拖出 / 剪贴板 / 进度）→ M3（缩略图 / 自动清理）三步走；
M1 约 500–700 行，不碰宿主大文件。

## 1. 目标与非目标

### 目标（M1 内）

- 从资源管理器把文件 / 多选拖进卡片 → 出现列表（名称、大小、来源）；
- 列表可滚动、可删除单条、可清空；双击打开，右键「在资源管理器中显示 / 复制路径 / 删除」；
- 两种收纳方式：**引用**（只记路径，不动文件）与**复制**（收进接收目录）；
- 列表持久化在每卡状态目录，重启后还在；源文件被删 / 移走时标记「已丢失」；
- 卡片设置：模式、接收目录、最多保留条数、是否显示图标；
- 拖入时高亮，锁定（点击穿透）时给出「解锁后才能拖入」的提示。

### 非目标（本阶段不做）

- PC ↔ 手机互传、局域网 HTTP 服务、二维码配对；
- 云同步 / 网盘；
- 压缩包解压、文件预览编辑、自动运行 / 自动打开（安全）；
- 把整个文件夹递归收进来（M2 再评估，需要确认与进度）。

## 2. 现有能力盘点（都能直接用）

| 能力 | 现状 | 对中转站意味着 |
|---|---|---|
| `ICardDropTarget` | v2.6.3；宿主统一注册 `AllowDrop` / `DragOver` / `Drop` | 模块只写 `CanDrop` / `Drop`，不碰窗口 |
| `HandlesOwnDrag` | v2.6.3；关掉整卡拖动 | **慎用**：一开就没法拖整张卡片（见 §7.2） |
| `NeedsMouseInput` | 已有；关掉点击穿透，内容能收鼠标 | 列表 / 右键 / 双击需要它 |
| `ICardHost` + `ICardHostAware` | v2.7.0；每卡状态目录、原子写、`RequestLayout` / `RequestRender` | 列表持久化、列表变长后重排 |
| `CardStateStore` | 原子写（`.tmp` + 替换）+ 文件名 / Id 校验 | `items.json` 就放这里，不塞 `settings.json` |
| `Extras` + `ICardSettingsModule` / `ICardEditorSection` / `ICardSettingsValidator` | v2.6.0–2.6.2 | 模式 / 目录 / 条数由模块自己管 |
| `ICardStyleAware` | v2.8.0 起；v2.9.4 起 CLI 也能就地换肤 | 换主题时列表不重建、不闪 |
| 锁定 = 点击穿透 | `OverlayWindow.UpdateClickThrough` | 锁定后收不到 drop，见 §7.3 |

## 3. 用户场景与交互

### 3.1 拖入

1. 从资源管理器选一个或多个文件，拖到卡片上；
2. 卡片出现「松开以添加 N 个文件」高亮；
3. 松开后：
   - 引用模式：直接进列表（不拷贝）；源文件不存在则拒绝并提示；
   - 复制模式：进列表，状态显示「复制中 1/3」，完成后变「就绪」；
   - 移动模式（M2）：复制成功后删除源文件，失败要能回滚提示。

### 3.2 列表操作

- 双击：`ShellExecute` 打开（等价于资源管理器双击）；
- 右键菜单：打开 / 在资源管理器中显示（`explorer /select,`）/ 复制路径 / 从列表移除 / 删除文件（仅当文件在接收目录里，需确认）；
- 顶部按钮：清空列表 / 打开接收目录 / 从剪贴板添加（M2）；
- 空状态：「把文件拖进来」+ 一行说明当前模式。

### 3.3 拖出（M2）

- 从某一行按住拖动 → `DoDragDrop` 带 `FileDrop`，可拖到资源管理器 / 其他程序；
- 拖出后是否从列表移除：**不移除**（中转站是“暂存”，不是“剪切板”）；用户手动删。

### 3.4 与卡片本身的关系

- 卡片可以常驻，也可以绑热键；
- 默认**不是** `IsInteractive`（不抢键盘、不抑制全局按键）；
- 整卡拖动：优先不用 `HandlesOwnDrag`，靠行内事件 `Handled` 阻止整卡拖动（需 spike，见 §7.2）。

## 4. 数据模型

### 4.1 卡片设置（`Extras.dropstation`）

```csharp
public sealed class StationSettings
{
    public string Mode = "Reference";     // Reference / Copy / Move
    public string Directory = "";         // Copy / Move 必填；Reference 忽略
    public int MaxEntries = 50;           // 1 ~ 500
    public bool ShowIcons = true;
    public bool ShowThumbnails = false;   // M3
    public int AutoClearMinutes = 0;      // 0 = 不自动清；只清列表，不删文件
    public bool ConfirmDelete = true;

    public void Normalize() { /* 白名单 / Trim / FullPath / 夹紧 */ }
}
```

`Validate`：Copy / Move 模式下目录为空或不存在 → 返回「还没选接收目录」；Reference 模式不校验目录。

### 4.2 每卡状态（`<state>\cards\<id>\items.json`）

```json
{
  "formatVersion": 1,
  "items": [
    {
      "id": "b1f2…",
      "path": "C:\\Users\\me\\Desktop\\a.zip",
      "storedPath": "D:\\中转站\\a.zip",
      "name": "a.zip",
      "size": 1234567,
      "addedUtc": "2026-10-31T08:00:00Z",
      "missing": false
    }
  ]
}
```

- 写：`ICardHost.WriteState("items.json", json)`，每次增删 / 复制完成 / 剪枝后写；
- 读：`ReadState`，失败 / 损坏 → 空列表 + 日志（不要因为状态坏了打不开卡片）；
- 剪枝：超过 `MaxEntries` 从最旧开始丢**列表项**，不删文件；
- 启动时轻量校验：`File.Exists(storedPath)` → 不在就标 `missing`，不自动删；
- 不把列表塞进 `settings.json`（配置会变大、和 `Normalize` 打架，见 P1-12）。

## 5. 关键流程

### 5.1 `CanDrop`（DragOver 会高频调用，不能做重 I/O）

```csharp
public bool CanDrop(IDataObject data)
{
    if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
    var paths = data.GetData(DataFormats.FileDrop) as string[];
    return paths != null && paths.Length > 0;
}
```

只检查格式与数量；**不要**在这里 `File.Exists` 或访问网络盘（拖动时每几十毫秒一次）。
真正的存在性检查放到 `Drop`。

### 5.2 `Drop`

1. 取 `FileDrop` 路径数组，去重（大小写不敏感的全路径）+ 与现有列表去重；
2. 引用模式：逐条 `File.Exists`（后台线程，别阻塞 UI）→ 加入列表；不存在的在状态行汇总提示；
3. 复制 / 移动模式：把任务交给后台任务队列（`Task.Run` + 顺序执行，避免同时拷 20 个大文件）：
   - 目标路径 = `Directory\文件名`；冲突 → `名字 (2).扩展名`；
   - 复制：1 MB 缓冲循环 + `CancellationToken`，每块检查取消；进度回报到该行；
   - 移动：先试 `File.Move`（同盘瞬时），跨盘 `IOException` → 复制 + 删除源；
   - 失败：保留源文件、该行标红、状态行列原因；**不删除目标半成品**（复制到 `.part` 再改名更稳）；
4. 每完成一条写一次状态（或批量写 + 退出前写，二者取其一；M1 用逐条写，简单可靠）。

### 5.3 复制进度与取消

- 每行一个细进度条（0–100%），总状态行「2/5 复制中」；
- 顶部「取消」只取消**未完成**的复制；已完成的保留；
- 大文件（> 2 GB）/ 网络盘：显示速度与剩余时间（可选），至少要有“还在动”的反馈。

### 5.4 冲突与重复

- 同名冲突：`a.txt` → `a (2).txt`，最多试到 999；
- 同一文件重复拖入：已在列表里 → 提到最前并闪一下，不重复添加；
- 源路径与目标路径相同（用户把接收目录设成源目录）：复制直接跳过，移动报错。

### 5.5 打开 / 定位

```csharp
Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
Process.Start("explorer.exe", "/select,\"" + path + "\"");
```

用 `UseShellExecute = true` 让系统按关联程序打开；不要自己 `Process.Start(path)` 拼命令行。

## 6. 与宿主契约的接线

### 6.1 模块声明

```csharp
public sealed class DropStationCard : RichCardModuleBase,
    ICardDropTarget, ICardSettingsModule, ICardEditorSection,
    ICardSettingsValidator, ICardSummary, ICardStyleAware
{
    public const string SettingsKey = "dropstation";
    public override string Kind => "dropstation";
    public override string DisplayName => "文件中转站";
    public override bool NeedsMouseInput => true;
    public override bool HandlesOwnDrag => false;   // 见 §7.2
    public override bool DefaultResident => true;
}
```

### 6.2 内容控件拿不到 `ICardHost`（v2.10.2 已补）

原始缺口：`CardRenderContext` 只有 `Settings / MetricsSnapshot / KeyHistory / CardSettings / Variables`，
没有 `ICardHost`；而 `ICardHostAware.Attach(host)` 又约定「模块是单例，不要把 host 存进字段」。
结果是列表变长、需要 `host.RequestLayout()` 时，内容控件拿不到 host。

**v2.10.2 已按方案 A 落地**：`CardRenderContext.Host`（可空），宿主在 `EnsureContent` / `RenderCardText` /
`SyncCards` 构造上下文时传 `slot.Host`；内容控件在构造函数里保存这份 host 即可，
模块本身仍然不存 host。文字渲染与截图路径的 `Host` 为 null（内容控件要判空）。

两条路的对比保留在这里，供以后改契约时参考：

| 方案 | 做法 | 评价 |
|---|---|---|
| A（已采用） | 给 `CardRenderContext` 加 `ICardHost Host`（可空），宿主传 `slot.Host`；内容控件自己保存 `_host` | 小改动，画板 / 中转站都受益；不违反“模块单例” |
| B | 模块按 `Config.Id` 维护 `Dictionary<string, ICardHost>` | 违反现有契约注释，且要处理删卡 / Detach 清理，不推荐 |

### 6.3 换肤

- 列表项的图标 / 文字 / 行高 / 滚动条都从 `ThemeTokens` 取；
- `ApplyStyle(context)` 只重套这些颜色 / 字号，不重建列表，保证换主题时不闪、不丢选中项；
- 行高按字号 × 1.4 + 内边距重算，超出时 `RequestLayout()`。

## 7. 三个必须先定的语义

### 7.1 锁定 = 点击穿透，收不到 drop

`Overlay.Locked` 时窗口是 `WS_EX_TRANSPARENT`，鼠标事件（包括拖放）根本到不了卡片。
两条产品路线：

- **推荐**：锁定卡片上显示「已锁定 · 解锁（F8）后可拖入」的静态提示，不改穿透语义；
- 不推荐：为拖放单独开“穿透例外”——那会让锁定的卡片挡住桌面点击，和 F8 的承诺冲突。

### 7.2 行内拖出 vs 整卡拖动

要支持“从某一行拖出到资源管理器”，需要在行上 `MouseMove` 超过阈值时调 `DragDrop.DoDragDrop`。
问题：`OverlayWindow` 的整卡拖动也在鼠标按下 / 移动上。两条路：

| 方案 | 代价 |
|---|---|
| 行内把 `MouseLeftButtonDown` / `MouseMove` 标记 `Handled` | 需要 spike 确认窗口拖动是冒泡还是 `Preview`；若是 Preview，行内拦不住 |
| `HandlesOwnDrag = true` | 简单，但整张卡片再也拖不动，只能改位置时去设置页调坐标——不可接受 |

**先做 30 分钟 spike**：在现有按钮卡上试“行内 `Handled` 能否阻止整卡拖动”。能 → 不用 `HandlesOwnDrag`；
不能 → M1 不做拖出，M2 再看是否给 `HandlesOwnDrag` 加“拖动手柄区域”的细化。

### 7.3 拖放效果只有 Copy

`ICardDropTarget.CanDrop` 返回 `bool`，宿主把 `DragOver.Effects` 固定为 `Copy`（或 `None`）。
所以用户在资源管理器里按住 Shift（移动）时，卡片仍显示“复制”。M1 用设置里的模式决定实际行为即可；
M2 如果要在 UI 上尊重 Shift / Ctrl，需要把接口扩成 `DragDropEffects CanDrop(IDataObject)`（宿主把返回值直接喂给
`DragOver.Effects`），并读 `Preferred DropEffect` 流。

## 8. 性能与可靠性

| 风险 | 处理 |
|---|---|
| 大文件 / 网络盘拖放卡 UI | 复制在后台线程；1 MB 块 + 取消检查；拖动判定不碰磁盘 |
| 同时拖入很多文件 | 串行队列，最多 2 个并发复制；列表先占位、后填进度 |
| 列表很长 | `ListBox` + `VirtualizingStackPanel`（固定行高），不要 `StackPanel` 全量实例化 |
| 图标 / 缩略图开销 | 图标按扩展名缓存；缩略图按需异步加载（`IShellItemImageFactory`），失败回退图标 |
| 状态文件体积 | 只存路径 / 名称 / 大小；超过 `MaxEntries` 剪枝；状态坏了按空列表处理 |
| 长路径 | manifest 已开 `longPathAware`；内部统一 `Path.GetFullPath`；仍要兜底 `PathTooLongException` |
| 杀毒 / 文件锁 | 复制失败重试 2 次（100 ms / 500 ms），仍失败则标红并保留源文件 |
| 磁盘满 | 复制前 `DriveInfo.AvailableFreeSpace` 检查；不足时拒绝并提示所需空间 |
| 重解析点 / 符号链接 | 默认跳过并提示（`FileAttributes.ReparsePoint`），避免把链接目标当普通文件复制 |
| 多显示器 / DPI | 列表用 WPF 布局单位（DIP），图标按 `DpiScale` 取对应尺寸 |

## 9. 安全与隐私

- 拖进来的路径是**不可信输入**：只当文件路径用，不拼命令行、不执行、不解压；
- 「打开」走 `ShellExecute`，是用户显式动作；「删除」默认二次确认，且只允许删接收目录内的文件；
- 状态文件只记录路径 / 名称 / 大小，不记录文件内容；日志默认只写文件名，不写完整用户路径（可调试开关）；
- 接收目录权限继承用户配置目录；不要把中转站目录设到 `Program Files` 这类需要管理员的位置（`Validate` 提示）；
- 拖入 `.lnk` / `.bat` 不特殊处理，打开仍由系统确认。

## 10. 分阶段实施

### M1 最小可用（建议先做）

- `DropStationCard` + `StationSettings` + `DropStationEditor`；
- 引用 / 复制两种模式；拖入、列表、双击打开、定位、移除、清空；
- `items.json` 持久化 + 缺失标记 + 剪枝；
- 空状态 / 拖入高亮 / 锁定提示；
- `ICardSummary`：「N 个文件 · 引用 / 收纳」；
- `CardRenderContext.Host`（§6.2，v2.10.2 已补）；
- 规模估计：模块 + 视图约 450–600 行，编辑器约 80 行，宿主 20 行。

### M2 体验

- 拖出（先做 §7.2 的 spike）；
- 剪贴板（`Clipboard.GetFileDropList`，`COMException` 重试）；
- 复制进度 / 取消 / 速度；
- 移动模式 + 跨盘回退；
- 冲突命名 / 去重 / 缺失文件批量清理。

### M3 增强

- 图标 / 缩略图（`SHGetFileInfo` + `IShellItemImageFactory`，按扩展名缓存）；
- 自动清理（只清列表）、搜索 / 过滤、多选；
- 每卡热键动作（清空 / 打开目录）。

## 11. 验收清单

**自检（`--selftest` 可覆盖的纯逻辑）**

- 冲突命名：`a.txt` 已存在 → `a (2).txt`；`a (2).txt` 已存在 → `a (3).txt`；
- 去重：同一路径大小写不同只保留一条；
- 剪枝：`MaxEntries = 3` 时加入 5 条，保留最新 3 条，且不删文件；
- 状态往返：序列化 → 反序列化 → 字段一致；损坏 JSON → 空列表不抛；
- 缺失标记：路径不存在 → `missing = true`，不自动删除条目。

**手工**

1. 拖 3 个文件进引用模式卡片 → 列表 3 条；重启后还在；
2. 切复制模式、选目录 → 拖入 → 目录里出现文件、列表显示就绪；同名再拖一次 → `(2)`；
3. 拖 1 GB 文件 → UI 不卡、能取消、取消后源文件在、目标没有半成品；
4. 网络盘文件 → 拖入不卡死；断开网络后状态行报错；
5. 锁定（F8）后拖入 → 卡片提示“解锁后可拖入”，不会静默失败；
6. 删掉源文件后重启 → 该条目标「已丢失」，仍可手动移除；
7. 换主题 / 改字号 → 列表不重建、选中项不丢、颜色跟着变；
8. 拖出到资源管理器（M2）→ 文件真的被复制 / 移动，且列表行为符合预期。

## 12. 未决问题（需要产品拍板）

1. **默认模式**：引用还是复制？（建议引用：零 I/O、零副作用；复制作为显式选择）
2. **拖出后是否移除**：建议不移除（暂存语义）；若要做“剪切板式”，需要单独设置。
3. **文件夹拖入**：M1 拒绝并提示；M2 是否支持递归复制？
4. **自动清理**：只清列表还是连文件一起删？（建议只清列表，删文件必须显式）
5. **上限**：单文件大小 / 总容量是否要限制？（建议 M1 只提示磁盘空间，不设人为上限）
6. **PC ↔ 手机传输**：另立卡片（HTTP + 二维码配对 + 防火墙），不在本卡片里做。

## 13. 参考

- 现有契约：[cards-architecture.md](cards-architecture.md) §2（输入能力、宿主服务、`DropStationCard` 模板）
- 风险与阶段：[modularity-review.md](modularity-review.md) §9.3 / §9.6
- 状态存储：`src/Core/CardStateStore.cs`、`src/Cards/CardHost.cs`
- 拖放接线：`OverlayController.WireInputCapabilities`、`OverlayWindow.EnableDrop`
- 颜色 / 主题：[theme-rewrite-prerequisites.md](theme-rewrite-prerequisites.md)

## 14. 实现状态（v2.11.0，M1）

已落地：

- `src/Cards/DropStationCard.cs`：模块 + `StationSettings` + `StationView` + `StationEditor`，单文件；
- 两种模式：**引用**（默认，只记路径）与**复制**（写入接收目录，重名自动 `名字 (2).扩展名`）；
- 拖入：`ICardDropTarget` + `ICardDropFeedback`，拖入时显示 `CardDropOverlay`；
- 列表：`CardList`（图标 / 名称 / 大小、丢失标记、双击打开、右键菜单）；
- 操作：打开 / 在资源管理器中显示 / 复制路径 / 从列表移除 / 删除接收目录内的文件（二次确认）；
- 持久化：每卡 `items.json`（`ICardHost` / `CardStateStore`），启动时标记丢失文件，超出上限丢最旧列表项；
- 复制：`CardJobQueue` 串行后台执行，失败 / 取消会删除写了一半的目标文件；
- 设置：模式 / 接收目录 / 最多保留；`Validate` 拦住「复制但没有目录」；
- 自检：模块能力、设置归一化 / 校验、拖放判定、临时文件拖入 / 去重 / 缺失拒绝；
  `--screenshot-widgets` 会同时渲染中转站的空状态。

尚未做（对应本文的 M2 / M3）：

- 行内拖出（`DragSource` 已就绪，但“行拖动 vs 整卡拖动”的 spike 还没做）；
- 移动模式（移动源文件）、文件夹递归、剪贴板添加；
- 缩略图（目前是扩展名图标）、自动清理、搜索 / 多选、逐字节进度；
- 崩溃恢复 / 复制队列持久化（重启后未完成的复制不会续传）。
