# 粒子聚字演示（ParticleToText）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `samples/ParticleToText/` 做一个独立 .NET 8 WPF 演示：几千个离散粒子聚成用户输入的任意文字，带拖尾与辉光，可用面板调参、可自检、可离屏截图、可压测。

**Architecture:** 两条取点路径（掩码采样 / 几何拒绝采样）统一产出 DIP 坐标点集；渲染层完全不建 WPF 视觉对象，用一块 `WriteableBitmap` 背后的 `uint[]` 能量缓冲做帧衰减拖尾 + 加色叠加 + 1/4 分辨率辉光；粒子用弹簧积分追目标点。诊断走三条命令行通道，报告落盘。

**Tech Stack:** .NET 8（`net8.0-windows`）、WPF、`System.Text.Json`（BCL）、零第三方包。

**Spec:** `docs/superpowers/specs/2026-11-11-particle-to-text-design.md`

## Global Constraints

- 目标框架 `net8.0-windows`，`LangVersion 12`，`Nullable=disable`，`ImplicitUsings=disable`（继承自 `Directory.Build.props`，不得覆盖）。
- **零 `PackageReference`**，只用 BCL 与 WPF。
- UI 文案、注释、日志一律简体中文；注释写"为什么"。
- 现代 C# 风格：file-scoped namespace、表达式体、`is null`、目标类型 `new()`。
- 本工程必须 `EnableDefaultApplicationDefinition=true`（全仓默认是 `false`）。
- 不得修改根 `MirrorpondHUD.csproj`、`Directory.Build.props`、宿主 `src/**`。
- 任何失败必须可见：`Log.Warn/Error` 落 `%LOCALAPPDATA%\ParticleToText\particle.log`，或进底栏状态行；禁止 `catch { }`。
- 降级阈值：物理像素 > **2 300 000** 时切半分辨率，底栏常亮「半分辨率」。
- 字号是上限；自动缩到放得下（6% 边距），底栏提示「已自动缩放」。
- 粒子数是目标值：候选点不足时如实显示实测值 + 「候选点不足」，不重复补点。
- `--shot` 固定用默认设置、等粒子到位（≤3s）、只拍画布、DPI 1:1。
- 构建必须 **0 警告 0 错误**；`--selftest` 退出码必须为 0。

## Review Focus

这些是 spec 隐含但没有任何任务天生会测到的输入/条件，每一条都已钉到下面的任务里：

1. **空白文字**（空串、纯空格、纯标点）——期望：拒绝采样、保留上一句、状态行说明原因，程序不崩。
2. **超长单行**（60 字以上）——期望：自动缩小字号直到放得下，底栏同时提示自动缩放与候选点不足。
3. **字体缺字**（emoji、生僻字）——期望：走字体回退，画面不空且不崩；完全采不到点时状态行报错。
4. **窗口趋零/最小化**（宽或高 ≤ 1 像素）——期望：不除零、不抛异常，跳过渲染直到尺寸恢复。
5. **快速连续回车**（旧句采样未完成就换新句）——期望：旧代结果被丢弃，最终画面只属于最后一句。

---

### Task 1: 工程骨架与诊断通道骨架

**Files:**
- Create: `samples/ParticleToText/ParticleToText.csproj`
- Create: `samples/ParticleToText/app.manifest`
- Create: `samples/ParticleToText/App.xaml`、`App.xaml.cs`
- Create: `samples/ParticleToText/MainWindow.xaml`、`MainWindow.xaml.cs`
- Create: `samples/ParticleToText/Core/Log.cs`
- Create: `samples/ParticleToText/Diagnostics/SelfTest.cs`、`ShotCapture.cs`、`BenchRunner.cs`

**Interfaces:**
- Produces:
  - `ParticleToText.Core.Log`：`static void Info/Warn/Error(string message, Exception ex = null)`、`static string FilePath`、`static void Reset(string header)`。
  - `ParticleToText.Diagnostics.SelfTest.Run() -> int`（失败项数）。
  - `ParticleToText.Diagnostics.ShotCapture.CaptureAsync(Application app, string path, Size size, string text) -> void`。
  - `ParticleToText.Diagnostics.BenchRunner.RunAsync(Application app, int seconds) -> void`。
  - `MainWindow.SetText(string text) -> void`（Task 10 补实现）。

- [ ] **Step 1: 建 csproj**

`OutputType=WinExe`、`UseWPF=true`、`AssemblyName`/`RootNamespace` 为 `ParticleToText`、`ApplicationManifest=app.manifest`、`EnableDefaultApplicationDefinition=true`、`Version=0.1.0`、`Product`/`Description` 用中文。不加任何 `PackageReference`。

- [ ] **Step 2: 建 app.manifest**

`dpiAware=true/PM`、`dpiAwareness=PerMonitorV2`、`longPathAware=true`，`requestedExecutionLevel` 保持 `asInvoker`。

- [ ] **Step 3: 写 Log**

`%LOCALAPPDATA%\ParticleToText\particle.log` 追加写，带 `yyyy-MM-dd HH:mm:ss` 时间戳与级别；写盘失败只吞 IO 异常并 `Trace.WriteLine`（日志器自己不能把程序带崩，这是唯一允许的窄 catch，必须注释原因）。`Reset(header)` 截断文件写入表头。

另加 `AttachConsole(-1)`（`kernel32` P/Invoke）：WinExe 从 cmd 启动时默认不接父控制台，`Console.WriteLine` 会进黑洞；附着失败（从资源管理器启动）无害，忽略即可。这样 `--selftest` 在 cmd 里能直接看到逐项结论，从资源管理器启动时仍有 `diagnostics.log` 兜底。

- [ ] **Step 4: 写 App.xaml.cs 分流**

照 `ContourLine/App.xaml.cs`：`--selftest` → `ShutdownMode.OnExplicitShutdown`、`Shutdown(SelfTest.Run())`；`--shot <path>` → `ShutdownMode.OnExplicitShutdown`、`ShotCapture.CaptureAsync(...)`；`--bench <秒>` → 同理。分流前调 `Log.Reset`。参数解析辅助函数 `HasFlag`/`ReadValue`/`ReadSize`（`--size WxH`，默认 1600×900，非法值走 `Console.Error` 并回落）。

- [ ] **Step 5: 诊断桩**

`SelfTest.Run()` 只打印表头与「全部通过」并返回 0；`ShotCapture`/`BenchRunner` 打一行「未实现」后 `Shutdown(1)`。`MainWindow` 只放一个 `Grid`。

- [ ] **Step 6: 验证骨架**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `0 个警告`、`0 个错误`。

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`；`%LOCALAPPDATA%\ParticleToText\particle.log` 里出现「自检开始」。

- [ ] **Step 7: 提交**

```bash
git add samples/ParticleToText
git commit -m "feat(particle): 搭起独立示例工程与三条诊断通道骨架"
```

---

### Task 2: MotionSettings 与夹取

**Files:**
- Create: `samples/ParticleToText/Core/MotionSettings.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Produces: `MotionSettings`（**公开字段**，DTO 风格）：
  `int ParticleCount = 8000`、`double FontSize = 220.0`、`double Glow = 0.55`、`double TrailDecay = 0.90`、
  `double Jitter = 0.6`、`double Speed = 1.0`、`bool BurstOnChange = true`、`bool NearestAssign = false`、
  `bool GeometrySampling = false`、`string Palette = "jade"`；
  `void Normalize()`、`MotionSettings Clone()`、`static MotionSettings Defaults() -> MotionSettings`（每次新实例）。

- [ ] **Step 1: 写失败断言**

在 `SelfTest.Run()` 里加 `Case("设置夹取", CheckSettings)`，断言：`ParticleCount = -5` → `Normalize()` 后为 500；`ParticleCount = 999999` → 30000；`TrailDecay = 1.5` → 0.98；`TrailDecay = 0.1` → 0.5；`FontSize = 0` → 24；`Palette = null` → `"jade"`；`Clone()` 后改副本不影响原件。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: 编译失败 `CS0246: 未能找到类型或命名空间名“MotionSettings”`。

- [ ] **Step 3: 实现 MotionSettings**

夹取区间：`ParticleCount` [500, 30000]、`FontSize` [24, 600]、`Glow` [0, 1]、`TrailDecay` [0.5, 0.98]、
`Jitter` [0, 4]、`Speed` [0.2, 4]；`Palette` 为 null/空白 → `"jade"`；`Normalize()` 对 `double.NaN` 一并归位到默认值。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`，日志里 `[通过] 设置夹取`。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Core/MotionSettings.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 设置模型与夹取规则"
```

---

### Task 3: SettingsStore 落盘与读回

**Files:**
- Create: `samples/ParticleToText/Settings/SettingsStore.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Produces: `SettingsStore`：`string FilePath { get; }`、`string LastError { get; }`、
  `MotionSettings Load()`、`bool Save(MotionSettings settings)`。

- [ ] **Step 1: 写失败断言**

加 `Case("设置落盘往返", ...)` 与 `Case("损坏设置兜底", ...)`：
往返——写一个改过的副本（`ParticleCount=1234`、`Palette="amber"`、`GeometrySampling=true`）到临时目录的 store，`Load()` 回来三项都相等；损坏——把文件写成 `"{ 这不是 json"`，`Load()` 返回默认值且 `LastError` 非空。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `CS0246`（找不到 `SettingsStore`）。

- [ ] **Step 3: 实现 SettingsStore**

`JsonSerializerOptions` 必须 `IncludeFields = true`（公开字段 DTO 的坑，见 spec §6.6）。`Save` 先写 `<path>.tmp` 再 `File.Replace`（无原文件时退 `File.Move`），失败回退 Copy+Move，全程失败写 `LastError` 并 `Log.Error`。`Load` 捕获 `IOException`/`JsonException`/`UnauthorizedAccessException`，返回 `MotionSettings.Defaults()`，原因写 `LastError`。构造函数接受可选的路径覆盖（自检用临时目录）。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`，两项都 `[通过]`。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Settings/SettingsStore.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 设置落盘与损坏兜底"
```

---

### Task 4: ParticlePalette 四套配色

**Files:**
- Create: `samples/ParticleToText/Core/ParticlePalette.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Produces: `ParticlePalette`：公开字段 `Id`、`Name`（中文）、`Color Core`、`Color Glow`、`Color Background`；
  `static IReadOnlyList<ParticlePalette> All { get; }`（jade/ice/aurora/amber 四套）；
  `static ParticlePalette ById(string id)`（未知 id → `All[0]` + `Log.Warn`）；
  `uint Sample(double hueJitter01, double intensity01)` → BGRA 打包（`B | G<<8 | R<<16 | 255<<24`）。

- [ ] **Step 1: 写失败断言**

加 `Case("配色与加色打包", ...)`：四套 `Id` 唯一；`ById("nope")` 返回 `All[0]`；`Sample(0.5, 1.0)` 的 alpha 字节为 255；`Sample` 对 `intensity01=0` 返回全零（加色时零贡献）；两套配色的 `Core` 不相同。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `CS0246`。

- [ ] **Step 3: 实现配色**

四套均为深色底、冷/暖对比（jade 青玉、ice 冰蓝、aurora 极光绿紫、amber 琥珀）。`Sample` 在 `Core` 与 `Glow` 之间按 `intensity01` 插值，`hueJitter01` 做 ±12° 色相偏移（走 HSV 往返，避免逐粒子 `Color.FromRgb` 分配）。`Background` 用作清屏底色。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Core/ParticlePalette.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 四套配色与加色打包"
```

---

### Task 5: MaskSampler 掩码采样

**Files:**
- Create: `samples/ParticleToText/Core/TextSample.cs`、`Core/MaskSampler.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Produces:
  - `TextSample`：`Vector[] Points`、`Rect Bounds`、`double EffectiveFontSize`、`bool WasScaledDown`、`string Mode`、`int RequestedCount`。
  - `MaskSampler.TrySample(FormattedText text, Size canvas, int targetCount, out Vector[] points, out Rect bounds, out string error) -> bool`。坐标原点在画布中心，Y 向上为正（与 WPF 画布相反，渲染层负责翻转）。

- [ ] **Step 1: 写失败断言**

加 `Case("掩码采样点数与范围", ...)`：`"镜潭"`、字号 200、画布 1600×900、目标 8000 → 返回 true 且 `points.Length > 1000`；所有点在 `bounds` 内（留 1px 容差）；`"   "`（纯空格）→ 返回 false 且 `error` 非空（**Review Focus 1**）；点数不超过 `targetCount`。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `CS0246`。

- [ ] **Step 3: 实现 MaskSampler**

`FormattedText` → `RenderTargetBitmap`（`PixelFormats.Pbgra32`、约 220px 字高、灰度）→ 拷 `byte[]` → 第一遍按步长 3 扫 alpha 估覆盖率 → 由 `targetCount / 覆盖率` 反推步长（夹到 ≥1）→ 第二遍收集候选 → `Random` 洗牌取前 N → 映射回画布坐标并居中。位图全空时返回 false、`error="字形位图为空（可能缺字）"`（**Review Focus 3**）。`targetCount <= 0` 返回 false。采样后调用方可 `Freeze`。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Core/TextSample.cs samples/ParticleToText/Core/MaskSampler.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 掩码采样取点"
```

---

### Task 6: GeometrySampler 几何拒绝采样

**Files:**
- Create: `samples/ParticleToText/Core/GeometrySampler.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Produces: `GeometrySampler.TrySample(FormattedText text, Size canvas, int targetCount, out Vector[] points, out Rect bounds, out string error) -> bool`，与 `MaskSampler` 同形（渲染层可互换）。

- [ ] **Step 1: 写失败断言**

加 `Case("几何采样覆盖与一致", ...)`：同一文本两条路采出的 `bounds` 宽高相对误差 < 15%；几何路点数 > 500；所有点通过 `Geometry.FillContains`（抽样 50 个验证）；空文本返回 false。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `CS0246`。

- [ ] **Step 3: 实现 GeometrySampler**

`BuildGeometry` 得轮廓 → 包围盒内均匀撒 `targetCount * 3` 个候选 → `FillContains` 筛 → 不足 `targetCount` 时接受实测数（**不补点**，spec §6.2）→ 洗牌取前 N。单次采样超过 400ms 时 `Log.Warn` 记录（不中断）。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Core/GeometrySampler.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 几何拒绝采样取点"
```

---

### Task 7: TextSampler 统一入口、自动缩放、代次

**Files:**
- Create: `samples/ParticleToText/Core/TextSampler.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Consumes: `MaskSampler.TrySample`、`GeometrySampler.TrySample`、`TextSample`、`MotionSettings`。
- Produces: `TextSampler`：`void Request(string text, Size canvas, MotionSettings settings)`、
  `bool TryTake(out TextSample sample, out string error)`、`bool IsBusy { get; }`。
  `Request` 立即返回；结果在后台线程产出，`TryTake` 在 UI 线程取（取走即清空）。

- [ ] **Step 1: 写失败断言**

加三项：`Case("空文字拒绝", ...)`——`Request("")` 后 `TryTake` 返回 false 且 `error` 含「空」；`Case("超长句自动缩放", ...)`——40 个「镜」字在 1600×900 上 `EffectiveFontSize < 220` 且 `WasScaledDown == true`（**Review Focus 2**）；`Case("旧代结果被丢弃", ...)`——连发两次 `Request`（第二句不同），等后台空闲后 `TryTake` 拿到的 `Points` 长度与第二句的单独采样一致（**Review Focus 5**）。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `CS0246`。

- [ ] **Step 3: 实现 TextSampler**

测量：`FormattedText` 按 `settings.FontSize` 建，若 `Width/Height` 超出 `canvas × 0.88` 则按比例缩字号，循环至放得下（最多 12 轮），置 `WasScaledDown`。按 `settings.GeometrySampling` 选采样器。代次：`Interlocked.Increment(ref _generation)`，后台任务携带代次，回写时 `if (gen != Volatile.Read(ref _generation)) return;` 丢弃（AGENTS.md §6.3 模式）。异常一律 `Log.Error` + `error` 文本，不抛给 UI。`canvas` 宽或高 < 32 时直接返回 false（**Review Focus 4**）。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`，三项都通过。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Core/TextSampler.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 采样入口、自动缩放与代次管理"
```

---

### Task 8: ParticleField 粒子状态与弹簧

**Files:**
- Create: `samples/ParticleToText/Core/ParticleField.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Produces:
  - `struct Particle`：`double X, Y, VX, VY, TX, TY, Hue`。
  - `ParticleField(int capacity)`、`int Count { get; }`、`double MeanRemainingDistance { get; }`、
    `void Scatter(Random rng, Size canvas, int count)`、
    `void SetTargets(Vector[] points, bool nearest, bool burst, Random rng)`、
    `void Step(double dt, double jitter, Random rng)`。

- [ ] **Step 1: 写失败断言**

加三项：`Case("弹簧收敛", ...)`——`Scatter` 后 `SetTargets` 到 200 个点，`Step(1/60)` 跑 180 帧，`MeanRemainingDistance < 1.0`；`Case("目标全覆盖", ...)`——目标点数 500 时 `Count == 500` 且每个目标至少被一个粒子占用；`Case("爆散方向", ...)`——`burst=true` 后首帧速度的径向分量为正（点积 > 0）。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `CS0246`。

- [ ] **Step 3: 实现 ParticleField**

弹簧 `VX += (TX - X) * k * dt`（`k ≈ 26`），阻尼 `V *= 0.86`（按 60fps 调），位置 `X += VX * dt`；`dt` 夹到 `[1/240, 1/20]` 防跳变。`SetTargets`：洗牌随机分配；`nearest=true` 时按 x 排序后错位配对（便宜近似，不做 O(n²)）。`burst` 给每个粒子加「旧质心 → 自身」方向 6–14 px/s 的初速。`Step` 到位后加幅值 ≤ 0.8px 的噪声。`Count` 随目标点数变化。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Core/ParticleField.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 粒子场、弹簧积分与目标分配"
```

---

### Task 9: EnergyCanvas 能量缓冲与辉光

**Files:**
- Create: `samples/ParticleToText/Core/EnergyCanvas.cs`
- Modify: `samples/ParticleToText/Diagnostics/SelfTest.cs`

**Interfaces:**
- Consumes: `ParticleField`、`ParticlePalette`、`MotionSettings`。
- Produces: `EnergyCanvas : FrameworkElement`：
  `void Resize(Size dipSize, double dpiScale)`、`void ClearFrame()`、
  `void Render(ParticleField field, ParticlePalette palette, MotionSettings settings)`、
  `bool HalfResolution { get; }`、`int PixelWidth { get; }`、`int PixelHeight { get; }`、
  `static uint Decay(uint bgra, int factor)`、`static uint AddSaturate(uint dst, uint src, int weight)`。

- [ ] **Step 1: 写失败断言**

加三项：`Case("衰减数学", ...)`——`Decay(0xFFFFFFFF, 230)` 每通道约为 `0xE6`（±1），alpha 保持 255；`Case("加色饱和", ...)`——`AddSaturate(0xFFFFFF00, 0xFFFFFF00, 128)` 各通道为 255 不溢出；`Case("半分辨率判定", ...)`——`1600×900 @1.0` 不降级，`1600×900 @1.5`（2.16M 像素）不降级，`1920×1080 @1.5`（4.66M）降级。

- [ ] **Step 2: 跑一次确认失败**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `CS0246`。

- [ ] **Step 3: 实现 EnergyCanvas**

`uint[]` 主缓冲（物理像素）+ 1/4 辉光缓冲；`Render` 顺序：衰减 → 粒子加色（中心满强度 + 3×3 权重 1/4）→ 辉光降采样/两遍盒模糊/双线性放大加回 → `WritePixels` → `InvalidateVisual()`。`OnRender` 只做 `DrawImage(_bitmap, 全窗)`。DPI 或尺寸变化时 `Resize` 重建缓冲（`dpiScale <= 0` 或尺寸 < 32 时直接 `ClearFrame` 并返回，**Review Focus 4**）。`Render` 里不得分配大对象（缓冲复用）；降级时置 `HalfResolution=true` 并在 `Resize` 里 `Log.Info` 记录一次状态转移。

- [ ] **Step 4: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: 退出码 `0`。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/Core/EnergyCanvas.cs samples/ParticleToText/Diagnostics/SelfTest.cs
git commit -m "feat(particle): 能量缓冲、帧衰减与辉光"
```

---

### Task 10: MainWindow 装配与动画循环

**Files:**
- Modify: `samples/ParticleToText/MainWindow.xaml`、`MainWindow.xaml.cs`
- Create: `samples/ParticleToText/Ui/AppearancePanel.xaml`、`Ui/AppearancePanel.xaml.cs`（Task 11 填内容，这里先放空壳占位）

**Interfaces:**
- Consumes: `TextSampler`、`ParticleField`、`EnergyCanvas`、`SettingsStore`、`MotionSettings`。
- Produces: `MainWindow`：`void SetText(string text)`、`Task<bool> WaitForSettleAsync(TimeSpan timeout)`、
  `bool CaptureFrame(out BitmapSource frame)`（供 Task 12 的 shot 用）。

- [ ] **Step 1: 写窗口 XAML**

三层：`EnergyCanvas` 铺满；顶部居中 `TextBox`（无边框、半透明底、占位文字「输入文字，回车成型」，字号与输入内容一致，失焦 30% 不透明度）；底部状态条（帧耗时、实测粒子数、模式、状态行）。`MinWidth/MinHeight=640/400`。

- [ ] **Step 2: 写动画循环**

`CompositionTarget.Rendering` 驱动（不用 DispatcherTimer），用 `Stopwatch` 算 `dt`。每帧：`TryTake` 取采样 → 有则 `SetTargets`；`field.Step(dt)`；`canvas.Render(...)`；每 8 帧更新底栏。`SizeChanged`/`DpiChanged` 触发 `canvas.Resize` 并 180ms 防抖后重新 `Request` 采样（**Review Focus 4**：尺寸 < 32 时跳过）。窗口加载时读 `SettingsStore.Load()`、`SetText("镜潭一览")`，并把 `LastError` 送状态行。

- [ ] **Step 3: 快捷键与输入**

`Enter` → `SetText(输入框文本)`；`Esc` → `Close()`；`Tab` → 切面板可见（`e.Handled = true`）；`Ctrl+R` → 重放当前句。`SetText` 对空串/空白直接返回并设状态行「文字为空，未成型」（**Review Focus 1**），不打扰画面。

- [ ] **Step 4: 验证**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: 0 警告 0 错误。

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe`
Expected: 窗口打开，几秒内粒子聚成「镜潭一览」；`Esc` 退出。

- [ ] **Step 5: 提交**

```bash
git add samples/ParticleToText/MainWindow.xaml samples/ParticleToText/MainWindow.xaml.cs samples/ParticleToText/Ui
git commit -m "feat(particle): 窗口装配、输入框与动画循环"
```

---

### Task 11: AppearancePanel 折叠面板

**Files:**
- Modify: `samples/ParticleToText/Ui/AppearancePanel.xaml`、`Ui/AppearancePanel.xaml.cs`
- Modify: `samples/ParticleToText/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `MotionSettings`、`ParticlePalette.All`、`SettingsStore`。
- Produces: `AppearancePanel`：`void Bind(MotionSettings settings, Action onChanged)`、
  `void ShowStatus(string message)`、`event Action ResetRequested`。

- [ ] **Step 1: 写面板 XAML**

右侧 280px 清单：粒子数、字号、辉光强度、拖尾衰减、抖动、速度（滑杆）；爆散、就近分配、几何采样（勾选）；配色四选一（胶囊按钮）；「重置」按钮；底部状态行。说明文字简洁、不用括号（AGENTS.md §10）。

- [ ] **Step 2: 写绑定逻辑**

`Bind` 里生成控件并双向同步到 `settings`；每次改动 → `settings.Normalize()` → `onChanged()`（MainWindow 里触发重采样与 `SettingsStore.Save`）。`Save` 失败 → `ShowStatus` 显示 `LastError`。重置 → 恢复 `MotionSettings.Defaults()` 并触发 `ResetRequested`。

- [ ] **Step 3: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe`
Expected: `Tab` 展开面板；拖粒子数滑杆后底栏粒子数随之变化；改配色立刻换色；改完关闭再启动，设置仍在（`%LOCALAPPDATA%\ParticleToText\settings.json`）。

- [ ] **Step 4: 提交**

```bash
git add samples/ParticleToText/Ui samples/ParticleToText/MainWindow.xaml.cs
git commit -m "feat(particle): 右侧折叠设置面板"
```

---

### Task 12: ShotCapture 与 BenchRunner 真实实现

**Files:**
- Modify: `samples/ParticleToText/Diagnostics/ShotCapture.cs`、`Diagnostics/BenchRunner.cs`
- Modify: `samples/ParticleToText/MainWindow.xaml.cs`（`CaptureFrame`、`WaitForSettleAsync`）

**Interfaces:**
- Consumes: `MainWindow.SetText`、`WaitForSettleAsync`、`CaptureFrame`。
- Produces: 两个诊断入口的实际行为（签名不变）。

- [ ] **Step 1: 写 ShotCapture**

`ShutdownMode.OnExplicitShutdown` 下建窗口（**不读**用户设置，用 `MotionSettings.Defaults()`）、`SetText(text)`、`WaitForSettleAsync(3s)`、`CaptureFrame` → `PngBitmapEncoder` 写文件；成功后 `Console.WriteLine("[shot] 已写出 <路径>")` 并 `Shutdown(0)`；超时按当前状态截图并 `Console.Error` 提示（照 `ContourLine/ShotCapture.cs`）；异常 → `Log.Error` + `Shutdown(1)`。

- [ ] **Step 2: 写 BenchRunner**

跑 `seconds` 秒，每帧 `Stopwatch` 记耗时，剔除前 30 帧预热，输出平均/中位/P95/最差与实测粒子数（照 `ContourLine/BenchRunner.cs` 的口径）。平均 > 16.7ms 时 `Console.Error` 一行提示但不改退出码（判定留给验收）。

- [ ] **Step 3: 验证**

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --shot out.png --text "镜潭一览"; $LASTEXITCODE`
Expected: 退出码 `0`，`out.png` 存在且能看见文字。

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --bench 6; $LASTEXITCODE`
Expected: 退出码 `0`，输出稳态平均帧耗时与粒子数。

- [ ] **Step 4: 提交**

```bash
git add samples/ParticleToText/Diagnostics samples/ParticleToText/MainWindow.xaml.cs
git commit -m "feat(particle): 离屏截图与压测通道"
```

---

### Task 13: README 与全量验收

**Files:**
- Create: `samples/ParticleToText/README.md`

- [ ] **Step 1: 写 README**

照 `samples/ContourLine/README.md` 的结构：一句话定位、运行方式、三条验证通道与实测数字、原理流水线图（掩码采样 → 目标分配 → 弹簧 → 能量缓冲 → 衰减/加色/辉光 → 上屏）、参数表（每个滑杆的含义与推荐区间）、"为什么这样画"（离散感、拖尾为何用衰减而非清屏、辉光为何低分辨率、降级阈值 230 万像素的由来、字号为何是上限）、文件地图、已知限制（仅 Windows、字体回退、高 DPI 半分辨率、粒子数上限 30000）。

- [ ] **Step 2: 全量验收**

Run: `dotnet build samples\ParticleToText\ParticleToText.csproj -c Release`
Expected: `0 个警告`、`0 个错误`。

Run: `& .\samples\ParticleToText\bin\Release\ParticleToText.exe --selftest; $LASTEXITCODE`
Expected: `0`。

Run: `dotnet build -c Release`
Expected: 根工程仍 0 警告 0 错误（samples 未被编入）。

- [ ] **Step 3: 提交**

```bash
git add samples/ParticleToText/README.md
git commit -m "docs(particle): 示例说明与验证记录"
```
