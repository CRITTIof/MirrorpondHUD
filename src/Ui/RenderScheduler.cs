using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using MirrorpondHUD.Cards;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>渲染调度需要宿主提供的状态与动作。</summary>
    internal interface IRenderHost
    {
        Settings Settings { get; }
        IList<CardSlot> Cards { get; }
        KeyHistory Keys { get; }
        bool OverlayVisible { get; }
        bool IntroRunning { get; }

        bool IsIntroCard(CardSlot slot);
        void IntroLayoutChanged();
        void LayoutIfNeeded();
        void ReleaseContent(CardSlot slot);
        void ShowCard(CardSlot slot, string text);
    }

    /// <summary>
    /// 渲染调度：120ms 渲染定时器、采样快照、每帧共享变量表、卡片文字 / 占位渲染，
    /// 以及「内容 / 样式签名变了要不要重建」的刷新调度。
    ///
    /// 从 <see cref="OverlayController"/> 抽出来（2026-11 审阅第三批第 12 项）：
    /// 控制器只保留“哪个卡片该显示 / 隐藏”，这里负责“显示什么、多久重画一次”。
    /// 变量表与采样快照只有这一份真相，别的组件只读，不再各存一份。
    /// </summary>
    internal sealed class RenderScheduler : IDisposable
    {
        const int RenderIntervalMs = 120;

        // 渲染失败日志节流（审阅 §7.2 第 6 条）：渲染循环 120ms 一轮，失败不节流约 8 条/秒，
        // 叠加 1MB 日志上限会很快挤掉历史。按卡片 id 各维护一个节流器：
        // 首次失败 Warn、恢复记一条 Info、持续失败每 30s 最多一条。
        const double RenderFailureIntervalMs = 30000;

        readonly IRenderHost _host;
        readonly Dispatcher _dispatcher;
        readonly Dictionary<string, LogThrottle> _renderThrottles =
            new Dictionary<string, LogThrottle>(StringComparer.OrdinalIgnoreCase);

        DispatcherTimer _timer;
        MetricsSnapshot _last = new MetricsSnapshot();
        bool _hasSnapshot;

        public RenderScheduler(IRenderHost host, Dispatcher dispatcher)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        /// <summary>首帧采样已经回来（标题为 false 时卡片显示占位符而不是假数据）。</summary>
        public bool HasSnapshot { get { return _hasSnapshot; } }

        /// <summary>当前采样快照；创建富内容时作为初始上下文。</summary>
        public MetricsSnapshot LastSnapshot { get { return _last; } }

        /// <summary>每次 Start 都把上一轮快照丢掉：新会话首帧仍先显示占位符。</summary>
        public void ResetSnapshot()
        {
            _last = new MetricsSnapshot();
            _hasSnapshot = false;
        }

        public void Start()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(RenderIntervalMs)
                };
                _timer.Tick += (s, e) => RenderNow();
            }

            _timer.Start();
        }

        public void Stop()
        {
            try { if (_timer != null) _timer.Stop(); } catch { }
        }

        public void Dispose()
        {
            Stop();
        }

        public void RenderNow()
        {
            if (!_host.OverlayVisible) return;

            IDictionary<string, object> shared = null;
            DateTime now = DateTime.UtcNow;

            // 交互卡（命令行）自己管刷新，靠下面循环里的 IsInteractive 跳过；
            // 这里不能再按命令行状态整块返回，否则命令行一开，其它常驻卡片的文字就停更了。
            foreach (CardSlot slot in _host.Cards)
            {
                if (!slot.Shown || slot.Module.IsInteractive) continue;
                if (slot.Rich != null) continue;   // 富内容（图片 / 视频）不需要逐帧刷新文字
                if (_host.IsIntroCard(slot) && _host.IntroRunning) continue;

                // 模块可以声明「最快多久重画一次」：时钟不必按 8 帧/秒重算模板。
                int interval = 0;
                try { interval = slot.Module.RefreshIntervalMs(slot.Config); }
                catch { interval = 0; }

                if (_hasSnapshot && interval > 0 &&
                    (now - slot.LastRenderUtc).TotalMilliseconds < interval)
                    continue;

                if (shared == null) shared = BuildFrameVariables();

                // 首帧采样还没回来时显示占位符，不要把 "0.0%" 这种假数据显示给用户。
                slot.Window.SetText(_hasSnapshot ? RenderCardText(slot, shared) : RenderPlaceholderText(slot), false);
                slot.LastRenderUtc = now;
            }

            _host.LayoutIfNeeded();
        }

        public void OnSnapshot(MetricsSnapshot snapshot)
        {
            _last = snapshot;
            _hasSnapshot = !snapshot.Warmup;

            if (_host.IntroRunning)
            {
                // 动画期间不刷新内容，但可以按真实数据把整块尺寸先撑好，
                // 这样动画结束时不会突然变宽、文字位置也不会跳。
                _dispatcher.BeginInvoke(new Action(_host.IntroLayoutChanged), DispatcherPriority.Background);
                return;
            }

            _dispatcher.BeginInvoke(new Action(RenderNow), DispatcherPriority.Background);
        }

        /// <summary>
        /// 一帧只建一次变量表：同一帧里所有文字卡片共用，时间 / 采样值完全一致。
        /// 用只读包装，避免某个模块顺手改写变量串到别的卡片。
        /// </summary>
        IDictionary<string, object> BuildFrameVariables()
        {
            try
            {
                return new ReadOnlyDictionary<string, object>(Variables.Build(_last, _host.Settings, _host.Keys));
            }
            catch (Exception ex)
            {
                Log.Warn("变量表构建失败: " + ex.Message);
                return null;
            }
        }

        public string RenderCardText(CardSlot slot)
        {
            return RenderCardText(slot, null);
        }

        /// <summary>用一帧里已经建好的变量表渲染（<paramref name="sharedVariables"/> 为 null 时自己建一份）。</summary>
        public string RenderCardText(CardSlot slot, IDictionary<string, object> sharedVariables)
        {
            if (slot == null || slot.Module.IsInteractive) return "";

            try
            {
                string text = slot.Module.Render(
                    new CardRenderContext(_host.Settings, _last, _host.Keys, slot.Config, sharedVariables, slot.Host));

                // 恢复打点：只有真的从失败状态恢复才会记一条 Info，正常渲染没有额外输出。
                LogThrottle throttle;
                if (_renderThrottles.TryGetValue(ThrottleKey(slot), out throttle)) throttle.Success();

                return text;
            }
            catch (Exception ex)
            {
                GetRenderThrottle(slot).Failure(ex.Message);
                return "(渲染失败)";
            }
        }

        /// <summary>首帧采样还没回来：所有占位符换成 ---，避免显示一堆假的 0。</summary>
        public string RenderPlaceholderText(CardSlot slot)
        {
            if (slot == null || slot.Module.IsInteractive) return "";

            try
            {
                return TemplateEngine.RenderPlaceholder(CardTemplate(slot), "---");
            }
            catch
            {
                return "";
            }
        }

        public string CardTemplate(CardSlot slot)
        {
            string content = slot.Config == null ? null : slot.Config.Content;
            return string.IsNullOrWhiteSpace(content) ? slot.Module.DefaultContent : content;
        }

        /// <summary>
        /// 内容维度：种类、模板、本卡自己的设置（Extras）。变了才重建控件。
        /// 序列化成字符串快照存进 <see cref="CardSlot.ContentSignature"/>——设置页在副本上改、
        /// 点「应用」时才换成新对象，旧值只能靠快照记住。
        /// </summary>
        sealed class ContentInputs
        {
            public string Kind;
            public string Content;
            public Dictionary<string, object> Extras;
        }

        /// <summary>
        /// 样式维度：所有卡片共用的外观、CLI 尺寸、圆角与投影。
        /// 只有样式变化时走 <see cref="ICardStyleAware.ApplyStyle"/>（支持就地换肤就不重建），
        /// 不支持的模块仍回退到重建。
        /// </summary>
        sealed class StyleInputs
        {
            public StyleSettings Style;
            public CliSettings Cli;
            public double CornerRadius;
            public bool Shadow;
        }

        public string ContentSignature(CardSettings config)
        {
            if (config == null) return "";

            var inputs = new ContentInputs
            {
                Kind = config.Kind,
                Content = config.Content,
                Extras = config.Extras
            };

            try { return Json.Serialize(inputs); }
            catch { return null; }   // 序列化失败：当作「变了」，走重建，宁可贵一点也不要留下陈旧内容
        }

        public string StyleSignature()
        {
            Settings settings = _host.Settings;
            var inputs = new StyleInputs
            {
                Style = settings.Style,
                Cli = settings.Cli,
                CornerRadius = settings.Overlay.CornerRadius,
                Shadow = settings.Overlay.Shadow
            };

            try { return Json.Serialize(inputs); }
            catch { return null; }
        }

        /// <summary>
        /// 设置应用后刷新一张仍然存在的卡片：内容 / 样式签名没变就只喂新数据，
        /// 只变样式时优先就地换肤，内容相关设置变了才释放并重建富内容。
        /// </summary>
        public void RefreshSlot(CardSlot slot, CardSettings config)
        {
            if (slot == null || config == null) return;

            string contentKey = ContentSignature(config);
            string styleKey = StyleSignature();

            // 内容没变（改名字 / 热键 / 间距 / 只改外观）：不重建控件。
            // 视频不重启、动图不回第一帧、AI 对话的历史也还在。
            bool sameContent = slot.Content != null &&
                               slot.Rich != null &&
                               contentKey != null &&
                               string.Equals(slot.ContentSignature, contentKey, StringComparison.Ordinal);

            bool sameStyle = styleKey != null &&
                             string.Equals(slot.StyleSignature, styleKey, StringComparison.Ordinal);

            slot.ContentSignature = contentKey;
            slot.StyleSignature = styleKey;

            var context = new CardRenderContext(_host.Settings, _last, _host.Keys, config, null, slot.Host);

            if (sameContent && sameStyle)
            {
                slot.Window.RefreshSettings(slot.Dialog);

                try { slot.Rich.UpdateContent(slot.Content, context); }
                catch (Exception ex)
                {
                    Log.Warn("卡片内容刷新失败(" + config.Name + "): " + ex.Message);
                }
            }
            else if (sameContent)
            {
                // 只改了外观 / CLI 尺寸：支持就地换肤的模块直接套新样式，不销毁内容。
                var styleAware = slot.Module as ICardStyleAware;
                if (styleAware != null)
                {
                    slot.Window.RefreshSettings(slot.Dialog);

                    // 富内容卡的底色 / 不透明度 / 圆角 / 投影由 OverlaySurface 画；
                    // 只调模块 ApplyStyle 的话，媒体卡与 CLI 卡会一直保留旧的外观。
                    slot.Window.RefreshSurface(slot.Dialog);

                    try { styleAware.ApplyStyle(slot.Content, context); }
                    catch (Exception ex)
                    {
                        Log.Warn("卡片换肤失败(" + config.Name + "): " + ex.Message);
                    }
                }
                else
                {
                    // 模块没实现就地换肤（CLI 面板的样式是构造期固化的）：回退到重建。
                    _host.ReleaseContent(slot);

                    if (slot.Shown)
                    {
                        if (slot.Rich != null) _host.ShowCard(slot, "");
                        else slot.Window.Apply(slot.Dialog, RenderCardText(slot));
                    }
                    else
                    {
                        slot.Window.Apply(slot.Dialog, "");
                    }
                }
            }
            else
            {
                // 内容相关设置变了（文件 / 工具 / 模型）：按新设置重建富内容。
                _host.ReleaseContent(slot);

                if (slot.Shown)
                {
                    if (slot.Rich != null) _host.ShowCard(slot, "");
                    else slot.Window.Apply(slot.Dialog, RenderCardText(slot));
                }
                else
                {
                    slot.Window.Apply(slot.Dialog, "");
                }
            }
        }

        string ThrottleKey(CardSlot slot)
        {
            string id = slot.Config == null ? null : slot.Config.Id;
            return string.IsNullOrEmpty(id) ? "?" : id;
        }

        /// <summary>
        /// 每张卡片一个失败节流器。卡片删掉后条目保留（很小）：重建同名 / 同 id 的卡片沿用
        /// 节流状态，避免刚删又加时从「首次失败」重新刷一轮。
        /// </summary>
        LogThrottle GetRenderThrottle(CardSlot slot)
        {
            string key = ThrottleKey(slot);

            LogThrottle throttle;
            if (_renderThrottles.TryGetValue(key, out throttle)) return throttle;

            string name = slot.Config == null || string.IsNullOrEmpty(slot.Config.Name) ? key : slot.Config.Name;
            throttle = new LogThrottle("卡片渲染(" + name + ")", RenderFailureIntervalMs);
            _renderThrottles[key] = throttle;
            return throttle;
        }
    }
}
