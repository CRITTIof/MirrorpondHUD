using System;
using System.Collections.Generic;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 启动时的逐字动画：欢迎语打字 → 停 → 擦除 → 卡片内容打字 → 回到实时刷新。
    ///
    /// 从 <see cref="OverlayController"/> 抽出来，控制器只保留“开始 / 是否在跑 / 结束”，
    /// 计时器、阶段和临时卡片状态都在这里。
    /// </summary>
    internal sealed class OverlayIntro : IDisposable
    {
        enum Phase
        {
            WelcomeTyping,
            WelcomeHold,
            WelcomeErase,
            ContentTyping,
            Live
        }

        readonly IList<CardSlot> _cards;
        readonly Func<Settings> _settings;
        readonly Func<CardSlot, string> _cardTemplate;
        readonly Func<CardSlot, string> _renderCardText;
        readonly Action<CardSlot> _syncHeight;
        readonly Action _layout;
        readonly Action _renderNow;
        readonly Dispatcher _dispatcher;

        DispatcherTimer _timer;
        CardSlot _card;
        Phase _phase = Phase.Live;
        DateTime _phaseStart;
        string _welcome = "";
        string _content = "";
        string _shownText = "";

        public OverlayIntro(IList<CardSlot> cards, Func<Settings> settings,
                            Func<CardSlot, string> cardTemplate,
                            Func<CardSlot, string> renderCardText,
                            Action<CardSlot> syncHeight,
                            Action layout,
                            Action renderNow,
                            Dispatcher dispatcher)
        {
            _cards = cards ?? throw new ArgumentNullException(nameof(cards));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cardTemplate = cardTemplate ?? throw new ArgumentNullException(nameof(cardTemplate));
            _renderCardText = renderCardText ?? throw new ArgumentNullException(nameof(renderCardText));
            _syncHeight = syncHeight ?? throw new ArgumentNullException(nameof(syncHeight));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _renderNow = renderNow ?? throw new ArgumentNullException(nameof(renderNow));
            _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public bool IsRunning { get { return _phase != Phase.Live; } }

        public bool IsCard(CardSlot slot)
        {
            return ReferenceEquals(slot, _card);
        }

        /// <summary>重新建卡 / 最小化启动时把动画状态清掉，不触发渲染。</summary>
        public void Reset()
        {
            StopTimer();
            _card = null;
            _phase = Phase.Live;
            _shownText = "";
        }

        public void Begin()
        {
            Settings settings = _settings();
            _card = FirstLiveCard();
            if (_card == null || !settings.Behavior.IntroEnabled)
            {
                _phase = Phase.Live;
                _renderNow();
                return;
            }

            _welcome = string.IsNullOrEmpty(settings.Behavior.WelcomeText)
                ? "Welcome Back"
                : settings.Behavior.WelcomeText;
            _content = TemplateEngine.RenderPlaceholder(_cardTemplate(_card), "---");
            _shownText = "";

            // 先用完整内容把整块尺寸定下来，逐字打字时窗口和文本位置都不会抖动。
            _card.Window.SetLayoutText(_content);
            _card.Window.SetText("", true);
            _syncHeight(_card);

            _phase = Phase.WelcomeTyping;
            _phaseStart = DateTime.UtcNow;

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(20)
                };
                _timer.Tick += (s, e) => Tick();
            }

            _timer.Start();
            Log.Info("启动动画开始（欢迎语=" + _welcome + "，速度=" + settings.Behavior.TypeSpeedMs + "ms/字）");
        }

        /// <summary>真实数据的尺寸到了：动画期间不刷新内容，但先按真实数据把卡片撑好。</summary>
        public void LayoutChanged()
        {
            if (_phase == Phase.Live || _card == null) return;
            _card.Window.SetLayoutText(_renderCardText(_card));
            _syncHeight(_card);
            _layout();
        }

        public void Finish()
        {
            if (_phase == Phase.Live) return;
            _phase = Phase.Live;
            StopTimer();

            if (_card != null)
            {
                _card.Window.SetLayoutText(null);
                _card.Window.SetText(_renderCardText(_card), false);
                _syncHeight(_card);
            }

            _card = null;
            _layout();

            Log.Info("启动动画结束，光标消失，开始刷新数据");
            _renderNow();
        }

        public void Dispose()
        {
            StopTimer();
            _timer = null;
        }

        CardSlot FirstLiveCard()
        {
            foreach (CardSlot slot in _cards)
                if (slot.Shown && !slot.Module.IsInteractive && slot.Rich == null) return slot;
            return null;
        }

        void StopTimer()
        {
            try { if (_timer != null) _timer.Stop(); } catch { }
        }

        void Tick()
        {
            if (_phase == Phase.Live)
            {
                StopTimer();
                return;
            }

            double speed = Math.Max(8, _settings().Behavior.TypeSpeedMs);
            double elapsed = (DateTime.UtcNow - _phaseStart).TotalMilliseconds;
            int chars = (int)(elapsed / speed);

            switch (_phase)
            {
                case Phase.WelcomeTyping:
                {
                    int n = Math.Min(chars, _welcome.Length);
                    ShowText(_welcome.Substring(0, n));
                    if (n >= _welcome.Length)
                    {
                        _phase = Phase.WelcomeHold;
                        _phaseStart = DateTime.UtcNow;
                    }
                    break;
                }

                case Phase.WelcomeHold:
                    if ((DateTime.UtcNow - _phaseStart).TotalMilliseconds >= 650)
                    {
                        _phase = Phase.WelcomeErase;
                        _phaseStart = DateTime.UtcNow;
                    }
                    break;

                case Phase.WelcomeErase:
                {
                    int removed = Math.Min(chars, _welcome.Length);
                    int remain = _welcome.Length - removed;
                    ShowText(remain <= 0 ? "" : _welcome.Substring(0, remain));
                    if (remain <= 0)
                    {
                        _phase = Phase.ContentTyping;
                        _phaseStart = DateTime.UtcNow;
                    }
                    break;
                }

                case Phase.ContentTyping:
                {
                    int n = Math.Min(chars, _content.Length);
                    ShowText(_content.Substring(0, n));
                    if (n >= _content.Length) Finish();
                    break;
                }
            }
        }

        void ShowText(string text)
        {
            if (text == _shownText || _card == null) return;
            _shownText = text;
            _card.Window.SetText(text, true);
        }
    }
}
