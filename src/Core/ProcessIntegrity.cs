using System;
using System.Runtime.InteropServices;

namespace MirrorpondHUD.Core;

/// <summary>
/// 进程完整性级别查询。低完整性（Low）下 Windows 会拒绝托盘图标、全局热键和键盘钩子，
/// 因此启动时要显式检查并提示用户。
/// </summary>
public static class ProcessIntegrity
{
    const int TokenIntegrityLevel = 25;
    const int QueryNotRun = int.MinValue;

    static int _level = QueryNotRun;
    static int _lastError;
    static string _lastErrorDetail = "";

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr buffer, uint length, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// 完整性级别 RID：0x0000 Untrusted、0x1000 Low、0x2000 Medium、0x3000 High、0x4000 System。
    /// 拿不到返回 -1；失败原因见 <see cref="LastErrorCode"/> / <see cref="LastErrorText"/>。
    /// 查询结果会缓存（启动期只查一次），成功 / 失败都不会影响启动。
    /// </summary>
    public static int Level
    {
        get
        {
            if (_level == QueryNotRun) QueryLevel();
            return _level;
        }
    }

    /// <summary>最近一次查询失败时的 Win32 错误码；成功或非 Win32 失败为 0。</summary>
    public static int LastErrorCode
    {
        get
        {
            if (_level == QueryNotRun) QueryLevel();
            return _lastError;
        }
    }

    /// <summary>最近一次查询失败的可读原因；成功为空串。报告 §7.2：失败不能只有一个 -1。</summary>
    public static string LastErrorText
    {
        get
        {
            int code = LastErrorCode;
            if (code != 0)
            {
                try
                {
                    return $"{new System.ComponentModel.Win32Exception(code).Message}（0x{code:X8}）";
                }
                catch (Exception ex)
                {
                    // 错误码无效 / 系统消息表异常：至少把码和异常类型留下。
                    return $"0x{code:X8}（{ex.GetType().Name}）";
                }
            }
            return _lastErrorDetail ?? "";
        }
    }

    static void QueryLevel()
    {
        _level = -1;
        _lastError = 0;
        _lastErrorDetail = "";

        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token))
            {
                _lastError = Marshal.GetLastWin32Error();
                return;
            }

            IntPtr buffer = Marshal.AllocHGlobal(256);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out var returned))
                {
                    _lastError = Marshal.GetLastWin32Error();
                    return;
                }

                // TOKEN_MANDATORY_LABEL { PSID Sid; DWORD Attributes; }
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                if (sid == IntPtr.Zero)
                {
                    _lastErrorDetail = "TOKEN_MANDATORY_LABEL 里的 SID 为空";
                    return;
                }

                int subAuthorityCount = Marshal.ReadByte(sid, 1);
                if (subAuthorityCount <= 0)
                {
                    _lastErrorDetail = "SID 的 SubAuthorityCount 非正";
                    return;
                }

                // SID = Revision(1) + SubAuthorityCount(1) + IdentifierAuthority(6) + N * DWORD
                IntPtr last = new(sid.ToInt64() + 8 + (subAuthorityCount - 1) * 4);
                _level = Marshal.ReadInt32(last);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            // 查询失败不能影响启动：保留 -1，并把异常类型记进可读原因。
            _lastErrorDetail = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            // P2-2：OpenProcessToken 的句柄之前从未关闭，启动期调用几次就泄漏几次。
            if (token != IntPtr.Zero) CloseHandle(token);
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
                default: return $"0x{Level:X}";
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
