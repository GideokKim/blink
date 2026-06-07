// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using Blink.App.Interop;
using Blink.Core.Config;
using Blink.Core.Search;
using Blink.Core.Store;
using Forms = System.Windows.Forms;

namespace Blink.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HotkeyHook? _hotkey;
    private SqliteFtsStore? _store;
    private LauncherWindow? _launcher;
    private IndexingService? _indexing;
    private Forms.NotifyIcon? _tray;
    private AppConfig _config = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard.
        _singleInstance = new Mutex(initiallyOwned: true, "Blink.App.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        _config = AppConfig.Load();
        _store = new SqliteFtsStore(_config.DbPath);
        var provider = new InProcessProvider(_store);

        _launcher = new LauncherWindow(provider);
        _indexing = new IndexingService();

        // Global hotkey (Left Alt+Space) → summon launcher on the UI thread.
        _hotkey = new HotkeyHook();
        _hotkey.HotkeyPressed += () => _launcher!.Summon();

        SetupTray();

        // Initial index of configured folders (off the UI thread).
        if (_config.Folders.Length > 0)
            _ = _indexing.ReindexAsync(_config.Folders, _store);
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Blink",
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Quit", null, (_, _) => QuitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _launcher!.Summon();
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_config);
        win.ReindexRequested += folders =>
        {
            if (_store is not null)
                _ = _indexing!.ReindexAsync(folders, _store);
        };
        win.Show();
        win.Activate();
    }

    private void QuitApp()
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        _indexing?.Dispose();
        _store?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
