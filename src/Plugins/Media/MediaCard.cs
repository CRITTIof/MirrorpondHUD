using System;
using System.Collections.Generic;
using System.Windows;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Controls;
using MirrorpondHUD.Ui.Controls.Media;

namespace MirrorpondHUD.Cards;

/// <summary>
/// 媒体卡：一张卡片显示图片 / 位图 / 动图 / 视频。
/// 文件、缩放方式、尺寸、透明度、循环、静音都在卡片自己的 Media 设置里，
/// 所以可以同时放「一张壁纸 + 一个动图 + 一个视频」三张媒体卡，互不影响。
///
/// 它还向「热键」页提供一组媒体卡通用热键（播放 / 暂停、前进、后退、加速、减速），
/// 对所有媒体卡片同时生效。
/// </summary>
public sealed class MediaCard : RichCardModuleBase, ICardHotkeySource, ICardHotkeyTarget,
                                ICardSettingsModule, ICardEditorSection, ICardSettingsValidator,
                                ICardSummary, ICardStyleAware, ICardScreenshotProvider, ICardSelfTest,
                                ICardDefaultProvider
{
    /// <summary>媒体设置存在 <see cref="CardSettings.Extras"/> 里的键名。</summary>
    public const string SettingsKey = "media";

    static readonly List<CardHotkeyAction> Actions = new()
    {
        new CardHotkeyAction("media.play-pause", "播放 / 暂停", ""),
        new CardHotkeyAction("media.forward", "前进 5 秒", ""),
        new CardHotkeyAction("media.backward", "后退 5 秒", ""),
        new CardHotkeyAction("media.speed-up", "加速 0.25×", ""),
        new CardHotkeyAction("media.speed-down", "减速 0.25×", "")
    };

    public override string Kind => "media";
    public override string DisplayName => "媒体";
    public override string Icon => Icons.Video;
    public override bool DefaultResident => false;
    public override string DefaultHotkey => "F11";
    public override int Order => 5;

    /// <summary>默认配置里媒体卡排在最后。</summary>
    public int DefaultOrder => 20;

    /// <summary>全新配置自带一张按 F11 呼出的媒体卡（M-1：由插件自己声明）。</summary>
    public IList<CardSettings> CreateDefaults() => new List<CardSettings>
        {
            new() {
                Kind = Kind,
                Name = DisplayName,
                Resident = false,
                Spacing = 12,
                Hotkey = DefaultHotkey,
                Content = ""
            }
        };

    /// <summary>「热键」页里媒体卡片那一组的动作。</summary>
    public IList<CardHotkeyAction> HotkeyActions => Actions;

    public void ExecuteHotkey(string action, FrameworkElement content)
    {
        var view = content as CardMediaView;
        if (view is not null) view.HandleHotkey(action);
    }

    public override FrameworkElement CreateContent(CardRenderContext context)
    {
        double radius = context is not null && context.Settings is not null
            ? context.Settings.Overlay.CornerRadius
            : 10;

        var view = new CardMediaView()
            .WithCornerRadius(radius)
            .WithStyle(context is null ? null : context.Settings.Style);

        view.Bind(Media(context));
        view.SetMeta(CardMeta.Header(context is null ? null : context.Settings,
                                     context is null ? null : context.Config),
                     CardMeta.Footer(context is null ? null : context.Settings,
                                     context is null ? null : context.Config));
        return view;
    }

    public override void UpdateContent(FrameworkElement content, CardRenderContext context)
    {
        var view = content as CardMediaView;
        if (view is null) return;
        view.Bind(Media(context));
        view.SetMeta(CardMeta.Header(context is null ? null : context.Settings,
                                     context is null ? null : context.Config),
                     CardMeta.Footer(context is null ? null : context.Settings,
                                     context is null ? null : context.Config));
    }

    public override void OnVisibilityChanged(FrameworkElement content, bool visible)
    {
        var view = content as CardMediaView;
        if (view is null) return;
        if (visible) view.Play();
        else view.Pause();
    }

    public override void OnZOrderChanged(FrameworkElement content)
    {
        var view = content as CardMediaView;
        if (view is not null) view.RaiseVideo();
    }

    static MediaSettings Media(CardRenderContext context) => MediaOf(context is null ? null : context.Config);

    // ==================== 模块自己的设置 ====================

    /// <summary>这张卡片自己的媒体设置；缺省时回落到默认值并写回配置。</summary>
    public static MediaSettings MediaOf(CardSettings config)
    {
        if (config is null) return new MediaSettings();

        MediaSettings media = config.ExtrasAs<MediaSettings>(SettingsKey);
        media.Normalize();
        config.SetExtras(SettingsKey, media);
        return media;
    }

    /// <inheritdoc />
    public void InitializeCard(CardSettings config)
    {
        if (config is null) return;
        MediaOf(config);
    }

    /// <inheritdoc />
    public FrameworkElement CreateEditor(CardSettings config, Settings settings, Action onChanged) => new MediaCardSettingsView(config, onChanged);

    /// <summary>主题 / 字号 / 圆角变了：就地换肤，视频不重启、动图不回第一帧。</summary>
    public void ApplyStyle(FrameworkElement content, CardRenderContext context)
    {
        var view = content as CardMediaView;
        if (view is null || context is null) return;

        view.ApplyStyle(context.Settings is null ? null : context.Settings.Style,
                        context.Settings is null ? 10 : context.Settings.Overlay.CornerRadius);
        // 只改页脚模板 / M6 开关时宿主不会重建内容，换肤后必须重算铭牌。
        CardSettings card = context.Config ?? (context.Host is null ? null : context.Host.Config);
        view.SetMeta(CardMeta.Header(context.Settings, card), CardMeta.Footer(context.Settings, card));
    }

    /// <inheritdoc />
    public string Validate(CardSettings config)
    {
        string path = MediaOf(config).Path;
        if (!string.IsNullOrWhiteSpace(path) && !System.IO.File.Exists(path))
            return $"的媒体文件不存在：{path}";
        return null;
    }

    /// <inheritdoc />
    public string Summary(CardSettings config)
    {
        string path = (MediaOf(config).Path ?? "").Trim();
        if (path.Length == 0) return "未选择文件";

        string name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;
        return name.Length > 28 ? $"…{name.Substring(name.Length - 27)}" : name;
    }

    /// <summary>文档截图：视频没有真实子窗口，先让媒体控件画出 ffmpeg 解出的第一帧。</summary>
    public void PrepareScreenshot(FrameworkElement content)
    {
        var view = content as CardMediaView;
        if (view is null) return;
        string path = view.MediaPath;
        if (!string.IsNullOrWhiteSpace(path) && CardMediaView.IsVideoPath(path))
            view.ShowPoster(path);
    }

    public string RunSelfTest()
    {
        var media = new MediaSettings();
        media.Path = "  \"a b.mp4\"  ";
        media.Fit = "cover";
        media.Decoder = "mediafoundation";
        media.Normalize();
        if (media.Path != "a b.mp4") return "媒体路径没有去掉引号 / 空白";
        if (media.Fit != "Cover" || media.Decoder != "BuiltIn") return "媒体设置归一化失败";
        if (!CardMediaView.IsVideoPath("x.MKV")) return "扩展名判断失败";
        return null;
    }
}
