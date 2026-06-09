// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using System.Windows.Media;
using Blink.Core.Theming;

namespace Blink.App.Theming;

public enum LauncherTheme { Dark, Light }

/// <summary>
/// Publishes the design tokens as application-level brush resources, recomputed for a
/// value-based base color + accent. The base color's relative luminance picks the dark/light
/// (contrast-safe) text/hairline set; window surfaces are derived from the base color.
/// Controls bind every color via <c>{DynamicResource Blink.*}</c>, so a theme/accent change
/// is a single <see cref="Apply(string,string,string)"/> call. Keys mirror the CSS custom properties.
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

    // Default fallbacks (matching AppConfig defaults).
    private static readonly (byte r, byte g, byte b) DefaultDarkBase = (0x0B, 0x0D, 0x12);
    private static readonly (byte r, byte g, byte b) DefaultLightBase = (0xF6, 0xF7, 0xFA);
    private static readonly (byte r, byte g, byte b) DefaultAccent = (0x3B, 0x7F, 0xE3);

    public static LauncherTheme Current { get; private set; } = LauncherTheme.Dark;

    /// <summary>The last-applied accent hex (#RRGGBB), so <see cref="Toggle"/> preserves it.</summary>
    public static string AccentHex { get; private set; } = "#3B7FE3";

    /// <summary>The last-applied base hex (#RRGGBB) for the current theme.</summary>
    public static string BaseHex { get; private set; } = "#0B0D12";

    /// <summary>The last-applied theme mode (<c>"system"</c> or <c>"value"</c>).</summary>
    public static string ThemeMode { get; private set; } = "system";

    /// <summary>
    /// Apply a value-based theme. <paramref name="themeMode"/> is <c>"system"</c> (follow OS, base
    /// resolved to a dark/light default) or <c>"value"</c> (use <paramref name="baseHex"/>).
    /// The base color's luminance picks the dark/light token set; surfaces derive from the base.
    /// </summary>
    public static void Apply(string themeMode, string baseHex, string accentHex)
    {
        ThemeMode = themeMode;
        AccentHex = accentHex;

        // 1. Resolve effective base RGB.
        (byte r, byte g, byte b) bas;
        if (string.Equals(themeMode, "system", StringComparison.OrdinalIgnoreCase))
            bas = OsPrefersLight() ? DefaultLightBase : DefaultDarkBase;
        else if (!ColorMath.TryParseHex(baseHex, out bas))
            bas = DefaultDarkBase;
        BaseHex = ColorMath.ToHex(bas.r, bas.g, bas.b);

        // 2. Luminance decision → dark/light token set.
        bool dark = ColorMath.RelativeLuminance(bas.r, bas.g, bas.b) < 0.45;
        Current = dark ? LauncherTheme.Dark : LauncherTheme.Light;

        // 3. Parse accent (fallback to default blue).
        if (!ColorMath.TryParseHex(accentHex, out var acc))
            acc = DefaultAccent;

        var r = Application.Current.Resources;

        // 4a. Accent tokens (derived from acc, both themes).
        r[Accent] = Brush(acc);
        r[AccentSoft] = Brush(acc, 0.16);
        r[AccentLine] = Brush(acc, 0.50);
        r[RowSel] = Brush(acc, dark ? 0.20 : 0.16);
        r[Mark] = Brush(acc, dark ? 0.30 : 0.22);
        r["Blink.AccentColor"] = Color.FromRgb(acc.r, acc.g, acc.b);

        // 4b. Text / hairline / rowhover / tileline — fixed per-theme (contrast-safe), copied
        // from the original dark/light branches so contrast holds regardless of base color.
        (byte, byte, byte) white = (255, 255, 255);
        (byte, byte, byte) black = (0, 0, 0);

        if (dark)
        {
            r[Hairline] = Oklch.ToBrush(0.99, 0, 0, 0.09);
            r[HairlineStrong] = Oklch.ToBrush(0.99, 0, 0, 0.14);
            r[Txt] = Oklch.ToBrush(0.96, 0.004, 255);
            r[TxtDim] = Oklch.ToBrush(0.74, 0.008, 255);
            r[TxtFaint] = Oklch.ToBrush(0.58, 0.008, 255);
            r[RowHover] = Oklch.ToBrush(0.99, 0, 0, 0.05);
            r[TileLine] = Oklch.ToBrush(0.99, 0, 0, 0.10);

            // Surfaces derived from base.
            r[Surface] = Brush(bas);
            r[Surface2] = Brush(ColorMath.Mix(bas, white, 0.06));
            r[Inset] = Brush(ColorMath.Mix(bas, black, 0.25));
            r[Titlebar] = Brush(ColorMath.Mix(bas, white, 0.03));
            r[Tile] = Brush(ColorMath.Mix(bas, white, 0.12));
            r[BgGlass] = Brush(ColorMath.Mix(bas, white, 0.05));
            r[BgGlass2] = Brush(ColorMath.Mix(bas, white, 0.08));
        }
        else
        {
            r[Hairline] = Oklch.ToBrush(0.20, 0.02, 255, 0.10);
            r[HairlineStrong] = Oklch.ToBrush(0.20, 0.02, 255, 0.16);
            r[Txt] = Oklch.ToBrush(0.26, 0.02, 260);
            r[TxtDim] = Oklch.ToBrush(0.46, 0.015, 260);
            r[TxtFaint] = Oklch.ToBrush(0.60, 0.012, 260);
            r[RowHover] = Oklch.ToBrush(0.50, 0.04, 260, 0.06);
            r[TileLine] = Oklch.ToBrush(0.20, 0.02, 255, 0.10);

            // Surfaces derived from base.
            r[Surface] = Brush(bas);
            r[Surface2] = Brush(ColorMath.Mix(bas, black, 0.03));
            r[Inset] = Brush(ColorMath.Mix(bas, white, 0.50));
            r[Titlebar] = Brush(ColorMath.Mix(bas, white, 0.40));
            r[Tile] = Brush(ColorMath.Mix(bas, white, 0.70));
            r[BgGlass] = Brush(ColorMath.Mix(bas, white, 0.30));
            r[BgGlass2] = Brush(ColorMath.Mix(bas, white, 0.20));
        }
    }

    /// <summary>Frozen opaque/alpha brush from an sRGB tuple.</summary>
    private static SolidColorBrush Brush((byte r, byte g, byte b) c, double a = 1.0)
    {
        var brush = new SolidColorBrush(
            Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.r, c.g, c.b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Tray "테마 전환": flip between a dark and light preset base, preserving the current accent.
    /// </summary>
    public static void Toggle() =>
        Apply("value", Current == LauncherTheme.Light ? "#0B0D12" : "#F6F7FA", AccentHex);

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
