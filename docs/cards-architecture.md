# 卡片模块化：现在的实现

> 目标：把「桌面信息叠层」做成一组**可添加、可删除、可排序**的卡片。
> 现在五种内置卡片（系统信息 / 时钟 / 按键 / 文本信息 / 命令行）已经按这个结构跑起来；
> 以后加文件中转站、游戏账号数据、AI 对话，只需要在 `src/Cards` 里新增一个文件，宿主不用改。

## 1. 文件结构

| 文件 | 职责 |
| --- | --- |
| `src/Cards/CardModule.cs` | `ICardModule` 接口、`CardRenderContext`、`CardModuleBase` 默认实现 |
| `src/Cards/CardRegistry.cs` | 反射扫描本程序集里所有 `ICardModule` 实现，按 `Order` 排序 |
| `src/Cards/CardModel.cs` | 卡片列表的创建、补全、校验、名称去重 |
| `src/Cards/MetricsCard.cs` 等 | 五种卡片各自的元数据与渲染 |
| `src/Ui/OverlayController.cs` | 每张卡片一个窗口，负责堆叠、显示 / 隐藏、热键、命令行会话 |
| `src/Ui/CardEditor.cs` | 设置页「内容」里的一张卡片设置卡（双向绑定） |

## 2. 卡片接口

```csharp
public interface ICardModule
{
    string Kind { get; }            // 配置里的 Kind，稳定标识
    string DisplayName { get; }     // 设置页显示名
    string Icon { get; }            // ReiconFont 图标字符
    string DefaultContent { get; }  // 内容模板为空时的默认值
    bool SupportsContent { get; }
    bool IsInteractive { get; }     // 独占键盘（命令行）
    bool DefaultResident { get; }
    string DefaultHotkey { get; }
    int Order { get; }              // 「添加卡片」菜单顺序
    string Render(CardRenderContext context);
}
```

`CardRenderContext` 持有本次采样的 `Settings`、`MetricsSnapshot`、`KeyHistory` 和当前 `CardSettings`；
变量表按需构建一次，卡片拿到的都是同一帧的数据。

新增一种卡片：

```csharp
public sealed class WeatherCard : CardModuleBase
{
    public override string Kind { get { return "weather"; } }
    public override string DisplayName { get { return "天气"; } }
    public override string Icon { get { return Icons.Web; } }
    public override string DefaultContent { get { return "{weather_temp}℃ {weather_text}"; } }
    public override int Order { get { return 20; } }

    public override string Render(CardRenderContext context)
    {
        return context.RenderOrDefault(DefaultContent);
    }
}
```

把文件放进 `src/Cards/` 即可：工程默认包含全部 `.cs`，`CardRegistry` 启动时反射发现，
「添加卡片」菜单、设置页编辑、默认内容、热键校验全部自动生效。

## 3. 配置模型

```json
"SchemaVersion": 2,
"Cards": [
  {
    "Id": "3f6c...",
    "Kind": "metrics",
    "Name": "系统信息",
    "Resident": true,
    "Hotkey": "",
    "Spacing": 0,
    "Content": "{cpu_icon} CPU {cpu:0.0}%"
  }
]
```

- `Id`：稳定标识，热键动作 `card:<Id>` 挂在它上面；删除 / 重排不变
- `Kind`：未知值会被 `CardRegistry.ResolveKind` 归一到系统信息卡
- `Name`：允许重名；为空时用模块显示名
- `Resident`：常驻；`false` 时必须给 `Hotkey`，否则保存时提示
- `Spacing`：与上一张**可见**卡片的间距
- `Content`：内容模板；空 = 用卡片默认内容
- 命令行卡强制 `Resident=false`、无内容模板

旧配置（没有 `SchemaVersion`，全局 `Content` + 带 `X/Y/Visible` 的 Cards）由
`Core/SettingsMigration.cs` 一次性迁移，迁移后立刻写回。

## 4. 布局：一条垂直堆叠

卡片不再各存 X/Y。所有可见卡片从锚点（`Overlay.X/Y`，相对所在显示器工作区）开始向下排：

```
top = 0
for card in Cards where visible:
    if not first: top += card.Spacing
    window.Y = anchor.Y + top
    top += card.BodyHeight      // 不含投影的 10 DIP 透明边
```

- 拖动任意卡片：算出位移，移动锚点，所有卡片一起走（`OnCardMoved`）
- 非常驻卡片被呼出时插入自己的位置，后面的卡片自动让开
- 文本变长、命令行展开后，每次渲染后 `LayoutIfNeeded()` 校正一次高度，不依赖 `SizeChanged` 的时序

## 5. 热键

- 全局热键：`toggle-overlay` / `open-settings` / `toggle-lock`，被占用时退到 `Ctrl+Alt+原键`
- 卡片热键：动作名 `card:<Id>`，关闭常驻的卡片各注册一个
- `HotkeyManager` 支持**同一组合键挂多个动作**：两张卡片设成同一个热键，按一次同时呼出
- 删除卡片后重新注册时会先 `UnregisterAll()`，不会留下失效热键

## 6. 生命周期

`OverlayController` 持有 `List<CardSlot>`，每个 `CardSlot` = `CardSettings` + `ICardModule` + `OverlayWindow` + 运行状态。
`ApplySettings` 时按 `Id` 对齐：

- 仍在列表里：复用窗口，换 `Dialog` / `Module` / `Config`，保留呼出状态
- 新增：创建窗口并接入事件
- 删除：关闭窗口；如果删的是正在输入的命令行卡片，先退出命令行模式

设置窗口只编辑一份克隆，点「保存并应用」才回到宿主；卡片结构与外观都是显式保存。

## 7. 还没做的

| 事项 | 说明 |
| --- | --- |
| 运行期插件 | `cards/*.dll` + `AssemblyLoadContext` + ApiVersion 校验；先留接口 |
| 富内容卡片 | 现在卡片都是 `TextCanvas` 文本；文件中转站 / AI 对话需要新的基类与滚动容器 |
| 毛玻璃调度 | 仍由每个窗口各自抓屏；卡片多时应改成串行 `GlassScheduler`，见 [glass-options.md](glass-options.md) |
| 卡片独立样式 | `Style` 目前全局共享；需要时给 `CardSettings` 加可空的样式覆盖 |
| 托盘增删 | 现在只能从设置页增删卡片 |
