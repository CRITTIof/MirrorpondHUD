# 2.13.4 审阅意见修复记录（v2.13.5）

> 来源：`MirrorpondHUD-2.13.4-review.md`。
> 本轮处理 P1 与“先修”高风险项；结构性/纯维护项与部分 P2 留到后续。

## 已修

| 审阅项 | 修复 | 主要文件 |
|---|---|---|
| §1 中转站销毁后完成变孤儿 | 视图销毁后复制成功仍按 Id 合并写 `items.json`，不碰 UI；Move 模式销毁后只补状态不删源 | `src/Cards/DropStationCard.cs` |
| §22 中转站合并截断方向 | 按 `AddedUtc` 旧→新稳定排序，从头部裁最旧；写盘成功后才更新 `_loadedIds`，失败保留基线重试 | `src/Cards/DropStationCard.cs` |
| §2 热键路由抵消旧绑定保护 | `HotkeyRouter.Register` 不再先 `UnregisterAll`；逐动作先注册新键，失败保留旧键并提示；`DetachStale` 清理删卡/停用动作 | `src/Ui/HotkeyRouter.cs` |
| §3 Chat 设置窗旧快照 | 保存前按“用户编辑优先、未编辑字段取 live”合并 Chat Mode / 呼出热键 / 全局热键 / 卡片动作热键；“恢复默认值”跳过合并 | `src/Ui/SettingsWindow.xaml.cs` |
| §5 DPAPI 失败清空旧 Key | `SetApiKey` 加密失败保留旧 Key，只提示新 Key 未保存 | `src/Core/Settings.cs` |
| §5 明文 `.bak` 先删后保存 | 改为保存成功后再清洗 `.bak` / `.broken-*` / `.tmp`，失败时保留唯一恢复副本 | `src/Core/ConfigStore.cs` |
| §7 缺版本/溢出走 legacy | 缺 `SchemaVersion` 时按卡片形状识别当前格式；版本号范围校验；未来版本只读加载且 `Save` 拒绝覆盖 | `src/Core/SettingsMigration.cs`、`ConfigStore.cs` |
| §8 Replace 回退备份失败仍覆盖 | `File.Copy` 备份失败即中止并返回 false；`ConfigStore` / `CardStateStore` 都修 | `src/Core/ConfigStore.cs`、`CardStateStore.cs` |
| §9 空/缺失主文件不试 `.bak` | 主文件缺失或为空时尝试 `.bak` 恢复，并提示 | `src/Core/ConfigStore.cs` |
| §10 明文清理不完整 | `Settings.Normalize` 遍历所有卡片 chat Extras（非 chat Kind 也加密）；保存成功后清洗 `.bak/.broken/.tmp` | `src/Core/Settings.cs`、`ConfigStore.cs` |
| §11 Save 无锁/固定 tmp | `ConfigStore.Save` 串行化；`CardStateStore.Write` 也串行化 | `src/Core/ConfigStore.cs`、`CardStateStore.cs` |
| §15 状态主文件损坏不回退 | `CardStateStore.Read` 主文件非合法 JSON 时回退 `.bak` | `src/Core/CardStateStore.cs` |
| §6 命令脱敏不完整 | 补空白分隔 `--token value` / `--password value` / `-u user:pass` / `--api-key value` 等；保留既有规则 | `src/Text/CommandRunner.cs` |
| §19 同路径切解码器视频停住 | `ReleaseVideo()` 后再设置 `_pendingVideo`，显式重启；异步探测完成后按 `_playing` 校正 Resume/Pause | `src/Media/MediaView.cs` |
| §20 媒体释放 UI 线程 Join | Ffmpeg 读线程 Join 移后台；WaveOut Stop 用 `Join(0)` 快速路径 + 后台有界收尾，收敛重复 Stop | `src/Media/FfmpegPlayer.cs`、`WaveOutPlayer.cs` |
| §21 媒体次级 | WaveOut 自然 EOF 释放；`FindFfmpeg` 60s 缓存；解码器首启显示“正在检测…”；窗口关闭后不再写玻璃画面 | `WaveOutPlayer.cs`、`Decoders.cs`、`MediaCardSettingsView.xaml.cs`、`OverlayWindow.xaml.cs` |
| §13 ChatView 异常/生命周期 | Send 外层 catch 后恢复输入；`ChatView` 实现幂等 `IDisposable`（停 timer / 取消请求） | `src/Cards/ChatCard.cs` |

## 未纳入本轮

- §4 API 局域网 HTTP 的显式 opt-in（产品决策，需设置项/UI）；
- §12 主题导入超时与 `\\?\` / `\\??\` 路径拦截；
- §17 归一化失败整份重置、I/O 暂时失败与解析失败区分；
- §21 剩余指标项：PDH 枚举 Sleep、`_adapterDirty` 丢更新、“无数据”当 0、NvidiaSmi 退避/Stale；
- §14 命令输出保留首 64KB + 尾 1MB；
- §16 SelfTest 独立测试工程/隔离配置/低完整性环境；
- §13 结构债（SettingsWindow / DropStationCard / SelfTest）与保存返回值上报 UI。

## 验证

- `dotnet build -c Release`：0 警告 0 错误（2.13.5）。
- `--selftest`：失败项 0；配置修复覆盖缺版本当前格式、版本溢出、未来版本只读、非 chat Extras 加密、状态 `.bak`（含坏主文件）。
- `--smoke 15 --smoke-visible`：退出码 0；热键全部注册、启动/退出正常。
- 临时目录实测：
  - `SchemaVersion:999` 只读加载，文件字节未变；
  - 主文件缺失/为空时从 `.bak` 恢复且保留备份；
  - 缺少 `SchemaVersion` 的当前格式补写为 6，chat/media/dropstation Kind 保留；
  - 明文 Key + `.bak/.broken/.tmp` 迁移后主文件只剩 `dpapi:`，含明文旧文件全删。

## 已知剩余风险

- 热键失败恢复依赖进程内 `_effective`，重启后首次注册失败没有旧键可恢复；
- 中转站 Move 模式在视图销毁后完成只补状态、不删源，源文件会保留；
- 媒体 Stop 返回时后台线程可能仍在收尾（最多约 1s；卡死交给 Feed finally/OS）；
- 解码器切换与热键路由主要是代码审查 + 编译，建议真机手动点一次“切换解码器”、改一个被占用热键。
