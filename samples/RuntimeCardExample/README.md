# 运行时卡片示例（2.14.3）

这是 `plugins/cards/*.dll` 运行时插件的最小示例：一个只实现 `ICardModule` 的**文字卡** + `manifest.json`。
2.14.x 的宿主支持影子复制、缺失插件占位卡、manifest 校验与 dev-only 文字卡热重载；生产默认仍是**重启生效**。
2.14.1 起插件只引用独立的 `MirrorpondHUD.Contracts` 契约程序集，不再引用宿主 DLL。

## 构建

示例插件只引用契约工程 `src/Contracts/MirrorpondHUD.Contracts.csproj`，构建它会自动构建 Contracts，
因此**不需要先构建宿主**（运行宿主时才需要 `dotnet build -c Release`）：

```bat
dotnet build samples\RuntimeCardExample\RuntimeCardExample.csproj -c Release
```

## 安装

便携版：把示例输出复制到 exe 的插件目录：

```bat
mkdir bin\Release\plugins\cards\example-clock
copy samples\RuntimeCardExample\bin\Release\RuntimeCardExample.dll bin\Release\plugins\cards\example-clock\
copy samples\RuntimeCardExample\bin\Release\RuntimeCardExample.deps.json bin\Release\plugins\cards\example-clock\
copy samples\RuntimeCardExample\manifest.json bin\Release\plugins\cards\example-clock\
```

也可以装到用户级目录（exe 目录不可写、或想让多个副本共用时）：

```text
%LOCALAPPDATA%\MirrorpondHUD\plugins\cards\example-clock\
  manifest.json
  RuntimeCardExample.dll
  RuntimeCardExample.deps.json
```

宿主会扫描**两个根目录**（存在即扫描）。启动 `bin\Release\MirrorpondHUD.exe`，日志应出现：

```text
已加载卡片插件 Example Clock 0.1.0（Kind=example-clock，entry=RuntimeCardExample.ExampleClockCard，文字卡，影子副本）
运行时插件加载完成：成功 1 个，失败 0 个（com.example.example-clock）
已注册卡片模块 7 个（内置 6，运行时插件 1）
```

2.14.0 会在加载前把插件目录影子复制到 `%LOCALAPPDATA%\MirrorpondHUD\plugin-cache`，源目录里的 DLL
不再被宿主锁住：运行中可以覆盖 / 删除源 DLL，下一次启动（或 dev 热重载）使用新文件。

然后在「设置 → 内容 → 添加卡片」里应看到「示例插件时钟」。添加后写入的 Kind 是 `example-clock`。

## 旧插件兼容（2.14.1 起）

- **新插件**：只引用 `src/Contracts/MirrorpondHUD.Contracts.csproj`（`Private=false`，不要把
  `MirrorpondHUD.Contracts.dll` 复制进插件目录）。
- **旧插件**：2.13.6 / 2.14.0 编译、仍引用 `MirrorpondHUD.dll` 的插件无需重编译；宿主 `src/TypeForwards.cs`
  对契约类型做了 `TypeForwardedTo`，运行时仍解析到同一批 Type。
- 插件 ALC 对宿主与 Contracts 两个程序集都返回 `null`，两者都由默认 ALC 提供，避免同一契约出现两份 Type。
- `apiVersion` 仍是 `1`：2.14.x 只有加法（如可选 `ICardModuleLifetime`）与类型转发，没有破坏性契约变更。
- `ThemeTokens` / `ThemeResolver` 自 2.14.3 起在 Contracts；其余 WPF 共享控件（`CardButton` / `CardList` 等）
  仍在宿主，示例刻意不用任何宿主控件，只做纯文字卡。

## 开发热重载（`--plugin-dev`）

```bat
bin\Release\MirrorpondHUD.exe --plugin-dev
```

- 宿主监听 `bin\Release\plugins\cards` 与 `%LOCALAPPDATA%\MirrorpondHUD\plugins\cards`，文件变化 700 ms 后自动重载；
- 改完 DLL / `manifest.json` 保存即可，日志会出现「已加载卡片插件 …（文字卡，影子副本）」→
  「运行时插件模块已替换」→「已刷新 N 张卡片」→「插件热重载完成」；
- 只支持**纯文字卡**（本示例满足）：一旦实现 `IRichCardModule`、`IsInteractive`、`ICardEditorSection`
  或 `ICardLegacyAlias`，会被拒绝并提示重启；
- 重载会关闭设置窗（未保存的编辑会丢），不改写配置；生产使用不要带 `--plugin-dev`，装 / 换插件后重启。

## 缺失插件与占位卡

- 插件缺失 / 加载失败时，2.14.0 不再把卡片回退成默认卡，而是按原 Kind 显示占位卡
  「插件不可用：example-clock」；占位卡不会出现在「添加卡片」菜单。
- 配置里的 `Kind` / `Content` / `Extras` 原样保留、**不写回改写**；插件重新装回后，dev 模式自动恢复，
  生产模式重启恢复。
- `--selftest` 已覆盖占位卡行为（失败项 0）。

## manifest 校验与 API version

2.14.0 宿主会校验：

- `manifest.json` ≤ 256 KB；`id` ≤ 100 字符、只允许字母数字与 `.-_`；`apiVersion` 必须为 1；
  `assembly` 只接受裸 `.dll` 文件名；`entryType` 非空、不含空白；
- 入口类型必须存在、非抽象 / 接口、实现 `ICardModule`，且有 public 无参构造；
- 卡片 `Kind` ≤ 64 字符、只允许字母数字与 `.-_`，且不能与内置 Kind / 旧别名冲突
  （`infotext` / `command` / `media` / `chat` / `dropstation` / `buttons` / `metrics` / `clock` / `keys` / `text`）；
- 同批扫描中重复 `id` / 重复 `Kind` 会被拒绝。

本示例的 `manifest.json` 满足这些约束：`apiVersion = 1`、`Kind = example-clock`。

## 边界

- 生产默认仍是重启生效；热重载只在 `--plugin-dev` 下、且只对纯文字卡（本示例）提供。
- 插件以当前用户权限运行，`AssemblyLoadContext` **不是安全沙箱**；只安装来源可信的本地插件。
- 2.14.x 没有签名 / 哈希 / 白名单校验，影子复制与 manifest 校验只是可靠性措施，不改变信任模型。
- 插件必须与宿主使用同一个 `net8.0-windows` / WPF 目标；`apiVersion` 目前只接受 `1`。
- 插件工程引用 `MirrorpondHUD.Contracts`（不要复制契约 DLL）；旧插件引用宿主的二进制靠 `TypeForwardedTo` 继续加载。
- 富内容卡（`IRichCardModule`）仍然只支持重启加载，不能热重载。
