// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Collections.ObjectModel;
using Blink.App.Mvvm;
using Blink.Core.Launch;
using Blink.Core.Search;

namespace Blink.App.ViewModels;

/// <summary>
/// Drives the launcher: live search on every keystroke, the pinned calculator row, and a
/// single selection index shared by keyboard and mouse hover (per the design spec).
/// Backed by <see cref="LaunchSearch"/> over a supplied launch index.
/// </summary>
public sealed class LauncherViewModel : ObservableObject
{
    private const int RealLimit = 50;
    private readonly IReadOnlyList<LaunchItem> _index;
    private readonly ISearchProvider? _provider;
    private bool _useRealIndex;

    public LauncherViewModel(IReadOnlyList<LaunchItem> index, ISearchProvider? provider = null)
    {
        _index = index;
        _provider = provider;
    }

    /// <summary>
    /// Switch between the demo index (empty DB) and real FTS results (≥1 indexed doc).
    /// Re-runs the current query when the mode actually changes.
    /// </summary>
    public void SetIndexMode(int docCount)
    {
        bool real = docCount >= 1 && _provider is not null;
        if (_useRealIndex == real) return;
        _useRealIndex = real;
        UpdateResults();
    }

    public ObservableCollection<RowViewModel> Results { get; } = new();

    /// <summary>Total indexed items shown in the footer (demo value; replace with the real count).</summary>
    public long IndexCount { get; set; } = 248302;
    public string IndexCountText => IndexCount.ToString("N0");

    private string _query = "";
    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value ?? "")) UpdateResults(); }
    }

    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = Results.Count == 0 ? -1 : Math.Clamp(value, 0, Results.Count - 1);
            if (_selectedIndex == clamped) return;

            if (_selectedIndex >= 0 && _selectedIndex < Results.Count)
                Results[_selectedIndex].IsSelected = false;
            _selectedIndex = clamped;
            if (_selectedIndex >= 0)
                Results[_selectedIndex].IsSelected = true;

            Raise();
            Raise(nameof(Selected));
            Raise(nameof(HasSelection));
        }
    }

    public RowViewModel? Selected =>
        _selectedIndex >= 0 && _selectedIndex < Results.Count ? Results[_selectedIndex] : null;

    public bool HasSelection => Selected != null;
    public bool HasResults => Results.Count > 0;
    public bool IsEmptyState => _query.Trim().Length > 0 && Results.Count == 0;
    public int ResultCount => Results.Count;
    public string ResultCountText => $"{Results.Count}건";

    private void UpdateResults()
    {
        // Reset selection bookkeeping before rebuilding.
        _selectedIndex = -1;
        Results.Clear();

        var q = _query.Trim();
        if (q.Length > 0)
        {
            var calc = LaunchSearch.TryCalc(q);
            if (calc is not null)
                Results.Add(RowViewModel.ForCalc(calc));

            if (_useRealIndex && _provider is not null)
            {
                foreach (var hit in _provider.Search(q, RealLimit, 0))
                    Results.Add(new RowViewModel(HitToLaunchItem.Convert(hit, q, _provider), q));
            }
            else
            {
                foreach (var r in LaunchSearch.Search(q, _index))
                    Results.Add(new RowViewModel(r, q));
            }
        }

        if (Results.Count > 0)
        {
            _selectedIndex = 0;
            Results[0].IsSelected = true;
        }

        Raise(nameof(SelectedIndex));
        Raise(nameof(Selected));
        Raise(nameof(HasSelection));
        Raise(nameof(HasResults));
        Raise(nameof(IsEmptyState));
        Raise(nameof(ResultCount));
        Raise(nameof(ResultCountText));
    }

    /// <summary>↑/↓ navigation with wrap-around at both ends.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        int n = Results.Count;
        int next = ((SelectedIndex + delta) % n + n) % n; // wrap both ends
        SelectedIndex = next;
    }
}
