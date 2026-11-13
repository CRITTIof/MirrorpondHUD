using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MirrorpondHUD.Core;

namespace MirrorpondHUD.Ui.Controls;

/// <summary>一次播放请求的结果：不可用 / 首池就位（不动画）/ 正在播放。</summary>
public enum ParticlePlayResult
{
    Unavailable,
    Primed,
    Animated
}

/// <summary>
/// 卡片内的“粒子聚字”视图（2.47.0）：采样交给 <see cref="ParticleTextSampler"/>，
/// 运动与绘制交给 <see cref="ParticleField"/>，这里只负责布局、定时器与可见性。
///
/// 换句时每个粒子在**自己的目标字形附近**生成（就近聚集），不再复用上一整行的位置；
/// 否则短句换行会先散成上一整行再飞回来。首池仍然直接落在目标位、不做位移动画。
/// </summary>
public sealed class CardParticleText : FrameworkElement
{
    const double FrameMs = 33.0;
    const double SnapDistance = 0.6;

    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = new();
    readonly Random _random = new(20261113);
    readonly ParticleField _field = new();
    readonly ParticleTextSampler _sampler = new();

    Brush _brush = Brushes.White;
    bool _playing;
    double _speed = 1.0;
    double _size = 2.2;

    public CardParticleText()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        SnapsToDevicePixels = false;
        // 必须用 Hidden 而不是 Collapsed：折叠元素不参与布局，ActualWidth 恒为 0，
        // Play 会一直判定“画布还没准备好”。Hidden 保留测量结果，只是不渲染。
        Visibility = Visibility.Hidden;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FrameMs)
        };
        _timer.Tick += OnTick;
        Unloaded += (s, e) => Stop();
    }

    /// <summary>收敛结束（自然收敛或取消）；调用方据此恢复普通文字。</summary>
    public event Action Settled;

    /// <summary>是否已经有粒子池：首池只就位不动画，和宿主信息卡一致。</summary>
    public bool HasPool => _field.HasPool;

    public bool IsPlaying => _playing;

    /// <summary>粒子边长（DIP），默认 2.2，取值 0.8–4.0。</summary>
    public double ParticleSize
    {
        get => _size;
        set => _size = Math.Max(0.8, Math.Min(4.0, value));
    }

    /// <summary>粒子数量上限，默认 600，取值 60–1600；实际点数由字形墨迹决定。</summary>
    public int ParticleCount { get; set; } = 600;

    /// <summary>收敛速度倍率，0.2–4.0；越大越快。</summary>
    public double Speed
    {
        get => _speed;
        set => _speed = Math.Max(0.2, Math.Min(4.0, value));
    }

    public FontFamily TextFont { get; set; } = new("Microsoft YaHei UI");

    public double TextSize { get; set; } = 16;

    public FontWeight TextWeight { get; set; } = FontWeights.SemiBold;

    /// <summary>
    /// 播放一轮：采样 <paramref name="text"/> 的字形并朝它收拢。
    /// 返回 <see cref="ParticlePlayResult.Unavailable"/> 时调用方应直接显示原文；
    /// <see cref="ParticlePlayResult.Primed"/> 表示首池就位、本来就没有位移动画；
    /// <see cref="ParticlePlayResult.Animated"/> 表示粒子在播，收敛后触发 <see cref="Settled"/>。
    /// </summary>
    public ParticlePlayResult Play(string text, Color color)
    {
        if (string.IsNullOrWhiteSpace(text)) return ParticlePlayResult.Unavailable;

        double width = ActualWidth;
        if (width < 8)
        {
            // 布局还没完成：这一轮不播，调用方直接显示文字，等下一句再聚。
            return ParticlePlayResult.Unavailable;
        }

        int wanted = (int)Math.Max(60, Math.Min(1600, ParticleCount));
        if (!_sampler.TrySample(text, TextFont, TextSize, TextWeight, width,
                                VisualTreeHelper.GetDpi(this), wanted, _random, out List<Point> targets))
        {
            return ParticlePlayResult.Unavailable;
        }
        if (targets.Count < 8) return ParticlePlayResult.Unavailable;

        bool firstPool = !_field.HasPool;
        if (firstPool)
        {
            _field.Prime(targets);
        }
        else
        {
            // 就近聚集：散开半径跟着字号走，短句不会借用上一整行的分布。
            double radius = Math.Max(6, Math.Min(34, TextSize * 1.15));
            _field.ScatterAround(targets, radius, _random);
        }

        _brush = new SolidColorBrush(Color.FromArgb(232, color.R, color.G, color.B));
        _brush.Freeze();

        if (firstPool)
        {
            Log.Debug($"粒子聚字就位：{_field.Count} 点 / {width:0}×{ActualHeight:0} DIP");
            return ParticlePlayResult.Primed;
        }

        Visibility = Visibility.Visible;
        _playing = true;
        _clock.Restart();
        _timer.Start();
        InvalidateVisual();
        return ParticlePlayResult.Animated;
    }

    /// <summary>清空粒子池：换歌 / 卡片重建后第一句从静态开始。</summary>
    public void Reset()
    {
        Stop();
        _field.Reset();
    }

    /// <summary>取消本轮（不发 <see cref="Settled"/>；调用方自己决定文字状态）。</summary>
    public void Stop()
    {
        _playing = false;
        _timer.Stop();
        _clock.Stop();
        Visibility = Visibility.Hidden;
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!_playing) return;

        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (dt <= 0) return;

        double mean = _field.Step(Math.Min(dt, 0.1), _speed);
        InvalidateVisual();
        if (mean < SnapDistance) Finish("收敛");
    }

    void Finish(string reason)
    {
        _playing = false;
        _timer.Stop();
        _clock.Stop();
        Visibility = Visibility.Hidden;
        Log.Debug($"粒子聚字本轮结束: {reason}");
        Settled?.Invoke();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_playing) return;
        _field.Render(dc, _brush, _size, ActualWidth, ActualHeight);
    }
}
