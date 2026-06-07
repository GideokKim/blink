// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Blink.App.Interop;
using Blink.Core.Search;

namespace Blink.App;

public partial class LauncherWindow : Window
{
    private readonly SearchController _controller;
    private readonly DispatcherTimer _toastTimer;
    private bool _suppressHide;
    private ResultRow? _selected;

    // ASSUMPTION: ISearchProvider is Blink.Core.Search.InProcessProvider, constructed by App with a SqliteFtsStore.
    internal LauncherWindow(ISearchProvider provider)
    {
        InitializeComponent();
        _controller = new SearchController(provider);
        ResultsList.ItemsSource = _controller.Results;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); HideToast(); };

        Deactivated += (_, _) => { if (!_suppressHide) HideLauncher(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Apply acrylic backdrop; on failure the solid window background remains (legible fallback).
        AcrylicHelper.TryApply(hwnd, (int)Width, (int)ActualHeight > 0 ? (int)ActualHeight : 200);
    }

    /// <summary>Summon the launcher: reset, position at top-third of the primary monitor, focus.</summary>
    public void Summon()
    {
        SearchBox.Text = "";
        PositionTopThird();
        Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        ForegroundHelper.BringToForeground(hwnd);
        SearchBox.Focus();
    }

    private void PositionTopThird()
    {
        var area = SystemParameters.WorkArea; // primary monitor work area
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height / 6; // upper third
    }

    public void HideLauncher()
    {
        _toastTimer.Stop();
        HideToast();
        Hide();
    }

    // ── Search box ────────────────────────────────────────────────────────────
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => _controller.OnQueryChanged(SearchBox.Text);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HideLauncher();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                InvokeSelected(shift);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_controller.Results.Count == 0) return;
        int idx = ResultsList.SelectedIndex < 0 ? 0 : ResultsList.SelectedIndex + delta;
        idx = Math.Clamp(idx, 0, _controller.Results.Count - 1);
        ResultsList.SelectedIndex = idx;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    /// <summary>Invoke the selected result. <paramref name="parentFolder"/> = Shift+Enter behavior.</summary>
    private void InvokeSelected(bool parentFolder)
    {
        if (ResultsList.SelectedItem is not ResultRow row || row.IsSentinel || row.Hit is null)
            return;

        var path = row.Hit.Path;
        if (parentFolder)
        {
            Actions.OpenParentFolder(path);
            HideLauncher();
            return;
        }

        var result = Actions.OpenDefault(path);
        if (result.Ok)
        {
            HideLauncher();
        }
        else
        {
            // Fallback: reveal in parent folder, show a ~2s toast, suppress focus-hide meanwhile.
            Actions.OpenParentFolder(path);
            ShowToast($"Couldn't open file — revealed in folder ({result.Reason})");
        }
    }

    // ── Selection → inline match lines ──────────────────────────────────────────
    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selected is not null)
        {
            _selected.IsSelected = false;
            _selected.MatchLines.Clear();
        }

        if (ResultsList.SelectedItem is ResultRow row && !row.IsSentinel)
        {
            row.IsSelected = true;
            row.MatchLines.Clear();
            foreach (var ml in _controller.MatchLinesFor(row))
                row.MatchLines.Add(ml);
            _selected = row;
        }
        else
        {
            _selected = null;
        }
    }

    // ── Infinite scroll / pagination ───────────────────────────────────────────
    private void ResultsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange <= 0) return;
        // Near the bottom (within ~2 rows) → request the next page.
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 2)
            _controller.RequestMore();
    }

    // ── Toast ───────────────────────────────────────────────────────────────────
    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _suppressHide = true;     // don't hide while the toast is up
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        Toast.Visibility = Visibility.Collapsed;
        _suppressHide = false;
        HideLauncher();           // after the toast, hide the launcher
    }
}
