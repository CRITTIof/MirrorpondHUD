using System.Collections.Generic;
using MirrorpondHUD.Core;
using Xunit;

namespace MirrorpondHUD.Tests;

public sealed class TemplateEngineTests
{
    [Theory]
    [InlineData("{n,-2147483648}")]
    [InlineData("{n,2147483647}")]
    public void ExtremeAlignmentIsClamped(string template)
    {
        var variables = new Dictionary<string, object> { ["n"] = 5 };
        string text = TemplateEngine.Render(template, variables);

        Assert.Equal(1024, text.Length);
    }

    [Fact]
    public void UnknownPlaceholderIsKeptAndReported()
    {
        var variables = new Dictionary<string, object> { ["cpu"] = 23.44 };
        string text = TemplateEngine.Render("CPU {cpu:0.0}% {nope}", variables, out List<string> unknown);

        Assert.Equal("CPU 23.4% {nope}", text);
        Assert.Single(unknown);
        Assert.Equal("nope", unknown[0]);
    }
}
