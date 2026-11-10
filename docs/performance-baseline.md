# 性能基线（2.27.0）

> 目的：在优化 `TextCanvas` 与 `GlassFeed` 之前，先留下同一台机器、同一份配置下的可复跑基线。
> 计数器只在 `--perf` 下开启；正常运行时 `PerformanceProbe` 的所有计数方法立即返回，不改变生产路径。

## 复跑方式

```bat
dotnet build -c Release
bin\Release\MirrorpondHUD.exe --perf 15 --visible
```

`--perf N` 会正常起界面，N 秒后把结果写进 `bin\Release\mirrorpond.log` 并退出（退出码 0）。
N 允许 3–120，默认 12。必须使用与基线相同的 `settings.json`：当前配置为 6 张卡片
（系统信息 / 时钟 / 按键 3 张常驻文字卡、命令卡、AI 对话、文件中转站），
毛玻璃为 DesktopFeed，`BlurFps=10`。

## 2.27.0 基线（2026-11-10，Windows 11 26200，DPI 100%）

```text
=== 性能采样结果（15.0 秒）===
TextCanvas: Measure=104，OnRender=102，BuildLine=497，FormattedText run=1746
GlassFeed: 取样轮=112，像素缓冲分配=550（14.52 MB，平均 27.0 KB/次），LOH(≥85KB)=0，BitmapSource=101
GC: 分配=32.15 MB，Gen0=5，Gen1=2，Gen2=1，WorkingSet=163.8 MB
```

## 读数解释

- **TextCanvas 的重复排版成立**：15 秒内 104 次 Measure + 102 次 OnRender，
  共调用 `BuildLine` 497 次、创建 1746 个 `FormattedText` run。Measure 与 OnRender
  各自重建同一行，是当前最明确的 CPU/分配优化目标（P-1）。
- **GlassFeed 的每帧分配成立，但当前尺寸未进 LOH**：112 轮取样、550 次像素缓冲分配，
  约 4.9 次/轮（5 张卡共享一轮），15 秒分配 14.52 MB；平均 27.0 KB/次，
  当前卡片尺寸下没有 ≥85 KB 的 LOH 分配。`BitmapSource=101` 小于取样轮数，
  是因为“和上一帧几乎一样就不重画”的跳过逻辑生效。
  因此 P-2 的“每帧 LOH”在当前配置下**未复现**，但“每帧新建 byte[]”与总分配量已确认。
  验证大卡片 / 高 DPI 时应重新跑本命令，观察 `LOH(≥85KB)` 是否出现。
- GC 的 32.15 MB / 15 秒是包含启动、启动动画与高频文字重画的整段分配；
  Gen2=1 说明这段时间有一次完整回收，优化后应重点比较 Gen0/Gen1 次数与分配量。

## 后续优化验收口径

1. 同一台机器、同一份 `settings.json`、同样 `--perf 15 --visible`；
2. TextCanvas 优化后 `BuildLine` / `FormattedText run` 应显著下降（目标：Measure 与 OnRender 共用一次排版）；
3. GlassFeed 优化后 `像素缓冲分配` 应随尺寸稳定而不随帧数增长（复用缓冲）；
4. 不把 `--perf` 的计数器留在正常热路径上：当前所有计数入口都有 `Enabled` 判断。
