# 评审 P2 修复记录（v2.13.4）

> 来源：`MirrorpondHUD-2.12.2-review.md` 的 P2 列表。
> 本轮只处理**有实际影响**的条目（泄漏 / 卡死 / 内存 / 密钥 / 指标正确性 / 资源生命周期）；
> 纯结构、测试工程、供应链与低风险项不纳入，见文末。

## 已修条目

| 条目 | 修复 | 主要文件 | 验证 |
|---|---|---|---|
| P2-2 进程令牌句柄泄漏 | `OpenProcessToken` 的 token 在 `finally` 里 `CloseHandle` | `src/Core/ProcessIntegrity.cs` | 编译；启动日志完整性级别正常 |
| P2-3 热键注册失败丢旧绑定 | `Register` 先解析 + 尝试新注册，成功后才摘旧动作；失败保留旧绑定；共享组合键不重复注册 | `src/Ui/HotkeyManager.cs` | 编译；现有 F8/F9/F10/F11 热键自检 |
| P2-4 窗口关闭后定时器泄漏 | `Closed` 停 `_topmostTimer` / `_glassTimer` 并置空，再释放底板 | `src/Ui/OverlayWindow.xaml.cs` | 15 秒冒烟关闭正常 |
| P2-5 命令输出无上限 | stdout/stderr 各保留尾部 1 MB，环形缓冲 + 读空管道；`CommandResult` 增加截断标志；显示只切尾部行并提示截断 | `src/Text/CommandRunner.cs` | 自检「命令安全」：尾部保留/截断提示 |
| P2-6 命令原文写入日志 | 日志只记工具、目录、命令长度、脱敏摘要（Authorization/Bearer/token/password 等 → `***`，≤80 字） | `src/Cards/CommandCard.cs`、`CommandRunner.cs` | 自检「命令安全」：`sk-secret` 不出现在摘要 |
| P2-7 主题导入无上限 / UI 卡顿 | 主题文件 1 MB 上限、拒绝 `\\.\` 设备/管道路径；设置页导入改后台读/解析 + 回 UI 应用、防重入 | `src/Core/ThemeFile.cs`、`src/Ui/SettingsWindow.xaml.cs` | 自检「主题文件校验」含超 1 MB 拒绝；正常导入往返 |
| P2-10 任务队列累积 / Dispose 竞态 | 已结束 job 只保留最近 16 个；Dispose 延迟到 in-flight 归零再释放信号量；`acquired` + ODE 兜底 | `src/Core/CardJobQueue.cs` | 自检「后台任务」含 20 个任务保留上限 + 入队即 Dispose |
| P2-11 播放覆盖系统音量 | 默认 100% 完全不写设备音量；显式 <100 时先 `waveOutGetVolume` 保存、收尾恢复；读不到原值则不覆盖 | `src/Media/WaveOutPlayer.cs` | 代码审查；真机声卡需人工听/看音量 |
| P2-17 PDH 合法状态被丢 | 接受 `PDH_CSTATUS_VALID_DATA=0` 与 `PDH_CSTATUS_NEW_DATA=1`，其它状态才跳过 | `src/Metrics/PdhQuery.cs` | 自检采样线程启动 + 15 秒冒烟 |
| P2-18 Ffmpeg `_stop` 兼作重启代号 | 引入 `_run` 代次；读帧/重开/进程安装/Kill 全代次校验；旧线程晚归不杀新进程、不报假失败；音频进程过期也收掉 | `src/Media/FfmpegPlayer.cs` | 编译 + 媒体卡冒烟 |

## 验证

- `dotnet build -c Release`：0 警告 0 错误（2.13.4）。
- `--selftest`：失败项 0；新增/扩展「命令安全」「主题文件校验」「后台任务」。
- `--smoke 15 --smoke-visible`：退出码 0；媒体卡、采样、热键、托盘、启动动画正常。
- 并行队友额外 harness（仓库外，已删除）：3.3 MB 输出精确截为最后 1 MB、超时/取消、脱敏、UTF-8 截断边界、主题超限/管道拒绝、正常主题往返共 19/19 通过。

## 未纳入本轮

- P2-1 GPU 索引：当前代码已先排序再按下标取值，报告中的行号与现状不符，未发现可复现错配；
- P2-8 `SelfTest` 巨石 / 独立测试工程：结构性工程，不在“有价值修复”范围；
- P2-9 空 catch 分级：涉及面大、纯维护性；
- P2-12 DPAPI 明文回退：已在 P1-4 修复；
- P2-13 命名空间/目录、P2-14 kind 硬编码、P2-15 ButtonsCard、P2-16 自签名证书脚本：结构/供应链，未动。

## 剩余风险

- P2-10：work 永不结束（无视取消）时信号量会延迟释放到进程退出；
- P2-11：<100 音量路径仍会临时改设备级音量（有恢复）；进程被强杀时可能来不及恢复；
- P2-18：重启瞬间仍可能有一帧旧代回调的微竞态；旧读线程卡管道时 `Join(1500)` 窗口与原来一致；
- P2-7：特殊路径若 `Read` 永久阻塞只挂后台线程，UI 不卡，导入入口在期间禁用。
