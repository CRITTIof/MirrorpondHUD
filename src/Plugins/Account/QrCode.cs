using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 最小二维码生成器（2.47.0）：固定 Version 4、ECC L、字节模式、mask 0。
///
/// 只服务于森空岛扫码登录：内容是 <c>hypergryph://scan_login?scanId=...</c>（约 63 字节），
/// Version 4-L 的字节容量是 78，固定版本后不需要容量选择 / 自动掩码 / 版本信息，
/// 少一大截表和分支，出问题也容易定位。不可用于任意长文本。
///
/// 实现按 ISO/IEC 18004 的公开算法：字节模式 → 分块 RS(20) → 之字形放码 → mask 0。
/// 自检里用「矩阵尺寸 + 定位图形 + 格式信息」做冒烟；开发期用 Python qrcode 逐位比对过。
/// </summary>
internal static class QrCode
{
    const int Version = 4;
    const int Size = 17 + Version * 4;      // 33
    const int DataCodewords = 80;           // v4-L：100 总码字 - 20 纠错
    const int EccCodewords = 20;
    const int Mask = 0;

    public static bool[,] Encode(string text)
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(text ?? "");
        if (payload.Length == 0 || payload.Length > 78)
            throw new ArgumentOutOfRangeException(nameof(text), "二维码内容必须是 1–78 字节");

        byte[] data = BuildDataCodewords(payload);
        byte[] ecc = ReedSolomon(data, EccCodewords);

        var modules = new bool[Size, Size];
        var function = new bool[Size, Size];

        DrawFunctionPatterns(modules, function);
        // 先把格式信息区标记成功能区，否则放码时数据会写进这些位置、后续全部错位。
        DrawFormatBits(modules, function);
        DrawCodewords(modules, function, data, ecc);
        ApplyMask(modules, function);
        DrawFormatBits(modules, function);

        return modules;
    }

    static byte[] BuildDataCodewords(byte[] payload)
    {
        var bits = new List<bool>(DataCodewords * 8);
        AppendBits(bits, 0b0100, 4);                 // 字节模式
        AppendBits(bits, payload.Length, 8);         // v1–9 用 8 位字符计数
        foreach (byte b in payload) AppendBits(bits, b, 8);

        int capacityBits = DataCodewords * 8;
        AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count)); // 终止符
        while (bits.Count % 8 != 0) bits.Add(false);

        var data = new List<byte>(DataCodewords);
        for (int i = 0; i < bits.Count; i += 8)
        {
            int value = 0;
            for (int j = 0; j < 8; j++) value = (value << 1) | (bits[i + j] ? 1 : 0);
            data.Add((byte)value);
        }

        // 交替填充 0xEC / 0x11，直到 DataCodewords。
        for (int pad = 0; data.Count < DataCodewords; pad++)
            data.Add((byte)(pad % 2 == 0 ? 0xEC : 0x11));

        return data.ToArray();
    }

    static void AppendBits(List<bool> bits, int value, int length)
    {
        for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    // ==================== Reed–Solomon (GF(256), 0x11D) ====================

    static byte[] ReedSolomon(byte[] data, int degree)
    {
        byte[] generator = BuildGenerator(degree);
        var remainder = new byte[degree];

        foreach (byte value in data)
        {
            int factor = value ^ remainder[0];
            Array.Copy(remainder, 1, remainder, 0, degree - 1);
            remainder[degree - 1] = 0;
            for (int i = 0; i < degree; i++)
                remainder[i] ^= GfMultiply(generator[i + 1], (byte)factor);
        }

        return remainder;
    }

    static byte[] BuildGenerator(int degree)
    {
        var result = new byte[degree + 1];
        result[0] = 1;
        int count = 1;
        for (int i = 0; i < degree; i++)
        {
            byte root = GfPow(2, i);
            // 乘以 (x - root)：从高次往低次更新。
            for (int j = count; j >= 1; j--)
                result[j] ^= GfMultiply(result[j - 1], root);
            count++;
        }
        return result;
    }

    static byte GfMultiply(byte x, byte y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    static byte GfPow(byte x, int power)
    {
        byte result = 1;
        for (int i = 0; i < power; i++) result = GfMultiply(result, x);
        return result;
    }

    // ==================== 矩阵 ====================

    static void DrawFunctionPatterns(bool[,] modules, bool[,] function)
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunction(modules, function, 6, i, i % 2 == 0);
            SetFunction(modules, function, i, 6, i % 2 == 0);
        }

        DrawFinder(modules, function, 0, 0);
        DrawFinder(modules, function, Size - 7, 0);
        DrawFinder(modules, function, 0, Size - 7);

        // v4 只有一个对齐图形，中心 (26, 26)。
        DrawAlignment(modules, function, 26, 26);
    }

    static void DrawFinder(bool[,] modules, bool[,] function, int left, int top)
    {
        for (int y = -1; y <= 7; y++)
        {
            for (int x = -1; x <= 7; x++)
            {
                int px = left + x;
                int py = top + y;
                if (px < 0 || py < 0 || px >= Size || py >= Size) continue;

                bool dark = x >= 0 && x <= 6 && y >= 0 && y <= 6
                            && (x == 0 || x == 6 || y == 0 || y == 6 || (x >= 2 && x <= 4 && y >= 2 && y <= 4));
                SetFunction(modules, function, px, py, dark);
            }
        }
    }

    static void DrawAlignment(bool[,] modules, bool[,] function, int cx, int cy)
    {
        for (int y = -2; y <= 2; y++)
        {
            for (int x = -2; x <= 2; x++)
            {
                bool dark = Math.Max(Math.Abs(x), Math.Abs(y)) != 1;
                SetFunction(modules, function, cx + x, cy + y, dark);
            }
        }
    }

    static void SetFunction(bool[,] modules, bool[,] function, int x, int y, bool dark)
    {
        modules[y, x] = dark;
        function[y, x] = true;
    }

    static void DrawCodewords(bool[,] modules, bool[,] function, byte[] data, byte[] ecc)
    {
        var codewords = new List<byte>(data.Length + ecc.Length);
        codewords.AddRange(data);
        codewords.AddRange(ecc);

        int bitIndex = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < Size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vert : vert;
                    if (function[y, x]) continue;

                    bool dark = false;
                    if (bitIndex < codewords.Count * 8)
                    {
                        dark = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                    }
                    modules[y, x] = dark;
                    bitIndex++;
                }
            }
        }
    }

    static void ApplyMask(bool[,] modules, bool[,] function)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (function[y, x]) continue;
                // mask 0：(x + y) % 2 == 0 取反。
                if ((x + y) % 2 == 0) modules[y, x] = !modules[y, x];
            }
        }
    }

    static void DrawFormatBits(bool[,] modules, bool[,] function)
    {
        // ECC L 的 format bits = 1（两位 01 里的 1）；mask 0。
        int data = (1 << 3) | Mask;
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;

        // 左上角副本。
        for (int i = 0; i <= 5; i++) SetFormat(modules, function, 8, i, GetBit(bits, i));
        SetFormat(modules, function, 8, 7, GetBit(bits, 6));
        SetFormat(modules, function, 8, 8, GetBit(bits, 7));
        SetFormat(modules, function, 7, 8, GetBit(bits, 8));
        for (int i = 9; i < 15; i++) SetFormat(modules, function, 14 - i, 8, GetBit(bits, i));

        // 右上 / 左下副本。
        for (int i = 0; i < 8; i++) SetFormat(modules, function, Size - 1 - i, 8, GetBit(bits, i));
        for (int i = 8; i < 15; i++) SetFormat(modules, function, 8, Size - 15 + i, GetBit(bits, i));
        SetFormat(modules, function, 8, Size - 8, true); // 固定深色模块
    }

    static void SetFormat(bool[,] modules, bool[,] function, int x, int y, bool dark)
    {
        modules[y, x] = dark;
        function[y, x] = true;
    }

    static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;
}
