using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 设置页「内容」里的一张卡片设置卡。
    /// 直接包住 <see cref="CardSettings"/>，控件双向绑定；展开 / 折叠只影响显示。
    /// 命令行卡片在这里改自己的工具 / 工作目录 / 超时，媒体卡片在这里挑文件与尺寸，
    /// 所以同一个页面可以同时放 cmd、pwsh、两张图片、一个视频，各改各的。
    /// </summary>
    public sealed class CardEditor : INotifyPropertyChanged
    {
        readonly CardSettings _config;
        readonly ICardModule _module;
        bool _expanded;
        bool _spacingEnabled;

        public CardEditor(CardSettings config, ICardModule module, bool expanded)
        {
            _config = config;
            _module = module;
            _expanded = expanded;
            if (_config.Command == null) _config.Command = new CommandSettings();
            if (_config.Media == null) _config.Media = new MediaSettings();
        }

        public CardSettings Config { get { return _config; } }
        public ICardModule Module { get { return _module; } }

        public string Icon { get { return _module.Icon; } }
        public string KindLabel { get { return _module.DisplayName; } }
        public IList<TokenInfo> Tokens { get { return Catalog.Tokens; } }

        public string DisplayName
        {
            get { return string.IsNullOrWhiteSpace(_config.Name) ? _module.DisplayName : _config.Name; }
        }

        public string Summary
        {
            get
            {
                if (_module.IsInteractive)
                    return CommandRunner.ToolLabel(_config.Command) + " · " + FitSummary(_config.Command.WorkingDirectory);

                if (!string.IsNullOrWhiteSpace(_module.Kind) && _module.Kind == "media")
                    return MediaSummary();

                if (_config.Resident) return "常驻";
                return string.IsNullOrWhiteSpace(_config.Hotkey) ? "未设置呼出热键" : "呼出 " + _config.Hotkey;
            }
        }

        static string FitSummary(string directory)
        {
            string dir = (directory ?? "").Trim();
            if (dir.Length == 0) dir = "用户主目录";
            if (dir.Length > 28) dir = "…" + dir.Substring(dir.Length - 27);
            return dir;
        }

        string MediaSummary()
        {
            string path = (_config.Media.Path ?? "").Trim();
            if (path.Length == 0) return "未选择文件";

            string name;
            try { name = System.IO.Path.GetFileName(path); }
            catch { name = path; }

            if (name.Length > 26) name = name.Substring(0, 25) + "…";
            return name;
        }

        public bool IsExpanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Raise("IsExpanded");
                Raise("BodyVisibility");
                Raise("ChevronAngle");
                Raise("HeaderCorner");
            }
        }

        public Visibility BodyVisibility
        {
            get { return _expanded ? Visibility.Visible : Visibility.Collapsed; }
        }

        public double ChevronAngle
        {
            get { return _expanded ? 180 : 0; }
        }

        /// <summary>
        /// 卡片标题栏的圆角：展开时只有上面两个角是圆的（下面接内容），
        /// 折叠时四个角都圆，否则底部会露出直角。
        /// </summary>
        public CornerRadius HeaderCorner
        {
            get
            {
                return _expanded
                    ? new CornerRadius(10, 10, 0, 0)
                    : new CornerRadius(10);
            }
        }

        public string Name
        {
            get { return _config.Name; }
            set
            {
                if (_config.Name == value) return;
                _config.Name = value;
                Raise("Name");
                Raise("DisplayName");
            }
        }

        public bool Resident
        {
            get { return _config.Resident; }
            set
            {
                if (_config.Resident == value) return;
                _config.Resident = value;
                Raise("Resident");
                Raise("HotkeyEnabled");
                Raise("Summary");
            }
        }

        public bool ResidentEnabled { get { return !_module.IsInteractive; } }

        public bool HotkeyEnabled
        {
            get { return !_config.Resident; }
        }

        public string Hotkey
        {
            get { return _config.Hotkey; }
            set
            {
                string text = value ?? "";
                if (_config.Hotkey == text) return;
                _config.Hotkey = text.Trim();
                Raise("Hotkey");
                Raise("Summary");
            }
        }

        public double Spacing
        {
            get { return _config.Spacing; }
            set
            {
                double rounded = Math.Round(value);
                if (Math.Abs(_config.Spacing - rounded) < 0.001) return;
                _config.Spacing = rounded;
                Raise("Spacing");
                Raise("SpacingText");
            }
        }

        public string SpacingText { get { return _config.Spacing.ToString("0"); } }

        public bool SpacingEnabled
        {
            get { return _spacingEnabled; }
            set
            {
                if (_spacingEnabled == value) return;
                _spacingEnabled = value;
                Raise("SpacingEnabled");
            }
        }

        public string Content
        {
            get { return _config.Content; }
            set
            {
                string text = value ?? "";
                if (_config.Content == text) return;
                _config.Content = text;
                Raise("Content");
            }
        }

        public bool SupportsContent { get { return _module.SupportsContent; } }

        public Visibility ContentVisibility
        {
            get { return _module.SupportsContent ? Visibility.Visible : Visibility.Collapsed; }
        }

        // ==================== 命令行卡片 ====================

        public Visibility CommandVisibility
        {
            get { return _module.IsInteractive ? Visibility.Visible : Visibility.Collapsed; }
        }

        /// <summary>0 = cmd，1 = Windows PowerShell，2 = PowerShell 7，3 = 自定义。</summary>
        public int ShellIndex
        {
            get
            {
                switch (Settings.NormalizeShell(_config.Command.Shell))
                {
                    case "powershell": return 1;
                    case "pwsh": return 2;
                    case "custom": return 3;
                    default: return 0;
                }
            }
            set
            {
                string shell = value == 1 ? "powershell" : value == 2 ? "pwsh" : value == 3 ? "custom" : "cmd";
                if (Settings.NormalizeShell(_config.Command.Shell) == shell) return;
                _config.Command.Shell = shell;
                Raise("ShellIndex");
                Raise("CustomShellEnabled");
                Raise("PromptPreview");
                Raise("Summary");
            }
        }

        public bool CustomShellEnabled
        {
            get { return string.Equals(_config.Command.Shell, "custom", StringComparison.OrdinalIgnoreCase); }
        }

        public string CustomShell
        {
            get { return _config.Command.CustomShell; }
            set
            {
                string text = value ?? "";
                if (_config.Command.CustomShell == text) return;
                _config.Command.CustomShell = text;
                Raise("CustomShell");
                Raise("PromptPreview");
                Raise("Summary");
            }
        }

        public string WorkingDirectory
        {
            get { return _config.Command.WorkingDirectory; }
            set
            {
                string text = value ?? "";
                if (_config.Command.WorkingDirectory == text) return;
                _config.Command.WorkingDirectory = text;
                Raise("WorkingDirectory");
                Raise("PromptPreview");
                Raise("Summary");
            }
        }

        public bool Truncate
        {
            get { return _config.Command.Truncate; }
            set
            {
                if (_config.Command.Truncate == value) return;
                _config.Command.Truncate = value;
                Raise("Truncate");
                Raise("MaxLineCharsEnabled");
            }
        }

        public bool MaxLineCharsEnabled { get { return _config.Command.Truncate; } }

        public int MaxLineChars
        {
            get { return _config.Command.MaxLineChars; }
            set
            {
                int v = Math.Max(20, Math.Min(4000, value));
                if (_config.Command.MaxLineChars == v) return;
                _config.Command.MaxLineChars = v;
                Raise("MaxLineChars");
            }
        }

        public int MaxLines
        {
            get { return _config.Command.MaxLines; }
            set
            {
                int v = Math.Max(1, Math.Min(500, value));
                if (_config.Command.MaxLines == v) return;
                _config.Command.MaxLines = v;
                Raise("MaxLines");
            }
        }

        public int TimeoutSeconds
        {
            get { return _config.Command.TimeoutSeconds; }
            set
            {
                int v = Math.Max(1, Math.Min(300, value));
                if (_config.Command.TimeoutSeconds == v) return;
                _config.Command.TimeoutSeconds = v;
                Raise("TimeoutSeconds");
            }
        }

        public string PromptPreview
        {
            get
            {
                try { return CommandRunner.BuildPrompt(_config.Command); }
                catch (Exception ex) { return "(预览失败: " + ex.Message + ")"; }
            }
        }

        // ==================== 媒体卡片 ====================

        public Visibility MediaVisibility
        {
            get { return _module.Kind == "media" ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string MediaPath
        {
            get { return _config.Media.Path; }
            set
            {
                string text = value ?? "";
                if (_config.Media.Path == text) return;
                _config.Media.Path = text;
                Raise("MediaPath");
                Raise("Summary");
            }
        }

        /// <summary>0 = 完整显示，1 = 裁剪填满，2 = 拉伸填满，3 = 原始大小。</summary>
        public int MediaFitIndex
        {
            get
            {
                switch (MediaSettings.NormalizeFit(_config.Media.Fit))
                {
                    case "Cover": return 1;
                    case "Fill": return 2;
                    case "None": return 3;
                    default: return 0;
                }
            }
            set
            {
                string fit = value == 1 ? "Cover" : value == 2 ? "Fill" : value == 3 ? "None" : "Contain";
                if (MediaSettings.NormalizeFit(_config.Media.Fit) == fit) return;
                _config.Media.Fit = fit;
                Raise("MediaFitIndex");
            }
        }

        public double MediaWidth
        {
            get { return _config.Media.Width; }
            set
            {
                double v = Math.Round(Math.Max(0, Math.Min(3000, value)));
                if (Math.Abs(_config.Media.Width - v) < 0.001) return;
                _config.Media.Width = v;
                Raise("MediaWidth");
                Raise("MediaWidthText");
            }
        }

        public string MediaWidthText { get { return _config.Media.Width.ToString("0", CultureInfo.InvariantCulture); } }

        public double MediaHeight
        {
            get { return _config.Media.Height; }
            set
            {
                double v = Math.Round(Math.Max(0, Math.Min(3000, value)));
                if (Math.Abs(_config.Media.Height - v) < 0.001) return;
                _config.Media.Height = v;
                Raise("MediaHeight");
                Raise("MediaHeightText");
            }
        }

        public string MediaHeightText { get { return _config.Media.Height.ToString("0", CultureInfo.InvariantCulture); } }

        public double MediaOpacity
        {
            get { return _config.Media.Opacity; }
            set
            {
                if (Math.Abs(_config.Media.Opacity - value) < 0.001) return;
                _config.Media.Opacity = value;
                Raise("MediaOpacity");
            }
        }

        public bool MediaLoop
        {
            get { return _config.Media.Loop; }
            set
            {
                if (_config.Media.Loop == value) return;
                _config.Media.Loop = value;
                Raise("MediaLoop");
            }
        }

        public bool MediaAutoplay
        {
            get { return _config.Media.Autoplay; }
            set
            {
                if (_config.Media.Autoplay == value) return;
                _config.Media.Autoplay = value;
                Raise("MediaAutoplay");
            }
        }

        public bool MediaMuted
        {
            get { return _config.Media.Muted; }
            set
            {
                if (_config.Media.Muted == value) return;
                _config.Media.Muted = value;
                Raise("MediaMuted");
            }
        }

        public bool MediaRounded
        {
            get { return _config.Media.Rounded; }
            set
            {
                if (_config.Media.Rounded == value) return;
                _config.Media.Rounded = value;
                Raise("MediaRounded");
            }
        }

        public double MediaSpeed
        {
            get { return _config.Media.Speed; }
            set
            {
                double v = Math.Round(value, 2);
                if (Math.Abs(_config.Media.Speed - v) < 0.001) return;
                _config.Media.Speed = v;
                Raise("MediaSpeed");
                Raise("MediaSpeedText");
            }
        }

        public string MediaSpeedText
        {
            get { return _config.Media.Speed.ToString("0.00", CultureInfo.InvariantCulture) + "×"; }
        }

        /// <summary>卡片标题栏的摘要可能需要刷新（改完设置后调用）。</summary>
        public void RefreshSummary()
        {
            Raise("Summary");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
