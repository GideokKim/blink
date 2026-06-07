// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Blink.App.Interop;

/// <summary>
/// Installs a WH_KEYBOARD_LL hook on a dedicated background thread that runs
/// its own Win32 message pump (GetMessage / TranslateMessage / DispatchMessage).
///
/// Fires <see cref="HotkeyPressed"/> on the UI thread when LEFT Alt+Space is detected.
/// Specifically tracks VK_LMENU (0xA4) only — right-Alt (VK_RMENU 0xA5) is ignored.
///
/// IMPORTANT: Low-level keyboard hooks have a system timeout (~300 ms by default,
/// configurable via HKCU\Control Panel\Desktop\LowLevelHooksTimeout). The callback
/// must return quickly — it is O(1) by design. If it blocks, Windows will silently
/// remove the hook.
/// </summary>
internal sealed class HotkeyHook : IDisposable
{
    // ── Win32 constants ──────────────────────────────────────────────────────
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_KEYUP       = 0x0101;
    private const int WM_SYSKEYUP    = 0x0105;
    private const int VK_LMENU       = 0xA4; // Left Alt specifically — right-Alt (0xA5) NOT tracked
    private const int VK_SPACE       = 0x20;

    // ── Win32 P/Invoke ───────────────────────────────────────────────────────
    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint   hwnd;
        public uint   message;
        public nint   wParam;
        public nint   lParam;
        public uint   time;
        public int    pt_x;
        public int    pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint   vkCode;
        public uint   scanCode;
        public uint   flags;
        public uint   time;
        public nint   dwExtraInfo;
    }

    // ── State ────────────────────────────────────────────────────────────────
    private readonly SynchronizationContext _uiContext;
    private nint                            _hookHandle;
    private volatile bool                   _leftAltDown;
    private bool                            _disposed;
    private Thread?                         _pumpThread;
    private uint                            _pumpNativeThreadId; // Win32 thread id (NOT ManagedThreadId)

    // Keep a GC-rooted reference to the delegate — the GC must not collect it
    // while the hook is installed (the runtime only holds a function pointer).
    private readonly LowLevelKeyboardProc _hookCallback;

    public event Action? HotkeyPressed;

    public HotkeyHook()
    {
        _uiContext    = SynchronizationContext.Current
                        ?? throw new InvalidOperationException(
                               "HotkeyHook must be constructed on the UI thread.");
        _hookCallback = HookCallback; // root the delegate
        InstallOnDedicatedThread();
    }

    // ── Installation ─────────────────────────────────────────────────────────

    private void InstallOnDedicatedThread()
    {
        // Use a ManualResetEventSlim to wait until the hook is installed
        // before returning from the constructor.
        using var ready = new ManualResetEventSlim(false);
        Exception? installError = null;

        _pumpThread = new Thread(() =>
        {
            // Capture the NATIVE Win32 thread id for PostThreadMessage on shutdown.
            _pumpNativeThreadId = GetCurrentThreadId();

            // Install the hook on THIS thread (threadId = 0 = global).
            nint hMod = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, hMod, 0);

            if (_hookHandle == nint.Zero)
                installError = new InvalidOperationException(
                    $"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");

            ready.Set(); // signal constructor to continue

            if (_hookHandle == nint.Zero)
                return;

            // Run a minimal Win32 message pump so the hook callback can be dispatched.
            // GetMessage blocks until a message arrives; the loop exits when the thread
            // is asked to quit (PostQuitMessage from Dispose).
            MSG msg;
            while (GetMessage(out msg, nint.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // Clean up — UnhookWindowsHookEx must be called on the same thread.
            if (_hookHandle != nint.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = nint.Zero;
            }
        });

        _pumpThread.Name         = "BlinkHotkeyPump";
        _pumpThread.IsBackground = true;
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();

        ready.Wait();

        if (installError is not null)
            throw installError;
    }

    // ── Callback — MUST be O(1); do NOT block ────────────────────────────────

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk      = kbStruct.vkCode;
            bool isDown  = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp    = wParam == WM_KEYUP   || wParam == WM_SYSKEYUP;

            if (vk == VK_LMENU) // Left Alt only — VK_RMENU (0xA5) intentionally excluded
            {
                _leftAltDown = isDown;
            }
            else if (vk == VK_SPACE && isDown && _leftAltDown)
            {
                // Post to UI thread; callback returns immediately (O(1)).
                _uiContext.Post(_ => HotkeyPressed?.Invoke(), null);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Posting WM_QUIT (0x0012) to the pump thread causes GetMessage to return false,
        // ending the loop and letting UnhookWindowsHookEx run on the correct thread.
        if (_pumpThread is { IsAlive: true } && _pumpNativeThreadId != 0)
        {
            PostThreadMessage(_pumpNativeThreadId, 0x0012, 0, 0); // WM_QUIT
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
