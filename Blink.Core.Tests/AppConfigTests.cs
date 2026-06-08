using Blink.Core.Config;

namespace Blink.Core.Tests;

public sealed class AppConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"blink-cfg-{Guid.NewGuid():N}");

    [Fact]
    public void Load_OnEmptyDir_ReturnsDefaults_WithDbPathUnderDir()
    {
        var cfg = AppConfig.Load(_dir);
        Assert.Empty(cfg.Folders);
        Assert.Equal(Path.Combine(_dir, "index.db"), cfg.DbPath);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var cfg = new AppConfig
        {
            Folders = new[] { @"C:\docs", "/home/user/notes" },
            DbPath = Path.Combine(_dir, "custom.db"),
            Autostart = true,
        };
        cfg.Save(_dir);

        Assert.True(File.Exists(Path.Combine(_dir, "config.json")));

        var loaded = AppConfig.Load(_dir);
        Assert.Equal(cfg.Folders, loaded.Folders);
        Assert.Equal(cfg.DbPath, loaded.DbPath);
        Assert.True(loaded.Autostart);
    }

    [Fact]
    public void Autostart_DefaultsFalse()
    {
        Assert.False(AppConfig.Load(_dir).Autostart);
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        var nested = Path.Combine(_dir, "nested");
        Assert.False(Directory.Exists(nested));
        new AppConfig().Save(nested);
        Assert.True(File.Exists(Path.Combine(nested, "config.json")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
