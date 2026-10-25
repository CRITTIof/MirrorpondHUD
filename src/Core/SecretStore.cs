using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 字符串加密存储：Windows DPAPI（当前用户范围）。
    ///
    /// settings.json 里只落 <c>dpapi:&lt;base64&gt;</c>，真正保护密钥的是 Windows 的用户配置文件
    /// （DPAPI 的主密钥存在 <c>%APPDATA%\Microsoft\Protect</c>，由用户登录口令派生）。
    /// 所以：
    /// - 同一台机器、同一个 Windows 用户：能解开，删掉 settings.json 就彻底没了；
    /// - 换用户 / 换机器：解不开（这时 <see cref="TryUnprotect"/> 返回 false，界面提示重新填写）；
    /// - 别人拿到 settings.json：只能看到一串 base64 密文，解不开。
    ///
    /// 用 P/Invoke 直接调 crypt32.dll，不引 NuGet 包，单文件发布不受影响。
    /// </summary>
    public static class SecretStore
    {
        /// <summary>加密串的前缀；见到它才说明这一项是密文。</summary>
        public const string Prefix = "dpapi:";

        /// <summary>附加熵：把密文和这个用途绑在一起，换个用途（别的 app）解不开。</summary>
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MirrorpondHUD|secret|v1");

        const int CryptprotectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(
            ref DataBlob dataIn, string description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(
            ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr memory);

        /// <summary>这一项是不是密文（旧配置里可能是明文）。</summary>
        public static bool IsProtected(string stored)
        {
            return !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 加密成 <c>dpapi:base64</c>。加不了密（极罕见）就原样返回明文，
        /// 保证功能可用，同时往日志里写一条警告。
        /// </summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";

            try
            {
                return Prefix + Convert.ToBase64String(Transform(plain, true));
            }
            catch (Exception ex)
            {
                Log.Warn("加密失败，这一项只能明文保存: " + ex.Message);
                return plain;
            }
        }

        /// <summary>解出明文；不是密文、或者在别的用户 / 机器上加密的，返回 false。</summary>
        public static bool TryUnprotect(string stored, out string plain)
        {
            plain = "";
            if (string.IsNullOrEmpty(stored)) return false;

            if (!IsProtected(stored))
            {
                plain = stored;
                return true;
            }

            try
            {
                string base64 = stored.Substring(Prefix.Length);
                byte[] blob = Convert.FromBase64String(base64);
                plain = Encoding.UTF8.GetString(Transform(blob, false));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("解密失败（换了 Windows 用户或机器？）: " + ex.Message);
                return false;
            }
        }

        /// <summary>只处理字节；text 版本走 UTF-8。</summary>
        static byte[] Transform(string text, bool protect)
        {
            return Transform(Encoding.UTF8.GetBytes(text), protect);
        }

        static byte[] Transform(byte[] input, bool protect)
        {
            IntPtr inputPtr = Marshal.AllocHGlobal(Math.Max(1, input.Length));
            IntPtr entropyPtr = Marshal.AllocHGlobal(Math.Max(1, Entropy.Length));

            try
            {
                Marshal.Copy(input, 0, inputPtr, input.Length);
                Marshal.Copy(Entropy, 0, entropyPtr, Entropy.Length);

                var inputBlob = new DataBlob { Size = input.Length, Data = inputPtr };
                var entropyBlob = new DataBlob { Size = Entropy.Length, Data = entropyPtr };

                var output = new DataBlob();
                bool ok = protect
                    ? CryptProtectData(ref inputBlob, "MirrorpondHUD", ref entropyBlob,
                        IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output)
                    : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob,
                        IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output);

                int error = Marshal.GetLastWin32Error();

                try
                {
                    if (!ok) throw new System.ComponentModel.Win32Exception(error);
                    var result = new byte[output.Size];
                    Marshal.Copy(output.Data, result, 0, output.Size);
                    return result;
                }
                finally
                {
                    if (output.Data != IntPtr.Zero) LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inputPtr);
                Marshal.FreeHGlobal(entropyPtr);
            }
        }
    }
}
