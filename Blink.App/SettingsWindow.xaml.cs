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
    private readonly ObservableCollection<FolderRow> _folders;
    private readonly ObservableCollection<IntervalOptionVm> _intervals;
    private string _selectedInterval;
    private string _selectedTheme;

    /// <summary>Raised when the user requests a re-index with the current folder set.</summary>
    public event Action<IReadOnlyList<string>>? ReindexRequested;

    /// <summary>Raised after the user saves, so the app can re-arm the auto-index cadence and sync UI.</summary>
    public event Action? SettingsSaved;

    public SettingsWindow(AppConfig config, IndexingStatusViewModel status)
    {
        InitializeComponent();
        _config = config;

        // Live indexing status drives the status line + progress bar bindings.
        DataContext = status;

        _folders = new ObservableCollection<FolderRow>(config.Folders.Select(f => new FolderRow(f)));
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

        // Theme segmented control — select the saved preference (triggers Theme_Checked once).
        _selectedTheme = NormalizeTheme(config.Theme);
        (_selectedTheme switch { "light" => ThemeLight, "system" => ThemeSystem, _ => ThemeDark }).IsChecked = true;
    }

    private static string NormalizeTheme(string? t) =>
        string.Equals(t, "light", StringComparison.OrdinalIgnoreCase) ? "light"
        : string.Equals(t, "system", StringComparison.OrdinalIgnoreCase) ? "system"
        : "dark";

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
            _folders.Add(new FolderRow(dlg.SelectedPath));
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

    // ── Theme ─────────────────────────────────────────────────────────────────
    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string theme }) return;
        _selectedTheme = theme;
        // Live-swap: ThemeManager publishes to app resources, so the launcher (DynamicResource) and
        // this window both update immediately.
        ThemeManager.Apply(ThemeManager.Resolve(theme), _config.AccentHue);
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
        _config.Theme = _selectedTheme;
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

/// <summary>A folder row: the full path split into a dimmed parent segment + leaf for display.</summary>
public sealed class FolderRow
{
    public string Path { get; }
    public string ParentSeg { get; }
    public string Leaf { get; }
    public string Sub => "검색 대상 폴더";

    public FolderRow(string path)
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
