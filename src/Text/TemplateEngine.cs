using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WindowOverlay.Core
{
    /// <summary>
    /// 模板渲染：{name}、{name:0.0}、{name,8}、{name,8:0.0}；
    /// 花括号用 {{ }} 转义。未知变量原样保留并记录。
    /// </summary>
    public static class TemplateEngine
    {
        public static string Render(string template, IDictionary<string, object> variables)
        {
            List<string> ignored;
            return Render(template, variables, out ignored);
        }

        public static string Render(string template, IDictionary<string, object> variables, out List<string> unknown)
        {
            return Render(template, variables, out unknown, null);
        }

        /// <summary>
        /// 把所有占位符替换成固定值，用于启动动画阶段展示 "---"。
        /// </summary>
        public static string RenderPlaceholder(string template, string placeholder)
        {
            List<string> ignored;
            return Render(template, null, out ignored, placeholder ?? "---");
        }

        public static string Render(string template, IDictionary<string, object> variables, out List<string> unknown, string forceValue)
        {
            unknown = new List<string>();
            if (string.IsNullOrEmpty(template)) return "";

            // 先保护转义花括号，避免被当成变量
            string working = template.Replace("{{", "\u0001").Replace("}}", "\u0002");
            var sb = new StringBuilder(working.Length + 64);
            int i = 0;

            while (i < working.Length)
            {
                char c = working[i];
                if (c != '{') { sb.Append(c); i++; continue; }

                int close = working.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(c); i++; continue; }

                string token = working.Substring(i + 1, close - i - 1);
                string name, format;
                int alignment;
                if (TryParseToken(token, out name, out alignment, out format))
                {
                    if (forceValue != null)
                    {
                        sb.Append(forceValue);
                    }
                    else
                    {
                        object value;
                        if (variables != null && variables.TryGetValue(name, out value))
                        {
                            sb.Append(FormatValue(value, alignment, format));
                        }
                        else
                        {
                            if (!unknown.Contains(name)) unknown.Add(name);
                            sb.Append('{').Append(token).Append('}');
                        }
                    }
                    i = close + 1;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString().Replace("\u0001", "{").Replace("\u0002", "}");
        }

        static bool TryParseToken(string token, out string name, out int alignment, out string format)
        {
            name = null; alignment = 0; format = null;

            int comma = token.IndexOf(',');
            int colon = token.IndexOf(':');

            string namePart;
            if (comma >= 0 && (colon < 0 || comma < colon))
            {
                namePart = token.Substring(0, comma);
                int fmtStart = colon >= 0 ? colon : token.Length;
                string alignPart = token.Substring(comma + 1, fmtStart - comma - 1).Trim();
                if (!int.TryParse(alignPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out alignment)) return false;
                if (colon >= 0) format = token.Substring(colon + 1);
            }
            else if (colon >= 0)
            {
                namePart = token.Substring(0, colon);
                format = token.Substring(colon + 1);
            }
            else
            {
                namePart = token;
            }

            namePart = namePart.Trim();
            if (namePart.Length == 0) return false;
            for (int i = 0; i < namePart.Length; i++)
            {
                char c = namePart[i];
                bool ok = char.IsLetterOrDigit(c) || c == '_';
                if (!ok) return false;
            }
            name = namePart;
            return true;
        }

        static string FormatValue(object value, int alignment, string format)
        {
            string text;
            if (value is IFormattable formattable && !string.IsNullOrEmpty(format))
                text = formattable.ToString(format, CultureInfo.InvariantCulture);
            else
                text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

            if (alignment == 0) return text;
            int pad = Math.Abs(alignment);
            if (text.Length >= pad) return text;
            return alignment > 0 ? text.PadLeft(pad) : text.PadRight(pad);
        }
    }
}
