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
    private readonly SearchCoordinator _coordinator = new();
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
        _ = RefreshAsync();
    }

    public ObservableCollection<RowViewModel> Results { get; } = new();

    /// <summary>Total indexed items shown in the footer (demo value; replace with the real count).</summary>
    public long IndexCount { get; set; } = 248302;
    public string IndexCountText => IndexCount.ToString("N0");

    private string _query = "";
    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value ?? "")) _ = RefreshAsync(); }
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
    public bool IsEmptyState => !IsSearching && _query.Trim().Length > 0 && Results.Count == 0;
    public int ResultCount => Results.Count;
    public string ResultCountText => $"{Results.Count}건";

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        private set { if (Set(ref _isSearching, value)) Raise(nameof(IsEmptyState)); }
    }

    /// <summary>
    /// 비동기 검색 파이프라인: 이전 검색을 즉시 취소(superseded)하고 백그라운드에서 검색한 뒤,
    /// UI 스레드 재개 시 세대 검사를 통과한 결과만 적용한다. 검색 중에는 기존 결과를
    /// 지우지 않아 blank-flash가 없고, 빈 쿼리는 즉시 비운다 (Escape 즉답).
    /// </summary>
    private async Task RefreshAsync()
    {
        var (gen, ct) = _coordinator.Begin();           // 이전 검색 즉시 취소
        var q = _query.Trim();
        bool real = _useRealIndex && _provider is not null;

        IReadOnlyList<LaunchResult> rows;
        if (q.Length == 0)
        {
            rows = Array.Empty<LaunchResult>();         // 빈 쿼리는 즉시 적용 (Escape 즉답)
        }
        else
        {
            IsSearching = true;
            try
            {
                rows = await Task.Run(() => real
                    ? HitToLaunchItem.ConvertAll(
                        _provider!.Search(q, SearchPolicy.EffectiveLimit(q, RealLimit), 0),
                        q, _provider!, ct)
                    : LaunchSearch.Search(q, _index), ct);
            }
            catch (OperationCanceledException) { return; }   // superseded — 최신 검색이 UI를 책임짐
            catch (Exception ex)                              // 검색 실패 → 빈 결과 (앱 보호)
            {
                System.Diagnostics.Trace.WriteLine($"[Blink] search failed: {ex}");
                rows = Array.Empty<LaunchResult>();
            }
        }

        // await 재개 = WPF SynchronizationContext = UI 스레드.
        // IsCurrent 검사 후 await 없이 곧장 적용 → stale 적용 경합 차단.
        if (!_coordinator.IsCurrent(gen)) return;
        ApplyResults(q, rows);
        IsSearching = false;
    }

    /// <summary>UI 스레드 전용: 검색 결과를 Results/SelectedIndex에 반영하고 일괄 통지.</summary>
    private void ApplyResults(string q, IReadOnlyList<LaunchResult> rows)
    {
        // Reset selection bookkeeping before rebuilding.
        _selectedIndex = -1;
        Results.Clear();

        if (q.Length > 0)
        {
            var calc = LaunchSearch.TryCalc(q);
            if (calc is not null)
                Results.Add(RowViewModel.ForCalc(calc));
        }

        foreach (var r in rows)
            Results.Add(new RowViewModel(r, q));

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

    /// <summary>
    /// Invalidate any in-flight search (e.g. when the launcher hides) so late
    /// background results are never applied to the UI.
    /// </summary>
    public void CancelSearch() => _coordinator.CancelPending();

    /// <summary>↑/↓ navigation with wrap-around at both ends.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        int n = Results.Count;
        int next = ((SelectedIndex + delta) % n + n) % n; // wrap both ends
        SelectedIndex = next;
    }
}
