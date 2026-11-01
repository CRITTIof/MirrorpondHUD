# 运行时卡片示例

这是 `plugins/cards/*.dll` 第一版（可信本地插件 + 重启生效）的最小示例。

## 构建

先构建宿主，确保 `bin\Release\MirrorpondHUD.dll` 存在：

```bat
dotnet build -c Release
dotnet build samples\RuntimeCardExample\RuntimeCardExample.csproj -c Release
```

## 安装（便携版）

把示例输出复制到 exe 的插件目录：

```bat
mkdir bin\Release\plugins\cards\example-clock
copy samples\RuntimeCardExample\bin\Release\RuntimeCardExample.dll bin\Release\plugins\cards\example-clock\
copy samples\RuntimeCardExample\bin\Release\RuntimeCardExample.deps.json bin\Release\plugins\cards\example-clock\
copy samples\RuntimeCardExample\manifest.json bin\Release\plugins\cards\example-clock\
```

启动 `bin\Release\MirrorpondHUD.exe`，日志应出现：

```text
已加载卡片插件 Example Clock 0.1.0（Kind=example-clock ...）
已注册卡片模块 ...
```

然后在「设置 → 内容 → 添加卡片」里应看到「示例插件时钟」。添加后写入的 Kind 是
`example-clock`，卸载插件时宿主会保留该 Kind 与 Extras，不会静默改成默认卡。

## 边界

- 第一版只支持重启生效，不支持热插拔/卸载；
- 插件以当前用户权限运行，ALC 不是安全沙箱；
- 插件必须与宿主使用同一个 `net8.0-windows` / WPF 目标；
- `apiVersion` 目前只接受 `1`。
