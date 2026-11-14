using System;
using System.IO;
using MirrorpondHUD.Core;
using Xunit;

namespace MirrorpondHUD.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void WriteReplacesFileAndKeepsPreviousVersionAsBackup()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "test-temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "old");

            AtomicFile.WriteResult result = AtomicFile.Write(path, "new", createBackup: true);

            Assert.True(result.Success);
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Equal("old", File.ReadAllText(path + ".bak"));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void MissingDirectoryIsCreated()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "test-temp", Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, "nested", "a.json");
            AtomicFile.WriteResult result = AtomicFile.Write(path, "x", createBackup: false);

            Assert.True(result.Success);
            Assert.Equal("x", File.ReadAllText(path));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
