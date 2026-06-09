// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Runtime.InteropServices;

namespace Blink.App.Interop;

/// <summary>
/// Applies an acrylic/blur backdrop + rounded corners to the launcher window.
///
/// The launcher is a WPF per-pixel-alpha window (AllowsTransparency=true) so it can render a
/// floating rounded panel + drop shadow. That layered-window style SUPPRESSES the Win11
/// DWMWA_SYSTEMBACKDROP_TYPE backdrop, so we do NOT use it. Instead, on both Win10 1809+ and
/// Win11 we use the undocumented SetWindowCompositionAttribute / ACCENT_ENABLE_ACRYLICBLURBEHIND,
/// which DOES blur behind a layered window. Rounded corners: DWM corner preference on Win11,
/// an HRGN region on Win10. Falls back to the caller's translucent BgGlass brush if composition fails.
///
/// The acrylic tint is kept light so the blur reads as glass rather than a solid fill; the
/// translucent BgGlass brush is layered on top in XAML (see ThemeManager.GlassAlpha).
/// </summary>
internal static class AcrylicHelper
{
    // ── DWM (Win11) ──────────────────────────────────────────────────────────
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND                    = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    // ── SetWindowCompositionAttribute (Win10) ────────────────────────────────
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor; // 0xAABBGGRR
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute; // WCA_ACCENT_POLICY = 19
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    private const int WCA_ACCENT_POLICY = 19;

    // ── Rounded-corner region (Win10) ────────────────────────────────────────
    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

    private static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Apply the best available backdrop for the current OS. Returns true on success.</summary>
    public static bool TryApply(nint hwnd, int width, int height, int cornerRadius = 12)
    {
        try
        {
            // Win11: let DWM round the corners. We deliberately skip DWMWA_SYSTEMBACKDROP_TYPE —
            // it is suppressed for AllowsTransparency layered windows (which the launcher is).
            if (IsWin11)
            {
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }

            // Acrylic blur-behind via composition attribute — works with the layered window on
            // both Win10 1809+ and Win11. Light tint so the blur reads as glass, not a solid fill.
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = 0x59FFFFFF, // ~35% white tint; lets the blur show through
            };
            int size = Marshal.SizeOf(accent);
            nint ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size,
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            // Rounded corners on Win10 (no DWM corner preference there).
            if (!IsWin11 && width > 0 && height > 0)
            {
                nint rgn = CreateRoundRectRgn(0, 0, width, height, cornerRadius, cornerRadius);
                SetWindowRgn(hwnd, rgn, true);
            }
            return true;
        }
        catch
        {
            return false; // caller should fall back to a flat translucent brush
        }
    }
}
