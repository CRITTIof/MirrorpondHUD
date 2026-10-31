# 评审 P1 修复记录（v2.13.3）

> 来源：`MirrorpondHUD-2.12.2-review.md` 的 P1-1 .. P1-10。
> 本记录只覆盖 P1；P2/P3 与上帝类拆分不在本轮范围。

## 对照表

| 条目 | 修复 | 主要文件 | 验证 |
|---|---|---|---|
| P1-1 顶层块为 null 时 NRE | `Settings.Normalize()` 对 `Style/Overlay/Cli/Data/Behavior/Hotkeys/Cards` 统一判空；`ConfigStore.Load` 的归一化包 `try` | `src/Core/Settings.cs`、`ConfigStore.cs` | 自检「配置修复」null 块；临时目录 `{SchemaVersion:6,` 断言 |
| P1-2 损坏配置静默恢复出厂 | `SettingsMigration.Load` 增加 `corrupted` 输出，区分旧版本与解析失败；`ConfigStore` 备份 `.broken-*`、尝试 `.bak` 恢复 | `SettingsMigration.cs`、`ConfigStore.cs` | 临时目录实测：损坏主文件 → `.broken-*` + 从 `.bak` 恢复 |
| P1-3 `Delete + Move` 非原子 | `ConfigStore.Save` / `CardStateStore.Write` 改临时文件 + `File.Replace`（失败回退备份+覆盖），`Flush(true)`；状态读取回退 `.bak` | `ConfigStore.cs`、`CardStateStore.cs` | 自检「配置修复」状态 `.bak` 回退 |
| P1-4 明文 Key 残留 `.bak` | 旧明文 Key 迁移时 `Save(backup:false)` 并删除旧 `.bak`；启动时扫描并删除仍含明文的 `.bak`；DPAPI 失败拒绝明文（清空而不是落盘） | `ConfigStore.cs`、`Settings.cs`、`SecretStore.cs` | 临时目录实测：迁移后 `.bak` 不存在、主文件含 `dpapi:`、无明文 |
| P1-5 ChatView 缓存旧配置回滚 | `ChatCard.UpdateContent` / `ApplyStyle` → `ChatView.Refresh` 换当前 `Settings` / `ChatSettings` / `ICardHost`；`Persist` 只写宿主当前卡片，旧视图不写盘 | `src/Cards/ChatCard.cs` | 编译 + 代码路径审查；设置应用后的刷新链路由宿主 `UpdateContent` 驱动 |
| P1-6 StationView 生命周期/旧视图覆盖 | `StationView : IDisposable`：`CancelAll`、停 `CardStatus` 定时器、丢弃已销毁视图的 ContinueWith/Dispatcher 回调；`Persist` 加锁 + 按 `Item.Id` 读-合并-写 | `src/Cards/DropStationCard.cs` | 自检创建/销毁视图；现有中转站 Copy/Move/搜索自检 |
| P1-7 WaveOut Stop 释放竞态 | `Stop` 只有 `Join` 成功才 `ClaimCleanup`；Join 超时把释放交给 Feed 线程；`Feed` 在 `Read` 后、复制前、`waveOutWrite` 前复检 `_stop` 和指针 | `src/Media/WaveOutPlayer.cs` | 编译 + 状态机审查（无设备/无音频时不可自动回归） |
| P1-8 外部进程阻塞 UI | 新增 `Core.AsyncProcess`：stdout/stderr 并行异步读、整体超时、`Kill(true)`、有界收尾；Ffmpeg/Decoders/NvidiaSmi 改异步或后台路径；设置页「检测」改 async；`Decoders.Detect()` getter 不再阻塞 | `AsyncProcess`（`NvidiaSmi.cs` 内）、`FfmpegPlayer.cs`、`Decoders.cs`、`MediaView.cs`、`MediaCardSettingsView.xaml.cs` | 编译；设置页检测异步；`Decoders.Detect(false)` 只读缓存 + 后台预热 |
| P1-9 PDH/共享 List 并发 | 删除 `MetricEngine` 共享 `_buffer`，改局部 List；`_pdhGate` / `PdhQuery._sync` 串行访问 PDH；`UpdateSettings` 只置 `_adapterDirty`，UI 不碰 PDH | `MetricEngine.cs`、`PdhQuery.cs` | 自检采样线程启动；15 秒冒烟 |
| P1-10 API URL 安全 | `ChatView.Configured` 用 `Uri.TryCreate`；带 Key 只允许 https，回环（127.0.0.1/localhost/::1）允许 http；非 http 拒绝；提示区分“未配置/不安全” | `src/Cards/ChatCard.cs` | 自检「接口安全」四种 URL；公网 http 被拒 |

## 验证

- `dotnet build -c Release`：0 警告 0 错误（2.13.3）。
- `--selftest`：失败项 0；新增「配置修复」「接口安全」。
- `--smoke 15 --smoke-visible`：退出码 0；媒体卡、采样、托盘、热键、启动动画正常。
- 临时目录手工验证：
  - 损坏 `settings.json`：生成 `.broken-*`，从 `.bak` 恢复且保留 `.bak`；
  - 明文 API Key 迁移：主文件含 `dpapi:`、`.bak` 被删除、无明文。

## 已知剩余

- 设置窗打开期间持有深拷贝，热键改 Chat 模式后再应用设置仍可能被旧快照覆盖（需设置窗协调）；
- WaveOut 若音频管道永久阻塞，释放会推迟到 Feed 线程退出（有意的延迟释放，避免 UAF）；
- `Decoders` 首次冷启动提示可能短暂显示“没找到”，后台预热完成后再次读取可恢复；
- P2/P3（PdhQuery 状态码、Ffmpeg `_stop` 重启代号、播放音量等）未在本轮处理。
