using System;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// ReiconFont 图标码位（U+E100 起软件图标，U+E200 起硬件图标，U+E300 起界面图标）。
    /// 字形高度都是 1em，可以直接和正文、键帽混排；卡片内容里用 {cpu_icon} 之类的占位符引用。
    /// </summary>
    public static class Icons
    {
        // ---------- 软件图标 U+E100–U+E107 ----------
        public const string Terminal = "\uE100";
        public const string Web = "\uE101";
        public const string App = "\uE102";
        public const string Game = "\uE103";
        public const string Folder = "\uE104";
        public const string Txt = "\uE105";
        public const string Image = "\uE106";
        public const string Agent = "\uE107";

        // ---------- 硬件图标 U+E200–U+E208 ----------
        public const string Cpu = "\uE200";
        public const string Gpu = "\uE201";
        public const string Ram = "\uE202";
        public const string Rom = "\uE203";
        public const string NetUp = "\uE204";
        public const string NetDown = "\uE205";
        public const string Usb = "\uE206";
        public const string Volume = "\uE207";
        public const string Microphone = "\uE208";

        // ---------- 界面图标 U+E300–U+E309 ----------
        public const string TabContent = "\uE300";
        public const string TabAppearance = "\uE301";
        public const string TabDataSource = "\uE302";
        public const string TabBehavior = "\uE303";
        public const string TabHotkey = "\uE304";
        public const string Search = "\uE305";
        public const string Trash = "\uE306";
        public const string Move = "\uE307";
        public const string Add = "\uE308";
        public const string Expand = "\uE309";
    }
}
