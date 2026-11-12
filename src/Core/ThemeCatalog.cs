using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MirrorpondHUD.Core;

/// <summary>一套内置主题（来自仓库的 docs/themes/*.mptheme）。</summary>
public sealed class ThemePreset
{
    public string Id;
    public string Name;
    public string Description;
    public ThemeDocument Document;
}

/// <summary>
/// 内置主题目录：`.mptheme` 以 EmbeddedResource 编进程序集，发布版不需要附带 docs 目录。
/// 用户自己的主题走文件导入，不进这里。
/// </summary>
public static class ThemeCatalog
{
    // C-22：内置主题是只读资源，解析一次即可；每次打开设置窗重解析所有 JSON 纯属浪费。
    static List<ThemePreset> _cache;

    public static List<ThemePreset> Load()
    {
        List<ThemePreset> cached = _cache;
        if (cached is not null) return new List<ThemePreset>(cached);

        var list = new List<ThemePreset>();
        // 主题资源编进宿主程序集（不是 Contracts），这里不能用 Settings.Assembly。
        Assembly assembly = typeof(ThemeCatalog).Assembly;

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(".mptheme", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream is null) continue;

                    string json;
                    using (var reader = new StreamReader(stream))
                        json = reader.ReadToEnd();

                    if (!ThemeFile.TryParse(json, out var document, out var error))
                    {
                        Log.Warn($"内置主题解析失败 {name}：{error}");
                        continue;
                    }

                    // Id 用稳定的文件标识（LogicalName=themes/<文件名>），
                    // 显示名可以随翻译/改名变化，不影响主题身份与迁移。
                    string id = Path.GetFileNameWithoutExtension(name);
                    list.Add(new ThemePreset
                    {
                        Id = id,
                        Name = string.IsNullOrWhiteSpace(document.Name) ? id : document.Name,
                        Description = document.Description ?? "",
                        Document = document
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"内置主题读取失败 {name}：{ex.Message}");
            }
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        _cache = list;
        return new List<ThemePreset>(list);
    }
}
