using System;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 颜色字符串的唯一解析入口：只认 <c>#RRGGBB</c> / <c>#AARRGGBB</c>（与 <see cref="Settings.IsColorToken"/> 一致）。
    /// 不认颜色名、不认 3 位短写——配置里本来也只允许这两种格式。
    ///
    /// 以前这段逻辑在 7 个地方各写了一遍（OverlaySurface / CardButton / CardTextBox / ButtonsCard /
    /// MediaView / ThemeResolver / ThemeFile），而且“解析失败”分别返回白 / 前景 / false；这里统一成
    /// 「返回 false，调用方自己决定 fallback」。
    /// </summary>
    public static class ColorCode
    {
        public static bool TryParse(string code, out byte a, out byte r, out byte g, out byte b)
        {
            a = 255;
            r = 0;
            g = 0;
            b = 0;

            string text = (code ?? "").Trim();
            if (text.Length != 7 && text.Length != 9) return false;
            if (text[0] != '#') return false;

            int offset = text.Length == 9 ? 2 : 0;   // #AARRGGBB 比 #RRGGBB 多两位
            if (text.Length == 9 && !TryByte(text, 1, out a)) return false;

            return TryByte(text, 1 + offset, out r)
                && TryByte(text, 3 + offset, out g)
                && TryByte(text, 5 + offset, out b);
        }

        static bool TryByte(string text, int index, out byte value)
        {
            value = 0;
            int high = Hex(text[index]);
            int low = Hex(text[index + 1]);
            if (high < 0 || low < 0) return false;

            value = (byte)(high * 16 + low);
            return true;
        }

        static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
