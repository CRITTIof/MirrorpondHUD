<h1 align="center">MirrorpondHUD · 镜潭一览</h1>

<p align="center">
  <b>可增删的桌面信息卡片：系统数据、时钟、按键、命令行，各自一张独立小窗，按顺序堆叠在桌面最上层。</b><br>
  <sub>A modular set of always-on-top desktop cards with live hardware stats, a clock, keystroke display and a built-in command line.</sub>
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
  <img src="docs/overlay.png" width="580" alt="卡片效果">
</p>

<a id="简体中文"></a>

鼠标默认**完全穿透**，不挡任何点击；卡片、顺序、内容和样式都在设置窗口里调整。

## ✨ 功能

- **卡片化** — 系统信息 / 时钟 / 按键 / 文本信息 / 命令行五种卡片，可随时添加、删除、上下移动；同一种卡片可以有任意多张，名称也可以重名
- **常驻或呼出** — 每张卡片可以「常驻」在屏幕上，也可以关掉常驻、绑定一个全局热键按需呼出；两张卡片设成同一个热键时，按下会**同时**显示
- **按顺序堆叠** — 设置页里的顺序就是叠层上的顺序；第一张对齐到锚点，其余按各自的「与上方卡片间距」向下排列，解锁后拖动任意一张会移动整叠
- **实时数据** — CPU / GPU / 磁盘 / 网络 / 内存 / 分区空间 / 时间 / 运行时长，PDH 性能计数器采样，独立线程不卡 UI
- **模板化内容** — `{cpu:0.0}%`、`{net_down}`、`{cpu_icon} CPU {cpu:0.0}%`，支持 .NET 格式串与对齐；每张卡片单独编辑内容
- **Reicon 图标** — 内嵌 [ReiconFont](https://github.com/CRITTIof/reicon-font)：87 个键帽字形、软件 / 硬件 / 界面图标；CPU、GPU、RAM、ROM、网络上下行图标已接入信息显示，设置页的页签与增删移动按钮也使用它
- **按键显示** — 全局键盘钩子，最近按键、重复折叠（`A×6`）、最大字符数限制，按键以键帽字形显示
- **快捷命令行** — 命令行是一张普通卡片，按自己的热键展开后执行 CMD / PowerShell / 自定义命令，返回原样打印
- **外观可调** — 字体字号、描边、背景图、透明度、圆角、投影、固定宽度；尺寸只增不减，数值变化时不跳动
- **启动动画** — 打字 / 退格动画 + 模拟光标（方块 / 竖线 / 下划线 / 空心框 / 关闭）
- **托盘常驻** — 单实例唤醒、开机自启、可自定义的全局热键、鼠标穿透锁定
- **无依赖** — 单文件 exe，目标机器不需要安装 .NET

## 🚀 快速开始

**下载即用**：到 [Releases](https://github.com/CRITTIof/MirrorpondHUD/releases) 下载 `v2.0.0`：

| 文件 | 适用 | 说明 |
| --- | --- | --- |
| `MirrorpondHUD-x64.exe` | 普通电脑（x64） | 自带 .NET 8 运行时，双击即用 |
| `MirrorpondHUD-arm64.exe` | Windows on ARM | 自带 .NET 8 运行时 |
| `MirrorpondHUD-x64-requires-dotnet.exe` | 已装 .NET 8 桌面运行时 | 体积小，依赖系统运行时 |
| `MirrorpondHUD-arm64-requires-dotnet.exe` | 已装 .NET 8 桌面运行时的 ARM 机器 | 体积小，依赖系统运行时 |

**从源码运行**：

```bat
git clone https://github.com/CRITTIof/MirrorpondHUD.git
cd MirrorpondHUD
run.bat
```

`run.bat` 会自动编译，然后直接从 `bin\Release` 启动。开机自启会直接显示全部常驻卡片，不受「启动后最小化到托盘」影响。
配置文件和日志也生成在那里（`bin\Release\settings.json`、`bin\Release\mirrorpond.log`）。

## ⌨️ 快捷键

| 操作 | 默认 | 说明 |
| --- | --- | --- |
| 打开设置窗口 | `F10` | 也可左键单击托盘图标 |
| 锁定 / 解锁（鼠标穿透） | `F8` | 所有卡片能否拖动、能否点右键只由它切换；打开 / 关闭设置窗口不会改变锁定状态 |
| 快捷命令行 | `F9` | 属于「命令行」卡片，可在「内容」页改 |
| 显示 / 隐藏全部 | *未设置* | 从托盘菜单操作，或自行绑定 |
| 每张卡片的呼出热键 | 见设置 | 在「内容」页每张卡片里设置；非「常驻」卡片必须设置，否则保存时会提示 |

所有热键都能在设置里改，**留空 = 不启用**；全局热键被其它程序占用时会自动退到 `Ctrl+Alt+原键` 并记入日志。
两张卡片使用同一个组合键是允许的：按键一次，两张卡片同时呼出 / 收起。
再次双击 exe 不会起第二个进程，而是唤醒已在运行的实例的设置窗口。

## 🗂️ 卡片


「设置 → 内容」就是卡片管理器：

- **添加卡片**：点「添加卡片」，从菜单里选一种卡片，新卡片会出现在列表最后
- **展开 / 折叠**：点标题栏或右侧箭头；展开时显示名称、常驻、呼出热键、与上方卡片间距、内容，折叠时只显示名称和状态
- **移动**：点卡片右侧的移动按钮，选择「上移 / 下移」，列表顺序就是叠层上的显示顺序
- **删除**：点垃圾桶按钮，确认后保存并应用才真正移除
- **搜索**：右上角搜索框按名称 / 类型筛选，不影响实际顺序
- **常驻**：勾选后一直显示；取消后必须设置「呼出热键」
- **不再有实时预览**：内容在叠层上直接看效果，设置页只做编辑

五种卡片：

| 卡片 | 内容 | 说明 |
| --- | --- | --- |
| 系统信息 | CPU / 内存 / GPU / 网络 / 磁盘，带 Reicon 图标 | 默认内容见下 |
| 时钟 | 时间 / 日期 / 星期 / 运行时长 | 每秒刷新 |
| 按键 | 最近按键（键帽字形） | 数量、折叠阈值在「数据源」页 |
| 文本信息 | 任意文字 + 全部占位符 | 适合放常用信息 |
| 命令行 | 提示符 / 历史 / 输入 | 独占键盘，按自己的热键展开 |

卡片在屏幕上的位置由两部分决定：**锚点**（「行为」页的显示器 / X / Y）和**每张卡片的间距**。
第一张可见卡片对齐锚点，之后依次向下排；非常驻卡片被呼出时也会插入到它自己的位置，其它卡片顺势让开。
「行为」页的「应用位置」只移动整叠，不会改变顺序。

## 🖥️ 快捷命令行

按命令行卡片的热键（默认 `F9`）展开卡片，提示符格式为 `[工具] [工作目录] >`，工具名统一大写，PowerShell 显示为 **PWSH**。

- 工具可选 **CMD**（默认）/ **Windows PowerShell** / **PowerShell 7** / **自定义模板**（`{cmd}` 占位），在「命令行」页设置
- 命令在后台线程执行，返回内容**原样打印**，然后给出新提示符，可连续执行多条
- 单行过长时只保留**后面** N 个字符并加 `…`（可关闭），也可限制最多保留多少行
- 默认 20 秒超时，超时强制结束；输出先按 UTF-8 解码，失败退回系统 OEM 代码页（中文不乱码）
- **再按一次该热键（或 `Esc`）退出**，清空命令与返回，卡片收起
- 启动动画期间会先结束动画再打开

输入框里的输入法被刻意关闭，保证命令按原样输入；支持方向键、`Home` / `End`、`Ctrl+V`、`Ctrl+Z`。

## 🧩 显示内容与占位符

每张卡片的内容是一段支持多行的模板文本，用 `{变量}` 插入实时数据：

```
{cpu_icon} CPU {cpu:0.0}%   {mem_icon} MEM {mem_pct:0}%
{net_down_icon} {net_down}   {disk_icon} {disk:0.0}%
```

| 变量 | 含义 | 变量 | 含义 |
| --- | --- | --- | --- |
| `{cpu}` / `{cpu_bar}` | CPU 占用 / 进度条 | `{gpu}` / `{gpu_bar}` | GPU 占用 / 进度条 |
| `{mem_pct}` `{mem_used}` `{mem_total}` `{mem_free}` | 内存 | `{gpu_name}` `{gpu_temp}` `{gpu_power}` | 显卡名 / 温度 / 功耗 |
| `{disk}` / `{disk_bar}` | 磁盘活动率 | `{vram_used}` `{vram_total}` `{vram_pct}` | 显存 |
| `{disk_read}` `{disk_write}` | 磁盘读写速度 | `{free_C}` `{total_C}` `{used_pct_C}` | 分区空间（`D` 即 D 盘） |
| `{net_down}` `{net_up}` `{net_total}` | 网络速度 | `{net_adapter}` | 当前网卡名 |
| `{keys}` / `{key_count}` | 最近按键 / 累计次数 | `{time}` `{date}` `{weekday}` | 时间日期 |
| `{uptime}` `{host}` `{user}` | 运行时长 / 计算机名 / 用户名 | `{cpu_icon}` `{gpu_icon}` `{mem_icon}` `{disk_icon}` `{net_up_icon}` `{net_down_icon}` | Reicon 图标 |

设置页每张卡片下方都有占位符下拉框，选中即插入到光标处。键盘显示可调数量、折叠阈值与最大字符数（默认 18），
超过上限从最旧的按键开始丢弃（`Back` / `Cap` / `ins` 等特殊键用短名字）。

## ⚙️ 配置

所有设置保存在 exe 同目录的 `settings.json`（源码运行时即 `bin\Release\settings.json`），
保存时自动备份为 `.bak`，损坏时另存 `.broken-时间戳` 并回退默认值；日志 `mirrorpond.log` 与配置同目录。

```json
{
  "SchemaVersion": 2,
  "Overlay": { "X": 1483, "Y": 127, "Monitor": 0, "Locked": true },
  "Style": { "FontFamily": "JetBrains Mono NL", "FontSize": 15, "BackgroundOpacity": 0.3 },
  "Cards": [
    {
      "Id": "3f6c...",
      "Kind": "metrics",
      "Name": "系统信息",
      "Resident": true,
      "Hotkey": "",
      "Spacing": 0,
      "Content": "{cpu_icon} CPU {cpu:0.0}%   {mem_icon} MEM {mem_pct:0}%"
    }
  ]
}
```

- `SchemaVersion` 为 2；旧版单窗口配置会在启动时自动迁移（旧 `Content` 归到系统信息卡、`{keys}` / 时间行拆到按键 / 时钟卡、命令行热键归到命令行卡），迁移后立即写回
- `Cards` 的顺序就是叠层顺序；`Spacing` 是与上一张可见卡片的间距
- `Resident=false` 的卡片必须有 `Hotkey`，否则保存时会提示
- 外观（`Style`）是**全局**的：所有卡片共用字体、颜色、背景、圆角等，卡片只负责内容与位置

## 🔨 编译

需要 .NET 8 SDK：

```bat
dotnet build MirrorpondHUD.csproj -c Release
```

输出在 `bin\Release\MirrorpondHUD.exe`。`build.bat` 会编译并调用 `sign.ps1` 做本地签名；
`publish.bat` 生成四个单文件 exe（x64 / ARM64 × 自带运行时 / 依赖运行时）。

```bat
run.bat            :: 编译并运行
build.bat          :: 只编译
publish.bat        :: 生成 publish\ 下的四个单文件
```

## 🩺 故障排查

- **按键不显示** — 先看日志里「键盘钩子：已安装」；以管理员身份运行的程序会拦住普通权限程序的全局钩子，反之亦然。程序被放在受控目录、以低完整性级别运行时，Windows 会拒绝键盘钩子、托盘和全局热键
- **中文乱码** — 命令行输出先按 UTF-8 解码，失败再按系统 OEM 代码页；若是 `chcp 65001` 的旧程序，可在命令前自行加 `chcp 936`
- **热键没反应** — 多半被其它程序占用。全局热键会自动退到 `Ctrl+Alt+原键`；卡片热键注册失败会在托盘气泡和日志里提示，换一个组合键即可
- **卡片不见了** — 检查「内容」页是否取消了「常驻」且没设热键；「显示 / 隐藏全部」也可能整体隐藏了叠层
- **日志与配置** — `bin\Release\mirrorpond.log`、`bin\Release\settings.json`

## ⚠️ 已知限制

- 同一时间只允许一张命令行卡片处于输入状态；多张命令行卡片会共用同一次命令会话
- 卡片结构（增删 / 排序 / 常驻 / 热键 / 内容）和外观都在「保存并应用」后生效
- 毛玻璃背景仍是实验功能，依赖抓屏，抓屏排除需要 Windows 10 2004+；想做得更好见 [docs/glass-options.md](docs/glass-options.md)
- 运行期插件（`cards/*.dll`）暂未实现；加一种内置卡片只需在 `src/Cards` 新增一个实现 `ICardModule` 的类，宿主不用改
- 卡片目前都是文字型（`TextCanvas` 绘制），文件中转站、AI 对话等富内容模块需要新的卡片基类

## 🗂️ 目录结构

```
src/
  App.xaml(.cs)          入口、单实例、命令行参数
  Cards/                 卡片模块：ICardModule / CardRegistry / 各卡片
  Core/                  设置、迁移、JSON、日志、图标、键帽映射
  Metrics/               PDH / nvidia-smi 采样
  Text/                  模板引擎、占位符、按键名、命令执行
  Ui/                    叠层窗口、绘制、设置窗口、托盘、热键、自启
docs/                    设计文档与截图
assets/keycaps/          ReiconFont 与映射文档
```

设计资料：

- [docs/cards-architecture.md](docs/cards-architecture.md) — 模块化卡片架构与扩展方式
- [docs/glass-options.md](docs/glass-options.md) — 毛玻璃方案调研（分层窗口 vs DWM vs 自采 + GPU 模糊）

## 📄 许可与字体致谢

本项目以 **MIT** 许可发布，见 [LICENSE](LICENSE)。

按键与图标字体来自 [ReiconFont](https://github.com/CRITTIof/reicon-font)，以 **SIL Open Font License 1.1** 发布，
许可原文见 `assets/keycaps/OFL.txt`；87 个键帽与 27 个图标的码位见 `assets/keycaps/MAPPING.md`。

---

<a id="english"></a>

# MirrorpondHUD

**A modular always-on-top desktop HUD.** Each card is an independent transparent window: system stats, clock, keystrokes, free-form text and a command line. Cards can be added, removed, reordered, pinned as resident, or summoned with their own global hotkey.

<p align="center">
  <img src="docs/overlay.png" width="580" alt="Cards">
</p>

## Features

- **Card modules** — metrics, clock, keys, text and command line; add / delete / reorder from *Settings → Content*, duplicates allowed
- **Resident or hotkey** — a card is either always visible, or hidden until its own hotkey is pressed; two cards may share one hotkey and appear together
- **Stack layout** — list order is screen order; the first card is anchored at a monitor-relative X/Y, every following card keeps its own gap to the previous one; dragging any card moves the whole stack
- **Live metrics** — CPU / GPU / disk / network / memory / drives / uptime through PDH counters on a background thread
- **Templated content** — `{cpu:0.0}%`, `{net_down}`, `{cpu_icon} CPU {cpu:0.0}%`; every card has its own template
- **Reicon icons** — embedded [ReiconFont](https://github.com/CRITTIof/reicon-font) keycaps and icons, including CPU / GPU / RAM / ROM / network arrows in the metrics card and all settings-tab / add / delete / move icons
- **Click-through** — the overlay never blocks the desktop unless unlocked with `F8`
- **Command line card** — `F9` by default, runs CMD / PowerShell / PowerShell 7 / custom, prints output verbatim
- **No runtime needed** — self-contained single-file builds for x64 and ARM64

## Quick start

Download `v2.0.0` from [Releases](https://github.com/CRITTIof/MirrorpondHUD/releases) (`MirrorpondHUD-x64.exe` is the usual choice), or build from source:

```bat
git clone https://github.com/CRITTIof/MirrorpondHUD.git
cd MirrorpondHUD
run.bat
```

`run.bat` builds into `bin\Release` and starts the app; settings and `mirrorpond.log` live next to the exe.

## Hotkeys

| Action | Default |
| --- | --- |
| Open settings | `F10` |
| Lock / unlock (click-through) | `F8` |
| Command line card | `F9` (configurable per card) |
| Show / hide everything | unbound |
| Per-card summon | configured in *Content* |

Global hotkeys fall back to `Ctrl+Alt+<key>` when occupied. Card hotkeys are explicit: duplicates are allowed and summon all matching cards.

## Configuration

Everything is stored in `settings.json` next to the executable (`SchemaVersion: 2`). Legacy single-overlay configs are migrated on first launch and written back. `Cards[]` holds `Id`, `Kind`, `Name`, `Resident`, `Hotkey`, `Spacing` and `Content`; the list order is the on-screen order. Appearance is global.

## Build

```bat
dotnet build MirrorpondHUD.csproj -c Release
publish.bat        :: four self-contained / framework-dependent single-file builds
```

## Credits and license

MIT — see [LICENSE](LICENSE). Keycap and icon glyphs come from [ReiconFont](https://github.com/CRITTIof/reicon-font), licensed under the SIL Open Font License 1.1 (`assets/keycaps/OFL.txt`, mapping in `assets/keycaps/MAPPING.md`).

Design notes: [docs/cards-architecture.md](docs/cards-architecture.md) and [docs/glass-options.md](docs/glass-options.md).
