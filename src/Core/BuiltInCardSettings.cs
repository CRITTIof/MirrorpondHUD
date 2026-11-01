using System;
using System.Collections.Generic;

namespace MirrorpondHUD.Core
{
    public sealed class CommandSettings
    {
        /// <summary>cmd / powershell / pwsh / custom。</summary>
        public string Shell = "cmd";
        /// <summary>自定义命令行模板，{cmd} 会被替换成用户输入；不含 {cmd} 时追加到末尾。</summary>
        public string CustomShell = "cmd.exe /c {cmd}";
        /// <summary>工作目录；空 = 用户主目录。</summary>
        public string WorkingDirectory = "";
        /// <summary>
        /// 旧版「超长单行截断」开关。命令行面板改成会换行的内部小面板之后已经没有设置项了，
        /// 默认关掉（换行比截断更可读）；字段留着只是为了旧配置能读进来。
        /// </summary>
        public bool Truncate = false;
        /// <summary>截断后单行保留的字符数（只在 <see cref="Truncate"/> 为 true 时生效）。</summary>
        public int MaxLineChars = 120;
        /// <summary>单条命令的输出最多保留多少行（兜底上限，防止死循环刷屏把内存吃满）。</summary>
        public int MaxLines = 30;
        public int TimeoutSeconds = 20;

        public CommandSettings Clone()
        {
            return new CommandSettings
            {
                Shell = Shell,
                CustomShell = CustomShell,
                WorkingDirectory = WorkingDirectory,
                Truncate = Truncate,
                MaxLineChars = MaxLineChars,
                MaxLines = MaxLines,
                TimeoutSeconds = TimeoutSeconds
            };
        }

        public void Normalize()
        {
            Shell = Settings.NormalizeShell(Shell);
            CustomShell = string.IsNullOrWhiteSpace(CustomShell) ? "cmd.exe /c {cmd}" : CustomShell.Trim();
            WorkingDirectory = (WorkingDirectory ?? "").Trim();
            MaxLineChars = (int)Settings.Clamp(MaxLineChars, 20, 4000);
            MaxLines = (int)Settings.Clamp(MaxLines, 1, 500);
            TimeoutSeconds = (int)Settings.Clamp(TimeoutSeconds, 1, 300);
        }
    }

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

    /// <summary>
    /// 一张卡片（一个独立透明小窗口）的配置。
    /// 位置不再单独保存：卡片按列表顺序自上而下堆叠，Spacing 是与上一张可见卡片之间的距离。
    public sealed class ChatSettings
    {
        /// <summary>设置页里「已经存了 Key」时输入框里的提示（不回显密钥本身）。</summary>
        public const string KeyPlaceholder = "已填写 API Key，重新输入以更换";

        /// <summary>接口根地址，例如 https://api.openai.com/v1（末尾的 / 会自动去掉）。</summary>
        public string BaseUrl = "https://api.openai.com/v1";

        /// <summary>模型名，显示在卡片第一行。</summary>
        public string Model = "";

        /// <summary>
        /// API Key 的落盘形式：<c>dpapi:&lt;base64&gt;</c>，用 Windows DPAPI（当前用户）加密，
        /// 只有同一台机器、同一个 Windows 用户能解开。空 = 没填（本地服务可以留空）。
        /// 请求时用 <see cref="PlainApiKey"/> 取明文，明文只在内存里。
        /// </summary>
        public string ApiKey = "";

        /// <summary>Single = 每次提问都不带历史；Context = 保留上下文连续对话（轮询）。</summary>
        public string Mode = "Single";

        /// <summary>系统提示词；空 = 不发送。</summary>
        public string SystemPrompt = "";

        /// <summary>请求超时（秒）。</summary>
        public int TimeoutSeconds = 60;

        /// <summary>
        /// 卡片宽度（DIP）。2.4.5 起由「设置 → 外观 → CLI 交互面板」的宽度统一控制，
        /// 这个字段只为旧配置能读进来而保留。
        /// </summary>
        public double Width = 380;

        /// <summary>答案区最多显示多少行。2.4.5 起由 CLI 面板的「内部小面板最大高度」控制。</summary>
        public int AnswerLines = 8;

        public ChatSettings Clone()
        {
            return new ChatSettings
            {
                BaseUrl = BaseUrl, Model = Model, ApiKey = ApiKey, Mode = Mode,
                SystemPrompt = SystemPrompt, TimeoutSeconds = TimeoutSeconds,
                Width = Width, AnswerLines = AnswerLines
            };
        }

        string _plainKeyCache;
        string _plainKeySource;

        /// <summary>
        /// 解出来的明文 Key（只在内存里，不落盘）；空 = 没填，或者解不开（换了 Windows 用户 / 机器）。
        /// 请求头用它，不要直接读 <see cref="ApiKey"/>。
        /// </summary>
        public string PlainApiKey
        {
            get
            {
                if (_plainKeyCache != null && string.Equals(_plainKeySource, ApiKey, StringComparison.Ordinal))
                    return _plainKeyCache;

                string stored = ApiKey ?? "";
                string plain;
                if (stored.Length == 0) plain = "";                                  // 没填
                else if (!SecretStore.IsProtected(stored)) plain = stored;            // 旧配置里的明文
                else if (!SecretStore.TryUnprotect(stored, out plain)) plain = "";    // 解不开

                _plainKeySource = ApiKey;
                _plainKeyCache = plain;
                return plain;
            }
        }

        /// <summary>有可用的 Key（能解开、非空）。</summary>
        public bool HasApiKey
        {
            get { return PlainApiKey.Length > 0; }
        }

        /// <summary>settings.json 里存了 Key，但在这台机器 / 这个用户下解不开。</summary>
        public bool ApiKeyUnreadable
        {
            get
            {
                return !string.IsNullOrEmpty(ApiKey) && SecretStore.IsProtected(ApiKey)
                    && PlainApiKey.Length == 0;
            }
        }

        /// <summary>填 / 换 Key：进内存前先加密，落盘就只有密文；加密失败时保留旧 Key，绝不落明文。</summary>
        public void SetApiKey(string plain)
        {
            plain = (plain ?? "").Trim();
            if (plain.Length == 0)
            {
                ApiKey = "";
                _plainKeySource = null;
                _plainKeyCache = null;
                return;
            }

            string stored;
            if (!SecretStore.TryProtect(plain, out stored))
            {
                // 新 Key 没存成功，但旧 Key 仍然可用：不要连旧 Key 一起清空。
                Log.Error("API Key 加密失败，保留原 Key，新 Key 未保存");
                return;
            }

            ApiKey = stored;
            _plainKeySource = null;
            _plainKeyCache = null;
        }

        /// <summary>忘记 Key（清空）。</summary>
        public void ClearApiKey()
        {
            ApiKey = "";
            _plainKeySource = null;
            _plainKeyCache = null;
        }

        public void Normalize()
        {
            BaseUrl = (BaseUrl ?? "").Trim().TrimEnd('/');
            Model = (Model ?? "").Trim();

            ApiKey = (ApiKey ?? "").Trim();
            // 旧版本是明文存的：读进来就加密，下一次保存（ConfigStore.Load 会立刻回写）就只剩密文。
            if (ApiKey.Length > 0 && !SecretStore.IsProtected(ApiKey))
            {
                Log.Info("检测到明文 API Key，转为 DPAPI 加密存储");
                string stored;
                if (SecretStore.TryProtect(ApiKey, out stored))
                {
                    ApiKey = stored;
                }
                else
                {
                    // 加密失败时清空，绝不让明文进入 settings.json / .bak。
                    Log.Error("明文 API Key 加密失败，已清空");
                    ApiKey = "";
                }
                _plainKeySource = null;
                _plainKeyCache = null;
            }

            Mode = NormalizeMode(Mode);
            SystemPrompt = SystemPrompt ?? "";
            TimeoutSeconds = (int)Settings.Clamp(TimeoutSeconds, 5, 300);
            Width = Settings.Clamp(Width, 220, 1200);
            AnswerLines = (int)Settings.Clamp(AnswerLines, 2, 40);
        }

        /// <summary>对话模式：Single（单次） / Context（轮询，保留上下文）。</summary>
        public static string NormalizeMode(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "context":
                case "continuous":
                case "poll":
                case "polling":
                    return "Context";
                default:
                    return "Single";
            }
        }
    }
}
