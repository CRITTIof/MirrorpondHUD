using System.Collections.Generic;
using MirrorpondHUD.Core;
using Xunit;

namespace MirrorpondHUD.Tests;

public sealed class JsonSettingsTests
{
    [Fact]
    public void UnknownKeysDoNotThrowAndDoNotLoseKnownFields()
    {
        Settings settings = Json.Deserialize<Settings>(
            "{\"SchemaVersion\":7,\"UnknownKey\":123,\"Cards\":[]}");

        Assert.NotNull(settings);
        Assert.NotNull(settings.Cards);
        Assert.Equal(7, settings.SchemaVersion);
    }

    [Fact]
    public void RoundTripPreservesCardAndExtras()
    {
        var source = new Settings();
        var card = new CardSettings
        {
            Id = "tests-card",
            Kind = "infotext",
            Name = "测试卡",
            Content = "{time}",
            Spacing = 16
        };
        card.Extras["command"] = new Dictionary<string, object>
        {
            { "Shell", "cmd" },
            { "TimeoutSeconds", 20 }
        };
        source.Cards.Add(card);

        Settings back = Json.Deserialize<Settings>(Json.Serialize(source));

        Assert.NotNull(back);
        Assert.Single(back.Cards);
        Assert.Equal("tests-card", back.Cards[0].Id);
        Assert.Equal("{time}", back.Cards[0].Content);
        Assert.True(back.Cards[0].Extras.ContainsKey("command"));
    }

    [Fact]
    public void NormalizeClampsOutOfRangeNumbers()
    {
        var settings = new Settings
        {
            Style = new StyleSettings { FontSize = 999, BackgroundOpacity = 5, BlurFps = 999 }
        };

        settings.Normalize();

        Assert.Equal(96, settings.Style.FontSize);
        Assert.Equal(1, settings.Style.BackgroundOpacity);
        Assert.Equal(30, settings.Style.BlurFps);
    }
}
