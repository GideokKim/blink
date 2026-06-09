using Blink.Core.Theming;

namespace Blink.Core.Tests;

public sealed class ColorMathTests
{
    // -------------------------------------------------------------------------
    // TryParseHex
    // -------------------------------------------------------------------------

    [Fact]
    public void TryParseHex_6Digit_WithHash()
    {
        Assert.True(ColorMath.TryParseHex("#3B7FE3", out var rgb));
        Assert.Equal((0x3B, 0x7F, 0xE3), rgb);
    }

    [Fact]
    public void TryParseHex_6Digit_NoHash_Lowercase()
    {
        Assert.True(ColorMath.TryParseHex("3b7fe3", out var rgb));
        Assert.Equal((0x3B, 0x7F, 0xE3), rgb);
    }

    [Fact]
    public void TryParseHex_3Digit_Shorthand()
    {
        Assert.True(ColorMath.TryParseHex("#abc", out var rgb));
        Assert.Equal((0xAA, 0xBB, 0xCC), rgb);
    }

    [Fact]
    public void TryParseHex_Invalid_Letters_ReturnsFalse()
    {
        Assert.False(ColorMath.TryParseHex("xyz", out var rgb));
        Assert.Equal((0, 0, 0), rgb);
    }

    [Fact]
    public void TryParseHex_TooShort_ReturnsFalse()
    {
        Assert.False(ColorMath.TryParseHex("#12", out var rgb));
        Assert.Equal((0, 0, 0), rgb);
    }

    [Fact]
    public void TryParseHex_Empty_ReturnsFalse()
    {
        Assert.False(ColorMath.TryParseHex("", out var rgb));
        Assert.Equal((0, 0, 0), rgb);
    }

    [Fact]
    public void TryParseHex_TooLong_7Digits_ReturnsFalse()
    {
        Assert.False(ColorMath.TryParseHex("#1234567", out var rgb));
        Assert.Equal((0, 0, 0), rgb);
    }

    [Fact]
    public void TryParseHex_TrimsWhitespace()
    {
        Assert.True(ColorMath.TryParseHex("  #3B7FE3  ", out var rgb));
        Assert.Equal((0x3B, 0x7F, 0xE3), rgb);
    }

    // -------------------------------------------------------------------------
    // ToHex
    // -------------------------------------------------------------------------

    [Fact]
    public void ToHex_FormatsCorrectly()
    {
        Assert.Equal("#3B7FE3", ColorMath.ToHex(0x3B, 0x7F, 0xE3));
    }

    [Fact]
    public void ToHex_RoundTripsWithTryParseHex()
    {
        var original = (r: (byte)0x3B, g: (byte)0x7F, b: (byte)0xE3);
        var hex = ColorMath.ToHex(original.r, original.g, original.b);
        Assert.True(ColorMath.TryParseHex(hex, out var roundTripped));
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ToHex_Black()
    {
        Assert.Equal("#000000", ColorMath.ToHex(0, 0, 0));
    }

    [Fact]
    public void ToHex_White()
    {
        Assert.Equal("#FFFFFF", ColorMath.ToHex(255, 255, 255));
    }

    // -------------------------------------------------------------------------
    // RelativeLuminance
    // -------------------------------------------------------------------------

    [Fact]
    public void RelativeLuminance_Black_IsZero()
    {
        Assert.Equal(0.0, ColorMath.RelativeLuminance(0, 0, 0));
    }

    [Fact]
    public void RelativeLuminance_White_IsApproxOne()
    {
        var lum = ColorMath.RelativeLuminance(255, 255, 255);
        Assert.InRange(lum, 0.999, 1.001);
    }

    [Fact]
    public void RelativeLuminance_DarkTheme_Base_BelowThreshold()
    {
        // #0B0D12 is the dark theme base — luminance should be well below 0.45
        Assert.True(ColorMath.TryParseHex("#0B0D12", out var rgb));
        var lum = ColorMath.RelativeLuminance(rgb.r, rgb.g, rgb.b);
        Assert.True(lum < 0.45, $"Expected luminance < 0.45, got {lum}");
    }

    [Fact]
    public void RelativeLuminance_LightTheme_Base_AboveThreshold()
    {
        // #F6F7FA is the light theme base — luminance should be well above 0.45
        Assert.True(ColorMath.TryParseHex("#F6F7FA", out var rgb));
        var lum = ColorMath.RelativeLuminance(rgb.r, rgb.g, rgb.b);
        Assert.True(lum > 0.45, $"Expected luminance > 0.45, got {lum}");
    }

    [Fact]
    public void RelativeLuminance_Ordering_Dark_Lt_Light()
    {
        Assert.True(ColorMath.TryParseHex("#0B0D12", out var dark));
        Assert.True(ColorMath.TryParseHex("#F6F7FA", out var light));
        var darkLum = ColorMath.RelativeLuminance(dark.r, dark.g, dark.b);
        var lightLum = ColorMath.RelativeLuminance(light.r, light.g, light.b);
        Assert.True(darkLum < 0.45 && 0.45 < lightLum,
            $"Expected dark({darkLum}) < 0.45 < light({lightLum})");
    }

    // -------------------------------------------------------------------------
    // ToHsl / FromHsl round-trip
    // -------------------------------------------------------------------------

    private static void AssertHslRoundTrip(byte r, byte g, byte b, int tolerance = 1)
    {
        var (h, s, l) = ColorMath.ToHsl(r, g, b);
        var (r2, g2, b2) = ColorMath.FromHsl(h, s, l);
        Assert.InRange(r2, (byte)Math.Max(0, r - tolerance), (byte)Math.Min(255, r + tolerance));
        Assert.InRange(g2, (byte)Math.Max(0, g - tolerance), (byte)Math.Min(255, g + tolerance));
        Assert.InRange(b2, (byte)Math.Max(0, b - tolerance), (byte)Math.Min(255, b + tolerance));
    }

    [Fact]
    public void HslRoundTrip_PureRed()   => AssertHslRoundTrip(255, 0, 0);

    [Fact]
    public void HslRoundTrip_Gray()      => AssertHslRoundTrip(128, 128, 128);

    [Fact]
    public void HslRoundTrip_Accent()    => AssertHslRoundTrip(0x3B, 0x7F, 0xE3);

    [Fact]
    public void HslRoundTrip_Black()     => AssertHslRoundTrip(0, 0, 0);

    [Fact]
    public void HslRoundTrip_White()     => AssertHslRoundTrip(255, 255, 255);

    // -------------------------------------------------------------------------
    // Mix
    // -------------------------------------------------------------------------

    [Fact]
    public void Mix_T0_ReturnsA()
    {
        var a = ((byte)10, (byte)20, (byte)30);
        var b = ((byte)100, (byte)200, (byte)255);
        Assert.Equal(a, ColorMath.Mix(a, b, 0.0));
    }

    [Fact]
    public void Mix_T1_ReturnsB()
    {
        var a = ((byte)10, (byte)20, (byte)30);
        var b = ((byte)100, (byte)200, (byte)255);
        Assert.Equal(b, ColorMath.Mix(a, b, 1.0));
    }

    [Fact]
    public void Mix_T05_BlackWhite_IsGray()
    {
        var black = ((byte)0, (byte)0, (byte)0);
        var white = ((byte)255, (byte)255, (byte)255);
        var (r, g, b) = ColorMath.Mix(black, white, 0.5);
        Assert.InRange(r, (byte)127, (byte)129);
        Assert.InRange(g, (byte)127, (byte)129);
        Assert.InRange(b, (byte)127, (byte)129);
    }

    [Fact]
    public void Mix_ClampsT_BelowZero()
    {
        var a = ((byte)50, (byte)50, (byte)50);
        var b = ((byte)200, (byte)200, (byte)200);
        Assert.Equal(a, ColorMath.Mix(a, b, -1.0));
    }

    [Fact]
    public void Mix_ClampsT_AboveOne()
    {
        var a = ((byte)50, (byte)50, (byte)50);
        var b = ((byte)200, (byte)200, (byte)200);
        Assert.Equal(b, ColorMath.Mix(a, b, 2.0));
    }

    // -------------------------------------------------------------------------
    // AdjustLightness
    // -------------------------------------------------------------------------

    [Fact]
    public void AdjustLightness_Increase_MakesLighter()
    {
        var rgb = (r: (byte)0x3B, g: (byte)0x7F, b: (byte)0xE3);
        var (_, _, lBefore) = ColorMath.ToHsl(rgb.r, rgb.g, rgb.b);
        var adjusted = ColorMath.AdjustLightness(rgb, 0.1);
        var (_, _, lAfter) = ColorMath.ToHsl(adjusted.r, adjusted.g, adjusted.b);
        Assert.True(lAfter > lBefore, $"Expected lighter: {lBefore} → {lAfter}");
    }

    [Fact]
    public void AdjustLightness_Decrease_MakesDarker()
    {
        var rgb = (r: (byte)0x3B, g: (byte)0x7F, b: (byte)0xE3);
        var (_, _, lBefore) = ColorMath.ToHsl(rgb.r, rgb.g, rgb.b);
        var adjusted = ColorMath.AdjustLightness(rgb, -0.1);
        var (_, _, lAfter) = ColorMath.ToHsl(adjusted.r, adjusted.g, adjusted.b);
        Assert.True(lAfter < lBefore, $"Expected darker: {lBefore} → {lAfter}");
    }

    // -------------------------------------------------------------------------
    // OklchToRgb — parity with Blink.App/Theming/Oklch.cs
    // -------------------------------------------------------------------------

    [Fact]
    public void OklchToRgb_DefaultAccent_LockedBytes()
    {
        // OklchToRgb(0.64, 0.155, 250) — the default accent preset.
        // Bytes locked from the first green run; must match App Oklch.ToColor constants.
        // r=0x31(49), g=0x90(144), b=0xE6(230)
        var (r, g, b) = ColorMath.OklchToRgb(0.64, 0.155, 250);
        Assert.Equal((byte)0x31, r);
        Assert.Equal((byte)0x90, g);
        Assert.Equal((byte)0xE6, b);
        // Round-trip sanity check
        var hex = ColorMath.ToHex(r, g, b);
        Assert.Equal("#3190E6", hex);
        Assert.True(ColorMath.TryParseHex(hex, out var parsed));
        Assert.Equal((r, g, b), parsed);
    }

    [Fact]
    public void OklchToRgb_ProducesValidRange()
    {
        var (r, g, b) = ColorMath.OklchToRgb(0.64, 0.155, 250);
        // All channels must be in byte range (guaranteed by the type, but sanity-check
        // that the values are non-trivially non-zero for a chromatic color).
        Assert.True(r > 0 || g > 0 || b > 0, "Expected non-black output");
    }

    [Fact]
    public void OklchToRgb_Black_ReturnsNearBlack()
    {
        var (r, g, b) = ColorMath.OklchToRgb(0.0, 0.0, 0.0);
        Assert.Equal(0, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void OklchToRgb_White_ReturnsNearWhite()
    {
        var (r, g, b) = ColorMath.OklchToRgb(1.0, 0.0, 0.0);
        Assert.InRange(r, (byte)253, (byte)255);
        Assert.InRange(g, (byte)253, (byte)255);
        Assert.InRange(b, (byte)253, (byte)255);
    }

    [Fact]
    public void OklchToRgb_RoundTripsToHex()
    {
        var (r, g, b) = ColorMath.OklchToRgb(0.64, 0.155, 250);
        var hex = ColorMath.ToHex(r, g, b);
        Assert.True(ColorMath.TryParseHex(hex, out var parsed));
        Assert.Equal((r, g, b), parsed);
    }
}
