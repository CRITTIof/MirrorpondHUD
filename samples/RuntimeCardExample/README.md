# 运行时卡片示例（2.20.0）

这是 `plugins/cards/*.dll` 运行时插件的最小示例：一个只实现 `ICardModule` 的**文字卡** + `manifest.json`。
示例只引用独立的 `MirrorpondHUD.Contracts` 契约程序集，不引用宿主 DLL；需要 `CardButton` / `CardList` 等
共享 WPF 控件时，再引用 `MirrorpondHUD.UiKit`。

> **没有热插拔 / 热重载**：安装、更新、删除插件都必须替换目录并重启宿主。`--plugin-dev` 已移除。

完整、可不读源码直接照做的制作指南见根目录 [DLL-CARD-PLUGIN-GUIDE.md](../DLL-CARD-PLUGIN-GUIDE.md)。

## 构建

示例插件只引用契约工程 `src/Contracts/MirrorpondHUD.Contracts.csproj`，构建它会自动构建 Contracts，
因此不需要先构建宿主（运行宿主时才需要 `dotnet build -c Release`）：

```bat
dotnet build samples\RuntimeCardExample\RuntimeCardExample.csproj -c Release
```

如果插件要用共享 WPF 控件，在插件 csproj 里再加一个对
`src/UiKit/MirrorpondHUD.UiKit.csproj` 的 `ProjectReference`（`Private=false`）；本示例不需要。

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

**重启** `bin\Release\MirrorpondHUD.exe` 后，日志应出现：

```text
已加载卡片插件 Example Clock 0.2.0（Kind=example-clock，entry=RuntimeCardExample.ExampleClockCard，文字卡，影子副本）
运行时插件加载完成：成功 1 个，失败 0 个（com.example.example-clock）
已注册卡片模块 7 个（内置 0，运行时插件 7）
```

然后在「设置 → 内容 → 添加卡片」里应看到「示例插件时钟」。添加后写入的 Kind 是 `example-clock`。

宿主加载前会把插件目录影子复制到 `%LOCALAPPDATA%\MirrorpondHUD\plugin-cache`，所以源目录里的 DLL
不会被宿主锁住，运行中可以覆盖 / 删除源 DLL；**新文件只在下次启动时生效**。

## 缺失插件与占位卡

- 插件缺失 / 加载失败时，宿主不会把卡片回退成默认卡，而是按原 Kind 显示占位卡
  「插件不可用：example-clock」；占位卡不会出现在「添加卡片」菜单。
- 配置里的 `Kind` / `Content` / `Extras` 原样保留、**不写回改写**；插件重新装回并重启后恢复。
- `--selftest` 覆盖占位卡行为。

## manifest 校验与 API version

宿主会校验：

- `manifest.json` ≤ 256 KB；`id` ≤ 100 字符、只允许字母数字与 `.-_`；`apiVersion` 必须为 2；
  `assembly` 只接受裸 `.dll` 文件名；`entryType` 非空、不含空白；
- 入口类型必须存在、非抽象 / 接口、实现 `ICardModule`，且有 public 无参构造；
- 卡片 `Kind` ≤ 64 字符、只允许字母数字与 `.-_`；同批扫描中重复 `id` / 重复 `Kind` 会被拒绝；
- 引用宿主 `MirrorpondHUD.dll` 的插件会被明确拒绝。

本示例的 `manifest.json` 满足这些约束：`version = 0.2.0`、`apiVersion = 2`、`Kind = example-clock`。

## 边界

- **没有热重载**：替换 DLL / manifest 后必须重启宿主。
- 插件以当前用户权限运行，`AssemblyLoadContext` **不是安全沙箱**；只安装来源可信的本地插件。
- 没有签名 / 哈希 / 白名单校验；影子复制与 manifest 校验只是可靠性措施，不改变信任模型。
- 插件必须与宿主使用同一个 `net8.0-windows` / WPF 目标；`apiVersion` 目前只接受 `2`。
- 插件工程引用 `MirrorpondHUD.Contracts`（要用共享控件时再加 `MirrorpondHUD.UiKit`），不要复制这两个 DLL；
  2.14.4 起旧插件不再兼容，需重编并升 `apiVersion=2`。
