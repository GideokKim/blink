// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using System.Windows.Media;

namespace Blink.App.Theming;

public enum LauncherTheme { Dark, Light }

/// <summary>
/// Publishes the design tokens as application-level brush resources, recomputed from
/// <c>styles.css</c>'s oklch values for the current theme and accent hue.
/// Controls bind every color via <c>{DynamicResource Blink.*}</c>, so a theme/accent change
/// is a single <see cref="Apply"/> call. Keys mirror the CSS custom properties.
/// </summary>
internal static class ThemeManager
{
    // Resource keys (referenced from XAML as {DynamicResource Blink.Txt} etc.)
    public const string Accent = "Blink.Accent";
    public const string AccentSoft = "Blink.AccentSoft";
    public const string AccentLine = "Blink.AccentLine";
    public const string BgGlass = "Blink.BgGlass";
    public const string BgGlass2 = "Blink.BgGlass2";
    public const string Hairline = "Blink.Hairline";
    public const string HairlineStrong = "Blink.HairlineStrong";
    public const string Txt = "Blink.Txt";
    public const string TxtDim = "Blink.TxtDim";
    public const string TxtFaint = "Blink.TxtFaint";
    public const string RowHover = "Blink.RowHover";
    public const string RowSel = "Blink.RowSel";
    public const string Tile = "Blink.Tile";
    public const string TileLine = "Blink.TileLine";
    public const string Mark = "Blink.Mark";
    // Settings-window surfaces (opaque window, unlike the launcher's glass). Shared so both
    // windows theme from one place. Match settings/Blink Settings.html tokens.
    public const string Surface = "Blink.Surface";    // opaque window body
    public const string Surface2 = "Blink.Surface2";  // footer fill
    public const string Inset = "Blink.Inset";        // lists / fields / inset controls
    public const string Titlebar = "Blink.Titlebar";  // custom title bar fill

    // Accent is fixed lightness/chroma; only hue varies (cool 220–280).
    private const double AccentL = 0.64;
    private const double AccentC = 0.155;

    public static LauncherTheme Current { get; private set; } = LauncherTheme.Dark;
    public static double AccentHue { get; private set; } = 250;

    public static void Apply(LauncherTheme theme, double accentHue = 250)
    {
        Current = theme;
        AccentHue = accentHue;

        var r = Application.Current.Resources;

        // Accent (hue-tweakable, both themes)
        r[Accent] = Oklch.ToBrush(AccentL, AccentC, accentHue);
        r[AccentSoft] = Oklch.ToBrush(AccentL, AccentC, accentHue, 0.16);
        r[AccentLine] = Oklch.ToBrush(AccentL, AccentC, accentHue, 0.55);
        // Solid accent color (for glows) too.
        r["Blink.AccentColor"] = Oklch.ToColor(AccentL, AccentC, accentHue);

        if (theme == LauncherTheme.Dark)
        {
            r[BgGlass] = Oklch.ToBrush(0.20, 0.013, 255); // opaque solid panel
            r[BgGlass2] = Oklch.ToBrush(0.235, 0.014, 255, 0.55);
            r[Hairline] = Oklch.ToBrush(0.99, 0, 0, 0.09);
            r[HairlineStrong] = Oklch.ToBrush(0.99, 0, 0, 0.14);
            r[Txt] = Oklch.ToBrush(0.96, 0.004, 255);
            r[TxtDim] = Oklch.ToBrush(0.74, 0.008, 255);
            r[TxtFaint] = Oklch.ToBrush(0.58, 0.008, 255);
            r[RowHover] = Oklch.ToBrush(0.99, 0, 0, 0.05);
            r[RowSel] = Oklch.ToBrush(AccentL, AccentC, accentHue, 0.20);
            r[Tile] = Oklch.ToBrush(0.30, 0.012, 255, 0.70);
            r[TileLine] = Oklch.ToBrush(0.99, 0, 0, 0.10);
            r[Mark] = Oklch.ToBrush(AccentL, AccentC, accentHue, 0.30);
            r[Surface] = Rgb(0x15, 0x18, 0x1F);
            r[Surface2] = Oklch.ToBrush(0.22, 0.012, 255, 0.5);
            r[Inset] = Oklch.ToBrush(0.16, 0.01, 255, 0.6);
            r[Titlebar] = Oklch.ToBrush(0.18, 0.011, 255, 0.85);
        }
        else
        {
            r[BgGlass] = Oklch.ToBrush(0.985, 0.003, 255); // opaque solid panel
            r[BgGlass2] = Oklch.ToBrush(0.96, 0.004, 255, 0.60);
            r[Hairline] = Oklch.ToBrush(0.20, 0.02, 255, 0.10);
            r[HairlineStrong] = Oklch.ToBrush(0.20, 0.02, 255, 0.16);
            r[Txt] = Oklch.ToBrush(0.26, 0.02, 260);
            r[TxtDim] = Oklch.ToBrush(0.46, 0.015, 260);
            r[TxtFaint] = Oklch.ToBrush(0.60, 0.012, 260);
            r[RowHover] = Oklch.ToBrush(0.50, 0.04, 260, 0.06);
            r[RowSel] = Oklch.ToBrush(AccentL, AccentC, accentHue, 0.16);
            r[Tile] = Oklch.ToBrush(1, 0, 0, 0.75);
            r[TileLine] = Oklch.ToBrush(0.20, 0.02, 255, 0.10);
            r[Mark] = Oklch.ToBrush(AccentL, AccentC, accentHue, 0.22);
            r[Surface] = Rgb(0xF6, 0xF7, 0xFA);
            r[Surface2] = Oklch.ToBrush(0.97, 0.003, 255, 0.7);
            r[Inset] = Oklch.ToBrush(1, 0, 0, 0.6);
            r[Titlebar] = Oklch.ToBrush(0.99, 0.002, 255, 0.9);
        }
    }

    /// <summary>Opaque brush from sRGB bytes (for the design's hex surface tokens).</summary>
    private static SolidColorBrush Rgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static void Toggle() =>
        Apply(Current == LauncherTheme.Dark ? LauncherTheme.Light : LauncherTheme.Dark, AccentHue);

    /// <summary>
    /// Resolve a stored theme preference (<c>"dark"</c>/<c>"light"</c>/<c>"system"</c>) to a concrete
    /// theme. <c>"system"</c> (or anything unknown) follows the Windows apps theme.
    /// </summary>
    public static LauncherTheme Resolve(string? theme)
    {
        if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)) return LauncherTheme.Light;
        if (string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)) return LauncherTheme.Dark;
        return OsPrefersLight() ? LauncherTheme.Light : LauncherTheme.Dark;
    }

    /// <summary>Reads the Windows "apps use light theme" preference; false (dark) if unavailable.</summary>
    private static bool OsPrefersLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    /// <summary>The per-category tile glyph tint: <c>oklch(0.8 0.09 hue)</c>.</summary>
    public static SolidColorBrush TileGlyph(int hue) => Oklch.ToBrush(0.8, 0.09, hue);
}
