// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using Blink.App.Interop;
using Blink.App.Theming;
using Blink.App.Update;
using Blink.App.ViewModels;
using Blink.Core.Config;
using Blink.Core.Indexing;
using Blink.Core.Launch;
using Blink.Core.Search;
using Blink.Core.Store;
using Blink.Core.Update;
using Forms = System.Windows.Forms;

namespace Blink.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HotkeyHook? _hotkey;
    private SqliteFtsStore? _store;
    private LauncherWindow? _launcher;
    private IndexingService? _indexing;
    private AutoIndexScheduler? _autoIndex;
    private Forms.NotifyIcon? _tray;
    private AppConfig _config = new();
    private readonly IndexingStatusViewModel _status = new();
    private readonly Dictionary<string, DateTime> _pendingFolderTimes = new();
    private UpdateService? _updates;
    private ReleaseInfo? _pendingRelease;
    // Always-visible update surface in the tray. Unlike the balloon/toast, a menu item cannot be
    // suppressed by Focus Assist / Do Not Disturb, so the update stays discoverable regardless.
    private Forms.ToolStripMenuItem? _updateMenuItem;
    private Forms.ToolStripSeparator? _updateSeparator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, "Blink.App.SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        _config = AppConfig.Load();

        // Self-heal autostart from the saved preference on every launch. Apply() is idempotent and
        // points the HKCU Run entry at the current exe (Environment.ProcessPath / Velopack's stable
        // current\ path), so the "autostart silently turns off after an install/update" bug — caused
        // by relying on the installer task + only applying on Settings-save — no longer happens.
        AutostartManager.Apply(_config.Autostart);

        // Theme tokens must be published before any launcher control is created.
        ThemeManager.Apply(_config.ThemeMode, _config.BaseColor, _config.Accent);

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
        // FolderCompleted fires on the background thread; just record under lock.
        _indexing.FolderCompleted += f => { lock (_pendingFolderTimes) _pendingFolderTimes[f] = DateTime.UtcNow; };
        // A failed folder is skipped (no completion stamp) and the run continues; log for diagnosis.
        _indexing.FolderFailed += (f, ex) => System.Diagnostics.Trace.WriteLine($"[Blink] indexing failed for '{f}': {ex}");

        _hotkey = new HotkeyHook();
        _hotkey.HotkeyPressed += () => _launcher!.Summon();

        SetupTray();

        // Background auto-reindex on the configured cadence ("수동" disables it). Marshal the
        // tick onto the UI thread; StartReindex touches UI-affine state. A tick that lands
        // while a run is still in flight is skipped — restarting would cancel the run and
        // reset its progress (a full pass over a network share can outlast the cadence).
        _autoIndex = new AutoIndexScheduler(() =>
            Dispatcher.Invoke(() =>
            {
                if (_config.Folders.Length > 0 && _indexing is { IsBusy: false })
                    StartReindex(_config.Folders);
            }));
        _autoIndex.Configure(_config.AutoIndexInterval);

        if (_config.Folders.Length > 0)
            StartReindex(_config.Folders);

        // Auto-update: clear installers left by the previous update (they can't delete
        // themselves while running), arm the 30 s + 24 h check, and show "새로워진 점"
        // on the first launch after an update.
        UpdateService.CleanupTempInstallers();
        _updates = new UpdateService(_config);
        _updates.UpdateAvailable += OnUpdateAvailable;
        _updates.Start();
        _ = ShowWhatsNewIfUpdatedAsync();
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

            // Flush the per-folder completion timestamps accumulated during this run.
            lock (_pendingFolderTimes)
            {
                if (_pendingFolderTimes.Count > 0)
                {
                    foreach (var (path, dt) in _pendingFolderTimes)
                        _config.FolderIndexTimes[path] = dt.ToString("o");
                    _pendingFolderTimes.Clear();
                    _config.Save();
                }
            }
        });

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

        // Persistent "update available" surface, hidden until an update is found. Sits at the top
        // of the tray menu so it is the first thing the user sees — the suppression-proof surface.
        _updateMenuItem = new Forms.ToolStripMenuItem("⬆ 업데이트 설치", null, (_, _) =>
        {
            if (_pendingRelease is not null) ShowUpdateWindow(_pendingRelease);
        }) { Visible = false };
        _updateSeparator = new Forms.ToolStripSeparator { Visible = false };
        menu.Items.Add(_updateMenuItem);
        menu.Items.Add(_updateSeparator);

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
        menu.Items.Add("☕ 후원하기", null, (_, _) => ShowDonate());

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
        _launcher?.ToggleTheme(); // ThemeManager.Toggle() flips the base preset, preserving accent
        _config.ThemeMode = "value";
        _config.BaseColor = ThemeManager.Current == LauncherTheme.Light ? "#F6F7FA" : "#0B0D12";
        _config.Save();
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(
            _config, _status,
            root => _store is null ? (0L, 0L) : _store.FolderStats(root),
            QuitApp);
        win.ReindexRequested += folders => StartReindex(folders);
        win.SettingsSaved += OnSettingsSaved;
        win.Show();
        win.Activate();
    }

    private DonateWindow? _donate;

    /// <summary>Open (or re-focus) the donation window. Reused by the tray menu and What's New.</summary>
    private void ShowDonate()
    {
        if (_donate is { IsLoaded: true }) { _donate.Activate(); return; }
        _donate = new DonateWindow();
        _donate.Closed += (_, _) => _donate = null;
        _donate.Show();
        _donate.Activate();
    }

    /// <summary>Apply settings the user just saved: re-arm the auto-index cadence, sync layout, sync the tray.</summary>
    private void OnSettingsSaved()
    {
        _autoIndex?.Configure(_config.AutoIndexInterval);
        _launcher?.SetDirection(DirectionOf(_config));
        ThemeManager.Apply(_config.ThemeMode, _config.BaseColor, _config.Accent);
        _dirSync?.Invoke();
    }

    /// <summary>
    /// Automatic check found an offerable release. Surfaces it on two channels: a persistent
    /// tray menu item + tooltip (always visible, survives Focus Assist/DND) and a best-effort
    /// balloon nudge (may be suppressed by the OS — that's why the tray item exists).
    /// </summary>
    private void OnUpdateAvailable(ReleaseInfo release)
    {
        _pendingRelease = release;
        if (_tray is null) return;

        // 1) Persistent tray surface — the reliable channel.
        if (_updateMenuItem is not null)
        {
            _updateMenuItem.Text = $"⬆ 업데이트 v{release.Version} 설치";
            _updateMenuItem.Visible = true;
        }
        if (_updateSeparator is not null) _updateSeparator.Visible = true;
        _tray.Text = "Blink · 업데이트 있음"; // tooltip hint (NotifyIcon.Text ≤ 63 chars)

        // 2) Best-effort instant nudge.
        _tray.BalloonTipClicked -= OnUpdateBalloonClicked; // re-arm idempotently per check
        _tray.BalloonTipClicked += OnUpdateBalloonClicked;
        _tray.ShowBalloonTip(10000, "Blink 업데이트",
            $"Blink {release.Version} 업데이트가 있습니다. 클릭하면 자세히 볼 수 있어요.",
            Forms.ToolTipIcon.Info);
    }

    private void OnUpdateBalloonClicked(object? sender, EventArgs e)
    {
        if (_pendingRelease is not null) ShowUpdateWindow(_pendingRelease);
    }

    private void ShowUpdateWindow(ReleaseInfo release)
    {
        var win = new UpdateWindow(release, _config, QuitApp);
        win.Show();
        win.Activate();
    }

    /// <summary>
    /// last_seen_version과 현재 버전 비교. Show면 해당 태그의 릴리스 노트를 조회해 창을
    /// 띄우고 갱신, 조회 실패(오프라인)면 갱신하지 않고 다음 시작에 재시도.
    /// </summary>
    private async Task ShowWhatsNewIfUpdatedAsync()
    {
        var current = UpdateService.CurrentVersion;
        switch (WhatsNewGate.Decide(_config.LastSeenVersion, current))
        {
            case WhatsNewAction.InitializeOnly:
                _config.LastSeenVersion = current;
                _config.Save();
                break;

            case WhatsNewAction.Show:
                var release = await UpdateService.FetchByTagAsync($"v{current}");
                if (release is null) return; // offline — keep last_seen, retry next launch
                new WhatsNewWindow(current, release.Body).Show();
                _config.LastSeenVersion = current;
                _config.Save();
                break;
        }
    }

    private void QuitApp()
    {
        _updates?.Dispose();
        _tray?.Dispose();
        _hotkey?.Dispose();
        _autoIndex?.Dispose();
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
