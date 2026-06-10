// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Blink.App.Interop;
using Blink.App.Mvvm;
using Blink.App.Theming;
using Blink.App.ViewModels;
using Blink.Core.Config;
using Blink.Core.Indexing;

namespace Blink.App;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly Func<string, (long count, long bytes)>? _statsProvider;
    private readonly ObservableCollection<FolderRow> _folders;
    private readonly ObservableCollection<IntervalOptionVm> _intervals;
    private string _selectedInterval;
    private string _selectedThemeMode;
    private string _selectedBaseColor;
    private string _selectedAccent;

    /// <summary>Raised when the user requests a re-index with the current folder set.</summary>
    public event Action<IReadOnlyList<string>>? ReindexRequested;

    /// <summary>Raised after the user saves, so the app can re-arm the auto-index cadence and sync UI.</summary>
    public event Action? SettingsSaved;

    public SettingsWindow(AppConfig config, IndexingStatusViewModel status,
        Func<string, (long count, long bytes)>? statsProvider = null)
    {
        InitializeComponent();
        _config = config;
        _statsProvider = statsProvider;

        // Live indexing status drives the status line + progress bar bindings.
        DataContext = status;

        _folders = new ObservableCollection<FolderRow>(config.Folders.Select(MakeFolderRow));
        FoldersList.ItemsSource = _folders;
        _folders.CollectionChanged += (_, _) => UpdateEmpty();
        UpdateEmpty();

        DbPathText.Text = config.DbPath;

        // Reflect the actual registry state, falling back to the saved preference.
        AutostartToggle.IsChecked = AutostartManager.IsEnabled() || config.Autostart;

        // Auto-index interval dropdown.
        _selectedInterval = AutoIndexInterval.Resolve(config.AutoIndexInterval).Key;
        _intervals = new ObservableCollection<IntervalOptionVm>(
            AutoIndexInterval.Options.Select(o => new IntervalOptionVm(o) { IsSelected = o.Key == _selectedInterval }));
        IntervalOptions.ItemsSource = _intervals;
        SyncIntervalButton();

        // Theme picker (value-based base color + 시스템 chip).
        _selectedThemeMode = config.ThemeMode;
        _selectedBaseColor = config.BaseColor;
        _selectedAccent = config.Accent;
        ThemePicker.Presets = new[] { "#0B0D12", "#14161C", "#0A0C18", "#F6F7FA", "#E9ECF2" };
        if (string.Equals(config.ThemeMode, "system", StringComparison.OrdinalIgnoreCase))
            ThemePicker.SetSystem();
        else
            ThemePicker.SelectedHex = config.BaseColor;
        ThemePicker.ColorChanged += OnThemePickerChanged;

        // Accent picker (value-based accent hex; no 시스템 chip).
        AccentPicker.Presets = new[] { "#3B7FE3", "#4F46E5", "#06B6D4", "#14B8A6", "#F43F5E" };
        AccentPicker.SelectedHex = config.Accent;
        AccentPicker.ColorChanged += OnAccentPickerChanged;
    }

    /// <summary>Build a folder row enriched with stats + last-indexed time from config.</summary>
    private FolderRow MakeFolderRow(string path)
    {
        DateTime? lastUtc = null;
        if (_config.FolderIndexTimes.TryGetValue(path, out var iso) &&
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            lastUtc = parsed;
        }
        (long count, long bytes)? stats = _statsProvider?.Invoke(path);
        return new FolderRow(path, stats, lastUtc);
    }

    private void UpdateEmpty() =>
        EmptyHint.Visibility = _folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Window chrome ─────────────────────────────────────────────────────────
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && WindowState == WindowState.Normal) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Folders ───────────────────────────────────────────────────────────────
    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK &&
            !string.IsNullOrWhiteSpace(dlg.SelectedPath) &&
            !_folders.Any(f => string.Equals(f.Path, dlg.SelectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            _folders.Add(new FolderRow(dlg.SelectedPath, null, null)); // never indexed → "대기 중…"
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            var row = _folders.FirstOrDefault(f => f.Path == path);
            if (row is not null) _folders.Remove(row);
        }
    }

    private void Reindex_Click(object sender, RoutedEventArgs e)
    {
        _config.Folders = _folders.Select(f => f.Path).ToArray();
        _config.Save();
        ReindexRequested?.Invoke(_config.Folders);
    }

    // ── Auto-index interval dropdown ──────────────────────────────────────────
    private void IntervalOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
        _selectedInterval = key;
        foreach (var vm in _intervals) vm.IsSelected = vm.Key == key;
        SyncIntervalButton();
        IntervalToggle.IsChecked = false; // close the popup
    }

    private void SyncIntervalButton()
    {
        var opt = AutoIndexInterval.Resolve(_selectedInterval);
        IntervalLabel.Text = opt.Label;
        IntervalPulse.Visibility = opt.Period is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Theme / accent pickers ────────────────────────────────────────────────
    private void OnThemePickerChanged()
    {
        _selectedThemeMode = ThemePicker.IsSystem ? "system" : "value";
        _selectedBaseColor = ThemePicker.SelectedHex;
        // Live-apply: ThemeManager publishes to app resources, so the launcher (DynamicResource)
        // and this window both update immediately.
        ThemeManager.Apply(_selectedThemeMode, _selectedBaseColor, _selectedAccent);
    }

    private void OnAccentPickerChanged()
    {
        _selectedAccent = AccentPicker.SelectedHex;
        ThemeManager.Apply(_selectedThemeMode, _selectedBaseColor, _selectedAccent);
    }

    // ── Database path ─────────────────────────────────────────────────────────
    private void DbOpen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _config.DbPath;
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
        }
        catch { /* explorer not available — non-fatal */ }
    }

    private void DbChange_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            _config.DbPath = Path.Combine(dlg.SelectedPath, "index.db");
            DbPathText.Text = _config.DbPath; // takes effect on next launch
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.Folders = _folders.Select(f => f.Path).ToArray();
        _config.Autostart = AutostartToggle.IsChecked == true;
        _config.AutoIndexInterval = _selectedInterval;
        _config.ThemeMode = _selectedThemeMode;
        _config.BaseColor = _selectedBaseColor;
        _config.Accent = _selectedAccent;
        AutostartManager.Apply(_config.Autostart);
        _config.Save();

        SettingsSaved?.Invoke();
        ShowSavedConfirmation();
    }

    private void ShowSavedConfirmation()
    {
        SavedIndicator.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SavedIndicator.Visibility = Visibility.Hidden;
        };
        timer.Start();
    }
}

/// <summary>
/// A folder row: the full path split into a dimmed parent segment + leaf, plus an index-stats
/// subtitle (format rules live in <see cref="FolderStatusFormat"/>). "대기 중…" only when the
/// folder was never indexed; an indexed-but-empty folder shows "0 파일 · 마지막 인덱싱 …".
/// </summary>
public sealed class FolderRow
{
    public string Path { get; }
    public string ParentSeg { get; }
    public string Leaf { get; }
    public string Sub { get; }

    public FolderRow(string path, (long count, long bytes)? stats, DateTime? lastIndexedUtc)
    {
        Path = path;
        var trimmed = path.TrimEnd('\\', '/');
        int idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        if (idx >= 0)
        {
            ParentSeg = trimmed[..(idx + 1)];
            Leaf = trimmed[(idx + 1)..];
        }
        else
        {
            ParentSeg = string.Empty;
            Leaf = trimmed;
        }

        Sub = FolderStatusFormat.Subtitle(stats, lastIndexedUtc, DateTime.UtcNow);
    }
}

/// <summary>View-model for one auto-index interval option in the dropdown (tracks selection).</summary>
public sealed class IntervalOptionVm : ObservableObject
{
    private readonly AutoIndexInterval.Option _opt;
    private bool _isSelected;

    public IntervalOptionVm(AutoIndexInterval.Option opt) => _opt = opt;

    public string Key => _opt.Key;
    public string Title => _opt.Label;
    public string Desc => _opt.Description;
    public string Tag => _opt.Tag;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
