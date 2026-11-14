using System;
using System.IO;
using MirrorpondHUD.Core;
using Xunit;

namespace MirrorpondHUD.Tests;

public sealed class ConfigStoreTests
{
    [Fact]
    public void IsolatedOverrideRedirectsSettingsPath()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "test-temp", Guid.NewGuid().ToString("N"));
        try
        {
            ConfigStore.SetDirectoryOverride(dir);

            Assert.Equal(Path.Combine(dir, "settings.json"), ConfigStore.Path);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            ConfigStore.ClearDirectoryOverride();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
