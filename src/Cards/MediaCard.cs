using System.Windows;
using MirrorpondHUD.Core;
using MirrorpondHUD.Media;

namespace MirrorpondHUD.Cards
{
    /// <summary>
    /// 媒体卡：一张卡片显示图片 / 位图 / 动图 / 视频。
    /// 文件、缩放方式、尺寸、透明度、循环、静音都在卡片自己的 Media 设置里，
    /// 所以可以同时放「一张壁纸 + 一个动图 + 一个视频」三张媒体卡，互不影响。
    /// </summary>
    public sealed class MediaCard : RichCardModuleBase
    {
        public override string Kind { get { return "media"; } }
        public override string DisplayName { get { return "媒体"; } }
        public override string Icon { get { return Icons.Image; } }
        public override bool DefaultResident { get { return false; } }
        public override string DefaultHotkey { get { return "F11"; } }
        public override int Order { get { return 5; } }

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
