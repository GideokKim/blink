// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows.Media;

namespace Blink.App.Theming;

/// <summary>
/// Converts the design system's authoritative <c>oklch()</c> colors to sRGB. XAML has no
/// oklch support, so the accent (hue-tweakable across the cool 220–280 range at fixed
/// L 0.64 / C 0.155) and the per-category tile tints (<c>oklch(0.8 0.09 hue)</c>) are
/// computed here for exact parity rather than hand-approximated.
/// </summary>
internal static class Oklch
{
    /// <summary>oklch (L 0–1, C, H degrees) + alpha 0–1 → WPF <see cref="Color"/>.</summary>
    public static Color ToColor(double l, double c, double hDeg, double alpha = 1.0)
    {
        double h = hDeg * Math.PI / 180.0;
        double a = c * Math.Cos(h);
        double b = c * Math.Sin(h);

        // oklab → LMS (cubed)
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double L3 = l_ * l_ * l_, M3 = m_ * m_ * m_, S3 = s_ * s_ * s_;

        // LMS → linear sRGB
        double r = 4.0767416621 * L3 - 3.3077115913 * M3 + 0.2309699292 * S3;
        double g = -1.2684380046 * L3 + 2.6097574011 * M3 - 0.3413193965 * S3;
        double bl = -0.0041960863 * L3 - 0.7034186147 * M3 + 1.7076147010 * S3;

        return Color.FromArgb(
            (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
            LinearToByte(r), LinearToByte(g), LinearToByte(bl));
    }

    public static SolidColorBrush ToBrush(double l, double c, double hDeg, double alpha = 1.0)
    {
        var brush = new SolidColorBrush(ToColor(l, c, hDeg, alpha));
        brush.Freeze();
        return brush;
    }

    private static byte LinearToByte(double c)
    {
        double s = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(s, 0, 1) * 255);
    }
}
