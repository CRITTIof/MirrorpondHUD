# 毛玻璃与液态玻璃：token-monitor 拆解与本项目可行性评估

> 调研对象：[Javis603/token-monitor](https://github.com/Javis603/token-monitor)（Electron 0.78.0，
> 2026-10-24 的 `main`，commit `e1c393c`）。本文只做**提炼与评估**，本项目本次不实现。
>
> 一句话结论：它不用截屏，Windows 上靠 **DWM 系统材质**或**独立底板窗口 + WinRT `HostBackdropBrush`**，
> macOS 上靠 **AppKit 的 `NSVisualEffectView` / `NSGlassEffectView`**。其中「独立底板窗口」这条路
> **可以搬进本项目**，而且能绕开我们被 `WS_EX_LAYERED` 卡住的死结；「真液态玻璃」的折射效果
> Windows 没有对应 API，只能用高光 / 渐变近似。

## 1. 结论摘要

| 能力 | token-monitor 的做法 | 本项目能否实现 | 建议 |
| --- | --- | --- | --- |
| Windows 实时毛玻璃（真模糊） | 不透明窗口 + DWM 材质；或独立底板窗口 + `CreateHostBackdropBrush` | **可以**，底板必须是独立 HWND | 值得做一个 PoC，成功后替换 `PrintWindow` 采样 |
| 液态玻璃（Apple 那种透镜 / 折射） | macOS 26 的 `NSGlassEffectView`，私有接口调形状与色调 | **不可以**：Windows 没有公开的背景采样 + 位移 API | 只做视觉近似 |
| 液态玻璃的视觉层（顶部高光、底边反光、胶囊、渐变描边） | CSS `inset 0 1px 0 rgba(255,255,255,.16)` 等 | **可以**，WPF 矢量层即可 | 成本最低，可以先做 |
| 任意圆角 / 非矩形形状 | Win11 DWM 圆角；DComp 几何裁剪；macOS `_setPath:` | 可以（我们已用 `SetWindowRgn`） | 保持现状 |
| 减少透明度 / 高对比度降级 | 原生材质状态 + CSS class 强制不透明 | 可以 | 建议补上 |
| 玻璃不透明度 / 模糊半径可调 | `glassOpacity` / `glassBlur`（68 / 32px） | 已有（`AcrylicBlur` / `AcrylicFps`） | 保持 |

## 2. 它为什么不需要截屏

token-monitor 的窗口本身**不负责**画背景模糊，内容层（Chromium）只画半透明色和控件：

- **Windows**：开玻璃时窗口是**不透明**窗口（`transparent: false`），模糊由 DWM / DComp 在窗口
  下面画，Web 内容只是一层 `rgba(48,52,56,0.68)` 的色调 + 内部控件的 `backdrop-filter`；
- **macOS**：窗口用 `vibrancy: 'hud'`（`NSVisualEffectView`）或 `NSGlassEffectView` 提供材质，
  网页的 `.shell` 直接设成 `background: transparent`，把材质让出来。

这和我们「分层窗口 + 自己抓背景」是两条路：它拿系统材质换**零 CPU**，代价是窗口必须不透明、
形状要交给系统（圆角 / 几何裁剪）。

## 3. Windows：三条材质路径

代码位置：`src/electron/main.js`（窗口创建）、`src/electron/windowsBackdrop.js`（Accent）、
`src/electron/windowsChrome.js`（DWM 圆角）、`src/windows-dock/DockBackdrop.cs`（独立底板）。

### 3.1 文档化路径：`backgroundMaterial: 'acrylic'`

```js
// main.js：开玻璃时窗口不透明，让 DWM 画材质
transparent: !(process.platform === 'win32' && glass),
...(process.platform === 'win32' && glass && !windowsAccent ? { backgroundMaterial: 'acrylic' } : {}),
```

Electron 的 `backgroundMaterial` 落到 `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW(3)`，
即 Win11 22H2+ 的实时桌面亚克力。前提是**非分层窗口**，这正是我们在 v2.0 试过、在
`AllowsTransparency=true` 上必然失败的原因。

### 3.2 兼容路径：AccentBlurBehind（未文档化）

`src/electron/windowsBackdrop.js` 用 koffi 直接调 `user32` / `dwmapi` / `gdi32`：

1. `DwmEnableBlurBehindWindow(hwnd, { dwFlags: ENABLE|BLURREGION|TRANSITIONONMAXIMIZED, fEnable: 1, hRgnBlur: CreateRectRgn(0,0,-1,-1) })`
2. `DwmExtendFrameIntoClientArea(hwnd, { -1,-1,-1,-1 })`
3. `SetWindowCompositionAttribute(hwnd, { Attrib: WCA_ACCENT_POLICY(19), pvData: ACCENT_POLICY{ AccentState: ACCENT_ENABLE_ACRYLICBLURBEHIND(4), GradientColor: 0x3A232323 } })`

失败就回落到 3.1 的 Acrylic。这条路的优点是 Win10 也有模糊，缺点是未文档化、没有模糊半径、
整窗矩形、系统更新可能失效。

### 3.3 真正值得借鉴的：独立底板窗口 + `HostBackdropBrush`

`src/windows-dock/DockBackdrop.cs` 是一个 **C# / Win32** 小进程，属于本项目可以直接抄的那一类：

```csharp
// 一个只负责“模糊”的独立窗口：没有 redirection bitmap，不接收输入
hwnd = CreateWindowExW(
    0x00200000 /* NOREDIRECTIONBITMAP */ | 0x08000000 /* NOACTIVATE */ |
    0x80 /* TOOLWINDOW */ | 0x20 /* TRANSPARENT */ | 0x8 /* TOPMOST */,
    ClassName, "Token Monitor Dock Backdrop", 0x80000000 /* WS_POPUP */, ...);

DwmSetWindowAttribute(hwnd, 17 /* DWMWA_USE_HOSTBACKDROPBRUSH */, ref enabled, 4);

// WinRT 合成器接管这一个 HWND，画刷 = “宿主背景模糊”
((IDesktopCompositorInterop)compositor).CreateDesktopWindowTarget(hwnd, true, out raw);
root    = compositor.CreateContainerVisual();
brush   = compositor.CreateHostBackdropBrush();
visual  = compositor.CreateSpriteVisual();
visual.Brush = brush;
visual.BorderMode = CompositionBorderMode.Soft;
visual.Clip = compositor.CreateGeometricClip(compositor.CreatePathGeometry(new CompositionPath(geometry)));
root.Children.InsertAtTop(visual);
target.Root = root;

// 插到内容窗口（Electron 的 HWND）正下方，而不是盖在它上面
SetWindowPos(hwnd, owner, bounds.left, bounds.top, width, height,
             0x10 /* NOACTIVATE */ | 0x200 /* NOOWNERZORDER */ | ...);
```

形状用 `NativeCurveGeometry`（实现 `IGeometrySource2D`，内部用 D2D1 建路径）描述，支持圆角矩形和
任意贝塞尔曲线；`CompositionGeometricClip` 负责裁剪，边缘由合成器抗锯齿。底板每 500ms 检查一次
是否和 owner 对齐，owner 被隐藏 / 移动就跟着隐藏或重摆。

**为什么这条对我们关键**：底板是**另一个窗口**，不接管 WPF 窗口的内容来源。也就是说
我们的卡片窗口可以继续 `AllowsTransparency=true` + `TextCanvas` 逐像素透明，
只要在它下面再放一个「模糊底板」窗口，就能得到真正的系统级模糊——不需要 `PrintWindow`、
不需要截图、CPU≈0。这直接修正了 [glass-options.md](glass-options.md) 里
「HostBackdropBrush 与 layered 不共存」的判断：**不共存的是同一个窗口，独立底板可以共存**。

### 3.4 圆角与边框

`src/electron/windowsChrome.js` 用两个 DWM 属性收拾 Win11 的窗口外观：

- `DWMWA_WINDOW_CORNER_PREFERENCE = 33`，值 `DWMWCP_ROUND = 2`：系统抗锯齿圆角
  （透明窗口上 CSS `border-radius` / `clip-path` 在 Windows 上抗锯齿很差，所以必须交给 DWM）；
- `DWMWA_BORDER_COLOR = 34`，值 `0xFFFFFFFE`：去掉无边框窗口那圈 1px 系统描边。

## 4. macOS：vibrancy 与 NSGlassEffectView

- **经典毛玻璃**：`vibrancy: 'hud'` + `visualEffectState: 'active'`，即 `NSVisualEffectView`
  的 HUD 材质。窗口整体矩形，非矩形区域（刘海、边缘 dock）再用 `macVibrancyMask.js`
  给材质视图套形状遮罩。
- **液态玻璃**：macOS 26（Darwin 25）才有。`src/electron/macLiquidGlass.js` 用 koffi
  直接调 AppKit / objc runtime，把系统的私有类 `NSGlassEffectView` 插到网页内容视图**下面**
  （`addSubview:positioned:relativeTo:`，位置 -1），然后：
  - `_setPath:` 传 `CGPath`，让玻璃可以是任意形状（圆角矩形、刘海、曲线 dock）；
  - `set_tintColor:` 给一点色调（`clear` 风格默认 35% 黑）；
  - `_subduedState` / `_forceMainAppearance` / `NSAppearance` 处理深浅色与“非活动窗口”状态；
  - 改完形状后 `invalidateShadow`，让窗口阴影按新的 alpha 重新算。
  - 旧系统 / 调用失败就回落到 vibrancy（`nativeMaterialVisibility.js` 的状态机：
    `liquid-glass` → `vibrancy` → `transparent`）。
- **网页侧配合**（`styles.css`）：`html.native-liquid-glass .shell { background: transparent;
  box-shadow: none; backdrop-filter: none; }` —— 原生材质已经模糊过了，CSS 再叠一层只会变脏；
  同时给控件加胶囊圆角和顶部高光。

## 5. 视觉配方（CSS 数值）

| 变量 / 规则 | 值 | 作用 |
| --- | --- | --- |
| `--glass-rgb` | `48, 52, 56` | 玻璃色调（深灰） |
| `--glass-alpha` | `0.68`（设置项 0–100，默认 68） | 玻璃不透明度 |
| `--glass-filter` | `blur(32px) saturate(115%)` | 页面内控件 / 面板的模糊 |
| 背景图不透明度 | 默认 `0.28`，独立一层 `::before` | 背景图不顶掉玻璃 |
| 液态玻璃高光 | `inset 0 1px 0 rgba(255,255,255,0.16), inset 0 -1px 0 rgba(255,255,255,0.035)` | 顶边受光、底边反光 |
| 液态玻璃控件 | `border-radius: 999px` + 去掉 `backdrop-filter` | 控件像同一块玻璃的透镜边缘 |
| 菜单 / 弹层 | `rgba(var(--glass-rgb), 0.86)` + 14px 圆角 + 高光 | 浮在内容上时用更实的玻璃 |
| 降级 | `native-reduced-transparency` / `native-material-opaque` / `native-high-contrast` | 关模糊、换不透明底、加粗边线 |

它的「液态玻璃」视觉其实是三层：**原生材质（模糊 + 折射）+ 半透明色调 + 1px 高光边**。
Windows 上没有原生折射，能复刻的是后两层。

## 6. 与本项目对照

| 项目 | token-monitor | 本项目（v2.1） |
| --- | --- | --- |
| 窗口形态 | 不透明窗口 + DWM 材质；或独立底板 | `AllowsTransparency=true` 分层窗口 |
| 模糊来源 | DWM / WinRT `HostBackdropBrush` | `PrintWindow` 取各层 + CPU 方框模糊 |
| CPU | ≈0（GPU 合成） | 7 张卡 15fps ≈ 单核 10–15% |
| 圆角 | Win11 DWM 圆角 / DComp 几何裁剪 | WPF `Clip` / `CornerRadius` |
| 形状 | 任意路径（D2D） | 圆角矩形（视频窗用 `SetWindowRgn`） |
| 阴影 | DWM / CSS | WPF `DropShadowEffect` |
| 截图 / 录屏 | 始终可见（不抓屏） | 始终可见（不抓屏，已过滤截图浮层） |
| 自身窗口入镜 | 会（材质模糊的是桌面与其它窗口） | 不会（只取外部窗口 + 桌面） |
| Win10 | Accent 路径可用 | 可用 |

## 7. 可行性评估

### A. 直接给卡片窗口加 DWM 材质 → 不可行

- 分层窗口拿不到材质（v2.0 实测整窗发黑），这是结构性冲突；
- 要换成非分层窗口，就失去逐像素 alpha：卡片之间的空隙、圆角外的像素都会变成窗口的一部分；
- `DWMWA_SYSTEMBACKDROP_TYPE` 只在 Win11 22H2+ 有效，Win10 只能走未文档化的 Accent；
- 圆角只有系统一个固定半径，不跟「外观 → 圆角」设置走；窗口阴影不可控。

### B. 独立底板窗口 + `HostBackdropBrush` → 可行，建议做 PoC

这是 token-monitor 的 3.3，搬到本项目需要：

1. 每张卡片一个底板 HWND（`WS_POPUP` + `NOREDIRECTIONBITMAP` + `TOPMOST|TOOLWINDOW|NOACTIVATE|TRANSPARENT`），
   位置和尺寸跟卡片内容区（不含 10px 阴影留白）一致；
2. `DwmSetWindowAttribute(17, TRUE)` + `Windows.UI.Composition` 的
   `CreateDesktopWindowTarget` → `CreateHostBackdropBrush` → 圆形裁剪；
3. 用 `SetWindowPos(backdrop, cardHwnd, ...)` 把底板插到卡片窗口**正下方**，
   卡片移动 / 缩放 / 改圆角时同步，卡片隐藏时隐藏；
4. 圆角用 `CompositionRoundedRectangleGeometry`（1809+）或自己实现 `IGeometrySource2D`
   （token-monitor 的 `NativeCurveGeometry` 就是后者）；
5. 保留现有 `PrintWindow` 实现作回退，设置项加「玻璃实现：兼容 / 系统」。

风险与待验证项：

| 风险 | 说明 | 缓解 |
| --- | --- | --- |
| Win10 行为 | `HostBackdropBrush` 在 Win10 1803+ 理论可用，但部分版本只给“色调”不给模糊 | 真机矩阵验证，失败回退 `PrintWindow` |
| WinRT 依赖 | 需要 `Windows.UI.Composition` 投影（`net8.0-windows10.0.19041.0` 或 CsWinRT） | 只发框架依赖版时要评估包体与最低系统版本 |
| z 序 | 底板必须紧贴卡片下面；卡片 `EnsureTopmost` 后要重摆；其它置顶窗口会插进来 | 复用现有 `RaisedTopmost` 事件；`GW_HWNDPREV` 校验 |
| 多屏 / DPI | 底板与卡片用同一套物理像素换算 | 复用 `PointToScreen` + `DpiScale` 的现有逻辑 |
| 视频子窗口 | 视频窗在卡片上面，底板在卡片下面，互不影响 | 无需改动 |
| 设置 / 托盘窗口 | 底板只模糊「它后面」的内容，设置窗会正常入镜（比现在更符合直觉） | — |
| 阴影 | 底板只覆盖内容区，阴影仍由 WPF 画；阴影区没有模糊 | 阴影本来就该透出桌面 |

工作量估计：底板窗口 + 合成器初始化 + 跟随逻辑约 300–400 行 C#，加一个设置项与回退分支；
调试重点在 z 序和 Win10 行为。

### C. 液态玻璃视觉层 → 完全可行，成本最低

不需要任何系统 API：

- 卡片顶部一条 1px 白色 16% 渐变高光，底部一条 3.5% 反光（WPF `Border` + `LinearGradientBrush`，
  或 `TextCanvas` 上方再叠一层 `Border`）；
- 外侧 1px 渐变描边（上亮下暗），模拟玻璃边缘的折射；
- 圆角附近加一点方向性高光（径向渐变）；
- 可选极轻的噪声纹理（`DrawingBrush` 程序生成），让大面积模糊不发“塑料”。

建议先做 C（视觉），再评估 B（真模糊）。两者不冲突。

### D. 真折射 / 色散（Apple 的透镜感）→ 不可行

需要“采样背景 → 按法线位移 → 色散”的着色器链路。Windows 公开 API 里没有背景纹理给你采样
（`HostBackdropBrush` 只输出模糊结果，`PrintWindow` 拿到的是位图但 CPU 做位移不现实），
DWM 的私有实现不可调用。只能做“看起来像”的高光与渐变，不做真实位移。

### E. macOS 原生材质 → 不适用

`NSGlassEffectView` / `NSVisualEffectView` 是 AppKit 专属，本项目只发 Windows。

## 8. 如果要做：建议的 PoC 顺序（本次不做）

1. **视觉层**：卡片高光边 + 渐变描边 + 圆角高光，纯 WPF，1–2 天，风险最低；
2. **底板窗口**：单卡片验证 `HostBackdropBrush` 能否在 Win11 24H2 + Win10 22H2 上出模糊；
3. **跟随与 z 序**：拖动、缩卡、`EnsureTopmost`、托盘隐藏/显示、多屏 DPI；
4. **回退**：Win10 失败 / 关闭系统透明效果 / 高对比度时自动切回 `PrintWindow`；
5. **设置项**：玻璃实现方式 + 性能说明，默认「系统（可用时）」或「兼容」。

## 8.5 原型结果（2026-10-24）

按方案 B 做了一个原型：独立底板窗口（`WS_EX_NOREDIRECTIONBITMAP`）+ `Windows.UI.Composition`
的 `CreateHostBackdropBrush` + `DWMWA_USE_HOSTBACKDROPBRUSH`，合成器跑在带 `DispatcherQueue` 的
独立线程上。结果：

- 合成器初始化成功，但 **HostBackdropBrush 在测试机（Win11 24H2）上渲染为纯色**，
  没有采到桌面内容（截屏采样能正常出模糊）；换成 `DWMWA_SYSTEMBACKDROP_TYPE = Transient`
  同样是纯色。原因可能是 Win32 窗口缺少 XAML host 提供的 host backdrop，或该 API 只对
  WinUI 窗口生效，需要继续查。
- 因此这次**没有合入**：继续用已验证的截屏采样实现，避免用户看到一块纯色卡片。
- 下一步要么把底板换成 WinUI/DesktopWindowXamlSource 宿主，要么回到 WGC + D3D11；
  两者都需要 Windows SDK 投影，先记在方案 E 里。

## 9. 本次不实现的原因

- 需要新增 WinRT 投影依赖，会影响「只发框架依赖版」的发布策略与包体；
- Win10 的回退、z 序边界（UAC、任务视图、全屏独占、其它置顶工具）需要真机矩阵，当前只有一台机器；
- 现有 `PrintWindow` 方案已经在真机验证过，先把媒体卡片与解码器稳定下来；
- 视觉层（C）可以随时单独做，不必等底板方案。

## 10. 参考

- 仓库：<https://github.com/Javis603/token-monitor>（commit `e1c393c`）
- 关键文件：
  - [`src/windows-dock/DockBackdrop.cs`](https://github.com/Javis603/token-monitor/blob/main/src/windows-dock/DockBackdrop.cs)（独立底板 + `HostBackdropBrush`）
  - [`src/windows-dock/NativeCurve.cs`](https://github.com/Javis603/token-monitor/blob/main/src/windows-dock/NativeCurve.cs)（`IGeometrySource2D` 任意形状）
  - [`src/electron/windowsBackdrop.js`](https://github.com/Javis603/token-monitor/blob/main/src/electron/windowsBackdrop.js)（AccentBlurBehind）
  - [`src/electron/windowsChrome.js`](https://github.com/Javis603/token-monitor/blob/main/src/electron/windowsChrome.js)（DWM 圆角 / 边框）
  - [`src/electron/macLiquidGlass.js`](https://github.com/Javis603/token-monitor/blob/main/src/electron/macLiquidGlass.js)（`NSGlassEffectView`）
  - [`src/electron/nativeMaterialVisibility.js`](https://github.com/Javis603/token-monitor/blob/main/src/electron/nativeMaterialVisibility.js)（材质状态机与降级）
  - [`src/electron/renderer/styles.css`](https://github.com/Javis603/token-monitor/blob/main/src/electron/renderer/styles.css)（玻璃变量、液态玻璃高光）
- 微软文档：
  - [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
  - [DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)（含 `DWMWA_USE_HOSTBACKDROPBRUSH`、`DWMWA_WINDOW_CORNER_PREFERENCE`）
  - [Windows.UI.Composition 的 HostBackdropBrush](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositor.createhostbackdropbrush)
  - [CompositionGeometricClip](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositiongeometricclip)
