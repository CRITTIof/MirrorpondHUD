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

    /// <summary>
    /// 2.45.3：区分模板里"预先写好的文字"与 {占位符} 渲染出来的动态值。
    /// 粒子聚字只该动动态值——"CPU "、"%"、{keys} 前后的字面量都必须保持清晰。
    ///
    /// 纯函数：不格式化任何值，只按 Render 完全同源的规则重新解析模板
    /// （同样的 {{ }} 转义、同样的占位符判定），再把每个字面量片段依次在 rendered
    /// 里定位：字面量覆盖的字符是静态，两个字面量之间的区间（占位符输出，含对齐补齐
    /// 与格式化结果）是动态。相邻占位符之间没有字面量时整段都算动态。
    ///
    /// 任何一处定位不上就返回 false 且不产出掩码：模板与渲染不同源（内容被别的环节
    /// 再替换过、换行被归一、用户内容里恰好含同样的片段）时，猜一个"看起来对"的掩码
    /// 会把动态值判成静态或反之，比不分区更糟。调用方据此退回"只排除图标 / 键帽"。
    /// </summary>
    public static bool TryBuildDynamicMask(string template, string rendered, out bool[] dynamicMask)
    {
        dynamicMask = null;
        if (template is null) return false;
        rendered ??= "";

        // 与 Render 同源的转义保护：{{ }} 先变成私用字符，解析完再还原成花括号，
        // 否则 "{{ok}}" 会被当成占位符。
        string working = template.Replace("{{", "\u0001").Replace("}}", "\u0002");

        // null = 占位符输出；非 null = 模板字面量（已还原花括号）。
        var segments = new List<string>(8);
        var literal = new StringBuilder();
        int i = 0;
        while (i < working.Length)
        {
            char c = working[i];
            if (c != '{')
            {
                literal.Append(c);
                i++;
                continue;
            }

            int close = working.IndexOf('}', i + 1);
            if (close < 0)
            {
                literal.Append(c);
                i++;
                continue;
            }

            string token = working.Substring(i + 1, close - i - 1);
            if (!TryParseToken(token, out _, out _, out _))
            {
                // 与 Render 一致：解析不出占位符时只跳过这个 '{'，后续字符按原样继续扫描。
                literal.Append(c);
                i++;
                continue;
            }

            if (literal.Length > 0)
            {
                segments.Add(UnescapeLiteral(literal.ToString()));
                literal.Clear();
            }
            segments.Add(null);
            i = close + 1;
        }
        if (literal.Length > 0) segments.Add(UnescapeLiteral(literal.ToString()));

        var mask = new bool[rendered.Length];
        int cursor = 0;
        for (int k = 0; k < segments.Count; k++)
        {
            string text = segments[k];
            if (text is null)
            {
                // 占位符输出一直延伸到后面第一个字面量开始处；后面没有字面量就延伸到文本末尾。
                // 相邻占位符无法再细分（值之间没有分隔证据），一起标动态并停在同一右边界。
                int end = rendered.Length;
                for (int n = k + 1; n < segments.Count; n++)
                {
                    if (segments[n] is null) continue;
                    end = rendered.IndexOf(segments[n], cursor, StringComparison.Ordinal);
                    if (end < 0) return false;
                    break;
                }
                for (int x = cursor; x < end; x++) mask[x] = true;
                cursor = end;
                continue;
            }

            // 字面量必须紧接上一次定位结果：中间多出没人认领的字符，
            // 说明模板与渲染不同源（例如渲染前还有别的替换），不能猜。
            if (rendered.IndexOf(text, cursor, StringComparison.Ordinal) != cursor) return false;
            cursor += text.Length;
        }

        // 片段用完了却还有剩余字符：同样不同源。
        if (cursor != rendered.Length) return false;

        dynamicMask = mask;
        return true;
    }

    /// <summary>把字面量里的 {{ / }} 保护字符还原成真正的花括号（与 Render 末尾的替换等价）。</summary>
    static string UnescapeLiteral(string text) =>
        text.Replace('\u0001', '{').Replace('\u0002', '}');

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
