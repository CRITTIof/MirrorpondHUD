using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;
using MirrorpondHUD.Media;

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
            if (_config.Chat == null) _config.Chat = new ChatSettings();
        }

        public CardSettings Config { get { return _config; } }
        public ICardModule Module { get { return _module; } }

        public string Icon
        {
            get
            {
                return IsInfoText ? InfoTextCard.IconFor(_config.Preset) : _module.Icon;
            }
        }

        public string KindLabel
        {
            get
            {
                return IsInfoText
                    ? _module.DisplayName + " · " + InfoTextCard.LabelFor(_config.Preset)
                    : _module.DisplayName;
            }
        }

        bool IsInfoText
        {
            get { return _module != null && _module.Kind == "infotext"; }
        }

        public Visibility PresetVisibility
        {
            get { return IsInfoText ? Visibility.Visible : Visibility.Collapsed; }
        }

        public int PresetIndex
        {
            get
            {
                IList<InfoTextPreset> presets = InfoTextCard.Presets;
                string current = InfoTextCard.NormalizePreset(_config.Preset);
                for (int i = 0; i < presets.Count; i++)
                {
                    if (presets[i].Id == current) return i;
                }
                return 0;
            }
            set
            {
                IList<InfoTextPreset> presets = InfoTextCard.Presets;
                if (value < 0 || value >= presets.Count) return;

                string next = presets[value].Id;
                if (InfoTextCard.NormalizePreset(_config.Preset) == next) return;

                _config.Preset = next;
                _config.Content = "";   // 换预设回到该预设的默认内容，避免串模板

                Raise("PresetIndex");
                Raise("KindLabel");
                Raise("Icon");
                Raise("Content");
                Raise("ContentVisibility");
                Raise("Summary");
            }
        }
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

        public Visibility ContentVisibility
        {
            get
            {
                if (!_module.SupportsContent) return Visibility.Collapsed;
                if (IsInfoText && InfoTextCard.NormalizePreset(_config.Preset) == "keys") return Visibility.Collapsed;
                return Visibility.Visible;
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

        /// <summary>0 = 自动，1 = 只用系统 Media Foundation，2 = 只用 ffmpeg。</summary>
        public int MediaDecoderIndex
        {
            get
            {
                switch (MediaSettings.NormalizeDecoder(_config.Media.Decoder))
                {
                    case "BuiltIn": return 1;
                    case "FFmpeg": return 2;
                    default: return 0;
                }
            }
            set
            {
                string decoder = value == 1 ? "BuiltIn" : value == 2 ? "FFmpeg" : "Auto";
                if (MediaSettings.NormalizeDecoder(_config.Media.Decoder) == decoder) return;
                _config.Media.Decoder = decoder;
                Raise("MediaDecoderIndex");
                Raise("DecoderHint");
            }
        }

        /// <summary>自定义 ffmpeg.exe；空 = 自动在 PATH 与常见目录里找。</summary>
        public string MediaDecoderPath
        {
            get { return _config.Media.DecoderPath; }
            set
            {
                string text = (value ?? "").Trim();
                if (string.Equals(_config.Media.DecoderPath ?? "", text, StringComparison.OrdinalIgnoreCase)) return;
                _config.Media.DecoderPath = text;
                Raise("MediaDecoderPath");
                Raise("DecoderHint");
            }
        }

        /// <summary>探测结果：找到哪些解码器、ffmpeg 在哪。</summary>
        public string DecoderHint
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_config.Media.DecoderPath))
                {
                    return System.IO.File.Exists(_config.Media.DecoderPath)
                        ? "使用自定义解码器：" + _config.Media.DecoderPath
                        : "自定义解码器不存在：" + _config.Media.DecoderPath;
                }

                List<DecoderInfo> found = Decoders.Detect();
                if (found.Count == 0)
                    return "没找到 ffmpeg / mpv / VLC；视频只能用 Windows 自带的 Media Foundation";

                DecoderInfo ffmpeg = null;
                var others = new System.Text.StringBuilder();
                foreach (DecoderInfo info in found)
                {
                    if (info.Frames && ffmpeg == null) { ffmpeg = info; continue; }
                    if (others.Length > 0) others.Append("、");
                    others.Append(info.Kind);
                }

                if (ffmpeg == null)
                    return "找到 " + others + "，但没有 ffmpeg；视频仍走系统解码（可在上面指定 ffmpeg.exe）";

                string label = string.IsNullOrWhiteSpace(ffmpeg.Version)
                    ? "FFmpeg"
                    : "FFmpeg " + ffmpeg.Version;
                string text = "已找到 " + label + "（" + ffmpeg.Path + "）";
                if (others.Length > 0) text += "；另有 " + others;
                return text;
            }
        }

        /// <summary>重新扫一遍 PATH（设置页的「检测」按钮）。</summary>
        public void RefreshDecoders()
        {
            List<DecoderInfo> found = Decoders.Detect(true);
            Log.Info("手动重新检测解码器：" + Decoders.Describe(found));
            Raise("DecoderHint");
        }

        // ==================== AI 对话卡片 ====================

        public Visibility ChatVisibility
        {
            get { return _module.Kind == "chat" ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string ChatBaseUrl
        {
            get { return _config.Chat.BaseUrl; }
            set
            {
                string text = (value ?? "").Trim();
                if (_config.Chat.BaseUrl == text) return;
                _config.Chat.BaseUrl = text;
                Raise("ChatBaseUrl");
                Raise("ChatHint");
                Raise("Summary");
            }
        }

        public string ChatModel
        {
            get { return _config.Chat.Model; }
            set
            {
                string text = (value ?? "").Trim();
                if (_config.Chat.Model == text) return;
                _config.Chat.Model = text;
                Raise("ChatModel");
                Raise("ChatHint");
                Raise("Summary");
            }
        }

        public string ChatApiKey
        {
            get { return _config.Chat.ApiKey; }
            set
            {
                string text = (value ?? "").Trim();
                if (_config.Chat.ApiKey == text) return;
                _config.Chat.ApiKey = text;
                Raise("ChatApiKey");
                Raise("Summary");
            }
        }

        /// <summary>0 = 单次（每次独立），1 = 轮询（保留上下文）。</summary>
        public int ChatModeIndex
        {
            get { return ChatSettings.NormalizeMode(_config.Chat.Mode) == "Context" ? 1 : 0; }
            set
            {
                string mode = value == 1 ? "Context" : "Single";
                if (ChatSettings.NormalizeMode(_config.Chat.Mode) == mode) return;
                _config.Chat.Mode = mode;
                Raise("ChatModeIndex");
                Raise("ChatHint");
                Raise("Summary");
            }
        }

        public string ChatSystemPrompt
        {
            get { return _config.Chat.SystemPrompt; }
            set
            {
                string text = value ?? "";
                if (_config.Chat.SystemPrompt == text) return;
                _config.Chat.SystemPrompt = text;
                Raise("ChatSystemPrompt");
            }
        }

        public int ChatTimeoutSeconds
        {
            get { return _config.Chat.TimeoutSeconds; }
            set
            {
                int v = (int)Math.Max(5, Math.Min(300, value));
                if (_config.Chat.TimeoutSeconds == v) return;
                _config.Chat.TimeoutSeconds = v;
                Raise("ChatTimeoutSeconds");
            }
        }

        public double ChatWidth
        {
            get { return _config.Chat.Width; }
            set
            {
                double v = Math.Round(Math.Max(220, Math.Min(1200, value)));
                if (Math.Abs(_config.Chat.Width - v) < 0.001) return;
                _config.Chat.Width = v;
                Raise("ChatWidth");
            }
        }

        /// <summary>设置页里的一行提示：还缺什么。</summary>
        public string ChatHint
        {
            get
            {
                if (_config.Chat.BaseUrl.Length == 0) return "接口地址为空：例如 https://api.openai.com/v1（本地服务填到 /v1）";
                if (_config.Chat.Model.Length == 0) return "模型名为空：卡片第一行就显示它，例如 gpt-4o-mini / deepseek-chat";
                if (!_config.Chat.BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return "接口地址要以 http:// 或 https:// 开头";

                return _config.Chat.ApiKey.Length == 0
                    ? "没填 Key：本地服务（LM Studio / Ollama 兼容层）可以留空；云端接口需要填"
                    : "请求会发到 " + _config.Chat.BaseUrl + "/chat/completions，Key 明文存在 settings.json 里";
            }
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
