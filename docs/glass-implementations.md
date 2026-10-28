# 毛玻璃：三种实现的取舍（以及为什么默认不推荐）

<a id="中文"></a>

**一句话**：毛玻璃是这个项目里唯一「没有正确答案」的功能。三条实现都能跑，各有硬伤，所以它**默认关闭**，
也不建议为了观感去开。只想要半透明卡片的话，把「背景不透明度」调到 0.5–0.7 就够了，没有任何额外开销。

> 本文只讲**现在**这三条路。历史调研（WGC / WinUI 3 / DWM 文档化材质逐项试错）没有随仓库发布。

## 1. 三条路

| | 系统亚克力（默认） | 兼容（截屏取样） | 桌面亚克力（DWM 底板） |
| --- | --- | --- | --- |
| 做法 | 卡片下面一个**从不绘制的 HWND**，挂未文档化的 `WCA_ACCENT_POLICY = ACRYLICBLURBEHIND`，DWM 在那个矩形里实时模糊 | 枚举卡片矩形内的外部窗口 → `PrintWindow` 逐层取图 → 按像素取「盖住这点的最上层」→ 缩小、多次盒式模糊 | 卡片下面一个**非分层**的 HWND，挂文档化的 `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` + DWM 圆角；材质对非活动窗口不生效，所以底板自己 `BitBlt` 截屏 + 模糊（200 ms 一帧） |
| 谁在干活 | DWM / GPU | 本进程（后台线程 + 定时器） | 本进程（200 ms 截屏 + CPU 模糊） |
| 实测 CPU | **1.5–2.2%** 单核，5 张卡（≈ 关闭毛玻璃的 1.6%） | **≈ 41%** 单核，5 张卡 15 fps（纯壁纸背景时 8–14%） | **3.1%** 单核，4 张卡 |
| 模糊程度 | 由系统决定，不可调 | 0–40px 可调，帧率 2–30 可调 | 固定（缩略图上 9px ≈ 原图 18px），不可调 |
| 抓得到什么 | DWM 合成后的屏幕（动态壁纸 / 视频也糊得出来） | 各窗口**自身内容**（`PrintWindow` 抓不到的就没有） | DWM 合成后的屏幕（动态壁纸 / 视频也在） |
| 截图 / 录屏 | 正常 | 正常（不抓屏，也不会把自己拍进去） | **卡片完全不会出现**（自截屏必须把自己排除在捕获之外） |

## 2. 系统亚克力的毛病

1. **未文档化接口**。`SetWindowCompositionAttribute` 是私有 API，微软随时可能改掉。
   调用失败会自动退回兼容实现，但「某天更新完突然变成另一种观感」是这个方案自带的。
2. **圆角卡片会露直角补角**。accent 的模糊区域永远是**窗口矩形**，既不吃 `SetWindowRgn`，
   也不吃 DWM 的圆角偏好。圆角半径超过约 12px 后，卡片四个角外面会露出没有 tint 的方形玻璃：

   ![圆角 24px 时卡片右端的补角](glass-system-corner.png)

3. **亮度来自色调，不来自壁纸**。DWM 亚克力的 alpha 基本被忽略，玻璃的**亮度**由 `GradientColor`
   决定（它只透出背后的色相）。所以深色卡片永远是烟灰色，压在浅色壁纸上会显得发闷。
4. **系统开关直接决定成败**。关闭「透明效果」、远程桌面、高对比度都会退化成纯色，需要自动回退。
5. **每张卡多一个 HWND**，且 Win10 上只有理论可用性，没有真机矩阵验证。

![深色卡片压在棋盘 + 红块上的系统玻璃](glass-system-dark.png)

*深色卡片（`#0D1117`，不透明度 0.30）：棋盘被糊成柔和明暗带（说明真的在模糊），红块的色相透得出来，
但亮度基本不变 —— 第 3 条就是这个意思。*

## 3. 兼容实现的毛病

1. **CPU 是实打实的**。每帧对每个窗口做一次 `PrintWindow`，再逐点合成、缩小、模糊。
   卡片越多、玻璃越大、身后的窗口越大，开销线性上升（上表：41% 单核）。
2. **取的是各窗口自身内容，不是合成后的画面**。被遮挡的部分取不到，窗口之间的层级只能靠枚举出来的
   z 序近似。
3. **抓不到的东西**：本进程自己的窗口（设置窗口、视频窗）不参与；动态壁纸（Wallpaper Engine 这类
   D3D 交换链窗口）`PrintWindow` 返回全黑，只能退回静态壁纸；独占全屏 / 硬件覆盖层同理。
4. **有延迟感**。15 fps 在拖动窗口或滚动网页时看得出来，调高帧率只会更贵。

## 4. 桌面亚克力底板：能用，但截图里看不到卡片

这条是从 `C:\Coding\acrylic-multiwindow` 移植的：每张卡片下面一块**非分层**的底板窗口，挂**文档化的**
`DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW`（Windows 11 桌面亚克力）加
`DWMWA_WINDOW_CORNER_PREFERENCE` 圆角。它在那边成立，在这里只能算「能用」，原因有三条：

1. **DWM 只给「当前活动窗口」画这层材质。** 卡片是分层窗口，底板又永远不会成为活动窗口，
   所以底板在屏幕上的样子是一块 **`#D3D3D3` 的圆角灰板**，背后没有模糊：

   ![DWM 桌面亚克力底板单独显示时的样子：一块灰板](glass-dwm-plate.png)

2. **于是底板自己截屏。** 这是 multiwindow 项目的 `PersistentAcrylic` 思路：`BitBlt` 屏幕对应区域
   → 缩到 1/2 的 DIB → 两遍方框模糊 → `StretchBlt` 画回窗口，200 ms 一帧。糊出来的内容长这样
   （卡片压在红 / 绿色块上，半边红半边绿被糊成渐变）：

   ![底板截到并模糊后的内容](glass-dwm-sample.png)

   > **这里踩过一个坑**：DIB 建好之后必须 `SelectObject` **选进内存 DC**，否则 `StretchBlt` 画在 DC
   > 默认的 1×1 单色位图上，采样永远是**全黑**。第一版漏了这一步，看起来就像「DWM 不支持」，
   > 补上之后采样亮度 90–250，玻璃正常。

3. **必须把自己排除在捕获之外。** 否则截屏会把底板 / 卡片的上一帧拍回玻璃里，形成拖影。
   所以底板和卡片都设 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`。

**这就是这个方案真正的代价，也是它的定义性缺点**：只要它在生效，**卡片就不会出现在任何截图 / 录屏里**
—— 截图工具、录屏软件、发给别人看的截图，都只有卡片背后的桌面。这不是 bug，是「自己截屏」这条路
绕不开的取舍。

其它已知限制：

- 进程侧每 200 ms 截屏 + 模糊：4 张卡 ≈3.5% 单核（系统亚克力 1.6%，兼容实现 41%）；
  整叠卡片区域只从屏幕读一次，各底板再从内存里取自己那一块（一次屏读 ≈ 一个 vsync）；
- 切窗口 / 最小化 / 弹窗时不只是等下一轮定时器：挂 `SetWinEventHook` 监听
  `EVENT_SYSTEM_FOREGROUND` / `MINIMIZE` / `MOVESIZEEND` / `SWITCHEND` 与 `OBJECT_SHOW` / `HIDE`，
  一有窗口栈变化立刻补帧（+40 ms 再补一帧）。实测玻璃落后屏幕：**30 ms**（改之前 163 ms）；
- 模糊半径固定（缩略图 9px ≈ 原图 18px）、刷新固定 200 ms，不像兼容实现那样可调；
- 底板的圆角是 DWM 的 8px，和卡片自己的圆角（默认 10px）不完全一致，角上会多出一点玻璃；
- 只截卡片这一块屏幕，卡片移动时要重截（已经这么做了），所以拖动时玻璃会比系统实现晚半拍；
- 卡片名下的子窗口（视频窗是 owner 为卡片的独立窗口）也会一起排除，所以这个模式下**视频同样不进截图**。

### 4.1 「玻璃每隔一会儿从上到下暗一下」

用户报告过：这个模式下玻璃会**周期性地从上到下变深一下**。查了一圈，结论是**没能在本机复现、也没能定位**，
把已经排除掉的部分记在这儿，免得下次重新踩：

| 怀疑对象 | 怎么测的 | 结论 |
| --- | --- | --- |
| 取样图本身带进了暗带（自截或合成不完整） | `MIRRORPOND_RAW_TRACE=1`：每轮把刚读下来的屏幕图按 8 条水平带统计亮度写进日志，静置 12 秒 | 各带亮度只在 ±1–2 内抖，**没有**任何一条带出现跳变 |
| 绘制（`StretchBlt` 2× 放大）是渐进的，DWM 合成到一半 | 单独做个复刻底板（同样的窗口标志 / DWM 属性 / 半分辨率 DIB）每 200 ms 重画，用 `GetDC(hwnd)` 以 ~2 kHz 采样自己窗口表面 | 29 次重画**每次都是整幅同时变**，没有缝 |
| 读屏和 vsync 没对齐，读到了半帧 | 同一区域 A/B：直接 `BitBlt` vs `DwmFlush()` + `BitBlt` | `DwmFlush` 是**净损失**：读 16.6 ms → 14.3 + 19.5 ms |
| 整幅重画本身触发了 DWM 重算 | 见下面两条缓解 | 顺手做了，静置时重画次数下降 |

两条不伤行为的缓解已经进了代码：

- 帧和上一帧差 ≤2（模糊图上肉眼看不出）就**不重画**，静置时底板不再每 200 ms 抖一次，
  DWM 也就不用每 200 ms 重算一次「排除窗口背后那块干净区域」；比较是跟**上次真正画出去的那帧**比，
  慢漂移会累积到超过阈值，不会卡死；
- 帧改为**同步画上去**（`GetDC(hwnd)` + `StretchBlt`），不再走「`InvalidateRect` → 等消息循环 →
  WM_PAINT」，少一个「窗口已失效但还没重画」的中间状态。

改完：玻璃落后屏幕仍是 30 ms（未退化），静置 CPU 4.8% → 3.5%。如果这个暗一下还在，
**下一刀就是把取图从 GDI 换成 DXGI 桌面复制**（下面 4.2 的验证结果），那是唯一能改掉「取图」这条路的办法。

诊断开关：`MIRRORPOND_GLASS_DEBUG=1`（每 5 秒一次取样统计 + 导出 BMP）、
`MIRRORPOND_GLASS_TRACE=1`（每画一帧往 `%TEMP%\mirrorpond-trace.log` 写一行亮度，
`hud-test/compare.py` 靠它算延迟）、`MIRRORPOND_RAW_TRACE=1`（每轮原始取样图的逐带亮度）。

### 4.2 GPU 能不能加速：能，但要换的是「取图」不是「模糊」

结论先说：**把模糊搬到 GPU 毫无意义，把取图搬到 GPU 有意义**。实测（i7 + GTX 1660 SUPER，区域 400×290 → 缩略图 200×145）：

| 阶段 | 现在（GDI） | DXGI 桌面复制 |
| --- | --- | --- |
| 从合成器拿到像素 | `BitBlt` 屏幕 **9–17 ms**，且与区域大小无关（32×32 到 1600×900 都是 ~16 ms，是同步开销不是拷贝开销） | `AcquireNextFrame` **0.09 ms**（帧已在，无需等合成） |
| 取到自己的区域 | 含在上面 | `CopySubresourceRegion` ≈ 0.00 ms |
| 拷回 CPU | 含在上面 | 400×290 回读 **0.39 ms** |
| 两遍方框模糊（200×145） | CPU **0.61 ms** | 原型的两趟 compute dispatch 0.14 ms（**原型输出仍有 bug，这个数不当结论**） |

也就是说：滤镜只占 0.6 ms，**给它上 GPU 最多省 0.5 ms**；而取图占 9–17 ms，换掉它整个一轮从 ~10 ms 掉到 ~1 ms，
并且 UI 线程不再每帧被卡一个 vsync。桌面复制还有两个关键前提，都已经验证：

- **它同样遵守 `WDA_EXCLUDEFROMCAPTURE`**：复制出来的帧里，卡片和底板所在区域是**卡片背后的桌面**，
  不是卡片、也不是黑块 —— 所以它能直接替换现在的 `BitBlt`，玻璃语义不变；
- 它给的是**整屏**纹理（1920×1080 `B8G8R8A8_UNorm`），取卡片区域用 `CopySubresourceRegion` 即可。

### 4.3 已落地：2.4.2 起取图默认走桌面复制

`src/Ui/DesktopFrameSource.cs`：手写 vtable 调用的 COM 互操作（**没有引入任何 NuGet 包**），只做三件事——
`DuplicateOutput` 拿到复制对象、`CopySubresourceRegion` 把**卡片整叠区域**拷进一块区域大小的 staging
纹理、`Map` 后逐行 `RtlMoveMemory` 进共享 DIB。GDI `BitBlt` 保留为回退路径。

实测（同一台机器、同一个桌面场景，日志里的 `RAWB ... src=dup|gdi read=x` 就是这一轮取图耗时）：

| | GDI 读屏 | 桌面复制 |
| --- | --- | --- |
| 一轮取图 | **18.3–18.6 ms**（挡住 UI 线程等 vsync） | **0.1 ms**（桌面没变化，只是问一句）/ **1–1.5 ms**（有新帧：拷区域 + Map + 搬行） |
| 玻璃落后屏幕（翻色面板放在卡片下面，700 ms 一次） | **46 ms** 中位 / 48 p90 | 47 ms 中位（**没变**）/ 配 100 ms 刷新 **16 ms** 中位 / 52 p90 |
| CPU（4 张卡，静置） | 4.2% | 6.8%（刷新从 200 ms 提到 100 ms） |
| 内存 | 134 MB | 152 MB |

**要诚实的一点**：换成桌面复制本身**并不会让玻璃更快** —— 一轮里「什么时候开始取图」由刷新定时器和窗口事件
决定，取图是 1 ms 还是 18 ms 都落在同一个 40–50 ms 的窗口里。它真正换来的是两件事：

1. **UI 线程不再每轮被堵 18 ms**（按 9 轮/秒算就是 17% 的墙钟时间）。拖动卡片、在对话卡里打字时不卡；
2. 取图便宜了，**刷新可以提到 100 ms**（`fromDuplication` 时自动提），这才是把 46 ms 压到 16 ms 的原因。

安全措施（都在代码里）：

- **按系统生成号选 vtable 下标**：本机 26200 的 `IDXGIOutput1::DuplicateOutput` 在 **22**，而 26100 SDK 头文件写的是 19
  （用 19 只会得到 `DXGI_ERROR_INVALID_CALL`）。低于 26200 的生成号直接走 GDI，不猜；
- 拿到复制对象后**先用 `GetDesc` 验收**宽高是否等于屏幕，对不上就整体退回 GDI（宁可慢，不画黑屏）；
- `DXGI_ERROR_ACCESS_LOST`（独占全屏 / 换分辨率）→ 重建复制对象，连续失败 4 次就永久退回 GDI；
- 刚建好复制对象时的第一帧是**黑帧**，预热阶段（最多 6 次）把黑帧当「没拿到」丢掉，不触发
  「连续 3 帧全黑 → 退回系统亚克力」的保险丝；
- 5 秒没有取图就释放复制对象和 D3D11 设备（别让 GPU 一直醒着），下次用到再建；
- 开关：`MIRRORPOND_GLASS_SOURCE=gdi` 强制走 GDI（做 A/B），`=dup` 在未验证的生成号上强制尝试。

代价 / 风险：vtable 下标依赖系统版本，新系统要重新实测一遍（现在只在 26200 上验证过，其它生成号自动用 GDI）；
独占全屏切换、远程桌面、混合显卡（画面由核显输出而设备建在独显上）都会退化到 GDI；
桌面复制会定期把 GPU 从低功耗叫醒，电池机器上要实测；内存多 18 MB（D3D11 设备 + 区域 staging）。

验证用的原型留在 `C:\Users\CRITT\Documents\deepseek-harness\coding\` 里，不进仓库：
`gpu-lab/`（Vortice 版：复制 + 计时 + compute 模糊链路）、`hud-test/dxprobe/`（无依赖的 vtable 版 + GDI 亮度对照）、
`hud-test/e2e-behind.sh` + `switch-behind.ps1`（把翻色面板放到卡片下面量端到端延迟）。


安全网：连续 3 帧采样全黑（比如系统以后又把截屏挡掉）会**自动退回系统亚克力**，日志写明原因：

```
[INFO] 毛玻璃改用 DWM 桌面亚克力底板：底板自己截屏模糊，卡片与底板都会从截图里消失
[WARN] DWM 桌面亚克力底板不可用（底板截屏全黑：…），毛玻璃退回系统亚克力底板
```

### 4.4 2.4.3：取图方式变成设置项；修掉「玻璃闪一下」

**设置项**：设置 → 外观 → 高级（实验性）→「取图方式」＝ 自动 / GPU 加速（DXGI 桌面复制）/ CPU 读屏（GDI）。
只对「桌面亚克力」生效 —— 系统亚克力不抓屏、兼容实现走自己的拼图路线，所以那两种模式下这一项是灰的。
环境变量 `MIRRORPOND_GLASS_SOURCE=gdi|dup` 仍然优先级最高，留着做 A/B。改完不用重启：
`AcrylicBackdrop.ConfigureSource` 会把现有复制对象丢掉，下一轮按新选择重建。

实测（同一台机器）：

| 设置 | 启动日志 | 一轮取图 | 自动刷新间隔 |
| --- | --- | --- | --- |
| 自动 / GPU 加速 | `玻璃取图改用 DXGI 桌面复制（GPU 加速，不再用 GDI 读屏）` | 0.1–1.5 ms | 100 ms |
| CPU 读屏 | `玻璃取图用 GDI 读屏：设置里选的是「CPU 读屏」（GDI BitBlt）` | ~18 ms | 200 ms |

**「闪一下」的根因：`FinishFrame` 里两步的顺序反了。**

```csharp
var buffer = new byte[w * h * 4];
Marshal.Copy(_dibBits, buffer, 0, buffer.Length);   // DIB 里是这次 StretchBlt 的「未模糊原始取样」
GlassCapture.BoxBlur(buffer, w, h, BlurRadius, true);   // 模糊只发生在 buffer 上
if (SameAsLastFrame(buffer, w, h)) return;          // ← 判同帧直接返回，DIB 里还是原始取样
Marshal.Copy(buffer, 0, _dibBits, buffer.Length);   // 只有「要重画」时才把模糊图写回 DIB
Paint();
```

桌面静置时每一轮都会判成「同帧」，于是 DIB 里**长期留着未模糊的图**；而底板的 `WM_PAINT` 会无条件
`Paint()`（把 DIB 整块贴到窗口上）。卡片内容每秒刷新、每 2.5 秒重申置顶、遮挡变化、`SetWindowPos`
都会发 `WM_PAINT` —— 玻璃就突然从模糊变成清晰（肉眼就是闪一下），而且**一直停在清晰状态**，
直到背景变化把模糊图贴回去。这个坑是 2.4.1 的「同帧不重画」优化引入的。

修法（`src/Ui/AcrylicBackdrop.cs`）：

1. **模糊图永远先写回 DIB**，再判同帧；判同帧只跳过「贴图」这一步 —— DIB 里不可能再出现原始取样；
2. `Follow` 在位置没变时给 `SetWindowPos` 加 `SWP_NOREDRAW`：不再每秒给底板塞一次重画；
3. `Paint` 贴完 `ValidateRect`，同一帧不会被 `WM_PAINT` 再贴一遍。

测量方式：`MIRRORPOND_GLASS_PAINT_TRACE=1` 让每次贴图记一行 `PAINT who=frame|paint ... sharp=`，
sharp = 相邻像素亮度差的平均值（模糊图远小于原图），把一张文字底板垫在卡片下面做对照：

| 底板尺寸 | 修复前 frame / WM_PAINT | 修复后 frame / WM_PAINT |
| --- | --- | --- |
| 204×30 | 1.26 / **27.61（22 倍）** | 1.23 / 1.23 |
| 190×46 | 1.00 / **21.46（21 倍）** | 0.97 / 0.97 |
| 93×30 | 0.56 / **11.02（20 倍）** | 0.56 / 0.56 |
| 68×20 | 1.00 / **25.03（25 倍）** | 1.00 / 1.00 |

同一个二进制里，`WM_PAINT` 那次贴图比「刚模糊完」那次清晰 20–25 倍 —— 那就是未模糊原图；修完两者完全一致。
顺带 CPU 也降了（4 张卡静置 20 秒平均，占整机 / 占单核）：修复前 0.95% / 7.6%，修复后 **0.55% / 4.4%**
—— 不再被 `WM_PAINT` 反复整块重贴。

### 4.5 2.4.4：模糊图改画进卡片内部，去掉底板窗口

用户报了两个毛病，根子是同一个：**模糊图原来画在一块独立的底板窗口上**，而那块窗口和卡片是
两个被 DWM 分别合成的窗口。

1. 「卡片圆角外面多出一圈更大的透明圆角」：卡片圆角是 10 px（`Overlay.CornerRadius`），底板窗口的圆角
   由 DWM 决定，两者半径不同，卡片圆角以外就露出了底板的边；
2. 「每张卡片顶部周期性突然变深」：底板比卡片高/偏 1–2 px 时，卡片顶边以上会露出一条底板
   （下面是卡片、上面是底板，看起来就是顶部突然变深）。这类错位只有在两个窗口分别合成时才可能发生。

改法：**不要底板窗口了**。`src/Ui/GlassFeed.cs` 取代 `src/Ui/AcrylicBackdrop.cs`（删掉约 1300 行窗口代码），
取样循环（DXGI 桌面复制 / GDI 回退、区域一次读、缩小、方框模糊）原样保留，但最后一跳从
「GDI `StretchBlt` 到窗口 DC」改成 **「`BitmapSource` → 卡片自己的玻璃层」**：

```csharp
BitmapSource image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, buffer, w * 4);
image.Freeze();
target.Surface.SetGlass(image);      // 卡片内部的 Border + ImageBrush，CornerRadius 与卡片一致
```

这正是兼容实现早就在用的通路（`OverlaySurface.SetGlass`），所以：

- 圆角由卡片自己的 `Border.CornerRadius` 裁剪，**和卡片背景/文字在同一次合成里**，不可能错位；
- 没有第二块窗口，就没有「顶部露出一条底板」这回事；
- 顺带删掉了底板的 `DWMWA_SYSTEMBACKDROP_TYPE`（DWM 只给活动窗口画材质，这块材质本来就是被盖住的）、
  DWM 圆角、以及「底板不画自己要排除在截屏之外」的一整套逻辑。

**新增设置**（设置 → 外观 → 高级（实验性），只在「桌面亚克力」下出现）：

| 设置项 | 默认 | 说明 |
| --- | --- | --- |
| 模糊强度 | 18 px | 按原始分辨率算；取样图缩 1/2 后按 1/2 半径模糊 |
| 刷新帧率 | 10 帧/秒 | 每秒重新取一次屏幕并模糊；桌面静止时降到 5 帧能省一半 CPU |
| 取图方式 | 自动 | GPU 加速（DXGI 桌面复制）/ CPU 读屏（GDI）/ 自动 |

CPU（4 张卡，20 秒平均，占单核）：**5 帧 3.4%、10 帧约 6–8%、15 帧 7.7%**，基本随帧率线性。
比 2.4.3 的底板方案（10 帧 4.4%）高一些 —— 差别在于现在每帧要经 WPF 重画卡片，而不是 GDI 直接贴窗口，
换来的是圆角/暗带两个问题从结构上消失。

验证（本机，`MIRRORPOND_GLASS_TRACE=1`）：

```
[INFO] 玻璃取图改用 DXGI 桌面复制（GPU 加速，不再用 GDI 读屏）
[INFO] 桌面复制已就绪：pitch=1280，区域 1446,260 314x60
[INFO] 玻璃画面已画进卡片：157x30（模糊半径 9px，取图后端 DXGI 桌面复制）
```

`EnumWindows` 里已经找不到 `MirrorpondHUD.AcrylicBackdrop` 类的窗口了。

## 5. 所以要怎么选

- **先别开**。需要「卡片像玻璃」的观感，只调「背景不透明度」就能拿到 90% 的效果，CPU 为 0。
- 一定要开、又要能截图：用**系统亚克力**（默认），并把圆角控制在 12px 以内。
- 圆角必须很大、或者需要在 Win10 / 远程桌面上稳定一致：切**兼容**，把帧率降到 8–10、
  模糊 8–12px，并且只留一两张卡片开玻璃。
- 想糊到**动态壁纸 / 视频 / 硬件加速窗口**（兼容实现抓不到），并且**接受卡片不进截图**：
  才用**桌面亚克力**。
- 切换位置：设置 → 外观 → 高级（实验性）→「玻璃实现」。

---

<a id="english"></a>

# Frosted glass: the three implementations, and why it stays off by default

**Short version:** frosted glass is the one feature in this project with no clean answer. All three paths run,
each with flaws this project cannot fix, so it ships **disabled**. If all you want is a translucent card, set
*Background opacity* to 0.5–0.7 — that costs nothing.

This page covers the three paths that exist **now**; the older research (WGC, WinUI 3, the documented-DWM-material
experiments) is not published with the repository.

## 1. The three paths

| | System acrylic (default) | Compat (screen sampling) | Desktop acrylic (DWM backdrop) |
| --- | --- | --- | --- |
| How | A never-drawn HWND under each card with the undocumented `WCA_ACCENT_POLICY = ACRYLICBLURBEHIND`; DWM blurs that rectangle live | Enumerate the windows behind the card → `PrintWindow` each of them → pick the topmost layer per pixel → downscale and box-blur | A **non-layered** HWND under each card with the documented `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` plus DWM rounded corners; the material does not apply to inactive windows, so the backdrop `BitBlt`s the screen and blurs it (one frame per 200 ms) |
| Who pays | DWM / GPU | This process (worker thread + timer) | This process (a screen capture and a CPU blur every 200 ms) |
| Measured CPU | **1.5–2.2%** of one core, 5 cards (≈ 1.6% with glass off) | **≈ 41%** of one core, 5 cards at 15 fps (8–14% over a plain wallpaper) | **3.1%** of one core, 4 cards |
| Blur amount | Chosen by the system | 0–40 px, 2–30 fps, both adjustable | Fixed (9 px on the downscaled copy ≈ 18 px at full size) |
| What it captures | The composed screen (animated wallpapers and video included) | Each window's **own contents** (whatever `PrintWindow` refuses is missing) | The composed screen (animated wallpapers and video included) |
| Screenshots / recording | Normal | Normal (it never captures the screen) | **Cards never show up** (a self-capturing window must exclude itself) |

## 2. What is wrong with system acrylic

1. **Undocumented API.** `SetWindowCompositionAttribute` is private; Microsoft may change it in any release.
   Failures fall back to compat automatically, but "the look changed after an update" is baked in.
2. **Rounded cards show square corner slivers.** The accent region is always the **window rectangle** — it
   honours neither `SetWindowRgn` nor DWM's corner preference. Above ~12 px radius the four corners show
   untinted glass: see `glass-system-corner.png` above.
3. **Brightness comes from the tint, not the wallpaper.** DWM acrylic mostly ignores alpha; the gradient
   colour sets the brightness and only the hue comes through.
4. **System switches decide everything.** "Transparency effects" off, remote desktop, or high contrast
   degrade the material to a flat fill and force a fallback.
5. **One extra HWND per card**, and Windows 10 support is theoretical.

## 3. What is wrong with the compat path

1. **The CPU cost is real.** Every frame calls `PrintWindow` for every window, then composites per pixel,
   downscales and blurs — linear in cards, glass area and window size (41% of one core above).
2. **It samples window contents, not the composed screen.** Occluded areas cannot be recovered.
3. **What it cannot see:** this process's own windows; animated wallpapers (D3D swap chains return black from
   `PrintWindow`); exclusive fullscreen and hardware overlays.
4. **It feels laggy.** 15 fps is visible while dragging or scrolling, and raising the frame rate only costs more.

## 4. Desktop acrylic: it works, but cards are invisible to capture

Ported from `C:\Coding\acrylic-multiwindow`: a **non-layered** backdrop HWND per card using the **documented**
`DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` (Windows 11 desktop acrylic) plus
`DWMWA_WINDOW_CORNER_PREFERENCE` rounding. Three things follow:

1. **DWM paints this material only for the active window.** Cards are layered windows and the backdrop never
   becomes active, so on its own the backdrop is a **`#D3D3D3` rounded plate** with no blur behind it
   (`glass-dwm-plate.png`).
2. **So the backdrop captures the screen itself** — the `PersistentAcrylic` idea from that project: `BitBlt`
   the region → a half-size DIB → two box-blur passes → `StretchBlt` back into the window, once per 200 ms
   (`glass-dwm-sample.png` shows what it captures and blurs).

   > **One trap that cost a debugging session**: the DIB must be **selected into the memory DC** with
   > `SelectObject`. Without it `StretchBlt` draws into the DC's default 1×1 monochrome bitmap and every
   > sample is **black** — which looks exactly like "DWM does not support this". With the selection in place
   > the samples read 90–250 and the glass looks right.

3. **It must exclude itself from capture**, otherwise the previous frame is captured back into the glass.
   Both the backdrop and the card get `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.

**That is the defining cost:** while this mode is active, **cards do not appear in any screenshot or screen
recording**. That is not a bug — it is inherent to capturing the screen from a window that has to sit in it.

Other limits: ≈3.5% of one core for 4 cards (system acrylic 1.6%, compat 41%); the blur radius (9 px on the
half-size copy) and the 200 ms interval are fixed; the backdrop's corners are DWM's 8 px, slightly tighter than
the card's own radius, so a sliver of glass can show at the corners. If the capture goes black for three frames
in a row, the mode **falls back to system acrylic automatically** and says why in the log.

Two things worth recording from later work:

- **"The glass darkens from top to bottom every so often."** Reported by a user; not reproduced and not rooted
  out here. Ruled out with measurements: the captured image itself (per-band luma of the raw capture stayed
  within ±2 over 12 s at rest), a progressive `StretchBlt` (a replica plate sampled at 2 kHz on its own window
  DC updated all bands in the same sample, 29 repaints in a row), and vsync misalignment (`DwmFlush` *adds*
  ~14 ms instead of replacing the ~16 ms read wait). Two harmless mitigations did ship: identical-ish frames
  (≤2 luma) are not repainted at all, and frames are painted **synchronously** instead of through
  `InvalidateRect`/`WM_PAINT`. Latency stayed at 30 ms; idle CPU went 4.8% → 3.5%.
- **GPU acceleration: the blur is not the problem, the readback is.** Measured on a 400×290 region
  (→ 200×145 thumbnail): GDI screen `BitBlt` **9–17 ms**, and *independent of region size* (16 ms even for
  32×32) — it is a synchronisation with the compositor, not a copy. DXGI desktop duplication acquires the same
  frame in **0.09 ms**, region copy ≈ 0 ms, readback 0.39 ms. The CPU box blur is only 0.61 ms, so moving the
  filter to a compute shader saves at most 0.5 ms; replacing the *capture* with duplication takes a whole round
  from ~10 ms to ~1 ms and stops stalling the UI thread for a vsync. Desktop duplication also **honours
  `WDA_EXCLUDEFROMCAPTURE`** (the cards' area comes back as the desktop behind them, not as black), so it can
  drop in where the GDI `BitBlt` is today. Cost: a D3D11 dependency (or hand-written COM), fallbacks for
  exclusive-fullscreen / remote sessions / hybrid GPUs, and periodic GPU wake-ups.
- **It shipped in 2.4.2** (`src/Ui/DesktopFrameSource.cs`, hand-written COM, no NuGet packages): one region read
  costs **0.1 ms** when nothing changed and **1–1.5 ms** for a fresh frame, against **18.6 ms** for the GDI read
  — and the GDI number was wall-clock spent *blocked* in the compositor, so the UI thread gets those 18 ms back.
  End to end the glass still lagged 46–47 ms either way, because the schedule (pump + window events) dominates;
  the win only turns into latency once the pump is allowed to run at **100 ms** instead of 200 ms, which the cheap
  read pays for: **16 ms median / 52 ms p90** at 6.8% CPU (4.2% before) and +18 MB. Safety: the `DuplicateOutput`
  vtable slot is picked by OS build (22 on 26200, 19 on older builds; unverified builds use GDI), the duplication is
  validated with `GetDesc` before use, the first (black) frame is discarded during priming, `ACCESS_LOST` rebuilds
  it, and it is released after 5 s without a capture. `MIRRORPOND_GLASS_SOURCE=gdi|dup` forces either path.
- **2.4.3** exposes that choice in the UI (Appearance → Advanced → *Capture backend*: auto / GPU / CPU) and fixes a
  **flicker** the 2.4.1 "skip identical frames" optimisation had introduced: the blurred frame was written back to the
  DIB *after* the "same as last frame" early return, so on a static desktop the DIB kept the **unblurred** sample, and
  any `WM_PAINT` (cards refresh every second, topmost is re-asserted every 2.5 s, occlusion changes) blitted it — the
  glass flipped from blurred to sharp and stayed there. Writing the blurred frame to the DIB first, adding
  `SWP_NOREDRAW` when the plate has not moved, and validating the plate after each blit removes it: paint-path
  sharpness went from 21–28 against 0.56–1.26 for the fresh frame (**20–25×**, i.e. the raw sample) to a perfect
  match, and idle CPU dropped from 0.95% to 0.55% (8 threads) because the plate is no longer re-blitted by the OS.
- **2.4.4** drops the separate plate window entirely: the blurred frame is now handed to the card's own glass layer
  (`OverlaySurface.SetGlass`, the same path the compat mode uses), so the rounded corners are clipped by the card's
  `Border.CornerRadius` in the *same* composition pass. That removed both reported artefacts at the root — a DWM-rounded
  plate corner poking out beyond the card's 10 px corner, and a periodic dark strip along the card's top edge when the
  two windows were off by a pixel or two. The acrylic settings (blur radius 18 px, refresh rate 10 fps, capture backend)
  are now exposed in the UI; idle CPU scales linearly with the rate (3.4% at 5 fps, ~6–8% at 10 fps, 7.7% at 15 fps on
  one core for four cards).

## 5. So what should you do

- **Leave it off.** Opacity alone gets you 90% of the look for zero CPU.
- Want glass *and* screenshots: use **system acrylic** (the default) with a radius of 12 px or less.
- Need a large radius, or consistent behaviour on Windows 10 / remote desktop: **compat**, 8–10 fps,
  8–12 px blur, and only one or two cards with glass on.
- Need to blur an **animated wallpaper, a video, or a GPU-accelerated window** (things `PrintWindow` cannot
  see) and can live without cards in your screenshots: **desktop acrylic**.
- Switch it in *Settings → Appearance → Advanced (experimental) → Glass implementation*.
