using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MirrorpondHUD.Cards;

/// <summary>一行带时间轴的歌词（秒）。</summary>
public sealed class LyricLine
{
    public double Time;
    public string Text = "";
}

/// <summary>
/// 标准 / 增强 LRC 的保守解析器：只取行级时间轴，逐字标签降级为整行。
/// 兼容一行多时间戳、10ms/1ms、乱序时间戳与 [offset:]（正值 = 歌词提前，按 Lyricify 口径）。
/// </summary>
public static class LrcParser
{
    static readonly Regex TimeTag = new Regex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);
    static readonly Regex OffsetTag = new Regex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<LyricLine> Parse(string text, out string warning)
    {
        warning = null;
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        int offsetMs = 0;
        Match offset = OffsetTag.Match(text);
        if (offset.Success && int.TryParse(offset.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            offsetMs = parsed;

        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            MatchCollection matches = TimeTag.Matches(raw);
            if (matches.Count == 0) continue;

            string lyric = TimeTag.Replace(raw, "").Trim();
            // 去掉增强 LRC 的逐字标签，保留整行文本。
            lyric = Regex.Replace(lyric, @"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", "");
            if (lyric.Length == 0) continue;

            foreach (Match match in matches)
            {
                int min = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                string secondsText = match.Groups[2].Value.Replace(':', '.');
                if (!double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double sec)) continue;
                lines.Add(new LyricLine
                {
                    Time = min * 60.0 + sec - offsetMs / 1000.0,
                    Text = lyric
                });
            }
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (lines.Count == 0) warning = "LRC 中没有可识别的时间轴行";
        return lines;
    }
}
