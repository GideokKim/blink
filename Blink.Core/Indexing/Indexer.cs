using Blink.Core.Model;
using Blink.Core.Parsers;
using Blink.Core.Store;

namespace Blink.Core.Indexing;

/// <summary>
/// Recursive indexer. Walks a folder, parses each file, and batch-upserts
/// <see cref="Document"/>s into an <see cref="IIndexStore"/>. Reports progress via
/// <c>IProgress&lt;IndexProgress&gt;</c> and honours cancellation.
///
/// INCREMENTAL: reads the stored (doc_id, mtime) for the subtree and re-parses only
/// new/modified files (deletions are handled by the pruner).
///
/// BUNDLING: filename-only files (images, data — anything with no content parser) that
/// pile up in one folder under the same extension are collapsed, once a group reaches
/// <see cref="_bundleThreshold"/>, into a single virtual "bundle" document instead of one
/// row per file. This keeps the index small and indexing fast for folders holding
/// millions of sequentially-named files. Content-bearing files (txt/pdf/docx/…) are
/// never bundled — they stay individually searchable. Pass a threshold of 0 to disable.
/// </summary>
public sealed class Indexer
{
    private const int BatchSize = 500;

    private readonly FileExcluder? _excluder;
    private readonly int _bundleThreshold;

    /// <param name="excluder">
    /// Optional exclusion ruleset. When null, each <see cref="Index"/> call builds one
    /// from the built-in defaults plus a <c>.blinkignore</c> at the root.
    /// </param>
    /// <param name="bundleThreshold">
    /// Minimum same-folder/same-extension filename-only file count to collapse into a
    /// bundle entry. Default 100; 0 disables bundling.
    /// </param>
    public Indexer(FileExcluder? excluder = null, int bundleThreshold = 100)
    {
        _excluder = excluder;
        _bundleThreshold = bundleThreshold;
    }

    public void Index(string root, IIndexStore store, IProgress<IndexProgress>? progress, CancellationToken ct)
    {
        var excluder = _excluder ?? FileExcluder.ForRoot(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !excluder.IsExcluded(p, root));

        // --- Plan: separate individually-indexed files from bundle candidates. ---
        // A bundle candidate is a filename-only file (no content parser) with an extension;
        // candidates are grouped by (folder, extension) and collapsed only past the threshold.
        var groups = new Dictionary<(string Dir, string Ext), List<string>>();
        var individuals = new List<string>();

        foreach (var path in files)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (_bundleThreshold > 0 && ext.Length > 0 && !ParserRegistry.GetParser(path).ReadsContent)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Path.GetFullPath(root);
                var key = (dir, ext);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<string>();
                list.Add(path);
            }
            else
            {
                individuals.Add(path);
            }
        }

        var bundles = new List<((string Dir, string Ext) Key, List<string> Members)>();
        var staleBundleIds = new List<string>();
        foreach (var (key, members) in groups)
        {
            if (members.Count >= _bundleThreshold)
                bundles.Add((key, members));
            else
            {
                individuals.AddRange(members);     // below threshold → index individually
                staleBundleIds.Add(BundleId(key)); // and drop any bundle made for it on a prior run
            }
        }

        int total = individuals.Count + bundles.Count;
        int processed = 0;

        // Existing (doc_id → stored mtime) for this subtree, for incremental skip and to
        // bound member deletions to entries that actually exist.
        var known = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (docId, mtime) in store.IterDocsUnder(root))
            known[docId] = (long)mtime;

        // Drop obsolete bundle markers (group fell below threshold), but only if present.
        var staleToDelete = staleBundleIds.Where(known.ContainsKey).ToList();
        if (staleToDelete.Count > 0)
            store.DeleteMany(staleToDelete);

        var batch = new List<Document>(BatchSize);

        // --- Individual files (incremental) ---
        foreach (var path in individuals)
        {
            ct.ThrowIfCancellationRequested();

            var docId = Path.GetFullPath(path);
            long mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
            processed++;

            if (known.TryGetValue(docId, out var storedMtime) && storedMtime == mtime)
            {
                progress?.Report(new IndexProgress(processed, total, path));
                continue;
            }

            var parser = ParserRegistry.GetParser(path);
            string content = parser.ReadsContent ? SafeExtract(parser, path) : string.Empty;

            batch.Add(new Document(docId, path, mtime, SafeSize(path), content));
            if (batch.Count >= BatchSize) { store.UpsertMany(batch); batch.Clear(); }

            progress?.Report(new IndexProgress(processed, total, path));
        }

        // --- Bundle entries: remove any individually-indexed members, emit one marker. ---
        foreach (var (key, members) in bundles)
        {
            ct.ThrowIfCancellationRequested();

            // Only delete members that were actually indexed individually before (bounded
            // by `known`), so steady-state re-indexing of a huge bundle costs ~nothing.
            var toDelete = members.Select(Path.GetFullPath).Where(known.ContainsKey).ToList();
            if (toDelete.Count > 0)
                store.DeleteMany(toDelete);

            batch.Add(new Document(
                DocId: BundleId(key),
                Path: key.Dir,                    // the folder is the display/open target
                Mtime: 0, Size: 0,                // members are not stat'd; a bundle is just a marker
                Content: key.Ext.TrimStart('.'),  // extension is searchable; folder name comes from Path
                IsBundle: true,
                MemberCount: members.Count));
            processed++;
            if (batch.Count >= BatchSize) { store.UpsertMany(batch); batch.Clear(); }

            progress?.Report(new IndexProgress(processed, total, key.Dir));
        }

        if (batch.Count > 0)
            store.UpsertMany(batch);
    }

    /// <summary>Reserved file-name prefix marking a synthetic bundle entry.</summary>
    public const string BundleMarker = "__bundle__";

    /// <summary>True if <paramref name="docId"/> is a synthetic bundle entry (not a real file).</summary>
    public static bool IsBundleId(string docId)
        => Path.GetFileName(docId).StartsWith(BundleMarker, StringComparison.Ordinal);

    /// <summary>Synthetic doc id for a bundle group: <c>&lt;dir&gt;/__bundle__&lt;ext&gt;</c>.</summary>
    private static string BundleId((string Dir, string Ext) key)
        => Path.Combine(key.Dir, BundleMarker + key.Ext);

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
