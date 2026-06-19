// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
//
// UNVERIFIED (authored on macOS, not compiled): explicit entry point required by Velopack.
// `VelopackApp.Build().Run()` MUST run before any WPF/UI code so Velopack's install/update/
// uninstall hooks can execute and exit early during those lifecycle events. On a non-Velopack
// (legacy Inno) layout it is a graceful no-op, so this is safe to ship before the bridge.
//
// Wiring: Blink.App.csproj sets <StartupObject>Blink.App.Program</StartupObject>, which
// suppresses the WPF SDK's auto-generated Main. Because the generated Main is gone, we MUST
// call app.InitializeComponent() ourselves — it loads App.xaml's merged ResourceDictionary
// (Themes/Shared.xaml). Without it ThemeManager and pack:// resources (tray icon) break.
using System;
using Velopack;

namespace Blink.App;

internal static class Program
{
    [STAThread] // WPF + WinForms NotifyIcon both require a single-threaded apartment.
    public static void Main(string[] args)
    {
        // First line, before anything else. Handles --veloapp-* lifecycle hooks and exits
        // during install/update/uninstall. No-ops for a normal run or a legacy Inno layout.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent(); // mandatory: merges App.xaml resources (see header note).
        app.Run();
    }
}
