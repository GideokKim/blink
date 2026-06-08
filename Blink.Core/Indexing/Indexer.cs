using Blink.Core.Model;
using Blink.Core.Parsers;
using Blink.Core.Store;

namespace Blink.Core.Indexing;

/// <summary>
/// Single-pass recursive indexer. Walks a folder, parses each file, and batch-upserts
/// <see cref="Document"/>s into an <see cref="IIndexStore"/>. Reports progress via
/// <c>IProgress&lt;IndexProgress&gt;</c> and honours cancellation between files.
///
/// Indexing is INCREMENTAL: it first reads the stored (doc_id, mtime) for the subtree
/// and skips files whose modification time is unchanged, re-parsing only new/modified
/// files. This matches the prototype and avoids re-reading an entire NAS/CloudDoc tree
/// on every run. (Deletions are handled separately by the pruner.)
/// </summary>
public sealed class Indexer
{
    private const int BatchSize = 500;

    private readonly FileExcluder? _excluder;

    /// <param name="excluder">
    /// Optional exclusion ruleset. When null, each <see cref="Index"/> call builds one
    /// from the built-in defaults plus a <c>.blinkignore</c> at the root.
    /// </param>
    public Indexer(FileExcluder? excluder = null) => _excluder = excluder;

    public void Index(string root, IIndexStore store, IProgress<IndexProgress>? progress, CancellationToken ct)
    {
        var excluder = _excluder ?? FileExcluder.ForRoot(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !excluder.IsExcluded(p, root))
            .ToList();

        // Existing (doc_id → stored mtime) for this subtree, to skip unchanged files.
        var known = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (docId, mtime) in store.IterDocsUnder(root))
            known[docId] = (long)mtime;

        int total = files.Count;
        int processed = 0;
        var batch = new List<Document>(BatchSize);

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            var docId = Path.GetFullPath(path);
            long mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
            processed++;

            // Skip files whose mtime matches what we already indexed.
            if (known.TryGetValue(docId, out var storedMtime) && storedMtime == mtime)
            {
                progress?.Report(new IndexProgress(processed, total, path));
                continue;
            }

            var parser = ParserRegistry.GetParser(path);
            string content = parser.ReadsContent ? SafeExtract(parser, path) : string.Empty;

            var doc = new Document(
                DocId: docId,
                Path: path,
                Mtime: mtime,
                Size: SafeSize(path),
                // Pure content; the store also indexes the filename for findability.
                Content: content);

            batch.Add(doc);

            if (batch.Count >= BatchSize)
            {
                store.UpsertMany(batch);
                batch.Clear();
            }

            progress?.Report(new IndexProgress(processed, total, path));
        }

        if (batch.Count > 0)
            store.UpsertMany(batch);
    }

    private static string SafeExtract(IParser parser, string path)
    {
        try { return parser.ExtractText(path); }
        catch { return string.Empty; } // unreadable file → filename-only
    }

    private static long SafeSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
