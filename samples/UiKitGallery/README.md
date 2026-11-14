# UiKit 控件画廊（示例插件）

`samples/UiKitGallery` 是一个**独立示例插件**：它用 `RichCardModuleBase` 把 UiKit 的公共控件
实际实例化、排版成一张可滚动的画廊，演示按钮 / 输入 / 列表 / 表格 / 图表 / 树 / 时间线 / 表单等
控件怎么用、主题怎么跟卡片走。

> **这不是内置卡**：示例不参与宿主构建（宿主工程只收集 `src/**`），只能像用户插件一样放进
> `plugins/cards/sample.uikitgallery/` 后由 `CardPluginLoader` 加载。

## 构建

```bat
dotnet build samples\UiKitGallery\UiKitGallery.csproj -c Release
```

- 工程会通过 `ProjectReference` 自动构建 `MirrorpondHUD.Contracts` 与 `MirrorpondHUD.UiKit`
  （两者都设了 `Private=false`，输出目录不会复制它们的 DLL；运行时由宿主提供）。
- 不构建宿主，也没有任何 NuGet 依赖。
- 输出目录：`samples\UiKitGallery\bin\Release\`，其中 `UiKitGallery.dll` 与 `manifest.json`
  就是安装所需文件（`manifest.json` 由工程自动复制到输出目录）。

## 安装

便携版（复制到 exe 旁边的插件目录）：

```bat
mkdir bin\Release\plugins\cards\sample.uikitgallery
copy samples\UiKitGallery\bin\Release\UiKitGallery.dll      bin\Release\plugins\cards\sample.uikitgallery\
copy samples\UiKitGallery\bin\Release\UiKitGallery.deps.json bin\Release\plugins\cards\sample.uikitgallery\
copy samples\UiKitGallery\bin\Release\manifest.json         bin\Release\plugins\cards\sample.uikitgallery\
```

用户级目录（exe 目录不可写、或想让多个副本共用时）：

```text
%LOCALAPPDATA%\MirrorpondHUD\plugins\cards\sample.uikitgallery\
  manifest.json
  UiKitGallery.dll
  UiKitGallery.deps.json
```

注意：

- **不要**把 `MirrorpondHUD.Contracts.dll` / `MirrorpondHUD.UiKit.dll` 复制进插件目录；
  宿主用默认 ALC 提供这两个程序集，复制反而可能造成类型标识不一致。
- 插件**没有热重载**：安装 / 更新 / 删除都必须重启宿主。
- 目录名建议与 manifest 的 `id` 一致（`sample.uikitgallery`）。

## 重启后添加卡片

1. 重启 `bin\Release\MirrorpondHUD.exe`；
2. 日志里应出现类似 `已加载卡片插件 UiKit 控件画廊 0.1.0（Kind=sample.uikitgallery…）` 的记录；
3. 打开「设置 → 内容 → 添加卡片」，在列表里选择「UiKit 控件画廊」；
4. 卡片写入的 `Kind` 是 `sample.uikitgallery`，删除插件后会按未知 Kind 显示占位卡，配置不会丢。

## 控件覆盖清单

画廊实际实例化的公共控件（共 27 个，另用 `CardTextBox` 作为 `CardInput` 的内部输入框）：

| 分区 | 控件 | 演示内容 |
| --- | --- | --- |
| 头部 | `CardHeader` / `CardToolbar` / `CardIcon` | 标题、徽标、右侧动作按钮（刷新图表 / 重置演示）与 Lucide 分区图标 |
| 指标 | `CardMetricTile` / `CardGauge` / `CardProgressRing` | 趋势 + 语气色指标块、仪表盘、环形进度 |
| 徽标 | `CardBadge` / `CardTag` / `CardStatus` | 五种语气徽标、可移除标签、状态行（后续交互都写到这里） |
| 列表 | `CardList` | 空状态、选中、双击 / 回车激活 |
| 表格 | `CardTable` | 列宽 / 对齐、行数据、选中、`SortBy` 排序 |
| 图表 | `CardSparkline` / `CardLineChart` / `CardBarChart` | 迷你趋势线、多序列折线、分组柱状图（本地正弦波数据） |
| 树 | `CardTree` | 展开 / 收起、根节点、选中事件 |
| 时间线 | `CardTimeline` | 时间、标题、语气、选中事件 |
| 表单 | `CardSettingsForm` / `CardSlider` / `CardToggle` / `CardSelect` / `CardNumberInput` / `CardColorPicker` / `CardSegmentedControl` / `CardFormRow` | 声明式表单一次排好控件，改值只更新状态行 |
| 输入 | `CardInput` / `CardButton` | 回车提交 / 清空、三种语气按钮与禁用态 |
| 附加 | `CardDropOverlay` | 按钮演示拖放提示层的 `Show` / `Hide` |

## 主题与交互约定

- 颜色和字号全部来自 `context.Settings.Style`：先 `ThemeResolver.Resolve(style)` 取
  Accent / Danger / Muted / Border / Panel / Scrollbar 令牌，前景色用公开的
  `ColorCode.TryParse` 解析；代码里不写死颜色常量。改「设置 → 外观」后重建卡片内容即可看到变化。
- 按钮 / 输入 / 滑块 / 列表 / 表格 / 树 / 时间线的状态全部留在画廊视图对象里：
  不读写配置文件、不访问宿主内部类型，宿主重建内容时会自动归零。
- 只使用 `MirrorpondHUD.Contracts` / `MirrorpondHUD.UiKit` 的公开 API，
  不 `using` 宿主命名空间，也不依赖 `src/Plugins/**` 里的任何类型。

## manifest

```json
{
  "id": "sample.uikitgallery",
  "name": "UiKit 控件画廊",
  "version": "0.1.0",
  "apiVersion": 2,
  "entryType": "UiKitGallery.GalleryCard",
  "assembly": "UiKitGallery.dll",
  "author": "sample"
}
```

宿主会校验：`apiVersion` 必须为 2、`id` 只允许字母数字与 `.-_`、`assembly` 只能是裸 DLL 文件名、
入口类型必须存在且实现 `ICardModule`；引用宿主 `MirrorpondHUD.dll` 的插件会被拒绝。

## 边界

- 示例插件与内置卡走同一条加载路径，但**不是内置卡**：宿主自检的「插件自检」不会把它算进 6 个内置模块。
- 插件以当前用户权限运行，`AssemblyLoadContext` 不是安全沙箱；只安装可信的本地插件。
- 目标框架必须与宿主一致（`net8.0-windows` / WPF）；`apiVersion=2`。
