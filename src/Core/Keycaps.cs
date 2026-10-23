using System;
using System.Collections.Generic;
using System.Text;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 按键名 → Reicon 字体的键帽字符（PUA U+E000–U+E056）。
    ///
    /// 这里只负责「换显示形态」：字符数截断仍由 <see cref="KeyHistory.Render"/> 按按键名计算，
    /// 所以改用键帽显示后，「最多显示多少个字符」的判定与原来完全一致。
    /// 没有对应字形的键（媒体键 / VKxx…）原样保留为文字。
    /// </summary>
    public static class Keycaps
    {
        public const char First = '\uE000';
        public const char Last = '\uE056';

        static readonly Dictionary<string, char> Map = Build();

        public static bool IsKeycap(char c)
        {
            return c >= First && c <= Last;
        }

        /// <summary>
        /// 把 <see cref="KeyHistory.Render"/> 的输出（如 "Ctrl A×6 Enter"）换成键帽字符。
        /// 重复折叠的 "×6" 后缀保留为普通文字，跟在键帽后面。
        /// </summary>
        public static string FromRendered(string rendered)
        {
            if (string.IsNullOrEmpty(rendered)) return rendered;

            string[] tokens = rendered.Split(' ');
            var sb = new StringBuilder(rendered.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                AppendToken(sb, tokens[i]);
            }
            return sb.ToString();
        }

        static void AppendToken(StringBuilder sb, string token)
        {
            if (string.IsNullOrEmpty(token)) return;

            string name = token;
            string suffix = "";
            int repeat = token.IndexOf('×');
            if (repeat > 0)
            {
                name = token.Substring(0, repeat);
                suffix = token.Substring(repeat);
            }

            char glyph;
            if (Map.TryGetValue(name, out glyph))
            {
                sb.Append(glyph);
                sb.Append(suffix);
            }
            else
            {
                sb.Append(token);
            }
        }

        static Dictionary<string, char> Build()
        {
            var map = new Dictionary<string, char>(StringComparer.Ordinal);

            // 功能键 / 编辑键
            map["Esc"] = '\uE000';
            for (int i = 1; i <= 12; i++) map["F" + i] = (char)(0xE000 + i);
            map["ins"] = '\uE00D';
            map["Del"] = '\uE00E';
            map["Home"] = '\uE00F';
            map["End"] = '\uE010';
            map["PgUp"] = '\uE011';
            map["PgDn"] = '\uE012';

            // 主键区第一排
            map["`"] = '\uE013';
            for (int i = 1; i <= 9; i++) map[i.ToString()] = (char)(0xE013 + i);
            map["0"] = '\uE01D';
            map["-"] = '\uE01E';
            map["="] = '\uE01F';
            map["Back"] = '\uE020';
            map["Tab"] = '\uE021';

            const string top = "QWERTYUIOP";
            for (int i = 0; i < top.Length; i++) map[top[i].ToString()] = (char)(0xE022 + i);

            map["["] = '\uE02C';
            map["]"] = '\uE02D';
            map["\\"] = '\uE02E';
            map["Cap"] = '\uE02F';

            const string home = "ASDFGHJKL";
            for (int i = 0; i < home.Length; i++) map[home[i].ToString()] = (char)(0xE030 + i);

            map[";"] = '\uE039';
            map["'"] = '\uE03A';
            map["Enter"] = '\uE03B';
            map["Shift"] = '\uE03C';

            const string bottom = "ZXCVBNM";
            for (int i = 0; i < bottom.Length; i++) map[bottom[i].ToString()] = (char)(0xE03D + i);

            map[","] = '\uE044';
            map["."] = '\uE045';
            map["/"] = '\uE046';

            // 修饰键
            map["Ctrl"] = '\uE047';
            map["Fn"] = '\uE048';
            map["Win"] = '\uE049';
            map["Alt"] = '\uE04A';
            map["Space"] = '\uE04B';
            map["←"] = '\uE04C';
            map["↑"] = '\uE04D';
            map["↓"] = '\uE04E';
            map["→"] = '\uE04F';

            // 小键盘：数字与主键区共用字形，运算符号复用主键区的 - . /
            map["NumLock"] = '\uE050';
            map["Num*"] = '\uE051';
            map["Num+"] = '\uE052';
            map["Num-"] = '\uE01E';
            map["Num."] = '\uE045';
            map["Num/"] = '\uE046';

            // 新版键帽：打印屏幕 / 滚动锁定 / 暂停 / 菜单
            map["PrtSc"] = '\uE053';
            map["ScrollLock"] = '\uE054';
            map["Pause"] = '\uE055';
            map["Menu"] = '\uE056';
            for (int i = 0; i <= 9; i++)
                map["Num" + i] = i == 0 ? '\uE01D' : (char)(0xE013 + i);

            return map;
        }
    }
}
