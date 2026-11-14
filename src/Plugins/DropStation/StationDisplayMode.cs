using System;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 中转站的四种显示方式。集中在这里定义尺寸与归一化，
/// StationView 只负责切换 CardList 的 ItemsPanel，StationRow 只负责把一条数据画成对应尺寸。
/// </summary>
internal static class StationDisplayMode
{
    public const string LargeIcon = "LargeIcon";
    public const string MediumIcon = "MediumIcon";
    public const string SmallIcon = "SmallIcon";
    public const string List = "List";

    public static string Normalize(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "large":
            case "largeicon":
            case "大图标":
                return LargeIcon;
            case "medium":
            case "mediumicon":
            case "中图标":
                return MediumIcon;
            case "small":
            case "smallicon":
            case "小图标":
                return SmallIcon;
            default:
                return List;
        }
    }

    public static bool IsTiles(string value) =>
        string.Equals(Normalize(value), LargeIcon, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Normalize(value), MediumIcon, StringComparison.OrdinalIgnoreCase);

    public static double IconSize(string value)
    {
        switch (Normalize(value))
        {
            case LargeIcon: return 64;
            case MediumIcon: return 44;
            case SmallIcon: return 24;
            default: return 16;
        }
    }

    public static double TileWidth(string value) =>
        string.Equals(Normalize(value), LargeIcon, StringComparison.OrdinalIgnoreCase) ? 112 : 124;

    public static double TileHeight(string value) =>
        string.Equals(Normalize(value), LargeIcon, StringComparison.OrdinalIgnoreCase) ? 118 : 96;

    public static string Label(string value)
    {
        switch (Normalize(value))
        {
            case LargeIcon: return "大图标";
            case MediumIcon: return "中图标";
            case SmallIcon: return "小图标";
            default: return "列表";
        }
    }
}
