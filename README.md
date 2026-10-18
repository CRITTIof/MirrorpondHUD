# 桌面叠层（WindowOverlay）

一个始终置顶的悬浮叠层：显示自定义文本 + CPU / GPU / 磁盘 / 网络 / 内存 / 分区 / 实时键盘输入，
附带一个可视化设置程序。鼠标默认**完全穿透**，不挡任何点击。

![叠层效果](docs/overlay.png)

## 快速开始

```bat
run.bat
```

首次运行会自动编译（需要 .NET 8 SDK），然后把运行时文件复制到
**`%LOCALAPPDATA%\Programs\WindowOverlay\`** 并从那里启动。

> **为什么要复制到外面再启动？**
> 如果项目放在受限目录（AI 工作区、沙箱、某些受管文件夹）里，从那里启动的进程会被系统
> 降权到 **Low 完整性级别**，Windows 随后会拒绝托盘图标、全局热键和键盘钩子——程序看起来
> “只存在于任务管理器里”。安装到 `%LOCALAPPDATA%\Programs` 下运行就完全正常。
> 程序启动时会自己检查完整性级别，一旦发现受限会弹窗说明。

不建议直接双击 `bin\Release\WindowOverlay.exe`（除非该目录不在受限位置）。
目标机器只需要 **.NET 8 桌面运行时**；如果希望对方什么都不装，用 `publish.bat` 打成独立 exe
后同样建议放到 `%LOCALAPPDATA%\Programs\` 下使用。

## 快捷键与托盘

| 操作 | 作用 |
| --- | --- |
| **左键单击托盘图标** | 打开设置窗口 |
| **右键单击托盘图标** | 菜单：显示/隐藏叠层、设置、锁定、开机自启、退出 |
| `F9` | 显示 / 隐藏叠层 |
| `F10` | 打开设置窗口 |
| `F8` | 锁定 / 解锁叠层 |

锁定＝鼠标穿透（叠层像水印一样，点击直接落到下面的窗口）；解锁后可以拖动叠层、右键菜单、双击打开设置。
三个热键都可以在「设置 → 热键」里改成任意组合键；若组合键被其它程序占用，程序会自动退到
`Ctrl+Alt+原键` 并记入日志。

> 再次双击 exe 不会起第二个进程，而是把已经在运行的那个实例的设置窗口唤出来（单实例唤醒）。

## 显示内容（模板占位符）

内容是一段模板文本，支持多行。用 `{变量}` 插入实时数据，支持 .NET 格式串与对齐：

```
CPU {cpu:0.0}%          →  CPU 23.4%
MEM {mem_pct:0}%        →  MEM 40%
{net_down,>10}          →  右对齐到 10 字符
```

| 变量 | 含义 | 变量 | 含义 |
| --- | --- | --- | --- |
| `{cpu}` / `{cpu_bar}` | CPU 占用率 / 进度条 | `{gpu}` / `{gpu_bar}` | GPU 占用率 / 进度条 |
| `{mem_pct}` `{mem_used}` `{mem_total}` `{mem_free}` | 内存 | `{gpu_name}` `{gpu_temp}` `{gpu_power}` | 显卡名 / 温度 / 功耗 |
| `{disk}` / `{disk_bar}` | 磁盘活动率 | `{vram_used}` `{vram_total}` `{vram_pct}` | 显存 |
| `{disk_read}` `{disk_write}` | 磁盘读写速度 | `{free_C}` `{total_C}` `{used_pct_C}` | C 盘空间（换成 D 即 D 盘） |
| `{net_down}` `{net_up}` `{net_total}` | 网络上下行速度 | `{net_adapter}` | 当前网卡名 |
| `{keys}` | 最近按键 | `{key_count}` | 累计按键次数 |
| `{time}` `{date}` `{weekday}` | 时间日期 | `{uptime}` `{host}` `{user}` | 运行时长 / 计算机名 / 用户名 |

设置窗口右侧列出全部占位符，双击即可插入；也内置了 6 套预设模板（标准面板、极简一行、游戏性能、
键盘直播、磁盘与网络、迷你状态条）。

### 键盘显示规则

只显示**最近 N 个按键**，连续重复超过阈值时折叠成一个 token：

```
按 A A A          →  A A A
按 A A A A A A    →  A×6
按 Ctrl C Ctrl V  →  Ctrl C Ctrl V
```

显示数量与折叠阈值在「设置 → 数据源 → 键盘输入」里调整，也可以随时清空记录。
按键只记录物理键，**输入法上屏的中文拿不到**（会显示 `v`、`Enter` 等）。

## 设置窗口

| 内容 | 外观 |
| --- | --- |
| 模板编辑 + 占位符 + 实时预览 | 字体、颜色、透明度、圆角、投影 |
| **数据源** | **行为 / 热键** |

所有修改都会实时反映在预览里，点「保存并应用」后写入配置文件并立即生效。

## 配置文件

默认在 **exe 同目录** `settings.json`。用 `run.bat` 启动时，这个目录是
`%LOCALAPPDATA%\Programs\WindowOverlay\`（不可写时退回 `%LOCALAPPDATA%\WindowOverlay\`）。
保存时会自动备份为 `settings.json.bak`；配置损坏时会另存为 `.broken-时间戳` 并回退默认值。
日志 `overlay.log` 与配置文件同目录。

```json
{
  "Content": "CPU {cpu:0.0}%   MEM {mem_pct:0}%",
  "Overlay": { "X": 48, "Y": 48, "Monitor": 0, "Locked": true, "Topmost": true },
  "Style": { "FontFamily": "Microsoft YaHei UI", "FontSize": 15, "BackgroundOpacity": 0.86 },
  "Data": { "ShowCpu": true, "ShowGpu": true, "ShowKeyboard": true, "RepeatThreshold": 3 },
  "Behavior": { "RefreshMs": 500, "AutoStart": false },
  "Hotkeys": { "ToggleOverlay": "F9", "OpenSettings": "F10", "ToggleLock": "F8" }
}
```

### 字段一览

| 分组 | 字段 | 默认值 | 说明 |
| --- | --- | --- | --- |
| 顶层 | `Content` | 内置模板 | 显示内容，`\n` 表示换行，支持 `{变量}` |
| `Overlay` | `X` / `Y` | `48` / `48` | 相对当前显示器工作区的偏移（DIP） |
| | `Monitor` | `0` | 显示器序号，从 0 开始 |
| | `Locked` | `true` | `true` 鼠标穿透；`false` 可拖动 |
| | `Topmost` | `true` | 始终置顶 |
| | `CornerRadius` | `10` | 圆角半径 |
| | `Shadow` | `true` | 是否画投影 |
| `Style` | `FontFamily` | `Microsoft YaHei UI` | 字体 |
| | `FontSize` | `15` | 字号 |
| | `Bold` | `false` | 加粗 |
| | `Foreground` | `#E6EDF3` | 文字颜色 |
| | `Background` | `#0D1117` | 背景颜色 |
| | `BackgroundOpacity` | `0.86` | 背景不透明度 0~1 |
| | `Padding` | `14` | 内边距 |
| | `LineHeight` | `0` | 行高，`0` = 自动 |
| | `TextAlignment` | `Left` | `Left` / `Center` / `Right` |
| `Data` | `ShowCpu` `ShowGpu` `ShowMemory` `ShowDisk` `ShowNetwork` `ShowKeyboard` `ShowNvidia` | `true` | 是否采集该数据源（关掉可省 CPU） |
| | `GpuIndex` | `-1` | `-1` 汇总全部 GPU；`0`、`1`… 指定某块 |
| | `DiskDrives` | `[]` | 参与统计的分区，如 `["C:\\","D:\\"]`；空 = 全部固定磁盘 |
| | `NetworkAdapter` | `""` | 网卡 PDH 实例名；空 = 自动选流量最大的 |
| | `KeyHistoryCount` | `14` | 键盘行最多显示多少个按键 |
| | `RepeatThreshold` | `3` | 连续重复超过该次数就折叠成 `A×N` |
| | `NvidiaIntervalMs` | `2000` | `nvidia-smi` 采样间隔 |
| `Behavior` | `RefreshMs` | `500` | 采样间隔（毫秒） |
| | `AutoStart` | `false` | 写入 `HKCU\...\Run` 开机自启 |
| | `StartMinimized` | `false` | 启动时不显示叠层，只在托盘 |
| | `TrayEnabled` | `true` | 是否显示托盘图标 |
| `Hotkeys` | `ToggleOverlay` | `F9` | 显示 / 隐藏 |
| | `OpenSettings` | `F10` | 打开设置 |
| | `ToggleLock` | `F8` | 锁定 / 解锁 |

> 手改 JSON 后程序会在下次启动时读取；若只想改少量样式，直接改这个文件比点界面更快。

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `--minimized` | 启动时不显示叠层，只在托盘里 |
| `--screenshot <路径>` | 用演示数据把叠层渲染成 PNG 后退出（无需显示窗口） |
| `--screenshot-settings <路径>` | 把设置窗口的每个页签渲染成 PNG 后退出 |
| `--selftest` | 跑一遍真实采样 / 键盘钩子 / 热键注册，结果写入日志 |
| `--smoke <秒>` | 正常启动后 N 秒自动退出（用于验证启动链路） |

## 打包

```bat
publish.bat
```

生成 `publish\WindowOverlay.exe`（单文件、自带运行时，约 100 MB）。目标机器无需安装 .NET。

## 实现要点

- **数据采集**：PDH 性能计数器（`\GPU Engine(*engtype_3D)\Utilization Percentage`、
  `\PhysicalDisk(_Total)\% Disk Time`、`\Network Interface(*)\Bytes Received/sec` 等）+
  `GlobalMemoryStatusEx`，独立后台线程，UI 永不被采样阻塞。所有计数器名走英文接口，中文系统同样可用。
- **GPU**：按 LUID 汇总所有 3D 引擎实例；温度 / 显存 / 功耗来自 `nvidia-smi`（2 秒一次，避免开销）。
- **键盘**：`WH_KEYBOARD_LL` 全局钩子，装在独立线程 + 消息循环上，过滤软件注入的按键；首个按键会写日志。
- **托盘**：原生 `Shell_NotifyIcon`（左键单击呼出设置、右键菜单、监听 `TaskbarCreated` 自动恢复），
  启动时用 `Shell_NotifyIconGetRect` 自检图标是否真的进了托盘。
- **热键**：隐藏消息窗口 + `RegisterHotKey`，注册失败时自动退到 `Ctrl+Alt+原键` 并记入日志。
- **叠层**：WPF 无边框透明窗口，`WS_EX_TRANSPARENT` 实现穿透，`SetWindowPos(HWND_TOPMOST)` 定时重申置顶。
- **渲染**：采样线程只负责取数，UI 每 120 ms 重新渲染一次文本，打字时反馈不卡顿。

## 故障排查

**看不到托盘图标**
Windows 11 会把新加入的图标默认放进任务栏的 `^` 隐藏区。点一下 `^`，把「桌面叠层」拖到外面即可常显；
或：设置 → 个性化 → 任务栏 → 其他系统托盘图标 → 打开「桌面叠层」。
程序启动后也会弹一个气泡提示，说明它已经在托盘里运行。

**热键不生效**
先看 `overlay.log` 里有没有 `热键 F9 注册成功`。若显示"被占用"，在设置里换一组即可；
若注册成功但按下没反应，多半是进程被安全策略限制（见下一条）。

**日志出现 `Shell_NotifyIcon ... err=5`，或者按键、热键完全没反应**
这是 **完整性级别（Integrity Level）** 问题：进程被降权到 Low 后，Windows 会拒绝托盘图标、
全局热键和键盘钩子。日志第一行会写明 `进程完整性级别: Low（1000）`。
解决办法就是双击项目里的 `run.bat`——它会自动把程序部署到
`%LOCALAPPDATA%\Programs\WindowOverlay` 再从那里启动；也可以手动把 `bin\Release` 整个目录
复制到该位置运行。正常情况下日志应显示 `进程完整性级别: Medium（2000）` 且
`托盘图标已进入托盘区`。

**键盘那一行一直是 `—`**
日志里应当出现 `键盘钩子：已捕获首个按键 X`。若整场没有这行，说明全局钩子没收到任何输入，
通常仍是受限环境，或与其它同样安装全局键盘钩子的软件冲突。

## 已知限制

- 独占全屏（DirectX exclusive）游戏可能盖住叠层，改成无边框全屏即可。
- 混合 DPI 的多显示器上，位置按主屏缩放比例换算，极端情况下需要手动微调。
- 圆角边缘在高分屏上是抗锯齿的，不会像原生控件那样完全平滑。
- 键盘钩子看不到输入法上屏的中文，只能看到物理按键。

## 目录结构

```
WindowOverlay.csproj     项目文件（net8.0-windows，WPF + WinForms 托盘）
build.bat                编译
run.bat                  部署到 %LOCALAPPDATA%\Programs\WindowOverlay 并启动
publish.bat              打包独立 exe
app.manifest             Per-Monitor V2 DPI 感知
src/
  App.xaml(.cs)          入口、单实例、命令行分发
  SelfTest.cs            --selftest 自检
  Core/                  配置模型、JSON、日志、设置读写
  Metrics/               PDH 封装、采样引擎、nvidia-smi
  Input/                 键盘钩子、键名映射
  Text/                  模板引擎、变量表
  Ui/                    叠层窗口、设置窗口、托盘、热键、主题
docs/                    界面截图
```
