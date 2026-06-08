using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blink.Core.Config;

/// <summary>
/// Persisted app configuration stored as <c>config.json</c> under
/// <c>%APPDATA%\Blink\</c> (cross-platform: <see cref="Environment.SpecialFolder.ApplicationData"/>).
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("folders")]
    public string[] Folders { get; set; } = Array.Empty<string>();

    [JsonPropertyName("db_path")]
    public string DbPath { get; set; } = "";

    /// <summary>
    /// Whether Blink registers itself to launch at Windows sign-in. Persisted here; the
    /// actual HKCU\…\Run registration is applied by the Windows-only app layer.
    /// </summary>
    [JsonPropertyName("autostart")]
    public bool Autostart { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Default config directory: <c>%APPDATA%\Blink</c> (or platform equivalent).</summary>
    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blink");

    private static string ConfigPath(string dir) => Path.Combine(dir, "config.json");

    /// <summary>Load config from <paramref name="dir"/> (defaults to <see cref="DefaultDir"/>); returns defaults if absent.</summary>
    public static AppConfig Load(string? dir = null)
    {
        dir ??= DefaultDir;
        var path = ConfigPath(dir);
        AppConfig cfg;
        if (File.Exists(path))
        {
            cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOpts) ?? new AppConfig();
        }
        else
        {
            cfg = new AppConfig();
        }

        if (string.IsNullOrEmpty(cfg.DbPath))
            cfg.DbPath = Path.Combine(dir, "index.db");
        return cfg;
    }

    /// <summary>Save config to <paramref name="dir"/> (defaults to <see cref="DefaultDir"/>), creating the directory if needed.</summary>
    public void Save(string? dir = null)
    {
        dir ??= DefaultDir;
        Directory.CreateDirectory(dir);
        if (string.IsNullOrEmpty(DbPath))
            DbPath = Path.Combine(dir, "index.db");
        File.WriteAllText(ConfigPath(dir), JsonSerializer.Serialize(this, JsonOpts));
    }
}
