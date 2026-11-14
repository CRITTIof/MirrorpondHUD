using System.Windows.Media;
using MirrorpondHUD.Core;
using MirrorpondHUD.Ui;
using MirrorpondHUD.Ui.Controls;
using Xunit;

namespace MirrorpondHUD.Tests;

/// <summary>2.47.0：进度条颜色令牌与时长读数共用的回归。</summary>
public sealed class CardProgressTests
{
    [Theory]
    [InlineData(0.0, "00:00")]
    [InlineData(65.4, "01:05")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "00:00")]
    [InlineData(double.NaN, "00:00")]
    public void ClockKeepsHourDigits(double seconds, string expected) =>
        Assert.Equal(expected, CardFormat.Clock(seconds));

    [Fact]
    public void ProgressColorInvalidFallsBackToFollowAccent()
    {
        var settings = new Settings();
        settings.Style.Progress = "#12";
        settings.Normalize();

        Assert.Equal("", settings.Style.Progress);

        ThemeTokens tokens = ThemeResolver.Resolve(settings.Style);
        Assert.False(tokens.ProgressExplicit);
        Assert.Equal(tokens.Accent, tokens.Progress);
    }

    [Fact]
    public void ProgressColorExplicitOverridesAccent()
    {
        var settings = new Settings();
        settings.Style.Accent = "#123456";
        settings.Style.Progress = "#80ABCDEF";
        settings.Normalize();

        ThemeTokens tokens = ThemeResolver.Resolve(settings.Style);
        Assert.True(tokens.ProgressExplicit);
        Assert.Equal(Color.FromArgb(0x80, 0xAB, 0xCD, 0xEF), tokens.Progress);
    }
}
