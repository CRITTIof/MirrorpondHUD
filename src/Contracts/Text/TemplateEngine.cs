using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MirrorpondHUD.Core;

/// <summary>
/// 模板渲染：{name}、{name:0.0}、{name,8}、{name,8:0.0}；
/// 花括号用 {{ }} 转义。未知变量原样保留并记录。
/// </summary>
public static class TemplateEngine
{
    public static string Render(string template, IDictionary<string, object> variables) => Render(template, variables, out var ignored);

    public static string Render(string template, IDictionary<string, object> variables, out List<string> unknown) => Render(template, variables, out unknown, null);

    /// <summary>
    /// 把所有占位符替换成固定值，用于启动动画阶段展示 "---"。
    /// </summary>
    public static string RenderPlaceholder(string template, string placeholder) => Render(template, null, out var ignored, placeholder ?? "---");

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
            if (TryParseToken(token, out var name, out var alignment, out var format))
            {
                if (forceValue is not null)
                {
                    sb.Append(forceValue);
                }
                else
                {
                    if (variables is not null && variables.TryGetValue(name, out var value))
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

    // C-28：NaN/∞ 直接格式化会显示 NaN/∞，看起来像真实监测值；回落到占位符并节流记录。
    static readonly LogThrottle NonFiniteLog = new("模板数值非有限值", 60_000);

    static string FormatValue(object value, int alignment, string format)
    {
        if (value is double d && (double.IsNaN(d) || double.IsInfinity(d)))
        {
            NonFiniteLog.Failure($"模板值={d}");
            return "n/a";
        }
        if (value is float f && (float.IsNaN(f) || float.IsInfinity(f)))
        {
            NonFiniteLog.Failure($"模板值={f}");
            return "n/a";
        }
        if (value is double || value is float) NonFiniteLog.Success();

        string text;
        if (value is IFormattable formattable && !string.IsNullOrEmpty(format))
            text = formattable.ToString(format, CultureInfo.InvariantCulture);
        else
            text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        if (alignment == 0) return text;
        // 对齐宽度来自用户可编辑的模板文本，必须夹取：Math.Abs(int.MinValue) 会抛
        // OverflowException，PadLeft(int.MaxValue) 会尝试申请约 2 GB。
        const int MaxAlignment = 1024;
        int pad = alignment == int.MinValue
            ? MaxAlignment
            : Math.Min(Math.Abs(alignment), MaxAlignment);
        if (text.Length >= pad) return text;
        return alignment > 0 ? text.PadLeft(pad) : text.PadRight(pad);
    }
}
