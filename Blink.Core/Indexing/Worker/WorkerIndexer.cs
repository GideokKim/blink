using Blink.Core.Store;

namespace Blink.Core.Indexing.Worker;

/// <summary>
/// The worker-process side of indexing. Performs ALL file I/O (enumerate, stat, parse,
/// existence checks for pruning) and streams the resulting store operations as JSON lines
/// to <paramref name="output"/>. Pure with respect to persistence — it never opens the
/// database. Exposed as a static method so it can be exercised in-process by tests as well
/// as from the worker executable.
/// </summary>
public static class WorkerIndexer
{
    /// <summary>
    /// Index <paramref name="root"/> incrementally (relative to <paramref name="known"/>) and
    /// prune entries for deleted files, emitting upsert/delete ops to <paramref name="output"/>.
    /// </summary>
    /// <exception cref="RootUnavailableException">Root is not accessible (caller maps to a skip).</exception>
    public static void Run(
        string root,
        IReadOnlyDictionary<string, long> known,
        TextWriter output,
        int bundleThreshold = 100,
        IProgress<IndexProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(root))
            throw new RootUnavailableException(Path.GetFullPath(root));

        using var sink = new JsonlEmitStore(output, known);
        new Indexer(bundleThreshold: bundleThreshold).Index(root, sink, progress, ct);
        // Prune deleted files too — File.Exists checks must run here (in the worker), not
        // in the main process, so the SMB access stays isolated.
        new Pruner().Apply(root, sink);
    }
}
