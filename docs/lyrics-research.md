# 实时获取主流音乐软件歌词的方式调研

> 调研范围：Windows 10 / 11 桌面端，目标是给 MirrorpondHUD 增加「跟随当前播放、实时滚动的歌词卡」。
> 结论以 2026-11 在本机的实测为准，实测环境：Windows 11 build 26200、.NET SDK 8.0.426、网易云音乐 PC 端运行中。
> 文中「已验证」= 本机实测或一手源码/官方文档；「待验证」= 仅来自社区资料，落地前需复测。

---

## 0. 结论速览

「实时歌词」= **识别曲目 + 拿到播放时钟 + 匹配到带时间轴的文本 + 低开销渲染**。四件事里最难的是播放时钟，最容易被低估的是匹配。

| 层 | 结论 |
| --- | --- |
| 播放会话 | Windows SMTC（`Windows.Media.Control`）是唯一通用正路。**已验证**未打包进程可跨进程读取会话、曲目信息、时间轴 |
| 播放时钟 | `Position` 是快照、约 6 秒才跳一次；**必须**用 `Position + (now - LastUpdatedTime)` 外推，再靠事件重锚 |
| 歌词文本 | 本地 `.lrc`/内嵌标签 → LRCLIB → 音源专用接口，按此顺序回退；音源接口默认不内置 |
| 逐字 | YRC / QRC / KRC / 增强 LRC / TTML 都能表达，但 QRC/KRC 要解密；建议只做「解析能力」，不做「抓取内置」 |

推荐落地顺序：**SMTC 会话层 → 本地 + LRCLIB 行级歌词 → 匹配与偏移校准 → 逐字（可选）→ 音源插件（用户自装）**。

---

## 1. 问题拆解

1. **曲目身份**：歌名、艺人、专辑、时长。没有时长就做不了可靠匹配，也算不出进度。
2. **播放时钟**：播到第几秒、是否暂停、是否刚拖动。见第 5 节。
3. **歌词文本**：纯文本 / 行级 / 逐字。见第 3、4 节。
4. **匹配与渲染**：把「播放位置」映射到「当前行 / 当前字」并画出来。见第 4.6、6 节。

---

## 2. 播放会话层：SMTC

### 2.1 能拿到什么

`GlobalSystemMediaTransportControlsSessionManager` 覆盖系统内所有接入 SMTC 的播放器：

- 会话：`GetSessions()`、`GetCurrentSession()`、`SessionsChanged`、`CurrentSessionChanged`
- 曲目：`TryGetMediaPropertiesAsync()` → `Title` / `Artist` / `AlbumTitle` / `AlbumArtist` / `TrackNumber` / `Genres` / `Thumbnail`
- 状态：`GetPlaybackInfo()` → `PlaybackStatus`（Playing / Paused / Stopped / Changing）、`PlaybackRate`、`AutoRepeatMode`、`ShuffleActive`
- 时间轴：`GetTimelineProperties()` → `Position`、`StartTime`、`EndTime`、`MinSeekTime`、`MaxSeekTime`、`LastUpdatedTime`
- 事件：`MediaPropertiesChanged`、`PlaybackInfoChanged`、`TimelinePropertiesChanged`
- 控制：`TryPlayAsync` / `TryPauseAsync` / `TrySkipNextAsync` / `TryChangePlaybackPositionAsync`

官方文档：<https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager>

### 2.2 本机实测

用两个临时工程交叉验证：一个用 `MediaPlayer` 注册会话并播放 60 秒 WAV，另一个进程读取。

| 观测点 | 结果 |
| --- | --- |
| 无播放器在放时 | `GetSessions().Count == 0`（网易云进程在跑但未播放，也不会留下会话） |
| 跨进程读取 | 成功：`CurrentSession = provider.exe`，`Sessions = 1` |
| 未打包进程是否被能力门禁拦住 | **没有**。文档标注的 `globalMediaControl` 是打包应用（MSIX）声明用的能力 |
| `Position` 更新粒度 | **约 6 秒跳变一次**，两次之间读到同一快照 |
| 外推精度 | `Position + (now - LastUpdatedTime)` 连续，误差 0.1 秒量级 |
| `TimelinePropertiesChanged` | 只在快照更新时触发，不能当逐帧时钟 |

### 2.3 零依赖约束下的两条路线

当前 `Directory.Build.props` 的 TFM 是 `net8.0-windows`（无版本号），**没有 WinRT 投影**，`Windows.Media.Control` 不可直接用。

**路线 A：TFM 升到 `net8.0-windows10.0.19041.0`**

- 仍是零 `PackageReference`：投影来自 SDK 首方 targeting pack（本机缓存 `Microsoft.Windows.SDK.NET.Ref 10.0.19041.56`）。
- **已验证**：WPF + WinForms + WinRT 类型混编可 0 警告构建。
- 代价：产物多出 `Microsoft.Windows.SDK.NET.dll`（实测 24.8 MB）与 `WinRT.Runtime.dll`（0.5 MB）；WPF 不支持裁剪，单文件发布同样变大。
- 风险：`SupportedOSPlatformVersion` 会降到 10.0.19041.0，可能引入 CA1416 新告警，需全仓构建确认。

**路线 B：手写 COM 激活，保持 TFM 不变**

`RoGetActivationFactory` + `IInspectable`：拿 `IGlobalSystemMediaTransportControlsSessionManagerStatics`，调 `RequestAsync` 并等待 `IAsyncOperation<T>`，手写会话/时间轴/事件 vtable。

- 收益：零体积增量，符合本仓库「纯手写原生互操作」的传统。
- 代价：`IAsyncOperation`、`IVectorView`、`ITypedEventHandler` 三套 ABI 全要自己封，估计 600–900 行，事件线程封送容易出错。

**建议**：先用路线 A 跑通功能，把 SMTC 访问收敛到一个类里；体积敏感时再换成路线 B 的实现，对外接口不变。

### 2.4 拿不到会话时的降级

老播放器、便携版、网页内嵌播放、异常状态都可能不注册会话。降级顺序：

1. 读不到会话 → 卡片显式显示「未检测到正在播放」，不要静默空白。
2. 用户可手动指定跟随的进程；窗口标题启发式只作兜底（命中率低、易误判）。
3. 本地文件播放可完全绕过 SMTC：直接读标签 + 同目录 `.lrc` + 自己的播放状态。

---

## 3. 歌词来源

### 3.1 本地优先

| 载体 | 读取方式 | 备注 |
| --- | --- | --- |
| 同目录 `.lrc` | 直接读文件 | 最可靠；同名规则要兼容 `歌名.lrc`、`艺人 - 歌名.lrc` |
| ID3v2 `USLT` / `SYLT` | 标签解析 | `USLT` 无时间轴，`SYLT` 才有行级；中文曲库 `SYLT` 少见 |
| Vorbis `LYRICS` / `UNSYNCEDLYRICS` | FLAC/OGG 标签 | 多数无时间轴 |
| APE / WMA 标签 | 对应标签 | 覆盖少，按需 |

本地源最稳，但**不能当唯一来源**——用户多数在听流媒体。

### 3.2 LRCLIB：最适合做默认在线源

免鉴权 HTTP API，路由可直接从其开源服务端确认（`server/src/router.rs`）：

| 路由 | 参数 | 用途 |
| --- | --- | --- |
| `GET /api/get` | `track_name`、`artist_name` 必填，`album_name`、`duration` 可选 | 按元数据精确取 |
| `GET /api/get/{id}` | — | 按 ID 取 |
| `GET /api/search` | `q` 或 `track_name`/`artist_name`/`album_name` | 关键词/结构化搜索 |
| `GET /api/v2/search` | 同上 | 新版搜索 |

响应字段（camelCase）：`id`、`trackName`、`artistName`、`albumName`、`duration`、`instrumental`、`plainLyrics`、`syncedLyrics`，以及 **`hasWordSync`、`lyricsfile`**——说明它已在支持逐字，但公开响应是否直接给逐字文本**待验证**。

服务端过载时返回 `ServerOverloaded` 错误（`server/src/errors.rs`），要按可重试处理，不要当「无歌词」缓存。官方文档 <https://lrclib.net/docs> 标注了限流，实测约为**每 IP 每分钟 45 / 90 / 180 次**三档，需要带 `User-Agent` 并尊重 `Retry-After`。注意 `/api/get-cached` **并不存在**（文档无、源码路由无、实测 404），不要照抄网上教程。代码是 MIT，但**歌词数据本身的许可未明示**，版权风险仍在。

### 3.3 音源专用接口：能力最强，风险最高

以下均为客户端内部接口，**不是公开 API**，随时可能变更：

| 音源 | 实测/资料 | 形态 |
| --- | --- | --- |
| 网易云音乐 | 实测：旧 `/api/song/lyric` 匿名 200，返回 `lrc`（明文 LRC）、`tlyric`（翻译）、`klyric`；逐字要 eapi `/eapi/song/lyric/v1`（参数 AES-128-ECB 加密），且**只有部分曲目有 yrc**；`lyric/new` 是第三方路由，不是官方接口 | 新旧两套格式并存，解析要兼容 |
| QQ 音乐 | 实测：fcg 接口返回明文 LRC（`lyric` 字段）；QRC 走另一通道，返回 XML 包裹的**十六进制密文** | 明文 + 加密双通道 |
| 酷狗音乐 | 实测：响应 `content` 为 base64，解码后以 `krc1` 开头（KRC 加密格式） | 加密 |
| 酷我 / 汽水 | 酷我 `newlyric` 接口实测返回 `TP=DENY REQUEST`，只有第三方源码可参考（**未验证**）；汽水走曲目详情 | 行级为主 |

**实测旁证**：本机网易云 PC 端磁盘上没有落地任何 `.lrc`；`Library/webdb.dat`（42 MB）、`library.dat` 被运行中的进程独占，直接读报「文件正由另一进程使用」，需影子复制且格式未公开。**「读客户端缓存拿歌词」这条路在现代版本基本走不通。** 另外，社区常用的自建服务 `NeteaseCloudMusicApi` 已于 2024-02-28 归档，说明这条生态在被持续收紧。

### 3.4 Spotify / Apple Music

- **Spotify**：官方 Web API 不提供歌词（待验证最新状态）。客户端内部有 `color-lyrics` 一类接口，需要登录态（`sp_dc` cookie），属非公开接口。
- **Apple Music**：歌词是 TTML，含逐字/逐音节时间轴；取用需要 Media User Token（Lyricify 的实现即如此）。公开 API 是否开放歌词端点**待验证**。

两家都建议「能拿就拿，拿不到退回 LRCLIB 匹配」，不做强依赖。

### 3.5 现成库：Lyricify.Lyrics.Helper

<https://github.com/WXRIW/Lyricify-Lyrics-Helper>，NuGet `Lyricify.Lyrics.Helper` 0.2.1，**Apache-2.0**，netstandard2.1。

- **格式**：LRC、增强 LRC、QRC、KRC、YRC、TTML、Spotify JSON、Musixmatch JSON 的解析与生成，统一模型 `Lyricify Lyrics`（未同步 / 逐行 / 逐字 / 混合）。
- **来源**：QQ、网易云、酷狗、汽水、Apple Music（需 token）、Musixmatch、Spotify（需 `sp_dc`）、LRCLIB，共 8 家，**不含酷我**。
- **依赖**：`Newtonsoft.Json`、`SharpZipLib`、`CHTCHSConv` —— 与本仓库「零第三方依赖」冲突。
- **定位**：**当作权威参考实现**（QRC/KRC 解密、YRC 解析、匹配算法），只移植需要的解析器，不引入它的 HTTP 层与依赖。

### 3.6 匹配策略（决定「有没有歌词」）

行业实现的共同点（参考 Lyricify `CompareHelper`）：

- **时长容差**：绝对差 <300 ms 视为完美，<700 ms 高，<1500 ms 中，<3500 ms 低，超过 3500 ms 视为不匹配。
- **名称归一化**：繁转简、小写、全半角与标点统一（`，`→`,`、`（）`→`()`、`[]`→`()`）、去多余空格；处理 `Artist - Title` 与 `Title (feat. X)`、`(Acoustic Version)` 这类变体。
- **多候选打分**：名称 + 艺人 + 时长加权，取最高分；低分要宁可不用，也不要贴错歌词。

### 3.7 来源矩阵

| 来源 | 覆盖 | 逐字 | 合规 | 稳定性 | 建议 |
| --- | --- | --- | --- | --- | --- |
| 本地 `.lrc` / 内嵌标签 | 用户自备 | 取决于文件 | 无风险 | 高 | 默认开 |
| LRCLIB | 欧美较好、中文一般 | 部分（待验证） | 低风险 | 高 | 默认开 |
| 网易云 | 华语强 | 是（YRC） | 逆向，中高风险 | 中 | 可选插件，默认关 |
| QQ 音乐 | 华语强 | 是（QRC） | 逆向，中高风险 | 中 | 可选插件，默认关 |
| 酷狗 | 华语强 | 是（KRC） | 逆向，中高风险 | 中 | 可选插件，默认关 |
| Spotify 内部 | 欧美强 | 部分 | 违反 TOS 风险 | 低 | 不做或用户自担 |
| Apple Music | 欧美强 | 是（TTML） | 需订阅与 token | 中 | 可选 |

### 3.8 可借鉴的开源实现

| 项目 | 语言/UI | 取向 | 可借鉴点 |
| --- | --- | --- | --- |
| [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper) | C#/.NET 库 | 只做解析/生成/搜索，不管播放 | **最佳参考**：格式检测、QRC/KRC 解密、统一模型、翻译对齐 |
| [Lyricify 4](https://github.com/WXRIW/Lyricify-App) | C#/WPF（应用闭源，文档开源） | SMTC/播放器集成 | 歌词规范文档、呼吸点/空白行约定、全局与按曲偏移 |
| [BetterLyrics](https://github.com/jayfunc/BetterLyrics) | C#/WinUI3 | 读 `Position` 快照靠高频刷新，不做外推 | 行索引增量查找（缓存上次命中行，超 1s 提前 break）、SMTC 会话封装 |
| [LyricsX](https://github.com/ddddxxx/LyricsX) | Swift/macOS | 播放器脚本取播放时间 | 偏移持久化模型、日文注音绘制、`playbackTime + delay` 查询方式 |
| [MusicBee-DesktopLyrics](https://github.com/cqjjjzr/MusicBee-DesktopLyrics) | C#/WinForms | 宿主提供播放状态 | 双行歌词（`/` 分隔）、桌面歌词窗口置顶与穿透 |
| [ModernFlyouts](https://github.com/ModernFlyouts-Community/ModernFlyouts) | C#/WPF | 只做媒体浮窗，不做歌词 | SMTC 会话获取与多屏定位，歌词同步无参考价值 |

注意 BetterLyrics 是「不外推、靠高频读快照」的反例：它依赖播放器频繁更新 `Position`；本机实测某些播放器 6 秒才更新一次快照，所以本项目的结论仍是**必须外推**。

---

## 4. 格式与统一模型

### 4.1 标准 LRC

```text
[ti:晴天]
[ar:周杰伦]
[offset:0]
[00:01.670][01:17.870]Lately, I've been, I've been losing sleep
[00:18.920]Yeah, we'll be counting stars
[00:16.040]We'll be counting stars
```

来自真实样本的坑（**已验证**）：

- 时间戳有 `[mm:ss.xx]`（10 ms）与 `[mm:ss.xxx]`（1 ms）两种写法，都要吃。
- **一行多时间戳**要展开成多条，否则副歌整段丢失。
- 真实文件里**时间戳会乱序**（样本中 `00:18.920` 排在 `00:16.040` 前面），解析后必须按起始时间稳定排序。
- `[offset:]` 单位毫秒，**没有正式规范**。Lyricify 的实现是 `StartTime -= offset`，即**正值 = 歌词提前**（已验证）；但 foobar2000 等播放器历史上有不同解释（待验证）。工程上：内部统一存「显示时间 = 媒体时间 + offset」，导入文件时按主流方向解析，并给一个「反转文件 offset 方向」的逃生开关。
- 元数据行 `ti/ar/al/by/length` 不是歌词行，必须过滤。

### 4.2 增强 LRC（逐字）

行内用 `<mm:ss.xx>` 标记字起点：

```text
[00:12.00]<00:12.00>今<00:12.30>天<00:12.60>很<00:12.90>好
```

支持度不齐，遇到不认识标签要能降级成行级。

### 4.3 YRC（网易云逐字）

**已验证**的真实形态是混合两种行：

```text
{"t":0,"c":[{"tx":"作词: "},{"tx":"Brent Kutzle"},{"tx":"/"}]}
[420,4440](420,1320,0)Lately(1740,0,0), (1740,570,0)I've (2310,600,0)been
```

- 元信息行是 **JSON**（`t` 起点、`c` 文本片段，片段里可能带 `li`/`or` 链接），不是 `[ti:]` 标签。
- 时间行：`[行起点,行时长]`，随后每段 `(字起点,字时长,0)文本`；**字的起点是绝对时间**，时长可以为 0。
- 解析要点：先按行首字符分流（`{` → JSON 元信息，`[` → 时间行），再拆字；空行、纯音乐标记跳过。

### 4.4 QRC（QQ 逐字，加密）

- 分发时**不是「base64 + XOR」**。源码与独立实现互证的解密链路是：XML 节点里的密文串（Lyricify 按 **hex** 解析，AMLL 文档写 base64，可能对应不同接口/版本，两种都要试）→ **非标准 3DES**（公开的硬编码 ASCII 密钥，属格式混淆而非真加密）→ **zlib inflate** → XML 文本，正文在 `Lyric_1` 节点的 `LyricContent` 属性里。
- QQ 接口外层通常有 `content` / `contentts` / `contentroma` 三份，分别对应原文、译文、罗马音。
- 切分要点：文本里可能含空格，必须「贪婪匹配到最后一个合法时间戳」来切，不能简单 `Split('(')`。
- 解密后的文本形如（**已验证**）：

```text
[0,4390]Stop(0,274) (274,274)And(548,274) (822,274)Stare(1096,274)
```

即 `[行起点,行时长]字(字起点,字时长)`，**字起点是绝对时间**。

### 4.5 KRC（酷狗逐字，加密）

- 加密链路（源码确认）：`base64 解码 → 丢掉前 4 字节（即 "krc1" 头）→ 与 16 字节固定密钥循环 XOR → zlib inflate → 去掉首个字符`。
- 头部 4 字节应校验为 `krc1`，不匹配就按「格式错误」上报，不要硬解。
- 解密后带 `[id:]`/`[ar:]`/`[ti:]`/`[hash:]`/`[language:]` 标签；翻译与罗马音藏在 `[language:]` 后的 base64 JSON 里，按行序与主歌词对齐。
- 解密后的行形如（**已验证**）：

```text
[419,610]<0,509,0>OneRepublic <509,51,0>- <560,50,0>Counting <610,0,0>Stars
```

注意与 QRC/YRC 的关键差异：**KRC 的字起点是相对行起点的偏移**，跨源统一时必须做一次换算，否则逐字会整体错位。

### 4.6 TTML（Apple）

- 结构：`<tt><body><div><p>`（行）+ `<span>`（字/词）。
- 时间：`begin` / `end` / `dur`，支持 `MM:SS.fff`、`HH:MM:SS.fff`、`12.3s`；`dur` 与 `end - begin` 可互推，解析器要两者都可缺省。
- 逐字模式：`itunes:timing="Word"`；逐行模式：`itunes:timing="Line"`（此时行内 span 时间通常不用）。
- 结构字段：`itunes:key="L1"`（行 ID）、`ttm:agent`（对唱者）、`ttm:role="x-translation" | "x-roman" | "x-bg"`（翻译 / 音译 / 背景人声）、`itunes:song-part`（段落）、`tts:ruby`（注音）。
- 翻译/音译也可能放在 `<iTunesMetadata><translations>` 里，用 `for="L1"` 关联行 ID。
- **Apple 没有公开 schema**，社区事实标准是 AMLL TTML DB；不要把它当「官方保证稳定」的契约。
- 结论：信息最全的逐字来源，但解析成本最高。

### 4.7 建议的统一模型

```text
LyricsDoc { Track{title,artist,album,durationMs}, Lines[], OffsetMs(用户), Source, Format, SyncLevel, Warnings[] }
Line      { StartMs: long, EndMs: long?, Text, Translation?, Roman?, Role, Agent?, Syllables[]?, SourceIndex }
Syllable  { StartMs: long, EndMs: long, Text, Ruby? }
```

必须做的归一化（这些是「看起来能跑、实际会错位」的高发区）：

1. 时间统一 `long` 毫秒；**KRC 的相对行首时间加 `lineStart`**，YRC/QRC 保持绝对值。
2. 文件 `offset` 只影响查询偏移，不写回时间戳；用户偏移单独存。
3. 一行多时间戳**展开成多条 Line**（同文本、不同起点），不要在渲染时特判。
4. `EndMs` 统一推导：取下一行起点（末行用 `durationMs`），逐字同理；禁止出现 `End < Start`。
5. 0 时长音节（YRC/QRC 里大量存在）并入前一音节的高亮区间，或标记为瞬时，避免除零。
6. 同时间戳的行（翻译/音译）保留原始顺序 `SourceIndex`，不要按时间戳去重。
7. `Warnings` 必须可见：解析降级、跳过行、解密失败都要能进日志与卡片状态，不能静默丢。

---

## 5. 时间轴同步

### 5.1 外推

```text
pos(now) = Position + (now - LastUpdatedTime) * PlaybackRate   // 仅 Playing 时成立
```

- `Position` 的官方定义就是「**as of `LastUpdatedTime`** 的快照」，所以必须补差值，不能直接逐帧渲染（实测约 6 秒跳一次）。
- 实现上先算一次锚点 `anchorDelta = DateTimeOffset.Now - LastUpdatedTime`，之后用 **`Stopwatch` 单调时钟**推进，避免 NTP 校时或睡眠唤醒把歌词甩飞；`LastUpdatedTime` 本身是墙钟，只在重锚时使用。
- 实测外推误差 0.1 秒量级；轮询 200–500 ms 足够，事件到达时立即重锚。
- 倍速播放（有声书）必须乘 `PlaybackRate`。

### 5.2 重锚与失效

- `PlaybackInfoChanged` → 状态离开 Playing 时**冻结**外推，不要继续往前推。
- `TimelinePropertiesChanged` → 拖动/跳转/切歌时快照跳变，**丢弃旧外推状态并重锚**。
- 外推结果钳制在 `[StartTime, EndTime]` 内。
- **seek 检测**：外推位置与下一次快照位置偏差超过 500–1000 ms，就判定为拖动/跳转并重锚；阈值不要太小，否则播放器自身快照误差会触发抖动。
- 切歌判定不能只看会话切换：`Title`/`Artist`/`EndTime` 变化都要触发重新匹配歌词。

### 5.3 行定位算法

参考 BetterLyrics 的 `LyricsSynchronizer`：缓存上次命中的行索引，先判断是否仍在同一行；否则从缓存位置向后扫，遇到「起始时间 > 当前 + 1000 ms」就提前退出。**不要每帧全表扫描**，长歌词（几百行）在高频刷新下会白烧 CPU。

双语/翻译不要塞进同一行：用 lane（主歌词 lane 0，翻译/罗马音 lane 1+）表达，取当前行时优先 lane 0，找不到再退化到次级 lane。这样间奏、无翻译行都不会把布局撑乱。

### 5.4 音频延迟与偏移校准

- 蓝牙耳机、HDMI、声卡会带来几十到几百毫秒延迟，表现为歌词**统一提前或滞后**。
- 设置里给 `offset` 滑块（建议 ±500 ms、步进 10 ms），只作用于渲染位置，不改歌词数据；单独提供「自动校准」入口。
- 倍速/有声书的偏移与音乐分开存，不要共用。

---

## 6. 渲染

- **行级滚动**：按当前行索引做 150–250 ms 缓动滚动，当前行高亮，前后行降透明度。绝大多数场景够用。
- **逐字高亮**：按 `(pos - word.Start) / word.Duration` 算进度。WPF 推荐的画法是**单个自绘元素 + `DrawingContext`**：缓存 `FormattedText`，用 `BuildHighlightGeometry` 生成裁剪几何，只改几何/不透明度，不要每帧重建控件或字符串。
- **刷新节奏**：宿主文字卡是 120 ms tick（`CardModule.RefreshIntervalMs`），对逐字不够；歌词视图自起 `DispatcherTimer`（16–33 ms）或挂 `CompositionTarget.Rendering`，**可见才跑、隐藏即停**（参考 ChatCard 的定时器生命周期处理）。
- **DPI**：必须声明 Per-Monitor V2（仓库已有 `app.manifest`），否则跨屏拖动会模糊或错位；滚动偏移按实际渲染高度换算，不用像素常量。
- 折行后高亮可能变成多段几何，需要按行内位置拆片；双语用固定槽位（主行 + 翻译行），不要动态撑高打乱滚动。
- 长行折行、双语双行、超长前奏（「即将开始」占位）、纯音乐（`instrumental`）都要有明确状态。

---

## 7. 对 MirrorpondHUD 的落地建议

### 7.1 形态

新增内置富内容卡插件 `src/Plugins/Lyrics/`（`RichCardModuleBase`，manifest `builtin.lyrics`）。不要塞进现有媒体卡——媒体卡的职责是「播放本地媒体文件」，语义不同，混在一起会污染设置页和契约。

- 视图：歌词滚动控件放 `MirrorpondHUD.UiKit`（与 `CardMediaView` 同级），只从主题令牌取色与字号。
- 数据：会话层 + 歌词源层放插件内的服务类，后台线程取数据，UI 线程只渲染。
- 配置：`Extras.lyrics`（来源开关、偏移、字号、双语、逐字），沿用 `ExtrasAs<T>` + `Normalize()`。

### 7.2 与现有契约

- `ICardHost` 已够用：状态目录、`RequestRender`、`SaveSettings`；不需要改 `apiVersion`。
- 失败可见：取歌词失败用 `LogThrottle` 做状态转移（失败/恢复各一条），原因透到卡片状态行（如「LRCLIB 超时，重试中」）。
- 自检（`SelfTest.cs`）加断言：LRC 乱序排序、多时间戳展开、offset 方向、YRC/QRC/KRC 字时间绝对化、外推钳制。

### 7.3 分阶段

1. **P0**：TFM 升级验证 + SMTC 读取 + 标准 LRC 解析 + 行级滚动 + 手动指定歌词文件。
2. **P1**：本地旁车/内嵌标签 + LRCLIB + 缓存（按 曲目+来源 落盘）+ 偏移校准 + 双语。
3. **P2**：逐字（增强 LRC / YRC）+ 卡拉OK 渲染 + 自检断言。
4. **P3（可选）**：音源插件化，凭据由用户提供，默认关闭并明示风险。

### 7.4 红线

- 不内置「自动抓取 + 解密」商业音源歌词的逻辑，不随包分发解密后的歌词文本。
- 不隐式默认使用外部源：首次使用让用户选择来源，并说明数据发往哪里。
- 网络请求必须有超时、取消、失败可见，不能拖慢卡片或主线程。

---

## 8. 风险与合规

- 歌词文本受著作权保护，缓存与再展示要看来源条款。
- 音乐客户端接口属私有接口，逆向使用通常违反用户协议，接口随时变更并可能触发风控；账号风险由使用者承担。
- LRCLIB 等社区源风险低，但要遵守其条款与限流。
- 产品姿态：**默认只用「本地 + 开放源」，音源接口作为可选插件，风险与凭据由用户自担，界面说清楚。**

---

## 9. 本机验证记录

| 实验 | 方法 | 结果 |
| --- | --- | --- |
| TFM 升级可行性 | `net8.0-windows10.0.19041.0` + WPF + WinForms + WinRT | 0 警告 0 错误；多出 `Microsoft.Windows.SDK.NET.dll` 24.8 MB、`WinRT.Runtime.dll` 0.5 MB |
| 无播放时会话数 | 网易云在跑但未播放 | `GetSessions().Count == 0` |
| 跨进程读取 | `MediaPlayer` 注册会话，另一进程读 | 成功读到 `provider.exe` |
| 时间轴粒度 | 250 ms 轮询 8 s | `Position` 约 6 s 跳一次；外推误差 ≈ 0.1 s |
| 网易云本地缓存 | 全盘搜 `*.lrc` | 无；`Library/*.dat` 被运行中进程独占 |
| 网易云接口 | `curl /api/song/lyric` | 200，返回 `lrc` + `tlyric` + `klyric`；eapi 变体返回 JSON 行逐字 |
| QQ 接口 | fcg 明文 / QRC 通道 | 明文 LRC 可取；QRC 为 XML 内十六进制密文 |
| 酷狗接口 | `content` base64 | 解码后以 `krc1` 开头 |
| LRCLIB 接口 | `curl /api/search` | 200；`/api/get`、`/api/get/{id}`、`/api/search`、`/api/v2/search` 路由由源码确认 |
| LRCLIB 不存在的路由 | `curl /api/get-cached` | 404，网上教程里的这个端点已不可用 |
| 酷我接口 | `newlyric` 请求 | 返回 `TP=DENY REQUEST`，仅第三方源码可参考，不能作为方案依据 |

> 注意：`Invoke-WebRequest` 在本机会因代理探测长时间挂起，用 `curl.exe -m` 直连才是有效判断。

---

## 10. 待验证清单

- [ ] 19041 投影对现有代码的 CA1416 影响（需全仓构建）。
- [ ] 网易云 / QQ / 酷狗 PC 端**播放时**的 SMTC `Position` 更新粒度（本次只测到「未播放 = 无会话」）。
- [ ] 网易云 eapi 逐字（yrc）的曲目覆盖率与风控触发条件。
- [ ] LRCLIB `hasWordSync` 的公开用法与逐字文本是否可通过 API 取到；中文曲目匹配率抽样。
- [ ] 酷我 / 汽水接口与格式（当前均无可靠实测）。
- [ ] Apple Music 公开 API 是否开放歌词端点；Windows 客户端 token 的合法获取方式。
- [ ] offset 标签方向在 foobar2000 等播放器中的实际解释（决定「反转开关」默认值）。
- [ ] 逐字渲染在 60 Hz 下的 UI 线程占用（需目标机器采样）。

---

## 11. 参考链接

- SMTC：<https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager>、[`Position`](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties.position)、[`LastUpdatedTime`](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties.lastupdatedtime)
- LRCLIB：<https://lrclib.net/docs>、服务端源码 <https://github.com/tranxuanthang/lrclib>
- Lyricify：<https://github.com/WXRIW/Lyricify-Lyrics-Helper>、歌词指南 <https://github.com/WXRIW/Lyricify-App/blob/main/docs/Lyricify%204/Lyrics.md>
- AMLL 格式与 TTML 事实标准：<https://github.com/amll-dev/applemusic-like-lyrics/tree/main/packages/docs/src/content/docs/guides/lyric>
- BetterLyrics：<https://github.com/jayfunc/BetterLyrics>
- LyricsX：<https://github.com/ddddxxx/LyricsX>
- MusicBee 桌面歌词：<https://github.com/cqjjjzr/MusicBee-DesktopLyrics>
- ModernFlyouts：<https://github.com/ModernFlyouts-Community/ModernFlyouts>
