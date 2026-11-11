using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui
{
    /// <summary>
    /// 叠层卡片的堆叠布局：从锚点开始按顺序排、左右对齐、把窗口高度变化合并成一次布局。
    ///
    /// 从 <see cref="OverlayController"/> 抽出来，控制器只负责“什么时候该排”，
    /// 这里负责“怎么排”。坐标 / 显示器 / 对齐都只看 <see cref="Settings"/> 与卡片窗口。
    /// </summary>
    internal sealed class CardLayoutCoordinator
    {
        readonly IList<CardSlot> _cards;
        readonly Func<Settings> _settings;
        readonly Func<bool> _disposed;
        readonly Dispatcher _dispatcher;
        bool _scheduled;

        public CardLayoutCoordinator(IList<CardSlot> cards, Func<Settings> settings,
                                     Func<bool> disposed, Dispatcher dispatcher)
        {
            _cards = cards ?? throw new ArgumentNullException(nameof(cards));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _disposed = disposed ?? (() => false);
            _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        /// <summary>窗口尺寸变化 / 模块请求布局时合并到下一次 Render 优先级执行。</summary>
        public void Schedule()
        {
            if (_scheduled || _disposed()) return;
            _scheduled = true;

            _dispatcher.BeginInvoke(new Action(() =>
            {
                _scheduled = false;
                if (_disposed()) return;
                SyncHeights();
                Layout();
            }), DispatcherPriority.Render);
        }

        /// <summary>从锚点开始按顺序把可见卡片往下排，间距取每张卡片自己的 Spacing。</summary>
        public void Layout()
        {
            LayoutCore(false, null);
        }

        /// <summary>
        /// 带动画的补位：已显示的卡片从当前位置 ease-out 到新位置；
        /// appearing 不为空时，这张新卡直接落到目标位（内容由呼出动画负责），
        /// 避免它从窗口默认位置「飞」到锚点。
        /// </summary>
        public void LayoutAnimated(CardSlot appearing = null)
        {
            LayoutCore(true, appearing);
        }

        void LayoutCore(bool animate, CardSlot appearing)
        {
            Settings settings = _settings();

            // 先量出整叠卡片里最宽的一张：右对齐时每张卡片的 X = 锚点 + (最宽 - 自己宽度)，
            // 这样所有卡片的右边缘落在同一条竖线上；左对齐时所有卡片共用锚点 X。
            double stackWidth = 0;
            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown) continue;
                stackWidth = Math.Max(stackWidth, slot.Window.CardWidth);
            }

            string align = ResolveAlign(settings, stackWidth);
            double top = 0;
            bool first = true;

            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown) continue;

                if (!first) top += Math.Max(0, slot.Config.Spacing);

                double x = settings.Overlay.X;
                if (align == "Right")
                    x += Math.Max(0, stackWidth - slot.Window.CardWidth);

                slot.Dialog.Overlay.X = x;
                slot.Dialog.Overlay.Y = settings.Overlay.Y + top;
                slot.Dialog.Overlay.Monitor = settings.Overlay.Monitor;
                slot.Dialog.Overlay.Locked = settings.Overlay.Locked;
                slot.Window.ApplyPosition(animate && !ReferenceEquals(slot, appearing));

                top += slot.BodyHeight > 0 ? slot.BodyHeight : slot.Window.CardHeight;
                first = false;
            }
        }

        /// <summary>文字变长 / 命令行展开后窗口会自己长高：渲染后校正一次堆叠位置。</summary>
        public void LayoutIfNeeded()
        {
            bool changed = false;
            foreach (CardSlot slot in _cards)
            {
                if (!slot.Shown) continue;
                double height = slot.Window.CardHeight;
                if (Math.Abs(height - slot.BodyHeight) > 0.5)
                {
                    slot.BodyHeight = height;
                    changed = true;
                }
            }

            if (changed) Layout();
        }

        public void SyncHeight(CardSlot slot)
        {
            if (slot == null) return;
            slot.BodyHeight = slot.Shown ? slot.Window.CardHeight : 0;
        }

        public void SyncHeights()
        {
            foreach (CardSlot slot in _cards) SyncHeight(slot);
        }

        /// <summary>左对齐 = 左边缘共线，右对齐 = 右边缘共线；自动按整叠卡片在屏幕左 / 右半边决定。</summary>
        static string ResolveAlign(Settings settings, double stackWidth)
        {
            string mode = Settings.NormalizeCardAlign(settings.Style.CardAlign);
            if (mode == "Left" || mode == "Right") return mode;

            try
            {
                ScreenInfo screen = ScreenGeometry.Get(settings.Overlay.Monitor);
                if (screen == null) return "Left";

                double scaleX = screen.ScaleX > 0.1 ? screen.ScaleX : 1.0;
                double workWidthDip = screen.WorkWidth / scaleX;

                double center = settings.Overlay.X + stackWidth / 2;
                return center < workWidthDip / 2 ? "Left" : "Right";
            }
            catch
            {
                return "Left";
            }
        }
    }
}
