// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Blink.Core.Launch;

namespace Blink.App.Interop;

/// <summary>
/// Installs a WH_KEYBOARD_LL hook on a dedicated background thread that runs
/// its own Win32 message pump (GetMessage / TranslateMessage / DispatchMessage).
///
/// Fires <see cref="HotkeyPressed"/> on the UI thread when LEFT Alt+Space is detected,
/// and CONSUMES the hotkey: Space down/up are swallowed (callback returns 1) so the
/// foreground app never sees them, and a dummy key (0xFF, tagged with
/// <see cref="HotkeyInterceptor.InjectionMarker"/>) is injected while Alt is still
/// held so the foreground app does not treat the sequence as a lone-Alt press and
/// activate its menu bar.
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
    private const ushort VK_DUMMY        = 0xFF;     // 미할당 VK — 어떤 앱도 바인딩 안 함
    private const uint   KEYEVENTF_KEYUP = 0x0002;
    private const uint   INPUT_KEYBOARD  = 1;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public nuint  dwExtraInfo;
    }

    // Size = 40 필수: 실제 Win32 INPUT은 MOUSEINPUT(32바이트)을 포함한 union이라
    // x64에서 40바이트다. KEYBDINPUT(24바이트)만 두면 32바이트가 되어 SendInput이
    // cbSize 불일치로 ERROR_INVALID_PARAMETER 실패한다.
    // NOTE: FieldOffset(8)은 x64 전용 (union 오프셋 = 포인터 정렬 8바이트).
    // x86/AnyCPU-32에서는 오프셋 4라 깨진다 — Blink.App은 x64 단독 배포라 OK.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint       type;
        [FieldOffset(8)] public KEYBDINPUT ki; // x64: union 오프셋 8
    }

    // ── State ────────────────────────────────────────────────────────────────
    private readonly SynchronizationContext _uiContext;
    private nint                            _hookHandle;
    private readonly HotkeyInterceptor      _interceptor = new(); // 단일 펌프 스레드 전용 — volatile 불필요
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
            bool isDown  = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp    = wParam == WM_KEYUP   || wParam == WM_SYSKEYUP;

            var action = _interceptor.Process(kbStruct.vkCode, isDown, isUp,
                                              (ulong)kbStruct.dwExtraInfo);
            if (action == HotkeyAction.SwallowAndFire)
            {
                // Alt가 아직 눌린 지금 더미 키를 주입해야 'Alt 단독' 메뉴 활성화가
                // 깨진다. SendInput 1회는 마이크로초 단위 — LL 훅 타임아웃과 무관.
                InjectDummyKey();
                _uiContext.Post(_ => HotkeyPressed?.Invoke(), null);
            }
            if (action != HotkeyAction.Forward)
                return 1; // 이벤트 소비 — 포그라운드 앱에 전달되지 않음
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// 미사용 VK(0xFF) down/up을 주입해 포그라운드 앱의 'Alt 단독 눌림' 판정을
    /// 깬다. dwExtraInfo 마커로 우리 훅의 상태 추적에서는 제외된다.
    /// VK_MENU를 합성하면 stuck-Alt가 발생하므로(ForegroundHelper 주석 참조) 금지.
    /// </summary>
    private static void InjectDummyKey()
    {
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT
                { wVk = VK_DUMMY, dwExtraInfo = HotkeyInterceptor.InjectionMarker } },
            new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT
                { wVk = VK_DUMMY, dwFlags = KEYEVENTF_KEYUP,
                  dwExtraInfo = HotkeyInterceptor.InjectionMarker } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
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
