# MirrorpondHUD v2.44.2 问题修复清单（175 项）

> 来源：[review-2.44.2.md](review-2.44.2.md)。修复后在 `[ ]` 里打 `x`，并在「改动文件」列写明改过的文件。

> 状态：`[x]` 已修并随本次构建验证；`[~]` 已改但需真机/特定环境验证；`[-]` 不修（写明理由）。

## 本次修复摘要（2026-11-12）

- P1 **26/26 已处理**（提交 `38c07ac`）：20 条 `[x]`、6 条 `[~]`。
- P2 **87/87 已处理**：55 条 `[x]`、31 条 `[~]`、1 条 `[-]`，0 条待办。
- P3 **62/62 已处理**：50 条 `[x]`、11 条 `[~]`、1 条 `[-]`，0 条待办。
- 本轮（2.45.1 第五轮）提交：主线 `fe332a5`、玻璃 `dac04cc`、文本 `cbe5cc4`、插件 `73c549c`、媒体 `0ac1c71`；`dotnet build` 0 警告 0 错误，两种 `--selftest` 退出 0，xUnit 12/12，`dotnet format --verify-no-changes` 0，卡片/设置窗截图正常。
- 第六轮（P3）提交：核心/跨层 `bd0a34a`、文本 `a587442`、插件 `7ff99d8`、媒体 `4c4a442`、运行时 `bbc39b2`、UiKit `9e3d706`；空 catch 100→36，三种截图冒烟通过。
- 其中 `X-3` 为顺带修复：源文件裸 NUL 导致 git 把 `RecycleBin.cs` 当二进制。
- 第一轮 P2 已由 `82a955e` 提交；决策项累计再改 **22 个文件，+294 / −81 行**。P1 在 `38c07ac`。`dotnet build` 0 警告 0 错误，`--selftest` 退出 0（失败项 0），`dotnet format --verify-no-changes` 退出 0。

## P1（26 项，本次全部处理）

| 状态 | 编号 | 模块 | 位置 | 问题 | 改动文件 / 说明 |
| --- | --- | --- | --- | --- | --- |
| [x] | C-1 | 契约/核心 | src/Core/SettingsLoader.cs:261（唯一精确取键处）、:68-72（补写分支）、:172-182（FindKey 大小写不敏感）、src/Core/ConfigStore.cs:206-211（migrated 触发自动落盘） | schemaversion 大小写不一致会绕过"高版本只读"保护，把新版配置静默降级覆盖 | src/Core/SettingsLoader.cs；src/SelfTest.cs（TryReadVersion 改用 FindKey；补自检断言（schemaversion:999 → 只读）） |
| [x] | C-2 | 契约/核心 | src/Core/ConfigStore.cs:366-368、src/Ui/SettingsApplier.cs:392-398 | DescribeIoError 在"报错"路径里自己会抛异常，保存失败可能升级成进程退出 | src/Core/ConfigStore.cs；src/Ui/SettingsApplier.cs（按命中后缀长度裁剪；SaveSettings 加兜底 try） |
| [x] | C-3 | 契约/核心 | src/Core/ConfigStore.cs:94-99（提示）、:312-317（拒绝）、:76（LastLoadReadOnly 只在 Load 开头复位） | 只读加载后提示"可重新保存"，但整个会话内保存必然被拒 → 用户改动全部丢失 | src/Core/ConfigStore.cs；src/Ui/SettingsWindow.xaml；src/Ui/SettingsWindow.Load.cs；src/Ui/SettingsWindow.Save.cs（文案改为“需重启”；只读时禁用保存并显式提示） |
| [x] | C-4 | 契约/核心 | src/Contracts/Text/TemplateEngine.cs:84、:117-120 | 模板对齐参数没有上限：{x,-2147483648} 抛 OverflowException，{x,2147483647} 申请 2 GB | src/Contracts/Text/TemplateEngine.cs；src/SelfTest.cs（对齐宽度夹取到 1024；补自检断言（int.MinValue/int.MaxValue）） |
| [x] | C-5 | 契约/核心 | src/Core/ConfigStore.cs:203-211（返回值无人接收）、:388-399（备份命名）、src/App.xaml.cs:143（只消费 LastLoadWarning） | 损坏恢复后的"自动落盘失败"被丢弃，且 .broken-* 每次启动新增一个、永不清理（历史项 L-18 仍未修） | src/Core/ConfigStore.cs（自动落盘失败进 LastLoadWarning+Error；.broken-* 只留最近 3 个） |
| [x] | C-6 | 契约/核心 | src/Contracts/Core/Settings.cs:940-968 | CardSettings.ExtrasAs<T> 有时返回副本、有时返回活引用，插件改完可能静默不落盘 | src/Contracts/Core/Settings.cs（ExtrasAs 首次绑定即缓存回 Extras，统一返回活引用） |
| [x] | S-01 | 设置/托盘 | src/Ui/TrayIcon.cs:37-38、:125-132、:288-297 | 托盘左键单击永远无效：NIN_SELECT / NIN_KEYSELECT 常量算错 | src/Ui/TrayIcon.cs（NIN_SELECT/KEYSELECT 改为 WM_USER+0/1；补 v3 鼠标消息回退） |
| [x] | S-02 | 设置/托盘 | src/Ui/HotkeyRouter.cs:121-125、src/Ui/SettingsApplier.cs:148-155、src/Ui/SettingsWindow.Save.cs:142-149、src/Core/ConfigStore.cs:352 | 保存链路的二次落盘会让"保存失败"变成假消息或假成功（L-22 的次生后果） | src/Ui/SettingsApplier.cs；src/Ui/HotkeyRouter.cs；src/Ui/OverlayController.cs（改为先注册热键、Apply 统一落盘一次；路由 persist:false） |
| [x] | S-03 | 设置/托盘 | src/Input/KeyboardHook.cs:159-174、:116 | KeyboardHook.Dispose 在 Join 超时后仍释放 _ready，后台线程可抛 ObjectDisposedException 终止进程 | src/Input/KeyboardHook.cs（只有 Join 成功才 Dispose；超时保留事件对象并 Warn） |
| [x] | S-04 | 设置/托盘 | src/Ui/SettingsWindow.Save.cs:139、src/Ui/SettingsWindow.Load.cs:178、src/Ui/SettingsWindow.xaml.cs:288-295、src/Ui/HotkeyRouter.cs:123-124 | 一次"保存并应用"把整棵卡片列表重建三次，模块编辑区块被反复创建/丢弃 | src/Ui/SettingsApplier.cs；src/Ui/HotkeyRouter.cs；src/Ui/OverlayController.cs（路由不再 Reload/二次保存；保存只重建一次） |
| [~] | G-1 | 玻璃/渲染 | src/Ui/DesktopFrameSource.cs:161（默认值 src/Contracts/Core/Settings.cs:593） | DXGI 桌面复制被 OS 生成号门控：Auto 在 Win10 与 Win11 24H2 及更早一律退回 18 ms 的 GDI 读屏 | src/Ui/DesktopFrameSource.cs（移除 build 门控；19/22 双下标逐个试 + GetDesc 校验。需 Win10 / 24H2 真机确认不崩） |
| [x] | G-2 | 玻璃/渲染 | src/Ui/GlassFeed.cs:715（复位点 :734-739） | TryCreate 失败一次后 _frameSourceTried 永不复位——连改设置都救不回来，只能重启进程 | src/Ui/GlassFeed.cs（失败后 30s 重试；改设置/空闲释放都会复位） |
| [x] | G-3 | 玻璃/渲染 | src/Ui/GlassCapture.cs:128（写 :455-465、:504）；src/Ui/GlassRuntime.cs:295 | GlassCapture.Frames 是静态 List，多张卡并发 Grab 时无锁读写——注释里的"只在 UI 线程"假设不成立 | src/Ui/GlassCapture.cs（静态 Frames 的读/写/删除全部加锁） |
| [~] | G-4 | 玻璃/渲染 | src/Ui/GlassFeed.cs:274（另见 :300-302） | SetWindowDisplayAffinity 的返回值被丢弃：截屏排除失败时玻璃会拍到自己，零日志 | src/Ui/GlassFeed.cs（SetLastError + 两处返回值检查并 Warn。需 Win10 旧版或拦截类软件验证） |
| [~] | G-5 | 玻璃/渲染 | src/Ui/GlassCapture.cs:502；src/Ui/GlassRuntime.cs:293,311 | PrintWindow 没有超时，被挂起的 _glassBusy 会把兼容玻璃永久冻住且不报错 | src/Ui/GlassRuntime.cs（2 秒看门狗 + 采集代次；旧结果按代次丢弃。需挂起窗口复现） |
| [~] | K-1 | UiKit/媒体 | src/UiKit/Media/CardMediaView.cs:644、src/UiKit/Media/FfmpegPlayer.cs:126、:215-218、:523-524 | ffmpeg 侧输出尺寸与读帧缓冲尺寸不保证一致，非 16:9 / 奇数高视频会整段画面错位 | src/UiKit/Media/FfmpegPlayer.cs（scale 后加 pad 强制 W×H。需用非 16:9 视频实测字节数） |
| [~] | K-2 | UiKit/媒体 | src/UiKit/Media/WaveOutPlayer.cs:341、:502、:545 | WaveOutPlayer 自然 EOF 收尾未 waveOutReset/未等最后一缓冲播完，UnprepareHeader 失败被吞后仍 FreeHGlobal | src/UiKit/Media/WaveOutPlayer.cs（EOF 先 waveOutReset；Unprepare/Close 返回码检查，失败保留缓冲。需音频设备实测） |
| [x] | K-3 | UiKit/媒体 | src/UiKit/Media/WaveOutPlayer.cs:302 | WaveOutPlayer.Feed 把短读当整块，尾部零填充 → 周期性插入静音，音频总长被拉长 | src/UiKit/Media/WaveOutPlayer.cs（循环读满整块，仅真正 EOF 尾块补零） |
| [~] | P-1 | 插件/指标 | src/Plugins/DropStation/RecycleBin.cs:65；承诺文案 src/Plugins/DropStation/StationFileActions.cs:141-146 | "移到回收站"缺 FOF_WANTNUKEWARNING：无法进回收站的文件会被静默永久删除 | src/Plugins/DropStation/RecycleBin.cs（加 FOF_WANTNUKEWARNING。需网络盘/超配额目录验证） |
| [x] | P-2 | 插件/指标 | src/Plugins/DropStation/StationCopyController.cs:314-337（删除）、调用链 :101-109 | Move 模式在 UI 线程递归删源：大目录冻结叠层且不可取消 | src/Plugins/DropStation/StationCopyController.cs（删源与目标大小移入工作线程；完成回调只读结果） |
| [x] | P-3 | 插件/指标 | src/Plugins/DropStation/StationView.cs:589-611；回填 src/Plugins/DropStation/StationCopyController.cs:307-312,344-358 | 清空 / 移除与在途复制竞态仍在：目标已落盘但列表里没有（孤儿文件） | src/Plugins/DropStation/StationCopyController.cs；src/Plugins/DropStation/StationView.cs（清空/移除先取消在途任务；完成时按 HasItem 合并记录） |
| [x] | P-4 | 插件/指标 | src/Metrics/PdhQuery.cs:58-66 → src/Metrics/MetricEngine.cs:63-67 → src/Ui/OverlayController.cs:631-636 → src/App.xaml.cs:165-177 | PdhQuery 构造抛异常会把整个宿主按"启动失败"关掉 | src/Metrics/PdhQuery.cs（PDH 打开失败只 Warn，不再抛异常拖垮启动） |
| [x] | T-1 | 文本/布局 | src/Ui/CardLayoutCoordinator.cs:99-103、:154-164、:193-197 | 右对齐卡片松手后整叠横向回跳：EndDrag 用 slot 的旧坐标副本算右边缘 | src/Ui/CardLayoutCoordinator.cs（用最近布局宽度 _stackWidth 重算右边缘） |
| [x] | T-2 | 文本/布局 | src/Ui/Screenshots.cs:100-238、:311-314、:522-525、:674-677、:822-825；src/Ui/CommandLineMode.cs:43-76 | 五种截图模式全部吞异常，进程退出码恒为 0 | src/Ui/Screenshots.cs；src/Ui/CommandLineMode.cs（五种截图返回 bool；失败/0 张卡退出码非 0） |
| [x] | T-3 | 文本/布局 | src/Ui/TextCanvas.cs:65-74、:662-670、:796-804 | 自绘文本层三处空 catch，把字体 / 几何 / DPI 失败静默降级 | src/Ui/TextCanvas.cs（字体/几何/DPI 三处 catch 落日志） |
| [x] | X-1 | 跨层 | src/App.xaml.cs:72-111（单实例门）、:133-136（立即模式）；src/Ui/CommandLineMode.cs:22-90 | --selftest / --screenshot* 在已有实例运行时静默"假通过"（exit 0） | src/App.xaml.cs；src/Ui/CommandLineMode.cs（已有实例时立即模式返回 2，不再伪造自检通过） |

## P2（87 项）

| 状态 | 编号 | 模块 | 位置 | 问题 | 改动文件 / 说明 |
| --- | --- | --- | --- | --- | --- |
| [x] | C-7 | 契约/核心 | src/Contracts/Core/Json.cs:345-350 | 绑定只遍历目标字段，map 里多出来的键（拼错、旧版本、未来版本）从不检查；错类型字段也只 catch 后跳过。配置层没有任何"这个键没人认识"的反馈 | src/Contracts/Core/Json.cs（未知/拼错键按“类型.键”警告一次） |
| [x] | C-8 | 契约/核心 | src/Contracts/Core/Json.cs:48,66,82-101 | Serialize 对 Dictionary<string,string>、char、DateTime、Type 等合法 C# 类型产 {} 而不报错 | src/Contracts/Core/Json.cs（空字段类型序列化时 Warn 一次） |
| [x] | C-9 | 契约/核心 | src/Contracts/Core/Log.cs:154 | 写失败冷却窗口内所有日志（含"恢复"记录）被直接丢弃；两个实例同时写同一文件时第二个必然失败。历史项 O-1 仍未修 | src/Contracts/Core/Log.cs（冷却期丢弃计数，恢复后补一条汇总） |
| [x] | C-10 | 契约/核心 | src/Contracts/Core/Settings.cs:42-56 vs :96-119 | 数值 Clamp 全部静默，颜色回退却 Log.Warn，同一份 Normalize 两套可观测性标准 | src/Contracts/Core/Settings.cs（数值夹取越界写 Warn） |
| [x] | C-11 | 契约/核心 | src/Core/ThemeFile.cs:421 | 主题导出用非原子 File.WriteAllText，同层已有 AtomicFile | src/Core/ThemeFile.cs（导出改用 AtomicFile 原子写） |
| [x] | C-12 | 契约/核心 | src/Core/CardStateStore.cs:36-52,163-172 | Read 无大小上限；IsSafeFileName 不拦 CON/NUL 等 Windows 保留设备名 | src/Core/CardStateStore.cs（4MB 读取上限 + Windows 保留设备名） |
| [x] | C-13 | 契约/核心 | src/Contracts/Core/AsyncProcess.cs:34,86-90,109-120 | stdout 固定 UTF-8（中文系统下 cmd 输出乱码）；超时后丢弃已读输出；每次调用留一个未取消的 Task.Delay | src/Contracts/Core/AsyncProcess.cs。stdout 严格 UTF-8 失败回退 Win32 OEM；超时保留部分输出并置 StdOutIncomplete；超时 Delay 可取消 |
| [x] | C-14 | 契约/核心 | src/Contracts/Core/SecretStore.cs:55,81-85 | 无前缀即当明文直通；Protect 失败返回 ""，与"用户清空 Key"不可区分 | src/Contracts/Core/SecretStore.cs；src/SelfTest.cs（Protect 失败返回 null + LastError；自检先判空） |
| [x] | C-20 | 契约/核心 | src/Contracts/Cards/CardModule.cs:21-22、Icons.cs:54-95 | ICardModule.Icon 允许任意字符串，但运行期只认 Icons 登记的 32 个码位，第三方插件用其它合法 Lucide 图标会静默落到正文字体 | src/Cards/CardRegistry.cs（注册时校验 Icon 是否在 Icons 表内） |
| [x] | C-21 | 契约/核心 | src/Core/ConfigStore.cs:18 | SettingsNormalizer 是 public static 可变委托，任何插件都能替换它（能改宿主的配置归一化） | src/Core/ConfigStore.cs（SettingsNormalizer 改 internal） |
| [x] | P-7 | 插件/指标 | src/Contracts/Core/Json.cs:84,345 | 只反射公共字段：第三方插件按常规写属性 DTO，序列化得 {}、反序列化不回填、全程无日志。历史项 B-4 仍未修 | src/Contracts/Core/Json.cs（只有属性没有公共字段的 DTO 绑定前 Warn） |
| [x] | C-15 | 契约/核心 |  | src/SelfTest.cs（RunUnitChecks/ReportUnitChecks）— 24 个领域结果被压成 0/1，与"退出码 = 失败数"的约定不符；热键段、设备枚举段失败根本不计数 | src/SelfTest.cs。按 30 个领域累加失败数并输出 FAIL 行；热键注册失败与设备枚举异常计入退出码 |
| [x] | C-16 | 契约/核心 | src/SelfTest.cs:313-342 | 自检每次在用户 cards/ 目录下留一个空目录（同文件另一段 :1400 有 finally 清理，这段没有） | src/SelfTest.cs（自检 finally 清理 cards 目录） |
| [x] | C-17 | 契约/核心 | src/SelfTest.cs:79,131-141,160-170,1814-1826 | 自检读写真实 settings.json、装全局键盘钩子并 SendInput 注入 F13、注册用户真实热键、需要真实 Dispatcher | src/Core/ConfigStore.cs；src/SelfTest.cs；src/Ui/CommandLineMode.cs（--selftest --isolated：临时配置、跳过输入注入/热键） |
| [x] | G-6 | 玻璃/渲染 | src/Ui/GlassRuntime.cs:286、GlassCapture.cs:137,246,270 | 兼容取样每帧在 UI 线程枚举全表窗口、逐候选开进程句柄（历史 P-3 未修），每卡一个定时器放大 N 倍 | src/Ui/GlassCapture.cs；src/Ui/GlassRuntime.cs。窗口候选快照 120ms TTL + pid→进程名 5s 缓存；FindLayers 移入 Task.Run，Grab 仍每卡并发避免 G-5 串扰 |
| [~] | G-13 | 玻璃/渲染 | src/Ui/OverlayWindow.xaml.cs:163、GlassBackdrop.cs:356 | 每张卡一个 2.5 s 置顶定时器，每次置顶都无条件重摆底板（历史 P-5 未修） | src/Ui/OverlayController.cs；src/Ui/OverlayWindow.xaml.cs；src/Ui/GlassBackdrop.cs。全局单一 2.5s 置顶调度；仅 Z 序变化时 SetWindowPos/重摆底板；需多显示器+其他置顶窗口真机验证 |
| [x] | S-10 | 设置/托盘 | src/Ui/SettingsWindow.Theme.cs:261-289 | 主题预览在每次 UiChanged 全量离屏渲染，保存/关闭也触发（历史 P-7 部分未修） | src/Ui/SettingsWindow.Theme.cs；src/Ui/SettingsWindow.xaml.cs。主题预览 160ms 防抖；读取控件期间跳过；窗口隐藏停表 |
| [x] | S-11 | 设置/托盘 | src/Ui/SettingsWindow.xaml.cs:335-350、Load.cs:436-446 | 关闭窗口的"脏检查"会 CommitPendingEdits（API Key 加密落盘）+ 原地 Normalize；用户选"放弃"也不撤销 | src/Ui/SettingsWindow.xaml.cs；src/Ui/SettingsWindow.Load.cs；src/Ui/SettingsWindow.Appearance.cs；src/Ui/CardEditor.cs；src/Contracts/Cards/CardHostServices.cs；src/Plugins/Chat/ChatCardSettingsView.xaml.cs。判脏改走副本 + 新增只读 ICardEditorPendingEdits；放弃时从 live 设置重载；读控件不再加密/Normalize 活配置 |
| [~] | P-11 | 插件/指标 | src/Plugins/DropStation/StationStore.cs:70-76 | 加载时逐条同步 File.Exists，网络路径/离线盘会卡 UI（历史 B-14 未修） | src/Plugins/DropStation/StationStore.cs；src/Plugins/DropStation/StationView.cs。Load 不再同步 IO；4 后台 worker 有界并发探针、代次丢弃、IO 失败记 Unknown 不判丢失；需断开的网络盘真机验证 |
| [x] | P-12 | 插件/指标 | src/Metrics/MetricEngine.cs:69-99 | Start() 在 UI 线程 Thread.Sleep(250) 预热（历史 P-6 未修） | src/Metrics/MetricEngine.cs（预热 Sleep 移入采样线程） |
| [~] | P-17 | 插件/指标 | src/Plugins/DropStation/StationView.cs:378-407 | 每次 Rebuild 全量重建行并同步取图标，500 条上限直接打满 UI 线程 | src/Plugins/DropStation/StationView.cs；src/Plugins/DropStation/StationRow.cs。行容器复用 + 有序子序列差分；图标异步 4 并发、缓存含失败负缓存；需大目录/渲染真机验证 |
| [~] | T-4 | 文本/布局 | src/Ui/Contour/ContourDecoration.cs:92-97、ContourGeometryBuilder.cs:109-115 | 等高线 30 fps × 每卡 × 每级全网格从零重建，相位闸门实际失效，常态占一核 | src/Ui/Contour/ContourDecoration.cs；src/Ui/Contour/ContourGeometryBuilder.cs。相位量化 + 降频 + 复用上一帧几何；需性能采样确认常态 CPU 下降且视觉不变 |
| [~] | T-5 | 文本/布局 | src/Ui/ParticleTextLayer.cs:332-361 | 粒子每轮播放都在 UI 线程整块 RenderTargetBitmap + 全尺寸字节数组 + 上万候选点 | src/Ui/ParticleTextLayer.cs。复用 RTB/字节缓冲、候选点与像素预算；需高粒子数真机验证 |
| [x] | K-12 | UiKit/媒体 | src/UiKit/Media/CardMediaView.cs:400,428,449、GifAnimator.cs:55 | 图片/GIF 全部在 UI 线程同步解码，GIF 还被解两遍，无像素预算 | src/UiKit/Media/CardMediaView.cs；src/UiKit/Media/GifAnimator.cs。后台解码 + 代次/路径校验；GIF 只解一次、计时器懒建；32MP/64MB 预算降采样，过小才拒绝并 Warn |
| [x] | K-15 | UiKit/媒体 | src/UiKit/Media/Decoders.cs:263,320 | FindFfmpeg 首次调用在 UI 线程同步扫 PATH，断开的网络盘可冻结数秒 | src/UiKit/Media/Decoders.cs；src/UiKit/Media/CardMediaView.cs。FindFfmpeg 不再同步扫 PATH；后台查找 + 3s 等待 + 候选目录优先；卡片显示查找态并自动重试 |
| [~] | G-7 | 玻璃/渲染 | src/Ui/OverlaySurface.cs:629 | 背景图同路径不重载；加载失败后永久缓存失败状态（历史 L-20 未修） | src/Ui/OverlaySurface.cs（背景图缓存加时间戳/长度键，失败 30s 重试。需改图验证） |
| [~] | G-8 | 玻璃/渲染 | src/Ui/DesktopBackground.cs:147 | 壁纸只按路径缓存，同路径换图/主题切换不更新；WallpaperStyle 语义不全（历史 S13 未修） | src/Ui/DesktopBackground.cs（壁纸缓存加时间戳/长度键。需换壁纸验证） |
| [x] | G-9 | 玻璃/渲染 | src/Ui/GlassFeed.cs:139 | Detach 不释放 DesktopFrameSource，玻璃关掉后 D3D 设备与桌面复制对象一直活着 | src/Ui/GlassFeed.cs（最后一张卡 detach 时释放 DXGI 源） |
| [~] | G-10 | 玻璃/渲染 | src/Ui/GlassFeed.cs:213,365,707 | 副屏/越界矩形下每 5 秒重建 D3D 设备并重复刷日志；拖动补帧仍按"快速后端"算间隔（历史 L-2 后半） | src/Ui/DesktopFrameSource.cs；src/Ui/GlassFeed.cs。越界保鲜 idle、按实际后端选补帧间隔、重建失败原因变化才记录；需多显示器/负坐标真机验证 |
| [~] | G-11 | 玻璃/渲染 | src/Ui/GlassFeed.cs:438,812-821 | 全黑保险丝把"内容本来就是黑的"当成"读不到屏幕"，3 轮（约 300 ms）整叠降级并进入 3 s→15 s 振荡 | src/Ui/GlassFeed.cs。熔断改由 GetDC/BitBlt 失败证据驱动，全黑只作日志；30 轮正常才重新武装；需黑屏内容/锁屏真机验证 |
| [x] | G-12 | 玻璃/渲染 | src/Ui/OverlayController.cs:651 | 第二实例唤醒事件仍在 Start() 最后创建，冷启动窗口期双击静默退出（历史 L-5 未修） | src/App.xaml.cs；src/Ui/OverlayController.cs。激活事件在互斥体之后、插件/配置加载之前创建；第二实例 TryOpenExisting 重试 2s |
| [~] | G-14 | 玻璃/渲染 | src/Ui/ScreenGeometry.cs:88 | Overlay.Monitor 用 AllScreens 枚举序号当持久身份，越界时静默钳制到最后一台 | src/Contracts/Core/Settings.cs；src/Ui/ScreenGeometry.cs；src/Cards/CardModel.cs；src/Ui/CardLayoutCoordinator.cs；src/Ui/SettingsApplier.cs；src/Ui/SettingsWindow.Behavior.cs；src/Ui/SettingsWindow.Load.cs；src/Ui/OverlaySurface.cs；src/Ui/Native.cs。OverlaySettings 追加 MonitorDevice（追加式 JSON 字段）；设备名优先、旧序号迁移、缺失 Warn 回退主屏；需热插拔真机验证 |
| [~] | G-15 | 玻璃/渲染 | src/Ui/GlassCapture.cs:145 | FindLayers 只过滤 IsWindowVisible，不查 DWM cloaked：其他虚拟桌面/挂起 UWP 的内容可能被采样显示 | src/Ui/GlassCapture.cs（跳过 DWM cloaked 窗口。需多虚拟桌面验证） |
| [~] | G-16 | 玻璃/渲染 | src/Ui/OverlaySurface.cs:583 vs GlassFeed.cs:41 / GlassBackdrop.cs:31 | 投影留白三种口径：实际 Margin=14（ambient 默认开），采样与底板按 10 DIP、兼容取样按全窗。默认配置下就会多出 4 DIP 灰边/采样错位 | src/Ui/GlassFeed.cs；src/Ui/GlassBackdrop.cs（统一投影留白为 14 DIP。需目视确认无灰边） |
| [x] | P-5 | 插件/指标 | src/App.xaml.cs:209-229、src/Ui/CardSlot.cs:15 | 退出时 _plugins=null 但 _controller 仍持有 _cards[].Module，卸载探针必然报"模块 6/6 未回收"（历史 L-6，比 2.26.0 更糟） | src/App.xaml.cs（Dispose 后置空 _controller，探针不再误报 6/6） |
| [x] | P-6 | 插件/指标 | src/Contracts/Core/Settings.cs:940-968 | Extras 存插件类型实例（钉住 ALC），ExtrasAs 是"读即写"（历史 M-3 未修） | src/Cards/CardPluginLoader.cs；DLL-CARD-PLUGIN-GUIDE.md（探针按“上下文预期保留”报告；指南写明已知限制） |
| [x] | P-8 | 插件/指标 | src/Cards/CardPluginLoader.cs:568-578 | Discard 跳过 ICardModuleLifetime，而全仓零实现者（历史 B-8 未修） | src/Cards/CardPluginLoader.cs（Discard 补 ICardModuleLifetime 回调） |
| [x] | P-9 | 插件/指标 | src/App.xaml.cs:123-129 | 插件加载失败只进日志，用户在设置页看不到"你装的插件没加载" | src/Cards/CardPluginLoader.cs；src/Ui/SettingsWindow.xaml；src/Ui/SettingsWindow.Load.cs（设置页顶部插件失败横幅） |
| [x] | P-10 | 插件/指标 | src/Plugins/Chat/ChatCardSettingsView.xaml.cs:159-176 | 设置页提示用 StartsWith("http")，运行时闸门是 IsSafeEndpoint（历史 B-9 未修） | src/Plugins/Chat/ChatCardSettingsView.xaml.cs（复用 ChatView.IsSafeEndpoint） |
| [x] | P-13 | 插件/指标 | src/Metrics/MetricEngine.cs:132-136 | 采样循环异常不节流，持续坏掉时每 100–500 ms 一条 Log.Error（同文件 :149 已有 LogThrottle） | src/Metrics/MetricEngine.cs（采样循环异常走 LogThrottle） |
| [~] | P-14 | 插件/指标 | src/Plugins/DropStation/StationCopyController.cs:164-196 | 收纳目录时符号链接/联接点被静默跳过，Move 仍递归删源 → "收纳成功"但内容不全 | src/Plugins/DropStation/StationCopyController.cs。链接跳过改为逐条记账；Move 计划含跳过项时拒绝递归删源；完成状态改 Warning 并说明跳过/保留源；需 junction 真机验证 |
| [x] | P-15 | 插件/指标 |  | src/Plugins/DropStation/StationFileActions.cs:289-300,317-339（待实测）— IsInside/UniqueTarget 只做字符串归一化，不解析 reparse point，junction 可让复制越出接收目录 | src/Plugins/DropStation/StationFileActions.cs；src/Plugins/DropStation/StationCopyController.cs（IsInside 检查父链 reparse；目录收纳遇到链接直接拒绝） |
| [x] | P-16 | 插件/指标 | src/Plugins/Command/CommandRunner.cs:137-164 | SplitCommandLine 不识别未加引号的含空格 exe 路径，会静默跑成另一个程序或直接失败 | src/Plugins/Command/CommandRunner.cs（含空格 exe 路径按最长存在前缀拆分） |
| [~] | P-18 | 插件/指标 | src/Metrics/NvidiaSmi.cs:117-134,86-90 | 多卡只取第一张；超时分支不走节流 | src/Metrics/NvidiaSmi.cs；src/Metrics/MetricEngine.cs（按 GpuIndex 选 nvidia-smi 行，越界回落。需多卡机器验证） |
| [x] | P-19 | 插件/指标 | src/Plugins/DropStation/StationFileActions.cs:242-251 | 大小/速度格式化用当前区域性，非英文区域显示 1,5 MB | src/Plugins/DropStation/StationFileActions.cs（大小格式化改 InvariantCulture） |
| [x] | S-05 | 设置/托盘 | src/Ui/SettingsWindow.Load.cs:126-131,509-517、SettingsDeviceCatalog.cs:30-57 | 设备枚举失败时下拉框回退到第一项，保存会静默改写 GPU/网卡/刷新率选择 | src/Ui/SettingsDeviceCatalog.cs；src/Ui/SettingsWindow.Load.cs（枚举失败标记 + LoadUi 记录原值、ReadUi 保留） |
| [x] | S-06 | 设置/托盘 | src/Ui/AutoStart.cs:49-81 | 写注册表失败只进日志，设置窗仍报"已保存并应用" | src/Ui/AutoStart.cs；src/Ui/SettingsApplier.cs；src/Ui/SettingsWindow.xaml.cs；src/Ui/SettingsWindow.Save.cs；src/Ui/OverlayController.cs。AutoStart.Set 返回失败原因；Apply 记录 LastApplyWarning，状态行显示“已保存并应用，但开机自启…失败”并 Log.Warn |
| [x] | S-07 | 设置/托盘 | src/Ui/AutoStart.cs:49,69-72 | --autostart 忽略"启动后最小化"，UI 仍展示为可用；Set(bool,bool) 第二参是死参 | src/Ui/AutoStart.cs；src/Ui/OverlayController.cs（自启命令带 --minimized，启动逻辑尊重 StartMinimized） |
| [x] | S-08 | 设置/托盘 | src/Ui/SettingsWindow.xaml:368-376 | "预设"下拉框写死 4 项，与插件 Presets 靠索引硬对齐，越界静默 return | src/Ui/CardEditor.cs；src/Ui/SettingsWindow.xaml（预设下拉改绑模块 Presets） |
| [x] | S-09 | 设置/托盘 | src/Ui/HotkeyManager.cs:227-253 | 允许注册无修饰键的全局热键，单按 F5 就吃掉全系统 F5 | src/Ui/HotkeyManager.cs（无修饰键仅允许 F1–F24） |
| [~] | S-12 | 设置/托盘 | src/Ui/TrayIcon.cs:314-320 | Explorer 重启后无条件重加托盘图标，忽略用户已关闭托盘 | src/Ui/TrayIcon.cs（记录期望状态，Explorer 重启按期望重加。需重启任务栏验证） |
| [~] | K-4 | UiKit/媒体 | src/UiKit/Media/WaveOutPlayer.cs:194,211,312 | 历史 L-11 只修一半：4 槽全失败才返回 false，1–3 槽失败仍返回 true，坏槽首次写入即终止整条音轨 | src/UiKit/Media/WaveOutPlayer.cs（任一槽 prepare 失败即返回 false。需音频设备验证） |
| [x] | K-5 | UiKit/媒体 | src/UiKit/Media/WaveOutPlayer.cs:374 | LastErrorName 是 static volatile string，多张媒体卡并发启动时错误信息互相覆盖 | src/UiKit/Media/WaveOutPlayer.cs；src/UiKit/Media/FfmpegPlayer.cs（LastErrorName 改实例字段） |
| [~] | K-6 | UiKit/媒体 | src/UiKit/Media/WaveOutPlayer.cs:326,337,518 | 运行期音频死亡（热拔/写失败）不上报 UI；EOF 回收还打印"自然播放结束"，与真实原因相反 | src/UiKit/Media/WaveOutPlayer.cs；src/UiKit/Media/FfmpegPlayer.cs；src/UiKit/Media/CardMediaView.cs。新增 ExitReason/AudioFailed，非正常 EOF 记 Warn 并上报卡片；需音频设备热拔真机验证 |
| [x] | K-7 | UiKit/媒体 | src/UiKit/Media/FfmpegPlayer.cs:510,106、CardMediaView.cs:1027 | 非循环视频自然播完后状态不收尾：IsRunning 恒 true、播放/暂停键失效、Completed 无人订阅 | src/UiKit/Media/FfmpegPlayer.cs；src/UiKit/Media/CardMediaView.cs。EOF 状态机收尾（_ended 与 _stop 分离、Completed、Position=Duration、重播从头）；卡片订阅并同步按钮 |
| [~] | K-8 | UiKit/媒体 | src/UiKit/Media/FfmpegPlayer.cs:682,702 | 暂停状态下去掉静音会立刻播放音频，视频仍冻结 | src/UiKit/Media/FfmpegPlayer.cs（暂停态取消静音后立即 PauseAudio。需播放验证） |
| [~] | K-9 | UiKit/媒体 | src/UiKit/Media/VideoWindow.cs:123,150 | 视频子窗每次位置变化都重建圆角 region 并强制整窗重绘（历史 P-9 未修） | src/UiKit/Media/VideoWindow.cs。region 仅在尺寸/圆角变化时重建，位置变化只移动窗口、bRedraw=false；需拖动/贴边真机验证 |
| [~] | K-10 | UiKit/媒体 | src/UiKit/Media/CardMediaView.cs:284,361,811 | 系统解码路径忽略媒体不透明度：_host.Opacity 管不到独立视频子窗口 | src/UiKit/Media/MediaNative.cs；src/UiKit/Media/VideoWindow.cs；src/UiKit/Media/CardMediaView.cs。分层窗口 SetLayeredWindowAttributes 应用不透明度；ffmpeg 路径改逐帧 WPF 合成，无 ffmpeg 降级静态封面并提示；需 GPU/RDP 真机验证 |
| [x] | K-11 | UiKit/媒体 | src/UiKit/Media/CardMediaView.cs:282,338,738 | L-4 的修复依赖 previous 与 _settings 不是同一实例，而 ExtrasAs<T> 恰恰会返回同一实例 → 差量失效，静音/速度/循环/自动播放可能不生效 | src/UiKit/Media/CardMediaView.cs。删除 previous 引用差量，改为 _applied* 值快照；解码器切换同用快照 |
| [~] | K-13 | UiKit/媒体 | src/UiKit/Media/CardMediaView.cs:316 | 文件在播放中被删除/替换没有感知，也没有可见降级 | src/UiKit/Media/CardMediaView.cs（每 2 秒探测源文件，丢失即停播并提示。需播放中删文件验证） |
| [x] | K-14 | UiKit/媒体 | src/Contracts/Core/AsyncProcess.cs:136、FfmpegPlayer.cs:987 | stdout 无上限；ExtractImage 对超大图片/坏文件是无界内存（解压炸弹） | src/Contracts/Core/AsyncProcess.cs（stdout 8MB 上限并继续读空，置 StdOutTruncated） |
| [~] | K-16 | UiKit/媒体 | src/UiKit/Media/CardMediaView.cs:613、FfmpegPlayer.cs:523 | 逐帧 BitmapSource.Create + 每帧一次 Dispatcher.BeginInvoke，高帧率视频造成 GC 与 Dispatcher 队列压力 | src/UiKit/Media/FfmpegPlayer.cs；src/UiKit/Media/CardMediaView.cs。3 块帧池 + 单槽最新帧 + Interlocked 合并通知，-r 封顶 30fps；复用 WriteableBitmap；需 4K 高帧率真机验证 |
| [~] | T-6 | 文本/布局 | src/Ui/ParticleTextLayer.cs:196,427-471 | 动画中卡片尺寸变化时，重采样用的是"上一句"的变化字符集，会隐藏错字符 | src/Ui/ParticleTextLayer.cs。尺寸变化后按当前句/尺寸重建字符差集；需动画中缩放真机验证 |
| [~] | T-7 | 文本/布局 | src/Ui/TextCanvas.cs:480-487,608-628 | TextCanvas 永不换行、永不省略，单行超长内容把卡片撑到屏外 | src/Ui/TextCanvas.cs（加 MaxTextWidth，超宽裁剪并画省略号。需目视验证） |
| [x] | T-8 | 文本/布局 | src/Ui/TextCanvas.cs:994-1006,883-908 | 逐字隐藏与光标定位按 UTF-16 code unit 切分，代理对/组合字符会被切半 | src/Ui/TextCanvas.cs。隐藏/光标/删除按 grapheme cluster（StringInfo）处理 |
| [~] | T-9 | 文本/布局 | src/Ui/CardLayoutCoordinator.cs:193-208 | 卡片堆叠不夹取到工作区，卡片多了会排到屏幕外且无法拖回 | src/Ui/CardLayoutCoordinator.cs。堆叠钳制到工作区，放不下时压缩间距/换列；需多屏+任务栏真机验证 |
| [~] | T-10 | 文本/布局 | src/Ui/CardLayoutCoordinator.cs:213-229,65-72 | 高度变化走无动画布局，并清掉拖动后定下的 _alignOverride | src/Ui/CardLayoutCoordinator.cs。高度变化动画过渡；保留拖动后 _alignOverride；需视觉验证 |
| [~] | T-11 | 文本/布局 |  | src/Ui/Screenshots.cs:225-230（待实测）— 截图 2× RTB 与自绘文本的 PixelsPerDip 不一致，字形与屏上不同 | src/Ui/Screenshots.cs。截图与自绘文本统一 PixelsPerDip；需截图目视对比 |
| [x] | T-12 | 文本/布局 | src/Ui/Contour/ScalarGrid.cs:29-84 | 等高线是纯计算，但生产路径绕开了可测 API（ScalarGrid.Build/Chain），自检零断言，两套网格尺寸推导已漂移 | src/Ui/Contour/ScalarGrid.cs。网格尺寸推导收敛到单入口，生产路径调用同一 API；建议的 SelfTest 断言见子代理报告 |
| [x] | T-13 | 文本/布局 | src/Ui/Contour/ContourExtractor.cs:526-583 | Chaikin 按 2^n 倍点数分配，点数组池只按"个数"限制 → LOH 常驻 | src/Ui/Contour/ContourExtractor.cs。Chaikin 点数/字节双预算 + 池复用，超限降级并 Warn |
| [x] | K-23 | UiKit/媒体 | src/UiKit/CardDataControls.cs:269,297,341 | CardMetricTile.Delta 赋值会吃掉 Trend 箭头，SetValue 每帧刷新时箭头消失 | src/UiKit/CardDataControls.cs（Delta setter 重算趋势标记） |
| [~] | K-24 | UiKit/媒体 | src/UiKit/CardDataControls.cs:83,161 | CardBadge/CardTag 的 Tone setter 把算出的 CornerRadius 反馈回 ApplyStyle，圆角逐次漂移 | src/UiKit/CardDataControls.cs（Tone 用原始圆角，不再回路计算值。需切主题目视） |
| [~] | K-25 | UiKit/媒体 |  | src/UiKit/**（0 命中）— 整个控件库 0 个 AutomationProperties/AutomationPeer，自绘控件对读屏/自动化完全不可见 | src/UiKit/CardDataControls.cs（五个控件补 AutomationPeer + AutomationProperties.Name。需读屏实测） |
| [-] | K-26 | UiKit/媒体 | src/UiKit/CardDataControls.cs:386 等 | 控件没有 DependencyProperty：Value/Text/Tone 全是普通 CLR 属性，不能绑定、无 AffectsRender 语义，主题只能手工 ApplyStyle | —（按 ABI 决策暂不做 DependencyProperty，保留 CLR 属性） |
| [x] | K-27 | UiKit/媒体 | src/UiKit/CardOverlayControls.cs:1032 | 静态 CardToast.Show 用 new StyleSettings() 套样式，永远拿不到宿主主题 | src/UiKit/CardOverlayControls.cs（新增带 StyleSettings 的 Toast 重载） |
| [~] | K-28 | UiKit/媒体 | src/UiKit/CardOverlayControls.cs:113,193 | CardDialog 没有焦点陷阱，Tab 可把焦点移到遮罩后的控件 | src/UiKit/CardOverlayControls.cs。TabNavigation=Cycle + 显式 Tab/Shift+Tab 循环；关闭恢复原焦点；需含 RichTextBox/ComboBox 真机验证 |
| [x] | C-18 | 契约/核心 |  | src/Contracts/Core/LucideIcons.cs（6,927 行）— 2,293 项字典 + 2,293 项数组是两份手工重复清单，而 Get/TryGet/Names 在生产代码中**零调用**；静态构造在首次绘制时于 UI 线程构建 | src/Contracts/Core/LucideIcons.cs；src/UiKit/CardIconSelfTest.cs（查询 API 标 [Obsolete]，自检局部 pragma） |
| [x] | C-19 | 契约/核心 | src/Contracts/Core/Keycaps.cs:16-21 | IsKeycap 是死代码，且其"按区间判断"与 Icons 里"不能按区间判断"的结论正面矛盾 | src/Contracts/Core/Keycaps.cs（IsKeycap 标 [Obsolete]） |
| [x] | X-2 | 跨层 | src/App.xaml.cs:55-63 | UI 异常处理里 args.Handled = false; Shutdown(1); 自相矛盾：Handled=false 会让 CLR 继续终止进程（并可能触发 Windows Error Reporting），Shutdown(1) 的退出码不会生效 | src/App.xaml.cs（Handled=true 接管退出，退出码可控） |
| [x] | X-3 | 跨层 | src/Plugins/DropStation/RecycleBin.cs:58 | 源码里内嵌了一个裸 NUL 字节（$"{path}<0x00>"），功能上等价于 "\0"，但 git 把整个文件判为二进制（实测 Bin 6131 -> 6132），今后任何修改都不进 diff，grep/file 也会跳过 | src/Plugins/DropStation/RecycleBin.cs（顺带修复：裸 NUL 改为 \0 转义，git 恢复文本 diff） |
| [x] | X-4 | 跨层 |  | 版本号在 **15 处**手工同步（根/Contracts/UiKit/6 个插件 csproj + 6 个 manifest），另有 README 与版本历史引用 | Directory.Build.props；tools/write-manifests.ps1；各 csproj（版本单一源，manifest 构建时生成） |
| [x] | X-5 | 跨层 | publish.bat:20-96 | 发布前 rd /s /q bin obj，把用户 settings.json/日志/插件暂存到 %TEMP% 再恢复；中途失败/断电时用户数据只在 TEMP，应用运行时删除会静默失败（2>nul）导致后续发布混架构 | publish.bat。发布前只清构建产物，不再搬用户数据到 TEMP；检测运行中实例并明确报错 |
| [x] | X-6 | 跨层 |  | 仓库无 .github/、无 TreatWarningsAsErrors、无 global.json；.editorconfig 的 IDE 规则默认不进构建（EnforceCodeStyleInBuild 未开）。"0 警告"是人工纪律而不是门禁 | global.json；Directory.Build.props；.editorconfig；.github/workflows/build.yml。SDK 固定 8.0.4xx；TreatWarningsAsErrors+EnforceCodeStyleInBuild+AnalysisLevel=latest；IDE0090 显式降级；CI 构建+manifest 校验 |
| [x] | X-7 | 跨层 |  | 没有测试工程，唯一自动验证是 1,855 行自检；它不可隔离、不可并行、不可 CI、不可 mock（C-15/C-16/C-17、T-12） | tests/**；MirrorpondHUD.csproj（xUnit 测试工程 + 进程内 runner；12 个测试通过） |
| [x] | X-8 | 跨层 | src/Contracts/Core/Log.cs:148-199 | 日志在全局锁内同步 File.AppendAllText + 轮转 File.Move，任何线程（含 UI/渲染）打日志都要等磁盘；调用点为 Debug 6 / Info 111 / Warn 276 / Error 62 | src/Contracts/Core/Log.cs；src/App.xaml.cs。有界队列+单写线程；队列溢出/冷却分开计数；OnExit/ProcessExit Flush(2s) |
| [x] | X-9 | 跨层 |  | 全仓 **138 处** catch { }（媒体 44、Ui 52、插件 14、Core 13、Cards 4、Metrics 4、SelfTest 3、UiKit 控件 2…），与 AGENTS §5 的绝对禁令直接冲突；其中 Dispose 路径的空 catch 是**正确**的，TextCanvas/WaveOutPlayer/FfmpegPlayer 的才是危险的 | src/Contracts/Core/SafeRelease.cs；tools/check-empty-catches.ps1；tools/empty-catch-baseline.txt；.github/workflows/build.yml；src/Cards/CardPluginLoader.cs；src/Ui/ShellIcons.cs；src/Ui/TrayIcon.cs；src/Plugins/Command/CommandRunner.cs；src/Plugins/Chat/ChatCard.cs；src/Metrics/MetricEngine.cs；src/Metrics/NvidiaSmi.cs。新增 SafeRelease 统一释放路径；8 处业务路径危险空 catch 改为可见日志；CI 空 catch 棘轮（132 基线，只降不升） |
| [~] | X-10 | 跨层 |  | 主题色有 **4 套来源**（Theme.xaml 37 个 brush、SettingsTheme.Palette 33 对、HudMenuStyle.Light/Dark、卡片 ThemeTokens），改一个颜色要改 2–3 处；且默认强调色已分叉（卡片 #2F6FEB 蓝 vs 设置窗 #F2E900 黄）。当前调色板全部用 DynamicResource，替换 brush 实例能工作，但新增 StaticResource 会立刻踩坑 | src/Ui/SettingsTheme.cs；src/UiKit/HudMenuStyle.cs；src/SelfTest.cs（改画刷 Color、菜单复用 Theme.xaml、自检键集合。需明暗切换目视） |

## P3（62 项）

| 状态 | 编号 | 模块 | 位置 | 问题 | 改动文件 / 说明 |
| --- | --- | --- | --- | --- | --- |
| [x] | C-22 | 契约/核心 | src/Core/ThemeCatalog.cs:23-66,51 | Load 每次全量重解析，Id 直接用品名（建议：加缓存；Id 用稳定文件标识） | src/Core/ThemeCatalog.cs；MirrorpondHUD.csproj；src/Ui/SettingsWindow.Theme.cs。内置主题解析结果缓存；Id 改用稳定文件标识（LogicalName=themes/<文件名>），主题选择同时按 Name/Id 兼容旧配置 |
| [x] | C-23 | 契约/核心 | src/Contracts/Core/Log.cs:148-199,202-223 | 日志在调用线程同步落盘（含轮转 File.Move）（建议：见 §5.2 异步化） | src/Contracts/Core/Log.cs；src/App.xaml.cs。已由 X-8 解决：有界队列 + 单写线程，退出 Flush(2s)，不再在调用线程同步落盘 |
| [x] | C-24 | 契约/核心 | src/Core/SettingsLoader.cs:97-111 | v6→v7 迁移静默丢弃另一套模糊参数（建议：迁移时记一条 Info 汇总丢弃字段） | src/Core/SettingsLoader.cs。v6→v7 合并模糊参数时记录选中的 BlurRadius/BlurFps 与被丢弃的旧键值 |
| [x] | C-25 | 契约/核心 | src/Contracts/Core/Settings.cs:469-483 | NormalizeDrives 给非盘符路径补反斜杠；盘符大小写不同会重复（建议：只接受 X:\ 形态；统一大写） | src/Contracts/Core/Settings.cs。盘符统一大写、补尾反斜杠并按 OrdinalIgnoreCase 去重；UNC 保留末尾规整 |
| [x] | C-26 | 契约/核心 | src/Core/ConfigStore.cs:395-396 | .broken-* 同秒内多次失败互相覆盖（建议：加毫秒或序号后缀） | src/Core/ConfigStore.cs。.broken-* 命名加毫秒并对同秒冲突追加序号，不再互相覆盖 |
| [x] | C-27 | 契约/核心 | src/Core/ThemeFile.cs:766-773 | effect 接受 Contour 但错误文案只列 4 种；导出时非法软色静默丢弃（建议：文案与校验表同源） | src/Contracts/Core/Settings.cs；src/Core/ThemeFile.cs。循环类型校验文案由 LoopEffectNames 单一清单生成（含 Contour）；导出非法软色 Log.Warn 后跳过 |
| [x] | C-28 | 契约/核心 | src/Contracts/Text/Variables.cs:59-64 | NaN 直接交给模板，显示 "NaN"（建议：格式化前判 NaN/Inf 并回落占位符） | src/Contracts/Text/TemplateEngine.cs。模板值 NaN/∞ 回落 n/a，并用 LogThrottle 节流记录原因 |
| [x] | C-29 | 契约/核心 | src/Contracts/Core/Settings.cs:88 | 按 char 截断文本，可能切断代理对（建议：按 text element 截断） | src/Contracts/Core/Settings.cs。WelcomeText 按 StringInfo text element 截断，不再切断代理对/组合字符 |
| [x] | S-13 | 设置/托盘 | src/Ui/HotkeyManager.cs:78,158-172 | 热键 id 单调增长不复用，逼近 0xBFFF 后注册必失败且日志归因"被占用"（建议：空闲 id 回收池） | src/Ui/HotkeyManager.cs。空闲热键 id 回收池；日志区分无可用 id 与组合键被占用 |
| [x] | S-14 | 设置/托盘 | src/Ui/SettingsWindow.xaml.cs:507-538 | "关闭"仍是 Hide，模块区块/预览位图/两份深拷贝一直被持有（建议：关闭时释放区块、重新加载时重建） | src/Ui/SettingsWindow.xaml.cs；src/Ui/SettingsWindow.Load.cs。关闭（Hide）时释放模块区块/预览位图/可重建缓存，重开 Reload 重建 |
| [x] | S-15 | 设置/托盘 | src/Ui/ShellIcons.cs:231-235 | IShellItemImageFactory.GetImage 丢 HRESULT，失败与"无缩略图"不可区分（建议：检查 HRESULT 并节流日志） | src/Ui/ShellIcons.cs。检查 IShellItemImageFactory.GetImage HRESULT，失败 LogThrottle；无缩略图不刷日志 |
| [x] | S-16 | 设置/托盘 | src/Ui/TrayIcon.cs:351-372 | 托盘回退图标是共享 SystemIcons.Application，Dispose 会释放它（建议：Icon.FromHandle 后克隆，或持有独立副本） | src/Ui/TrayIcon.cs。回退图标改自有副本，不再释放共享 SystemIcons.Application |
| [x] | S-17 | 设置/托盘 | src/Ui/TrayIcon.cs:334-349 | 气泡失败路径全空 catch/忽略返回值，固定 256 字符截断（建议：检查返回码；截断显式加省略号） | src/Ui/TrayIcon.cs。气泡失败检查返回码并 Warn；超长文本截断加省略号 |
| [~] | S-18 | 设置/托盘 | src/Ui/SettingsWindow.Appearance.cs:161-192 | 字体列表首次在 UI 线程全量枚举；筛选框每键入换 ItemsSource（建议：启动预热 + 复用集合） | src/Ui/SettingsWindow.Appearance.cs。字体列表后台预热、筛选复用集合，避免每键换 ItemsSource；需真机字体环境验证 |
| [x] | S-19 | 设置/托盘 | src/Ui/SettingsWindow.Appearance.cs:76-101 | ApplyCliBoxes 非法输入静默回退；任一框失焦重写四个框（建议：逐框校验 + 只写变更框） | src/Ui/SettingsWindow.Appearance.cs。CLI 逐框校验，只回写变化的框，不再整组重写 |
| [x] | S-20 | 设置/托盘 | src/Ui/CommandLineMode.cs:57-62 | --screenshot-settings 的"标题"实为输出路径，未校验（建议：解析为绝对路径并确认目录可写） | src/Ui/CommandLineMode.cs；src/Ui/Screenshots.cs。截图参数按输出路径校验：绝对路径、.png 扩展、目录可写、目标非目录/只读 |
| [x] | S-21 | 设置/托盘 | src/Ui/HotkeyManager.cs:91-103 | 热键消息回调无异常隔离，OnHotkey 抛错从 WndProc 逸出（待实测）（建议：try/catch + Log.Error） | src/Ui/HotkeyManager.cs。热键消息回调 try/catch + Log.Error，异常不再从 WndProc 逸出 |
| [x] | G-17 | 玻璃/渲染 | src/Ui/GlassFeed.cs:559 | 每帧新建 BitmapSource 并整块拷贝（P-2 半修）（建议：复用 WriteableBitmap + Lock/Unlock） | src/Ui/GlassFeed.cs。复用 WriteableBitmap/帧缓冲，尺寸变化才重建；避免每帧 BitmapSource+整块拷贝 |
| [~] | G-18 | 玻璃/渲染 | src/Ui/OverlayWindow.xaml.cs:284 等 | 本区域 40 处裸 catch，关键 Win32 返回值集体不检查，GetExStyle 的 int 截断（建议：统一 GetExStyle 返回 nint；返回值检查） | src/Ui/OverlayWindow.xaml.cs；src/Ui/Native.cs；src/Ui/GlassBackdrop.cs；src/Ui/GlassBackdrops.cs；src/Ui/DesktopBackground.cs。GetExStyle/SetExStyle 改 nint+返回值检查；关键 Win32 调用检查与节流日志；释放路径走 SafeRelease。需真机验证 Z 序/系统交互 |
| [x] | G-19 | 玻璃/渲染 | src/Ui/OverlayController.cs:831、GlassRuntime.cs:255 | 退出时可能打伪 WARN；隐藏后泵空转、每帧读环境变量（建议：停机判定收口；环境变量缓存） | src/Ui/OverlayController.cs；src/Ui/GlassRuntime.cs。停机判定收口，隐藏时停泵，环境变量缓存一次 |
| [x] | T-14 | 文本/布局 | src/Ui/Contour/ContourExtractor.cs:512-518 | ContourPath.Level/LevelIndex 恒为 0（建议：填真实层级或删字段） | src/Ui/Contour/ContourExtractor.cs。删除恒 0 的 Level/LevelIndex 死字段与写入（全仓零读取） |
| [x] | T-15 | 文本/布局 | src/Ui/Contour/ScalarGrid.cs:26-44 | 网格上限两份（512 vs 140），生产用后者（建议：单一常量） | src/Ui/Contour/ScalarGrid.cs；src/Ui/Contour/ContourGeometryBuilder.cs。单一 MaxCellsPerAxis=140 + MinCellSize，生产/自检共用 ComputeProductionSize |
| [x] | T-16 | 文本/布局 | src/Ui/TextCanvas.cs:55,80-81 | 键帽码位区间注释 83 与实现 87 不一致（建议：注释与常量同源） | src/Ui/TextCanvas.cs；src/Contracts/Core/Icons.cs；src/Contracts/Core/Keycaps.cs。键帽码位区间统一引用 Keycaps.First/Last，删除手写数字与字形个数 |
| [~] | T-17 | 文本/布局 | src/Ui/CardDragAdorner.cs:60-79 | 拖动层硬编码颜色；每帧新建 Pen/Brush/Geometry（建议：用主题令牌并缓存） | src/Ui/CardDragAdorner.cs；src/Ui/SettingsWindow.Cards.cs。拖动层改主题令牌并缓存 Pen/Brush/Geometry；需真机看浅色主题与高 DPI 描边 |
| [x] | T-18 | 文本/布局 | src/Ui/Screenshots.cs:828-837 | PNG 非原子写；设置窗截图按整 DIP 截断（建议：走 AtomicFile；按 DPI 取整） | src/Core/AtomicFile.cs；src/Ui/Screenshots.cs。PNG 走 AtomicFile.WriteBinary 原子写；尺寸改 Math.Ceiling 按 DPI 取整 |
| [x] | T-19 | 文本/布局 | src/Ui/Screenshots.cs:102-120 | 截图模式直接在运行中的 Settings 上改演示参数（待实测污染）（建议：深拷贝一份再改） | src/Ui/Screenshots.cs。截图演示参数改深拷贝 Settings，失败则本次截图失败，不污染实时配置 |
| [x] | T-20 | 文本/布局 | src/Ui/ParticleTextLayer.cs:154-157 | 粒子提前返回路径不回发 Finished（建议：统一收尾事件） | src/Ui/ParticleTextLayer.cs。统一幂等 NotifyFinished，提前返回/取消/异常都回发一次 Finished |
| [~] | T-21 | 文本/布局 | src/Ui/TextCanvas.cs:557,749-750 | 排版缓存上限 12，被光标前缀键反复驱逐（P-1 残余）（建议：光标键不计入缓存或单独缓存） | src/Ui/TextCanvas.cs。光标前缀独立单槽缓存；主缓存改近似 LRU（12→8），不再整表清空；需真机看输入手感 |
| [x] | K-17 | UiKit/媒体 | GifAnimator.cs:28 | "动图过大"提示被换肤覆盖；LastSkipReason 是 static，双卡串提示（建议：改实例字段，状态行优先 Warning） | src/UiKit/Media/GifAnimator.cs；src/UiKit/Media/CardMediaView.cs。跳过原因改实例字段经 out 返回；状态行 Warning 优先，换肤/刷新不覆盖 |
| [x] | K-18 | UiKit/媒体 | GifAnimator.cs:144-150 | GIF 暂停后按播放从头重播（建议：Play(bool restart=false) 默认续播） | src/UiKit/Media/GifAnimator.cs；src/UiKit/Media/CardMediaView.cs。Play(bool restart=false) 默认续播，仅首次加载 restart:true；4 个调用点已更新 |
| [~] | K-19 | UiKit/媒体 | WaveOutPlayer.cs:273,286,407；VideoWindow.cs:67 等 | winmm 返回码被忽略 + catch {}，Unprepare/Close/SetVolume 失败不可见（建议：检查返回码并节流日志） | src/UiKit/Media/MediaNative.cs；src/UiKit/Media/VideoWindow.cs；src/UiKit/Media/WaveOutPlayer.cs；src/UiKit/Media/FfmpegPlayer.cs。TryGetExStyle/SetExStyle 改 bool 并用 LastPInvokeError 区分；GDI/音频释放返回码检查；需热拔/远程桌面真机 |
| [~] | K-20 | UiKit/媒体 | VideoWindow.cs:210-214 | HWND_TOPMOST 把视频窗抬到所有置顶窗口之上（建议：用 owner 句柄作 hWndInsertAfter） | src/UiKit/Media/VideoWindow.cs。视频窗不再全局 Topmost，以 owner/前邻窗为 z-order 锚点跟随卡片；需多置顶窗真机验证 |
| [~] | K-21 | UiKit/媒体 | CardMediaView.cs:805-811 | 跨不同缩放屏幕时子窗位置/圆角偏（待实测）（建议：TransformToDevice 逆变换 + DpiChanged） | src/UiKit/Media/CardMediaView.cs；src/UiKit/Media/VideoWindow.cs。物理像素坐标经 TransformToDevice 计算，处理 DpiChanged 并重建 region；需混合缩放多屏真机 |
| [~] | K-22 | UiKit/媒体 | FfmpegPlayer.cs:484 | -re 回退锁 1×，UI 仍显示用户设置倍速（建议：SetSpeed 返回 bool 并回滚显示） | src/UiKit/Media/FfmpegPlayer.cs；src/UiKit/Media/CardMediaView.cs。SetSpeed 返回 bool，-re 回退时 SpeedForced 上报实际 1×，卡片回滚显示并说明；需旧版 ffmpeg 真机 |
| [x] | K-29 | UiKit/媒体 | 16 处 XamlReader.Parse | 每次换肤重解析模板，颜色串错误时 XamlParseException 直崩 UI 线程（建议：缓存模板 + TryParse 回退） | src/UiKit/CardTemplates.cs；src/UiKit 各控件。XamlReader 模板集中缓存 + TryParse 回退，解析失败 Log.Error 不再直崩 UI 线程 |
| [x] | K-30 | UiKit/媒体 | CardButton.cs:178 等 | 未 Freeze 的 SolidColorBrush（含生产控件 CardTextBox/CardList）（建议：统一走 CardPalette.Brush） | src/UiKit/CardButton.cs；src/UiKit/CardTextBox.cs；src/UiKit/CardList.cs。画刷统一走调色板并 Freeze，避免每实例新建等价画刷 |
| [x] | K-31 | UiKit/媒体 | CardStatus.cs:111,267 | 90 ms 转圈定时器无停止出口（建议：Unloaded 停表） | src/UiKit/CardStatus.cs。90ms 转圈定时器在 Unloaded/隐藏时停止，Dispose 清理 |
| [~] | K-32 | UiKit/媒体 | CardTreeTimeline.cs:276-297 | SelectedItem setter 同步 UpdateLayout()，布局回调重入风险（待实测）（建议：BeginInvoke 延后） | src/UiKit/CardTreeTimeline.cs。SelectedItem 滚动/布局改 BeginInvoke 延后且幂等；需真机验证不再重入 |
| [x] | K-33 | UiKit/媒体 | CliUi.cs:260,332-336 | 换肤丢失 per-block bold；构造对 Style==null 无防护（建议：存 Bold；settings?.Style ?? new） | src/UiKit/CliUi.cs。换肤保留 per-block Bold；Style==null 用兜底实例，不崩 |
| [x] | K-34 | UiKit/媒体 | CardFormControls.cs:166 等 | ToString(ValueFormat) 无校验，非法格式抛 FormatException（建议：复用图表侧的安全格式化） | src/UiKit/CardFormControls.cs；src/UiKit/CardFormat.cs。ValueFormat 走共享安全格式化，非法格式回退默认并 Warn 一次 |
| [x] | K-35 | UiKit/媒体 | CardDataControls.cs:1084,947 | 表格排序混合类型抛 ArgumentException；列重建双 Refresh（待实测）（建议：类型不同按字符串比较） | src/UiKit/CardDataControls.cs。排序混合类型按字符串比较，稳定；列重建只 Refresh 一次 |
| [x] | K-36 | UiKit/媒体 | CardChartControls.cs:353,507,701 | Series/Values/Labels 直接暴露内部 List，改列表不重绘（建议：返回只读集合） | src/UiKit/CardChartControls.cs。Series/Values/Labels 公共面改 IReadOnlyList，变更走显式 Add/Remove/Clear 并重绘 |
| [x] | K-37 | UiKit/媒体 | CardList.cs:166,148 | ApplyStyle(style, cli) 的 cli 从未被读（建议：删参数或真正使用） | src/UiKit/CardList.cs；src/Ui/Screenshots.cs；src/SelfTest.cs；src/Plugins/DropStation/StationView.cs。删除未使用的 cli 参数并更新全部调用点 |
| [x] | K-38 | UiKit/媒体 | CardIcon.cs:39,42 | Size 不同步 FontSize，运行期改大小无效（建议：setter 内同步 + InvalidateMeasure） | src/UiKit/CardIcon.cs。Size setter 同步 FontSize 并 InvalidateMeasure |
| [x] | K-39 | UiKit/媒体 | CardSegmentBar.cs:87-94 | OnRender 里改 _segments，渲染副作用改写公开属性（建议：局部变量 + Arrange 更新） | src/UiKit/CardSegmentBar.cs。分段计算移到 Arrange/属性变更，OnRender 只读局部变量 |
| [x] | K-40 | UiKit/媒体 | CardTextStyle.cs:57-83 | 每次换肤为每个 TextBlock 新建 DropShadowEffect（建议：复用 Effect；跳过空子树） | src/UiKit/CardTextStyle.cs。阴影 Effect 按颜色/半径复用并冻结，跳过空子树 |
| [x] | K-41 | UiKit/媒体 | HudMenuStyle.cs:51,76,83 | 每次 Apply 重复订阅，LayoutUpdated 永不解绑（建议：附加属性防重入；Opened 挂 Closed 解） | src/UiKit/HudMenuStyle.cs。附加属性防重入；Opened 挂/Closed 解，重复 Apply 不叠加 |
| [x] | K-42 | UiKit/媒体 | CardOverlaySelfTest.cs:193 | 自检只验"不崩/尺寸"，不验颜色与对比度；清理只吞 InvalidOperationException（建议：补对比度断言；宽 catch 计入失败） | src/UiKit/CardOverlaySelfTest.cs。补前景/背景对比度与令牌一致断言；清理异常计入失败 |
| [x] | P-20 | 插件/指标 | src/Cards/CardRegistry.cs:78-97 | 空 Kind 仍硬编码 "infotext"；零模块时每次调用新建占位模块（建议：用 UnavailableCardModule 单例） | src/Cards/CardRegistry.cs。FallbackKind 常量 + NoModulesModule 单例，零模块不再每次分配 |
| [x] | P-21 | 插件/指标 | src/Cards/CardRegistry.cs:19-25 | 双检锁的 _initialized 不是 volatile（历史 L-16）（建议：volatile 或 Lazy<T>） | src/Cards/CardRegistry.cs。_initialized 改 volatile，双检锁语义正确 |
| [~] | P-22 | 插件/指标 | 本区域 32 处空 catch | 其中 6 处吞掉了需要留痕的失败（建议：见 §5.2 分类治理） | src/Cards/CardPluginLoader.cs；src/Plugins/Command/CommandRunner.cs；src/Plugins/Command/CommandCard.cs。6 处吞失败空 catch 改为 Warn/收窄；其余合法释放路径保留；需真实 ALC 卸载失败/进程杀不死场景 |
| [x] | P-23 | 插件/指标 | InfoTextCard.cs:105-109 | 用内容字符串相等反推预设，手改内容会静默换预设（建议：记录显式 Preset，内容只做兜底） | src/Plugins/InfoText/InfoTextCard.cs。Extras 新增显式 Preset，旧配置按内容兜底推断一次并写回 |
| [~] | P-24 | 插件/指标 | MediaCardSettingsView.xaml.cs:220-265 | DecoderHint 是带副作用的绑定 getter，每次刷新同步 File.Exists 并可能启动探测（建议：改为显式刷新方法） | src/Plugins/Media/MediaCardSettingsView.xaml.cs。DecoderHint 绑定 getter 改只读缓存；去抖+版本号后台探测；需网络盘真机 |
| [x] | P-25 | 插件/指标 | 四份设置视图 | Raise/commit 复制粘贴，只有 Chat 实现 ICardEditorCommit（建议：抽基类或共享 helper） | src/UiKit/CardEditorNotifier.cs；Chat/Media/DropStation/Command 设置视图。抽出组合式 CardEditorNotifier 统一 Raise/Notify/MarkChanged；Command 数字框接入 pending |
| [x] | P-26 | 插件/指标 | CardPluginLoader.cs:356-362 | 只校验 manifest.apiVersion，不校验插件编译所依据的 Contracts 版本（建议：manifest 加 contractsVersion 并校验） | src/Contracts/Cards/PluginManifest 所在文件；src/Cards/CardPluginLoader.cs；6 份 manifest.json；DLL-CARD-PLUGIN-GUIDE.md。manifest 增 contractsVersion；缺失跳过、解析失败拒绝、高于宿主拒绝、低于 Warn；apiVersion 仍 2 |
| [x] | P-27 | 插件/指标 | CardJobQueue.cs:1-6 | 位置与文档不符；StationCopyController.cs:126 注释与事实相反（建议：修正文档/注释） | src/Plugins/DropStation/CardJobQueue.cs；src/Plugins/DropStation/StationCopyController.cs。修正文件归属/命名空间与首任务执行方式的过时注释，不改位置 |
| [x] | X-11 | 跨层 | 20 个文件 / 117 处 DllImport | Native.cs 的集中声明未被遵守；GetWindowLongPtrW 等在 Native.cs 与 MediaNative.cs 重复声明（建议：单文件 P/Invoke + 可空性/句柄包装） | src/Ui/Native.cs。最小集：确认 GWL_EXSTYLE 32 位语义并补跨程序集 P/Invoke 边界注释；ForceForeground 失败改为 Log.Debug 可见。全量集中到 Native.cs 暂不做 |
| [x] | X-12 | 跨层 | src/Ui/Controls/、src/Text/、6 个 plugin.csproj、9 个 RootNamespace | 空目录残留；六个工程同名导致构建日志不可辨；所有程序集同根命名空间（建议：清空目录；工程按卡片改名；RootNamespace 按程序集区分） | MirrorpondHUD.csproj；src/Plugins/*/MirrorpondHUD.Cards.*.csproj；src/Ui/Controls、src/Text。6 个同名 plugin.csproj 按程序集名重命名并同步根工程引用；删除两个空目录。RootNamespace 暂不动 |
| [x] | X-13 | 跨层 | .gitignore 第 34 行附近 | docs/review/ 被忽略，审阅报告无法入库/评审/在 PR 引用（建议：改为跟踪，或独立仓库 + 链接） | .gitignore；docs/review/*.md。仅忽略 docs/review/.work/；review 正文、清单与历史评估入库 |
| [x] | X-14 | 跨层 | sign.ps1:27 | PFX 口令硬编码（MirrorpondHUDDev!2026），并向 CurrentUser 存储写入自签名证书（建议：参数/环境变量；文档说明信任流程） | sign.ps1；README.md。PFX 口令改读 MIRRORPOND_PFX_PASSWORD 或 -Password；缺失时 Warn 并跳过证书/签名，README 已说明 |
| [-] | X-15 | 跨层 | src/Contracts/MirrorpondHUD.Contracts.csproj:4 | UseWPF=true；契约里出现 Color（19 处）、FrameworkElement（16 处）、Brush/Pen/Size/CornerRadius（建议：拆出不依赖 WPF 的纯数据/纯逻辑契约） | 。决策：暂不拆 WPF-free 契约程序集，避免插件 ABI/引用关系大改；在 Contracts 继续禁止新增 UI 类型，后续单独评估 |
| [x] | X-16 | 跨层 | src/App.xaml.cs:76-104 | 第二实例在 UI 线程 Thread.Sleep(100) × 30（最多 3 s）等唤醒事件（建议：短超时 + 异步/后台等待，或先建事件再建互斥） | src/App.xaml.cs。唤醒事件提前到取互斥体之前创建；第二实例改为 4×25ms 有界尝试，不再阻塞 UI 线程 2 秒 |

## 验证记录

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build -c Release` | **0 警告 0 错误** |
| 自检 | `bin\Release\MirrorpondHUD.exe --selftest` | **退出码 0，失败项 0** |
| 格式 | `dotnet format --verify-no-changes` | **退出码 0** |
| 单测 | `dotnet run --project tests/TestRunner` | **12 passed / 0 failed** |
| 改动 | `git diff --stat` | 22 files, +294 / −81；第一轮 P2 已提交 82a955e |

### 验证方式（完整 `[~]` 列表见各表格）

| 编号 | 场景 | 验证方法 |
| --- | --- | --- |
| G-1 | Win10 19041–19045 / Win11 21H2–24H2 | 选「桌面取样」，看日志是否出现 DXGI；失败应回退 GDI 且不崩 |
| G-4 | Win10 2004 以下 / 安装屏幕保护类软件 | 拖动卡片，观察是否出现自拍拖影；日志应有 `设置截屏排除失败` |
| G-5 | 卡片后压一个挂起窗口 | 玻璃卡住 2 秒后应出现 `超过 2 秒未返回` 并自动恢复 |
| K-1 | 非 16:9 / 奇数高视频 | 用 `ffmpeg ... -vf scale=...` 对比帧字节数，或直接看画面是否错位 |
| K-2 | 非循环音频自然播完 | 播完不崩、不爆音；日志无 `Unprepare 失败` |
| P-1 | 网络盘 / 超回收站配额 | 删除时应出现系统“是否永久删除”确认，而不是静默删除 |

