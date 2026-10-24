# 毛玻璃：已实现方案与备选调研

> 结论先行：`AllowsTransparency=true`（`WS_EX_LAYERED` + 逐像素 alpha）与 DWM 的系统亚克力**结构性互斥**——
> 材质由 DWM 画在窗口的 redirect surface 之下，而分层窗口提交的是一张带 alpha 的位图，DWM 不会为它绘制材质
> （典型表现：整窗发黑或纯透明）。所以自 v2.1 起用的是**自采 + 模糊**路线，具体见下文「已实现」。
>
> 另：token-monitor 用「**独立底板窗口** + `HostBackdropBrush`」绕开了这个冲突——模糊画在卡片
> 下面的另一个 HWND 上，WPF 卡片继续保留分层窗口。拆解与可行性见
> [liquid-glass.md](liquid-glass.md)。

## 已实现（v2.1）

把卡片**后面**的画面按像素拼出来，再缩小模糊，代码在
[`src/Ui/GlassCapture.cs`](../src/Ui/GlassCapture.cs)，窗口侧调度在
[`OverlayWindow.xaml.cs`](../src/Ui/OverlayWindow.xaml.cs) 的「毛玻璃」一节。

| 步骤 | 做法 |
| --- | --- |
| 1. 收集层 | `EnumWindows` 按 z 序（上 → 下）枚举与卡片矩形相交、可见、非本进程的顶层窗口；**桌面窗口（`Progman`）按它真实的 z 序位置插入**，不强行垫底 |
| 2. 过滤 | 跳过本进程窗口（叠层自己）；跳过截图 / 录屏浮层（类名含 `screenclip` / `snipping` / `screenshot`，或进程是 `SnippingTool` / `ScreenSketch` / `Snipaste` / `ShareX` / `NVIDIA Share` …），避免一截屏毛玻璃就变色 |
| 3. 取图 | 每层用 `PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)` 画进 32bpp 离屏位图；普通窗口缓存 150 ms，桌面缓存 2000 ms；全黑的层直接丢弃 |
| 4. 合成 | 目标图只有卡片尺寸的 1/3–1/8；**每个采样点取“盖住它的最上层”那一层的像素**（否则取桌面），所以卡片跨窗口、压住窗口边角、或只压在桌面上都正确 |
| 5. 模糊 | 两遍可分离方框模糊，`ImageBrush` + 高质量插值放大成卡片底色 |

**三种情况为什么都对**：

- **只压在桌面上**：层里只有 `Progman`，取到的就是壁纸（连桌面图标一起，模糊后是柔和的色块）；
- **压在窗口边角**：窗口层只覆盖它自己的矩形，卡片其余部分自动落到桌面层，不会像早期版本那样
  把窗口边缘的像素拉出来涂抹成一条硬边；
- **截屏时**：截图工具的浮层被第 2 步过滤掉，玻璃仍然取下面真正的内容，颜色不会突变。

**已知限制**：

- 只取“外部窗口 + 桌面”，**本进程自己的窗口**（设置窗口、媒体视频窗）不会出现在玻璃里；
- 目标窗口若禁用 `PrintWindow`（少数独占全屏 / 硬件覆盖层程序），那一层被丢弃，会退到桌面层；
- 被别的窗口遮挡时，取到的是各窗口自身内容，不是屏幕合成后的结果（遮挡对叠层没有意义，因为叠层永远在最上面）；
- 「显示桌面」状态下 `Progman` 会跑到 z 序最前，这里保留了真实顺序，所以仍是壁纸而不是被遮住的窗口。

**为什么不用「抓屏 + 临时关闭截屏可见性」**（v2.0 的做法）：`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`
是**窗口级、全局生效**的开关——抓屏那一刻叠层对所有截屏 / 录像 / 共享都不可见。每帧切换一次，
意味着 OBS 里叠层会周期性闪没。`PrintWindow` 完全不碰屏幕抓取，因此截图、录屏、共享屏幕里叠层始终可见。

**为什么不用推荐方案 E（WGC + D3D11）**：`Windows.Graphics.Capture` 需要 WinRT 投影
（`Microsoft.Windows.SDK.NET` + `net8.0-windows10.0.19041.0`），会把单文件发布变成依赖 Windows SDK 投影包，
并且仍然要自己实现 D3D11 设备 / 帧池 / 采样器与 `D3DImage` 互操作。当前 `PrintWindow` 路线在真机上
（1920×1080、多张卡片）稳定跑在 15 帧/秒、CPU 不到一个核的 15%，视觉差异很小。真要继续升级，
改造点只在 `GlassCapture` 内部，窗口逻辑不用动。

## 备选方案对比（调研）

| 方案 | 实时 | 与分层窗口共存 | 成本 | 结论 |
| --- | --- | --- | --- | --- |
| A. DWM 系统背景材质（Win11 22621+） | 是 | **否** | 低 | 只能配非分层窗口，需双窗 |
| B. `SetWindowCompositionAttribute` | 是 | 未定义 | 低 | 未文档化、无模糊半径、整窗矩形 |
| C. Windows App SDK SystemBackdrop | 是 | 否 | 高 | 要 XAML Islands / DComp target |
| D. HostBackdropBrush + DComp（独立底板窗口） | 是 | **是**（底板是另一个 HWND） | 中高 | 真模糊、CPU≈0；需 WinRT 投影，Win10 待验证 |
| E. WGC + D3D11 模糊 | 是 | 是 | 中高 | 目标方案，当前未做（见上） |
| **F. PrintWindow + CPU 模糊（已实现）** | 15 fps | 是 | 低 | **当前方案** |

### A. DWM 系统背景材质（Win11 22621+）

`DwmExtendFrameIntoClientArea(hwnd, { -1,-1,-1,-1 })` 让材质铺满整窗，再用
`DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE=38, &type, 4)`：

| 值 | 含义 |
| --- | --- |
| 2 | `DWMSBT_MAINWINDOW`（Mica，只取壁纸，不实时） |
| 3 | `DWMSBT_TRANSIENTWINDOW`（桌面亚克力，实时） |
| 4 | `DWMSBT_TABBEDWINDOW`（Mica Alt） |

- 必须 `AllowsTransparency=false`；穿透只能靠 `WS_EX_TRANSPARENT` + `WM_NCHITTEST → HTTRANSPARENT`（区域级）。
- 实时、开销≈0；Win10 调用返回 `E_INVALIDARG`；RDP / 关闭透明效果 / 省电 / 高对比度会静默退化为纯色。
- WPF 的圆角、投影、`SizeToContent` 都要自己处理。

### B. SetWindowCompositionAttribute（未文档化）

`ACCENT_POLICY{ AccentState=4 ACRYLICBLURBEHIND, AccentFlags=2, GradientColor=0xAABBGGRR }`。
实时但**没有模糊半径**、整窗矩形都会被模糊，分层窗口上的行为未定义，系统更新可能随时失效。

### C. Windows App SDK SystemBackdrop

`DesktopAcrylicController` / `MicaController` 需要目标实现 `ICompositionSupportsSystemBackdrop`，WPF 窗口不实现；
得挂 `DesktopWindowXamlSource` 或自建 DComp target，底层仍要求非分层窗口，成本最高。

### D. HostBackdropBrush + DirectComposition（独立底板窗口）

`DWMWA_USE_HOSTBACKDROPBRUSH=17` + `Compositor.CreateHostBackdropBrush()` + `CompositionGeometricClip`，
画在**卡片下面的一个独立 HWND** 上（`WS_EX_NOREDIRECTIONBITMAP`，无分层），再由
`SetWindowPos(backdrop, cardHwnd, ...)` 插到卡片正下方。

- 这样就不占用 WPF 窗口的内容来源，卡片可以继续 `AllowsTransparency=true`；
- 真模糊（GPU）、零截屏，Win11 实时亚克力同一套材质；
- 需要 `Windows.UI.Composition` 投影（`net8.0-windows10.0.19041.0` / CsWinRT），Win10 行为待验证；
- 详细拆解与 PoC 计划见 [liquid-glass.md](liquid-glass.md)。

### E. WGC + GPU 模糊

`Windows.Graphics.Capture`（`Direct3D11CaptureFramePool` + `GraphicsCaptureSession`）抓窗口背后区域，
D3D11 降采样 + 两遍高斯，输出 `D3DImage` 或直接走 DComp。

- 与 layered / 穿透 / 多屏完全兼容，逐像素 alpha 保留；30–60 fps，GPU 1–3 ms，CPU≈0。
- 坑：捕获会包含自身；`WDA_EXCLUDEFROMCAPTURE` 常驻会污染用户截图 / 共享；
  HDR 需要 `R16G16B16A16_FLOAT`；每屏按物理像素取 rect，PerMonitorV2 换算。
- 系统版本：WGC 需要 Windows 10 1803（17134）+，`IsSupported()` 运行期判断；
  去边框 `IsBorderRequired` 需要 Windows 11（22000）+。

### F. 明确不可行

`DwmEnableBlurBehindWindow`（Win8 起不再产生模糊）、单独 `DwmExtendFrameIntoClientArea`（只透明不模糊）、
Magnification API / `DwmRegisterThumbnail`（无模糊）、注入 `dwm.exe` 私有接口。

## 版本与性能小结

| 项目 | 要求 / 数值 |
| --- | --- |
| 当前方案（PrintWindow） | Windows 7+ 均可；多张卡片 15 fps 时约占单个 CPU 核心的 10–15%，单窗口单次取样约 1–2 ms |
| WGC（方案 E） | Windows 10 1803+；GPU 1–3 ms/帧，CPU ≈ 0 |
| DWM 材质（方案 A） | Windows 11 22H2（22621）+，且必须非分层窗口 |
| `WDA_EXCLUDEFROMCAPTURE` | Windows 10 2004（19041）+（当前方案不再使用） |

## 参考

- [PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)
- [DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- [DwmEnableBlurBehindWindow（Win8 起失效）](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow)
- [Windows 应用中的材料（回退条件）](https://learn.microsoft.com/zh-cn/windows/apps/develop/ui/materials)
- [Windows.Graphics.Capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
