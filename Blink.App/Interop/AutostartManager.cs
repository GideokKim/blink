// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using Microsoft.Win32;

namespace Blink.App.Interop;

/// <summary>
/// Registers/unregisters Blink for launch at Windows sign-in via the per-user
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> key (no admin rights
/// needed), matching the Python prototype's HKCU autostart. Safe to call repeatedly;
/// <see cref="Apply"/> is idempotent.
/// </summary>
internal static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Blink";

    /// <summary>Add or remove the Run-key entry to match <paramref name="enabled"/>.</summary>
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
            return;

        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                key.SetValue(ValueName, $"\"{exe}\"");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>True if the Run-key entry currently exists.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }
}
