using Blink.Core.Store;

namespace Blink.Core.Indexing;

/// <summary>
/// Thrown when a prune is attempted against an index root that is not currently
/// reachable (e.g. a disconnected NAS / unmounted drive). Pruning then is unsafe: every
/// document would look deleted and the whole subtree's index would be wiped. Callers
/// should treat this as "skip pruning this root for now", not as data loss.
/// </summary>
public sealed class RootUnavailableException : Exception
{
    public string Root { get; }
    public RootUnavailableException(string root)
        : base($"Index root is not accessible; refusing to prune: {root}") => Root = root;
}

/// <summary>The read-only result of <see cref="Pruner.Plan"/>: which indexed docs no longer exist on disk.</summary>
public sealed record PrunePlan(IReadOnlyList<string> StaleDocIds, int Scanned)
{
    public int StaleCount => StaleDocIds.Count;
}

/// <summary>
/// Removes index entries for files that have been deleted or moved. Two phases, mirroring
/// the prototype's pruner: <see cref="Plan"/> is read-only (safe to show/preview) and
/// <see cref="Apply"/> commits the deletions. Both refuse to run when the root is
/// inaccessible (<see cref="RootUnavailableException"/>) so a flaky network mount can't
/// trigger a mass purge.
/// </summary>
public sealed class Pruner
{
    /// <summary>Compute stale doc ids under <paramref name="root"/> without modifying the store.</summary>
    /// <exception cref="RootUnavailableException">The root is not currently accessible.</exception>
    public PrunePlan Plan(string root, IIndexStore store)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
            throw new RootUnavailableException(fullRoot);

        var stale = new List<string>();
        int scanned = 0;
        foreach (var (docId, _) in store.IterDocsUnder(fullRoot))
        {
            scanned++;
            // A normal doc_id is the file's full path → stale if the file is gone.
            // A bundle entry is synthetic (no file on disk) → stale only if its folder is gone;
            // a shrunk-below-threshold group is handled by the indexer, not here.
            bool missing = Indexer.IsBundleId(docId)
                ? !Directory.Exists(Path.GetDirectoryName(docId) ?? docId)
                : !File.Exists(docId);
            if (missing)
                stale.Add(docId);
        }
        return new PrunePlan(stale, scanned);
    }

    /// <summary>Plan, then delete the stale entries. Returns the number of removed documents.</summary>
    /// <exception cref="RootUnavailableException">The root is not currently accessible.</exception>
    public int Apply(string root, IIndexStore store)
    {
        var plan = Plan(root, store);
        if (plan.StaleCount > 0)
            store.DeleteMany(plan.StaleDocIds);
        return plan.StaleCount;
    }
}
