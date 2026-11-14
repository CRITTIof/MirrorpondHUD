using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>
/// 字符串 XAML 模板的解析缓存（K-29）。
///
/// 控件的模板与样式是拼字符串出来的：颜色只能靠 <see cref="Color.ToString"/> 之类的文本插进
/// XAML。于是每次换肤都要把同一批模板重新解析一遍（换肤卡顿、无编译期校验），而颜色串一旦
/// 不合法，<see cref="System.Windows.Markup.XamlReader.Parse"/> 会抛 XamlParseException ——
/// 调用点在 ApplyStyle 里，异常直接逸出到 UI 线程就是崩窗口。
///
/// 这里把两者一起收口：按「模板种类 + 参数键」缓存解析结果（命中就不再来一遍解析），解析
/// 失败只记一条 ERROR 并返回 null，调用方保持控件原有的默认样式，异常不再外逃。
/// </summary>
internal static class CardTemplates
{
    // 16 处解析点，每处按颜色 / 字号 / 圆角最多几十个变体；超上限整表清空。
    // 换肤只在用户改主题时发生，清空后重建的成本远低于维护一套 LRU 的复杂度。
    const int Capacity = 64;

    static readonly Dictionary<string, object> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 取（并缓存）一个模板 / 样式。<paramref name="key"/> 必须唯一标识全部参与拼接的参数，
    /// 否则换肤后会命中上一套颜色的旧模板。
    /// </summary>
    public static T Get<T>(string key, Func<T> factory, string what) where T : class
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out object hit)) return hit as T;
        }

        T built;
        try
        {
            built = factory();
        }
        catch (Exception ex)
        {
            // 颜色 / 字号来自用户可编辑的设置；解析失败只可能是这套参数不可用。
            // 这里必须兜住：调用点在 ApplyStyle 里，抛出去就是 UI 线程未处理异常（AGENTS §5）。
            Log.Error($"{what} 解析失败，已回退默认样式：{ex.GetType().Name}：{ex.Message}");
            return null;
        }

        if (built is null) return null;

        // 模板是不可变的 Freezable：冻结后才能安全地在多个控件实例之间共享（也是 K-30 的画刷纪律）。
        if (built is Freezable freezable && freezable.CanFreeze) freezable.Freeze();

        lock (Cache)
        {
            if (Cache.Count >= Capacity) Cache.Clear();
            Cache[key] = built;
        }
        return built;
    }

    /// <summary>颜色 → 可直接嵌进 XAML 属性值的 #AARRGGBB 文本。</summary>
    public static string Hex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>画刷 → 缓存键片段；非纯色画刷退回类型名（它本来也不该出现在这类模板里）。</summary>
    public static string KeyOf(Brush brush) =>
        brush is SolidColorBrush solid ? Hex(solid.Color) : brush is null ? "null" : brush.GetType().Name;
}
