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

    /// <summary>Launcher layout: <c>"A"</c> (Classic, single column) or <c>"B"</c> (Dual Panel). Default A.</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "A";

    /// <summary>Legacy theme key retained for one-time migration. Use <see cref="ThemeMode"/> going forward.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    /// <summary>Legacy accent hue retained for one-time migration. Use <see cref="Accent"/> going forward.</summary>
    [JsonPropertyName("accent_hue")]
    public int AccentHue { get; set; } = 250;

    /// <summary>
    /// Auto-index cadence key: <c>"15m"</c>, <c>"1h"</c> (default), <c>"6h"</c>, or <c>"off"</c> (manual only).
    /// Mapped to a real period by <see cref="Blink.Core.Indexing.AutoIndexInterval"/>.
    /// </summary>
    [JsonPropertyName("auto_index_interval")]
    public string AutoIndexInterval { get; set; } = Indexing.AutoIndexInterval.Default;

    /// <summary>Theme mode: <c>"system"</c> (follow OS) or <c>"value"</c> (use <see cref="BaseColor"/>).</summary>
    [JsonPropertyName("theme_mode")]
    public string ThemeMode { get; set; } = "system";

    /// <summary>Window background hex color; used when <see cref="ThemeMode"/> is <c>"value"</c>.</summary>
    [JsonPropertyName("base_color")]
    public string BaseColor { get; set; } = "#0B0D12";

    /// <summary>Accent color hex (e.g. <c>"#3B7FE3"</c>).</summary>
    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#3B7FE3";

    /// <summary>Per-folder path → ISO-8601 UTC timestamp of last successful index.</summary>
    [JsonPropertyName("folder_index_times")]
    public Dictionary<string, string> FolderIndexTimes { get; set; } = new();

    /// <summary>Whether the app checks GitHub Releases for updates automatically (default on).</summary>
    [JsonPropertyName("update_check")]
    public bool UpdateCheck { get; set; } = true;

    /// <summary>Version the user chose to skip (e.g. "1.2.0"); newer releases still notify.</summary>
    [JsonPropertyName("skip_version")]
    public string SkipVersion { get; set; } = "";

    /// <summary>Last version whose "새로워진 점" was shown (or initialized); drives WhatsNewGate.</summary>
    [JsonPropertyName("last_seen_version")]
    public string LastSeenVersion { get; set; } = "";

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
        string rawJson = "";
        if (File.Exists(path))
        {
            rawJson = File.ReadAllText(path);
            cfg = JsonSerializer.Deserialize<AppConfig>(rawJson, JsonOpts) ?? new AppConfig();
        }
        else
        {
            cfg = new AppConfig();
        }

        if (string.IsNullOrEmpty(cfg.DbPath))
            cfg.DbPath = Path.Combine(dir, "index.db");

        cfg.Migrate(rawJson);
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

    /// <summary>
    /// One-time migration from legacy <c>theme</c>/<c>accent_hue</c> fields to
    /// <c>theme_mode</c>/<c>base_color</c>/<c>accent</c> when the new keys are absent in the
    /// raw JSON (i.e. config was saved by an older build).
    /// </summary>
    private void Migrate(string rawJson)
    {
        if (!rawJson.Contains("\"theme_mode\""))
        {
            switch (Theme)
            {
                case "dark":
                    ThemeMode = "value";
                    BaseColor = "#0B0D12";
                    break;
                case "light":
                    ThemeMode = "value";
                    BaseColor = "#F6F7FA";
                    break;
                default:
                    ThemeMode = "system";
                    break;
            }
        }

        if (!rawJson.Contains("\"accent\""))
        {
            Accent = AccentHueToHex(AccentHue);
        }
    }

    /// <summary>
    /// Converts an Oklch accent hue (fixed L=0.64, C=0.155) to a #RRGGBB hex string.
    /// Replicates <c>Blink.App.Theming.Oklch.ToColor</c> math without WPF dependencies.
    /// Delegates to <c>Blink.Core.Theming.ColorMath</c> when available; falls back to the
    /// inline implementation so <c>Blink.Core</c> compiles standalone.
    /// </summary>
    internal static string AccentHueToHex(int hueDeg)
    {
        var (r, g, b) = OklchToRgb(0.64, 0.155, hueDeg);
        return ToHex(r, g, b);
    }

    private static (byte r, byte g, byte b) OklchToRgb(double l, double c, double hDeg)
    {
        double h = hDeg * Math.PI / 180.0;
        double a = c * Math.Cos(h);
        double b = c * Math.Sin(h);

        double l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double L3 = l_ * l_ * l_, M3 = m_ * m_ * m_, S3 = s_ * s_ * s_;

        double r = 4.0767416621 * L3 - 3.3077115913 * M3 + 0.2309699292 * S3;
        double g = -1.2684380046 * L3 + 2.6097574011 * M3 - 0.3413193965 * S3;
        double bl = -0.0041960863 * L3 - 0.7034186147 * M3 + 1.7076147010 * S3;

        return (LinearToByte(r), LinearToByte(g), LinearToByte(bl));
    }

    private static byte LinearToByte(double c)
    {
        double s = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(s, 0, 1) * 255);
    }

    private static string ToHex(byte r, byte g, byte b) =>
        $"#{r:X2}{g:X2}{b:X2}";
}
