# ReiconFont 映射文档

版本 **1.8.0** · **119** 个字形 · **106** 个 `event.code` · **62** 个 ASCII 别名 · **181** 个 cmap 条目。

本文说明字符、PUA 码位和浏览器键盘事件如何对应到 **ReiconFont** 中的字形。

## 1. 三种取字方式

1. **PUA 码位**：每个字形都有固定码位（键帽 `U+E000` 起，软件图标 `U+E100` 起，硬件图标 `U+E200` 起，界面图标 `U+E300` 起），适合精确指定某个键帽或图标。
2. **ASCII 别名**：`A–Z`、`a–z`、`0–9` 直接映射到对应键帽；`A` 与 `a` 都会显示大写 A 键帽。
3. **`event.code`**：先查下表把物理键映射到码位，再用 `String.fromCodePoint(codepoint)` 渲染。

```css
@font-face {
  font-family: "ReiconFont";
  src: url("fonts/ReiconFont-Regular.woff2") format("woff2");
}

.key { font: 400 38px/1 "ReiconFont"; color: currentColor; }
```

```html
<span class="key">&#xE000;</span>   <!-- Esc：键帽 -->
<span class="key">&#xE100;</span>   <!-- CLI：软件图标 -->
<span class="key">&#xE200;</span>   <!-- CPU：硬件图标 -->
<span class="key">&#xE300;</span>   <!-- 内容：界面图标 -->
<span class="key">A</span>          <!-- A：ASCII 别名 -->
```

> **注意：** ASCII 字母和数字被有意映射为键帽。不要把本字体用于正文；正文请使用普通文本字体。

## 2. PUA 码位表

键帽连续占用 `U+E000–U+E056`；软件图标（含文件与 AI）从 `U+E100` 开始，硬件图标从 `U+E200` 开始，界面图标从 `U+E300` 开始。每个区块预留 256 个码位，块内和块间的空位都留给后续扩展。

| PUA | 字形 ID | 类型 | 分类 | 图例 | 名称 | ASCII 别名 |
|---|---|---|---|---|---|---|
| <code>U+E000</code> | <code>escape</code> | 键帽 | 功能键 | <code>Esc</code> | Escape | — |
| <code>U+E001</code> | <code>f1</code> | 键帽 | 功能键 | <code>F1</code> | F1 | — |
| <code>U+E002</code> | <code>f2</code> | 键帽 | 功能键 | <code>F2</code> | F2 | — |
| <code>U+E003</code> | <code>f3</code> | 键帽 | 功能键 | <code>F3</code> | F3 | — |
| <code>U+E004</code> | <code>f4</code> | 键帽 | 功能键 | <code>F4</code> | F4 | — |
| <code>U+E005</code> | <code>f5</code> | 键帽 | 功能键 | <code>F5</code> | F5 | — |
| <code>U+E006</code> | <code>f6</code> | 键帽 | 功能键 | <code>F6</code> | F6 | — |
| <code>U+E007</code> | <code>f7</code> | 键帽 | 功能键 | <code>F7</code> | F7 | — |
| <code>U+E008</code> | <code>f8</code> | 键帽 | 功能键 | <code>F8</code> | F8 | — |
| <code>U+E009</code> | <code>f9</code> | 键帽 | 功能键 | <code>F9</code> | F9 | — |
| <code>U+E00A</code> | <code>f10</code> | 键帽 | 功能键 | <code>F10</code> | F10 | — |
| <code>U+E00B</code> | <code>f11</code> | 键帽 | 功能键 | <code>F11</code> | F11 | — |
| <code>U+E00C</code> | <code>f12</code> | 键帽 | 功能键 | <code>F12</code> | F12 | — |
| <code>U+E00D</code> | <code>insert</code> | 键帽 | 功能键 | <code>Ins</code> | Insert | — |
| <code>U+E00E</code> | <code>delete</code> | 键帽 | 功能键 | <code>Del</code> | Delete | — |
| <code>U+E00F</code> | <code>home</code> | 键帽 | 功能键 | <code>Home</code> | Home | — |
| <code>U+E010</code> | <code>end</code> | 键帽 | 功能键 | <code>End</code> | End | — |
| <code>U+E011</code> | <code>pageup</code> | 键帽 | 功能键 | <code>PgUp</code> | Page Up | — |
| <code>U+E012</code> | <code>pagedown</code> | 键帽 | 功能键 | <code>PgDn</code> | Page Down | — |
| <code>U+E013</code> | <code>backquote</code> | 键帽 | 数字区 | <code>`</code> | Backquote | — |
| <code>U+E014</code> | <code>digit1</code> | 键帽 | 数字区 | <code>1</code> | 1 | <code>1</code> |
| <code>U+E015</code> | <code>digit2</code> | 键帽 | 数字区 | <code>2</code> | 2 | <code>2</code> |
| <code>U+E016</code> | <code>digit3</code> | 键帽 | 数字区 | <code>3</code> | 3 | <code>3</code> |
| <code>U+E017</code> | <code>digit4</code> | 键帽 | 数字区 | <code>4</code> | 4 | <code>4</code> |
| <code>U+E018</code> | <code>digit5</code> | 键帽 | 数字区 | <code>5</code> | 5 | <code>5</code> |
| <code>U+E019</code> | <code>digit6</code> | 键帽 | 数字区 | <code>6</code> | 6 | <code>6</code> |
| <code>U+E01A</code> | <code>digit7</code> | 键帽 | 数字区 | <code>7</code> | 7 | <code>7</code> |
| <code>U+E01B</code> | <code>digit8</code> | 键帽 | 数字区 | <code>8</code> | 8 | <code>8</code> |
| <code>U+E01C</code> | <code>digit9</code> | 键帽 | 数字区 | <code>9</code> | 9 | <code>9</code> |
| <code>U+E01D</code> | <code>digit0</code> | 键帽 | 数字区 | <code>0</code> | 0 | <code>0</code> |
| <code>U+E01E</code> | <code>minus</code> | 键帽 | 数字区 | <code>-</code> | Minus | — |
| <code>U+E01F</code> | <code>equal</code> | 键帽 | 数字区 | <code>=</code> | Equal | — |
| <code>U+E020</code> | <code>backspace</code> | 键帽 | 数字区 | <code>Back</code> | Backspace | — |
| <code>U+E021</code> | <code>tab</code> | 键帽 | 字母区 | <code>Tab</code> | Tab | — |
| <code>U+E022</code> | <code>keyq</code> | 键帽 | 字母区 | <code>Q</code> | Q | <code>Q</code>、<code>q</code> |
| <code>U+E023</code> | <code>keyw</code> | 键帽 | 字母区 | <code>W</code> | W | <code>W</code>、<code>w</code> |
| <code>U+E024</code> | <code>keye</code> | 键帽 | 字母区 | <code>E</code> | E | <code>E</code>、<code>e</code> |
| <code>U+E025</code> | <code>keyr</code> | 键帽 | 字母区 | <code>R</code> | R | <code>R</code>、<code>r</code> |
| <code>U+E026</code> | <code>keyt</code> | 键帽 | 字母区 | <code>T</code> | T | <code>T</code>、<code>t</code> |
| <code>U+E027</code> | <code>keyy</code> | 键帽 | 字母区 | <code>Y</code> | Y | <code>Y</code>、<code>y</code> |
| <code>U+E028</code> | <code>keyu</code> | 键帽 | 字母区 | <code>U</code> | U | <code>U</code>、<code>u</code> |
| <code>U+E029</code> | <code>keyi</code> | 键帽 | 字母区 | <code>I</code> | I | <code>I</code>、<code>i</code> |
| <code>U+E02A</code> | <code>keyo</code> | 键帽 | 字母区 | <code>O</code> | O | <code>O</code>、<code>o</code> |
| <code>U+E02B</code> | <code>keyp</code> | 键帽 | 字母区 | <code>P</code> | P | <code>P</code>、<code>p</code> |
| <code>U+E02C</code> | <code>bracketleft</code> | 键帽 | 字母区 | <code>[</code> | Left Bracket | — |
| <code>U+E02D</code> | <code>bracketright</code> | 键帽 | 字母区 | <code>]</code> | Right Bracket | — |
| <code>U+E02E</code> | <code>backslash</code> | 键帽 | 字母区 | <code>\</code> | Backslash | — |
| <code>U+E02F</code> | <code>capslock</code> | 键帽 | 主键区 | <code>Cap</code> | Caps Lock | — |
| <code>U+E030</code> | <code>keya</code> | 键帽 | 主键区 | <code>A</code> | A | <code>A</code>、<code>a</code> |
| <code>U+E031</code> | <code>keys</code> | 键帽 | 主键区 | <code>S</code> | S | <code>S</code>、<code>s</code> |
| <code>U+E032</code> | <code>keyd</code> | 键帽 | 主键区 | <code>D</code> | D | <code>D</code>、<code>d</code> |
| <code>U+E033</code> | <code>keyf</code> | 键帽 | 主键区 | <code>F</code> | F | <code>F</code>、<code>f</code> |
| <code>U+E034</code> | <code>keyg</code> | 键帽 | 主键区 | <code>G</code> | G | <code>G</code>、<code>g</code> |
| <code>U+E035</code> | <code>keyh</code> | 键帽 | 主键区 | <code>H</code> | H | <code>H</code>、<code>h</code> |
| <code>U+E036</code> | <code>keyj</code> | 键帽 | 主键区 | <code>J</code> | J | <code>J</code>、<code>j</code> |
| <code>U+E037</code> | <code>keyk</code> | 键帽 | 主键区 | <code>K</code> | K | <code>K</code>、<code>k</code> |
| <code>U+E038</code> | <code>keyl</code> | 键帽 | 主键区 | <code>L</code> | L | <code>L</code>、<code>l</code> |
| <code>U+E039</code> | <code>semicolon</code> | 键帽 | 主键区 | <code>;</code> | Semicolon | — |
| <code>U+E03A</code> | <code>quote</code> | 键帽 | 主键区 | <code>&#x27;</code> | Apostrophe | — |
| <code>U+E03B</code> | <code>enter</code> | 键帽 | 主键区 | <code>Enter</code> | Enter | — |
| <code>U+E03C</code> | <code>shift</code> | 键帽 | Shift 区 | <code>Shift</code> | Shift | — |
| <code>U+E03D</code> | <code>keyz</code> | 键帽 | Shift 区 | <code>Z</code> | Z | <code>Z</code>、<code>z</code> |
| <code>U+E03E</code> | <code>keyx</code> | 键帽 | Shift 区 | <code>X</code> | X | <code>X</code>、<code>x</code> |
| <code>U+E03F</code> | <code>keyc</code> | 键帽 | Shift 区 | <code>C</code> | C | <code>C</code>、<code>c</code> |
| <code>U+E040</code> | <code>keyv</code> | 键帽 | Shift 区 | <code>V</code> | V | <code>V</code>、<code>v</code> |
| <code>U+E041</code> | <code>keyb</code> | 键帽 | Shift 区 | <code>B</code> | B | <code>B</code>、<code>b</code> |
| <code>U+E042</code> | <code>keyn</code> | 键帽 | Shift 区 | <code>N</code> | N | <code>N</code>、<code>n</code> |
| <code>U+E043</code> | <code>keym</code> | 键帽 | Shift 区 | <code>M</code> | M | <code>M</code>、<code>m</code> |
| <code>U+E044</code> | <code>comma</code> | 键帽 | Shift 区 | <code>,</code> | Comma | — |
| <code>U+E045</code> | <code>period</code> | 键帽 | Shift 区 | <code>.</code> | Period | — |
| <code>U+E046</code> | <code>slash</code> | 键帽 | Shift 区 | <code>/</code> | Slash | — |
| <code>U+E047</code> | <code>ctrl</code> | 键帽 | 修饰键 | <code>Ctrl</code> | Control | — |
| <code>U+E048</code> | <code>fn</code> | 键帽 | 修饰键 | <code>Fn</code> | Function | — |
| <code>U+E049</code> | <code>win</code> | 键帽 | 修饰键 | <code>Win</code> | Windows | — |
| <code>U+E04A</code> | <code>alt</code> | 键帽 | 修饰键 | <code>Alt</code> | Alt | — |
| <code>U+E04B</code> | <code>space</code> | 键帽 | 修饰键 | <code>Space</code> | Space | — |
| <code>U+E04C</code> | <code>arrowleft</code> | 键帽 | 修饰键 | <code>←</code> | Arrow Left | — |
| <code>U+E04D</code> | <code>arrowup</code> | 键帽 | 修饰键 | <code>↑</code> | Arrow Up | — |
| <code>U+E04E</code> | <code>arrowdown</code> | 键帽 | 修饰键 | <code>↓</code> | Arrow Down | — |
| <code>U+E04F</code> | <code>arrowright</code> | 键帽 | 修饰键 | <code>→</code> | Arrow Right | — |
| <code>U+E050</code> | <code>num</code> | 键帽 | 小键盘 | <code>Num</code> | Num Lock | — |
| <code>U+E051</code> | <code>asterisk</code> | 键帽 | 小键盘 | <code>*</code> | Multiply | — |
| <code>U+E052</code> | <code>plus</code> | 键帽 | 小键盘 | <code>+</code> | Plus | — |
| <code>U+E053</code> | <code>printscreen</code> | 键帽 | 功能键 | <code>PrtSc</code> | Print Screen | — |
| <code>U+E054</code> | <code>scrolllock</code> | 键帽 | 功能键 | <code>ScrLk</code> | Scroll Lock | — |
| <code>U+E055</code> | <code>pause</code> | 键帽 | 功能键 | <code>Pause</code> | Pause | — |
| <code>U+E056</code> | <code>menu</code> | 键帽 | 修饰键 | <code>Menu</code> | Context Menu | — |
| <code>U+E100</code> | <code>terminal</code> | 图标 | 软件 | <code>CLI</code> | Command line | — |
| <code>U+E101</code> | <code>web</code> | 图标 | 软件 | <code>Web</code> | Web page | — |
| <code>U+E102</code> | <code>app</code> | 图标 | 软件 | <code>App</code> | Application | — |
| <code>U+E103</code> | <code>game</code> | 图标 | 软件 | <code>Game</code> | Game | — |
| <code>U+E104</code> | <code>folder</code> | 图标 | 文件 | <code>Folder</code> | Folder | — |
| <code>U+E105</code> | <code>txt</code> | 图标 | 文件 | <code>TXT</code> | Text file | — |
| <code>U+E106</code> | <code>image</code> | 图标 | 文件 | <code>Image</code> | Image | — |
| <code>U+E107</code> | <code>agent</code> | 图标 | AI | <code>AI</code> | AI Agent | — |
| <code>U+E200</code> | <code>cpu</code> | 图标 | 硬件 | <code>CPU</code> | Processor | — |
| <code>U+E201</code> | <code>gpu</code> | 图标 | 硬件 | <code>GPU</code> | Graphics processor | — |
| <code>U+E202</code> | <code>ram</code> | 图标 | 硬件 | <code>RAM</code> | Memory | — |
| <code>U+E203</code> | <code>rom</code> | 图标 | 硬件 | <code>ROM</code> | Hard drive (ROM) | — |
| <code>U+E204</code> | <code>netup</code> | 图标 | 硬件 | <code>NetUp</code> | Network uplink | — |
| <code>U+E205</code> | <code>netdown</code> | 图标 | 硬件 | <code>NetDown</code> | Network downlink | — |
| <code>U+E206</code> | <code>usb</code> | 图标 | 硬件 | <code>USB</code> | USB drive | — |
| <code>U+E207</code> | <code>volume</code> | 图标 | 硬件 | <code>Vol</code> | Volume | — |
| <code>U+E208</code> | <code>microphone</code> | 图标 | 硬件 | <code>Mic</code> | Microphone | — |
| <code>U+E300</code> | <code>content</code> | 图标 | 图标 | <code>Content</code> | Content | — |
| <code>U+E301</code> | <code>appearance</code> | 图标 | 图标 | <code>Look</code> | Appearance | — |
| <code>U+E302</code> | <code>datasource</code> | 图标 | 图标 | <code>Source</code> | Data source | — |
| <code>U+E303</code> | <code>behavior</code> | 图标 | 图标 | <code>Action</code> | Behavior | — |
| <code>U+E304</code> | <code>hotkey</code> | 图标 | 图标 | <code>Hotkey</code> | Hotkey | — |
| <code>U+E305</code> | <code>search</code> | 图标 | 图标 | <code>Search</code> | Search | — |
| <code>U+E306</code> | <code>trash</code> | 图标 | 图标 | <code>Delete</code> | Delete | — |
| <code>U+E307</code> | <code>move</code> | 图标 | 图标 | <code>Move</code> | Move | — |
| <code>U+E308</code> | <code>add</code> | 图标 | 图标 | <code>Add</code> | Add | — |
| <code>U+E309</code> | <code>expand</code> | 图标 | 图标 | <code>Expand</code> | Expand | — |
| <code>U+E30A</code> | <code>video</code> | 图标 | 图标 | <code>Video</code> | Video | — |
| <code>U+E30B</code> | <code>clock</code> | 图标 | 图标 | <code>Clock</code> | Clock | — |
| <code>U+E30C</code> | <code>keypress</code> | 图标 | 图标 | <code>Press</code> | Key press | — |
| <code>U+E30D</code> | <code>play</code> | 图标 | 图标 | <code>Play</code> | Play | — |
| <code>U+E30E</code> | <code>pausemedia</code> | 图标 | 图标 | <code>Pause</code> | Pause | — |

### 字形说明

- `ROM` 沿用键盘上的丝印；图形实际是**硬盘**（盘片、主轴、磁臂），不是 ROM 芯片。
- 32 个图标分为 **8 个软件 / 文件 / AI**（`CLI`、`Web`、`App`、`Game`、`Folder`、`TXT`、`Image`、`AI`）、**9 个硬件**（`CPU`、`GPU`、`RAM`、`ROM`、`NetUp`、`NetDown`、`USB`、`Vol`、`Mic`）和 **15 个界面图标**（`Content`、`Look`、`Source`、`Action`、`Hotkey`、`Search`、`Delete`、`Move`、`Add`、`Expand`、`Video`、`Clock`、`Press`、`Play`、`Pause`）。
- `PrtSc`、`ScrLk`、`Pause`、`Menu` 与其余键帽一样占 `U+E000` 区块，不单独分区；`MetaRight` 复用 `Win` 键帽。
- 图标是象形符号，没有 `event.code`，也不属于文本。

## 3. ASCII 别名

`A–Z` 与 `a–z` 都渲染同一字母的大写键帽，`0–9` 渲染对应数字键帽。

| 字符 | Unicode | 字形 ID | PUA |
|---|---|---|---|
| <code>0</code> | <code>U+0030</code> | <code>digit0</code> | <code>U+E01D</code> |
| <code>1</code> | <code>U+0031</code> | <code>digit1</code> | <code>U+E014</code> |
| <code>2</code> | <code>U+0032</code> | <code>digit2</code> | <code>U+E015</code> |
| <code>3</code> | <code>U+0033</code> | <code>digit3</code> | <code>U+E016</code> |
| <code>4</code> | <code>U+0034</code> | <code>digit4</code> | <code>U+E017</code> |
| <code>5</code> | <code>U+0035</code> | <code>digit5</code> | <code>U+E018</code> |
| <code>6</code> | <code>U+0036</code> | <code>digit6</code> | <code>U+E019</code> |
| <code>7</code> | <code>U+0037</code> | <code>digit7</code> | <code>U+E01A</code> |
| <code>8</code> | <code>U+0038</code> | <code>digit8</code> | <code>U+E01B</code> |
| <code>9</code> | <code>U+0039</code> | <code>digit9</code> | <code>U+E01C</code> |
| <code>A</code> | <code>U+0041</code> | <code>keya</code> | <code>U+E030</code> |
| <code>B</code> | <code>U+0042</code> | <code>keyb</code> | <code>U+E041</code> |
| <code>C</code> | <code>U+0043</code> | <code>keyc</code> | <code>U+E03F</code> |
| <code>D</code> | <code>U+0044</code> | <code>keyd</code> | <code>U+E032</code> |
| <code>E</code> | <code>U+0045</code> | <code>keye</code> | <code>U+E024</code> |
| <code>F</code> | <code>U+0046</code> | <code>keyf</code> | <code>U+E033</code> |
| <code>G</code> | <code>U+0047</code> | <code>keyg</code> | <code>U+E034</code> |
| <code>H</code> | <code>U+0048</code> | <code>keyh</code> | <code>U+E035</code> |
| <code>I</code> | <code>U+0049</code> | <code>keyi</code> | <code>U+E029</code> |
| <code>J</code> | <code>U+004A</code> | <code>keyj</code> | <code>U+E036</code> |
| <code>K</code> | <code>U+004B</code> | <code>keyk</code> | <code>U+E037</code> |
| <code>L</code> | <code>U+004C</code> | <code>keyl</code> | <code>U+E038</code> |
| <code>M</code> | <code>U+004D</code> | <code>keym</code> | <code>U+E043</code> |
| <code>N</code> | <code>U+004E</code> | <code>keyn</code> | <code>U+E042</code> |
| <code>O</code> | <code>U+004F</code> | <code>keyo</code> | <code>U+E02A</code> |
| <code>P</code> | <code>U+0050</code> | <code>keyp</code> | <code>U+E02B</code> |
| <code>Q</code> | <code>U+0051</code> | <code>keyq</code> | <code>U+E022</code> |
| <code>R</code> | <code>U+0052</code> | <code>keyr</code> | <code>U+E025</code> |
| <code>S</code> | <code>U+0053</code> | <code>keys</code> | <code>U+E031</code> |
| <code>T</code> | <code>U+0054</code> | <code>keyt</code> | <code>U+E026</code> |
| <code>U</code> | <code>U+0055</code> | <code>keyu</code> | <code>U+E028</code> |
| <code>V</code> | <code>U+0056</code> | <code>keyv</code> | <code>U+E040</code> |
| <code>W</code> | <code>U+0057</code> | <code>keyw</code> | <code>U+E023</code> |
| <code>X</code> | <code>U+0058</code> | <code>keyx</code> | <code>U+E03E</code> |
| <code>Y</code> | <code>U+0059</code> | <code>keyy</code> | <code>U+E027</code> |
| <code>Z</code> | <code>U+005A</code> | <code>keyz</code> | <code>U+E03D</code> |
| <code>a</code> | <code>U+0061</code> | <code>keya</code> | <code>U+E030</code> |
| <code>b</code> | <code>U+0062</code> | <code>keyb</code> | <code>U+E041</code> |
| <code>c</code> | <code>U+0063</code> | <code>keyc</code> | <code>U+E03F</code> |
| <code>d</code> | <code>U+0064</code> | <code>keyd</code> | <code>U+E032</code> |
| <code>e</code> | <code>U+0065</code> | <code>keye</code> | <code>U+E024</code> |
| <code>f</code> | <code>U+0066</code> | <code>keyf</code> | <code>U+E033</code> |
| <code>g</code> | <code>U+0067</code> | <code>keyg</code> | <code>U+E034</code> |
| <code>h</code> | <code>U+0068</code> | <code>keyh</code> | <code>U+E035</code> |
| <code>i</code> | <code>U+0069</code> | <code>keyi</code> | <code>U+E029</code> |
| <code>j</code> | <code>U+006A</code> | <code>keyj</code> | <code>U+E036</code> |
| <code>k</code> | <code>U+006B</code> | <code>keyk</code> | <code>U+E037</code> |
| <code>l</code> | <code>U+006C</code> | <code>keyl</code> | <code>U+E038</code> |
| <code>m</code> | <code>U+006D</code> | <code>keym</code> | <code>U+E043</code> |
| <code>n</code> | <code>U+006E</code> | <code>keyn</code> | <code>U+E042</code> |
| <code>o</code> | <code>U+006F</code> | <code>keyo</code> | <code>U+E02A</code> |
| <code>p</code> | <code>U+0070</code> | <code>keyp</code> | <code>U+E02B</code> |
| <code>q</code> | <code>U+0071</code> | <code>keyq</code> | <code>U+E022</code> |
| <code>r</code> | <code>U+0072</code> | <code>keyr</code> | <code>U+E025</code> |
| <code>s</code> | <code>U+0073</code> | <code>keys</code> | <code>U+E031</code> |
| <code>t</code> | <code>U+0074</code> | <code>keyt</code> | <code>U+E026</code> |
| <code>u</code> | <code>U+0075</code> | <code>keyu</code> | <code>U+E028</code> |
| <code>v</code> | <code>U+0076</code> | <code>keyv</code> | <code>U+E040</code> |
| <code>w</code> | <code>U+0077</code> | <code>keyw</code> | <code>U+E023</code> |
| <code>x</code> | <code>U+0078</code> | <code>keyx</code> | <code>U+E03E</code> |
| <code>y</code> | <code>U+0079</code> | <code>keyy</code> | <code>U+E027</code> |
| <code>z</code> | <code>U+007A</code> | <code>keyz</code> | <code>U+E03D</code> |

## 4. `event.code` 映射表

字体本身不处理键盘事件。在 JavaScript 中查表，把 `event.code` 转成码位：

```js
document.addEventListener("keydown", (event) => {
  const key = KEYS.find((item) => item.code === event.code);
  if (key) output.textContent = String.fromCodePoint(key.codepoint);
});
```

`demo.html` 中的 `KEYS` 数组包含全部 106 条映射，可直接复制。

| `event.code` | 键帽文字 | 字形 ID | PUA | 分区 |
|---|---|---|---|---|
| <code>Escape</code> | <code>Esc</code> | <code>escape</code> | <code>U+E000</code> | 功能键 |
| <code>F1</code> | <code>F1</code> | <code>f1</code> | <code>U+E001</code> | 功能键 |
| <code>F2</code> | <code>F2</code> | <code>f2</code> | <code>U+E002</code> | 功能键 |
| <code>F3</code> | <code>F3</code> | <code>f3</code> | <code>U+E003</code> | 功能键 |
| <code>F4</code> | <code>F4</code> | <code>f4</code> | <code>U+E004</code> | 功能键 |
| <code>F5</code> | <code>F5</code> | <code>f5</code> | <code>U+E005</code> | 功能键 |
| <code>F6</code> | <code>F6</code> | <code>f6</code> | <code>U+E006</code> | 功能键 |
| <code>F7</code> | <code>F7</code> | <code>f7</code> | <code>U+E007</code> | 功能键 |
| <code>F8</code> | <code>F8</code> | <code>f8</code> | <code>U+E008</code> | 功能键 |
| <code>F9</code> | <code>F9</code> | <code>f9</code> | <code>U+E009</code> | 功能键 |
| <code>F10</code> | <code>F10</code> | <code>f10</code> | <code>U+E00A</code> | 功能键 |
| <code>F11</code> | <code>F11</code> | <code>f11</code> | <code>U+E00B</code> | 功能键 |
| <code>F12</code> | <code>F12</code> | <code>f12</code> | <code>U+E00C</code> | 功能键 |
| <code>Insert</code> | <code>Ins</code> | <code>insert</code> | <code>U+E00D</code> | 功能键 |
| <code>Delete</code> | <code>Del</code> | <code>delete</code> | <code>U+E00E</code> | 功能键 |
| <code>Home</code> | <code>Home</code> | <code>home</code> | <code>U+E00F</code> | 功能键 |
| <code>End</code> | <code>End</code> | <code>end</code> | <code>U+E010</code> | 功能键 |
| <code>PageUp</code> | <code>PgUp</code> | <code>pageup</code> | <code>U+E011</code> | 功能键 |
| <code>PageDown</code> | <code>PgDn</code> | <code>pagedown</code> | <code>U+E012</code> | 功能键 |
| <code>Backquote</code> | <code>`</code> | <code>backquote</code> | <code>U+E013</code> | 数字区 |
| <code>Digit1</code> | <code>1</code> | <code>digit1</code> | <code>U+E014</code> | 数字区 |
| <code>Digit2</code> | <code>2</code> | <code>digit2</code> | <code>U+E015</code> | 数字区 |
| <code>Digit3</code> | <code>3</code> | <code>digit3</code> | <code>U+E016</code> | 数字区 |
| <code>Digit4</code> | <code>4</code> | <code>digit4</code> | <code>U+E017</code> | 数字区 |
| <code>Digit5</code> | <code>5</code> | <code>digit5</code> | <code>U+E018</code> | 数字区 |
| <code>Digit6</code> | <code>6</code> | <code>digit6</code> | <code>U+E019</code> | 数字区 |
| <code>Digit7</code> | <code>7</code> | <code>digit7</code> | <code>U+E01A</code> | 数字区 |
| <code>Digit8</code> | <code>8</code> | <code>digit8</code> | <code>U+E01B</code> | 数字区 |
| <code>Digit9</code> | <code>9</code> | <code>digit9</code> | <code>U+E01C</code> | 数字区 |
| <code>Digit0</code> | <code>0</code> | <code>digit0</code> | <code>U+E01D</code> | 数字区 |
| <code>Minus</code> | <code>-</code> | <code>minus</code> | <code>U+E01E</code> | 数字区 |
| <code>Equal</code> | <code>=</code> | <code>equal</code> | <code>U+E01F</code> | 数字区 |
| <code>Backspace</code> | <code>Back</code> | <code>backspace</code> | <code>U+E020</code> | 数字区 |
| <code>Tab</code> | <code>Tab</code> | <code>tab</code> | <code>U+E021</code> | 字母区 |
| <code>KeyQ</code> | <code>Q</code> | <code>keyq</code> | <code>U+E022</code> | 字母区 |
| <code>KeyW</code> | <code>W</code> | <code>keyw</code> | <code>U+E023</code> | 字母区 |
| <code>KeyE</code> | <code>E</code> | <code>keye</code> | <code>U+E024</code> | 字母区 |
| <code>KeyR</code> | <code>R</code> | <code>keyr</code> | <code>U+E025</code> | 字母区 |
| <code>KeyT</code> | <code>T</code> | <code>keyt</code> | <code>U+E026</code> | 字母区 |
| <code>KeyY</code> | <code>Y</code> | <code>keyy</code> | <code>U+E027</code> | 字母区 |
| <code>KeyU</code> | <code>U</code> | <code>keyu</code> | <code>U+E028</code> | 字母区 |
| <code>KeyI</code> | <code>I</code> | <code>keyi</code> | <code>U+E029</code> | 字母区 |
| <code>KeyO</code> | <code>O</code> | <code>keyo</code> | <code>U+E02A</code> | 字母区 |
| <code>KeyP</code> | <code>P</code> | <code>keyp</code> | <code>U+E02B</code> | 字母区 |
| <code>BracketLeft</code> | <code>[</code> | <code>bracketleft</code> | <code>U+E02C</code> | 字母区 |
| <code>BracketRight</code> | <code>]</code> | <code>bracketright</code> | <code>U+E02D</code> | 字母区 |
| <code>Backslash</code> | <code>\</code> | <code>backslash</code> | <code>U+E02E</code> | 字母区 |
| <code>CapsLock</code> | <code>Cap</code> | <code>capslock</code> | <code>U+E02F</code> | 主键区 |
| <code>KeyA</code> | <code>A</code> | <code>keya</code> | <code>U+E030</code> | 主键区 |
| <code>KeyS</code> | <code>S</code> | <code>keys</code> | <code>U+E031</code> | 主键区 |
| <code>KeyD</code> | <code>D</code> | <code>keyd</code> | <code>U+E032</code> | 主键区 |
| <code>KeyF</code> | <code>F</code> | <code>keyf</code> | <code>U+E033</code> | 主键区 |
| <code>KeyG</code> | <code>G</code> | <code>keyg</code> | <code>U+E034</code> | 主键区 |
| <code>KeyH</code> | <code>H</code> | <code>keyh</code> | <code>U+E035</code> | 主键区 |
| <code>KeyJ</code> | <code>J</code> | <code>keyj</code> | <code>U+E036</code> | 主键区 |
| <code>KeyK</code> | <code>K</code> | <code>keyk</code> | <code>U+E037</code> | 主键区 |
| <code>KeyL</code> | <code>L</code> | <code>keyl</code> | <code>U+E038</code> | 主键区 |
| <code>Semicolon</code> | <code>;</code> | <code>semicolon</code> | <code>U+E039</code> | 主键区 |
| <code>Quote</code> | <code>&#x27;</code> | <code>quote</code> | <code>U+E03A</code> | 主键区 |
| <code>Enter</code> | <code>Enter</code> | <code>enter</code> | <code>U+E03B</code> | 主键区 |
| <code>ShiftLeft</code> | <code>Shift</code> | <code>shift</code> | <code>U+E03C</code> | Shift 区 |
| <code>KeyZ</code> | <code>Z</code> | <code>keyz</code> | <code>U+E03D</code> | Shift 区 |
| <code>KeyX</code> | <code>X</code> | <code>keyx</code> | <code>U+E03E</code> | Shift 区 |
| <code>KeyC</code> | <code>C</code> | <code>keyc</code> | <code>U+E03F</code> | Shift 区 |
| <code>KeyV</code> | <code>V</code> | <code>keyv</code> | <code>U+E040</code> | Shift 区 |
| <code>KeyB</code> | <code>B</code> | <code>keyb</code> | <code>U+E041</code> | Shift 区 |
| <code>KeyN</code> | <code>N</code> | <code>keyn</code> | <code>U+E042</code> | Shift 区 |
| <code>KeyM</code> | <code>M</code> | <code>keym</code> | <code>U+E043</code> | Shift 区 |
| <code>Comma</code> | <code>,</code> | <code>comma</code> | <code>U+E044</code> | Shift 区 |
| <code>Period</code> | <code>.</code> | <code>period</code> | <code>U+E045</code> | Shift 区 |
| <code>Slash</code> | <code>/</code> | <code>slash</code> | <code>U+E046</code> | Shift 区 |
| <code>ShiftRight</code> | <code>Shift</code> | <code>shift</code> | <code>U+E03C</code> | Shift 区 |
| <code>ControlLeft</code> | <code>Ctrl</code> | <code>ctrl</code> | <code>U+E047</code> | 修饰键 |
| <code>FnLeft</code> | <code>Fn</code> | <code>fn</code> | <code>U+E048</code> | 修饰键 |
| <code>MetaLeft</code> | <code>Win</code> | <code>win</code> | <code>U+E049</code> | 修饰键 |
| <code>AltLeft</code> | <code>Alt</code> | <code>alt</code> | <code>U+E04A</code> | 修饰键 |
| <code>Space</code> | <code>Space</code> | <code>space</code> | <code>U+E04B</code> | 修饰键 |
| <code>AltRight</code> | <code>Alt</code> | <code>alt</code> | <code>U+E04A</code> | 修饰键 |
| <code>FnRight</code> | <code>Fn</code> | <code>fn</code> | <code>U+E048</code> | 修饰键 |
| <code>ControlRight</code> | <code>Ctrl</code> | <code>ctrl</code> | <code>U+E047</code> | 修饰键 |
| <code>ArrowLeft</code> | <code>←</code> | <code>arrowleft</code> | <code>U+E04C</code> | 修饰键 |
| <code>ArrowUp</code> | <code>↑</code> | <code>arrowup</code> | <code>U+E04D</code> | 修饰键 |
| <code>ArrowDown</code> | <code>↓</code> | <code>arrowdown</code> | <code>U+E04E</code> | 修饰键 |
| <code>ArrowRight</code> | <code>→</code> | <code>arrowright</code> | <code>U+E04F</code> | 修饰键 |
| <code>NumLock</code> | <code>Num</code> | <code>num</code> | <code>U+E050</code> | 小键盘 |
| <code>NumpadDivide</code> | <code>/</code> | <code>slash</code> | <code>U+E046</code> | 小键盘 |
| <code>NumpadMultiply</code> | <code>*</code> | <code>asterisk</code> | <code>U+E051</code> | 小键盘 |
| <code>NumpadSubtract</code> | <code>-</code> | <code>minus</code> | <code>U+E01E</code> | 小键盘 |
| <code>Numpad7</code> | <code>7</code> | <code>digit7</code> | <code>U+E01A</code> | 小键盘 |
| <code>Numpad8</code> | <code>8</code> | <code>digit8</code> | <code>U+E01B</code> | 小键盘 |
| <code>Numpad9</code> | <code>9</code> | <code>digit9</code> | <code>U+E01C</code> | 小键盘 |
| <code>NumpadAdd</code> | <code>+</code> | <code>plus</code> | <code>U+E052</code> | 小键盘 |
| <code>Numpad4</code> | <code>4</code> | <code>digit4</code> | <code>U+E017</code> | 小键盘 |
| <code>Numpad5</code> | <code>5</code> | <code>digit5</code> | <code>U+E018</code> | 小键盘 |
| <code>Numpad6</code> | <code>6</code> | <code>digit6</code> | <code>U+E019</code> | 小键盘 |
| <code>Numpad1</code> | <code>1</code> | <code>digit1</code> | <code>U+E014</code> | 小键盘 |
| <code>Numpad2</code> | <code>2</code> | <code>digit2</code> | <code>U+E015</code> | 小键盘 |
| <code>Numpad3</code> | <code>3</code> | <code>digit3</code> | <code>U+E016</code> | 小键盘 |
| <code>NumpadEnter</code> | <code>Enter</code> | <code>enter</code> | <code>U+E03B</code> | 小键盘 |
| <code>Numpad0</code> | <code>0</code> | <code>digit0</code> | <code>U+E01D</code> | 小键盘 |
| <code>NumpadDecimal</code> | <code>.</code> | <code>period</code> | <code>U+E045</code> | 小键盘 |
| <code>PrintScreen</code> | <code>PrtSc</code> | <code>printscreen</code> | <code>U+E053</code> | 功能键 |
| <code>ScrollLock</code> | <code>ScrLk</code> | <code>scrolllock</code> | <code>U+E054</code> | 功能键 |
| <code>Pause</code> | <code>Pause</code> | <code>pause</code> | <code>U+E055</code> | 功能键 |
| <code>MetaRight</code> | <code>Win</code> | <code>win</code> | <code>U+E049</code> | 修饰键 |
| <code>ContextMenu</code> | <code>Menu</code> | <code>menu</code> | <code>U+E056</code> | 修饰键 |

重复的物理键共用一个字形，但每个 `event.code` 都单独列出。例如 `ShiftLeft` 与 `ShiftRight` 都使用 `shift`，`Digit7` 与 `Numpad7` 都使用 `digit7`。

## 5. 字体信息

- 字体族：**ReiconFont**，样式 **Regular**，版本 **1.800**。
- Units per em：**1000**；所有字形占满 **1em / 62 设计单位** 的显示高度。
- 键帽边框高 1000 字体单位；独立 SVG 的设计高度为 62 单位。
- 单色轮廓，内部透明，颜色由 `color` / `currentColor` 控制。
- 轮廓来自 Noto Sans SemiBold 与 JetBrains Mono SemiBold，均以 SIL OFL 1.1 使用。完整许可与版权声明见 `LICENSE`。
