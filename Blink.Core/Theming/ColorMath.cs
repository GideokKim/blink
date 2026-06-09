namespace Blink.Core.Theming;

/// <summary>
/// Pure sRGB/HSL/Oklch color math with no WPF/System.Windows dependency.
/// All methods are thread-safe (static, no state).
/// </summary>
public static class ColorMath
{
    // -------------------------------------------------------------------------
    // Hex parsing / formatting
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a hex color string.  Accepts "#RGB", "#RRGGBB" and the same
    /// without the leading '#'.  Case-insensitive.  Whitespace is trimmed.
    /// Anything else returns false and sets <paramref name="rgb"/> to (0,0,0).
    /// </summary>
    public static bool TryParseHex(string s, out (byte r, byte g, byte b) rgb)
    {
        rgb = (0, 0, 0);
        if (s is null) return false;

        s = s.Trim();
        if (s.StartsWith('#')) s = s[1..];

        if (s.Length == 3)
        {
            // Expand shorthand: "ABC" → "AABBCC"
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        }

        if (s.Length != 6) return false;

        if (!byte.TryParse(s[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte r)) return false;
        if (!byte.TryParse(s[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g)) return false;
        if (!byte.TryParse(s[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b)) return false;

        rgb = (r, g, b);
        return true;
    }

    /// <summary>Returns "#RRGGBB" (uppercase).</summary>
    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    // -------------------------------------------------------------------------
    // Relative luminance (WCAG 2.x)
    // -------------------------------------------------------------------------

    /// <summary>
    /// WCAG 2.x relative luminance in the range 0..1.
    /// sRGB linearisation: channel/255; if c &gt; 0.04045 → ((c+0.055)/1.055)^2.4 else c/12.92.
    /// Weights: 0.2126 R + 0.7152 G + 0.0722 B.
    /// </summary>
    public static double RelativeLuminance(byte r, byte g, byte b)
    {
        static double Lin(byte v)
        {
            double c = v / 255.0;
            return c > 0.04045 ? Math.Pow((c + 0.055) / 1.055, 2.4) : c / 12.92;
        }
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    // -------------------------------------------------------------------------
    // HSL conversion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts sRGB to HSL.  H is in degrees [0, 360), S and L are in [0, 1].
    /// </summary>
    public static (double h, double s, double l) ToHsl(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;

        double l = (max + min) / 2.0;
        double s = 0.0;
        if (delta > 1e-10)
            s = delta / (1.0 - Math.Abs(2.0 * l - 1.0));

        double h = 0.0;
        if (delta > 1e-10)
        {
            if (max == rf)
                h = 60.0 * (((gf - bf) / delta) % 6.0);
            else if (max == gf)
                h = 60.0 * ((bf - rf) / delta + 2.0);
            else
                h = 60.0 * ((rf - gf) / delta + 4.0);
        }

        if (h < 0.0) h += 360.0;

        return (h, s, l);
    }

    /// <summary>
    /// Converts HSL back to sRGB.  H is in degrees, S and L are in [0, 1].
    /// Inputs are clamped to valid ranges.
    /// </summary>
    public static (byte r, byte g, byte b) FromHsl(double h, double s, double l)
    {
        s = Math.Clamp(s, 0.0, 1.0);
        l = Math.Clamp(l, 0.0, 1.0);
        h = ((h % 360.0) + 360.0) % 360.0;   // normalise to [0, 360)

        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = l - c / 2.0;

        double rf, gf, bf;
        if (h < 60)      { rf = c; gf = x; bf = 0; }
        else if (h < 120){ rf = x; gf = c; bf = 0; }
        else if (h < 180){ rf = 0; gf = c; bf = x; }
        else if (h < 240){ rf = 0; gf = x; bf = c; }
        else if (h < 300){ rf = x; gf = 0; bf = c; }
        else             { rf = c; gf = 0; bf = x; }

        return (
            (byte)Math.Round(Math.Clamp(rf + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(gf + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(bf + m, 0, 1) * 255));
    }

    // -------------------------------------------------------------------------
    // Linear interpolation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Per-channel linear interpolation.  t=0 → a, t=1 → b.  t is clamped to [0, 1].
    /// </summary>
    public static (byte r, byte g, byte b) Mix(
        (byte r, byte g, byte b) a,
        (byte r, byte g, byte b) b,
        double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return (
            (byte)Math.Round(a.r + (b.r - a.r) * t),
            (byte)Math.Round(a.g + (b.g - a.g) * t),
            (byte)Math.Round(a.b + (b.b - a.b) * t));
    }

    // -------------------------------------------------------------------------
    // Lightness adjustment
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts <paramref name="rgb"/> to HSL, adds <paramref name="deltaL"/>
    /// to the L channel (clamped to [0, 1]), and converts back to RGB.
    /// </summary>
    public static (byte r, byte g, byte b) AdjustLightness(
        (byte r, byte g, byte b) rgb,
        double deltaL)
    {
        var (h, s, l) = ToHsl(rgb.r, rgb.g, rgb.b);
        return FromHsl(h, s, Math.Clamp(l + deltaL, 0.0, 1.0));
    }

    // -------------------------------------------------------------------------
    // Oklch → RGB  (ported from Blink.App/Theming/Oklch.cs)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts an Oklch color (L 0–1, C chroma, H degrees) to sRGB bytes.
    /// This is a WPF-free port of the exact math in Blink.App/Theming/Oklch.cs
    /// so that accent-hue migration is testable in Blink.Core.
    /// </summary>
    public static (byte r, byte g, byte b) OklchToRgb(double l, double c, double hDeg)
    {
        double h = hDeg * Math.PI / 180.0;
        double a = c * Math.Cos(h);
        double b = c * Math.Sin(h);

        // oklab → LMS (intermediate)
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = l - 0.0894841775 * a - 1.2914855480 * b;

        // cube
        double L3 = l_ * l_ * l_;
        double M3 = m_ * m_ * m_;
        double S3 = s_ * s_ * s_;

        // LMS → linear sRGB
        double rLin =  4.0767416621 * L3 - 3.3077115913 * M3 + 0.2309699292 * S3;
        double gLin = -1.2684380046 * L3 + 2.6097574011 * M3 - 0.3413193965 * S3;
        double bLin = -0.0041960863 * L3 - 0.7034186147 * M3 + 1.7076147010 * S3;

        return (LinearToByte(rLin), LinearToByte(gLin), LinearToByte(bLin));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies the sRGB gamma transfer (same as Blink.App Oklch.LinearToByte).
    /// </summary>
    private static byte LinearToByte(double c)
    {
        double s = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(s, 0.0, 1.0) * 255);
    }
}
