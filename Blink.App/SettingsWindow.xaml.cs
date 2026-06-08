// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Collections.ObjectModel;
using System.Windows;
using Blink.App.Interop;
using Blink.Core.Config;

namespace Blink.App;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly ObservableCollection<string> _folders;

    /// <summary>Raised when the user requests a re-index with the current folder set.</summary>
    public event Action<IReadOnlyList<string>>? ReindexRequested;

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        _folders = new ObservableCollection<string>(config.Folders);
        FoldersList.ItemsSource = _folders;
        DbPathText.Text = config.DbPath;
        // Reflect the actual registry state, falling back to the saved preference.
        AutostartCheck.IsChecked = AutostartManager.IsEnabled() || config.Autostart;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        // Windows Forms folder picker (UseWindowsForms=true). ASSUMPTION: WinForms interop is acceptable for the slice.
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK &&
            !string.IsNullOrWhiteSpace(dlg.SelectedPath) &&
            !_folders.Contains(dlg.SelectedPath))
        {
            _folders.Add(dlg.SelectedPath);
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FoldersList.SelectedItem is string sel)
            _folders.Remove(sel);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.Folders = _folders.ToArray();
        _config.Autostart = AutostartCheck.IsChecked == true;
        AutostartManager.Apply(_config.Autostart);
        _config.Save();
        MessageBox.Show("Saved.", "Blink", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Reindex_Click(object sender, RoutedEventArgs e)
    {
        _config.Folders = _folders.ToArray();
        _config.Save();
        ReindexRequested?.Invoke(_folders.ToArray());
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
