// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blink.Core.Indexing;
using Blink.Core.Model;

namespace Blink.App;

/// <summary>
/// Lists, per configured folder, the documents whose body wasn't extracted — grouped by reason
/// with a hint — so a user on a DRM / on-demand drive can see why content search misses and
/// copy the evidence into an issue. "다시 시도" re-extracts that folder's failed files.
/// </summary>
public partial class ExtractFailuresWindow : Window
{
    private const int MaxRows = 500;

    private readonly Func<string, IReadOnlyList<ExtractFailure>> _list;
    private readonly Func<string, bool> _retry;
    private IReadOnlyList<ExtractFailure> _current = Array.Empty<ExtractFailure>();

    public ExtractFailuresWindow(IReadOnlyList<string> folders, string? initialFolder,
        Func<string, IReadOnlyList<ExtractFailure>> list, Func<string, bool> retry)
    {
        InitializeComponent();
        _list = list;
        _retry = retry;
        FolderPicker.ItemsSource = folders;
        FolderPicker.SelectedItem = initialFolder is not null && folders.Contains(initialFolder)
            ? initialFolder : folders.FirstOrDefault();
    }

    /// <summary>Reload the selected folder (App calls this after an indexing run completes).</summary>
    public void Refresh()
    {
        RetryStatus.Text = "";
        if (FolderPicker.SelectedItem is not string folder) { Groups.ItemsSource = null; return; }
        _current = _list(folder);
        Groups.ItemsSource = _current
            .GroupBy(f => f.Status)
            .OrderBy(g => g.Key.IsFailure() ? 0 : 1).ThenBy(g => g.Key)
            .Select(g => new FailureGroup(
                $"{ExtractStatusText.Label(g.Key)} · {g.Count():N0}",
                ExtractStatusText.Hint(g.Key),
                g.ToList()))
            .ToList();
        EmptyText.Visibility = _current.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LimitText.Visibility = _current.Count >= MaxRows ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FolderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ExtractStatusText.ToTsv(_current)); } catch { /* clipboard busy */ }
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (FolderPicker.SelectedItem is not string folder) return;
        // The retry can't start while another run is in flight (it would cancel that run); say so
        // instead of implying a retry that never happened.
        RetryStatus.Text = _retry(folder) ? "다시 시도 중…" : "인덱싱이 진행 중입니다. 끝난 뒤 다시 시도하세요.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    /// <summary>Max rows fetched per folder (the list shows a "first 500" note when reached).</summary>
    public static int RowLimit => MaxRows;
}

public sealed record FailureGroup(string Header, string Hint, IReadOnlyList<ExtractFailure> Rows);
