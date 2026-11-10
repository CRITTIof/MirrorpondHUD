using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls
{
    /// <summary>
    /// Lucide 图标字体与 CardIcon 的最小回归：码位表 / 查询 / 字体族 / 真实渲染。
    /// 返回 null 表示通过，否则是中文失败说明。
    /// </summary>
    public static class CardIconSelfTest
    {
        public static string Run()
        {
            try
            {
                if (LucideIcons.Names.Count < 1000) return "Lucide 图标名数量异常：" + LucideIcons.Names.Count;
                if (LucideIcons.Get("cpu") != LucideIcons.Cpu) return "Lucide CPU 码位不一致";
                if (LucideIcons.Get("icon-cpu") != LucideIcons.Cpu) return "Lucide 带 icon- 前缀查询失败";
                if (LucideIcons.Get("__no_such_icon__").Length != 0) return "Lucide 未知图标没有返回空串";
                if (string.IsNullOrEmpty(LucideIcons.TriangleAlert)) return "Lucide 告警图标为空";

                // 2.26.0：卡片图标统一改 Lucide，但文本画布必须把键帽的 PUA 码位留给 Reicon。
                if (!Icons.IsIconChar(Icons.Cpu[0])) return "Icons.Cpu 没有被登记为图标字符";
                if (!Icons.IsIconChar(Icons.Terminal[0])) return "Icons.Terminal 没有被登记为图标字符";
                if (!Icons.IsIconChar(Icons.TabBehavior[0])) return "Icons.TabBehavior 没有被登记为图标字符";
                if (Icons.IsIconChar('\uE000')) return "键帽码位 U+E000 被误判为图标";
                if (Icons.IsIconChar('\uE042')) return "未登记的 Lucide 码位被误判为图标";
                if (Icons.Cpu[0] <= '\uE056') return "Icons.Cpu 落在键帽码位区间";

                var icon = new CardIcon(LucideIcons.Cpu) { Size = 24, Width = 64, Height = 64 };
                icon.ApplyStyle(new StyleSettings
                {
                    FontSize = 13,
                    Foreground = "#FFE6EDF3"
                }, 8);

                icon.Measure(new Size(64, 64));
                icon.Arrange(new Rect(0, 0, 64, 64));
                icon.UpdateLayout();

                var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(icon);

                int pixels = 64 * 64;
                var data = new byte[pixels * 4];
                bitmap.CopyPixels(data, 64 * 4, 0);
                bool drew = false;
                for (int i = 3; i < data.Length; i += 4)
                {
                    if (data[i] != 0) { drew = true; break; }
                }
                return drew ? null : "CardIcon 没有渲染出任何像素（字体可能未加载）";
            }
            catch (Exception ex)
            {
                return "图标字体自检异常：" + ex.GetType().Name + "：" + ex.Message;
            }
        }
    }
}
