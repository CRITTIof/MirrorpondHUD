using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// <see cref="ICardShellService"/> 的宿主实现：把已有的剪贴板 / 图标 / 拖出工具包一层，
    /// 插件只依赖 Contracts，不再直接引用宿主 Ui 命名空间。
    /// </summary>
    internal sealed class CardShellService : ICardShellService
    {
        public bool TryGetClipboardFiles(out string[] paths)
        {
            return ClipboardService.TryGetFiles(out paths);
        }

        public bool TrySetClipboardText(string text)
        {
            return ClipboardService.TrySetText(text);
        }

        public ImageSource GetFileIcon(string path, int size, bool extensionOnly)
        {
            return ShellIcons.GetIcon(path, size, extensionOnly);
        }

        public DragDropEffects BeginDragFiles(FrameworkElement source, IList<string> paths, DragDropEffects allowed)
        {
            if (source == null || paths == null || paths.Count == 0) return DragDropEffects.None;
            return DragSource.BeginFiles(source, paths, allowed);
        }

        public void OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("打开路径失败: " + ex.Message);
            }
        }

        public void RevealPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                Log.Warn("定位路径失败: " + ex.Message);
            }
        }
    }
}
