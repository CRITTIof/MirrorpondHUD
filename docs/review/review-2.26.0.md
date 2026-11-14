# 代码审阅：MirrorpondHUD v2.26.0（2026-11-10）

> 范围：`src/` 全部 C#（41,643 行）+ 多程序集工程 / 插件清单 / 脚本 / 现有文档。
> 立场：**只读审阅**，未改动任何源码。行号以 `2e6d14c`（2.26.0）为准。
> 方法：通读宿主编排、插件加载、设置存储、渲染/布局、媒体与玻璃链路；静态扫描
> （空 catch、硬编码色、反射 JSON、线程/定时器）；对照 `AGENTS.md`、`docs/legacy-audit.md`、
> `docs/modularity-review.md`、`docs/review-p1-fixes.md`、`docs/review-p2-fixes.md` 去重已修项；
> 用 `bin\Release\mirrorpond.log` 的真实运行痕迹交叉验证。
>
> 基线（本地实跑）：
> - `dotnet build -c Release` → **0 警告 0 错误**
> - `bin\Release\MirrorpondHUD.exe --selftest` → **退出码 0**（失败项 0）
>
> 本文不含 P0；下表 P1 是"下一批必须修"，P2 是"应该排期"，P3 是"顺手做"。

---

## 0. 结论摘要

整体质量在同类"个人桌面工具"里属于偏高的一档：契约层用大量**能力接口**拆分
（`ICardDropTarget` / `ICardPresetSource` / `ICardStyleAware` / `ICardSettingsModule`…），
宿主没有 `switch(kind)`；媒体层的代次计数 + "认领收尾"CAS 是这套代码里最扎实的部分；
失败可见性（`LastSaveError`、`LogThrottle`、`ProcessIntegrity`）有明确设计。

问题集中在三条线：
1. **边界**：多显示器 / 混合 DPI / 玻璃重连 / 插件卸载——都缺一条"越界与重连"的显式判定；
2. **纪律回归**：`catch { }` 已扩散到 152 处（项目自己定的最高纪律），其中约 15 处直接吞掉
   "黑屏 / 没声音 / 没暂停"这类用户可见失败；
3. **设置窗半自动**：`LoadUi`/`ReadUi`/`MergeRuntimeFields` 三份手工清单互相漏项，
   造成"保存后主题名丢了""托盘开关被回滚""保存抛异常后运行与落盘不一致"。

| 级别 | 条数 | 代表项 |
|---|---|---|
| P1 | 7 | 保存清空主题名；副屏玻璃读错像素；共享 DIB 重建触发黑屏降级；ffmpeg 路径四设置静默失效；空 catch 152 处；中转站根路径越界；首个复制任务在 UI 线程 |
| P2 | 25 | 第二实例唤醒竞态；卸载探针结论不可信；保存链路无兜底；AutoStart 回滚；混合 DPI 定位；命令面板 CTS 误释放；对话模式切换后重建；UiKit 5 千行无消费者；TextCanvas 双份排版；玻璃取样 LOH |
| P3 | 28 | 日志冷却丢日志；CardRegistry 双检锁非 volatile；设置窗 Hide 持有资源；未冻结 Brush；每窗 2.5s 置顶定时器；中转站加载逐项同步 IO 等 |

### 最值得先做的八件事

1. **B-1 中转站路径越界**（`ItemName` / `UniqueTarget` + 一条 `IsInside` 断言）——
   根目录条目会让目标跑到盘符根；Move 模式最坏递归删源目录根。
2. **B-2 首个复制任务在 UI 线程**（`CardJobQueue.RunAsync` 用 `Task.Run` 包住 `work`）——
   拖入大文件/文件夹期间叠层、设置窗、取消按钮全部无响应。
3. **L-1 保存清空主题名**（`SettingsWindow.Save/Load/Theme` 三处，一个 `_readingUi` 守卫）——
   每次"套用主题→保存"都在丢值，且会触发一次富内容重建。
4. **L-2/L-3 玻璃边界**（`DesktopFrameSource.TryReadInto` + `GlassFeed`，各十几行）——
   多屏/重连时用户直接看到错像素或黑屏降级，且完全没日志。
5. **L-4 ffmpeg 设置热更新**（`CardMediaView.ApplyVideoSettings` 加 `_player` 分支）——
   静音/速度/循环/自动播放改了不生效，是"设置页骗人"级别。
6. **S-1 空 catch 分级**（先处理媒体生命周期 ~15 处）——项目最高纪律已经明显脱节。
7. **L-6 卸载探针**（日志 5/18 次误报）——结论不可信，会长期制造噪音。
8. **L-5 第二实例唤醒**（冷启动窗口期双击静默退出）——日志已有实证。

---

## 1. 逻辑正确性

### L-1【P1】「保存并应用」每次都会清空刚套用的主题名

**证据链**（逐行核对）

`src/Ui/SettingsWindow.Save.cs:80-101`：
```csharp
Settings s;
try { s = ReadUi(); } ...
bool saved = _onApply != null && _onApply(s);
```
`src/Ui/SettingsWindow.Load.cs:198-204`（`ReadUi` 开头）：
```csharp
foreach (CardEditor editor in _editors) editor.CommitPendingEdits();
Settings s = _working;
ApplyCliBoxes();   // 输入框可能还没失焦（直接点了「确定」）
```
`src/Ui/SettingsWindow.Appearance.cs:62-80`（`ApplyCliBoxes` 末尾）：
```csharp
UiChanged();      // 无条件
```
`src/Ui/SettingsWindow.xaml.cs:127-133`：
```csharp
void UiChanged()
{
    if (_suppress) return;
    if (!_applyingTheme) ClearThemeName();
    ...
}
```
`src/Ui/SettingsWindow.Theme.cs:48-55`：
```csharp
void ClearThemeName()
{
    if (_working == null || string.IsNullOrEmpty(_working.Style.ThemeName)) return;
    _working.Style.ThemeName = "";
    ThemeNameText.Text = "自定义";
    if (ThemeCombo.SelectedIndex != 0) ThemeCombo.SelectedIndex = 0;
}
```
保存时 `_suppress == false`（它只在 `LoadUi` 的 try/finally 内为 true），
`_applyingTheme == false` → **`ReadUi()` 期间就把 `_working.Style.ThemeName` 清空了**，
而 `ReadUi()` 全文没有任何地方把主题名从 `ThemeNameText`/`ThemeCombo` 读回来。

**影响**
1. 界面当场跳"自定义"；
2. `settings.json` 不再有 `Style.ThemeName`，重启后认不出主题（外观值还在，显示退化成自定义）；
3. `ThemeName` 参与 `RenderScheduler.StyleSignature()`（`RenderScheduler.cs:251-264`）——
   保存瞬间签名变化，下一次应用设置时对不实现 `ICardStyleAware` 的模块走**重建**
   （视频重开、对话历史清空），属于 S8 同族问题的实际触发点；
4. 之后导出的文件名回落到 `mirrorpond`（`SettingsWindow.Theme.cs:181-182`）。

**修复**：加 `_readingUi` 标志（或复用 `_suppress`），让 `ClearThemeName` 在读取期直接 return；
`ReadUi` 末尾把 `ThemeName` 显式写回（当前完全没读）。回归断言：套用主题 → 保存 → `Style.ThemeName`
仍等于主题名。**这是本次最值得先修的一条。**

### L-2【P1】副屏上的桌面玻璃取样：左侧读错像素、右侧永久退回 GDI，全程静默

**证据** `src/Ui/DesktopFrameSource.cs:195-196` 把屏幕尺寸固定成主屏：
```csharp
_screenWidth = GetSystemMetrics(0);    // SM_CXSCREEN = 主屏宽
_screenHeight = GetSystemMetrics(1);
```
`TryReadInto`（`:348-352`）对请求矩形做钳制：
```csharp
if (left < 0) left = 0;
if (top < 0) top = 0;
if (left + width > _screenWidth) width = _screenWidth - left;
if (top + height > _screenHeight) height = _screenHeight - top;
if (width < 1 || height < 1) return false;
```
而复制对象只建在 adapter 0 的 output 0（`:222-236`），坐标系就是主屏；
`GlassFeed.CaptureAll` 却按**虚拟桌面并集**算 `left/top`，`Render` 再用
`left - originLeft` 去共享图取块（`GlassFeed.cs:527`）。

**影响**（取图后端 = `Gpu`/`Auto` 且 DXGI 可用时）
- 任一张卡在主屏左侧（负坐标）→ `left` 被钳成 0，共享图内容来自主屏左上角，
  但取块偏移仍按原 `originLeft` 算 → **所有卡片的玻璃一起错位**（整叠共用一张共享图）；
- 卡在主屏右侧 → `width = 主屏宽 - left < 1`，每轮静默返回 false，永久走 18ms 的 GDI 路径；
  `GlassFeed.cs:193` 仍按"有快速后端"给 16ms 补帧间隔，拖动反而更慢；
- 坐标越界既不计失败、也不 `LogThrottle`，排障时零线索。

**修复**：`TryReadInto` 改为"请求矩形必须完全落在复制输出内，否则原样返回 false，
不得改写 left/top/width/height"；要真正支持副屏，按 `IDXGIOutput1` 为每个输出各建一个
duplication 实例。最低限度加一条一次性 WARN（请求区域超出复制输出范围）。

### L-3【P1】共享取样 DIB 重建后帧缓存不失效 → 黑帧 → 误触发"全黑保险丝"整叠降级

**证据** `GlassFeed.Detach`（`:130-150`）在最后一张卡移除时只 `ReleaseSharedSurface()`，
**不释放 `_frameSource`**；`Attach` → `CaptureAll` → `EnsureSharedSurface`（`:715-718`）
发现 `_sharedDc == 0` 就重建一块**全零** DIB：
```csharp
if (_sharedDc != IntPtr.Zero && _sharedWidth == width && _sharedHeight == height) return true;
ReleaseSharedSurface();
```
`DesktopFrameSource.TryReadInto` 用"缓存帧是否覆盖同一区域"决定要不要重拷贝：
```csharp
bool regionKnown = _frameAvailable && _frameLeft == left && ... ;
int hr = _acquire(_duplication, _primed ? 0u : 50u, frameInfo, out resource);
if (hr == DxgiErrorWaitTimeout) return regionKnown;   // 静态桌面：直接 true，不拷贝
```
**影响**：切走再切回「桌面取样」（或全黑降级冷却后重试）后，只要桌面静止，
`AcquireNextFrame` 超时 → 直接返回 true → 共享图仍是全零 → 采到黑帧 → 连续 3 轮判定
"读不到屏幕" → 整叠卡片降级到系统亚克力至少 3 秒；桌面不动就一直看不到恢复。
**修复**：给 `DesktopFrameSource` 加 `InvalidateDestination()`（`_frameAvailable = false`），
在 `ReleaseSharedSurface` / `EnsureSharedSurface` 重建后调用；或让 `TryReadInto`
多收一个 destination 代次参数比对。自检补"重建共享面后首帧必须重新拷贝"。

### L-4【P1】ffmpeg 解码路径下，静音 / 速度 / 循环 / 自动播放四个设置被静默忽略

**证据** `src/UiKit/Media/CardMediaView.cs:730-732`：
```csharp
void ApplyVideoSettings(MediaSettings previous)
{
    if (_videoWindow == null) return;   // ffmpeg 路径下 _videoWindow 永远是 null
```
启动参数只在 `Start` 时传一次（`:641-643`）：
```csharp
_player.Start(ffmpeg, path, width, height, info.Fps,
              _settings.Loop, info.Duration, _settings.Autoplay,
              _speed, 0, !_settings.Muted, 100);
```
**影响**：Decoder=FFmpeg（或 Auto 且系统解不开）时，改静音声音照放、改速度还是开播倍率、
改循环/自动播放不生效；`Bind` 的同路径分支也只调 `ApplyVideoSettings`，点"应用"什么都不发生。
系统解码路径行为正确，两条路径不一致，日志里没有一行提示。
**修复**：补 `_player` 分支——速度用 `SetSpeed`；静音给 `FfmpegPlayer` 加
`SetMuted(bool)`（内部 PauseAudio/ResumeAudio）；`Loop`/`Autoplay` 不能热应用的写一条
"需重开视频才生效"的 Info，不要静默。

### L-5【P2】第二实例唤醒存在竞态：冷启动窗口期双击会静默退出

**证据** `src/App.xaml.cs:76-93`：拿到单实例 Mutex 后立刻返回；唤醒事件要到
`OverlayController.Start()` → `StartActivationListener()`（`OverlayController.cs:674-701`）才创建：
```csharp
_mutex = new Mutex(true, "Local\\MirrorpondHUD.SingleInstance", out createdNew);
if (!createdNew)
{
    try { using (EventWaitHandle activation = EventWaitHandle.OpenExisting("Local\\MirrorpondHUD.Activate")) ... }
    catch (Exception ex) { Log.Warn("唤醒已有实例失败: " + ex.Message); }
    Shutdown(0);
    return;
}
```
日志实证（`bin\Release\mirrorpond.log`，20:50:15）：
```
[INFO] 已有实例在运行，通知它打开设置窗口
[WARN] 唤醒已有实例失败: No handle of the given name exists.
```
**影响**：首实例要经历插件扫描、配置读取、卡片创建、玻璃初始化……才创建事件；这期间
用户再双击一次，第二实例只写一条 WARN 就退出，桌面毫无反应（表现为"双击没反应"）。
**修复**：拿到 Mutex 后**立即**创建 `EventWaitHandle`（"启动完成"另设一个事件），
第二实例对 `OpenExisting` 做 2s 内重试；`StartActivationListener` 只启动等待线程。
冒烟补"连开两次"用例。

### L-6【P2】插件卸载探针的结论不可信（日志实证：同一条件下时好时坏）

**证据** 判定在 `App.OnExit` 末尾执行（`App.xaml.cs:212`），
`CardPluginUnloadProbe.Verify` 做 3 轮 `GC.Collect + WaitForPendingFinalizers`
（`CardPluginLoader.cs:99-137`）。日志（同一台机器、同一构建）：
```
[WARN] 插件 ALC 卸载后仍有模块引用：模块 4/6，上下文 6/6
[INFO] 插件模块已回收，ALC 上下文仍在卸载（运行时延迟回收；开发模式可接受）
```
18 次运行里 5 次报"模块 3–4/6 未回收"，其余报"已回收"；**上下文 6/6 每次都还活着**。

**根因（两条，都不是"随机"）**
1. **上下文必然回收不了**：`CardSettings.Extras` 在内存里存的是**插件程序集里的类型实例**
   （`CommandSettings` / `ChatSettings` / `MediaSettings` / `StationSettings`，
   见 `CommandCard.cs:53-55`、`ChatCard.cs:105-107`、`MediaCard.cs:95-97`、`DropStationCard.cs:40-42`）。
   这些实例活在宿主 `Settings` 树上，而 `App._controller` 退出时没被置空 →
   `OverlayController._settings` → `Cards[].Extras` → 插件类型 → ALC 永远不能卸载。
2. **模块实例的 3–4/6 是测量误差**：`Unload` 返回后立即 `Verify`，JIT 可能仍在寄存器/栈槽里
   保留 `CardPluginHandle` 列表的临时引用（GC 会扫寄存器），导致 `WeakReference.IsAlive` 假阳性；
   这也解释了数量为什么在 3 和 4 之间跳。

**影响**：这条 WARN 会长期误导维护者去找不存在的泄漏；真正的"上下文回收不了"又被降级成
Info 里一句"开发模式可接受"，等于把结论说反了。
**修复**：明确目标（2.20.0 已取消热重载，退出回收只是锦上添花）——把"上下文仍活"改成
一次性 Info 并写明原因；若要验证模块回收，把 `Verify` 移到独立后台线程、并先把
`App._controller = null`；长期方案见 M-3。

### L-7【P2】保存链路对 `_onApply` 无兜底，`Apply` 不是原子的

**证据** `SettingsWindow.Save.cs:101`：`bool saved = _onApply != null && _onApply(s);`（无 try）；
`SettingsApplier.cs:117-121`：
```csharp
_host.Settings = settings;
settings.Normalize();
CardModel.Normalize(settings);
SyncCards(summoned);
```
`SyncCards`（`:142-176`）只有循环全成功后才 `cards.Clear()`，而 `CreateSlot` 会 `new OverlayWindow`。
**影响**：任何一处抛出，异常从 `SaveClick` 逸出——`_liveSettings`/`_working`/`_runtimeBaseline`/
`SyncAnchor` 全不更新、状态行无失败文案；`_host.Settings` 已是新树而 `_cards` 仍是旧槽，
之后落盘新配置、运行用旧配置；若抛的是 `InvalidOperationException` 会被 App 白名单吞掉，
表现为"保存没反应"。
**修复**：`SaveClick` 包 try/catch 并保留旧 live；`Apply` 把 `_host.Settings` 赋值推迟到
`SyncCards` 成功后（或先建到临时列表再一次性提交）。断言：假 `ISettingsHost` 的
`CreateSlot` 抛异常 → 旧 `Settings` 引用不变。

### L-8【P2】`Behavior.AutoStart` 漏在运行时字段合并清单外 → 设置窗保存静默回滚托盘开关

**证据** `SettingsApplier.cs:284-290` `ToggleAutoStart` 直接改 live 树并立即 `SaveSettings()`；
`SettingsWindow.Save.cs:170-205` `MergeRuntimeFields` 只合并 `Hotkeys` 与卡片级字段，
`Behavior` 一个字段都没合并。
**影响**：设置窗开着 → 托盘切"开机自动启动" → 回设置窗点保存 → 复选框旧值把 `AutoStart`
翻回去，`ApplyAutoStart` 改写注册表。用户显式操作被静默撤销且报"保存成功"。
**修复**：补 bool 版 `MergeRuntimeValue` 合并 `Behavior.AutoStart`（及同类布尔运行时字段），
或 `ToggleAutoStart` 主动回填设置窗复选框。

### L-9【P2，历史项仍未修】`ConfigStore.Load` 把"读失败"当"损坏"，存在覆盖有效配置的风险

**证据** `src/Core/ConfigStore.cs:90-108, 237-241`：
```csharp
if (!TryReadSettings(file, out settings, ...)) corrupted = true;
...
catch (Exception ex) { Log.Warn("读取设置失败: " + ex.Message); return false; }
```
任何 IOException / 共享冲突都被归类成 `corrupted`，随后走"备份 `.broken-*` → 从 `.bak`
或默认值恢复 → 立即 `Save` 覆盖主文件"。`review-p2-fixes.md` 的
"§17 I/O 暂时失败与解析失败区分"一直是**未纳入**状态。
**影响**：杀软/索引器短暂锁文件、OneDrive 占位、网络盘抖动这些瞬时读失败会被当成内容损坏；
若此时 `File.Copy`（备份）恰好成功，主文件就被默认值覆盖（`.broken-*` 里有原件，
但用户看到默认配置，下一次保存就彻底丢）。
**修复**：`TryReadSettings` 拆三态（解析失败 / IO 失败 / 不存在）；IO 失败不走损坏恢复、
不 `Save`，提示"配置暂时读不到"并禁用保存（与 `LoadFailed` 同链路）。

### L-10【P2】异常配额是终身预算；不可恢复分支 `Handled=false` 抢在 `Shutdown(1)` 之前

**证据** `src/App.xaml.cs:19-20, 38-45`：`_swallowedUiExceptions++` 只增不减；
`:48-55`：
```csharp
Log.Error("UI 线程异常不可恢复（白名单外 / 超限 / 主界面未启动），应用将退出", ex);
MessageBox.Show(...);
args.Handled = false;
Shutdown(1);
```
**影响**
1. 配额终身有效：叠层常驻几天攒满 5 次瞬态 COM/剪贴板异常后，第 6 次同类可恢复异常
   直接判死并弹"应用已处于不可信状态"（`MaxSwallowedUiExceptions` 不是"连续"语义）；
2. `Handled=false` 会让异常继续上抛，进程按未处理异常终止（WER 崩溃框），
   `Shutdown(1)` 的退出码与 `OnExit` 收尾（互斥量/托盘/插件卸载/日志）成竞争失败方；
   最坏情况是托盘留幽灵图标、`.tmp/.bak` 未清理、日志没有"App 退出"行。
**修复**：配额加时间窗（10 分钟无异常则清零）或改成"连续 N 次"；不可恢复分支改
`args.Handled = true; Shutdown(1);`。补一条不可恢复异常 → 退出码 1 的实测。

### L-11【P2】`WaveOutPlayer.Start` 在缓冲准备失败时仍返回成功

**证据** `src/UiKit/Media/WaveOutPlayer.cs:203-208`：
```csharp
int headerCode = waveOutPrepareHeader(_handle, header, headerSize);
if (headerCode != 0)
    Log.Warn("准备音频缓冲失败: 槽 " + i + "  " + MmsysErrorName(headerCode));
}
Volatile.Write(ref _resources, 1);
...
return true;
```
**影响**：设备被独占/驱动异常时 4 个槽全失败，`Start` 仍 true → `FfmpegPlayer` 记
"音频输出已启动"、媒体卡状态行不显示"音频不可用"；直到 Feed 里 `waveOutWrite` 失败才 Warn。
**修复**：统计 prepare 失败数，全失败返回 false 并设 `LastErrorName`（上层已有
`AudioUnavailable` 通道）；部分失败把计数写进那条 Warn。

### L-12【P2】`TextCanvas` 的光标闪烁定时器没有卸载出口

**证据** `src/Ui/TextCanvas.cs:97-105` 构造里建 `DispatcherTimer`，`CursorVisible`
（`:295-315`）为 true 就 `Start`；类里没有 `Unloaded` / `Dispose`。
`OverlaySurface.Apply(..., cursor:true)` 是唯一写入点（`OverlaySurface.cs:115,160`）。
**影响**：命令行卡进入过"显示光标"状态后，`Tick` 委托把整个 OverlaySurface 可视树钉在
Dispatcher 上（与已修的 P2-4 同类）；卡片隐藏后仍每 530ms `InvalidateVisual`。
**修复**：`Unloaded += (s,e) => _blink.Stop();`，`IsVisibleChanged` 按可见性起停。

### L-13【P2，需真机实测】混合 DPI 下叠层定位只用主屏缩放

**证据** `src/Ui/OverlayWindow.xaml.cs:313-328` 全程只有一个 `scale`
（`PrimaryScreenWidth / screens[0].Bounds.Width`）：
```csharp
left = screen.WorkingArea.Left * scale + _settings.Overlay.X;
```
`app.manifest:22-23` 声明 `PerMonitorV2`；`DetectMonitor`（`:800-836`）与
`CardLayoutCoordinator.ResolveAlign`（`:144-152`）复用同一主屏 `scale`。
**影响**：主屏 150% + 副屏 100%（或反之）时，锚点设到副屏会偏出几百到上千 DIP；
`DetectMonitor` 用错误坐标反推显示器会把 `Overlay.Monitor` 写错并落盘（拖一次永久记错）；
`ResolveAlign` 的左右半区判定也错，副屏"自动对齐"恒定落到一侧。
**说明**：WPF 的 `Window.Left/Top` 在 PerMonitorV2 下的语义本身有历史坑，这段代码是
"按主屏比例换算虚拟屏原点"的经验写法，**建议先实测确认**再改；不改则应在文档里写明
"混合 DPI 不支持跨屏精确定位"。
**修复**：走物理像素（`MonitorFromPoint` + `GetDpiForMonitor`，或 `SetWindowPos` 传物理坐标），
三处统一用目标屏 DPI。

### L-14【P3】运行期重新开启托盘失败没有兜底也没有提示

**证据** `TrayController.cs:61-67` 的兜底只在 `Start` 跑一次；`:74-82` `Update` 只设
`_tray.Visible`；`TrayIcon.cs:254-268` 失败仅 `_addFailed = true` + WARN，`AddFailed`
没有任何运行期消费者。
**影响**：启动时托盘关闭/被策略拦 → 设置里重新勾上 → `NIM_ADD` 失败只写 WARN；
用户以为托盘已开，实际既无图标也无提示（Explorer 重启重试失败同样静默）。
**修复**：抽 `EnsureTrayAvailable()`，在 `Update` 与重建失败分支都调用，
把原因透到设置窗"行为"页状态行。

### L-15【P3】卡片设置区块创建失败后界面无声，且本次窗口内不再重试

**证据** `CardEditor.cs:54-76`：
```csharp
_sectionCreated = true;              // 先落位
try { _section = source.CreateEditor(...); }
catch (Exception ex) { Log.Warn(...); _section = null; }
...
public Visibility SettingsSectionVisibility
    { get { return SettingsSection == null ? Visibility.Collapsed : Visibility.Visible; } }
```
**影响**：插件区块抛异常时用户看到"这张卡没有设置项"，与真的没有设置无法区分（违反 §5）；
且不再重试，`CommitPendingEdits` 变空操作，用户草稿丢失。
**修复**：catch 里构造可见错误占位（Danger 色 TextBlock），`_sectionCreated` 仅成功时为 true。

### L-16【P3】`CardRegistry.EnsureInitialized` 双检锁缺 `volatile`（ARM64 目标）

`src/Cards/CardRegistry.cs:125-143`：`_initialized` 锁外读、锁内写，`_modules`/`_lookup`
先于它发布。x64 实测无碍，但工程同时发布 `win-arm64`，ECMA 内存模型允许读线程先看到
`_initialized = true`、后看到 `_lookup` 写入。
**修复**：`static volatile bool _initialized;`。同类 `Log._path`（`Log.cs:35-37`）是
字符串引用赋值，实际安全，可不动。

### L-17【P3】`KeyboardHook.Dispose` 在 Join 超时后仍释放 `_ready`

`src/Input/KeyboardHook.cs:160-175`：`Join(1500)` 结果被忽略，之后无条件
`_ready.Dispose()`；若线程尚未执行 `_ready.Set()`（`:116`），后台线程上的
`ObjectDisposedException` 会直接终止进程（违反 §6.3"超时后移交收尾权"）。
**修复**：仅 Join 成功才 Dispose `_ready`（或干脆不释放）；`_callback` 静态单槽
改为实例字段 + 列表持有，避免第二个实例覆盖旧委托的根。

### L-18【P3】配置自动修复的落盘结果被忽略

`src/Core/ConfigStore.cs:200-205`：`if (migrated || recovered || corrupted || empty) Save(...)`，
返回值未检查。修复写盘失败（只读/磁盘满/占用）时只有 `LastSaveError` + ERROR 日志，
`LastLoadWarning` 不会告诉用户"修好了但没存进去"，下次启动重复整轮修复。
**修复**：失败时把原因追加进 `LastLoadWarning`，走既有提示链路。

### L-19【P3】`CardStateStore.Delete` 绕过写锁

`src/Core/CardStateStore.cs:77-86` `Write` 有 `lock (WriteSync)`；`:119-135` `Delete`
无锁直接 `File.Delete`。后台保存与删除并发时，Delete 落在 `AtomicFile` 的
Replace/.bak 之间 → Replace 失败 → 回退 `File.Copy` 抛 FileNotFound → 判成
`BackupError` 中止写入，日志报"备份失败"（语义误导）。`AtomicFile` 注释明确要求调用方锁串行化。
**修复**：`Delete` 同样 `lock (WriteSync)`。

### L-20【P3】`OverlaySurface` 背景图同路径不重载，加载失败后永久缓存失败状态

`src/Ui/OverlaySurface.cs:212-223`：路径相同则不重载；`LoadImage` 失败时 `_imagePath`
已写成该路径、`_image == null`，之后恒走"不重载"分支。
**影响**：同路径覆盖背景图不更新；首次因占用/权限失败后永远不重试，只能改路径或重启。
**修复**：缓存键加 `LastWriteTimeUtc + Length`（与 S13 对壁纸的建议一致）；失败时
`_imagePath = null`。**这是 S13 之外的第二处。**

### L-21【P3】设置窗「关闭」只是 Hide，模块区块与预览位图在退出前一直被持有

`SettingsWindow.xaml.cs:153-162`：`OnClosing` 里 `e.Cancel = true; Hide();`，
`Closed` 永不触发，`ReleaseCardEditors` / `ItemsSource=null` 只在 `CloseForReal` 执行一次。
隐藏期间仍持有整棵可视树、每个模块的 `SettingsSection`（间接持有插件实例）、
`ThemePreviewImage` 的 2x RTB、`_working`/`_runtimeBaseline` 两份深拷贝。
**修复**：Hide 路径调 `ReleaseCardEditors` 并清 `ThemePreviewImage.Source`，
下次 Show 前靠 `Reload` 重建。

### L-22【P3】一次保存里热键路由二次落盘 + 整树重建设置页

`HotkeyRouter.cs:121-125`：`if (saved) { _host.SaveSettings(); _host.ReloadSettings(); }`；
`SettingsApplier.Apply` 自身在 `:127` 已 `SaveSettings()`，`:130` 才 `RebuildHotkeys()`。
**影响**：首次应用设置几乎必然命中——落盘 2 次（多一次 `File.Replace` + `.bak` 轮转）、
`LoadUi` 2 次、`RebuildCardList` 2 次、模块区块摘挂 2 次，用户看到"保存后界面闪一下"。
另 `HotkeyManager.cs:166` `_nextId++` 单调增长（长期频繁改键会耗 id 空间）。
**修复**：`Register` 只记录需写回字段，由 `Apply` 末尾统一落盘一次、只回填控件；id 复用最小空闲值。

---

## 2. 性能

### P-1【P2】`TextCanvas` 每次 Measure + OnRender 把同一行排版算两遍

`TextCanvas.cs:364-375`（Measure）里 `BuildLine(line).Width`；`:605-617`（OnRender）里
又 `BuildLine(lines[i])`；`LineHeightOf()` 每次还额外 `CreateRun("Ag", ...)`（`:400-410`）。
每个 `BuildLine` 的每个 run 都是 `new FormattedText` + `new Typeface`（`:473-478`）。
**影响**：10 行 × 3 run 的卡片一次文本更新 ≈ 60 个 FormattedText；指标卡按 8Hz 刷新时
UI 线程每秒数百个这类对象，再加 `Split()` 的 `List<string>`。
**修复**：把 `lines → List<TextLine>` 缓存到字段，`Text/LayoutText/Font*/IconScale/LineHeight`
变化时置脏，Measure 与 OnRender 复用；"Ag" 行高按 (字体,字号,字重) 缓存。
**收益**：卡片刷新路径上最贵的对象直接减半，不动渲染结构。

### P-2【P2】`GlassFeed` 每帧每卡分配一块直接进 LOH 的 `byte[]`

`GlassFeed.cs:527-535`：
```csharp
var buffer = new byte[w * h * 4];
Marshal.Copy(target.Bits, buffer, 0, buffer.Length);
GlassCapture.BoxBlur(buffer, w, h, _blurRadius, true);
```
600×400 的卡缩到 1/2 后 240KB ≥ 85KB，**每卡每帧**落 LOH；10fps × 5 张 ≈ 12MB/s，
`BitmapSource.Create` 每帧还一次同尺寸拷贝。
**修复**：`Target` 里加 `byte[] Buffer; int BufferW/H;`，尺寸变化才重分配
（`LastFrame` 在 `:654-662` 已经是这个写法）。

### P-3【P2】兼容玻璃取样在 UI 线程每帧枚举窗口、逐窗口开进程句柄

`GlassRuntime.cs:287`（UI 线程）→ `GlassCapture.FindLayers` → `ProcessNameOf`
（`GlassCapture.cs:270-282`）每次 `new Process.GetProcessById(pid)`；`Grab` 已丢进
`Task.Run`（`GlassRuntime.cs:296`），偏偏 `FindLayers` 留在 UI 线程。
**影响**：Compat 实现 `BlurFps` 最高 30，拖动/切窗口时最频繁。
**修复**：`FindLayers` 一起进那个 `Task.Run`（只需矩形快照），或给 pid→进程名加缓存。

### P-4【P2】`GifAnimator` 把 GIF 每一帧解码成冻结位图常驻

`GifAnimator.cs:61-68`：循环 `SelectActiveFrame` + `ToBitmapSource`，全部塞进 `_frames`，无上限。
800×600 / 200 帧 ≈ 384MB 常驻；4K GIF 上 GB，且无降级提示。
**修复**：`TryCreate` 先估算 `count * width * height * 4`，超预算（建议 64MB）记 Warn
并退回"当前帧解码"路径，或只缓存前 N 帧。

### P-5【P2】每张卡片窗口各有一个 2.5s 置顶定时器

`OverlayWindow.xaml.cs:140-157`：每窗口 `_topmostTimer` 2500ms，Tick 里
`EnsureTopmost()` → `SetWindowPos` + `_glassRuntime.UpdateBackdrop()` + `RaisedTopmost`。
N 张可见卡 → 每 2.5s N 次 `SetWindowPos` + N 次底板重摆。
**修复**：宿主持有共享置顶定时器统一遍历 `_cards`，窗口只提供 `EnsureTopmost`。纯合并，不改行为。

### P-6【P2】`MetricEngine.Start()` 在 UI 线程 `Thread.Sleep(250)`

`MetricEngine.cs:69-99` 两次 `_pdh.Collect()` 之间 `Thread.Sleep(250)`，
调用链是 `OverlayController.Start` → `Safe("采样引擎", StartEngine)`（UI 线程）。
启动固定多 250ms 无内容。P1-9 已保证"UI 不碰 PDH"，但这个 Sleep 漏了。
**修复**：预热移到采样线程第一轮，`Start` 只创建线程立即返回。

### P-7【P2】设置页每次按键重建整棵卡片列表；外观预览每次按键重建 2x 位图

`SettingsWindow.Cards.cs:58-70` 每按键 `CardList.ItemsSource = filtered`（新 List 实例）；
`SettingsWindow.xaml:60-63` 是 `ScrollViewer + ItemsControl`（无虚拟化）；
`SettingsWindow.Theme.cs:267-289` 每次 `UiChanged` 都 `new OverlaySurface()` +
Measure/Arrange + 2x `RenderTargetBitmap`（560×2 位图）；触发点是
`SettingsWindow.xaml.cs:85-113` 的 `TextChanged/ValueChanged`。
**影响**：①列表换 `ItemsSource` 就重建全部容器，模板里
`ContentControl Content="{Binding SettingsSection}"` 反复摘挂已缓存区块——卡片多时明显卡顿，
也是"元素已有父级"类绑定异常的高风险面；②改前景色逐字符、拖滑块时每秒几十次全量离屏渲染。
**修复**：`ListBox + VirtualizingStackPanel` 或 `CollectionViewSource.Filter`（不换 ItemsSource）；
预览用 30–50ms `DispatcherTimer` 合并；色板走 `GetBrush` 缓存并 Freeze。

### P-8【P3】媒体状态行每 250ms 重建整段 `Inlines`

`CardMediaView.cs:788-799`（250ms Tick）+ `:1101-1118`：`Inlines.Clear()` + 2~3 个新 `Run`。
**修复**：建固定 `Run` 字段只改 `.Text`，`_audioNote` 仅在内容变化时增删。

### P-9【P3】视频子窗拖动时每帧重建 GDI region 并强制整窗重绘

`VideoWindow.cs:128-142`（`_lastRect` 变化）+ `:169-173`（`CreateRoundRectRgn` +
`SetWindowRgn(hwnd, region, true)`）；`Place` 由 `LayoutUpdated` 驱动
（`CardMediaView.cs:177-182`）。位置一变就重建圆角区域并强制整窗重绘，画面会闪/停。
**修复**：圆角没变就不重设 region；拖动中跳过，拖完补一次。

### P-10【P3】每帧变量表与模板渲染的分配

`Variables.Build`（`Variables.cs:20-112`）每帧 new `Dictionary` + ~40 个字符串；
`TemplateEngine.Render` 每次 4 个字符串（花括号转义的两次 `Replace` + 结尾两次）。
8Hz 下是"每秒几千个短命对象"的量级，不卡，但和 P-1 叠加在同一线程。
**修复**：时间/日期/主机名按秒缓存；`Bar()` 用查表代替循环拼 StringBuilder。

### P-11【P3】`Json.Serialize` 每次反射 `GetFields`，无字段缓存

`src/Contracts/Core/Json.cs:84`：每个对象都 `value.GetType().GetFields(...)`，
`f.GetValue` 还装箱。只在"应用设置 / 落盘 / 克隆设置窗副本"时触发，**当前不是瓶颈**；
将来做实时配置同步会变成瓶颈。**修复**：加 `Dictionary<Type, FieldInfo[]>` 缓存。

---

## 3. 代码风格与工程纪律

### S-1【P1，纪律性】152 处 `catch { }`，只有 3 处带说明

`AGENTS.md` §5 明确"不允许写 `catch { }`"。实测（`src/**`，排除 `bin/obj/samples`）：
```
空 catch 总数：152
紧邻上一行有 // 注释：3
按文件 Top：OverlayController 19、FfmpegPlayer 13、WaveOutPlayer 10、CardMediaView 9、
            Log 9、VideoWindow 8、CommandRunner 8、TrayIcon 5、AsyncProcess 4、
            HotkeyManager 4、CardPluginLoader 4 …
```
`review-p2-fixes.md` 把"P2-9 空 catch 分级"列为**未纳入**（"涉及面大、纯维护性"），
所以这不是新发现，但范围已从当时抽样扩到 152 处，且里面确实藏着功能路径的吞错：

| 位置 | 吞掉的是什么 | 后果 |
|---|---|---|
| `OverlayController.cs:336` | `Rich.OnZOrderChanged` | 视频子窗没摆回 z 序，画面被压住，无日志 |
| `OverlayController.cs:526` | `OnVisibilityChanged(content, true)` | 视频/动图没开始播放，卡片像"坏了" |
| `OverlayController.cs:556/563/566` | `OnVisibilityChanged(false)` / `Dispose` / `ClearRichContent` | 媒体句柄释放失败完全不可见，正是泄漏最容易发生的地方 |
| `CardMediaView.cs:939-950` | 整个 `Pause()` | 暂停失败时 `_playing` 停在旧值，状态行与画面不一致 |
| `CommandRunner.cs:212` | `process.Kill(true)` | 超时进程杀不掉，继续占资源 |
| `TrayIcon.cs` 5 处 | 托盘添加/删除/气泡 | 托盘消失后用户只能重开程序 |

**建议（分级，不要一次全改）**
1. 先把**媒体生命周期**（上面 OverlayController 4 处 + `CardMediaView.Pause/Play/Stop`）
   全部改 `Log.Warn`——这些失败直接对应"黑屏/没声音/关不掉"；
2. 纯清理路径保留空 catch，但统一加一行注释说明"对象可能已 Dispose / 句柄已被回收"；
3. 在 `AGENTS.md` §9 提交清单加一条"新增 `catch { }` 必须带注释"。

### S-2【P3】4 处生成未冻结的 `SolidColorBrush`，与 `CardPalette` 约定不一致

`CardButton.cs:167-171`、`CardList.cs:139`、`CardDataControls.cs:927`、`CardTextBox.cs:30-34`；
同文件族的 `CliUi.CardPalette.Brush`（`CliUi.cs:33-46`）都会 `Freeze()`。
**修复**：统一走 `CardPalette.Brush(...)` 或就地 `Freeze()`。

### S-3【P3】两个实例写同一份日志；未持有 Mutex 的实例也调 `ReleaseMutex`

`App.xaml.cs:58-59` 在 Mutex 检查**之前**就 `Log.Info("App 启动…")`，第二实例会与首实例
交错写同一文件（`Log` 的轮转是跨进程无锁的，`Log.cs:204-225`）；`OnExit` 对第二实例也调
`ReleaseMutex()`（`App.xaml.cs:203-206`），会抛 `ApplicationException` 被 `Log.Debug` 吞掉。
**修复**：第二实例不写主日志（或 `MIRRORPONDHUD_LOG` 分流）；用 `createdNew` 决定是否 Release。

### S-4【P3】`LoadUi` / `ReadUi` 是两份手工镜像清单

`SettingsWindow.Load.cs:72-90`（控件←字段）与 `:208-232`（字段←控件）逐项手抄同一清单；
新增设置项要同时改 XAML + LoadUi + ReadUi，任一侧漏改即静默丢值，编译期零提示，
自检覆盖不到。**L-1 就是这条的受害者**。
**修复**：做成 Load/Read 成对的声明表；加自检断言"Settings 每个公共字段都被某条 binding
覆盖或在忽略清单里"（反射，参照卡片注册段）；`ReadUi` 末尾回灌一次控件。

---

## 4. 模块化

### M-1【P2】宿主仍然硬编码 "infotext / metrics / clock / keys / command / media"

`src/Cards/CardModel.cs:99-133`：`CreateDefaultCards()` 写死五种 Kind 与三种预设，
默认内容取自 `CardDefaults`；`src/Plugins/InfoText/InfoTextCard.cs` 里还有一套
`InfoTextDefaults` + `PresetList`，两份模板逐字相同（`CardDefaults.cs` vs `InfoTextDefaults.cs`）。
`docs/modularity-review.md` 的 P2-14"kind 硬编码"一直是"未动"。
**影响**：
- 用户删掉/改名内置插件后，首次启动的默认卡片会落到 `UnavailableCardModule`（名字变"插件不可用"）；
- 想加一种"默认常驻卡"必须改宿主，和"加一个类即可"的承诺不一致；
- 两份模板靠人肉同步，改一处忘另一处就是默认观感漂移；
- `InfoTextCard.InferPreset` 还依赖"内容字符串与预设默认模板精确相等"，模板一漂移，
  预设识别就会失效（显示与实际内容不符）。
**建议**：给 `ICardModule` 加可选能力 `ICardDefaultProvider`
（返回 `IList<CardSettings> CreateDefaults()`），`CardModel.CreateDefaultCards` 遍历
`CardRegistry.Modules` 取默认卡；`CardDefaults` 与 `InfoTextDefaults` 全删。
这是模块化评审 §9.3 没列、但成本最低的一块。

### M-2【P2】UiKit 约 5 千行没有任何生产消费者

全仓引用统计（排除 `samples/UiKitGallery` 与 `SelfTest`）：

| 文件 | 行数 | 生产引用 |
|---|---|---|
| `CardOverlayControls.cs`（Modal/Dialog/Popover/Tooltip/ContextMenu/Toast） | 1238 | 0 |
| `CardDataControls.cs`（Table/Badge/Tag/Metric/Gauge/Ring） | 1188 | 0 |
| `CardChartControls.cs`（Sparkline/Line/Bar） | 898 | 0 |
| `CardFormControls.cs`（Slider/Toggle/Select/Number/Color/Segmented/Form） | 886 | 0 |
| `CardTreeTimeline.cs`（Tree/Timeline） | 913 | 0 |
| 合计 | 5123 | 仅 `SelfTest` + 画廊示例 |

UiKit 总量 9,915 行，这 5 个文件约占 **52%**。每次主题令牌重构都要跟着改，
而 `docs/cards-architecture.md` 把它们写成"共享控件"，插件作者会以为已有现成实现。
另有一个真实缺陷：`CardToast.ShowCore` 用 `new StyleSettings()` 上色
（`CardOverlayControls.cs:1063-1075`），直接调用拿到的是默认深色主题而非当前主题。
**建议**：三选一并写进 docs——(a) 删到 `examples/`；(b) 保留但在文件头注明
"预留、无生产消费者"；(c) 让画廊插件成为官方发布的一部分（当前它不进 `bin`）。
顺带修 `CardToast` 的样式参数。

### M-3【P3】`Extras` 在内存里存插件类型，ALC 结构上不可回收

`CardSettings.Extras` 是 `Dictionary<string, object>`；`InitializeCard` 后放进的是
插件程序集的强类型对象（`CommandSettings` 等），随宿主 `Settings` 树一直活到退出。
与 L-6 是同一根因的两面：一边制造"上下文 6/6 不回收"，一边让探针结论失真。
**建议**：`Extras` 只存 `Dictionary<string,object>`（JSON 节点），强类型对象只在模块内部
临时绑定；或接受现状，把 ALC 回收的期待从"退出时"彻底删掉（2.20.0 已无热重载）。

### M-4【P3】`ICardHostAware` 是一个"文档说别用"的扩展点

`src/Contracts/Cards/CardHost.cs:44-61` 自己写着"模块是单例、Attach 不携带卡片标识，
因此不能把 host 存进模块字段"；内置 6 张卡无人实现，唯一正确用法是
`CardRenderContext.Host`。宿主仍每次 `CreateSlot`/`RebindSlot` 调 `Attach`
（`OverlayController.cs:423-432`）。
**建议**：删除 `ICardHostAware`（apiVersion 2 尚未被第三方大规模使用时），或标 `[Obsolete]`
并指向 `CardRenderContext.Host`。少一个能写错的扩展点，比多一句"文档免责声明"可靠。

### M-5【P3】`IsInteractive` 仍然一身多义

模块化评审 §9.2 结论 3 已指出：`IsInteractive` 同时表示"键盘独占 / 单槽 / 强制非常驻 /
抑制全局键盘钩子"。v2.6.3 已拆出 `NeedsMouseInput` / `HandlesOwnDrag`，但 `IsInteractive`
本身仍是四义；当前只有命令行卡使用，问题被掩盖。**建议**：把"强制非常驻"从
`CardModel.Normalize`（`:61`）挪到模块声明，并在接口注释写清"新增交互卡最多一张"。

### 值得保留的设计（不要"顺手重构"）

- 条件能力接口 + 注册表驱动，宿主没有 `switch(kind)`；
- `IRichCardModule` 只返回 `FrameworkElement`，窗口/层级留在宿主；
- `AtomicFile` 把两处原子写收成一份，"备份失败即中止"写得很清楚；
- `ICardEditorSection` + `Extras` 让新卡"带设置"不用改宿主五个文件（命令卡已验证）；
- 媒体层代次计数 + 有界 Join + "认领收尾"CAS，是本项目最值得保留的并发模式。

---

## 5. 其他（诊断 / 文档 / 自检）

### O-1【P2】日志写失败冷却窗口内直接丢弃，且恢复后没有任何记录

`src/Contracts/Core/Log.cs:148-201`：写失败进入 500ms→30s 冷却，期间 `Write` 直接 return
（连降级文件也不写）；恢复时 `_writeFailures = 0` 但没有一条"日志已恢复"。
**影响**：磁盘满/被杀软锁的 30 秒里，恰好是最需要日志的窗口，ERROR 也会被静默吞掉；
事后无法判断"中间丢了什么"。
**建议**：冷却期间累计丢弃条数；恢复时先写一条
`[WARN] 日志写入恢复，期间丢弃 N 条`。

### O-2【P3】`theme-system.md` 描述的 `MIRRORPOND_THEME_WATCH` 热重载并不存在

文档写"`MIRRORPOND_THEME_WATCH=1` 时 300ms 内重新套用"（`theme-system.md:128`），
全仓没有该环境变量、没有 `FileSystemWatcher`。该文首行标注"研究 + 建议"，
严格说不算虚假承诺，但按"现状"读会误解。**建议**：段首标 `[未实现]` 或删掉。

### O-3【P3】`SelfTest` 的回归覆盖缺口

现有自检覆盖了设置损坏、主题、交互卡、控件、媒体策略等大量场景，但本次发现的
关键逻辑没有断言可依赖：`MergeRuntimeFields` 逐字段合并、`HotkeyRouter`"新键失败保留旧键"、
`ApplySettings` 返回 false 的状态行、混合 DPI 定位、玻璃共享面重建、ffmpeg 设置热更新、
第二实例唤醒、卸载探针。
**建议（按可行性）**：①把 `MergeRuntimeFields` 改成 `internal static` 纯函数，用
"副本/基线/live"三棵树断言；②假 `IHotkeyHost` + 预先占组合键验证旧键仍生效；
③假 `ISettingsHost` 验证失败路径；④定位/对齐抽纯函数做表驱动断言。
L-3/L-4 至少可以纯逻辑测（注入假目标矩形、暴露 `ApplyVideoSettings` 的可测分支）。

---

## 6. 仍有效的历史项（本次不重复展开）

| 编号 | 状态 | 本次观察 |
|---|---|---|
| S5 `OverlaySurface.BrushCache` 无上限 | 未修 | 未见新增同类缓存，未恶化 |
| S6 `ButtonsCard` 不做就地换肤 | 未修 | 演示卡，可接受 |
| S8 `Style.ThemeName` 进样式签名 | 未修 | 由 L-1 实际触发（保存时签名变化） |
| S9 `.mptheme` 数值范围与 `Normalize` 双份 | 未修 | `ThemeFile.cs:540-584` 与 `Settings.Normalize` 仍是两张表 |
| S11 巨型文件 | 部分 | `OverlayController` 已从 1772 降到 892；UiKit 仍有 800–1200 行文件 |
| S13 壁纸只按路径缓存 | 未修 | 多了一份常驻冻结位图（`DesktopBackground.cs:40,161-165`）；`OverlaySurface` 是第二处（L-20） |
| P1-5 设置窗快照 | 已修 | `MergeRuntimeFields` 已落地，缺 `Behavior` 字段（L-8） |
| P2-9 空 catch 分级 | 未纳入 | 已扩到 152 处，见 S-1 |
| §17 IO 失败 vs 解析失败 | 未纳入 | 见 L-9 |

---

## 7. 建议的修复顺序

> §9 的 B-1（路径越界）与 B-2（复制卡 UI）风险高于本表，应排在第 1、2 位；
> 下表保留主体项的排期。

| 顺序 | 内容 | 理由 | 预估 |
|---|---|---|---|
| 1 | L-1（主题名守卫 + 回读） | 每次保存都丢值，改动最小，收益最直接 | 0.25 天 |
| 2 | L-2 + L-3（玻璃边界） | 用户可见，有日志可验收 | 0.5 天 |
| 3 | L-4（ffmpeg 设置热更新） | "改了不生效"最伤信任 | 0.5 天 |
| 4 | L-5 + L-6（唤醒 + 卸载探针） | 诊断可信度，止住长期噪音 | 0.5 天 |
| 5 | S-1 分级清理（先媒体生命周期 ~15 处） | 项目最高纪律 | 0.5 天 |
| 6 | L-7 + L-8（保存链路兜底 + AutoStart 合并） | 同一链路一次改完 | 0.5 天 |
| 7 | P-1 + P-2（TextCanvas 缓存、GlassFeed 复用 buffer） | 热路径分配减半，风险低 | 1 天 |
| 8 | M-1 + M-4（默认卡注册表化、删 ICardHostAware） | 模块化收口，删两份模板 | 1 天 |
| 9 | L-9 + O-1（IO 三态、日志恢复记录） | 数据安全与可诊断性 | 0.5 天 |
| 10 | 其余 P3 | 顺手做 | 按需 |

## 8. 可复跑命令

```bat
dotnet build -c Release
bin\Release\MirrorpondHUD.exe --selftest
findstr /n /c:"catch {" src\*.cs
findstr /n /c:"Thread.Sleep" src\*.cs
git log --oneline -20
```

---

## 9. 插件 / 契约层补充（第二轮）

> 这一节来自插件层专项审阅，条目均已逐行核对代码。B-1 / B-2 是本次追加的 P1。
> B-11（默认模板双份）已并入 M-1；另有两项低价值问题未列入
> （manifest 的 name/version 未单行化就进日志；`CardRenderContext` 对 null Settings
> 静默造默认树，与富内容卡直接 NRE 的行为不一致）。

### B-1【P1】中转站根路径条目绕过接收目录，可能写到盘符根、Move 时递归删源

**证据** `src/Plugins/DropStation/StationFileActions.cs:256-266`：
```csharp
string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, ...);
string name = Path.GetFileName(trimmed);
return string.IsNullOrEmpty(name) ? path : name;   // 根目录 → 返回整条路径
```
`UniqueTarget`（`:299-301`）`Path.Combine(directory, fileName)`——`fileName` 若是
`D:\` 这样的绝对路径会**丢弃 directory**；`StationCopyController.cs:194-195`
直接 `Directory.CreateDirectory(context.Target)`；Move 成功分支（`:332`）执行
`Directory.Delete(item.Path, true)`；失败清理 `CleanupPartial`（`:293-301`）也递归删 target。
代码里的 `StationFiles.IsInside` 只用于回收站，构建复制计划时没有任何
"target 必须在接收目录内"的断言。

**影响**：根目录条目（`D:\`）会让目标变成 `D:\ (2)`；Move 成功后递归删除 `D:\` 的内容。
**可达性说明**：Explorer 直接拖盘符通常被系统拦，但 `Ctrl+C` 盘符、手改 `items.json`、
第三方拖放源仍可能送进根路径；无论可达与否，这条断言都该有，按 P1 处理。
**修复**：`ItemName` 对根返回稳定叶名或直接拒收；`BuildFilePlan` / `BuildDirectoryPlan`
断言 `IsInside(context.Directory, context.Target)`；`CleanupPartial` 删除前同样断言；
`StationSettings.Normalize` 把 `Directory` 规范成绝对路径。

### B-2【P1】第一个收纳任务在 UI 线程同步执行

**证据** `src/Plugins/DropStation/CardJobQueue.cs:202-233`：
`await _gate.WaitAsync(job.Token)` 在信号量空闲时同步续跑，紧接着
`await work(job.Token, job)`；`StationCopyController.cs:104-108` 的 work lambda
直接调 `RunCopy(...)` 再 `return Task.CompletedTask`；`RunCopy`（`:154-186`）
建目录、枚举目录树、1MB 循环读写全同步。`RunCopy` 的注释自己写着
"全程同步（与原实现一致：首个任务会在调用线程上直接跑）"。
**影响**：`AddPaths` 在 UI 线程 → `Enqueue` → 首个任务同步跑完——复制 1GB 期间
叠层、设置窗、取消按钮全部无响应，进度 `BeginInvoke` 也排不上队。
**修复**：`RunAsync` 用 `Task.Run` 包住 `work`（信号量等待仍在其中），
或 `Enqueue` 内 `_ = Task.Run(() => RunAsync(...))`；顺手把
`StationCopyController.cs:248` 的 `new byte[1024*1024]` 提到循环外。

### B-3【P2】CommandView 的 finally 会 Dispose 别人的 CTS

**证据** `src/Plugins/Command/CommandCard.cs:244-245` 每次执行
`_cancel = new CancellationTokenSource()`；finally（`:293-307`）里
`if (session == _session)` 只守住 UI 收尾，随后**无条件**执行
```csharp
try { if (_cancel != null) { _cancel.Dispose(); _cancel = null; } } catch { }
```
**影响**：用户取消后重开面板提交新命令，旧 await 的 continuation 可能晚归
（`CommandRunner.WaitOutput` 最多 5 秒），此时 finally 释放的是**新命令**的 CTS 并清空字段；
新命令在 `CommandRunner.cs:206` `token.Register(...)` 抛 `ObjectDisposedException` 直接失败，
此后 `CancelRunning` 也找不到 CTS（新命令不可取消）。
**修复**：用局部 CTS 收尾：
```csharp
var cts = new CancellationTokenSource(); _cancel = cts;
...
finally { try { cts.Dispose(); } catch { } if (ReferenceEquals(_cancel, cts)) _cancel = null; }
```

### B-4【P2】插件设置 DTO 用属性会静默丢配置（Json 只认公共字段）

**证据** `src/Contracts/Core/Json.cs:84`（序列化）与 `:347-352`（绑定）都只
`GetFields(BindingFlags.Public | BindingFlags.Instance)`。内置卡恰好都用公共字段，
契约文档也没有明确写出这条约束。
**影响**：第三方作者按常规写 `public string Path { get; set; }`，落盘得到 `{}`、
读回不回填——设置"看起来保存成功、重启变默认"，全程无日志（违反 §5）。
**修复**：Write / Bind 同时处理公共可读写属性；或对"没有公共字段但有可写属性"的类型
`Log.Warn`；在 `DLL-CARD-PLUGIN-GUIDE.md` 明确 DTO 必须是公共字段，并加一条
`ExtrasAs` / `SetExtras` 往返自检。

### B-5【P2】命令行卡「自定义模板为空」校验永远不触发

**证据** `src/Plugins/Command/CommandSettings.cs:40`：
```csharp
CustomShell = string.IsNullOrWhiteSpace(CustomShell) ? "cmd.exe /c {cmd}" : CustomShell.Trim();
```
`SettingsWindow.Load.cs:294-295` 在保存前先 `s.Normalize(); CardModel.Normalize(s);`
（后者调 `InitializeCard` → `CommandOf` → `Normalize`），此时空模板已被补成默认值；
`CommandCard.Validate` 再读也读不到空串。
**影响**：用户选"自定义"并清空模板 → 保存时被静默改成 `cmd.exe /c {cmd}`，
实际执行的是 cmd 而不是用户预期的工具，设置页也显示默认模板。
**修复**：`Normalize` 只在 `CustomShell == null` 时补默认（空串保留给校验），
或用独立的"用户显式清空"标记；`Validate` 才有意义。

### B-6【P2】对话模式热键切换后，下一次「应用设置」重建对话视图并清空历史

**证据** `ChatCard.cs:370-387` `ToggleMode` → `:409-428` `Persist` 直接改 live
`Extras["chat"].Mode`；`RenderScheduler.cs:236-249` 内容签名 =
`Json.Serialize({Kind, Content, Extras})`；`:336-349` 签名不等 → `ReleaseContent` + 重建；
`SettingsApplier.Apply` 每次对所有卡片走 `RefreshSlot`。
**影响**：按热键切模式后再改任意设置（哪怕只改名字/热键），内容签名与 slot 快照不一致
→ ChatView 重建，`_messages` 清空，与 P0-2"改非外观设置时 AI 历史不丢"的验收目标冲突。
**修复**：契约层给"运行期字段"声明（如 `ICardRuntimeSettingsMerge` 增加可投影的
签名排除项），`RenderScheduler` 算签名时剔除；或切模式后由宿主刷新 `slot.ContentSignature`。

### B-7【P2】中转站 items.json 读取失败时不是只读，下一次写盘会用当前（空）列表覆盖

**证据** `src/Plugins/DropStation/StationStore.cs:307-328` `ReadState` 对任何异常/损坏都
返回 null，只有 FormatVersion 过高才 `_readOnly`；`:218-267` `MergeWithDisk` 读到 null
就只写内存列表。
**影响**：items.json 被占用/权限不足/主文件与 `.bak` 双双损坏时 Load 返回空；
用户随后添加或删除一项 → Persist 覆盖原文件（旧内容只剩 `AtomicFile` 的 `.bak`），
列表静默丢失，仅有一条 WARN "状态解析失败"。
**修复**：`ReadState` 用 `out bool failed` 区分"文件不存在"与"读取/解析失败"，
失败时置 `_readOnly` 并在状态行提示（与 FormatVersion 只读同语义）。

### B-8【P3】重复 / 非法插件被拒绝时跳过 `ICardModuleLifetime`

**证据** `CardPluginLoader.cs:528-540` 正常卸载先调 `OnPluginUnloading()`；
`:585-595` `Discard` 只 `context.Unload()` + `Release()`。
**影响**：同 id / 同 Kind 的重复插件（用户把插件复制两份）构造期启动的线程/定时器
收不到卸载回调，留在已请求卸载的 ALC 里继续跑，直到进程退出。
**修复**：`Discard` 按 `Unload` 的顺序先调 lifetime（try/catch + 日志）再卸 ALC。

### B-9【P3】对话设置页的地址提示与运行时安全闸门不一致

**证据** `ChatCardSettingsView.xaml.cs:166-182` 只判断 `StartsWith("http")`，
并展示"请求发到 {BaseUrl}/chat/completions"；`ChatCard.cs:278-300`
用 `Uri.TryCreate` + 带 Key 时仅 https/回环。
**影响**：用户填公网 `http://` + Key，设置页显示一切就绪，卡片运行时却拒绝发送；
两边口径不同，用户无法判断该信哪边。
**修复**：设置页复用 `ChatCard.IsSafeEndpoint(BaseUrl, hasKey)`（提升为静态方法），
提示文案与卡片一致。

### B-10【P3】`Settings.Normalize` 不校验 `OutlineColor`（S1/S2 同类遗漏）

**证据** `src/Contracts/Core/Settings.cs:89-112` 只校验 Foreground/Background 与颜色令牌；
`OverlaySurface.cs:151` `GetBrush(style.OutlineColor, 1.0)` → `ParseColor` 默认白色。
**影响**：手改 settings.json / 旧主题写坏描边色 → 描边静默变白，无日志。
**修复**：按 Foreground 的方式补一条 `if (!IsHexColor(Style.OutlineColor))` 的 Warn + 回落。

### B-12【P3】`CardJobQueue` 位置与文档承诺不一致

`docs/cards-architecture.md` 的"共享控件"表列出 `CardJobQueue`（串行可取消后台任务），
但类实际在 `src/Plugins/DropStation/CardJobQueue.cs`，Contracts/UiKit 里没有。
第三方插件无法复用，要排队只能再写一份。
**修复**：移到 UiKit；DropStation 只留 `StationCopyController` 等业务层。

### B-13【P2】`MediaCardSettingsView.RefreshDecoders` 是未兜底的 `async void`（S4 同类回归）

`src/Plugins/Media/MediaCardSettingsView.xaml.cs:271-276`：
```csharp
public async void RefreshDecoders()
{
    List<DecoderInfo> found = await Decoders.DetectAsync(true);
    Log.Info("手动重新检测解码器：" + Decoders.Describe(found));
    Raise("DecoderHint");
}
```
`DetectAsync` / 枚举抛异常，或 await 期间设置窗关闭后 `Raise` 抛异常，都会从
`async void` 逸出到 Dispatcher；非白名单类型会走"不可信状态 → Shutdown(1)"。
**修复**：拆 `async void` 外壳 + `Core()`，catch 记日志并写状态行（与 ChatView / CommandView 同模式）。

### B-14【P3】中转站 `Load` 在 UI 线程对每个条目做同步 `Exists`

`StationStore.cs:61-67` 逐项 `item.Missing = !item.PathExists()`（两次同步 IO）；
`StationView` 构造函数在 `:136` 调 `LoadItems()` → `_store.Load(...)`。
**影响**：条目含网络路径/离线盘时，MaxEntries 500 × 2 次同步 IO，卡片创建
（启动、应用设置）可卡数秒到数十秒。
**修复**：加载时只做轻量标记，存在性检查移到后台线程，或首屏只检查前 N 条。

### B-16【P3】清空 / 移除与在途复制竞态，产生孤儿文件

`StationView.cs:558-567` `ClearAll` 清空 `_items` 并 `Persist`；`:545-556` `RemoveSelected`
移除选中项；`StationCopyController.CompleteCopy`（`:321-327`）完成后只改
`item.StoredPath`，不保证条目还在 `_items`。
**影响**：收纳过程中点"清空"/"从列表移除"，复制完成后目标已落盘但列表里没有 →
孤儿文件；Move 模式源已删、目标不在列表，用户找不到文件。
**修复**：完成回调里若 item 不在 `_items`，走 `MergeCompletedAfterDispose` 式单条合并，
或提示"已收纳但已从列表移除"。

### B-17【P3】`CardModule` 注释与实际加载方式不符

`src/Contracts/Cards/CardModule.cs:9-12` 仍写"构造时由 `CardRegistry` 反射发现"，
但 2.14 起内置卡与第三方卡都由 `CardPluginLoader` 按 manifest 加载；
`CardRegistry.Discover` 只扫宿主程序集（当前为空）。
**修复**：注释改为"由下载器按 manifest 加载"，避免新作者按旧注释找错入口。

---

## 附：本次审阅没有做的事

- 没有在真机多显示器 / 混合 DPI 环境实测玻璃与定位（L-2/L-13 的"副屏"结论来自代码路径推导，
  L-13 已标注需实测）；
- 没有运行 UI 手动回归（仅 `--selftest` + 现有运行日志）；
- 没有做 IL 级性能采样，性能数字均为代码级估算（数量级，不是实测 ms）；
- 6 张内置卡的业务逻辑由第二轮专项审阅覆盖（见 §9，B-1～B-17 均逐行核对）；
  Chat 请求构造与命令行脱敏规则仍为交叉抽查。
