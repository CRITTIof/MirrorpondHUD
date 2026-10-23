using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 设置页「内容」里的一张卡片设置卡。
    /// 直接包住 <see cref="CardSettings"/>，控件双向绑定；展开 / 折叠只影响显示。
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
                double rounded = System.Math.Round(value);
                if (System.Math.Abs(_config.Spacing - rounded) < 0.001) return;
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

        public Visibility CommandHintVisibility
        {
            get { return _module.IsInteractive ? Visibility.Visible : Visibility.Collapsed; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
