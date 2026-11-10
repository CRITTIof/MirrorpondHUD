using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 把文件 / 文件夹移到回收站的最小封装（SHFileOperationW）。
    /// 为什么不用 Directory.Delete / File.Delete：那两条 API 是不可逆删除，误删后只能靠外部备份；
    /// 中转站接收目录里的文件是用户拖进来的暂存数据，删除必须是可还原动作。
    /// 这里只做单路径删除：调用方（<see cref="StationFileActions.DeleteFiles"/>）已经逐项过滤并逐项调用，
    /// 这样某一项失败时其余项仍能各自拿到明确的返回码，而不是整批静默失败。
    /// 注意：SHFileOperationW 的 pFrom 受 MAX_PATH 限制；超长路径会以非 0 返回码失败并留在列表，
    /// 调用方不得因此回退到永久删除（长路径需要 IFileOperation，另立项处理）。
    /// </summary>
    internal static class RecycleBin
    {
        // SHFileOperation 的 wFunc 只定义了 COPY / MOVE / DELETE / RENAME，删除固定是 3。
        const uint FO_DELETE = 0x0003;

        // FOF_SILENT：不显示进度对话框；FOF_NOCONFIRMATION：不再弹系统确认框（中转站自己已经确认过）；
        // FOF_ALLOWUNDO：走回收站；FOF_NOERRORUI：失败不要弹系统错误框，交给状态行与日志。
        const ushort FOF_SILENT = 0x0004;
        const ushort FOF_NOCONFIRMATION = 0x0010;
        const ushort FOF_ALLOWUNDO = 0x0040;
        const ushort FOF_NOERRORUI = 0x0400;

        // SHFileOperation 返回 0 但 fAnyOperationsAborted 非 0 时表示“操作被中止”（系统对话框被取消等），
        // 这里借用 Win32 的 ERROR_CANCELLED，让调用方的失败明细一眼能看出不是文件系统错误。
        const int ErrorCancelled = 1223;

        /// <summary>
        /// 把单个路径（文件或目录）移到回收站。不抛异常：成功返回 true；
        /// 失败返回 false 并写回 SHFileOperation 返回码（成功时为 0），供调用方汇总进日志与状态行。
        /// </summary>
        /// <param name="owner">用于系统对话框的宿主窗口；null 时 hwnd 传 IntPtr.Zero。</param>
        /// <param name="path">已归一化且确认存在的路径；空路径直接失败。</param>
        /// <param name="errorCode">失败返回码；成功时为 0。</param>
        public static bool TryMoveToRecycleBin(Window owner, string path, out int errorCode)
        {
            errorCode = 0;
            if (string.IsNullOrEmpty(path))
            {
                // 空路径没有可交给 SHFileOperation 的输入；用 ERROR_INVALID_PARAMETER 让调用方的
                // 失败明细仍能显示一个非 0 返回码，而不是看起来像“成功但没做事”。
                errorCode = 87;
                return false;
            }

            IntPtr from = IntPtr.Zero;
            try
            {
                // pFrom 是 PCZZWSTR：路径自身以 NUL 结束，整个列表再以额外一个 NUL 结束。
                // StringToHGlobalUni 会自动补一个结尾 NUL，叠加 path 尾部这个 NUL 后正好是双 NUL；
                // 单路径不需要再建路径数组。
                from = Marshal.StringToHGlobalUni(path + "\0");

                var op = new SHFILEOPSTRUCT();
                op.hwnd = HwndOf(owner);
                op.wFunc = FO_DELETE;
                op.pFrom = from;
                op.pTo = IntPtr.Zero;
                op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);
                op.fAnyOperationsAborted = 0;
                op.hNameMappings = IntPtr.Zero;
                op.lpszProgressTitle = IntPtr.Zero;

                int code = SHFileOperation(ref op);
                if (code != 0)
                {
                    // 失败码原样交给调用方；不在这里吞掉，也绝不回退到永久删除。
                    errorCode = code;
                    return false;
                }

                if (op.fAnyOperationsAborted != 0)
                {
                    errorCode = ErrorCancelled;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // P/Invoke 与字符串封送在极端情况下会抛（内存不足等）；这里不能静默，
                // 记日志并把 -1 当作“没拿到 SHFileOperation 返回码”的失败码交给调用方。
                Log.Warn("中转站调用回收站失败  路径=" + path + "  原因=" + ex.Message);
                errorCode = -1;
                return false;
            }
            finally
            {
                if (from != IntPtr.Zero) Marshal.FreeHGlobal(from);
            }
        }

        static IntPtr HwndOf(Window owner)
        {
            if (owner == null) return IntPtr.Zero;
            // WindowInteropHelper 必须在 UI 线程取；删除入口全部来自右键菜单 / 按钮回调。
            return new WindowInteropHelper(owner).Handle;
        }

        // SHFILEOPSTRUCTW 的原生布局（x86 / x64 / ARM64 都按平台默认指针对齐）：
        //   HWND hwnd; UINT wFunc; PCZZWSTR pFrom; PCZZWSTR pTo;
        //   FILEOP_FLAGS fFlags（WORD，2 字节）; BOOL fAnyOperationsAborted;
        //   LPVOID hNameMappings; PCWSTR lpszProgressTitle;
        // 关键点：fFlags 必须是 ushort / short。若写成 int，x64 上会把后面的 BOOL 与两个指针整体推后，
        // 系统读到的 pFrom 就错位（可能直接崩在 shell32 里）。
        // 指针统一用 IntPtr，不能换成 string：封送 string 只按单个 NUL 截断，SHFileOperation 拿到的
        // 列表就没有“双 NUL 结束”，在 x64 / ARM64 上会把相邻内存当第二条路径继续解析。
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public IntPtr pFrom;
            public IntPtr pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public IntPtr lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    }
}
