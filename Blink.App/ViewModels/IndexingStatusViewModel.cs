// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using Blink.App.Mvvm;
using Blink.Core.Indexing;

namespace Blink.App.ViewModels;

/// <summary>
/// App-wide indexing status, observed by both the Settings window (progress bar) and the
/// launcher footer (live caption + pulsing dot). Fed by <see cref="IndexingService"/>'s
/// <c>ProgressChanged</c>/<c>Completed</c> events (already marshaled to the UI thread by the
/// caller). All members are touched on the UI thread.
/// </summary>
public sealed class IndexingStatusViewModel : ObservableObject
{
    private bool _isIndexing;
    public bool IsIndexing { get => _isIndexing; private set { if (Set(ref _isIndexing, value)) RaiseDerived(); } }

    private int _processed;
    public int Processed { get => _processed; private set { if (Set(ref _processed, value)) RaiseDerived(); } }

    private int _total;
    public int Total { get => _total; private set { if (Set(ref _total, value)) RaiseDerived(); } }

    private string _currentName = "";
    public string CurrentName { get => _currentName; private set => Set(ref _currentName, value); }

    private long _documentCount;
    public long DocumentCount { get => _documentCount; private set { if (Set(ref _documentCount, value)) RaiseDerived(); } }

    public int Percent => _total > 0 ? (int)Math.Round(100.0 * _processed / _total) : 0;
    public bool HasCount => _documentCount > 0;

    /// <summary>Settings-window status line.</summary>
    public string StatusText => _isIndexing
        ? (_total > 0 ? $"인덱싱 중… {Processed:N0} / {Total:N0} ({Percent}%)" : "인덱싱 시작 중…")
        : (_documentCount > 0 ? $"완료 · {_documentCount:N0}개 문서 인덱싱됨" : "");

    /// <summary>Begin a new indexing run (resets counters).</summary>
    public void Begin()
    {
        Processed = 0;
        Total = 0;
        CurrentName = "";
        IsIndexing = true;
    }

    public void Report(IndexProgress p)
    {
        Total = p.Total;
        Processed = p.Processed;
        if (!string.IsNullOrEmpty(p.CurrentPath))
            CurrentName = System.IO.Path.GetFileName(p.CurrentPath);
    }

    public void Complete(long documentCount)
    {
        DocumentCount = documentCount;
        IsIndexing = false;
    }

    private void RaiseDerived()
    {
        Raise(nameof(Percent));
        Raise(nameof(HasCount));
        Raise(nameof(StatusText));
    }
}
