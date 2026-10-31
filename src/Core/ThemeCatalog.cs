using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MirrorpondHUD.Core
{
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
        public static List<ThemePreset> Load()
        {
            var list = new List<ThemePreset>();
            Assembly assembly = typeof(Settings).Assembly;

            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(".mptheme", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    using (Stream stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null) continue;

                        string json;
                        using (var reader = new StreamReader(stream))
                            json = reader.ReadToEnd();

                        ThemeDocument document;
                        string error;
                        if (!ThemeFile.TryParse(json, out document, out error))
                        {
                            Log.Warn("内置主题解析失败 " + name + "：" + error);
                            continue;
                        }

                        list.Add(new ThemePreset
                        {
                            Id = name,
                            Name = string.IsNullOrWhiteSpace(document.Name) ? name : document.Name,
                            Description = document.Description ?? "",
                            Document = document
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("内置主题读取失败 " + name + "：" + ex.Message);
                }
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
            return list;
        }
    }
}
