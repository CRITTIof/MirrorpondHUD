# 毛玻璃：两种实现的取舍（以及为什么默认不推荐）

<a id="中文"></a>

**一句话**：毛玻璃是这个项目里唯一「没有正确答案」的功能。两条实现都能用，但都有无法在本项目里消除的
硬伤，所以它**默认关闭**，也不建议为了观感去开。只想要半透明卡片的话，把「背景不透明度」调到
0.5–0.7 就够了，没有任何额外开销。

> 本文只讲**现在**这两条路。历史调研（WGC / WinUI 3 / DWM 文档化材质逐项试错）没有随仓库发布。

## 1. 两条路

| | 系统亚克力（默认） | 兼容（截屏取样） |
| --- | --- | --- |
| 做法 | 每张卡片下面再放一个**从不绘制的 HWND**，给它未文档化的 `WCA_ACCENT_POLICY = ACRYLICBLURBEHIND`，由 DWM 在那个矩形里实时模糊 | 枚举卡片矩形内的外部窗口 → `PrintWindow` 逐层取图 → 按像素取「盖住这个点的最上层」→ 缩小、多次盒式模糊 |
| 谁在干活 | DWM / GPU | 本进程（后台线程 + 定时器） |
| 实测 CPU（5 张卡，身后一个 820×560 窗口） | **1.5–2.2%** 单核（≈ 关闭毛玻璃的 1.6%） | **≈ 41%** 单核（纯壁纸背景时 8–14%） |
| 模糊程度 | 由系统决定，不可调 | 0–40px 可调，帧率 2–30 可调 |
| 截图 / 录屏 | 正常 | 正常（不抓屏，也不会把自己拍进去） |

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

## 4. 所以要怎么选

- **先别开**。需要「卡片像玻璃」的观感，只调「背景不透明度」就能拿到 90% 的效果，CPU 为 0。
- 一定要开：用**系统亚克力**（默认），并把圆角控制在 12px 以内。
- 圆角必须很大、或者需要在 Win10 / 远程桌面上稳定一致：切**兼容**，把帧率降到 8–10、
  模糊 8–12px，并且只留一两张卡片开玻璃。
- 切换位置：设置 → 外观 → 高级（实验性）→「玻璃实现」。日志里会写明这一帧走的是哪条路：

  ```
  [INFO] 毛玻璃改用系统亚克力底板：模糊在 DWM / GPU 上完成，进程侧不再抓屏
  [WARN] 系统亚克力底板不可用（系统「透明效果」已关闭），毛玻璃改用截屏取样
  ```

---

<a id="english"></a>

# Frosted glass: the two implementations, and why it stays off by default

**Short version:** frosted glass is the one feature in this project with no clean answer. Both paths work,
both have flaws that cannot be fixed inside this project, so it ships **disabled**. If all you want is a
translucent card, set *Background opacity* to 0.5–0.7 — that costs nothing and looks close enough.

This page covers the two implementations that exist **now**; the older research (WGC, WinUI 3, documented
DWM materials, and the dead ends they led to) is not published with the repository.

## 1. The two paths

| | System acrylic (default) | Compat (screen sampling) |
| --- | --- | --- |
| How | A never-drawn HWND sits **under each card** with the undocumented `WCA_ACCENT_POLICY = ACRYLICBLURBEHIND`; DWM blurs that rectangle live | Enumerate the windows behind the card → `PrintWindow` each of them → pick the topmost layer per pixel → downscale and box-blur |
| Who pays | DWM / GPU | This process (worker thread + timer) |
| Measured CPU (5 cards, one 820×560 window behind) | **1.5–2.2%** of one core (≈ 1.6% with glass off) | **≈ 41%** of one core (8–14% over a plain wallpaper) |
| Blur amount | Chosen by the system | 0–40 px, 2–30 fps, both adjustable |
| Screenshots / recording | Normal | Normal (it never captures the screen, so it cannot include itself) |

## 2. What is wrong with system acrylic

1. **Undocumented API.** `SetWindowCompositionAttribute` is private; Microsoft may change it in any release.
   Failures fall back to compat automatically, but "the look changed after an update" is baked in.
2. **Rounded cards show square corner slivers.** The accent region is always the **window rectangle** — it
   honours neither `SetWindowRgn` nor DWM's corner preference. Above ~12 px radius the four corners show
   untinted glass: see `glass-system-corner.png` above.
3. **Brightness comes from the tint, not the wallpaper.** DWM acrylic mostly ignores alpha; the gradient
   colour sets the brightness and only the hue comes through. Dark cards therefore stay smoke-grey and look
   dull on light wallpapers.
4. **System switches decide everything.** "Transparency effects" off, remote desktop, or high contrast
   degrade the material to a flat fill and force a fallback.
5. **One extra HWND per card**, and Windows 10 support is theoretical — there is no real test matrix behind it.

## 3. What is wrong with the compat path

1. **The CPU cost is real.** Every frame calls `PrintWindow` for every window, then composites per pixel,
   downscales and blurs. More cards, larger glass, larger windows behind — all linear (41% of one core above).
2. **It samples window contents, not the composed screen.** Occluded areas cannot be recovered; z-order is
   approximated from window enumeration.
3. **What it cannot see:** this process's own windows (settings window, video window); animated wallpapers
   (D3D swap chains such as Wallpaper Engine return pure black from `PrintWindow`, so the static wallpaper is
   used instead); exclusive fullscreen and hardware overlays.
4. **It feels laggy.** 15 fps is visible while dragging a window or scrolling a page, and raising the frame
   rate only makes it more expensive.

## 4. So what should you do

- **Leave it off.** Opacity alone gets you 90% of the look for zero CPU.
- If you really want it: keep **system acrylic** (the default) and keep the corner radius at or below 12 px.
- If you need a large radius, or consistent behaviour on Windows 10 / remote desktop, switch to **compat**,
  drop to 8–10 fps with an 8–12 px blur, and only turn glass on for one or two cards.
- Switch it in *Settings → Appearance → Advanced (experimental) → Glass implementation*. The log always says
  which path is live:

  ```
  [INFO] using the system acrylic backdrop: blur runs in DWM / on the GPU, no capture in this process
  [WARN] system acrylic unavailable (transparency effects are off), falling back to screen sampling
  ```
