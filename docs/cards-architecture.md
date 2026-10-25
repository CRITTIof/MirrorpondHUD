# 卡片模块化：现在的实现

> 目标：把「桌面信息叠层」做成一组**可添加、可删除、可排序**的卡片。
> 现在五种内置卡片（信息与文本 / 命令行 / 媒体 / AI 对话 / 按钮演示）都按这个结构跑起来；
> 再加一种卡片只需要在 `src/Cards` 里新增一个文件，宿主不用改
> ——文字卡用 `CardModuleBase`，图片 / 视频 / 面板这类富内容卡用 `RichCardModuleBase`。

## 1. 文件结构

| 文件 | 职责 |
| --- | --- |
| `src/Cards/CardModule.cs` | `ICardModule` 接口、`CardRenderContext`、`CardModuleBase` 默认实现 |
| `src/Cards/RichCard.cs` | `IRichCardModule` / `RichCardModuleBase`：返回任意 WPF 内容的卡片契约（含 `NeedsMouseInput`） |
| `src/Cards/CardRegistry.cs` | 反射扫描本程序集里所有 `ICardModule` 实现，按 `Order` 排序 |
| `src/Cards/CardModel.cs` | 卡片列表的创建、补全、校验、名称去重 |
| `src/Cards/InfoTextCard.cs` | 信息与文本卡：系统信息 / 时钟 / 按键 / 文本四个预设 |
| `src/Cards/CommandCard.cs` / `MediaCard.cs` | 命令行卡（独占键盘）、媒体卡（图片 / 动图 / 视频） |
| `src/Cards/ChatCard.cs` | AI 对话卡：命令行样式（模型名 + `>` 输入行 + 终端记录），单次 / 轮询走全局热键 |
| `src/Cards/ButtonsCard.cs` | 按钮演示卡：验证 `CardButton` / `CardTextBox` 这套卡片控件 |
| `src/Core/SecretStore.cs` | 敏感字符串加密：Windows DPAPI（当前用户），settings.json 里只落 `dpapi:…` |
| `src/Ui/Controls/CardButton.cs` | 卡片里的按钮：三种语气 + 三态模板，颜色跟随卡片外观 |
| `src/Ui/Controls/CardTextBox.cs` | 卡片里的输入框：底色 / 光标 / 选区跟着卡片文字色 |
| `src/Media/MediaView.cs` | 图片 / 动图 / 视频控件：卡片内容的实际绘制者（文件名 + 进度条 + 圆角） |
| `src/Media/GifAnimator.cs` | 动图按帧播放（GDI+ 取帧 + `DispatcherTimer`） |
| `src/Media/VideoWindow.cs` | 视频子窗口：分层窗口画不出 `MediaElement`，视频单独开窗贴在卡片上 |
| `src/Media/Decoders.cs` | 解码器探测：自定义路径 → PATH → 常见安装目录，找 ffmpeg / mpv / VLC |
| `src/Media/FfmpegPlayer.cs` | 外部 ffmpeg 解码：原始 BGRA 帧管道、进度、循环、变速、图片兜底 |
| `src/Media/WaveOutPlayer.cs` | winmm waveOut 播放 ffmpeg 的 PCM 音频（暂停 / 继续 / 音量） |
| `src/Ui/OverlayController.cs` | 每张卡片一个窗口，负责堆叠、显示 / 隐藏、热键、命令行会话、富内容生命周期 |
| `src/Ui/CardEditor.cs` | 设置页「内容」里的一张卡片设置卡（双向绑定，含每卡命令行 / 媒体 / AI 接口设置） |

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

### 4.1 卡片对齐（外观 → 卡片 → 对齐方式）

`Style.CardAlign` 只决定每张卡片的 **X**，不改变锚点语义：

```
stackWidth = max(可见卡片宽度)          // CardWidth，不含投影的 10 DIP 留白
align      = Left | Right | Auto
for card in Cards where visible:
    window.X = anchor.X + (align == "Right" ? stackWidth - card.CardWidth : 0)
```

- **左对齐**：所有卡片共用锚点 X，左边缘一条线；
- **右对齐**：`X = 锚点 + (最宽 - 自己宽度)`，所有卡片右边缘一条线，整叠仍然占同一块外框；
- **自动**：拿整叠卡片的中心（`anchor.X + stackWidth / 2`）和所在显示器工作区的中线比较，
  在左半边就整叠左对齐、右半边就整叠右对齐；
- 宽度变化（系统信息位数变多、媒体换分辨率）会触发 `SizeChanged → ScheduleLayout`，X 跟着重算；
- 卡片内部文字的对齐仍由 `Style.TextAlignment` 单独控制，两者互不影响。

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
- ffmpeg 逐帧解码的进度 = 已读帧数 / 探测到的帧率，卡片底部进度条直接用这个值；
- ffmpeg 路线有声音：第二条 ffmpeg 进程只取音频，`atempo` 跟随倍速，输出 48 kHz / 立体声 / 16 bit PCM
  交给 `WaveOutPlayer`；暂停时同时停读管道与音频设备；
- 右上角状态行：播放 / 暂停图标（ReiconFont `Play` / `PauseMedia`）+ `当前 / 总时长`，
  速度不是 1× 时追加 `· 1.5×`，垫一层深色圆角小底保证浅色背景上也看得清。

### 媒体卡通用热键

`MediaCard` 实现 `ICardHotkeySource`（声明动作）与 `ICardHotkeyTarget`（执行动作）：

| 动作 | 默认 | 说明 |
| --- | --- | --- |
| `media.play-pause` | 空 | 播放 / 暂停 |
| `media.forward` / `media.backward` | 空 | 前进 / 后退 5 秒 |
| `media.speed-up` / `media.speed-down` | 空 | 变速 ±0.25×（0.25–4） |

设置页「热键」在「全局」下面按卡片种类分组（组间有分割线），只需要卡片模块声明
`ICardHotkeySource.HotkeyActions`；新增卡片类型不用改设置页或宿主代码。

### 卡片里的按钮与输入框

叠层默认是**点击穿透**的（`WS_EX_TRANSPARENT`），这样卡片不会挡住桌面。要让卡片里能点按钮、能打字，
模块声明 `IRichCardModule.NeedsMouseInput = true`，宿主据此做三件事：

| 位置 | 变化 |
| --- | --- |
| `OverlayWindow.Interactive` | 这张卡的窗口不再加 `WS_EX_TRANSPARENT` / `WS_EX_NOACTIVATE` |
| `OverlaySurface.ContentHitTestVisible` | 内容层参与命中测试（默认 `false`，图片 / 视频不该挡住桌面） |
| `OverlayWindow.UpdateClickThrough` | 锁定状态下也不穿透；光标改成箭头（不是移动手势） |

代价写在卡片实现里：**它会挡住自己身下的桌面点击**，所以交互卡片通常尺寸克制、默认常驻。

控件在 `src/Ui/Controls/`，颜色 / 字号 / 圆角都跟着卡片外观走，不依赖设置窗口那套浅色资源：

- `CardButton`：`Normal` / `Primary` / `Danger` 三种语气，自定义模板画圆角底 + hover / pressed /
  键盘焦点三态；`SetActive(bool)` 用于「多选 / 当前模式」这类选中态。
- `CardTextBox`：单行 / 多行输入，底色是前景色的 7%，光标、选区、边框都跟着卡片文字色。

`ButtonsCard`（按钮演示）就是最小验证：三种语气的按钮 + 一个禁用按钮 + 输入框回显，
用来确认点击、键盘、禁用态和样式跟随都成立。

### AI 对话卡片

`ChatCard` 是第一个「有状态」的富内容卡片：一张卡片一套接口与上下文，样子做成**终端**。

![AI 对话卡片](chat-card.png)

- 布局：第一行模型名（等宽字体）+ 右侧模式标记 `[单次]` / `[轮询]`，一条细横线，下面 `> ` 提示符的输入行，
  再下面是终端风格的记录区（`ScrollViewer`，最多 `AnswerLines` 行高，底纹是前景色 5%、圆角边框）。
  用户行是 `> 内容`（暗一点，像打过的命令），回复行是亮色正文 —— 一眼能分清谁说的。
- 卡片上**没有按钮**：单次 / 轮询、清空上下文都在「设置 → 热键 → AI 对话卡片」里（`ICardHotkeySource`），
  默认 `Ctrl+Alt+Shift+M` 切换模式。四键组合是故意的：三键的 `Ctrl+Alt+M` / `Ctrl+Shift+M`
  很容易被输入法、录屏、IDE 占掉或抢走。
- 请求：`POST {Chat.BaseUrl}/chat/completions`，`Authorization: Bearer <明文 Key>`（可空），
  响应取 `choices[0].message.content`（也兼容 `content` 是 `[{type,text}]` 数组、以及 `choices[0].text`）。
- `ChatSettings.Mode`：`Single` 每次只发当前提问；`Context`（轮询）把最近 12 条消息一起发回去，
  形成连续对话。热键切换会写回设置并立刻落盘，重启还是那个模式；切模式时清空这一张卡的上下文。
- 上下文只在内存里：卡片隐藏 / 重建 / 重启就清空。回答期间输入框只读而不禁用，
  焦点一直留在提示符后面，连着问不用重新点；点卡片任意位置都会把焦点拉回输入行。
- 每张卡片自己的 `Chat.SystemPrompt` / `TimeoutSeconds` / `Width` 都在「设置 → 内容 → AI 接口」里。

### API Key 怎么存

`ChatSettings.ApiKey` 里存的是 `dpapi:<base64>`，由 `Core/SecretStore.cs` 调 `crypt32.dll` 的
`CryptProtectData`（当前用户 + 附加熵）加密：

- 同一台机器、同一个 Windows 用户：能解开；换用户 / 换机器就解不开，卡片和设置页都会提示重填；
- 别人拿到 `settings.json`：只有一串 base64，没有明文；
- 2.4.0 及更早版本是明文存的：`ChatSettings.Normalize` 读入时自动加密，`ConfigStore.Load` 立刻回写，
  磁盘上不留明文；
- 明文的解密结果只放在 `ChatSettings.PlainApiKey`（内存，不参与序列化，`Json` 只写公共字段）；
- 设置页的 Key 输入框永远不回显：已存 Key 时框里是灰字「已填写 API Key，重新输入以更换」，
  旁边一个「清除」；只有真的打了新内容（失焦或点「保存并应用」）才替换，空着不动原来的 Key。

## 6. 热键

- 全局热键：`toggle-overlay` / `open-settings` / `toggle-lock`，被占用时退到 `Ctrl+Alt+原键`
- 卡片热键：动作名 `card:<Id>`，关闭常驻的卡片各注册一个；同样带 `Ctrl+Alt+原键` 兜底，
  兜底成功后把实际生效的组合键写回卡片设置
- 卡片**通用动作**热键（`ICardHotkeySource` / `ICardHotkeyTarget`）：动作名形如
  `media.play-pause`、`chat.toggle-mode`，一个动作作用于**所有同类卡片**，设置页「热键」按卡片分组列出。
  只在真的有这种卡片时才注册，删掉全部同类卡片就释放组合键
- AI 对话的两个动作：`chat.toggle-mode`（切换单次 / 轮询，默认 `Ctrl+Alt+Shift+M`）、
  `chat.clear`（清空上下文，默认留空）
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

### 8.1 卡片控件（已实现 / 未实现）

已经有 `CardButton`（三种语气）和 `CardTextBox`（单行输入），它们靠
`IRichCardModule.NeedsMouseInput` 让宿主关掉点击穿透。下面是还没做的控件，按「加进来的代价」排：

| 控件 | 用途 | 加进来的代价 / 备注 |
| --- | --- | --- |
| 开关 `CardToggle` | 一个开关切换某个行为（静音、锁定、跟随） | 轻：Button 换模板 + 两态；但卡片默认穿透，仍然要 `NeedsMouseInput` |
| 复选框 / 单选组 `CardCheck`、`CardRadio` | 多选、互斥选择（如选择要显示的数据源） | 轻：样式跟 `CardButton` 一致即可 |
| 分段控件 `CardSegmented` | 模式切换（单次 / 轮询、日 / 周 / 月） | 中：一组按钮 + 选中态 + 键盘左右移动；AI 卡目前用设置页下拉 + 全局热键顶替 |
| 滑块 `CardSlider` | 连续值（音量、透明度、阈值） | 中：要一套细轨道模板 + 数值气泡，拖动时不能触发卡片拖动 |
| 数字步进 `CardStepper` | 精确小整数（数量、间隔秒数） | 轻：输入框 + 两个小按钮，需要输入校验 |
| 下拉框 `CardCombo` | 枚举选择（模型、来源、单位） | 中：弹出层在分层窗口里容易被裁剪，得自己画 Popup（或用 `Popup`） |
| 进度条 `CardProgress` | 下载 / 编译 / 任务进度（确定或不确定态） | 轻：纯显示，不需要鼠标；但要有「不确定」动画 → `DispatcherTimer` |
| 状态灯 / 徽标 `CardBadge` | 「在线 / 离线 / 有 3 条待处理」 | 轻：纯显示，配合 `NeedsMouseInput = false` 也能用 |
| 小图表 `CardSparkline` | CPU / 网络的历史曲线、环形进度 | 中：要维护环形缓冲 + 自绘；值变化频繁时注意不要每帧重建几何 |
| 图标按钮 `CardIconButton` | 用 Reicon 字形代替文字的紧凑按钮 | 轻：`CardButton` 多一个 ctor，字体换成 ReiconFont、字号按 `IconScale` |
| 可折叠面板 `CardExpander` | 收起长内容（问答历史、命令历史） | 中：展开时要重算卡片高度，`SizeToContent` 会整窗跳；需要动画的话更麻烦 |
| 卡片内标签页 `CardTabs` | 一张卡里放多页内容（信息 / 设置 / 日志） | 中：内容宿主 + 选中态；和「一张卡一个内容」的现有契约冲突，得改宿主 |
| 滚动列表 `CardList`（虚拟化） | 多行列表（进程、下载、消息） | 重：分层窗口里滚动 + 焦点 + 虚拟化都要自己接；卡片高度要固定 |
| 多行输入 `CardTextArea` | 长文本（提示词、命令模板） | 轻：`CardTextBox` 打开 `AcceptsReturn`；难点是 Enter 提交与换行的区别 |
| 密码框 `CardPassword` | Key / 口令输入 | 轻：换成 `PasswordBox` 模板（现在靠「不回显 + 失焦才提交」实现，磁盘上已经是 DPAPI 密文） |
| 搜索框 `CardSearch` | 带清除按钮 + 过滤回调 | 轻：输入框 + 图标按钮 + 防抖 |
| 自动完成 / 建议列表 | 输入命令、模型名时的候选 | 重：Popup + 键盘导航 + 定位到框下面，分层窗口里容易画到窗外 |
| 日期时间选择 `CardDateTime` | 选一个时间点（闹钟、定时） | 重：日历控件整套样式要跟着卡片配色重做 |
| 颜色选择 `CardColor` | 选颜色（卡片底色、描边） | 中：色板 + 十六进制输入；弹层同样受窗口裁剪限制 |
| 工具提示 `CardToolTip` | 悬停显示解释 | 中：`ToolTip` 是独立窗口，跟随卡片坐标；卡片移动时要重算 |
| 卡片内右键菜单 `CardContextMenu` | 对一行内容做操作（复制、删除） | 中：`ContextMenu` 也是独立窗口；要处理点击穿透与锁定态 |
| 拖放目标 `CardDropTarget` | 把文件拖到卡片上（图片卡换图、命令行读文件） | 重：`AllowDrop` + `DragEnter/Drop`，分层窗口接收 OLE 拖放要额外验证 |
| 拖拽排序 | 卡片内列表拖动排序 | 重：命中测试 + 插入指示线；和卡片整体拖动冲突 |
| 通知条 `CardToast` | 卡片内短暂提示（保存成功、请求失败） | 轻：一个自动消失的 Border；要避免每帧重建 |
| 富文本只读 + 可复制 | 答案、日志可选中复制 | 中：`TextBlock` 不支持选择，得用只读 `TextBox` 或 `RichTextBox`（那套样式很重） |
| 键盘可达性 | Tab 顺序、焦点环、Esc 取消 | 中：一张卡一个窗口，`TabIndex` 要自己排；现在只有 `CardButton` 有焦点态 |
| 无障碍 | `AutomationPeer`、读屏器能念出按钮 / 输入 | 中：WPF 默认有一半，自定义模板要补 `AutomationProperties` |
| 主题跟随（深浅色） | 控件随系统深浅色切换 | 中：现在颜色来自卡片设置，系统主题切换要额外监听 |
| 统一的间距 / 圆角 token | 控件对齐、间距一致 | 轻：把 `Style` 里的 padding / corner 抽成一组常量 |

### 8.2 其它没做的

| 事项 | 说明 |
| --- | --- |
| 运行期插件 | `cards/*.dll` + `AssemblyLoadContext` + ApiVersion 校验；先留接口 |
| 每卡独立样式 | `Style` 目前全局共享（字体、颜色、图标倍率、毛玻璃都是全局）；需要时给 `CardSettings` 加可空的样式覆盖 |
| 卡片内滚动 | 长内容（问答历史）现在靠固定行高 + 滚动条；嵌套滚动和卡片拖动要协调 |
| 托盘增删 | 现在只能从设置页增删卡片 |
| 卡片尺寸约束 | 除了 `FixedWidth`，没有最小 / 最大高度；长答案会把卡片撑得很高 |

