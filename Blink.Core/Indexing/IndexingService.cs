using Blink.Core.Store;

namespace Blink.Core.Indexing;

/// <summary>
/// Runs <see cref="Indexer"/> on a background Task with progress reporting and cancellation.
/// Indexing never touches the UI thread; progress is marshaled back via <see cref="IProgress{T}"/>.
/// </summary>
public sealed class IndexingService : IDisposable
{
    private readonly Indexer _indexer = new();
    private CancellationTokenSource? _cts;
    private int _running;

    public event Action<IndexProgress>? ProgressChanged;
    public event Action? Completed;

    /// <summary>Fired (on the background thread) after a folder is fully indexed + pruned.</summary>
    public event Action<string>? FolderCompleted;

    /// <summary>Fired (on the background thread) when one folder fails; the run moves on to the next.</summary>
    public event Action<string, Exception>? FolderFailed;

    /// <summary>True while a reindex run is in flight. Auto-cadence ticks use this to avoid
    /// cancelling and restarting an ongoing run.</summary>
    public bool IsBusy => Volatile.Read(ref _running) > 0;

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
            Interlocked.Increment(ref _running);
            try
            {
                var pruner = new Pruner();
                foreach (var folder in folderList)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(folder))
                        continue;

                    try
                    {
                        _indexer.Index(folder, store, progress, ct);

                        // Remove entries for files deleted since the last run. Guarded against
                        // a vanished root (RootUnavailableException) so a transient mount drop
                        // can't purge the index.
                        try { pruner.Apply(folder, store); }
                        catch (RootUnavailableException) { /* skip prune for this root */ }

                        FolderCompleted?.Invoke(folder);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // One bad folder (network drop, access denied, …) must not kill the
                        // whole run: surface it and move on to the next folder.
                        FolderFailed?.Invoke(folder, ex);
                    }
                }

                // A cancelled run never reports Completed — the superseding run owns that.
                ct.ThrowIfCancellationRequested();
                Completed?.Invoke();
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }, ct);
    }

    public void CancelOngoing() => _cts?.Cancel();

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
