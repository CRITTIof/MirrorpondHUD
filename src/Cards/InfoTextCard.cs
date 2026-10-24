using System;
using System.Collections.Generic;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Cards
{
    /// <summary>「信息与文本」的四种预设。</summary>
    public sealed class InfoTextPreset
    {
        public InfoTextPreset(string id, string label, string defaultContent, string icon)
        {
            Id = id;
            Label = label;
            DefaultContent = defaultContent;
            Icon = icon;
        }

        public string Id { get; private set; }
        public string Label { get; private set; }
        public string DefaultContent { get; private set; }
        public string Icon { get; private set; }
    }

    /// <summary>
    /// 信息与文本卡：把原来的系统信息 / 时钟 / 按键 / 文本四种文字卡合并成一种，
    /// 用卡片自己的 `Preset` 选择预设。四种预设都是模板渲染，所以内容、占位符、
    /// 图标倍率、描边等文字能力完全一致。
    /// </summary>
    public sealed class InfoTextCard : CardModuleBase
    {
        static readonly List<InfoTextPreset> PresetList = new List<InfoTextPreset>
        {
            new InfoTextPreset("metrics", "系统信息", Settings.DefaultMetricsContent, Icons.Cpu),
            new InfoTextPreset("clock", "时钟", Settings.DefaultClockContent, Icons.Clock),
            new InfoTextPreset("keys", "按键", Settings.DefaultKeysContent, Icons.TabHotkey),
            new InfoTextPreset("text", "文本信息", Settings.DefaultTextContent, Icons.Txt)
        };

        public static IList<InfoTextPreset> Presets { get { return PresetList; } }

        public override string Kind { get { return "infotext"; } }
        public override string DisplayName { get { return "信息与文本"; } }
        public override string Icon { get { return Icons.Txt; } }
        public override string DefaultContent { get { return Settings.DefaultMetricsContent; } }
        public override int Order { get { return 0; } }

        public override string Render(CardRenderContext context)
        {
            string preset = NormalizePreset(context == null || context.Config == null ? null : context.Config.Preset);
            return context.RenderOrDefault(DefaultFor(preset));
        }

        /// <summary>未知 / 空的预设一律归到系统信息。</summary>
        public static string NormalizePreset(string value)
        {
            string key = (value ?? "").Trim().ToLowerInvariant();
            foreach (InfoTextPreset preset in PresetList)
            {
                if (preset.Id == key) return preset.Id;
            }
            return "metrics";
        }

        /// <summary>
        /// 合并前的四种老卡片迁移成 infotext 后，Preset 缺省会落成默认的 metrics；
        /// 内容恰好是某个预设的默认内容时，把预设纠回那一个（老配置里时钟 / 按键 / 文本卡就能对上）。
        /// </summary>
        public static string InferPreset(string preset, string content)
        {
            string key = NormalizePreset(preset);
            string text = (content ?? "").Trim();
            if (text.Length == 0) return key;

            foreach (InfoTextPreset item in PresetList)
            {
                if (item.Id == key) continue;
                if (string.Equals(item.DefaultContent.Trim(), text, StringComparison.Ordinal))
                    return item.Id;
            }
            return key;
        }

        public static InfoTextPreset Find(string value)
        {
            string key = NormalizePreset(value);
            foreach (InfoTextPreset preset in PresetList)
            {
                if (preset.Id == key) return preset;
            }
            return PresetList[0];
        }

        public static string DefaultFor(string preset)
        {
            return Find(preset).DefaultContent;
        }

        public static string LabelFor(string preset)
        {
            return Find(preset).Label;
        }

        public static string IconFor(string preset)
        {
            return Find(preset).Icon;
        }
    }
}
