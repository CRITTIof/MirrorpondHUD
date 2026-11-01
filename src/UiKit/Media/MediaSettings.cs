using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls.Media
{
    /// <summary>
    /// 媒体卡 / 媒体控件的显示设置。放在 UiKit，因为 <see cref="CardMediaView"/> 是它的直接消费者；
    /// 卡片插件把它序列化进自己的 <c>Extras</c>（公开字段，能被宿主 Json 读写）。
    /// </summary>
    public sealed class MediaSettings
    {
        /// <summary>文件路径；空 = 卡片显示占位提示。</summary>
        public string Path = "";
        /// <summary>缩放方式：Contain（完整显示）/ Cover（裁剪填满）/ Fill（拉伸）/ None（原始大小）。</summary>
        public string Fit = "Contain";
        /// <summary>卡片内容宽度（DIP）；0 = 用媒体原始尺寸（会被 MaxWidth 夹住）。</summary>
        public double Width = 320;
        /// <summary>卡片内容高度（DIP）；0 = 按媒体长宽比自动。</summary>
        public double Height = 0;
        /// <summary>媒体不透明度 0~1。</summary>
        public double Opacity = 1.0;
        /// <summary>视频 / 动图循环播放。</summary>
        public bool Loop = true;
        /// <summary>视频自动播放。</summary>
        public bool Autoplay = true;
        /// <summary>视频静音（默认静音，避免打扰）。</summary>
        public bool Muted = true;
        /// <summary>视频播放速度倍率 0.25~4。</summary>
        public double Speed = 1.0;
        /// <summary>按外观里的圆角把媒体的四个角裁圆。</summary>
        public bool Rounded = true;

        /// <summary>
        /// 视频 / 图片解码器：Auto = 先试 Windows 自带，失败再换 ffmpeg；
        /// BuiltIn = 只用 Media Foundation；FFmpeg = 只用 ffmpeg（不支持的格式也靠它）。
        /// </summary>
        public string Decoder = "Auto";

        /// <summary>自定义 ffmpeg.exe 路径；空 = 自动在 PATH 与常见安装目录里找。</summary>
        public string DecoderPath = "";

        public MediaSettings Clone()
        {
            return new MediaSettings
            {
                Path = Path, Fit = Fit, Width = Width, Height = Height, Opacity = Opacity,
                Loop = Loop, Autoplay = Autoplay, Muted = Muted, Speed = Speed, Rounded = Rounded,
                Decoder = Decoder, DecoderPath = DecoderPath
            };
        }

        public void Normalize()
        {
            Path = (Path ?? "").Trim().Trim('"');
            Fit = NormalizeFit(Fit);
            Width = Settings.Clamp(Width, 0, 4000);
            Height = Settings.Clamp(Height, 0, 4000);
            Opacity = Settings.Clamp(Opacity, 0, 1);
            Speed = Settings.Clamp(Speed, 0.25, 4);
            Decoder = NormalizeDecoder(Decoder);
            DecoderPath = (DecoderPath ?? "").Trim().Trim('"');
        }

        /// <summary>解码器：Auto / BuiltIn / FFmpeg（也接受 mediafoundation、external 之类写法）。</summary>
        public static string NormalizeDecoder(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "builtin":
                case "built-in":
                case "mediafoundation":
                case "mf":
                case "system":
                    return "BuiltIn";
                case "ffmpeg":
                case "ffmpeg.exe":
                case "external":
                    return "FFmpeg";
                default:
                    return "Auto";
            }
        }

        /// <summary>缩放方式：Contain / Cover / Fill / None（也接受 WPF 的 Stretch 名称）。</summary>
        public static string NormalizeFit(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "cover":
                case "uniformtofill":
                case "crop":
                    return "Cover";
                case "fill":
                case "stretch":
                    return "Fill";
                case "none":
                case "original":
                    return "None";
                default:
                    return "Contain";
            }
        }
    }
}
