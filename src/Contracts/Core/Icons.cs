using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// 卡片图标码位（Lucide 字体，见 <see cref="LucideIcons"/>）。
    /// 字形高度都是 1em，可以直接和正文、键帽混排；卡片内容里用 {cpu_icon} 之类的占位符引用。
    ///
    /// 2.26.0 起图标统一改用 Lucide：设置页页签、卡片模块图标、媒体播放状态和硬件占位符都走这里。
    /// 键帽仍用 Reicon（U+E000–U+E056），两套字体的 PUA 码位有重叠，因此文本画布不能按整段
    /// 码位区间判断，只能按本类登记过的图标字符分流（见 <see cref="IsIconChar"/>）。
    /// </summary>
    public static class Icons
    {
        // ---------- 软件图标 ----------
        public const string Terminal = LucideIcons.Terminal;
        public const string Web = LucideIcons.Globe;
        public const string App = LucideIcons.AppWindow;
        public const string Game = LucideIcons.Gamepad;
        public const string Folder = LucideIcons.Folder;
        public const string Txt = LucideIcons.FileText;
        public const string Image = LucideIcons.Image;
        public const string Agent = LucideIcons.Bot;

        // ---------- 硬件图标 ----------
        public const string Cpu = LucideIcons.Cpu;
        public const string Gpu = LucideIcons.Gpu;
        public const string Ram = LucideIcons.MemoryStick;
        public const string Rom = LucideIcons.HardDrive;
        public const string NetUp = LucideIcons.Upload;
        public const string NetDown = LucideIcons.Download;
        public const string Usb = LucideIcons.Usb;
        public const string Volume = LucideIcons.Volume2;
        public const string Microphone = LucideIcons.Mic;

        // ---------- 界面图标 ----------
        public const string TabContent = LucideIcons.LayoutGrid;
        public const string TabAppearance = LucideIcons.Palette;
        public const string TabDataSource = LucideIcons.Database;
        public const string TabBehavior = LucideIcons.SlidersHorizontal;
        public const string TabHotkey = LucideIcons.Keyboard;
        public const string Search = LucideIcons.Search;
        public const string Trash = LucideIcons.Trash2;
        public const string Move = LucideIcons.GripVertical;
        public const string Add = LucideIcons.Plus;
        public const string Expand = LucideIcons.ChevronDown;
        public const string Video = LucideIcons.Video;
        public const string Clock = LucideIcons.Clock;
        public const string Press = LucideIcons.Keyboard;
        public const string Play = LucideIcons.Play;
        public const string PauseMedia = LucideIcons.Pause;

        static readonly HashSet<char> IconChars = new HashSet<char>();

        static Icons()
        {
            Register(Terminal);
            Register(Web);
            Register(App);
            Register(Game);
            Register(Folder);
            Register(Txt);
            Register(Image);
            Register(Agent);
            Register(Cpu);
            Register(Gpu);
            Register(Ram);
            Register(Rom);
            Register(NetUp);
            Register(NetDown);
            Register(Usb);
            Register(Volume);
            Register(Microphone);
            Register(TabContent);
            Register(TabAppearance);
            Register(TabDataSource);
            Register(TabBehavior);
            Register(TabHotkey);
            Register(Search);
            Register(Trash);
            Register(Move);
            Register(Add);
            Register(Expand);
            Register(Video);
            Register(Clock);
            Register(Press);
            Register(Play);
            Register(PauseMedia);
        }

        static void Register(string glyph)
        {
            if (glyph != null && glyph.Length == 1) IconChars.Add(glyph[0]);
        }

        /// <summary>
        /// 是否是本类登记过的 Lucide 图标码位。只认已知字符而不是整个 PUA 区间：
        /// Lucide 也有 U+E000–U+E056 的图标，按区间判断会把键帽字形抢走。
        /// </summary>
        public static bool IsIconChar(char c)
        {
            return IconChars.Contains(c);
        }
    }
}
