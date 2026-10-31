using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 剪贴板读写：统一处理“剪贴板被别的程序占用”的 COM 异常（重试几次），
    /// 并给出失败原因而不是让调用方各写一套 try/catch。
    ///
    /// WPF 的 Clipboard 只能在 STA 线程（UI 线程）调用；这里的同步方法请从 UI 线程使用。
    /// 文件/文本/图片都可读写；图片会 Freeze，方便跨线程显示。
    /// </summary>
    public static class ClipboardService
    {
        const int Retries = 3;
        const int RetryDelayMs = 30;

        public static bool TryGetText(out string text)
        {
            string value = null;
            bool ok = Execute(() =>
            {
                if (!Clipboard.ContainsText()) return false;
                value = Clipboard.GetText();
                return true;
            }, "读取文本");
            text = value ?? "";
            return ok;
        }

        public static bool TrySetText(string text)
        {
            return Execute(() =>
            {
                if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                else Clipboard.SetText(text);
                return true;
            }, "写入文本");
        }

        public static bool TryGetFiles(out string[] paths)
        {
            string[] captured = null;
            bool ok = Execute(() =>
            {
                if (!Clipboard.ContainsFileDropList()) return false;
                StringCollection list = Clipboard.GetFileDropList();
                if (list == null || list.Count == 0) return false;

                var result = new string[list.Count];
                list.CopyTo(result, 0);
                captured = result;
                return true;
            }, "读取文件列表");

            paths = captured ?? new string[0];
            return ok;
        }

        public static bool TrySetFiles(IEnumerable<string> paths)
        {
            var list = new StringCollection();
            if (paths != null)
            {
                foreach (string path in paths)
                    if (!string.IsNullOrWhiteSpace(path)) list.Add(path);
            }

            if (list.Count == 0) return false;
            return Execute(() =>
            {
                Clipboard.SetFileDropList(list);
                return true;
            }, "写入文件列表");
        }

        public static bool TryGetImage(out BitmapSource image)
        {
            BitmapSource captured = null;
            bool ok = Execute(() =>
            {
                if (!Clipboard.ContainsImage()) return false;
                BitmapSource source = Clipboard.GetImage();
                if (source == null) return false;
                if (source.CanFreeze) source.Freeze();
                captured = source;
                return true;
            }, "读取图片");

            image = captured;
            return ok;
        }

        /// <summary>重试执行：剪贴板被占用时 Windows 会抛 CLIPBRD_E_CANT_OPEN（COMException）。</summary>
        static bool Execute(Func<bool> action, string operation)
        {
            for (int attempt = 0; attempt < Retries; attempt++)
            {
                try
                {
                    return action();
                }
                catch (COMException ex)
                {
                    if (attempt == Retries - 1)
                    {
                        Log.Warn("剪贴板" + operation + "失败（被占用）：" + ex.Message);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("剪贴板" + operation + "失败：" + ex.Message);
                    return false;
                }

                Thread.Sleep(RetryDelayMs);
            }

            return false;
        }
    }
}
