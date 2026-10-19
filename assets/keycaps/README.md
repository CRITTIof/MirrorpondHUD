<h1 align="center">ReiKeycap</h1>

<p align="center">
  <b>透明、贴合内容的键帽与系统图标字体：83 个键帽 + 12 个图标，共用一套 1em 网格。</b><br>
  <sub>A transparent, content-fitted keycap and system-icon font with 83 keycaps and 12 icons on a single 1em grid.</sub>
</p>

<p align="center">
  <img alt="version" src="https://img.shields.io/badge/version-1.4.0-555">
  <img alt="glyphs" src="https://img.shields.io/badge/glyphs-95-2C8EBB">
  <img alt="keycaps" src="https://img.shields.io/badge/keycaps-83-0078D6">
  <img alt="license" src="https://img.shields.io/badge/license-OFL--1.1-3DA639">
</p>

<p align="center">
  <img src="docs/preview.png" width="760" alt="ReiKeycap 实时键盘演示">
</p>

<p align="center">
  <a href="#简体中文">简体中文</a> · <a href="#english">English</a>
</p>

<a id="简体中文"></a>

依据实拍 Windows 键盘重绘。所有键帽边框、图例和系统图标都落在同一个 **1em / 62 设计单位** 的显示槽里，因此放大缩小都不会错位。字体只画轮廓，内部透明，颜色完全交给 `currentColor`。

## ✨ 特性

- **95 个字形** — 83 个键帽 + 12 个系统图标，覆盖 101 个 `event.code`
- **统一网格** — 每个字形占满 1em，键帽高 1000 字体单位，混排不跳动
- **透明内部** — 单色轮廓，跟随文字颜色，适合浅色 / 深色界面
- **精确映射** — 每个字形有固定 PUA 码位；`A–Z`、`a–z`、`0–9` 可直接当别名
- **等宽字母** — 字母和数字使用 JetBrains Mono SemiBold，单键宽度一致
- **内容自适应** — `Esc`、`Ctrl`、`PgUp` 等键帽按图例宽度收缩，不留空白
- **零运行时依赖** — 构建只需要 Python、`fonttools` 和 `brotli`

## 🚀 快速开始

到 **[Releases](https://github.com/CRITTIof/rei-keycap-font/releases/latest)** 下载，四个文件按需选择：

| 文件 | 用途 | 说明 |
| --- | --- | --- |
| `ReiKeycap-Regular.ttf` | 桌面安装 | Windows / macOS 双击安装 |
| `ReiKeycap-Regular.woff2` | 网页字体 | 体积小，适合 `@font-face` |
| `MAPPING.md` | 映射文档 | 95 个 PUA、62 个 ASCII 别名、101 个 `event.code` |
| `LICENSE` | 许可证 | SIL Open Font License 1.1 |

**桌面安装**：Windows 双击 TTF 选择“安装”；macOS 用“字体册”打开后选择“安装字体”。

**网页使用**：

```css
@font-face {
  font-family: "ReiKeycap";
  src: url("fonts/ReiKeycap-Regular.woff2") format("woff2");
  font-weight: 400;
  font-style: normal;
  font-display: block;
}

.key {
  font: 400 38px/1 "ReiKeycap";
  color: currentColor;
}
```

`font-size` 就是键帽的视觉高度。保持 `line-height: 1`，颜色用 `color` / `currentColor` 控制。

**本地演示**：打开仓库里的 [demo.html](demo.html)，支持物理按键、点击键帽、明暗主题和字号调节，无需构建或本地服务器。

## 🧩 映射

每个字形有三种取法：

1. **PUA 码位** — 精确指定某个键帽或图标。
2. **ASCII 别名** — `A–Z`、`a–z`、`0–9` 直接映射到对应键帽；`A` 和 `a` 都显示大写 A。
3. **`event.code`** — 先查表转成码位，再渲染。

| 范围 | 内容 |
| --- | --- |
| `U+E000–U+E052` | 83 个键帽 |
| `U+E053–U+E05E` | 12 个系统图标 |
| `A–Z`、`a–z`、`0–9` | 键帽的 ASCII 别名 |

```html
<span class="key">&#xE000;</span>  <!-- Esc -->
<span class="key">&#xE053;</span>  <!-- CPU -->
<span class="key">&#xE059;</span>  <!-- CLI -->
<span class="key">A</span>         <!-- A 键帽 -->
```

字体本身不处理键盘事件。`demo.html` 里的 `KEYS` 数组包含全部 101 条映射，可以直接复制：

```js
document.addEventListener("keydown", (event) => {
  const key = KEYS.find((item) => item.code === event.code);
  if (key) output.textContent = String.fromCodePoint(key.codepoint);
});
```

完整中文对照表见 **[MAPPING.md](MAPPING.md)**。

<details>
<summary><b>12 个系统图标</b>（点击展开）</summary>

| 软件 | PUA | 硬件 | PUA |
| --- | --- | --- | --- |
| CLI | `U+E059` | CPU | `U+E053` |
| Web | `U+E05A` | GPU | `U+E054` |
| App | `U+E05B` | RAM | `U+E055` |
| Game | `U+E05C` | ROM / 硬盘 | `U+E056` |
| | | NetUp | `U+E057` |
| | | NetDown | `U+E058` |
| | | USB | `U+E05D` |
| | | Vol | `U+E05E` |

</details>

## 🔨 构建

需要 Python 3.10+ 和 [requirements.txt](requirements.txt) 中的依赖。

```bash
python -m venv .venv
# Windows
.venv\Scripts\activate
# macOS / Linux
# source .venv/bin/activate

pip install -r requirements.txt
python src/build_font.py
```

输出：

```text
fonts/ReiKeycap-Regular.ttf
fonts/ReiKeycap-Regular.woff2
```

构建不依赖 Node.js、浏览器、图像库或网络。修改键位和图标分类看 `src/data.py`，修改轮廓和几何看 `src/geometry.py`，修改字体元数据看 `src/build_font.py`。

## 🗂️ 目录结构

```text
rei-keycap-font/
├─ demo.html                     实时键盘演示
├─ docs/preview.png              README 预览图
├─ fonts/
│  ├─ ReiKeycap-Regular.ttf      桌面字体
│  └─ ReiKeycap-Regular.woff2    Web 字体
├─ MAPPING.md                    中文完整映射文档
├─ README.md
├─ LICENSE                       SIL OFL 1.1
├─ requirements.txt
├─ assets/                       Noto Sans / JetBrains Mono 源字体
└─ src/
   ├─ build_font.py              构建入口
   ├─ data.py                    键位、图例与图标分类
   └─ geometry.py                字形轮廓与几何
```

逐字形 SVG、构建中间文件和 ZIP 不进入仓库；`docs/preview.png` 只用于 README 预览。Release 只保留字体、映射文档和许可证。

## ⚠️ 注意

- **不要用于正文。** ASCII 字母和数字被有意映射为键帽，正文请使用普通文本字体。
- **字体不知道键盘事件。** 需要自己把 `event.code` 映射到码位。
- 图标是象形符号，没有 `event.code`；`ROM` 沿用键盘丝印，但图形实际是硬盘。

## 📄 许可

字体以 **[SIL Open Font License 1.1](LICENSE)** 发布，可自由使用、嵌入、修改和再分发；再分发时请保留 `LICENSE`，不要单独售卖字体文件。用字体排版的文档、图片和网页不受该许可约束。

字形轮廓来自 **Noto Sans SemiBold**（Copyright 2018 The Noto Project Authors）和 **JetBrains Mono SemiBold**（Copyright 2020 The JetBrains Mono Project Authors），两者同样以 OFL 1.1 使用。生成字体命名为 **ReiKeycap**，未使用源字体的保留名称。

<p align="center"><sub>ReiKeycap v1.4.0 · 95 glyphs · SIL Open Font License 1.1</sub></p>

---

<a id="english"></a>

## English

**A transparent, content-fitted keycap and system-icon font**, redrawn from a photographed Windows keyboard. Every keycap frame and icon occupies the same **1em / 62-unit** slot, so they stay aligned at any size. The interior is transparent; color comes from `currentColor`.

- **95 glyphs** — 83 keycaps + 12 system icons, covering 101 `event.code` values
- **101 key mappings** and **62 ASCII aliases** (`A–Z`, `a–z`, `0–9`)
- **TTF + WOFF2**, 1000 units/em, all glyphs exactly 1em tall
- **Single-color outlines** with a transparent interior
- **No runtime dependencies**; build with Python, `fonttools` and `brotli`

### Quick start

Download the fonts from **[Releases](https://github.com/CRITTIof/rei-keycap-font/releases/latest)**.

| File | Purpose |
| --- | --- |
| `ReiKeycap-Regular.ttf` | Desktop install |
| `ReiKeycap-Regular.woff2` | Web font |
| `MAPPING.md` | Full Chinese mapping reference |
| `LICENSE` | SIL Open Font License 1.1 |

```css
@font-face {
  font-family: "ReiKeycap";
  src: url("fonts/ReiKeycap-Regular.woff2") format("woff2");
}
.key { font: 400 38px/1 "ReiKeycap"; color: currentColor; }
```

```html
<span class="key">&#xE000;</span>  <!-- Esc -->
<span class="key">&#xE053;</span>  <!-- CPU -->
<span class="key">A</span>         <!-- A keycap -->
```

Use PUA codepoints for exact glyphs. ASCII letters and digits are aliases for the matching keycaps. **Do not use this font for body text.** See **[MAPPING.md](MAPPING.md)** for the full PUA / ASCII / `event.code` tables, or copy the `KEYS` array from [demo.html](demo.html).

### Build

```bash
pip install -r requirements.txt
python src/build_font.py
```

Outputs `fonts/ReiKeycap-Regular.ttf` and `.woff2`. Python 3.10+ only; no Node.js, browser or image tooling required.

### License

Released under the **[SIL Open Font License 1.1](LICENSE)**. Outlines are derived from **Noto Sans SemiBold** (Copyright 2018 The Noto Project Authors) and **JetBrains Mono SemiBold** (Copyright 2020 The JetBrains Mono Project Authors), also under OFL 1.1. The generated font is named **ReiKeycap** and uses no reserved name from either source font.

<p align="center"><a href="#简体中文">返回中文 ↑</a></p>
