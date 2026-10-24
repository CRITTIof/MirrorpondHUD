using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 极简 JSON 读写。只服务于配置文件的“对象 / 数组 / 字符串 / 数字 / 布尔 / null”，
    /// 不引入任何第三方依赖，也不依赖 .NET 版本特性。
    /// </summary>
    internal static class Json
    {
        // =============================== 序列化 ===============================

        public static string Serialize(object value)
        {
            var sb = new StringBuilder(2048);
            Write(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value, int depth)
        {
            if (value == null) { sb.Append("null"); return; }

            if (value is string s) { WriteString(sb, s); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is Enum) { WriteString(sb, value.ToString()); return; }

            if (value is int || value is long || value is short || value is byte ||
                value is uint || value is ulong || value is ushort || value is sbyte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is double || value is float || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("0");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary<string, object> map)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('\n'); Indent(sb, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(": ");
                    Write(sb, kv.Value, depth + 1);
                }
                if (!first) { sb.Append('\n'); Indent(sb, depth); }
                sb.Append('}');
                return;
            }

            if (value is IEnumerable seq)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in seq)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('\n'); Indent(sb, depth + 1);
                    Write(sb, item, depth + 1);
                }
                if (!first) { sb.Append('\n'); Indent(sb, depth); }
                sb.Append(']');
                return;
            }

            // 普通对象：反射公共字段
            sb.Append('{');
            var fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            bool any = false;
            foreach (var f in fields)
            {
                any = true;
                sb.Append('\n'); Indent(sb, depth + 1);
                WriteString(sb, f.Name);
                sb.Append(": ");
                Write(sb, f.GetValue(value), depth + 1);
                sb.Append(',');
            }
            if (any)
            {
                sb.Length -= 1; // 去掉最后一个逗号
                sb.Append('\n'); Indent(sb, depth);
            }
            sb.Append('}');
        }

        static void Indent(StringBuilder sb, int depth) { sb.Append(' ', depth * 2); }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // =============================== 反序列化 ===============================

        public static T Deserialize<T>(string json) where T : class, new()
        {
            object node = Parse(json);
            return (T)Bind(typeof(T), node);
        }

        /// <summary>把已经解析好的 JSON 节点绑定成对象（迁移旧配置里的子对象时用）。</summary>
        public static T BindNode<T>(object node) where T : class, new()
        {
            try { return (T)Bind(typeof(T), node); }
            catch (Exception ex)
            {
                Log.Warn("解析配置节点 " + typeof(T).Name + " 失败: " + ex.Message);
                return new T();
            }
        }

        public static object Parse(string text)
        {
            if (text == null) return null;
            int i = 0;
            object v = ParseValue(text, ref i, 0);
            SkipWs(text, ref i);
            if (i < text.Length) throw new FormatException("JSON 尾部存在多余内容（位置 " + i + "）");
            return v;
        }

        static object ParseValue(string s, ref int i, int depth)
        {
            if (depth > 64) throw new FormatException("JSON 嵌套过深");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            switch (s[i])
            {
                case '{': return ParseObject(s, ref i, depth);
                case '[': return ParseArray(s, ref i, depth);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObject(string s, ref int i, int depth)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 对象键必须是字符串（位置 " + i + "）");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少冒号（位置 " + i + "）");
                i++;
                map[key] = ParseValue(s, ref i, depth + 1);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 意外结束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("JSON 对象缺少逗号或右花括号（位置 " + i + "）");
            }
        }

        static List<object> ParseArray(string s, ref int i, int depth)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 意外结束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("JSON 数组缺少逗号或右方括号（位置 " + i + "）");
            }
        }

        static string ParseString(string s, ref int i)
        {
            i++; // 开引号
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON 非法转义: \\" + e);
                }
            }
            throw new FormatException("JSON 字符串未闭合");
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (i == start) throw new FormatException("JSON 无法识别的值（位置 " + start + "）");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Expect(string s, ref int i, string token)
        {
            if (i + token.Length > s.Length || string.CompareOrdinal(s, i, token, 0, token.Length) != 0)
                throw new FormatException("JSON 期望 " + token + "（位置 " + i + "）");
            i += token.Length;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        // =============================== 值绑定 ===============================

        static object Bind(Type target, object node)
        {
            if (node == null) return target.IsValueType ? Activator.CreateInstance(target) : null;

            if (target == typeof(string)) return Convert.ToString(node, CultureInfo.InvariantCulture);

            if (target == typeof(bool))
            {
                if (node is bool b) return b;
                if (node is string s) return string.Equals(s.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                if (node is double d) return d != 0;
                return false;
            }

            if (target.IsEnum)
                return Enum.Parse(target, Convert.ToString(node, CultureInfo.InvariantCulture), true);

            if (target == typeof(int)) return (int)Math.Round(Convert.ToDouble(node, CultureInfo.InvariantCulture));
            if (target == typeof(long)) return (long)Math.Round(Convert.ToDouble(node, CultureInfo.InvariantCulture));
            if (target == typeof(double)) return Convert.ToDouble(node, CultureInfo.InvariantCulture);
            if (target == typeof(float)) return (float)Convert.ToDouble(node, CultureInfo.InvariantCulture);
            if (target == typeof(decimal)) return Convert.ToDecimal(node, CultureInfo.InvariantCulture);

            if (target == typeof(List<string>) && node is List<object> arr)
            {
                var outList = new List<string>(arr.Count);
                foreach (var item in arr) outList.Add(item == null ? "" : Convert.ToString(item, CultureInfo.InvariantCulture));
                return outList;
            }

            // 任意 List<T>：卡片的 List<CardSettings> 等配置数组都走这里。
            if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type itemType = target.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(target);
                if (node is List<object> items)
                {
                    foreach (object item in items) list.Add(Bind(itemType, item));
                }
                return list;
            }

            if (node is Dictionary<string, object> map)
            {
                object inst;
                try { inst = Activator.CreateInstance(target); }
                catch { return null; }

                foreach (var f in target.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!map.TryGetValue(f.Name, out object raw)) continue;
                    try { f.SetValue(inst, Bind(f.FieldType, raw)); }
                    catch (Exception ex) { Log.Warn("配置字段 " + f.Name + " 解析失败: " + ex.Message); }
                }
                return inst;
            }

            return null;
        }
    }
}
