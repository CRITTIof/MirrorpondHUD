# 性能基线（2.27.0 → 2.29.0）

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

## 2.29.0 A/B（2026-11-11，Windows 11 26200，主屏 1920×1080@100%）

为了排除“卡片数量变了”的干扰，这次用 `git worktree` 检出 2.28.0（`e489af2`）单独构建，
两个版本使用**同一份 4 卡配置**（3 张信息卡 + 命令行；DesktopFeed，`BlurFps=10`），
同样 `--perf 15 --visible`，同一台机器连续跑：

| 指标 | 2.28.0（旧） | 2.29.0（新） | 变化 |
|---|---|---|---|
| TextCanvas Measure / OnRender | 84 / 85 | 104 / 102 | 刷新次数不同，只看每轮成本 |
| BuildLine 总数 | 422 | 204 | **−52%** |
| FormattedText run 总数 | 794 | 510 | **−36%** |
| 每次 Measure+OnRender 的 BuildLine | 2.50 | 0.99 | **−60%** |
| 每次 Measure+OnRender 的 run | 4.70 | 2.48 | **−47%** |
| GlassFeed 取样轮 | 121 | 119 | 基本一致 |
| 像素缓冲分配 | 360（3.36 MB） | **7（0.08 MB）** | **−98%** |
| LOH(≥85KB) | 0 | 0 | 当前尺寸未进 LOH |
| GC 分配 / Gen0/1/2 | 12.35 MB / 2/1/0 | **9.71 MB / 1/0/0** | 分配下降 |

结论：P-1 把 Measure 与 OnRender 的重复排版收敛为一次（每轮 BuildLine 约 2.5 → 1）；
P-2 让像素缓冲分配从“每轮每卡一次”变成“尺寸变化才分配”，分配次数下降两个数量级。

## 4K@300% 实测（2026-11-11，副屏 3840×2160，DPI 288）

把锚点放到副屏（`Overlay.Monitor=1`、`X=Y=100`）后跑 `--perf 12 --visible`：

```text
=== 性能采样结果（12.0 秒）===
TextCanvas: Measure=81，OnRender=79，BuildLine=150，FormattedText run=212
GlassFeed: 取样轮=81，像素缓冲分配=9（0.56 MB，平均 63.9 KB/次），LOH(≥85KB)=4，BitmapSource=193
GC: 分配=9.41 MB，Gen0=3，Gen1=3，Gen2=3，WorkingSet=185.4 MB
```

- 卡片物理尺寸随 300% 放大后单块缓冲超过 85KB，`LOH=4` 是**尺寸变化时的重分配**；
  81 轮取样只分配 9 次，证明 P-2 的复用在大卡 / 高 DPI 下仍然成立（旧实现会随轮次线性增长）。
- 该配置下桌面取样仍回退 GDI：`DesktopFrameSource` 只对主屏输出建 duplication，
  4K 副屏的请求区域会记一条 WARN 并走 GDI（见 `docs/glass-implementations.md` 的 L-2 现状）。
  这属于已知限制，不是本轮 L-13 的定位问题；若要让副屏也走 GPU 玻璃，需要按
  `IDXGIOutput1` 为每个输出各建一个 duplication 实例。
- 定位实测：首卡落在物理 `(2220, 300)`（= 副屏工作区原点 1920 + 100 DIP × 3），DPI=288；
  模拟真实拖动 120px 后锚点变为 `(140, 140)`、`显示器=1`，与 120 ÷ 3 = 40 DIP 一致。
