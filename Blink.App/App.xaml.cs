// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using Blink.App.Interop;
using Blink.App.Theming;
using Blink.App.ViewModels;
using Blink.Core.Config;
using Blink.Core.Launch;
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
    private readonly IndexingStatusViewModel _status = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, "Blink.App.SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        _config = AppConfig.Load();

        // Theme tokens must be published before any launcher control is created.
        ThemeManager.Apply(ThemeOf(_config), _config.AccentHue, 0.45);

        // Production content index + GUI search facade. Built before the launcher so the real
        // FTS provider can be injected and the initial demo/real mode decided up front.
        _store = new SqliteFtsStore(_config.DbPath);
        var provider = new InProcessProvider(_store);

        // The launcher UI runs the ported launcher engine over an index. With an empty DB it
        // shows the demo index (reproduces the design); once ≥1 doc is indexed it switches to
        // real FTS results through the same view-model.
        _launcher = new LauncherWindow(DemoIndex.Items, _status, provider);
        _launcher.SetDirection(DirectionOf(_config));
        _launcher.SetIndexMode(_store.Count());

        // Indexing progress is surfaced live via _status to the launcher footer and Settings.
        _indexing = new IndexingService();
        _indexing.ProgressChanged += p => _status.Report(p);          // UI thread (Progress<T>)
        _indexing.Completed += OnIndexingCompleted;                    // background thread → marshal

        _hotkey = new HotkeyHook();
        _hotkey.HotkeyPressed += () => _launcher!.Summon();

        SetupTray();

        if (_config.Folders.Length > 0)
            StartReindex(_config.Folders);
    }

    /// <summary>Begin a reindex and flip the shared status to "indexing".</summary>
    private void StartReindex(IReadOnlyList<string> folders)
    {
        if (_store is null || _indexing is null) return;
        _status.Begin();
        _ = _indexing.ReindexAsync(folders, _store);
    }

    private void OnIndexingCompleted()
        => Dispatcher.Invoke(() =>
        {
            int count = _store?.Count() ?? 0;
            _status.Complete(count);
            _launcher?.SetIndexMode(count); // flip to real results once the index is populated
        });

    private static LauncherTheme ThemeOf(AppConfig c) =>
        string.Equals(c.Theme, "light", StringComparison.OrdinalIgnoreCase) ? LauncherTheme.Light : LauncherTheme.Dark;

    private static LauncherDirection DirectionOf(AppConfig c) =>
        string.Equals(c.Direction, "B", StringComparison.OrdinalIgnoreCase) ? LauncherDirection.Split : LauncherDirection.Classic;

    private void SetupTray()
    {
        var iconUri = new Uri("pack://application:,,,/blink.ico");
        using var iconStream = Application.GetResourceStream(iconUri)!.Stream;

        _tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream),
            Visible = true,
            Text = "Blink",
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var dirA = new Forms.ToolStripMenuItem("방향 A · 클래식", null, (_, _) => SetDirection("A"));
        var dirB = new Forms.ToolStripMenuItem("방향 B · 듀얼 패널", null, (_, _) => SetDirection("B"));
        void SyncDir()
        {
            dirA.Checked = DirectionOf(_config) == LauncherDirection.Classic;
            dirB.Checked = DirectionOf(_config) == LauncherDirection.Split;
        }
        SyncDir();
        _dirSync = SyncDir;
        menu.Items.Add(dirA);
        menu.Items.Add(dirB);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("테마 전환 (다크/라이트)", null, (_, _) => ToggleTheme());

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitApp());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _launcher!.Summon();
    }

    private Action? _dirSync;

    private void SetDirection(string dir)
    {
        _config.Direction = dir;
        _config.Save();
        _launcher?.SetDirection(DirectionOf(_config));
        _dirSync?.Invoke();
    }

    private void ToggleTheme()
    {
        _launcher?.ToggleTheme();
        _config.Theme = ThemeManager.Current == LauncherTheme.Light ? "light" : "dark";
        _config.Save();
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_config, _status);
        win.ReindexRequested += folders => StartReindex(folders);
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
