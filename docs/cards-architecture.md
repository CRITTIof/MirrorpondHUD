# 卡片模块化：现在的实现

> 目标：把「桌面信息叠层」做成一组**可添加、可删除、可排序**的卡片。
> 现在六种内置卡片（系统信息 / 时钟 / 按键 / 文本信息 / 命令行 / 媒体）已经按这个结构跑起来；
> 以后加文件中转站、游戏账号数据、AI 对话，只需要在 `src/Cards` 里新增一个文件，宿主不用改
> ——文字卡用 `CardModuleBase`，图片 / 视频这类富内容卡用 `RichCardModuleBase`。

## 1. 文件结构

| 文件 | 职责 |
| --- | --- |
| `src/Cards/CardModule.cs` | `ICardModule` 接口、`CardRenderContext`、`CardModuleBase` 默认实现 |
| `src/Cards/RichCard.cs` | `IRichCardModule` / `RichCardModuleBase`：返回任意 WPF 内容的卡片契约 |
| `src/Cards/CardRegistry.cs` | 反射扫描本程序集里所有 `ICardModule` 实现，按 `Order` 排序 |
| `src/Cards/CardModel.cs` | 卡片列表的创建、补全、校验、名称去重 |
| `src/Cards/MetricsCard.cs` 等 | 六种卡片各自的元数据与渲染 |
| `src/Media/MediaView.cs` | 图片 / 动图 / 视频控件：卡片内容的实际绘制者（文件名 + 进度条 + 圆角） |
| `src/Media/GifAnimator.cs` | 动图按帧播放（GDI+ 取帧 + `DispatcherTimer`） |
| `src/Media/VideoWindow.cs` | 视频子窗口：分层窗口画不出 `MediaElement`，视频单独开窗贴在卡片上 |
| `src/Media/Decoders.cs` | 解码器探测：自定义路径 → PATH → 常见安装目录，找 ffmpeg / mpv / VLC |
| `src/Media/FfmpegPlayer.cs` | 外部 ffmpeg 解码：原始 BGRA 帧管道、进度、循环、图片兜底 |
| `src/Ui/OverlayController.cs` | 每张卡片一个窗口，负责堆叠、显示 / 隐藏、热键、命令行会话、富内容生命周期 |
| `src/Ui/CardEditor.cs` | 设置页「内容」里的一张卡片设置卡（双向绑定，含每卡命令行 / 媒体设置） |

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

新增一张**文字卡**：

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

新增一张**富内容卡**（图片、动图、视频、以后的自定义面板）：

```csharp
public sealed class MyMediaCard : RichCardModuleBase
{
    public override string Kind { get { return "mymedia"; } }
    public override string DisplayName { get { return "我的媒体"; } }
    public override string Icon { get { return Icons.Image; } }
    public override int Order { get { return 30; } }

    public override FrameworkElement CreateContent(CardRenderContext context)
    {
        var view = new MediaView()
            .WithCornerRadius(context.Settings.Overlay.CornerRadius)
            .WithStyle(context.Settings.Style);
        view.Bind(context.Config.Media);
        return view;
    }
}
```

富内容契约只有三个方法：`CreateContent`（建控件）、`UpdateContent`（设置没变时刷新数据）、
`OnVisibilityChanged` / `OnZOrderChanged`（卡片显示 / 隐藏、重新置顶时通知）。
控件自己不碰窗口，所以卡片模块依旧是「新增一个类即可」。

把文件放进 `src/Cards/` 即可：工程默认包含全部 `.cs`，`CardRegistry` 启动时反射发现，
「添加卡片」菜单、设置页编辑、默认内容、热键校验全部自动生效。

## 3. 配置模型

```json
"SchemaVersion": 3,
"Cards": [
  {
    "Id": "3f6c...",
    "Kind": "metrics",
    "Name": "系统信息",
    "Resident": true,
    "Hotkey": "",
    "Spacing": 0,
    "Content": "{cpu_icon} CPU {cpu:0.0}%",
    "Command": { "Shell": "cmd", "CustomShell": "cmd.exe /c {cmd}", "WorkingDirectory": "",
                 "Truncate": true, "MaxLineChars": 120, "MaxLines": 30, "TimeoutSeconds": 20 },
    "Media": { "Path": "", "Fit": "Contain", "Width": 320, "Height": 0, "Opacity": 1,
               "Loop": true, "Autoplay": true, "Muted": true, "Speed": 1, "Rounded": true,
               "Decoder": "Auto", "DecoderPath": "" }
  }
]
```

- `Id`：稳定标识，热键动作 `card:<Id>` 挂在它上面；删除 / 重排不变
- `Kind`：未知值会被 `CardRegistry.ResolveKind` 归一到系统信息卡
- `Name`：允许重名；为空时用模块显示名
- `Resident`：常驻；`false` 时必须给 `Hotkey`，否则保存时提示
- `Spacing`：与上一张**可见**卡片的间距
- `Content`：内容模板；空 = 用卡片默认内容
- `Command`：**每张命令行卡片自己的**工具 / 模板 / 工作目录 / 截断 / 行数 / 超时。
  v2 里这些是全局设置，v3 起拆到卡片上，于是 cmd、pwsh、WSL 可以各放一张卡片同时待命
- `Media`：**每张媒体卡片自己的**文件、缩放方式、宽高、不透明度、循环 / 自动播放 / 静音 / 倍速、
  圆角开关、解码器（`Auto` / `BuiltIn` / `FFmpeg`）与自定义 `ffmpeg.exe` 路径
- 命令行卡强制 `Resident=false`、无内容模板

旧配置由 `Core/SettingsMigration.cs` 一次性迁移：v0/v1（单窗口，全局 `Content` + `X/Y/Visible`）
走 `LegacySettings`；v2 把全局 `Command` 复制进每张命令行卡片。迁移后立刻写回。

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
- 文本变长、命令行展开、视频换分辨率后，每次渲染后 `LayoutIfNeeded()` 校正一次高度，不依赖 `SizeChanged` 的时序

## 5. 富内容与视频子窗口

媒体卡片和文字卡片共用同一块「底层」：卡片窗口照常由 `OverlaySurface` 画底色 / 毛玻璃 / 圆角 /
投影，`MediaView` 在四周留出和文字一样的内边距，所以媒体卡看起来仍是同一叠卡片里的一张：

```
┌──────────────────────────────┐  ← OverlaySurface：tint / 毛玻璃 / 圆角 / 投影
│  clip.mkv                    │  ← 顶部：文件名，超长 TextTrimming=CharacterEllipsis
│  ┌────────────────────────┐  │
│  │      媒体画面          │  │  ← 按外观圆角裁剪（MediaSettings.Rounded）
│  └────────────────────────┘  │
│  ▬▬▬▬▬▬▬▬▬▬▬▬▬▬░░░░░░░░░░░░  │  ← 底部：视频进度条（250 ms 刷新，图片不显示）
└──────────────────────────────┘
```

| 内容 | 承载方式 |
| --- | --- |
| 静态图片 | `Image` + `BitmapDecoder`（png / jpg / bmp / tif / ico），高质量缩放；WPF 解不开时交给 ffmpeg 转一帧 |
| 动图 GIF | `GifAnimator`：GDI+ 逐帧合成、按 GIF 自带延时切换，卡片隐藏时停表 |
| 视频（系统） | `VideoWindow`：**独立的不透明子窗口**，`Owner` = 卡片，跟随位置并做圆角 |
| 视频（ffmpeg） | `FfmpegPlayer`：`-re -f rawvideo -pix_fmt bgra` 管道逐帧画回卡片，不需要子窗口 |

媒体默认按外观里的圆角把四个角裁圆（`MediaSettings.Rounded`，可在卡片里关掉）；
图片 / 动图 / ffmpeg 帧用 `RectangleGeometry` 裁剪，视频子窗口用 `SetWindowRgn` 得到同样的圆角。

视频为什么要单独开窗：叠层用 `AllowsTransparency=true`（`WS_EX_LAYERED`），WPF 在这种窗口上用软件渲染，
`MediaElement` 的视频画面会被整块丢弃。子窗口不透明、`WS_EX_TRANSPARENT|NOACTIVATE|TOOLWINDOW`
（鼠标穿透、不抢焦点、不进 Alt+Tab），`SetWindowRgn` 做圆角，视觉上仍然是一张圆角卡片。
卡片每次 `EnsureTopmost()` 之后会通过 `RaisedTopmost` 事件让子窗口重新 `SetWindowPos(HWND_TOPMOST)`，
否则会被卡片自己压住。

### 解码器

系统自带的 Media Foundation 能放 mp4 / wmv，但 mkv / webm / av1 / 部分 hevc 取决于系统与扩展；
ffmpeg 路线把视频解成逐帧原始像素，什么格式都能放（代价是没有声音、固定 1×、走 CPU）。

- `MediaSettings.Decoder`：`Auto`（系统优先，失败自动换 ffmpeg）/ `BuiltIn`（只用系统）/
  `FFmpeg`（只用 ffmpeg）；
- `MediaSettings.DecoderPath`：自定义 `ffmpeg.exe`，留空则 `Decoders` 依次查
  自定义路径 → `PATH` → 常见安装目录（Program Files、Chocolatey、Scoop、WinGet Links…）；
- 设置页展示探测结果（版本 + 路径），「检测」按钮强制重新扫描；
- 系统解码失败（`MediaElement.MediaFailed`）且 `Auto` 时，自动 `ReleaseVideo()` 并切到
  `FfmpegPlayer`；图片（webp / avif / heic）解不开时同样用 `ffmpeg -frames:v 1` 转一帧；
- ffmpeg 逐帧解码的进度 = 已读帧数 / 探测到的帧率，卡片底部进度条直接用这个值。

## 6. 热键

- 全局热键：`toggle-overlay` / `open-settings` / `toggle-lock`，被占用时退到 `Ctrl+Alt+原键`
- 卡片热键：动作名 `card:<Id>`，关闭常驻的卡片各注册一个；同样带 `Ctrl+Alt+原键` 兜底，
  兜底成功后把实际生效的组合键写回卡片设置
- `HotkeyManager` 支持**同一组合键挂多个动作**：两张卡片设成同一个热键，按一次同时呼出
- 删除卡片后重新注册时会先 `UnregisterAll()`，不会留下失效热键

## 7. 生命周期

`OverlayController` 持有 `List<CardSlot>`，每个 `CardSlot` = `CardSettings` + `ICardModule` + `OverlayWindow`
+ 富内容控件 + 命令行状态（历史 / 输入 / 取消令牌）。

`ApplySettings` 时按 `Id` 对齐：

- 仍在列表里：复用窗口，换 `Dialog` / `Module` / `Config`，保留呼出状态；
  富内容控件按新设置重建（换图片 / 换视频路径立即生效）
- 新增：创建窗口并接入事件
- 删除：释放富内容（停视频 / 停动图）、关闭窗口；如果删的是正在输入的命令行卡片，先退出命令行模式

设置窗口只编辑一份克隆，点「保存并应用」才回到宿主；卡片结构与外观都是显式保存。

## 8. 还没做的

| 事项 | 说明 |
| --- | --- |
| 运行期插件 | `cards/*.dll` + `AssemblyLoadContext` + ApiVersion 校验；先留接口 |
| 每卡独立样式 | `Style` 目前全局共享（字体、颜色、图标倍率、毛玻璃都是全局）；需要时给 `CardSettings` 加可空的样式覆盖 |
| 卡片内交互 | 富内容目前只显示不交互；要支持播放控制、滚动、点击，需要临时解除鼠标穿透并把命中区域限制在控件上 |
| 托盘增删 | 现在只能从设置页增删卡片 |
