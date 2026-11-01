# DLL 卡片插件开发指南（2.19.0+）

本文面向要在 MirrorpondHUD 里添加**运行时卡片插件**的开发者。插件是一个独立的 .NET 8 WPF 类库（DLL），
通过 `plugins/cards/<id>/manifest.json` 被宿主加载，可以贡献文字卡或富内容卡，并复用宿主的设置页、
热键、拖放、状态存储和 `MirrorpondHUD.UiKit` 里的主题化控件。

> 当前插件 API 版本为 **2**（2.16.0 新增的宿主服务契约均为加法，旧插件无需改 manifest）。2.14.4 起不再兼容
> 2.14.3 及更早编译的插件；旧插件必须重新编译并引用 `MirrorpondHUD.Contracts`。插件是**可信本地代码**，
> `AssemblyLoadContext` 只是加载隔离，不是安全沙箱。
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
- 内置插件的 id 以 `builtin.` 开头（`CardPluginHandle.IsBundled`），`--plugin-dev` 不会热重载它们；
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

### 2.3 manifest.json

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

---

## 4. 富内容卡

```csharp
using System.Windows;
using MirrorpondHUD.Cards;

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
> 从磁盘读进来时 `Extras` 里是原始 JSON 节点，`ExtrasAs<T>` 会绑定成强类型并写回 `Extras<T>` 前先修改再 `SetExtras`。

### 5.2 设置页 UI：`ICardEditorSection`

```csharp
using System;
using System.Windows;
using MirrorpondHUD.Cards;
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

## 9. 生命周期与热重载

2.16.0 起 `--plugin-dev` 的热重载只针对**用户**文字卡：内置插件（id `builtin.*`）不参与替换，改内置插件的代码
需要重新构建 / 重启宿主。生产环境始终是「替换 DLL 后重启生效」。

- `ICardModuleLifetime.OnPluginUnloading()`：卸载插件 ALC 前调用，停线程/定时器、解绑静态事件；
- 富内容卡建议在 `OnVisibilityChanged(content, false)` 暂停视频/动图；
- `--plugin-dev`：开发期只对**用户纯文字卡**热重载（富内容/交互/设置区块插件会被拒绝并提示重启；内置插件不参与）；
- 热重载时会关闭设置窗（未保存编辑会丢）；生产使用不要带 `--plugin-dev`，安装/更新插件后重启。

---

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
| `Kind 重复 / 与其它插件冲突` | 注册表合并时后者被告警丢弃（启动）/ 抛错并保留旧模块（dev 重载）；换一个自己的 Kind |
| `assembly 字段非法` / `程序集不存在` | manifest 的 `assembly` 只能是裸 DLL 文件名且文件存在 |
| `manifest.json 解析为空` / `读取失败` | JSON 格式或大小（≤256KB） |

---

## 11. 安全与兼容性

当前插件 API 版本为 **2**。2.16.0 新增的宿主服务契约（`CardHostServices.cs`）与 `ICardHost.SaveSettings()` /
`Shell` 都是**加法**：旧插件不需要改 manifest，重新编译时引用最新 Contracts / UiKit 即可。

- 只加载**可信本地插件**；ALC 不是沙箱，插件可执行当前用户权限内的任意代码；
- 签名/哈希/白名单目前未实现；
- 插件 API 2：旧插件必须重编译；引用宿主的插件会被明确拒绝；
- 插件热重载只支持纯文字卡；富内容卡、交互卡、自带 WPF 设置区块的插件需要重启；
- 插件 DTO 必须使用 public field（`Json` 限制）；
- 插件卸载时请实现 `ICardModuleLifetime`，否则自己的线程/静态事件可能阻止 ALC 回收。

---

## 12. 开发检查清单

- [ ] 目标 `net8.0-windows` + `UseWPF`，引用 Contracts（需要控件时加 UiKit），不引用宿主；
- [ ] `manifest.json`：id / apiVersion=2 / entryType / assembly 正确；
- [ ] 只使用 `MirrorpondHUD.Cards` / `MirrorpondHUD.Core` / `MirrorpondHUD.Ui` 公开契约；
- [ ] DTO 使用 public field，通过 `ExtrasAs<T>` / `SetExtras` 读写；
- [ ] 富内容卡在 `ICardStyleAware.ApplyStyle` 里重套 UiKit 控件样式；
- [ ] 线程/定时器在 `OnVisibilityChanged` / `ICardModuleLifetime` 里停止；
- [ ] 不依赖当前用户配置迁移（2.14.7 起旧 Schema 不再迁移）；
- [ ] 插件目录可被宿主影子复制，源 DLL 不锁定；
- [ ] 在真实宿主里用日志验证加载、显示、设置保存、热键、重启。
