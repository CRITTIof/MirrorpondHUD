<h1 align="center">Overlay Information with CMD</h1>

<p align="center">
  <b>始终置顶的桌面信息叠层：实时硬件数据、按键显示，按 <code>F9</code> 还能直接在叠层里敲命令。</b><br>
  <sub>An always-on-top desktop overlay with live hardware stats, keystroke display and a built-in command line.</sub>
</p>

<p align="center">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6?logo=windows&logoColor=white">
  <img alt="dotnet" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="arch" src="https://img.shields.io/badge/arch-x64%20%7C%20ARM64-555">
  <img alt="ui" src="https://img.shields.io/badge/UI-WPF-2C8EBB">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-3DA639">
</p>

<p align="center">
  <a href="#简体中文">简体中文</a> · <a href="#english">English</a>
</p>

<p align="center">
  <img src="docs/overlay.png" width="560" alt="叠层效果">
</p>

<a id="简体中文"></a>

鼠标默认**完全穿透**，不挡任何点击；所有内容、样式和行为都在设置窗口里可视化调整。

## ✨ 功能

- **实时数据** — CPU / GPU / 磁盘 / 网络 / 内存 / 分区空间 / 时间，PDH 性能计数器采样，独立线程不卡 UI
- **自定义文本** — 模板化占位符 `{cpu:0.0}%`，支持 .NET 格式串与对齐，内置 6 套预设模板
- **按键显示** — 全局键盘钩子，最近按键、重复折叠（`A×6`）、最大字符数限制；按键以 **ReiKeycap** 键帽字形显示（内嵌字体，可和普通文字同行混排）
- **快捷命令行** — `F9` 在信息栏下方执行 CMD / PowerShell / 自定义命令，返回原样打印
- **启动动画** — 打字 / 退格动画 + 模拟光标（方块 / 竖线 / 下划线 / 空心框 / 关闭）
- **外观可调** — 文字描边、背景图片、毛玻璃背景（实验性）、固定宽度、圆角、透明度、字体字号，尺寸只增不减不跳动
- **字体齐全** — 列出全部系统字体，中文字体显示中文名（如「宋体 (SimSun)」），带关键字筛选
- **托盘常驻** — 单实例唤醒、开机自启、可自定义的全局热键、鼠标穿透锁定
- **无依赖** — 单文件 exe，目标机器不需要安装 .NET

## 🚀 快速开始

**下载即用**：到 [Releases](https://github.com/CRITTIof/overlay-information-with-cmd/releases) 下载，四种任选：

| 文件 | 适用 | 说明 |
| --- | --- | --- |
| `WindowOverlay-x64.exe` | 普通电脑（x64） | 自带 .NET 8 运行时，约 60 MB，双击即用 |
| `WindowOverlay-arm64.exe` | Windows on ARM | 自带 .NET 8 运行时，约 60 MB |
| `WindowOverlay-x64-requires-dotnet.exe` | 已装 .NET 8 桌面运行时 | 体积约 0.4 MB，依赖系统运行时 |
| `WindowOverlay-arm64-requires-dotnet.exe` | 已装 .NET 8 桌面运行时的 ARM 机器 | 体积约 0.4 MB，依赖系统运行时 |

不放心的机器选前两个（免安装运行时）；想省空间、且已经装了
[.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)的，选后两个。

**从源码运行**：

```bat
git clone https://github.com/CRITTIof/overlay-information-with-cmd.git
cd overlay-information-with-cmd
run.bat
```

`run.bat` 会自动编译，然后直接从 `bin\Release` 启动。
配置文件和日志也生成在那里（`bin\Release\settings.json`、`bin\Release\overlay.log`）。

## ⌨️ 快捷键

| 操作 | 默认 | 说明 |
| --- | --- | --- |
| 打开 / 关闭快捷命令行 | `F9` | 见下一节 |
| 打开设置窗口 | `F10` | 也可左键单击托盘图标 |
| 锁定 / 解锁（鼠标穿透） | `F8` | 叠层能否拖动、能否点右键只由它切换；打开 / 关闭设置窗口不会自动改变锁定状态，保存设置也不会移动叠层 |
| 显示 / 隐藏叠层 | *未设置* | 从托盘菜单操作，或自行绑定 |

所有热键都能在「设置 → 热键」里改，**留空 = 不启用**；被其它程序占用时会自动退到 `Ctrl+Alt+原键`。
再次双击 exe 不会起第二个进程，而是唤醒已在运行的实例的设置窗口。

## 🖥️ 快捷命令行（`F9`）

![快捷命令行](docs/command-line.png)

按 `F9` 后会在信息栏下方另起三行：一个空行、提示符 `[CMD] [C:\Users\CRITT] >`、以及输入行。

- 提示符格式为 `[工具] [工作目录] >`，工具名统一大写，PowerShell 显示为 **PWSH**
- 工具可选 **CMD**（默认）/ **Windows PowerShell** / **PowerShell 7** / **自定义模板**（`{cmd}` 占位）
- 命令在后台线程执行，返回内容**原样打印**，然后给出新提示符，可连续执行多条
- 单行过长时只保留**后面** N 个字符并加 `…`（可关闭），也可限制最多保留多少行
- 默认 20 秒超时，超时强制结束；输出先按 UTF-8 解码，失败退回系统 OEM 代码页（中文不乱码）
- **再按 `F9`（或 `Esc`）退出**，清空命令与返回，恢复正常信息显示
- 启动动画期间、叠层隐藏时该热键不生效

输入框里的输入法被刻意关闭，保证命令按原样输入；支持方向键、`Home` / `End`、`Ctrl+V`、`Ctrl+Z`。

## 🧩 显示内容与占位符

内容是一段支持多行的模板文本，用 `{变量}` 插入实时数据：

```
CPU {cpu:0.0}%      →  CPU 23.4%
MEM {mem_pct:0}%    →  MEM 40%
{net_down,>10}      →  右对齐到 10 个字符
```

| 变量 | 含义 | 变量 | 含义 |
| --- | --- | --- | --- |
| `{cpu}` / `{cpu_bar}` | CPU 占用 / 进度条 | `{gpu}` / `{gpu_bar}` | GPU 占用 / 进度条 |
| `{mem_pct}` `{mem_used}` `{mem_total}` `{mem_free}` | 内存 | `{gpu_name}` `{gpu_temp}` `{gpu_power}` | 显卡名 / 温度 / 功耗 |
| `{disk}` / `{disk_bar}` | 磁盘活动率 | `{vram_used}` `{vram_total}` `{vram_pct}` | 显存 |
| `{disk_read}` `{disk_write}` | 磁盘读写速度 | `{free_C}` `{total_C}` `{used_pct_C}` | 分区空间（`D` 即 D 盘） |
| `{net_down}` `{net_up}` `{net_total}` | 网络速度 | `{net_adapter}` | 当前网卡名 |
| `{keys}` / `{key_count}` | 最近按键 / 累计次数 | `{time}` `{date}` `{weekday}` | 时间日期 |
| `{uptime}` `{host}` `{user}` | 运行时长 / 计算机名 / 用户名 |  |  |

设置窗口右侧列出全部占位符，双击插入；键盘显示可调数量、折叠阈值与最大字符数（默认 18），
超过上限从最旧的按键开始丢弃（`Back` / `Cap` / `ins` 等特殊键用短名字）。

## ⚙️ 配置

所有设置保存在 exe 同目录的 `settings.json`（源码运行时即 `bin\Release\settings.json`），
保存时自动备份为 `.bak`，损坏时另存 `.broken-时间戳` 并回退默认值；日志 `overlay.log` 与配置同目录。

```json
{
  "Content": "CPU {cpu:0.0}%   MEM {mem_pct:0}%",
  "Overlay": { "X": 48, "Y": 48, "Monitor": 0, "Locked": true, "Topmost": true },
  "Style": {
    "FontSize": 15, "BackgroundOpacity": 0.86, "TextOutline": true,
    "BackgroundImage": "", "ImageStretch": "UniformToFill", "FixedWidth": 0,
    "Acrylic": false, "AcrylicBlur": 12
  },
  "Data": { "MaxKeyChars": 18, "RepeatThreshold": 3, "KeyHistoryCount": 14 },
  "Behavior": { "RefreshMs": 500, "IntroEnabled": true, "WelcomeText": "Welcome Back", "CursorStyle": "Block" },
  "Hotkeys": { "ToggleOverlay": "", "CommandLine": "F9", "OpenSettings": "F10", "ToggleLock": "F8" },
  "Command": {
    "Shell": "cmd", "WorkingDirectory": "", "Truncate": true,
    "MaxLineChars": 120, "MaxLines": 30, "TimeoutSeconds": 20
  }
}
```

> 上面只列了常用字段，省略的都使用默认值，完整清单见下方字段表。

<details>
<summary><b>完整字段一览</b>（点击展开）</summary>

| 分组 | 字段 | 默认值 | 说明 |
| --- | --- | --- | --- |
| 顶层 | `Content` | 内置模板 | 显示内容，`\n` 换行，支持 `{变量}` |
| `Overlay` | `X` / `Y` | `48` / `48` | 相对显示器工作区的偏移（DIP） |
| | `Monitor` | `0` | 显示器序号，从 0 开始 |
| | `Locked` | `true` | `true` 鼠标穿透；`false` 可拖动 |
| | `Topmost` | `true` | 始终置顶 |
| | `CornerRadius` / `Shadow` | `10` / `true` | 圆角半径 / 是否画投影 |
| `Style` | `FontFamily` | `Microsoft YaHei UI` | 字体 |
| | `FontSize` / `Bold` | `15` / `false` | 字号 / 加粗 |
| | `Foreground` / `Background` | `#E6EDF3` / `#0D1117` | 文字 / 背景颜色 |
| | `BackgroundOpacity` | `0.86` | 背景不透明度 0~1 |
| | `Padding` / `LineHeight` | `14` / `0` | 内边距 / 行高（`0` = 自动） |
| | `TextAlignment` | `Left` | `Left` / `Center` / `Right` |
| | `TextOutline` / `OutlineColor` / `OutlineThickness` | `false` / `#000000` / `1.2` | 文字描边 |
| | `BackgroundImage` / `ImageOpacity` / `ImageStretch` | `""` / `1.0` / `UniformToFill` | 背景图片（`UniformToFill` `Uniform` `Fill` `None`） |
| | `FixedWidth` | `0` | 固定宽度；`0` = 自适应但只增不减 |
| | `Acrylic` | `false` | 毛玻璃背景（实验性）：抓取叠层后面的桌面并模糊，约 1.2 秒刷新一次 |
| | `AcrylicBlur` | `12` | 毛玻璃模糊强度（像素），`0` = 不模糊 |
| `Data` | `ShowCpu` `ShowGpu` `ShowMemory` `ShowDisk` `ShowNetwork` `ShowKeyboard` `ShowNvidia` | `true` | 是否采集该数据源 |
| | `GpuIndex` | `-1` | `-1` 汇总全部 GPU；`0`、`1`… 指定某块 |
| | `DiskDrives` / `NetworkAdapter` | `[]` / `""` | 统计分区 / 网卡实例名，空 = 自动 |
| | `KeyHistoryCount` / `MaxKeyChars` / `RepeatThreshold` | `14` / `18` / `3` | 键盘显示规则 |
| | `NvidiaIntervalMs` | `2000` | `nvidia-smi` 采样间隔 |
| `Behavior` | `RefreshMs` | `500` | 采样间隔（毫秒） |
| | `AutoStart` / `StartMinimized` / `TrayEnabled` | `false` / `false` / `true` | 开机自启 / 静默启动 / 托盘图标 |
| | `IntroEnabled` / `WelcomeText` | `true` / `Welcome Back` | 启动动画开关与欢迎语 |
| | `TypeSpeedMs` | `28` | 动画速度（毫秒/字符） |
| | `CursorStyle` / `CursorBlink` | `Block` / `true` | 光标样式（`None` `Block` `Bar` `Underline` `Outline`）与闪烁 |
| `Hotkeys` | `ToggleOverlay` / `CommandLine` / `OpenSettings` / `ToggleLock` | `""` / `F9` / `F10` / `F8` | 留空 = 不启用 |
| `Command` | `Shell` | `cmd` | `cmd` / `powershell` / `pwsh` / `custom` |
| | `CustomShell` | `cmd.exe /c {cmd}` | 自定义模板，`{cmd}` 替换为输入的命令 |
| | `WorkingDirectory` | `""` | 工作目录，空 = 用户主目录 |
| | `Truncate` / `MaxLineChars` / `MaxLines` | `true` / `120` / `30` | 行截断与返回行数上限 |
| | `TimeoutSeconds` | `20` | 命令超时秒数 |

</details>

<details>
<summary><b>设置窗口截图</b>（点击展开）</summary>

| 内容 | 外观 | 数据源 |
| --- | --- | --- |
| **行为** | **热键** | **命令行** |

修改会实时反映在预览里，点「保存并应用」写入配置并立即生效。

</details>

## 🔨 编译

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bat
build.bat      :: 编译到 bin\Release，并用自签名证书签名
publish.bat    :: 生成四个单文件 exe（自带运行时 + 依赖运行时，各 x64 / ARM64）
run.bat        :: 编译（如未编译）并在 bin\Release 内启动
```

`publish.bat` 的输出：

```
publish\x64\WindowOverlay.exe                   自带 .NET 运行时
publish\arm64\WindowOverlay.exe                 自带 .NET 运行时
publish\x64-requires-dotnet\WindowOverlay.exe   需要 .NET 8 桌面运行时
publish\arm64-requires-dotnet\WindowOverlay.exe 需要 .NET 8 桌面运行时
```

自带运行时的版本压缩后约 60 MB，目标机器无需安装 .NET；依赖运行时的版本只有约 0.4 MB。
签名使用自签名证书（主体为 GitHub 账号 `CRITTIof`，写入 `CurrentUser` 存储，不需要管理员），
所以 Windows 显示发布者名称而不是“未知发布者”。

## 🩺 故障排查

<details>
<summary><b>按键没有反应</b></summary>

先看 `overlay.log` 里有没有 `热键 F9 注册成功`：显示被占用就换一组组合键（「设置 → 热键」）。
启动动画期间、叠层隐藏时快捷命令行是刻意禁用的。命令行模式会临时把叠层切到前台并**关闭输入法**
（保证命令原样输入），点了别的窗口需要再按一次 `F9`；`F9` 后仍打不出字时用鼠标点一下叠层再试。
如果键盘那一行一直是 `—`，多半是全局键盘钩子被其它软件抢占（输入法、录屏、宏工具），关掉再试。

</details>

<details>
<summary><b>命令行输出是乱码</b></summary>

程序会先按 UTF-8 严格解码，失败再按系统 OEM 代码页（中文系统是 GBK）解码。
如果某个工具用的是第三种编码，可以改用走 PowerShell 的方式执行，或把「设置 → 命令行」的工具换成 `pwsh`。

</details>

## ⚠️ 已知限制

- 独占全屏（DirectX exclusive）游戏可能盖住叠层，改成无边框全屏即可
- 混合 DPI 多显示器下位置按主屏缩放换算，极端情况需手动微调
- 键盘钩子只能看到物理按键，拿不到输入法上屏的中文
- 命令行返回过多时叠层会向下变高，屏幕放不下就调小「最多保留行数」
- **毛玻璃是实验性功能**：程序定时截取叠层背后的桌面再模糊（约 1.2 秒刷新一次），不是系统的 DWM 材质。
  因此背后是视频或快速滚动的画面时会有轻微滞后，纯色背景下也几乎看不出模糊；
  建议把「背景不透明度」调到 0.5 左右，并用「模糊强度」调整模糊半径

## 🗂️ 目录结构

```
WindowOverlay.csproj   项目文件（net8.0-windows，WPF + WinForms 托盘）
build.bat / run.bat    编译签名 / 编译并启动
publish.bat            四个单文件 exe（x64 + ARM64，各含自带运行时 / 依赖运行时两种）
sign.ps1               自签名证书与签名
bin\Release\           编译输出，settings.json 与 overlay.log 也在这里
src/
  Core/                配置模型、JSON、日志
  Metrics/             PDH 封装、采样引擎、nvidia-smi
  Input/               键盘钩子、键名映射
  Text/                模板引擎、快捷命令行执行器
  Ui/                  叠层、设置窗口、托盘、热键、TextCanvas 自绘
docs/                  界面截图
assets/                应用图标与键帽字体（logo*.ico、keycaps/）
tools/make_logo.py     从 SVG 几何生成 assets/logo*.ico
```

## 📄 许可与字体致谢

- 本仓库的**代码以 [MIT 许可](LICENSE) 发布**，可自由使用、修改和再分发。
- 键帽字形来自 **[ReiKeycap](https://github.com/CRITTIof/rei-keycap-font)**（作者 CRITTIof），
  以 **SIL Open Font License 1.1** 授权，许可证见 `assets/keycaps/OFL.txt`。
  这里只使用 83 个键帽字形（U+E000–U+E052）；字体里额外的软硬件图标（U+E053–U+E05E）没有使用。
- 字形轮廓基于 **Noto Sans SemiBold**（Copyright 2018 The Noto Project Authors）与
  **JetBrains Mono SemiBold**（Copyright 2020 The JetBrains Mono Project Authors），两者同样以 OFL 1.1 使用。

<p align="center"><sub>基于 .NET 8 · WPF · PDH · 自绘文本渲染</sub></p>

---

<a id="english"></a>

## English

**An always-on-top desktop overlay with live hardware stats, keystroke display and a built-in command line.**
The overlay is click-through by default and never blocks your clicks. Content, style and behavior are all
configured in a visual settings window.

### Features

- **Live metrics** — CPU / GPU / disk / network / memory / drive space / time, sampled through PDH counters on a background thread
- **Templated content** — placeholders like `{cpu:0.0}%` with .NET format strings and alignment; 6 built-in presets
- **Keystroke display** — global keyboard hook, recent keys, repeat folding (`A×6`), max characters; keys are drawn as **ReiKeycap** keycaps (embedded font, mixes with normal text on one line)
- **Command line** — `F9` opens a three-line prompt under the overlay for CMD / PowerShell / custom commands; output is printed verbatim
- **Startup animation** — type / backspace animation with a simulated cursor (block / bar / underline / outline / off)
- **Appearance** — text outline, background image, frosted glass (experimental), fixed width, corner radius, opacity, fonts; size only grows, never jumps
- **Fonts** — every installed family, Chinese families shown with their Chinese names (e.g. 宋体 (SimSun)), with a filter box
- **Tray** — single-instance activation, auto-start, customizable global hotkeys, click-through lock
- **No runtime needed** — single-file exe; the target machine does not need .NET installed

### Quick start

Download from [Releases](https://github.com/CRITTIof/overlay-information-with-cmd/releases):

| File | For | Notes |
| --- | --- | --- |
| `WindowOverlay-x64.exe` | most PCs (x64) | .NET 8 runtime bundled, ~60 MB, just run |
| `WindowOverlay-arm64.exe` | Windows on ARM | .NET 8 runtime bundled, ~60 MB |
| `WindowOverlay-x64-requires-dotnet.exe` | x64 with .NET 8 Desktop Runtime | ~0.4 MB, needs the runtime |
| `WindowOverlay-arm64-requires-dotnet.exe` | ARM with .NET 8 Desktop Runtime | ~0.4 MB, needs the runtime |

Or build from source:

```bat
git clone https://github.com/CRITTIof/overlay-information-with-cmd.git
cd overlay-information-with-cmd
run.bat
```

`run.bat` builds and starts from `bin\Release`; `settings.json` and `overlay.log` live there too.

### Hotkeys

| Action | Default | Notes |
| --- | --- | --- |
| Toggle command line | `F9` | see below |
| Open settings | `F10` | also left-click the tray icon |
| Lock / unlock (click-through) | `F8` | the only switch for dragging and right-click; opening or closing settings never changes it, and saving settings never moves the overlay |
| Show / hide overlay | *unset* | tray menu, or bind your own |

Every hotkey can be changed under Settings → Hotkeys; an empty value disables it. If another app owns
the combination, the overlay falls back to `Ctrl+Alt+<key>`.

### Command line (`F9`)

Pressing `F9` adds three lines below the info: a blank line, the prompt `[CMD] [C:\Users\CRITT] >`, and the input line.

- Prompt format `[tool] [working dir] >`; tool names are upper-case, PowerShell shows as **PWSH**
- Tools: **CMD** (default), **Windows PowerShell**, **PowerShell 7**, **custom template** (`{cmd}` placeholder)
- Runs on a background thread and prints the output verbatim, then waits for the next command
- Long lines are truncated from the front with `…` (optional); the number of output lines can be capped
- 20 s timeout by default; output is decoded as UTF-8 first, then the OEM code page (no mojibake)
- Press `F9` (or `Esc`) again to leave and clear the buffer
- Disabled during the startup animation and while the overlay is hidden

### Placeholders

```text
CPU {cpu:0.0}%      →  CPU 23.4%
MEM {mem_pct:0}%    →  MEM 40%
{net_down,>10}      →  right-aligned to 10 characters
```

| Variable | Meaning | Variable | Meaning |
| --- | --- | --- | --- |
| `{cpu}` / `{cpu_bar}` | CPU usage / bar | `{gpu}` / `{gpu_bar}` | GPU usage / bar |
| `{mem_pct}` `{mem_used}` `{mem_total}` `{mem_free}` | memory | `{gpu_name}` `{gpu_temp}` `{gpu_power}` | GPU name / temp / power |
| `{disk}` / `{disk_bar}` | disk activity | `{vram_used}` `{vram_total}` `{vram_pct}` | VRAM |
| `{disk_read}` `{disk_write}` | disk throughput | `{free_C}` `{total_C}` `{used_pct_C}` | drive space |
| `{net_down}` `{net_up}` `{net_total}` | network | `{net_adapter}` | adapter name |
| `{keys}` / `{key_count}` | recent keys / total | `{time}` `{date}` `{weekday}` | time and date |
| `{uptime}` `{host}` `{user}` | uptime / host / user | | |

The settings window lists every placeholder; double-click to insert one. The keyboard display has key count,
repeat threshold and max characters (default 18); once over the limit the oldest keys are dropped.
`{keys}` renders as keycaps and still truncates by the original character count.

### Configuration

Settings live next to the exe as `settings.json` (`bin\Release\settings.json` when run from source). Every save
writes a `.bak`; a broken file is kept as `.broken-<timestamp>` before falling back to defaults. Logs go to
`overlay.log` in the same folder.

```json
{
  "Content": "CPU {cpu:0.0}%   MEM {mem_pct:0}%",
  "Overlay": { "X": 48, "Y": 48, "Monitor": 0, "Locked": true, "Topmost": true },
  "Style": { "FontSize": 15, "BackgroundOpacity": 0.86, "Acrylic": false, "AcrylicBlur": 12 },
  "Hotkeys": { "CommandLine": "F9", "OpenSettings": "F10", "ToggleLock": "F8" }
}
```

The JSON keys mirror the settings window; the full field table is in the Chinese section above.

### Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bat
build.bat      :: build to bin\Release and sign
publish.bat    :: four single-file exes (bundled / framework-dependent, x64 / ARM64)
run.bat        :: build if needed, then run from bin\Release
```

`publish.bat` writes `publish\x64`, `publish\arm64`, `publish\x64-requires-dotnet` and
`publish\arm64-requires-dotnet`. Bundled builds are ~60 MB; framework-dependent ones are ~0.4 MB.
Signing uses a self-signed certificate (subject `CRITTIof`, stored in `CurrentUser`, no admin needed),
so Windows shows a publisher name instead of “unknown publisher”.

### Troubleshooting

- **Hotkeys do nothing** — check `overlay.log` for `热键 F9 注册成功`; if the key is taken, pick another
  combination. The command line is intentionally disabled during the startup animation and while hidden.
- **Command output is garbled** — output is decoded as UTF-8 first, then the OEM code page; switch the tool
  to PowerShell / `pwsh` if a program uses a third encoding.
- **The key line stays `—`** — the global keyboard hook is probably taken by another app (IME, recorder, macro tool).

### Known limitations

- Exclusive full-screen DirectX games can cover the overlay; use borderless full-screen
- Mixed-DPI multi-monitor setups use the primary screen's scale; occasional manual tweaks may be needed
- The keyboard hook only sees physical keys, not IME composition
- Large command output grows the overlay downwards; lower the line cap if it does not fit
- Frosted glass is experimental: the desktop behind the overlay is captured and blurred (~1.2 s refresh),
  not a DWM material. Video behind it may lag slightly and flat colors barely show; set background
  opacity around 0.5 for the best result

### License and credits

- The app code is released under the **[MIT License](LICENSE)**.
- Keycap glyphs come from **[ReiKeycap](https://github.com/CRITTIof/rei-keycap-font)** by CRITTIof,
  licensed under the **SIL Open Font License 1.1** (see `assets/keycaps/OFL.txt`). Only the 83 keycap
  glyphs (U+E000–U+E052) are used; the extra software/hardware icons (U+E053–U+E05E) are ignored.
- Glyph outlines are based on **Noto Sans SemiBold** and **JetBrains Mono SemiBold**, both under OFL 1.1.

<p align="center"><sub>.NET 8 · WPF · PDH · custom text rendering</sub></p>
