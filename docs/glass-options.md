# 毛玻璃实现调研（未实现）

> 结论先行：`AllowsTransparency=true`（`WS_EX_LAYERED` + 逐像素 alpha）与 DWM 的系统亚克力**结构性互斥**——
> 材质由 DWM 画在窗口的 redirect surface 之下，而分层窗口提交的是一张带 alpha 的位图，DWM 不会为它绘制材质
> （典型表现：整窗发黑或纯透明）。要同时满足「透明 + 鼠标穿透 + 置顶 + 实时」的只有**自采 + GPU 模糊**。

## 方案对比

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

### D. HostBackdropBrush + DirectComposition

`DWMWA_USE_HOSTBACKDROPBRUSH=17` + `Compositor.CreateHostBackdropBrush()` + `CompositionEffectBrush`，
经 DComp 呈现；会接管 HWND 内容来源，与 WPF / layered 不共存。

### E. 自采 + GPU 模糊（推荐）

`Windows.Graphics.Capture`（`Direct3D11CaptureFramePool` + `GraphicsCaptureSession`）抓窗口背后区域，
D3D11 降采样 + 两遍高斯，输出 `D3DImage` 或直接走 DComp。

- 与 layered / 穿透 / 多屏完全兼容，逐像素 alpha 保留；30–60 fps，GPU 1–3 ms，CPU≈0。
- 改造点只在 `GlassCapture.Grab`（见 [`src/Ui/GlassCapture.cs`](../src/Ui/GlassCapture.cs)），窗口逻辑全部保留。
- 坑：捕获会包含自身（`WDA_EXCLUDEFROMCAPTURE` 常驻会污染用户截图 / 共享；现有「抓前切换」有 20 ms 黑闪）；
  HDR 需要 `R16G16B16A16_FLOAT`；每屏按物理像素取 rect，PerMonitorV2 换算。

### F. 明确不可行

`DwmEnableBlurBehindWindow`（Win8 起不再产生模糊）、单独 `DwmExtendFrameIntoClientArea`（只透明不模糊）、
Magnification API / `DwmRegisterThumbnail`（无模糊）、注入 `dwm.exe` 私有接口。

## 建议

1. **首选 E**：把 `GlassCapture` 换成 `Windows.Graphics.Capture` + D3D11 模糊，刷新从 1.2 s 提到实时，
   卡片多开时用共享抓屏调度（一次抓取服务多张卡）。
2. **Win11 22H2+ 可选双窗**：非分层背景窗（A 的 `DWMSBT_TRANSIENTWINDOW`）+ 现有 layered 内容窗只画 UI；
   Win10 自动回退 E。代价是 z 序 / 层间对齐 / 点击穿透要自己维护。
3. 不推荐 C/D：收益不如 A/E，复杂度更高。

## 参考

- [DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- [DwmEnableBlurBehindWindow（Win8 起失效）](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow)
- [Windows 应用中的材料（回退条件）](https://learn.microsoft.com/zh-cn/windows/apps/develop/ui/materials)
- [System backdrops 控制器](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)
- [DesktopAcrylicController](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.systembackdrops.desktopacryliccontroller)
- [Windows.Graphics.Capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
