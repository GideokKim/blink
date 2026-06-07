// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Runtime.InteropServices;

namespace Blink.App.Interop;

/// <summary>
/// Applies an acrylic/blur backdrop + rounded corners to a window, branching on OS version:
///   • Win11 (build 22000+): DWMWA_SYSTEMBACKDROP_TYPE (acrylic) + DWMWA_WINDOW_CORNER_PREFERENCE.
///   • Win10 1809+ (build 17763+): undocumented SetWindowCompositionAttribute with
///     ACCENT_ENABLE_ACRYLICBLURBEHIND, rounded corners via an HRGN region.
/// Falls back to a flat translucent brush (caller-controlled) if composition fails.
///
/// IMPORTANT: do NOT set WPF AllowsTransparency=true on the Win11 DWM-backdrop path —
/// a per-pixel-alpha layered window suppresses the system backdrop. Use AllowsTransparency
/// only for the last-resort flat-translucent fallback. Keep text at #111111 for legibility.
/// </summary>
internal static class AcrylicHelper
{
    // ── DWM (Win11) ──────────────────────────────────────────────────────────
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE       = 38;
    private const int DWMSBT_TRANSIENTWINDOW          = 3; // acrylic
    private const int DWMWCP_ROUND                     = 2;

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
            if (IsWin11)
            {
                int backdrop = DWMSBT_TRANSIENTWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                return true;
            }

            // Win10 acrylic via composition attribute.
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = 0xAAFFFFFF, // ~67% white tint; keeps #111111 text legible
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

            // Rounded corners (Win10 has no DWM corner preference).
            if (width > 0 && height > 0)
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
