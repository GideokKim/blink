// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Runtime.InteropServices;

namespace Blink.App.Interop;

/// <summary>
/// Reliably moves a window to the foreground by temporarily attaching input queues.
///
/// Why AttachThreadInput instead of keybd_event(VK_MENU) Alt-synthesis:
///   The hotkey already depresses Left Alt. Synthesising another VK_MENU key event
///   causes a stuck-Alt condition (the real key-up never fires for the synthetic key),
///   resulting in the system thinking Alt is still held after the window appears.
///   The AttachThreadInput approach avoids any additional key events and is the
///   documented workaround for ForegroundLockTimeout (since Win2000).
/// </summary>
internal static class ForegroundHelper
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int SW_SHOW   = 5;
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Bring <paramref name="hwnd"/> to the foreground, bypassing ForegroundLockTimeout.
    /// Call this after the WPF window's <c>Show()</c> / <c>Visibility = Visible</c>.
    /// </summary>
    public static void BringToForeground(nint hwnd)
    {
        nint fgWnd      = GetForegroundWindow();
        uint fgThread   = GetWindowThreadProcessId(fgWnd, out _);
        uint ourThread  = GetCurrentThreadId();

        bool attached = false;
        if (fgThread != 0 && fgThread != ourThread)
        {
            // Temporarily merge input queues so SetForegroundWindow succeeds.
            attached = AttachThreadInput(fgThread, ourThread, true);
        }

        try
        {
            ShowWindow(hwnd, SW_SHOW);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(fgThread, ourThread, false);
        }
    }
}
