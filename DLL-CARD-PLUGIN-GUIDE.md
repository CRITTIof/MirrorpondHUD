# DLL 卡片插件开发指南（2.20.0+）

本文面向要在 MirrorpondHUD 里添加**运行时卡片插件**的开发者。插件是一个独立的 .NET 8 WPF 类库（DLL），
通过 `plugins/cards/<id>/manifest.json` 被宿主加载，可以贡献文字卡或富内容卡，并复用宿主的设置页、
热键、拖放、状态存储和 `MirrorpondHUD.UiKit` 里的主题化控件。

> 当前插件 API 版本为 **2**。本文是自包含的制作指南：只读本文即可完成 DLL 卡片，不要求阅读宿主源码。
> 2.14.4 起不再兼容 2.14.3 及更早编译的插件；旧插件必须重新编译并引用 `MirrorpondHUD.Contracts`。
> 插件是**可信本地代码**，`AssemblyLoadContext` 只是加载隔离，不是安全沙箱。
>
> **2.20.0 起没有热插拔 / 热重载**：安装、更新、删除插件都通过替换插件目录并**重启宿主**生效；
> 没有 `--plugin-dev`，没有插件目录监听，也没有运行期替换注册表。
>
> **2.16.0 起，6 张内置卡片本身就是 DLL 插件**（`src/Plugins/*`，manifest id `builtin.*`），宿主不再内置任何
> `ICardModule`；你的用户插件和内置插件走同一条加载路径、同一套契约与宿主服务。

---

## 0. 内置卡片就是插件（2.16.0+）

从 2.16.0 起，宿主程序集里**没有**任何具体卡片实现：信息与文本 / 命令行 / 媒体 / AI 对话 / 文件中转站 / 按钮演示
6 张内置卡都在 `src/Plugins/<Name>/` 里，各自编成 `MirrorpondHUD.Cards.<Name>.dll` + `manifest.json`
（id `builtin.*`，`apiVersion=2`）。宿主构建 / 发布时把它们复制到 `<exe>/plugins/cards/builtin.*/`，
启动时和用户插件一起由 `CardPluginLoader.LoadAll()` 加载；干净环境的启动日志是
「已注册卡片模块 6 个（内置 0，运行时插件 6）」。

对你的插件意味着：

- 加载行为、影子复制、manifest 校验、ALC 隔离与内置插件完全一致；
- 内置插件与用户插件走同一条启动加载路径；替换后重启宿主生效；
- 不要依赖「宿主里一定有某个内置卡片类型」——所有卡片（包括内置卡）都经由契约程序集交互；
- 新增内置卡 = 在 `src/Plugins` 新增一个插件工程，不需要改宿主。

## 1. 能力边界

插件可以：

- 新增一种或多种卡片（`ICardModule` / `IRichCardModule`），出现在「添加卡片」菜单里；
- 使用宿主采样数据、时间、按键历史、设置和模板变量渲染文字；
- 用任意 WPF 内容做富卡片（图片、图表、列表、表单、按钮、媒体等）；
- 提供设置数据结构与设置页 UI（`ICardSettingsModule` / `ICardEditorSection`）；
- 声明通用热键（`ICardHotkeySource` / `ICardHotkeyTarget`）；
- 接收拖入 / 提供拖入反馈（`ICardDropTarget` / `ICardDropFeedback`）；
- 读写每张卡片自己的状态文件（`ICardHost`）；
- 使用 `MirrorpondHUD.UiKit` 中主题化的按钮、输入框、列表、状态行、表单控件。

插件不能：

- 直接操作宿主窗口、叠层、毛玻璃、托盘、设置页整体布局；
- 任意注册全局热键、弹系统对话框、打开文件选择器、发托盘通知（目前没有公开服务 API）；
- 访问宿主内部类型或私有状态；
- 突破 ALC 安全边界——插件和宿主同进程、同用户权限。

---

## 2. 工程配置

### 2.1 目标框架

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<OutputType>Library</OutputType>
```

插件必须和宿主使用同一个 `net8.0-windows` / WPF 目标，否则契约类型无法统一。

### 2.2 引用 Contracts（必须）和 UiKit（可选）

插件**只引用契约程序集**，不要引用宿主 `MirrorpondHUD.dll`：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Contracts\MirrorpondHUD.Contracts.csproj">
    <Private>false</Private>
  </ProjectReference>

  <!-- 需要 CardButton / CardList / CardSlider 等共享控件时再加 -->
  <ProjectReference Include="..\..\src\UiKit\MirrorpondHUD.UiKit.csproj">
    <Private>false</Private>
  </ProjectReference>
</ItemGroup>
```

`Private=false` 表示不要把 Contracts / UiKit 的 DLL 复制进插件目录——运行时由宿主的默认 ALC 提供。
插件自己的第三方依赖可以正常复制到插件目录，由插件的可收集 ALC 加载。

如果插件工程不在宿主源码树里，也可以直接引用**宿主发布目录里的程序集**（不需要源码）：

```xml
<ItemGroup>
  <Reference Include="MirrorpondHUD.Contracts">
    <HintPath>C:\Path\To\MirrorpondHUD\MirrorpondHUD.Contracts.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <Reference Include="MirrorpondHUD.UiKit">
    <HintPath>C:\Path\To\MirrorpondHUD\MirrorpondHUD.UiKit.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

两种方式二选一即可。**不要把 `MirrorpondHUD.Contracts.dll` / `MirrorpondHUD.UiKit.dll` 复制进插件目录**：
它们由宿主默认 ALC 提供；插件目录只放自己的 DLL、manifest 和必要的第三方依赖。

宿主和插件必须使用同一个 `net8.0-windows` / WPF 运行时；不要用 `net8.0`（非 Windows）或另一套 WPF 版本。

### 2.3 项目文件完整示例

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <OutputType>Library</OutputType>
    <Nullable>disable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>MyCard</AssemblyName>
    <RootNamespace>MyCard</RootNamespace>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <DebugType>portable</DebugType>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Contracts\MirrorpondHUD.Contracts.csproj">
      <Private>false</Private>
    </ProjectReference>
    <ProjectReference Include="..\..\src\UiKit\MirrorpondHUD.UiKit.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>
</Project>
```

（外部工程把两个 `ProjectReference` 换成上面的 `Reference` + `HintPath`；不需要 UiKit 时删掉 UiKit 引用。）


### 2.4 manifest.json

```json
{
  "id": "com.example.my-card",
  "name": "My Card",
  "version": "1.0.0",
  "apiVersion": 2,
  "entryType": "MyCard.MyCardModule",
  "assembly": "MyCard.dll",
  "author": "example"
}
```

字段规则：

| 字段 | 规则 |
|---|---|
| `id` | 稳定插件 id，≤100 字符，只允许字母数字与 `.` `-` `_` |
| `name` / `version` | 展示与排错用 |
| `apiVersion` | 必须是 **2** |
| `entryType` | 入口类型全名，必须实现 `ICardModule`，public 无参构造 |
| `assembly` | 裸 DLL 文件名，不能带路径 |
| `author` | 可选展示信息 |

插件目录：

```text
<exe>/plugins/cards/<id>/         便携目录（推荐）
%LOCALAPPDATA%\MirrorpondHUD\plugins\cards\<id>/   用户级目录
  manifest.json
  MyCard.dll
  MyCard.deps.json
  <依赖>.dll
```

宿主会扫描两个根目录；插件加载前会影子复制到 `%LOCALAPPDATA%\MirrorpondHUD\plugin-cache`，
所以源 DLL 不会被锁住，可以在宿主运行时覆盖/删除。

---

## 3. 最小文字卡

```csharp
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MyCard
{
    public sealed class MyCardModule : CardModuleBase
    {
        public override string Kind { get { return "my-card"; } }
        public override string DisplayName { get { return "我的卡片"; } }
        public override string Icon { get { return "\uE100"; } }
        public override string DefaultContent { get { return "CPU {cpu:0.0}%"; } }
        public override int RefreshIntervalMs(CardSettings config) { return 1000; }

        public override string Render(CardRenderContext context)
        {
            return "CPU " + context.Snapshot.CpuPercent.ToString("0.0") + "%";
        }
    }
}
```

`Render` 可访问：

- `context.Settings`：全局设置（外观、CLI、采样、行为、热键）；
- `context.Snapshot`：CPU/GPU/内存/磁盘/网络/分区等采样；
- `context.Keys`：按键历史；
- `context.Config`：这张卡片的 `CardSettings`；
- `context.Variables`：模板变量表；
- `context.Render(template)` / `RenderOrDefault(...)`：套用模板。

文字卡可以用 `Context.Render("{time} {cpu:0.0}%")`，也可以自己拼字符串；不要做阻塞 UI 的慢操作。

### 3.1 卡片元数据

`ICardModule` 还可声明：

- `Order`：添加菜单排序；
- `DefaultResident` / `DefaultHotkey`：新卡默认是否常驻 / 呼出热键；
- `IsInteractive`：键盘独占卡片（同时只能有一张）；
- `HandlesOwnDrag`：内容自己处理鼠标拖动；
- `SupportsContent`：是否允许在设置页编辑内容模板；
- `RefreshIntervalMs`：文字卡最小重画间隔。

### 3.2 图标

`Icon` 使用 ReiconFont 码位，推荐直接引用 `MirrorpondHUD.Core.Icons`：

| 常量 | 用途 | 常量 | 用途 |
|---|---|---|---|
| `Icons.Terminal` | 命令行 | `Icons.Cpu` | CPU |
| `Icons.Web` / `Icons.App` / `Icons.Game` | 通用应用 | `Icons.Gpu` / `Icons.Ram` / `Icons.Rom` | 硬件 |
| `Icons.Folder` / `Icons.Txt` / `Icons.Image` | 文件 / 文本 / 图片 | `Icons.NetUp` / `Icons.NetDown` | 网络 |
| `Icons.Agent` | AI / 对话 | `Icons.Volume` / `Icons.Microphone` | 音频 |
| `Icons.Add` / `Icons.Search` / `Icons.Trash` / `Icons.Move` | 操作 | `Icons.Video` / `Icons.Clock` / `Icons.Play` / `Icons.PauseMedia` | 媒体 |

也可以直接写任意 ReiconFont 码位（`""` 等）；不要用普通 Unicode emoji，宿主卡片字体里没有对应字形时会变成方框。

---

## 4. 富内容卡

```csharp
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MyCard
{
    public sealed class MyRichCard : RichCardModuleBase
    {
        public override string Kind { get { return "my-rich-card"; } }
        public override string DisplayName { get { return "我的富卡片"; } }
        public override string Icon { get { return "\uE106"; } }
        public override bool NeedsMouseInput { get { return true; } }

        public override FrameworkElement CreateContent(CardRenderContext context)
        {
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "CPU " + context.Snapshot.CpuPercent.ToString("0.0") + "%"
            });
            return panel;
        }
    }
}
```

宿主负责：

- 把 `FrameworkElement` 放进卡片窗口；
- 卡片外壳的背景、毛玻璃、圆角、阴影、点击穿透；
- 调用 `UpdateContent`、`OnVisibilityChanged`、`OnZOrderChanged`；
- 在配置变化时重建内容（实现了 `ICardStyleAware` 时优先就地换肤）。

富内容卡建议把 UI 做成可重建的轻量控件；视频/动图/后台任务要在 `OnVisibilityChanged`
或 `ICardModuleLifetime.OnPluginUnloading` 里停掉。

### 4.1 `NeedsMouseInput`

- `false`：纯展示卡，窗口默认点击穿透；
- `true`：卡片内按钮/输入框/滑块可点，但卡片会挡住下方桌面。

---

## 5. 设置与表单控件

### 5.1 设置数据用 `CardSettings.Extras`

```csharp
public sealed class MySettings
{
    public bool Enabled = true;      // 必须是 public field，不能是属性
    public double Volume = 0.5;
    public string Mode = "Auto";
}

public override void InitializeCard(CardSettings config)
{
    MySettings settings = config.ExtrasAs<MySettings>("my-card");
    settings.Volume = Math.Max(0, Math.Min(1, settings.Volume));
    config.SetExtras("my-card", settings);
}
```

> `MirrorpondHUD.Core.Json` 通过反射写 **public 字段**，不写属性。DTO 用自动属性会静默丢失。
> 支持的类型：`string` / `bool` / 整数 / `double` / `float` / `decimal` / `enum`（存字符串）、
> `List<T>` / `T[]`、`Dictionary<string, object>` 以及只含 public field 的嵌套 DTO。不要用 `DateTime`、
> `Color`、WPF 控件等无法 JSON 往返的类型。
>
> 从磁盘读进来时 `Extras` 里是原始 JSON 节点；`ExtrasAs<T>` 返回绑定后的实例（可能是副本），
> 修改后必须 `config.SetExtras(key, dto)` 写回，否则不会保存。

### 5.2 设置页 UI：`ICardEditorSection`

```csharp
using System;
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

public sealed class MyEditor : ICardEditorSection
{
    public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged)
    {
        MySettings data = config.ExtrasAs<MySettings>("my-card");

        var form = new CardSettingsForm();
        var volume = form.AddSlider("音量", 0, 1, data.Volume);
        var enabled = form.AddToggle("启用", data.Enabled);
        var mode = form.AddSelect("模式", new[] { "Auto", "Manual" }, data.Mode);

        volume.ValueChanged += value =>
        {
            data.Volume = value;
            config.SetExtras("my-card", data);
            onChanged();
        };
        enabled.Checked += (s, e) => { data.Enabled = true; config.SetExtras("my-card", data); onChanged(); };
        enabled.Unchecked += (s, e) => { data.Enabled = false; config.SetExtras("my-card", data); onChanged(); };
        mode.SelectionChanged += value =>
        {
            data.Mode = value;
            config.SetExtras("my-card", data);
            onChanged();
        };

        form.ApplyStyle(settings.Style, settings.Overlay.CornerRadius);
        return form;
    }
}
```

模块同时实现 `ICardEditorSection` 即可自动在设置页出现自定义区块。还可以实现：

- `ICardSettingsValidator.Validate(config)`：返回错误文案或 null；
- `ICardSummary.Summary(config)`：设置页标题右侧摘要；
- `ICardSettingsModule.InitializeCard(config)`：补默认值、夹紧范围。

---

## 6. `MirrorpondHUD.UiKit` 控件（表单 / 媒体 / 数据展示）

`MirrorpondHUD.UiKit` 的控件都使用卡片外观令牌；内容控件在创建 / `ApplyStyle` 时调用：

```csharp
control.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
```

### 6.0 基础控件索引

| 控件 | 常用 API | 用途 |
|---|---|---|
| `CardButton` | `new CardButton(text, CardButtonTone.Normal/Primary/Danger)`、`Text`、`Click`、`ApplyStyle` | 卡片内按钮 |
| `CardTextBox` | `Text`、`KeyDown`、`ApplyStyle` | 单行输入框 |
| `CardInput` | `Text`、`Placeholder`、`Submitted`、`TextChanged`、`Submit`、`Clear`、`FocusInput`、`SetError`、`ApplyStyle` | 带提交 / 错误态的输入行 |
| `CardList` | `Items`、`ItemsSource`、`ItemTemplate`、`SelectedItem`、`SelectedItems`、`SelectionMode`、`ItemActivated`、`SetEmptyText`、`ApplyStyle(style, cli)` | 虚拟化列表 |
| `CardStatus` | `SetText(text, CardStatusKind)`、`SetBusy`、`SetProgress`、`ApplyStyle` | 状态 / 进度 / 错误行 |
| `CardHeader` | `Title`、`Badge`、`Actions.Add(text, click, tone, tooltip)`、`ApplyStyle` | 标题 + 动作按钮 |
| `CardDropOverlay` | `Show(count|text, accepted)`、`Hide`、`ApplyStyle` | 拖入高亮层 |
| `CardSlider` | `Value`、`Minimum`、`Maximum`、`Step`、`ValueChanged` | 数值滑块 |
| `CardToggle` | `IsChecked`、`Checked` / `Unchecked` | 开关 |
| `CardSelect` | `ItemsSource`、`SelectedItem`、`SelectedIndex`、`SelectionChanged` | 下拉 |
| `CardNumberInput` | `Value`、`Minimum`、`Maximum`、`ValueChanged` | 数字输入 |
| `CardColorPicker` | `Value`、`ValueChanged` | 颜色选择 |
| `CardSegmentedControl` | `Items`、`SelectedIndex`、`SelectedItem`、`SelectionChanged` | 分段选择 |
| `CardFormRow` / `CardSettingsForm` | `AddSlider` / `AddToggle` / `AddSelect` / `AddNumber` / `AddColor` / `AddSegmented` / `AddDescription` / `AddDivider` | 声明式设置表单 |
| `CardMediaView` + `MediaSettings` | `Bind(settings)`、`Play/Pause`、`ShowPoster`、`HandleHotkey`、`ApplyStyle` | 图片 / 动图 / 视频 |
| `CardTable` | `AddColumn` / `AddRow` / `ItemsSource` / `SortBy` / `SelectedItem` / `SelectionMode` / `ItemActivated` / `SetEmptyText` | 虚拟化表格 |
| `CardBadge` / `CardTag` / `CardMetricTile` | `Text` / `Tone` / `Value` / `Trend` / `ApplyStyle` | 徽标 / 标签 / 指标块 |
| `CardGauge` / `CardProgressRing` | `Value`、`Label`、`Unit`、`ValueFormat`、`ApplyStyle` | 仪表 / 圆环 |
| `CardSparkline` / `CardLineChart` / `CardBarChart` | `SetValues` / `Series` / `Labels` / `ApplyStyle` | 趋势 / 折线 / 柱状 |
| `CardTree` / `CardTimeline` | `AddRoot` / `Add` / `SelectedItem` / `ItemActivated` / `ApplyStyle` | 树 / 时间线 |
| `CardModal` / `CardDialog` | `Show(host)` / `ShowAsync(host)` / `Close` / `ApplyStyle` | 卡片内模态 |
| `CardPopover` / `CardTooltip` | `ShowAt(target)` / `Attach(target)` / `Close` / `Detach` | 浮层 / 悬停提示 |
| `CardContextMenu` | `Add` / `AddSeparator` / `AddHeader` / `ShowAt(target)` / `ApplyStyle` | 右键菜单 |
| `CardToast` | `CardToast.Show(host, text, tone, duration, actionText, action)` / `Dismiss` | 非阻塞通知 |

控件全部在命名空间 `MirrorpondHUD.Ui.Controls`；媒体控件在
`MirrorpondHUD.Ui.Controls.Media`（`CardMediaView` / `MediaSettings`）。


所有表单控件都在 `MirrorpondHUD.Ui.Controls`，实现 `ICardFormControl`：

```csharp
public interface ICardFormControl
{
    void ApplyStyle(StyleSettings style, double cornerRadius);
}
```

| 控件 | 主要 API | 用途 |
|---|---|---|
| `CardSlider` | `CardSlider(label, min, max, value)`、`Value`、`ValueChanged` | 音量 / 透明度 / 阈值 |
| `CardToggle` | `CardToggle(text)`、`IsChecked`、`Checked`/`Unchecked` | 开关 |
| `CardSelect` | `CardSelect(label, options)`、`SelectedItem`、`SelectionChanged` | 下拉选择 |
| `CardNumberInput` | `CardNumberInput(label, value, min, max)`、`Value`、`ValueChanged` | 数值 |
| `CardColorPicker` | `CardColorPicker(label, "#FF2F6FEB")`、`Value`、`ValueChanged` | 颜色 |
| `CardSegmentedControl` | `CardSegmentedControl(label, options)`、`SelectedIndex`、`SelectionChanged` | 分段选择 |
| `CardFormRow` | `new CardFormRow(label, content)` | 一行标题 + 控件 |
| `CardSettingsForm` | `AddRow` / `AddSlider` / `AddToggle` / `AddSelect` / `AddNumber` / `AddColor` / `AddSegmented` / `AddDescription` / `AddDivider` | 声明式设置表单 |

`CardSettingsForm.ApplyStyle(style, cornerRadius)` 会递归应用到所有行和控件。

### 6.1 主题与换肤

卡片外壳由宿主绘制，插件内容区需要自己处理样式。推荐：

```csharp
public sealed class MyRichCard : RichCardModuleBase, ICardStyleAware
{
    CardSettingsForm _form;

    public override FrameworkElement CreateContent(CardRenderContext context)
    {
        _form = new CardSettingsForm();
        _form.AddSlider("音量", 0, 1, 0.5);
        _form.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
        return _form;
    }

    public void ApplyStyle(FrameworkElement content, CardRenderContext context)
    {
        var form = content as CardSettingsForm;
        if (form != null) form.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
    }
}
```

其他主题工具：

- `ThemeResolver.Resolve(StyleSettings)` → `ThemeTokens`（Accent/Danger/Muted/Border/Panel/Scrollbar/MonoFont）；
- `CardPalette.TryColor / WithAlpha / Brush / ResolveFont`；
- `CliScrollBars.Apply(scrollViewer, thumb, hot)` 统一滚动条；
- `CardList` / `CardStatus` / `CardButton` / `CardInput` 等自带 `ApplyStyle`。

---

### 6.2 媒体控件（2.16.0 起在 UiKit）

媒体卡片的图片 / 动图 / 视频渲染控件从宿主 `MediaView` 迁入 UiKit，更名为 `CardMediaView`，命名空间
`MirrorpondHUD.Ui.Controls.Media`，同目录还有 `MediaSettings` / `GifAnimator` / `FfmpegPlayer` / `VideoWindow` /
`WaveOutPlayer` / `Decoders`。插件要用媒体能力时引用 UiKit 即可：

```csharp
using MirrorpondHUD.Ui.Controls.Media;

var view = new CardMediaView();
var media = new MediaSettings { Path = "C:\\media\\a.mp4", Fit = "Cover", Loop = true, Muted = true };
media.Normalize();
view.Bind(media);
```

`CardMediaView` 负责文件名行、进度条与圆角内边距；视频子窗口会贴到卡片上（分层窗口画不出 `MediaElement`），
因此视频区域的鼠标事件归子窗口。`MediaCard` 内置插件就是使用它实现的。

### 6.3 数据展示控件（2.17.0 起在 UiKit）

面向监控、行情、日志、任务、文件列表这类信息密度高的卡片。全部实现
`ApplyStyle(StyleSettings style, double cornerRadius)`，换肤时逐个调用即可。

| 控件 | 主要 API | 用途 |
|---|---|---|
| `CardTable` | `AddColumn` / `AddRow` / `ItemsSource` / `SortBy` / `SelectedItem` / `SelectionMode` / `ItemActivated` / `SetEmptyText` | 虚拟化表格：表头、列宽、点击表头排序、单选/多选、空态 |
| `CardBadge` | `new CardBadge(text, tone)`、`Text`、`Tone` | 状态、计数、只读标记 |
| `CardTag` | `new CardTag(text, tone)`、`Removable`、`Removed` | 胶囊标签；可带“×”移除 |
| `CardMetricTile` | `Title` / `Value` / `Unit` / `Delta` / `Caption` / `Icon` / `Trend` / `Tone` | 监控 KPI 块：大数值 + 趋势 |
| `CardGauge` | `Value` / `Minimum` / `Maximum` / `Label` / `Unit` / `ValueFormat` | 280° 单值仪表 |
| `CardProgressRing` | `Value` / `Label` / `ShowValue` / `RingThickness` / `ValueFormat` | 圆环进度 / 配额 / 完成度 |

表格示例（`CardTableItem` 按列下标放 Cells；也可以用 `ItemsSource` + 自定义列 `Value`）：

```csharp
using MirrorpondHUD.Ui.Controls;

var table = new CardTable { Width = 340, Height = 180 };
table.AddColumn("文件", null, null, new GridLength(1, GridUnitType.Star));
table.AddColumn("状态", null, null, new GridLength(72));
table.AddColumn("大小", null, null, new GridLength(72), TextAlignment.Right);
table.AddRow("MirrorpondHUD.exe", "运行中", "1.8 MB");
table.AddRow("mirrorpond.log", "写入", "248 KB");
table.SortBy(0, false);
table.SelectionMode = SelectionMode.Extended;
table.ItemActivated += item => { /* CardTableItem.Tag 或自定义数据 */ };
table.SetEmptyText("暂无文件");
table.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
```

指标块 / 仪表示例：

```csharp
var cpu = new CardMetricTile("CPU", "42")
{
    Unit = "%", Delta = "+3.2", Caption = "较上次",
    Trend = CardMetricTrend.Up, Tone = CardBadgeTone.Accent
};
cpu.ApplyStyle(style, cornerRadius);

var gauge = new CardGauge { Value = 62, Label = "CPU", Unit = "%" };
gauge.ApplyStyle(style, cornerRadius);

var ring = new CardProgressRing { Value = 76, Label = "完成" };
ring.ApplyStyle(style, cornerRadius);
```

说明：

- `CardTable` 内部使用 `ListCollectionView` + 虚拟化 `ListBox`，数据量大时只创建可视行；
- 排序默认按 `CardTableItem.Cells[i]` 或列 `SortKey` 比较，点表头会切换升/降序并触发 `SortChanged`；
- `CardTable` / `CardGauge` / `CardProgressRing` 都是 UiKit 控件，不依赖宿主；`CardBadge` / `CardTag` / `CardMetricTile` 可放在任意卡片内容里；
- 这些控件 2.17.0 只做数据展示，交互浮层（Dialog / Popover / ContextMenu / Toast）在后续版本补齐。

### 6.4 图表 / 树 / 时间线（2.18.0 起在 UiKit）

面向历史采样、流量、行情、进程树、命令历史、任务时间线。全部控件都实现
`ApplyStyle(StyleSettings style, double cornerRadius)`，并且是自绘 `FrameworkElement`
或主题化 ItemsControl，不依赖宿主窗口。

| 控件 | 主要 API | 用途 |
|---|---|---|
| `CardSparkline` | `SetValues` / `Add` / `Values` / `AutoScale` / `Minimum` / `Maximum` / `Fill` | 极简趋势线，无坐标轴 |
| `CardLineChart` | `Series`（`CardChartSeries`）/ `Labels` / `ShowGrid` / `ShowLegend` / `ShowYLabels` / `ShowXLabels` / `ValueFormat` | 多序列折线 / 面积图 |
| `CardBarChart` | `Series` / `Labels` / `ShowGrid` / `ShowLegend` / `ShowYLabels` / `ShowXLabels` | 分组柱状图 |
| `CardTree` | `Roots` / `AddRoot` / `SelectedItem` / `SelectionChanged` / `ItemActivated` / `ExpandAll` | 主题化树：进程树、目录树、JSON |
| `CardTimeline` | `Items` / `Add(time, title, detail, tone, icon)` / `Count` / `ScrollToEnd` / `ItemActivated` | 时间线：日志、命令历史、任务事件 |

```csharp
using MirrorpondHUD.Ui.Controls;

var spark = new CardSparkline { Width = 320, Height = 40, Fill = true };
spark.SetValues(new double[] { 12, 18, 14, 22, 28, 24, 31 });
spark.ApplyStyle(style, cornerRadius);

var line = new CardLineChart { Width = 480, Height = 220 };
line.Series = new List<CardChartSeries>
{
    new CardChartSeries("CPU")  { Values = new List<double> { 12, 30, 24, 55, 40 } },
    new CardChartSeries("内存") { Values = new List<double> { 40, 42, 44, 47, 50 } }
};
line.Labels = new List<string> { "00", "05", "10", "15", "20" };
line.ApplyStyle(style, cornerRadius);

var tree = new CardTree { Width = 300, Height = 200 };
var root = tree.AddRoot("MirrorpondHUD");
root.Add("Cards").Add("CardTable");
root.Add("Contracts");
tree.ApplyStyle(style, cornerRadius);

var timeline = new CardTimeline { Width = 360, Height = 200 };
timeline.Add("14:01", "启动完成", "已注册 6 个插件", CardTimelineTone.Accent);
timeline.Add("14:03", "警告", "媒体解码器回退", CardTimelineTone.Warning);
timeline.ApplyStyle(style, cornerRadius);
```

说明：

- 图表颜色来自 `ThemeTokens.Accent / Danger / Muted / Border`，`CardChartSeries.Color` 可覆盖；
- 空数据、`NaN` / `Infinity`、宽高 < 2 都不会抛异常；
- `CardTree` / `CardTimeline` 用回收式虚拟化容器，只创建可视行；深层树只保证顶层虚拟化；
- 直接修改 `Series` / `Values` 的 live List 不会自动重绘，改完调用 `SetValues` / `Add` / 重新赋值
  `Series` 或 `ApplyStyle` 触发 `InvalidateVisual`；
- 2.18.0 仍没有“成功色”令牌，`CardTimelineTone.Success` 复用 Accent。

### 6.5 浮层与交互（2.19.0 起在 UiKit）

数据控件让卡片“好读”，浮层控件让卡片“能操作”。配合 `ICardActivation`（卡片呼出 / 收起）和
`NeedsMouseInput`（关闭点击穿透），卡片可以从展示型升级成可操作的小应用。全部用卡片内 / 独立 Popup
的主题化控件，不建议用系统 `MessageBox`。

| 控件 | 主要 API | 用途 |
|---|---|---|
| `CardModal` | `Body` / `Show(host)` / `Close()` / `IsOpen` / `CloseOnBackdropClick` / `Closed` | 卡片内模态遮罩 + 居中面板；Esc / 点遮罩关闭 |
| `CardDialog` | `Title` / `Message` / `Buttons`（Ok / OkCancel / YesNo）/ `Task<bool> ShowAsync(host)` / `DialogResult` | 确认、删除、表单前确认 |
| `CardPopover` | `Content` / `ShowAt(target)` / `Close()` / `ApplyStyle` | 锚定详情面板 / 二级菜单 |
| `CardTooltip` | `Text` / `DelayMs` / `Attach(target)` / `Detach()` | 截断文本、图标说明、悬停提示 |
| `CardContextMenu` | `Add(text, action, shortcut, danger)` / `AddSeparator()` / `AddHeader()` / `ShowAt(target)` | 主题化右键菜单 |
| `CardToast` | `CardToast.Show(host, text, tone, duration, actionText, action)` / `Dismiss()` | 非阻塞反馈：已复制、保存失败、可撤销 |

```csharp
using MirrorpondHUD.Ui.Controls;

// 确认删除：卡片根 Grid 作为 host，模态层会盖住卡片内容
var dialog = new CardDialog
{
    Title = "删除这张卡片？",
    Message = "卡片配置与状态会一起删除，无法撤销。",
    Buttons = CardDialogButtons.YesNo,
    OkText = "删除",
    CancelText = "取消"
};
dialog.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
bool ok = await dialog.ShowAsync(this);   // this = 卡片内容的根 Panel

// 非阻塞反馈
CardToast.Show(this, "已复制 3 个文件路径", CardToastTone.Success, 2500);

// 右键菜单
var menu = new CardContextMenu();
menu.Add("打开", () => OpenFile(), "Enter");
menu.Add("复制路径", () => CopyPath());
menu.AddSeparator();
menu.Add("删除", () => DeleteFile(), null, danger: true);
menu.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);
menu.ShowAt(targetElement);
```

说明：

- `CardModal` / `CardDialog` 必须加在**卡片内容根 `Panel`**（Grid / StackPanel）里，才能覆盖内容区；
- `CardPopover` / `CardTooltip` / `CardContextMenu` 是 WPF `Popup` / `ContextMenu`（独立 HWND），
  目标元素必须已进入可视树；关闭 / 卡片销毁时要调 `Close()` / `Detach()` / `Dismiss()`，避免残留窗口和定时器；
- `CardToast.Show` 会自动加入宿主、底部居中、按顺序向上堆叠并自动消失；点击 Action 只执行回调，不自动关闭；
- `CardDialog` 的 Enter = 确认、Esc = 取消；重复 `ShowAsync` 返回同一个 Task，不会叠出第二层遮罩；
- 浮层颜色 / 字体 / 圆角都走 `StyleSettings`，`CardContextMenu` 的条目是自绘模板，不使用系统白底；
- 版本 2.19.0 不包含独立的成功色令牌，`CardToastTone.Success` 用内定的绿色，Warning 用内定黄色。

## 7. 宿主服务与状态

除了 `ICardHost`（状态目录 / 重画 / 布局 / `SaveSettings()` / `Shell`），2.16.0 起 Contracts 还提供一组
**通用宿主服务契约**（`src/Contracts/Cards/CardHostServices.cs`），宿主不认具体卡片类型，插件按需实现 / 使用：

| 契约 | 方向 | 用途 |
|---|---|---|
| `ICardShellService` | 宿主 → 插件 | 剪贴板（文件 / 文本）、文件图标、文件拖出、用默认程序打开 / 在资源管理器定位；通过 `ICardHost.Shell` 获取 |
| `ICardEditorCommit` | 插件 → 宿主 | 设置区块里的“失焦才提交”草稿（如 API Key 输入框）；宿主点确定前调用 `CommitPendingEdits()` |
| `ICardScreenshotProvider` | 插件 → 宿主 | 文档截图前把富内容切到可离屏渲染的状态（如视频卡画第一帧封面） |
| `ICardPresetSource` + `CardPreset` | 插件 → 宿主 | 通用预设下拉（信息与文本卡的四个预设）；宿主设置页不再认识具体卡片 |
| `ICardRuntimeSettingsMerge` | 插件 → 宿主 | 卡片有绕过设置窗直接写 live settings 的运行时字段（对话模式）时，保存前做三方合并 |
| `ICardSelfTest` | 插件 → 宿主 | 插件自带回归；`--selftest` 会逐个调用，返回 null / 空串表示通过 |

```csharp
public sealed class MyCard : RichCardModuleBase, ICardHostAware, ICardSelfTest, ICardPresetSource
{
    ICardHost _host;

    public void Attach(ICardHost host) { _host = host; }
    public void Detach(ICardHost host) { _host = null; }

    // 运行期改了 Extras 需要落盘时：bool saved = _host.SaveSettings();
    // 宿主服务（剪贴板 / 图标 / 拖出 / 打开）：_host.Shell.TryGetClipboardFiles(out var paths);

    public string RunSelfTest() => null;   // 返回失败说明字符串即视为失败
}
```

模块实现 `ICardHostAware` 可在建卡时拿到 `ICardHost`：

```csharp
public void Attach(ICardHost host)
{
    // 不要存进单例字段；存进当前内容控件或状态文件
}

public void Detach(ICardHost host) { }
```

`ICardHost`：

- `Config`：当前卡片配置（会换对象，不要长期缓存）；
- `StateDirectory`、`ReadState`、`WriteState`、`DeleteState`：原子状态文件；
- `RequestRender`、`RequestLayout`：数据/尺寸变化后通知宿主。

---

## 8. 热键、拖放、激活

### 8.1 热键

```csharp
public sealed class MyCard : CardModuleBase, ICardHotkeySource, ICardHotkeyTarget
{
    public IList<CardHotkeyAction> HotkeyActions
    {
        get { return new List<CardHotkeyAction> { new CardHotkeyAction("my.refresh", "刷新", "Ctrl+Alt+R") }; }
    }

    public void ExecuteHotkey(string action, FrameworkElement content) { /* ... */ }
}
```

### 8.2 拖放

```csharp
public sealed class MyCard : CardModuleBase, ICardDropTarget
{
    public bool CanDrop(IDataObject data) { return data.GetDataPresent(DataFormats.FileDrop); }
    public void Drop(IDataObject data, FrameworkElement content) { /* ... */ }
}
```

锁定（点击穿透）状态下窗口收不到拖放；需要拖入时先解锁。

### 8.3 交互卡

`IsInteractive=true` 的卡片键盘独占，同时只能存在一张；可实现 `ICardActivation` 在呼出/收起时初始化/收尾，
CLI 面板可实现 `IClifFocusTarget`（`CliPanelView` 已实现）。

---

## 9. 生命周期、重启与卸载

**没有热插拔 / 热重载。** 安装、替换、更新、删除插件都必须关闭宿主、替换
`plugins/cards/<id>/`，再重新启动。宿主不会监听插件目录，也没有 `--plugin-dev`。

生命周期约定：

- 模块实例是**单例**，一张卡的富内容控件按卡片分别创建；不要把某张卡的 `ICardHost` /
  `CardSettings` / 控件存进模块字段；
- `ICardHostAware.Attach(host)` 在卡片创建 / 应用设置时调用，`Detach(host)` 在删卡 / 退出时调用；
- 富内容控件实现 `IDisposable` 时，卡片销毁会调用 `Dispose()`；`OnVisibilityChanged(content, false)`
  适合暂停视频 / 动图 / 轮询；
- `ICardHost.RequestRender()` / `RequestLayout()` 通知宿主数据或尺寸变化；
- 退出时宿主先调用 `ICardModuleLifetime.OnPluginUnloading()`，再请求卸载可收集 ALC。这里必须停掉
  自己的线程 / 定时器、注销静态事件、释放文件句柄；否则 ALC 可能延迟回收；
- 插件的 `Content` / `Extras` / 状态文件在重启后继续有效；不会因为插件缺失被宿主改写。

更新插件的推荐步骤：

1. 关闭宿主；
2. 替换 `plugins/cards/<id>/` 下的 DLL / manifest / 依赖；
3. 启动宿主，看日志确认新版本已加载；
4. 如果新插件加载失败，旧配置仍保留，卡片显示「插件不可用」，修好后重启恢复。

> 影子复制意味着宿主运行时可以覆盖源 DLL，但**新 DLL 只在下次启动时加载**；不要指望运行中生效。

## 10. 构建、安装、调试

```bat
dotnet build MyCard\MyCard.csproj -c Release
mkdir bin\Release\plugins\cards\my-card
copy MyCard\bin\Release\MyCard.dll bin\Release\plugins\cards\my-card\
copy MyCard\bin\Release\MyCard.deps.json bin\Release\plugins\cards\my-card\
copy MyCard\manifest.json bin\Release\plugins\cards\my-card\
```

启动 `bin\Release\MirrorpondHUD.exe`，日志（`bin\Release\mirrorpond.log`）应出现：

```text
已加载卡片插件 My Card 1.0.0（Kind=my-card，entry=MyCard.MyCardModule，文字卡，影子副本）
运行时插件加载完成：成功 1 个，失败 0 个（com.example.my-card）
已注册卡片模块 ... my-card ...
```

常见失败：

| 日志 | 原因 |
|---|---|
| `插件引用了宿主程序集` | 改引用 `MirrorpondHUD.Contracts`，不要引用 `MirrorpondHUD.dll` |
| `API 版本不兼容 api=1（宿主支持 2）` | manifest 的 `apiVersion` 改为 2 |
| `入口类型不存在` / `不符合 ICardModule 契约` | `entryType` / 基类 / public 无参构造不对 |
| `Kind 重复 / 与其它插件冲突` | 注册表启动合并时后者被告警丢弃；换一个自己的 Kind |
| `assembly 字段非法` / `程序集不存在` | manifest 的 `assembly` 只能是裸 DLL 文件名且文件存在 |
| `manifest.json 解析为空` / `读取失败` | JSON 格式或大小（≤256KB） |

---

## 11. 安全与兼容性

当前插件 API 版本为 **2**。2.16.0 新增的宿主服务契约（`CardHostServices.cs`）与 `ICardHost.SaveSettings()` /
`Shell` 都是**加法**：旧插件不需要改 manifest，重新编译时引用最新 Contracts / UiKit 即可。

- 只加载**可信本地插件**；ALC 不是沙箱，插件可执行当前用户权限内的任意代码；
- 签名/哈希/白名单目前未实现；
- 插件 API 2：旧插件必须重编译；引用宿主的插件会被明确拒绝；
- 插件 DTO 必须使用 public field（`Json` 限制）；
- 插件卸载只发生在宿主退出时；请实现 `ICardModuleLifetime` 停线程 / 解绑事件，避免 ALC 延迟回收；
- 插件替换 / 更新后**重启宿主**生效；本项目不提供热插拔。

---

## 12. 契约签名速查（完整）

这一节是制作插件时真正需要的公开 API；不需要打开宿主源码。所有类型在
`MirrorpondHUD.Contracts`（命名空间 `MirrorpondHUD.Cards` / `MirrorpondHUD.Core` /
`MirrorpondHUD.Ui`）或 `MirrorpondHUD.UiKit`（`MirrorpondHUD.Ui.Controls`）里。

### 12.1 `ICardModule` / `CardModuleBase`

```csharp
public interface ICardModule
{
    string Kind { get; }
    string DisplayName { get; }
    string Icon { get; }
    string DefaultContent { get; }
    bool SupportsContent { get; }
    bool IsInteractive { get; }
    bool HandlesOwnDrag { get; }
    bool DefaultResident { get; }
    string DefaultHotkey { get; }
    int Order { get; }
    int RefreshIntervalMs(CardSettings config);
    string Render(CardRenderContext context);
}

public abstract class CardModuleBase : ICardModule
{
    public abstract string Kind { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
    public virtual string DefaultContent { get; }       // ""
    public virtual bool SupportsContent { get; }        // true
    public virtual bool IsInteractive { get; }          // false
    public virtual bool HandlesOwnDrag { get; }         // false
    public virtual bool DefaultResident { get; }        // true
    public virtual string DefaultHotkey { get; }        // ""
    public virtual int Order { get; }                   // 50
    public virtual int RefreshIntervalMs(CardSettings config); // 0 = 每个渲染 tick
    public abstract string Render(CardRenderContext context);
}
```

### 12.2 富内容卡

```csharp
public interface IRichCardModule : ICardModule
{
    bool NeedsMouseInput { get; }
    FrameworkElement CreateContent(CardRenderContext context);
    void UpdateContent(FrameworkElement content, CardRenderContext context);
    void OnVisibilityChanged(FrameworkElement content, bool visible);
    void OnZOrderChanged(FrameworkElement content);
}

public abstract class RichCardModuleBase : CardModuleBase, IRichCardModule
{
    public override bool SupportsContent { get; }       // false
    public virtual bool NeedsMouseInput { get; }        // false
    public override string Render(CardRenderContext context); // ""
    public abstract FrameworkElement CreateContent(CardRenderContext context);
    public virtual void UpdateContent(...);             // 空实现
    public virtual void OnVisibilityChanged(...);       // 空实现
    public virtual void OnZOrderChanged(...);           // 空实现
}
```

### 12.3 `CardRenderContext`

```csharp
public sealed class CardRenderContext
{
    public Settings Settings { get; }
    public MetricsSnapshot Snapshot { get; }
    public KeyHistory Keys { get; }          // 可能为 null
    public CardSettings Config { get; }
    public ICardHost Host { get; }           // 文字 / 截图路径为 null
    public IDictionary<string, object> Variables { get; }
    public string Render(string template);
    public string RenderOrDefault(string defaultContent);
}
```

### 12.4 `CardSettings` / `Extras`

```csharp
public sealed class CardSettings
{
    public string Id;          // 宿主生成的稳定标识
    public string Kind;        // 模块 Kind
    public string Name;
    public bool Resident;
    public string Hotkey;
    public double Spacing;
    public string Content;
    public string Preset;      // 通用预设字段，模块可自用
    public Dictionary<string, object> Extras;

    public T ExtrasAs<T>(string key) where T : class, new();
    public void SetExtras<T>(string key, T value) where T : class;
}
```

`Settings` 里常用的只读参考字段：

- `Settings.Style`：`StyleSettings`（`FontFamily` / `FontSize` / `Foreground` / `Accent` / `Danger` / `Padding` 等）
- `Settings.Overlay.CornerRadius` / `Shadow` / `BackgroundOpacity` 等外观参数
- `Settings.Cli`：CLI 面板尺寸（`MaxWidth` / `MaxHeight`）
- `Settings.Data`：采样配置
- `Settings.Hotkeys`：全局 / 卡片动作热键

### 12.5 设置 / 编辑器 / 样式 / 生命周期

```csharp
public interface ICardSettingsModule
{
    void InitializeCard(CardSettings config);
}

public interface ICardEditorSection
{
    FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged);
}

public interface ICardEditorCommit
{
    void CommitPendingEdits();
}

public interface ICardSettingsValidator
{
    string Validate(CardSettings config);   // null = 通过
}

public interface ICardSummary
{
    string Summary(CardSettings config);
}

public interface ICardStyleAware
{
    void ApplyStyle(FrameworkElement content, CardRenderContext context);
}

public interface ICardModuleLifetime
{
    void OnPluginUnloading();
}
```

### 12.6 宿主服务

```csharp
public interface ICardHostAware
{
    void Attach(ICardHost host);
    void Detach(ICardHost host);
}

public interface ICardHost
{
    CardSettings Config { get; }
    string StateDirectory { get; }
    string ReadState(string fileName);
    bool WriteState(string fileName, string text);
    bool DeleteState(string fileName);
    void RequestRender();
    void RequestLayout();
    bool SaveSettings();
    ICardShellService Shell { get; }
}

public interface ICardShellService
{
    bool TryGetClipboardFiles(out string[] paths);
    bool TrySetClipboardText(string text);
    ImageSource GetFileIcon(string path, int size, bool extensionOnly);
    DragDropEffects BeginDragFiles(FrameworkElement source, IList<string> paths, DragDropEffects allowed);
    void OpenPath(string path);
    void RevealPath(string path);
}
```

### 12.7 热键 / 拖放 / 激活

```csharp
public sealed class CardHotkeyAction
{
    public CardHotkeyAction(string action, string label, string defaultHotkey);
    public string Action { get; }
    public string Label { get; }
    public string DefaultHotkey { get; }
}

public interface ICardHotkeySource { IList<CardHotkeyAction> HotkeyActions { get; } }
public interface ICardHotkeyTarget { void ExecuteHotkey(string action, FrameworkElement content); }

public interface ICardDropTarget
{
    bool CanDrop(IDataObject data);
    void Drop(IDataObject data, FrameworkElement content);
}

public interface ICardDropFeedback
{
    void OnDropStateChanged(FrameworkElement content, bool over, IDataObject data);
}

public interface ICardActivation
{
    string OnActivated(CardSettings config);
    void OnDeactivated(CardSettings config);
}
```

### 12.8 预设 / 截图 / 运行时设置合并 / 自检

```csharp
public sealed class CardPreset
{
    public CardPreset(string id, string label, string defaultContent, string icon);
    public string Id { get; }
    public string Label { get; }
    public string DefaultContent { get; }
    public string Icon { get; }
    public bool EditableContent { get; set; }
}

public interface ICardPresetSource
{
    IList<CardPreset> Presets { get; }
    string CurrentPreset(CardSettings config);
    void ApplyPreset(CardSettings config, string presetId);
}

public interface ICardScreenshotProvider
{
    void PrepareScreenshot(FrameworkElement content);
}

public interface ICardRuntimeSettingsMerge
{
    void MergeRuntimeFields(CardSettings target, CardSettings live, CardSettings baseline);
}

public interface ICardSelfTest
{
    string RunSelfTest();   // null / 空串 = 通过
}
```

### 12.9 采样与模板

`CardRenderContext.Snapshot`（`MetricsSnapshot`）常用字段：

- `CpuPercent` / `MemUsedGb` / `MemTotalGb` / `MemPercent`
- `GpuAvailable` / `GpuPercent` / `GpuTempC` / `GpuPowerW` / `VramUsedMb` / `VramTotalMb`
- `DiskPercent` / `DiskReadBps` / `DiskWriteBps`
- `NetDownBps` / `NetUpBps` / `NetAdapter`

`CardRenderContext.Variables` 支持模板变量（CPU / 内存 / GPU / 磁盘 / 网络 / 时间 / 日期 / 星期 / 运行时长 /
主机名 / 用户名 / 按键历史 / 分区等）；`context.Render("{cpu:0.0}%")` 直接套用，`RenderOrDefault` 在
`Config.Content` 为空时回退默认模板。

---

## 13. 开发检查清单

- [ ] 目标 `net8.0-windows` + `UseWPF`，引用 Contracts（需要控件时加 UiKit），不引用宿主；
- [ ] `manifest.json`：id / apiVersion=2 / entryType / assembly 正确；
- [ ] 只使用 `MirrorpondHUD.Cards` / `MirrorpondHUD.Core` / `MirrorpondHUD.Ui` 公开契约；
- [ ] DTO 使用 public field，通过 `ExtrasAs<T>` / `SetExtras` 读写；
- [ ] 富内容卡在 `ICardStyleAware.ApplyStyle` 里重套 UiKit 控件样式；
- [ ] 线程/定时器在 `OnVisibilityChanged` / `ICardModuleLifetime` 里停止；
- [ ] 不依赖当前用户配置迁移（2.14.7 起旧 Schema 不再迁移）；
- [ ] 插件目录可被宿主影子复制，源 DLL 不锁定；
- [ ] 在真实宿主里用日志验证加载、显示、设置保存、热键；
- [ ] 更新插件后重启验证新 DLL 生效；不要依赖热重载。

---

## 14. 复制即用：最小插件工程

目录：

```text
MyCard/
  MyCard.csproj
  manifest.json
  MyCardModule.cs
```

`MyCard.csproj`（把 `HintPath` 改成你的宿主发布目录；宿主必须已经构建过一次）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <OutputType>Library</OutputType>
    <Nullable>disable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>MyCard</AssemblyName>
    <RootNamespace>MyCard</RootNamespace>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="MirrorpondHUD.Contracts">
      <HintPath>C:\Path\To\MirrorpondHUD\MirrorpondHUD.Contracts.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

`manifest.json`：

```json
{
  "id": "com.example.my-card",
  "name": "我的卡片",
  "version": "1.0.0",
  "apiVersion": 2,
  "entryType": "MyCard.MyCardModule",
  "assembly": "MyCard.dll",
  "author": "example"
}
```

`MyCardModule.cs`：

```csharp
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MyCard
{
    public sealed class MyCardModule : CardModuleBase
    {
        public override string Kind { get { return "my-card"; } }
        public override string DisplayName { get { return "我的卡片"; } }
        public override string Icon { get { return Icons.Txt; } }
        public override string DefaultContent { get { return "CPU {cpu:0.0}%"; } }
        public override int Order { get { return 40; } }

        public override string Render(CardRenderContext context)
        {
            return context.RenderOrDefault(DefaultContent);
        }
    }
}
```

构建与安装：

```bat
dotnet build MyCard\MyCard.csproj -c Release
mkdir <MirrorpondHUD>\plugins\cards\com.example.my-card
copy MyCard\bin\Release\MyCard.dll <MirrorpondHUD>\plugins\cards\com.example.my-card\
copy MyCard\manifest.json <MirrorpondHUD>\plugins\cards\com.example.my-card\
```

**重启**宿主的 `MirrorpondHUD.exe`；日志应出现：

```text
已加载卡片插件 我的卡片 1.0.0（Kind=my-card，entry=MyCard.MyCardModule，文字卡，影子副本）
运行时插件加载完成：成功 1 个，失败 0 个（com.example.my-card）
```

然后在「设置 → 内容 → 添加卡片」里添加「我的卡片」。

> 没有热重载：改完代码重新 build → 覆盖插件目录 → 重启宿主。
