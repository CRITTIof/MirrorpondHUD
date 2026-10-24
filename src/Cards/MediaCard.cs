using System.Collections.Generic;
using System.Windows;
using MirrorpondHUD.Core;
using MirrorpondHUD.Media;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 媒体卡：一张卡片显示图片 / 位图 / 动图 / 视频。
    /// 文件、缩放方式、尺寸、透明度、循环、静音都在卡片自己的 Media 设置里，
    /// 所以可以同时放「一张壁纸 + 一个动图 + 一个视频」三张媒体卡，互不影响。
    ///
    /// 它还向「热键」页提供一组媒体卡通用热键（播放 / 暂停、前进、后退、加速、减速），
    /// 对所有媒体卡片同时生效。
    /// </summary>
    public sealed class MediaCard : RichCardModuleBase, ICardHotkeySource, ICardHotkeyTarget
    {
        static readonly List<CardHotkeyAction> Actions = new List<CardHotkeyAction>
        {
            new CardHotkeyAction("media.play-pause", "播放 / 暂停", ""),
            new CardHotkeyAction("media.forward", "前进 5 秒", ""),
            new CardHotkeyAction("media.backward", "后退 5 秒", ""),
            new CardHotkeyAction("media.speed-up", "加速 0.25×", ""),
            new CardHotkeyAction("media.speed-down", "减速 0.25×", "")
        };

        public override string Kind { get { return "media"; } }
        public override string DisplayName { get { return "媒体"; } }
        public override string Icon { get { return Icons.Video; } }
        public override bool DefaultResident { get { return false; } }
        public override string DefaultHotkey { get { return "F11"; } }
        public override int Order { get { return 5; } }

        /// <summary>「热键」页里媒体卡片那一组的动作。</summary>
        public IList<CardHotkeyAction> HotkeyActions { get { return Actions; } }

        public void ExecuteHotkey(string action, FrameworkElement content)
        {
            var view = content as MediaView;
            if (view != null) view.HandleHotkey(action);
        }

        public override FrameworkElement CreateContent(CardRenderContext context)
        {
            double radius = context != null && context.Settings != null
                ? context.Settings.Overlay.CornerRadius
                : 10;

            var view = new MediaView()
                .WithCornerRadius(radius)
                .WithStyle(context == null ? null : context.Settings.Style);

            view.Bind(Media(context));
            return view;
        }

        public override void UpdateContent(FrameworkElement content, CardRenderContext context)
        {
            var view = content as MediaView;
            if (view != null) view.Bind(Media(context));
        }

        public override void OnVisibilityChanged(FrameworkElement content, bool visible)
        {
            var view = content as MediaView;
            if (view == null) return;
            if (visible) view.Play();
            else view.Pause();
        }

        public override void OnZOrderChanged(FrameworkElement content)
        {
            var view = content as MediaView;
            if (view != null) view.RaiseVideo();
        }

        static MediaSettings Media(CardRenderContext context)
        {
            return context != null && context.Config != null && context.Config.Media != null
                ? context.Config.Media
                : new MediaSettings();
        }
    }
}
