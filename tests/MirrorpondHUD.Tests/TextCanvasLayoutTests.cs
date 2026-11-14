using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MirrorpondHUD.Ui;
using Xunit;

namespace MirrorpondHUD.Tests;

/// <summary>
/// 2.46.3：信息卡「显示不全」回归。
///
/// 现象：启动动画结束后，首张信息卡（系统信息）只剩一条 29 DIP 宽的细缝，
/// 内容被卡片轮廓硬切，每行只看得见第一个图标。
///
/// 根因：<see cref="OverlaySurface.SetLayoutText"/> 把 null 也送进 NormalizeNewlines，
/// 空串不是 null，于是 <c>_layoutText ?? _text</c> 一直取空串——测量按空文本算（宽 1），
/// 绘制却照旧画完整文本，超出的部分被窗口边缘切掉。
/// </summary>
public sealed class TextCanvasLayoutTests
{
    /// <summary>WPF 元素有线程亲和性，测试统一在 STA 线程里跑。</summary>
    static void RunSta(Action action)
    {
        ExceptionDispatchInfo error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void EmptyLayoutTextMustNotCollapseWidth() =>
        RunSta(() =>
        {
            var canvas = new TextCanvas
            {
                FontSize = 15,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Text = "CPU 100% RAM 100% GPU 100%\nNET 1.2M/s DISK 40%"
            };
            canvas.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double natural = canvas.DesiredSize.Width;
            Assert.True(natural > 60, $"自然宽度不可信，断言没有意义: {natural}");

            // 空串 = 「没有布局文本」，必须回落到显示文本，而不是按空文本定宽。
            canvas.ResetStableSize();
            canvas.LayoutText = "";
            canvas.Measure(new Size(double.PositiveInfinity, 500));
            canvas.Measure(new Size(double.PositiveInfinity, 501));

            Assert.True(canvas.DesiredSize.Width > natural * 0.9,
                $"布局文本为空串时宽度塌陷: {canvas.DesiredSize.Width:0.##} < 自然宽 {natural:0.##}");
        });

    [Fact]
    public void SetLayoutTextNullFollowsDisplayedText() =>
        RunSta(() =>
        {
            var surface = new OverlaySurface();
            surface.SetText("正文", false);

            surface.SetLayoutText("占位布局文本");
            Assert.Equal("占位布局文本", surface.Canvas.LayoutText);

            // 契约：null = 跟随显示内容。经过 Surface 之后必须仍然是 null。
            surface.SetLayoutText(null);
            Assert.Null(surface.Canvas.LayoutText);
        });

    [Theory]
    [InlineData(300.0, 29.0, true)]
    [InlineData(300.0, 0.0, false)]
    [InlineData(300.0, 0.4, false)]
    [InlineData(300.0, 299.8, false)]
    [InlineData(29.0, 300.0, false)]
    public void OverflowsWindowNeedsTrustedRenderedWidth(double block, double rendered, bool expected) =>
        Assert.Equal(expected, TextCanvas.OverflowsWindow(block, rendered));
}
