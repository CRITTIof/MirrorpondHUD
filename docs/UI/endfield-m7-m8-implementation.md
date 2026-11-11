# 终末地 M7 / M8 实现记录（2.31.0）

> 对应 [`endfield.md`](endfield.md) §7 的 M7 / M8，以及 M6 装饰纹样的可见性修复。
> 设计调研见 [`endfield-m7-m8-research.md`](endfield-m7-m8-research.md)；
> M1–M6 的选项见 [`endfield-implementation.md`](endfield-implementation.md)。

## 0. 结论

- **M7 已实现**：四档一次性动效（Current / Cut / Industrial / Off）+ 可关闭的循环呼吸；
  循环动效有明确的持续 GPU 合成开销，默认关闭。
- **M8 已实现**：`.mptheme` 增加 `appearance.motion` 数据段，和 `appearance.industrial`
  一起构成“主题数据”；主题仍然只是数据，不携带可执行代码。
- **边界拖动已修复**：拖动期间用 15ms 物理光标轮询、真实按键状态和“松手光标重算落点”，
  并在自动对齐翻边时改用卡片左边缘作为锚点；100% ↔ 300% 交界实测不再出现大小/位置错。

## 1. M7 四档动效

### 1.1 档位参数

`Style.MotionProfile`（`Settings.NormalizeMotionProfile`）：

| 档位 | 补位 / 对齐 | 呼出 | 消失 | 位移 | 缓动 |
|---|---:|---:|---:|---:|---|
| `Current`（默认） | 180ms | 160ms | 160ms | 8 DIP | ease-out |
| `Cut` | 90ms | 80ms | 80ms | 0 | 线性 |
| `Industrial` | 120ms | 110ms | 110ms | 2 DIP | ease-out |
| `Off` | 0 | 0 | 0 | 0 | 瞬时 |

实现位置：

- `src/Ui/CardMotion.cs`：常量改为按档位解析的只读属性；`Ease(t)` 在 `Cut` 下走线性；
- `OverlayWindow`：补位 / 呼出 / 消失都读 `CardMotion.*`，不再引用硬编码常量；
- `Off` 使 `CardMotion.Enabled == false`，窗口动画直接落位，但不会改动用户的「减少动效」设置；
- `Behavior.ReduceMotion` 仍然一票否决；截图 / `--selftest` 的 `SuppressForCapture()` 仍然优先。

设置入口：外观 → 工业风格 → **动效档位**。

### 1.2 循环动效

`Style.LoopEffect`（默认 `None`）：

| 效果 | 视觉 | 实现 |
|---|---|---|
| `Breath` | 整卡明暗呼吸 | `_loopLayer` 整卡填充 + Opacity 往返 |
| `Scanline` | 一条横线从上往下扫过 | `_loopBand`（2px）+ TranslateTransform.Y 循环 |
| `Pulse` | 1px 描边明暗脉冲 | `_loopLayer` 只设 BorderBrush / BorderThickness |

可调参数：

- `LoopDurationMs`：600–20000，默认 2600；
- `LoopAmplitude`：0.02–0.8，默认 0.10（叠加层最大不透明度）；
- `LoopMotion`：2.31 的旧布尔字段，仅用于旧配置兼容；读入后由 `LoopEffect` 派生。

关闭 / 卡片重建 / `ReduceMotion` 时执行 `BeginAnimation(..., null)` 并折叠，不保留动画时钟；
主题文件：`appearance.motion.effect` / `durationMs` / `amplitude`（`loop` 仍兼容）；
设置入口：外观 → 工业风格 → **循环效果 / 循环时长 / 呼吸幅度**。

### 1.3 性能代价（必须知道）

| 项 | 代价 |
|---|---|
| 动画机制 | WPF 合成器动画，无 `DispatcherTimer`、无每帧托管分配 |
| Breath | 每卡一个整卡大小的半透明矩形重绘；4K@300% 约 1200×900 物理像素/卡 |
| Scanline | 只重绘一条 2px 横线，填充率远低于 Breath；仍有每帧合成时钟 |
| Pulse | 只重绘 1px 描边，成本最低 |
| 典型影响 | 集显 / 远程桌面 / 高刷新屏上会多占一点 GPU 与电量；不会造成布局抖动 |
| 默认 | **None**；主题示例写 `"loop": false` |
| 关闭方式 | 设置页选“无”、`ReduceMotion=true`、`MotionProfile=Off` 任一都立即停止 |

如果只想要“工业感”而不想付持续合成成本，用 `Industrial` 档位的一次性动效即可。

## 2. M8 主题数据

### 2.1 主题文件字段

```json
"appearance": {
  "industrial": {
    "monoReadouts": true,
    "cardCodes": true,
    "decoration": "Grid"
  },
  "motion": {
    "profile": "Industrial",
    "effect": "Scanline",
    "loop": true,
    "durationMs": 2600,
    "amplitude": 0.10
  }
}
```

- `industrial`：M4 等宽读数 / M5 铭牌 / M6 装饰纹样；
- `motion`：M7 档位与循环效果（`effect` / `durationMs` / `amplitude`；`loop` 兼容旧字段）；
- 全部 optional，不写 = 保持当前值；
- `decoration` / `profile` 是**有界枚举**，主题不能写任意曲线或任意绘制代码；
- schema 仍为 1，旧主题不受影响；`ThemeFile` 导出会写出这两个段，往返自检覆盖。

### 2.2 代码落点

| 文件 | 作用 |
|---|---|
| `src/Core/ThemeFile.cs` | `ThemeMotion` / `ThemeIndustrial` 数据模型、解析、校验、导出 |
| `src/Contracts/Core/Settings.cs` | `MotionProfile` / `LoopEffect` / `LoopDurationMs` / `LoopAmplitude` / `MonoReadouts` / `IndustrialHeader` / `Decoration` |
| `src/Contracts/Ui/ThemeTokens.cs` | `Success` / `Warning` / `MonoFont` 等最终令牌 |
| `src/Ui/CardMotion.cs` | 档位 → 时长 / 位移 / 缓动 |
| `src/Ui/OverlaySurface.cs` | 装饰画刷、循环层、MonoFont 接线 |
| `src/UiKit/CardHeader.cs` | 铭牌编号 / 徽标等宽大写 |
| `src/UiKit/CardStatus.cs` | Success / Warning / 进度条令牌 |

**不做**：主题携带 DLL、`IThemeVisualProvider` 插件、任意 cubic-bezier 字符串。
需要新纹样 / 新档位时在宿主目录里加一个枚举值 + 一个 `case`，保证性能和验证边界可控。

## 3. 边界拖动修复（同一版本）

3.1 问题复现（主屏 100% + 副屏 300%，卡片从 x=1600 拖到 2600）：

- 修复前：拖动在 x≈1902 提前结束，窗口停在交界，锚点 `-6`，随后自动对齐翻成左对齐；
- 根因一：WPF 在 DPI 切换时不再派发 MouseMove / 丢捕获；
- 根因二：`GetAsyncKeyState` 在切换瞬间误报松开；
- 根因三：右对齐叠层落点减旧偏移得到负锚点，自动对齐翻边把整叠拉回交界左侧。

3.2 修复：

1. 拖动期间每 15ms 用 `GetCursorPos` 读物理光标，不依赖 WPF MouseMove；
2. 按键状态用 `GetKeyState` + `GetAsyncKeyState` 双读；
3. `FinishDrag` 用松手瞬间的光标重算目标，不再用可能滞后的 `_dragTarget`；
4. 自动对齐 + 右对齐叠层：落点卡片中心在目标屏左半区时，直接用卡片左边缘作为新锚点；
5. 拖动期间 `DpiChanged` 不调 `ApplyPosition`，并在 Render 优先级把窗口压回拖动目标。

3.3 实测：

| 指标 | 修复前 | 修复后 |
|---|---|---|
| 落点窗口 x | 1902（交界） | **2600** |
| 窗口宽度 | 742（3× 但位置错） | **742（247 DIP × 3）** |
| DPI | 288 | **288** |
| 锚点 | -6 | **226.67** |
| 显示器 | 1 | **1** |

## 4. 验证

- `dotnet build -c Release`：**0 警告 0 错误**；
- `--selftest`：**退出码 0、失败项 0**；新增四档参数、主题 `motion` 往返、非法 profile / decoration 拒绝；
- 墨版 `--screenshot-cards`：装饰网格在 32% 透明度下可见；圆角 0、等宽数字、`06 文件中转站` 铭牌；
- 交界拖动：`SetCursorPos + mouse_event` 从 1600,240 拖到 2600,360，落点/尺寸/DPI/锚点见上表；
- 用户设置已恢复（`X=1367 / Monitor=0 / 4 卡`，主文件与 `.bak` 同步）。

## 5. 已知边界

- 循环动效是 GPU 合成成本，不是零成本；默认关闭，主题示例也关闭；
- 拖动修复依赖 Win32 光标轮询，每 15ms 一次，只在拖动期间运行；
- 鼠标注入测试不等于真人拖放；真人手感、极快甩动、跨屏中途松手仍建议再确认；
- `Cut` 档位的线性缓动是近似“切入”，没有做逐帧曲线拟合；
- 主题数据只覆盖动效 / 装饰选项，不改变材质、取图后端、交互复杂度。
