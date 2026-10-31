using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 卡片内容发起 OLE 拖出的最小助手：只做“阈值判断 + DoDragDrop”，
    /// 不接管行上的鼠标事件（中转站 / 画板自己决定从哪一行、哪个图形开始拖）。
    ///
    /// 注意：<see cref="BeginFiles"/> / <see cref="BeginText"/> 会阻塞到拖放结束，必须在 UI 线程调用；
    /// 与整卡拖动的冲突见 docs/file-transfer-station.md §7.2。
    /// </summary>
    public static class DragSource
    {
        public const double DefaultThreshold = 6.0;

        /// <summary>按下后移动超过阈值才认为是“拖”，避免把单击误判成拖出。</summary>
        public static bool ShouldStart(Point pressOrigin, Point current, double threshold = DefaultThreshold)
        {
            double dx = current.X - pressOrigin.X;
            double dy = current.Y - pressOrigin.Y;
            return dx * dx + dy * dy >= threshold * threshold;
        }

        /// <summary>把一组文件拖出（资源管理器 / 其他程序）。返回目标接受的效果。</summary>
        public static DragDropEffects BeginFiles(FrameworkElement source, IEnumerable<string> paths,
                                                DragDropEffects allowed = DragDropEffects.Copy)
        {
            if (source == null || paths == null) return DragDropEffects.None;

            var list = new List<string>();
            foreach (string path in paths)
                if (!string.IsNullOrWhiteSpace(path)) list.Add(path);

            if (list.Count == 0) return DragDropEffects.None;

            try
            {
                var data = new DataObject(DataFormats.FileDrop, list.ToArray());
                return DragDrop.DoDragDrop(source, data, allowed);
            }
            catch (Exception ex)
            {
                Log.Warn("拖出文件失败: " + ex.Message);
                return DragDropEffects.None;
            }
        }

        /// <summary>把一段文本拖出（输入框 / 便签 / 聊天记录）。</summary>
        public static DragDropEffects BeginText(FrameworkElement source, string text,
                                                DragDropEffects allowed = DragDropEffects.Copy)
        {
            if (source == null || string.IsNullOrEmpty(text)) return DragDropEffects.None;

            try
            {
                var data = new DataObject(DataFormats.UnicodeText, text);
                return DragDrop.DoDragDrop(source, data, allowed);
            }
            catch (Exception ex)
            {
                Log.Warn("拖出文本失败: " + ex.Message);
                return DragDropEffects.None;
            }
        }
    }
}
