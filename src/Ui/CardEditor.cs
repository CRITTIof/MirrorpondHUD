using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 设置页「内容」里的一张卡片设置卡。
    /// 直接包住 <see cref="CardSettings"/>，控件双向绑定；展开 / 折叠只影响显示。
    /// 通用字段（名称 / 常驻 / 热键 / 间距 / 内容）由宿主画；卡片自己的设置
    /// （命令行工具、以后的画板 / 中转站参数）由模块通过 <see cref="ICardEditorSection"/> 提供，
    /// 宿主只用一个 ContentControl 摆放，所以加一种带设置的卡片不用再改这个类和 SettingsWindow.xaml。
    /// </summary>
    public sealed class CardEditor : INotifyPropertyChanged
    {
        readonly CardSettings _config;
        readonly ICardModule _module;
        readonly Settings _settings;
        bool _expanded;
        bool _spacingEnabled;
        FrameworkElement _section;
        bool _sectionCreated;

        public CardEditor(CardSettings config, ICardModule module, Settings settings, bool expanded)
        {
            _config = config;
            _module = module;
            _settings = settings ?? new Settings();
            _expanded = expanded;
        }

        public CardSettings Config { get { return _config; } }
        public ICardModule Module { get { return _module; } }

        /// <summary>
        /// 模块自己的设置区块（命令行卡的工具 / 目录 / 超时）。由模块自己创建，
        /// 宿主只负责摆放；null = 这种卡片没有额外设置。
        /// </summary>
        public FrameworkElement SettingsSection
        {
            get
            {
                if (_sectionCreated) return _section;
                _sectionCreated = true;

                var source = _module as ICardEditorSection;
                if (source == null) return null;

                try
                {
                    _section = source.CreateEditor(_config, _settings, RefreshSummary);
                }
                catch (Exception ex)
                {
                    Log.Warn("卡片设置区块创建失败(" + _config.Name + "): " + ex.Message);
                    _section = null;
                }

                return _section;
            }
        }

        public Visibility SettingsSectionVisibility
        {
            get { return SettingsSection == null ? Visibility.Collapsed : Visibility.Visible; }
        }

        /// <summary>
        /// 把模块区块里还没提交的草稿落盘（目前只有 API Key 输入框：用户打完字直接点「确定」时，
        /// 文本框还没失焦，草稿只在控件里）。保存前由设置窗口统一调用。
        /// </summary>
        public void CommitPendingEdits()
        {
            if (!_sectionCreated || _section == null) return;

            foreach (ChatCardSettingsView chat in FindChildren<ChatCardSettingsView>(_section))
                chat.CommitChatApiKey();
        }

        static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                var match = child as T;
                if (match != null) yield return match;

                foreach (T nested in FindChildren<T>(child)) yield return nested;
            }
        }

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
                // 摘要由模块自己说（命令行卡显示工具 + 目录、媒体卡显示文件名、对话卡显示模型名）；
                // 没实现的模块用宿主的通用文案。
                var summary = _module as ICardSummary;
                if (summary != null)
                {
                    try { return summary.Summary(_config); }
                    catch (Exception ex) { Log.Warn("卡片摘要失败(" + _config.Name + "): " + ex.Message); }
                }

                if (_config.Resident) return "常驻";
                return string.IsNullOrWhiteSpace(_config.Hotkey) ? "未设置呼出热键" : "呼出 " + _config.Hotkey;
            }
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

        // 命令行卡片的设置（工具 / 自定义命令 / 工作目录 / 返回内容 / 提示符预览）
        // 已搬到模块自己身上：见 src/Cards/CommandCardEditor.cs 与 ICardEditorSection。

        // 媒体卡与 AI 对话卡的设置区块已搬到模块自己身上：
        // 见 src/Cards/MediaCardSettingsView.xaml(.cs) 与 ChatCardSettingsView.xaml(.cs)。

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
