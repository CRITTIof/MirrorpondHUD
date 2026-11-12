using System;
using System.Collections.Generic;
using System.Globalization;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 数值 → 文本的唯一入口（K-34）。
///
/// 设置里的数值格式（ValueFormat）是用户自由输入的字符串，非法格式会让
/// <see cref="double.ToString(string, IFormatProvider)"/> 抛 FormatException。图表侧原本
/// 自己 try/catch 兜住了，表单 / 数据仪表 / 表格列却直接 ToString —— 一个手滑的格式串就能
/// 在 OnRender 或 UI 回调里崩掉窗口。现在统一走这里：非法格式回退默认格式，并只提示一次。
/// </summary>
internal static class CardFormat
{
    /// <summary>格式串为空 / 非法时使用的默认格式。</summary>
    public const string DefaultNumber = "0.##";

    static readonly HashSet<string> WarnedFormats = new(StringComparer.Ordinal);

    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>按设置里的格式显示数值；非法格式退回 <see cref="DefaultNumber"/>，不抛异常。</summary>
    public static string Number(double value, string format)
    {
        // 非有限值没有合理的“显示文本”，空串让调用方自己补占位符。
        if (!IsFinite(value)) return "";

        string text = string.IsNullOrWhiteSpace(format) ? DefaultNumber : format;
        try
        {
            return value.ToString(text, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            // 同一个非法格式在 OnRender 里会被反复调用；只记第一条，避免刷爆日志。
            bool first;
            lock (WarnedFormats) first = WarnedFormats.Add(text);
            if (first) Log.Warn($"数值格式 “{text}” 非法，已回退默认格式 {DefaultNumber}");
            return value.ToString(DefaultNumber, CultureInfo.CurrentCulture);
        }
    }
}
