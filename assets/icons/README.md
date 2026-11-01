# Lucide 图标字体

- 字体：`Lucide.ttf`（TrueType，字体族名 `lucide`）
- 来源：https://lucide.dev / `lucide-static` npm 包 1.66.0，文件路径 `font/lucide.ttf`
- 许可证：ISC，见同目录 `LICENSE.txt`（Copyright (c) 2026 Lucide Icons and Contributors）
- 码位表：`src/Contracts/Core/LucideIcons.cs`，由 `lucide.css` 的 `icon-*` 映射生成（共 2292 个名字）

## 在卡片里使用

```csharp
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;

var icon = new CardIcon(LucideIcons.Cpu) { Size = 20 };
icon.ApplyStyle(context.Settings.Style, context.Settings.Overlay.CornerRadius);

// 或者直接使用 WPF TextBlock
var text = new System.Windows.Controls.TextBlock
{
    Text = LucideIcons.Get("chart-line"),
    FontFamily = LucideIcons.FontFamily,
    FontSize = 20
};
```

`LucideIcons` 同时提供：
- 常用常量（`LucideIcons.Cpu` / `Terminal` / `Folder` / `ChartLine` / `TriangleAlert` 等）；
- 全部名字：`LucideIcons.Names`；
- 按 CSS 名查询：`LucideIcons.Get("cpu")` / `LucideIcons.TryGet("cpu", out glyph)`（可带 `icon-` 前缀）。

## 更新方式

从 `lucide-static` 新版本取 `font/lucide.ttf` 与 `font/lucide.css`，替换字体并重新生成
`LucideIcons.cs` 的常量 / 字典；不要手工改 TTF。重新生成后跑 `--selftest` 和一张图标预览图。
