// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.IO;
using Blink.Core.Indexing;
using Blink.Core.Store;

namespace Blink.App;

/// <summary>
/// Runs <see cref="Indexer"/> on a background Task with progress reporting and cancellation.
/// Indexing never touches the UI thread; progress is marshaled back via <see cref="IProgress{T}"/>.
/// </summary>
internal sealed class IndexingService : IDisposable
{
    private readonly Indexer _indexer = new();
    private CancellationTokenSource? _cts;

    public event Action<IndexProgress>? ProgressChanged;
    public event Action? Completed;

    /// <summary>Re-index all configured folders into <paramref name="store"/> off the UI thread.</summary>
    public Task ReindexAsync(IEnumerable<string> folders, IIndexStore store)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var folderList = folders.ToList();

        // Progress<T> captures the current SynchronizationContext (the UI thread) so
        // ProgressChanged handlers run on the UI thread.
        var progress = new Progress<IndexProgress>(p => ProgressChanged?.Invoke(p));

        return Task.Run(() =>
        {
            foreach (var folder in folderList)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(folder))
                    _indexer.Index(folder, store, progress, ct);
            }
            Completed?.Invoke();
        }, ct);
    }

    public void CancelOngoing() => _cts?.Cancel();

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
