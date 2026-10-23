using System;
using System.Runtime.InteropServices;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 进程完整性级别查询。低完整性（Low）下 Windows 会拒绝托盘图标、全局热键和键盘钩子，
    /// 因此启动时要显式检查并提示用户。
    /// </summary>
    public static class ProcessIntegrity
    {
        const int TokenIntegrityLevel = 25;

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr buffer, uint length, out uint returnLength);

        /// <summary>完整性级别 RID：0x0000 Untrusted、0x1000 Low、0x2000 Medium、0x3000 High、0x4000 System。拿不到返回 -1。</summary>
        public static int Level
        {
            get
            {
                try
                {
                    IntPtr token;
                    if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token)) return -1;

                    IntPtr buffer = Marshal.AllocHGlobal(256);
                    try
                    {
                        uint returned;
                        if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out returned)) return -1;

                        // TOKEN_MANDATORY_LABEL { PSID Sid; DWORD Attributes; }
                        IntPtr sid = Marshal.ReadIntPtr(buffer);
                        if (sid == IntPtr.Zero) return -1;

                        int subAuthorityCount = Marshal.ReadByte(sid, 1);
                        if (subAuthorityCount <= 0) return -1;

                        // SID = Revision(1) + SubAuthorityCount(1) + IdentifierAuthority(6) + N * DWORD
                        IntPtr last = new IntPtr(sid.ToInt64() + 8 + (subAuthorityCount - 1) * 4);
                        return Marshal.ReadInt32(last);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                catch { return -1; }
            }
        }

        public static string Name
        {
            get
            {
                switch (Level)
                {
                    case 0x0000: return "Untrusted";
                    case 0x1000: return "Low";
                    case 0x2000: return "Medium";
                    case 0x3000: return "High";
                    case 0x4000: return "System";
                    case -1: return "Unknown";
                    default: return "0x" + Level.ToString("X");
                }
            }
        }

        /// <summary>低完整性/不可信时，托盘、热键、全局钩子都会被系统拒绝。</summary>
        public static bool IsRestricted
        {
            get
            {
                int level = Level;
                return level >= 0 && level <= 0x1000;
            }
        }
    }
}
