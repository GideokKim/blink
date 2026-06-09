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
    public void AutoIndexInterval_DefaultsToHourly()
    {
        Assert.Equal("1h", AppConfig.Load(_dir).AutoIndexInterval);
    }

    [Fact]
    public void AutoIndexInterval_RoundTrips()
    {
        new AppConfig { AutoIndexInterval = "15m" }.Save(_dir);
        Assert.Equal("15m", AppConfig.Load(_dir).AutoIndexInterval);
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        var nested = Path.Combine(_dir, "nested");
        Assert.False(Directory.Exists(nested));
        new AppConfig().Save(nested);
        Assert.True(File.Exists(Path.Combine(nested, "config.json")));
    }

    // ── New fields: defaults ──────────────────────────────────────────────────

    [Fact]
    public void NewFields_DefaultValues()
    {
        var cfg = new AppConfig();
        Assert.Equal("system", cfg.ThemeMode);
        Assert.Equal("#0B0D12", cfg.BaseColor);
        Assert.Equal("#3B7FE3", cfg.Accent);
        Assert.Empty(cfg.FolderIndexTimes);
    }

    // ── New fields: round-trip ────────────────────────────────────────────────

    [Fact]
    public void NewFields_RoundTrip()
    {
        var dir2 = Path.Combine(_dir, "rt");
        var cfg = new AppConfig
        {
            ThemeMode = "value",
            BaseColor = "#14161C",
            Accent = "#4F46E5",
            FolderIndexTimes = new Dictionary<string, string>
            {
                { @"C:\docs", "2026-06-01T12:00:00.0000000Z" },
                { "/home/user/notes", "2026-06-02T08:30:00.0000000Z" },
            },
        };
        cfg.Save(dir2);

        var loaded = AppConfig.Load(dir2);
        Assert.Equal("value", loaded.ThemeMode);
        Assert.Equal("#14161C", loaded.BaseColor);
        Assert.Equal("#4F46E5", loaded.Accent);
        Assert.Equal(2, loaded.FolderIndexTimes.Count);
        Assert.Equal("2026-06-01T12:00:00.0000000Z", loaded.FolderIndexTimes[@"C:\docs"]);
        Assert.Equal("2026-06-02T08:30:00.0000000Z", loaded.FolderIndexTimes["/home/user/notes"]);
    }

    // ── Migration: legacy dark ────────────────────────────────────────────────

    [Fact]
    public void Migration_LegacyDark_SetsValueModeAndDarkBase()
    {
        var dir2 = Path.Combine(_dir, "migDark");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "config.json"), """
            {
              "folders": ["C:\\docs"],
              "theme": "dark",
              "accent_hue": 250
            }
            """);

        var cfg = AppConfig.Load(dir2);
        Assert.Equal("value", cfg.ThemeMode);
        Assert.Equal("#0B0D12", cfg.BaseColor);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", cfg.Accent);
    }

    // ── Migration: legacy light ───────────────────────────────────────────────

    [Fact]
    public void Migration_LegacyLight_SetsValueModeAndLightBase()
    {
        var dir2 = Path.Combine(_dir, "migLight");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "config.json"), """
            {
              "folders": [],
              "theme": "light",
              "accent_hue": 220
            }
            """);

        var cfg = AppConfig.Load(dir2);
        Assert.Equal("value", cfg.ThemeMode);
        Assert.Equal("#F6F7FA", cfg.BaseColor);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", cfg.Accent);
    }

    // ── Migration: legacy system ──────────────────────────────────────────────

    [Fact]
    public void Migration_LegacySystem_SetsSystemMode()
    {
        var dir2 = Path.Combine(_dir, "migSystem");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "config.json"), """
            {
              "folders": [],
              "theme": "system",
              "accent_hue": 250
            }
            """);

        var cfg = AppConfig.Load(dir2);
        Assert.Equal("system", cfg.ThemeMode);
    }

    // ── Migration: accent hue 250 produces a valid hex ────────────────────────

    [Fact]
    public void Migration_AccentHue250_ProducesValidHex()
    {
        var dir2 = Path.Combine(_dir, "migAccent");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "config.json"), """
            {
              "theme": "dark",
              "accent_hue": 250
            }
            """);

        var cfg = AppConfig.Load(dir2);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", cfg.Accent);
    }

    // ── Migration: new keys already present are NOT overwritten ──────────────

    [Fact]
    public void Migration_ExistingThemeMode_NotOverwritten()
    {
        var dir2 = Path.Combine(_dir, "migExisting");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "config.json"), """
            {
              "theme": "dark",
              "accent_hue": 250,
              "theme_mode": "system",
              "base_color": "#14161C",
              "accent": "#06B6D4"
            }
            """);

        var cfg = AppConfig.Load(dir2);
        Assert.Equal("system", cfg.ThemeMode);
        Assert.Equal("#14161C", cfg.BaseColor);
        Assert.Equal("#06B6D4", cfg.Accent);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
}
