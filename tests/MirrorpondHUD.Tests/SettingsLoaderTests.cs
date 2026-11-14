using MirrorpondHUD.Core;
using Xunit;

namespace MirrorpondHUD.Tests;

public sealed class SettingsLoaderTests
{
    [Fact]
    public void FutureVersionIsReadOnly()
    {
        Settings settings = SettingsLoader.Load(
            "{\"SchemaVersion\":999,\"Cards\":[]}",
            out bool needsSave, out bool corrupted, out bool future);

        Assert.NotNull(settings);
        Assert.True(future);
        Assert.False(corrupted);
        Assert.False(needsSave);
    }

    [Fact]
    public void CaseInsensitiveVersionDoesNotDowngrade()
    {
        Settings settings = SettingsLoader.Load(
            "{\"schemaversion\":999,\"Cards\":[]}",
            out bool needsSave, out bool corrupted, out bool future);

        Assert.NotNull(settings);
        Assert.True(future);
        Assert.False(corrupted);
        Assert.False(needsSave);
    }

    [Fact]
    public void UnsupportedOldSchemaIsCorrupted()
    {
        Settings settings = SettingsLoader.Load(
            "{\"SchemaVersion\":1,\"Cards\":[]}",
            out bool needsSave, out bool corrupted);

        Assert.NotNull(settings);
        Assert.True(corrupted);
        Assert.False(needsSave);
    }
}
