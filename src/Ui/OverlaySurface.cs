using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui.Contour;
using MirrorpondHUD.Ui.Controls;

namespace MirrorpondHUD.Ui;

/// <summary>叠层内容与设置面板预览共用的绘制表面：底色 / 背景图 / 圆角 / 投影 / 文字。</summary>
public sealed class OverlaySurface : Border
{
    static readonly Dictionary<string, SolidColorBrush> BrushCache =
        new(StringComparer.OrdinalIgnoreCase);

    readonly Grid _root;
    readonly Border _tintLayer;
    readonly Border _glassLayer;
    readonly Border _imageLayer;
    readonly Border _decorLayer;
    readonly Border _loopLayer;
    readonly Border _loopBand;
    readonly Border _contentLayer;
    readonly TextCanvas _canvas;
    readonly ParticleTextLayer _particleLayer;

    /// <summary>
    /// G-19：玻璃画刷只建一次。旧实现每帧 new ImageBrush + Freeze + 重设 BitmapScalingMode，
    /// 每秒几十次分配，对静态属性重复赋值也会触发额外的属性失效。
    /// G-17：取样图改成可复用的 WriteableBitmap 后，同一实例的内容更新由 WPF 自己重绘，
    /// 这里只在换图（尺寸变化）时重建画刷。WriteableBitmap 不可 Freeze —— 只在 CanFreeze 时冻结。
    /// </summary>
    ImageBrush _glassBrush;
    readonly ContourDecoration _contourLayer;
    readonly Border _contourHost;
    readonly TextBlock _metaTop;
    readonly TextBlock _metaBottom;
    CardSettings _metaConfig;
    Thickness _textMargin;
    DispatcherTimer _contourTimer;
    readonly Stopwatch _contourWatch = new();
    double _contourPhase;
    double _contourSpeed = 1.0;

    Settings _lastSettings;
    string _resolvedCardAlign;
    string _lastCardAlignSetting;
    string _lastParticleText;
    DateTime _lastParticlePlayUtc = DateTime.MinValue;
    string _lastTextEffect = "None";
    string _lastParticleSettings;
    Color _particleColor = Colors.White;
    bool _particlePending;
    int _particleRetries;

    /// <summary>
    /// 2.45.3：模板动态掩码定位失败的节流打点（30s）。降级是"正确性优先"的正常路径，
    /// 不是错误，但必须留痕（Debug）——否则"某个字面量为什么还在动"会变成没法排查的黑盒。
    /// </summary>
    long _particleMaskFallbackTicks;

    BitmapImage _image;
    string _imagePath;
    // G-7：路径 + 最后写入时间 + 长度做缓存键；失败不再永久缓存（30s 后重试）。
    string _imageKey;
    DateTime _imageNextStat = DateTime.MinValue;

    public OverlaySurface()
    {
        _canvas = new TextCanvas();
        _particleLayer = new ParticleTextLayer();
        _particleLayer.Finished += OnParticleFinished;
        _tintLayer = new Border { IsHitTestVisible = false };
        _glassLayer = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _imageLayer = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        // M6：装饰纹样画在底色 / 背景图之上、文字与富内容之下；不参与命中测试。
        _decorLayer = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        // M7 循环呼吸：常驻合成动画；默认关闭，描述见 OverlaySurface.ApplyLoopMotion。
        _loopLayer = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Opacity = 0
        };
        _loopBand = new Border
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransform = new TranslateTransform()
        };
        _contourLayer = new ContourDecoration { Visibility = Visibility.Visible };
        _contourLayer.SizeChanged += (s, e) => _contourLayer.InvalidateVisual();
        _contourHost = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            ClipToBounds = true,
            Child = _contourLayer
        };
        _contentLayer = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            ClipToBounds = true
        };
        _metaTop = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _metaBottom = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        _root = new Grid();
        _root.Children.Add(_glassLayer);
        _root.Children.Add(_tintLayer);
        _root.Children.Add(_imageLayer);
        _root.Children.Add(_decorLayer);
        _root.Children.Add(_contourHost);
        _root.Children.Add(_loopLayer);
        _root.Children.Add(_loopBand);
        _root.Children.Add(_canvas);
        _root.Children.Add(_particleLayer);
        _root.Children.Add(_contentLayer);
        _root.Children.Add(_metaTop);
        _root.Children.Add(_metaBottom);
        Unloaded += (s, e) => StopContourLoop();
        Unloaded += (s, e) => _particleLayer.Stop();
        Loaded += OnSurfaceLoaded;
        IsVisibleChanged += OnSurfaceVisibleChanged;


        Child = _root;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
    }

    public TextCanvas Canvas => _canvas;

    void OnParticleFinished() => _canvas.ClearHiddenChars();

    void OnSurfaceLoaded(object sender, RoutedEventArgs e) => FlushParticlePending();

    void OnSurfaceVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) FlushParticlePending();
        else
        {
            _particlePending = false;
            _particleRetries = 0;
            _particleLayer.Stop();
            _canvas.ClearHiddenChars();
            // 隐藏后再呼出同一句也要重新播；下次显示从当前文字重新开始。
            _lastParticleText = null;
        }
    }

    void FlushParticlePending()
    {
        if (!_particlePending) return;
        if (!ParticleEnabled())
        {
            _particlePending = false;
            _particleRetries = 0;
            return;
        }

        if (_particleRetries++ > 8)
        {
            // 画布一直没量好（例如卡片刚创建就被折叠）：放弃这次，等下一次 SetText。
            _particlePending = false;
            _particleRetries = 0;
            return;
        }

        PlayParticle(_canvas.Text ?? "");
        if (!_particlePending) return;

        // 首次显示时 IsVisibleChanged 往往早于布局，ActualWidth 还是 0；等下一轮布局再试。
        Dispatcher.BeginInvoke(new Action(FlushParticlePending),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    bool ParticleEnabled()
    {
        if (_lastSettings is null || _metaConfig is null) return false;
        if (!string.Equals(_metaConfig.Kind, "infotext", StringComparison.OrdinalIgnoreCase)) return false;
        return CardMotion.ParticleTextEnabled(_lastSettings);
    }

    /// <summary>纯文字卡的 M6 编号 / 页脚；富内容卡由各自控件画，传 null 即隐藏。</summary>
    public void SetCardMeta(CardSettings config)
    {
        bool kindChanged = !string.Equals(_metaConfig?.Kind, config?.Kind, StringComparison.OrdinalIgnoreCase);
        _metaConfig = config;
        if (kindChanged)
        {
            _particleLayer.Stop();
            _canvas.ClearHiddenChars();
            _lastParticleText = null;
            _particlePending = false;
        }
        UpdateMeta();
    }

    void UpdateMeta()
    {
        bool textMode = !HasContent && _lastSettings is not null && _metaConfig is not null
                        && CardMeta.Enabled(_lastSettings, _metaConfig);
        string header = textMode ? CardMeta.Header(_lastSettings, _metaConfig) : "";
        string footer = textMode ? CardMeta.Footer(_lastSettings, _metaConfig) : "";
        string title = textMode ? (_metaConfig.Name ?? "") : "";

        _metaTop.Text = header + title;
        _metaBottom.Text = footer;
        _metaTop.Visibility = string.IsNullOrWhiteSpace(_metaTop.Text) ? Visibility.Collapsed : Visibility.Visible;
        _metaBottom.Visibility = string.IsNullOrWhiteSpace(footer) ? Visibility.Collapsed : Visibility.Visible;

        if (textMode)
        {
            _canvas.Margin = new Thickness(
                _textMargin.Left,
                _textMargin.Top + 20,
                _textMargin.Right,
                _textMargin.Bottom + 20);
        }
        else
        {
            _canvas.Margin = _textMargin;
        }
        _particleLayer.Margin = _canvas.Margin;
    }

    /// <summary>装饰层是否为等高线且首帧几何已就绪；截图等离屏路径用。</summary>
    public bool ContourReady => _contourHost.Visibility != Visibility.Visible || _contourLayer.IsReady;

    /// <summary>主动预热等高线首帧；离屏渲染不会自动跑 OnRender 的请求。</summary>
    public void PrimeContour()
    {
        if (_contourHost.Visibility == Visibility.Visible) _contourLayer.Prime();
    }

    /// <summary>
    /// 布局协调器算出的整叠对齐（Left / Right）。只影响“跟随卡片对齐”的文字；
    /// 空 = 让表面按卡片位置自己判断（设置窗预览 / 截图等没有协调器的场景）。
    /// </summary>
    public void SetResolvedCardAlignment(string value)
    {
        string normalized = string.IsNullOrEmpty(value)
            ? null
            : (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase) ? "Right" : "Left");
        if (string.Equals(_resolvedCardAlign, normalized, StringComparison.Ordinal)) return;
        _resolvedCardAlign = normalized;
        UpdateTextAlignment();
    }

    /// <summary>最近一次布局协调器解析出的整叠对齐；卡片滑入“跟随对齐”用。</summary>
    public string ResolvedCardAlignment => _resolvedCardAlign;

    /// <summary>
    /// 文字对齐：Left / Center / Right / Justify 直接映射；Card 跟随整叠对齐。
    /// 没有协调器传入的整叠对齐时，用卡片自身在屏幕上的位置兜底。
    /// </summary>
    void UpdateTextAlignment()
    {
        if (_lastSettings is null) return;

        string mode = Settings.NormalizeTextAlign(_lastSettings.Style.TextAlignment);
        if (!string.Equals(mode, "Card", StringComparison.Ordinal))
        {
            _canvas.Alignment = ParseAlignment(mode);
            return;
        }

        string cardAlign = _resolvedCardAlign;
        if (string.IsNullOrEmpty(cardAlign))
        {
            cardAlign = Settings.NormalizeCardAlign(_lastSettings.Style.CardAlign);
            if (cardAlign == "Auto") cardAlign = ResolveAutoCardAlign();
        }

        _canvas.Alignment = string.Equals(cardAlign, "Right", StringComparison.OrdinalIgnoreCase)
            ? TextAlignment.Right
            : TextAlignment.Left;
    }

    string ResolveAutoCardAlign()
    {
        try
        {
            ScreenInfo screen = ScreenGeometry.Get(_lastSettings.Overlay.Monitor, _lastSettings.Overlay.MonitorDevice);
            if (screen is null) return "Left";

            double scaleX = screen.ScaleX > 0.1 ? screen.ScaleX : 1.0;
            double workWidth = screen.WorkWidth / scaleX;
            double width = ActualWidth > 0 ? ActualWidth : 320;
            double center = _lastSettings.Overlay.X + width / 2;
            return center < workWidth / 2 ? "Left" : "Right";
        }
        catch (Exception ex)
        {
            Log.Warn($"判断文字跟随对齐失败，回落左对齐: {ex.Message}");
            return "Left";
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_lastSettings is not null
            && string.Equals(Settings.NormalizeTextAlign(_lastSettings.Style.TextAlignment),
                "Card", StringComparison.Ordinal)
            && string.IsNullOrEmpty(_resolvedCardAlign))
        {
            UpdateTextAlignment();
        }
    }

    /// <summary>
    /// 富内容里的控件能不能收到鼠标。默认 false（图片 / 视频不该挡住桌面点击）；
    /// 交互卡片（按钮 / 输入框）由宿主设成 true，否则命中测试会停在内容层外面。
    /// </summary>
    public bool ContentHitTestVisible
    {
        get => _contentLayer.IsHitTestVisible; set => _contentLayer.IsHitTestVisible = value;
    }

    /// <summary>富内容（图片 / 动图 / 视频）的宿主；有内容时文字层隐藏。</summary>
    public void SetContent(FrameworkElement content)
    {
        if (content is null)
        {
            ClearContent();
            return;
        }

        _contentLayer.Child = content;
        _contentLayer.Visibility = Visibility.Visible;
        _canvas.Visibility = Visibility.Collapsed;
        UpdateMeta();
    }

    public void ClearContent()
    {
        _contentLayer.Child = null;
        _contentLayer.Visibility = Visibility.Collapsed;
        _canvas.Visibility = Visibility.Visible;
        UpdateMeta();
    }

    public bool HasContent => _contentLayer.Child is not null;

    /// <summary>当前富内容控件（宿主用它把焦点交给 CLI 面板的输入行）。</summary>
    public FrameworkElement ContentElement => _contentLayer.Child as FrameworkElement;

    public void SetText(string text, bool cursor) => SetText(text, cursor, -1);

    void MaybePlayParticle(string text, bool cursor)
    {
        if (_lastSettings is null) return;

        string current = text ?? "";
        if (!ParticleEnabled())
        {
            _particleLayer.Stop();
            _canvas.ClearHiddenChars();
            _lastParticleText = current;
            _particlePending = false;
            return;
        }

        // 启动打字动画 / CLI 光标逐字刷新不播粒子；打字动画与粒子互斥。
        if (cursor)
        {
            _particleLayer.Stop();
            _canvas.ClearHiddenChars();
            _lastParticleText = current;
            _particlePending = false;
            return;
        }

        if (string.Equals(current, _lastParticleText, StringComparison.Ordinal)) return;

        if (!IsLoaded || !IsVisible)
        {
            _particlePending = true;
            _particleRetries = 0;
            return;
        }

        // 指标卡 120ms 刷新一次；按卡片限频，避免粒子层永远在重启动。
        bool metrics = string.Equals(_metaConfig?.Preset, "metrics", StringComparison.OrdinalIgnoreCase);
        if (metrics && (DateTime.UtcNow - _lastParticlePlayUtc).TotalMilliseconds < 800) return;

        PlayParticle(current);
    }

    /// <summary>
    /// 只把变化字符交给粒子层，未变字符继续由 TextCanvas 正常绘制。
    /// 2.45.3：模板里预先写好的文字与图标 / 键帽码位一律不参赛——前者由
    /// <see cref="TemplateEngine.TryBuildDynamicMask"/> 的掩码剔除，后者按字形恒排除
    /// （即使掩码失败也不放过）；首播（整段文本都算"变化"）走同一条过滤。
    /// </summary>
    void PlayParticle(string current)
    {
        int[] changed = ChangedIndices(_lastParticleText ?? "", current);
        if (changed.Length == 0)
        {
            _lastParticleText = current;
            return;
        }

        string template = _metaConfig?.Content;
        bool[] mask = BuildParticleMask(template, current);
        int changedBefore = changed.Length;
        changed = ParticleTextLayer.FilterParticleTargets(current, changed, mask);
        // 诊断（Debug，默认关闭）：冒烟时用来核对"参赛字符"只覆盖动态值。
        Log.Debug($"粒子参赛字符：变化 {changedBefore} → {changed.Length}"
            + $"（{(mask is null ? "掩码不可用，仅排除图标/键帽" : "模板动态掩码生效")}，文本 {current.Length} 字符）");
        if (changed.Length == 0)
        {
            // 本次变化全在模板字面量 / 图标上：粒子没有可动的字符。
            // 不启动粒子层，但必须清掉遮罩——上一轮藏起来的字符不能一直缺字。
            _canvas.ClearHiddenChars();
            _lastParticleText = current;
            return;
        }

        // 遮罩由粒子层在 BeginPlay 里与“显示粒子”同一回调设置；
        // 宿主不能提前藏字，否则 WPF 可能在粒子出现前合成一帧缺字。
        bool firstPool = !_particleLayer.HasPool;
        int configured = (int)Math.Max(200, Math.Min(3000,
            _lastSettings is null ? 900 : _lastSettings.Style.TextParticleCount));
        int count = firstPool ? configured
            : Math.Max(60, Math.Min(configured, changed.Length * 120));

        _particleLayer.Margin = _canvas.Margin;
        // 动画中途换文本时由粒子层用同一个掩码工厂按新文本重算（见 ResolveChangedTargets）。
        if (_particleLayer.SchedulePlay(_canvas, _particleColor, _lastSettings.Style, changed, count,
                                        mask, text => BuildParticleMask(template, text)))
        {
            _lastParticleText = current;
            _lastParticlePlayUtc = DateTime.UtcNow;
            _particlePending = false;
            _particleRetries = 0;
        }
        else
        {
            _particlePending = true;
            // 不要重置 _particleRetries：否则画布长期量不出来时会在 Background 级无限自旋。
        }
    }

    /// <summary>
    /// 2.45.3：算本卡模板的动态字符掩码。只有 infotext 卡的
    /// <see cref="CardSettings.Content"/> 就是渲染模板，粒子也只对这类卡生效
    /// （见 <see cref="ParticleEnabled"/>）。返回 null = 退回"仅排除图标 / 键帽"，
    /// 绝不猜一个可能错位的掩码。
    ///
    /// 模板先按画布同样的规则归一换行：<see cref="SetText(string, bool, int)"/> 会把 CRLF
    /// 压成 LF，不归一就会让字面量定位整体错一位。
    /// </summary>
    bool[] BuildParticleMask(string template, string text)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            LogParticleMaskFallback("卡片模板为空，无法区分字面量与动态值");
            return null;
        }

        if (TemplateEngine.TryBuildDynamicMask(NormalizeNewlines(template), text ?? "", out bool[] mask))
            return mask;

        LogParticleMaskFallback("模板字面量在渲染文本里定位失败（用户内容可能含相同片段，或模板与渲染不同源）");
        return null;
    }

    /// <summary>2.45.3：掩码降级按 30s 节流写 Debug（保守回退不是错误，但必须可见）。</summary>
    void LogParticleMaskFallback(string reason)
    {
        long now = Stopwatch.GetTimestamp();
        if (_particleMaskFallbackTicks != 0
            && Stopwatch.GetElapsedTime(_particleMaskFallbackTicks, now).TotalMilliseconds < 30000)
        {
            return;
        }

        _particleMaskFallbackTicks = now;
        Log.Debug($"粒子动态掩码退回「仅排除图标/键帽」: {reason}");
    }

    /// <summary>统一换行：TextCanvas 的 Split 会把 CRLF / CR 归一成 LF，
    /// 遮罩下标必须基于同一字符串，否则多行文本从第二行起会整体错一位。</summary>
    static string NormalizeNewlines(string text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>
    /// 按字符找变化位置（只服务粒子层：变化字符交给粒子，未变字符继续由画布绘制）。
    ///
    /// · 等长：逐字符比较，最精确（时钟 "11:08:21" → "11:08:23" 只标最后一位）；
    /// · 不等长：行数相同时按行对齐，逐行取「行内公共前缀 + 公共后缀」之外的新字符。
    ///   2.45.2 之前是全局公共前后缀包住一整段——多行各自变长/变短（指标卡同时改两行）
    ///   时，中间没变的那一行也会被标成变化，整段重采、粒子多摊一片。
    /// · 行数不同（有整行增删 / 行号已错位）时无法安全对齐，退回全局公共前后缀整段标记。
    ///
    /// 两种策略都只会多标、不会漏标：返回的下标一定覆盖新文本里所有真正变化的字符。
    /// 纯删除某一行的字符时该行窗口为空（新文本里没有要重采的字符），不会退化成整行重采。
    /// </summary>
    internal static int[] ChangedIndices(string oldText, string newText)
    {
        oldText ??= "";
        newText ??= "";
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return Array.Empty<int>();

        if (oldText.Length == newText.Length)
        {
            var changed = new List<int>();
            for (int i = 0; i < newText.Length; i++)
                if (oldText[i] != newText[i]) changed.Add(i);
            return changed.ToArray();
        }

        List<(int Start, int Length)> oldLines = SplitLineSpans(oldText);
        List<(int Start, int Length)> newLines = SplitLineSpans(newText);
        if (oldLines.Count == newLines.Count)
        {
            List<int> refined = null;
            for (int i = 0; i < newLines.Count; i++)
            {
                string oldLine = oldText.Substring(oldLines[i].Start, oldLines[i].Length);
                string newLine = newText.Substring(newLines[i].Start, newLines[i].Length);
                if (string.Equals(oldLine, newLine, StringComparison.Ordinal)) continue;

                refined ??= new List<int>();
                AddLineChange(oldLine, newLine, newLines[i].Start, refined);
            }

            // 行数相同时"所有行都相同"不可能（文本不等、且等长分支已排除），
            // 但纯删除会让所有窗口为空：新文本里确实没有需要重采的字符。
            if (refined is not null) return refined.ToArray();
        }

        int prefix = 0;
        while (prefix < oldText.Length && prefix < newText.Length && oldText[prefix] == newText[prefix])
            prefix++;
        int suffix = 0;
        while (suffix < oldText.Length - prefix && suffix < newText.Length - prefix
               && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
            suffix++;

        int count = newText.Length - prefix - suffix;
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = prefix + i;
        return result;
    }

    /// <summary>
    /// 按 '\n' 切行并给出每行在原文里的 [Start, Start+Length)（不含行尾换行符）。
    /// 与 <c>TextCanvas.Split</c> 同源：宿主在 SetText 已把 '\r' 归一成 '\n'，
    /// 这里不再处理 '\r'，避免行起点与画布文本错位。
    /// </summary>
    static List<(int Start, int Length)> SplitLineSpans(string text)
    {
        var spans = new List<(int Start, int Length)>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            spans.Add((start, i - start));
            start = i + 1;
        }
        spans.Add((start, text.Length - start));
        return spans;
    }

    /// <summary>
    /// 一对不同行之间的最小变化窗口：行内公共前缀 + 公共后缀之外的新字符。
    /// 前缀 / 后缀都不跨行，所以一行内的插入只标插入段，不会波及其它行。
    /// </summary>
    static void AddLineChange(string oldLine, string newLine, int newLineStart, List<int> result)
    {
        int prefix = 0;
        while (prefix < oldLine.Length && prefix < newLine.Length && oldLine[prefix] == newLine[prefix])
            prefix++;
        int suffix = 0;
        while (suffix < oldLine.Length - prefix && suffix < newLine.Length - prefix
               && oldLine[oldLine.Length - 1 - suffix] == newLine[newLine.Length - 1 - suffix])
        {
            suffix++;
        }

        for (int i = prefix; i < newLine.Length - suffix; i++) result.Add(newLineStart + i);
    }

    /// <summary>当前文字层内容（只重画底层时用来原样恢复）。</summary>
    public string Text => _canvas.Text;

    public void SetText(string text, bool cursor, int cursorIndex)
    {
        _canvas.Text = NormalizeNewlines(text);
        _canvas.CursorIndex = cursorIndex;
        _canvas.CursorVisible = cursor;
        MaybePlayParticle(_canvas.Text, cursor);
    }

    /// <summary>重新按当前内容量尺寸（切换命令行时用）。</summary>
    public void ResetStableSize() => _canvas.ResetStableSize();

    /// <summary>用于计算尺寸的文本；null = 跟随显示内容。启动动画期间传完整内容。</summary>
    public void SetLayoutText(string text) => _canvas.LayoutText = NormalizeNewlines(text);

    public void Apply(Settings settings, string text) => Apply(settings, text, false);

    public void Apply(Settings settings, string text, bool cursor)
    {
        StyleSettings style = settings.Style;
        OverlaySettings overlay = settings.Overlay;
        _lastSettings = settings;

        _canvas.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(style.FontFamily)
            ? "Microsoft YaHei UI"
            : style.FontFamily);
        ThemeTokens tokens = ThemeResolver.Resolve(style);
        _canvas.MonoFont = new FontFamily(tokens.MonoFont);
        _canvas.MonoNumbers = style.MonoReadouts;
        _canvas.FontSize = Math.Max(4, style.FontSize);
        _canvas.IconScale = style.IconScale > 0 ? style.IconScale : 1.0;
        _canvas.FontWeight = style.Bold ? FontWeights.SemiBold : FontWeights.Normal;
        _canvas.Foreground = GetBrush(style.Foreground, 1.0);
        _canvas.LineHeight = style.LineHeight > 0 ? style.LineHeight : 0;

        // 卡片对齐设置变了，上一轮布局传下来的 Left / Right 就过期了；
        // 先清掉，让本次 Apply 按新的显式值（或卡片自身位置）排版，等下一次布局再覆盖。
        string cardAlignSetting = Settings.NormalizeCardAlign(style.CardAlign);
        if (_lastCardAlignSetting is not null
            && !string.Equals(_lastCardAlignSetting, cardAlignSetting, StringComparison.Ordinal))
        {
            _resolvedCardAlign = null;
        }
        _lastCardAlignSetting = cardAlignSetting;

        UpdateTextAlignment();

        _canvas.Outline = style.TextOutline && style.OutlineThickness > 0.05;
        _canvas.OutlineBrush = GetBrush(style.OutlineColor, 1.0);
        _canvas.OutlineThickness = Math.Max(0.2, style.OutlineThickness);

        _canvas.FixedWidth = Math.Max(0, style.FixedWidth);
        _canvas.CursorStyle = Settings.NormalizeCursor(settings.Behavior.CursorStyle);
        _canvas.CursorBlink = settings.Behavior.CursorBlink;

        _canvas.ResetStableSize();
        _canvas.Text = NormalizeNewlines(text);
        _canvas.CursorVisible = cursor;

        Background = Brushes.Transparent;
        CornerRadius = new CornerRadius(Math.Max(0, overlay.CornerRadius));
        _tintLayer.Background = GetBrush(style.Background, style.BackgroundOpacity);
        _tintLayer.CornerRadius = CornerRadius;
        _glassLayer.CornerRadius = CornerRadius;
        if (!style.Acrylic) ClearGlass();
        Padding = new Thickness(0);

        double pad = Math.Max(0, style.Padding);
        // 富内容（图片 / 视频）铺满卡片，文字按内边距缩进。
        _textMargin = new Thickness(pad * 1.15, pad * 0.7, pad * 1.15, pad * 0.7);
        _canvas.Margin = _textMargin;
        _metaTop.FontFamily = new FontFamily(tokens.MonoFont);
        _metaTop.FontSize = Math.Max(8, style.FontSize - 3);
        _metaTop.Foreground = GetBrush(style.Foreground, 0.46);
        _metaTop.Margin = new Thickness(pad * 1.15, Math.Max(9, pad * 0.8), 0, 0);
        _metaBottom.FontFamily = _metaTop.FontFamily;
        _metaBottom.FontSize = Math.Max(8, style.FontSize - 4);
        _metaBottom.Foreground = GetBrush(style.Foreground, 0.34);
        _metaBottom.Margin = new Thickness(0, 0, pad * 1.15, Math.Max(9, pad * 0.8));
        _contentLayer.Margin = new Thickness(0);
        _contentLayer.CornerRadius = CornerRadius;

        _imageLayer.CornerRadius = CornerRadius;
        ApplyImage(style);
        ApplyDecoration(style, tokens);
        ApplyLoopMotion(style, tokens);

        if (overlay.Shadow)
        {
            // M2：柔和环境遮蔽 + 1px 接触阴影，避免纯零偏移时卡片像“浮在黑影上”。
            bool ambient = EndfieldVisuals.Shadow(style);
            Effect = new DropShadowEffect
            {
                BlurRadius = ambient ? 32 : 26,
                ShadowDepth = ambient ? 1 : 4,
                Direction = 270,
                Opacity = ambient ? 0.24 : 0.55,
                Color = Colors.Black
            };
            Margin = new Thickness(ambient ? 14 : 10);
        }
        else
        {
            Effect = null;
            Margin = new Thickness(0);
        }

        UpdateMeta();

        _particleColor = ParseColor(style.Foreground, Colors.White);
        string textEffect = Settings.NormalizeTextEffect(style.TextEffect);
        string particleSettings = $"{style.TextParticleSize:0.###}|{style.TextParticleCount}|"
            + $"{style.TextParticleSpeed:0.###}|{style.TextParticleNearest}|{style.TextParticleGeometry}";
        if (!string.Equals(particleSettings, _lastParticleSettings, StringComparison.Ordinal))
        {
            _lastParticleSettings = particleSettings;
            // 改大小 / 数量 / 速度 / 采样方式后，即使文字没变也重播一次，方便即时看效果。
            _lastParticleText = null;
            _lastParticlePlayUtc = DateTime.MinValue;
        }
        if (!string.Equals(textEffect, _lastTextEffect, StringComparison.Ordinal))
        {
            _lastTextEffect = textEffect;
            // 从“无”切到“粒子”时强制下一次播放，不能因为文字没变就看不到效果。
            _lastParticleText = null;
            _lastParticlePlayUtc = DateTime.MinValue;
        }
        _particleLayer.Margin = _canvas.Margin;
        MaybePlayParticle(_canvas.Text, cursor);
    }

    // ==================== 背景图片 ====================

    /// <summary>G-7：同一路径换图 / 重写后也能发现；失败状态不再永久缓存。</summary>
    static string ImageCacheKey(string path)
    {
        try
        {
            var info = new System.IO.FileInfo(path);
            return info.Exists ? $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : $"{path}|missing";
        }
        catch (Exception ex)
        {
            Log.Debug($"读取背景图元数据失败({path}): {ex.Message}");
            return path;
        }
    }

    void ApplyImage(StyleSettings style)
    {
        string path = (style.BackgroundImage ?? "").Trim();
        if (path.Length == 0)
        {
            _imageLayer.Visibility = Visibility.Collapsed;
            _imageLayer.Background = null;
            _image = null;
            _imagePath = null;
            _imageKey = null;
            _imageNextStat = DateTime.MinValue;
            return;
        }

        bool reload = !string.Equals(path, _imagePath, StringComparison.OrdinalIgnoreCase);
        if (!reload && DateTime.UtcNow >= _imageNextStat)
        {
            _imageNextStat = DateTime.UtcNow.AddSeconds(2);
            reload = !string.Equals(ImageCacheKey(path), _imageKey, StringComparison.Ordinal);
        }

        if (reload)
        {
            _image = LoadImage(path);
            _imagePath = path;
            if (_image is not null)
            {
                _imageKey = ImageCacheKey(path);
                _imageNextStat = DateTime.UtcNow.AddSeconds(2);
            }
            else
            {
                _imageKey = null;
                _imageNextStat = DateTime.UtcNow.AddSeconds(30);
                Log.Warn($"背景图片无法加载: {path}");
            }
        }

        if (_image is null)
        {
            _imageLayer.Visibility = Visibility.Collapsed;
            return;
        }

        var brush = new ImageBrush(_image)
        {
            Stretch = ParseStretch(style.ImageStretch),
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TileMode = TileMode.None
        };
        brush.Freeze();

        _imageLayer.Background = brush;
        _imageLayer.Opacity = Math.Max(0, Math.Min(1, style.ImageOpacity));
        _imageLayer.Visibility = Visibility.Visible;
    }

    // ==================== M6 装饰纹样 ====================

    /// <summary>
    /// 低透明度纹样只作点缀：网格 / 扫描线 / 45° 斜纹 / 半调网点 / 等高线。
    /// 用冻结的 DrawingBrush 平铺，不建可视树、不参与命中测试；None 时整层折叠。
    /// </summary>
    void ApplyDecoration(StyleSettings style, ThemeTokens tokens)
    {
        string kind = Settings.NormalizeDecoration(style.Decoration);
        if (string.Equals(kind, "None", StringComparison.Ordinal))
        {
            _decorLayer.Background = null;
            _decorLayer.Visibility = Visibility.Collapsed;
            _contourHost.Visibility = Visibility.Collapsed;
            StopContourLoop();
            return;
        }

        if (string.Equals(kind, "Contour", StringComparison.Ordinal))
        {
            // 等高线不是平铺画刷：它是按卡片尺寸 + 地形场实时生成的几何。
            _decorLayer.Background = null;
            _decorLayer.Visibility = Visibility.Collapsed;
            _contourHost.CornerRadius = CornerRadius;
            _contourHost.Visibility = Visibility.Visible;
            _contourLayer.Apply(style, ThemeResolver.WithAlpha(tokens.Border, 0.22));
            return;
        }

        _contourHost.Visibility = Visibility.Collapsed;
        StopContourLoop();

        DrawingBrush brush = BuildDecorationBrush(kind, tokens.Border, style.DecorationDensity);
        if (brush is null)
        {
            _decorLayer.Background = null;
            _decorLayer.Visibility = Visibility.Collapsed;
            return;
        }

        _decorLayer.CornerRadius = CornerRadius;
        _decorLayer.Background = brush;
        // 纹样只做“坐标系”暗示：32% 是深色底上可见、又不压过文字的档位。
        _decorLayer.Opacity = 0.32;
        _decorLayer.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// M7 循环动效：None / Breath（整卡明暗） / Scanline（横线扫过） / Pulse（描边明暗） /
    /// Contour（等高线相位漂移）。
    ///
    /// 性能代价（必须知道）：
    /// - Breath / Pulse 是 WPF 合成器动画，无每帧托管分配，但持续占用 GPU 填充/合成；
    ///   Scanline 只重绘一条 2px 横线；
    /// - Contour 不是合成动画：约 30fps 重算地形与几何；基础高度网格与容器已缓存，
    ///   几何在后台生成，UI 线程只提交冻结结果（详见 ContourDecoration）。
    /// - 4K@300% 的卡片约 1200×900 物理像素；默认关闭，ReduceMotion 一票否决。
    /// - 关掉后立即 BeginAnimation(null) / 停表并折叠，不保留动画时钟。
    /// </summary>
    void ApplyLoopMotion(StyleSettings style, ThemeTokens tokens)
    {
        _loopLayer.CornerRadius = CornerRadius;
        _loopLayer.BeginAnimation(UIElement.OpacityProperty, null);
        _loopLayer.Opacity = 0;
        _loopLayer.Background = null;
        _loopLayer.BorderBrush = null;
        _loopLayer.BorderThickness = new Thickness(0);
        _loopLayer.Visibility = Visibility.Collapsed;

        _loopBand.BeginAnimation(UIElement.OpacityProperty, null);
        _loopBand.Opacity = 0;
        _loopBand.Background = null;
        _loopBand.Visibility = Visibility.Collapsed;
        var bandTransform = _loopBand.RenderTransform as TranslateTransform;
        if (bandTransform is not null) bandTransform.BeginAnimation(TranslateTransform.YProperty, null);
        StopContourLoop();

        string effect = Settings.NormalizeLoopEffect(style.LoopEffect, style.LoopMotion);
        if (effect == "None" || !CardMotion.Enabled) return;

        double durationMs = Math.Max(600, Math.Min(20000, style.LoopDurationMs));
        double amplitude = Math.Max(0.02, Math.Min(0.8, style.LoopAmplitude));
        var brush = new SolidColorBrush(tokens.Accent);
        brush.Freeze();

        if (effect == "Contour")
        {
            StartContourLoop(style.ContourSpeed);
            return;
        }

        if (effect == "Scanline")
        {
            _loopBand.Height = Math.Max(1, Math.Min(10, style.LoopScanlineThickness));
            _loopBand.Background = brush;
            _loopBand.Opacity = amplitude;
            _loopBand.Visibility = Visibility.Visible;

            if (bandTransform is not null)
            {
                // 固定 800 DIP 的行程覆盖常见卡片高度；超出部分在窗口外，不可见。
                var sweep = new DoubleAnimation(-4, 800, TimeSpan.FromMilliseconds(durationMs))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                bandTransform.BeginAnimation(TranslateTransform.YProperty, sweep);
            }
            return;
        }

        _loopLayer.Visibility = Visibility.Visible;
        if (effect == "Pulse")
        {
            // 描边脉冲：只重绘细边框，比整卡填充便宜；粗细由「脉冲」设置控制。
            double thickness = Math.Max(1, Math.Min(8, style.LoopPulseThickness));
            _loopLayer.BorderBrush = brush;
            _loopLayer.BorderThickness = new Thickness(thickness);
        }
        else
        {
            _loopLayer.Background = brush;
        }

        var pulse = new DoubleAnimation(0.0, amplitude, TimeSpan.FromMilliseconds(durationMs / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        _loopLayer.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    void StartContourLoop(double styleSpeed)
    {
        if (_contourHost.Visibility != Visibility.Visible) return;

        // 原地形变：phase 只改变两套固定地形场的混合权重，不移动噪声坐标。
        // 速度 1.0 = 10 秒一个形变周期；2.0 约 5 秒，0.5 约 20 秒。
        _contourSpeed = Math.Max(0.1, Math.Min(4.0, styleSpeed));

        if (_contourTimer is null)
        {
            // 130ms（约 8fps）在等高线上会明显一顿一顿；30fps 配合后台几何生成，
            // 相位推进按真实经过时间计算，UI 偶发卡顿不会让动画越走越慢。
            _contourTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _contourTimer.Tick += (s, e) =>
            {
                double elapsed = _contourWatch.IsRunning ? _contourWatch.Elapsed.TotalMilliseconds : 33.0;
                _contourWatch.Restart();
                // 休眠 / 卡顿后不要一次跳过半个周期，最多按 100ms 推进。
                if (elapsed < 0) elapsed = 0;
                if (elapsed > 100) elapsed = 100;

                _contourPhase += (2.0 * Math.PI * elapsed / ContourBasePeriodMs) * _contourSpeed;
                if (_contourPhase > 2.0 * Math.PI) _contourPhase -= 2.0 * Math.PI;
                _contourLayer.SetPhase(_contourPhase);
            };
        }
        if (!_contourTimer.IsEnabled)
        {
            _contourWatch.Restart();
            _contourTimer.Start();
        }
    }

    /// <summary>1.0x 的等高线形变周期（毫秒）；与 StyleSettings.ContourPeriodSeconds 保持一致。</summary>
    const double ContourBasePeriodMs = 10000.0;

    void StopContourLoop()
    {
        if (_contourTimer is not null && _contourTimer.IsEnabled) _contourTimer.Stop();
        _contourWatch.Stop();
    }

    static DrawingBrush BuildDecorationBrush(string kind, Color color, double density)
    {
        double densityValue = Math.Max(0.0, Math.Min(2.0, density));
        double factor = densityValue <= 1.0
            ? 1.6 - 1.2 * densityValue
            : Math.Max(0.18, 0.4 / densityValue);
        double size;
        Drawing drawing;

        switch (kind)
        {
            case "Grid":
                size = 24 * factor;
                drawing = Stroked(new GeometryGroup
                {
                    Children =
                    {
                        new LineGeometry(new Point(0, 0), new Point(size, 0)),
                        new LineGeometry(new Point(0, 0), new Point(0, size))
                    }
                }, color);
                break;

            case "Scanline":
                size = 4 * factor;
                drawing = Stroked(new LineGeometry(new Point(0, 0), new Point(size, 0)), color);
                break;

            case "Hatch":
                size = 12 * factor;
                drawing = Stroked(new LineGeometry(new Point(0, size), new Point(size, 0)), color);
                break;

            case "Halftone":
                size = 8 * factor;
                var dot = new SolidColorBrush(color);
                dot.Freeze();
                drawing = new GeometryDrawing(dot, null,
                    new EllipseGeometry(new Point(size / 2, size / 2), 1.1, 1.1));
                break;

            default:
                return null;
        }

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    static Drawing Stroked(Geometry geometry, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        pen.Freeze();
        var drawing = new GeometryDrawing(null, pen, geometry);
        drawing.Freeze();
        return drawing;
    }

    static BitmapImage LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Warn($"加载背景图片失败: {ex.Message}");
            return null;
        }
    }

    // ==================== 毛玻璃 ====================

    /// <summary>设置玻璃底图（已经在 CPU 上模糊好的小图，直接铺满卡片）。</summary>
    public void SetGlass(ImageSource image)
    {
        if (image is null)
        {
            ClearGlass();
            return;
        }

        _glassLayer.Child = null;

        // G-19 / G-17：同一张取样图（GlassFeed 的复用 WriteableBitmap）不重建画刷 ——
        // 位图内容更新会由 WPF 自动重绘；换图（尺寸变化）时才换画刷。
        if (_glassBrush is null || !ReferenceEquals(_glassBrush.ImageSource, image))
        {
            var brush = new ImageBrush(image)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

            // G-17 修复：Freezable 是级联的——冻结 ImageBrush 会把作为子对象的
            // WriteableBitmap 一起冻结，下一帧 WritePixels 就会抛 “IsFrozen 必须为 false”。
            // 只有真正的不可变图源（背景图 BitmapImage 等）才冻结画刷。
            if (!(image is WriteableBitmap) && brush.CanFreeze) brush.Freeze();

            _glassBrush = brush;
            _glassLayer.Background = brush;

            // 取样图很小（几十像素），放大时用高质量插值，边缘不会出现方块。
            // 画刷复用时这条静态属性已经设过，不必每帧再设。
            RenderOptions.SetBitmapScalingMode(_glassLayer, BitmapScalingMode.HighQuality);
        }

        _glassLayer.Visibility = Visibility.Visible;
    }

    public void ClearGlass()
    {
        _glassBrush = null;
        _glassLayer.Background = null;
        _glassLayer.Child = null;
        _glassLayer.Visibility = Visibility.Collapsed;
    }

    public static Stretch ParseStretch(string value)
    {
        switch (Settings.NormalizeStretch(value))
        {
            case "Uniform": return Stretch.Uniform;
            case "Fill": return Stretch.Fill;
            case "None": return Stretch.None;
            default: return Stretch.UniformToFill;
        }
    }

    // ==================== 颜色 ====================

    public static TextAlignment ParseAlignment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TextAlignment.Left;
        switch (value.Trim().ToLowerInvariant())
        {
            case "center": return TextAlignment.Center;
            case "right": return TextAlignment.Right;
            case "justify": return TextAlignment.Justify;
            case "card":
            case "follow":
            case "followcard": return TextAlignment.Left;
            default: return TextAlignment.Left;
        }
    }

    public static SolidColorBrush GetBrush(string hex, double opacity)
    {
        string key = $"{(hex ?? "#000000")}|{opacity:0.00}";
        if (BrushCache.TryGetValue(key, out var cached)) return cached;

        Color color = ParseColor(hex);
        color.A = (byte)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    /// <summary>解析颜色；非法值返回白色（取色预览等“总能画出来”的场景用）。</summary>
    public static Color ParseColor(string hex) => ParseColor(hex, Colors.White);

    /// <summary>解析颜色；非法值返回 <paramref name="fallback"/>。唯一实现委托给 <see cref="ColorCode"/>。</summary>
    public static Color ParseColor(string hex, Color fallback)
    {
        if (!ColorCode.TryParse(hex, out var a, out var r, out var g, out var b)) return fallback;
        return Color.FromArgb(a, r, g, b);
    }
}
