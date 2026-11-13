using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 本地 LRC 读取（2.47.0，P0-7）：中文歌词站下载的 .lrc 大量是 GBK/GB18030，
/// 直接按 UTF-8 读会整篇乱码。先看 BOM，再严格 UTF-8，最后回退 GB18030。
///
/// 零依赖：不引 System.Text.Encoding.CodePages，直接调 MultiByteToWideChar(936)。
/// </summary>
internal static class LrcFile
{
    const int CodePageGb18030 = 936;
    const uint MbErrInvalidChars = 0x00000008;

    public static async Task<string> ReadAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        return DecodeBytes(bytes);
    }

    /// <summary>纯解码入口（自检直接调，不需要真实文件）。</summary>
    public static string DecodeBytes(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return "";

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            // 严格解码：遇到非法 UTF-8 字节直接抛，不静默替换成乱码。
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DecodeGb18030(bytes);
        }
    }

    static string DecodeGb18030(byte[] bytes)
    {
        int needed = MultiByteToWideChar(CodePageGb18030, MbErrInvalidChars, bytes, bytes.Length, null, 0);
        if (needed <= 0)
            throw new InvalidOperationException($"LRC 既不是 UTF-8 也不是 GB18030（Win32 错误 {Marshal.GetLastWin32Error()}）");

        var chars = new char[needed];
        int written = MultiByteToWideChar(CodePageGb18030, MbErrInvalidChars, bytes, bytes.Length, chars, needed);
        if (written <= 0)
            throw new InvalidOperationException($"LRC GB18030 解码失败（Win32 错误 {Marshal.GetLastWin32Error()}）");

        return new string(chars, 0, written);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] multiByte, int multiByteCount,
                                          char[] wideChar, int wideCharCount);
}
