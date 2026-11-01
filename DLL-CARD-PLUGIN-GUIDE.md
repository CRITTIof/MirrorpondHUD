# DLL 卡片插件开发指南（2.15.0+）

本文面向要在 MirrorpondHUD 里添加**运行时卡片插件**的开发者。插件是一个独立的 .NET 8 WPF 类库（DLL），
通过 `plugins/cards/<id>/manifest.json` 被宿主加载，可以贡献文字卡或富内容卡，并复用宿主的设置页、
热键、拖放、状态存储和 `MirrorpondHUD.UiKit` 里的主题化控件。

> 当前插件 API 版本为 **2**。2.14.4 起不再兼容 2.14.3 及更早编译的插件；旧插件必须重新编译并引用
> `MirrorpondHUD.Contracts`。插件是**可信本地代码**，`AssemblyLoadContext` 只是加载隔离，不是安全沙箱。

---

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

## 6. `MirrorpondHUD.UiKit` 表单控件

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

## 7. 宿主服务与状态

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

- `ICardModuleLifetime.OnPluginUnloading()`：卸载插件 ALC 前调用，停线程/定时器、解绑静态事件；
- 富内容卡建议在 `OnVisibilityChanged(content, false)` 暂停视频/动图；
- `--plugin-dev`：开发期只对**纯文字卡**热重载（富内容/交互/设置区块/旧别名插件会被拒绝并提示重启）；
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
| `Kind 与内置模块冲突` | 换一个插件自己的 Kind |
| `assembly 字段非法` / `程序集不存在` | manifest 的 `assembly` 只能是裸 DLL 文件名且文件存在 |
| `manifest.json 解析为空` / `读取失败` | JSON 格式或大小（≤256KB） |

---

## 11. 安全与兼容性

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
