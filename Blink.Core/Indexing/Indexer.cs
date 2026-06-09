using Blink.Core.Model;
using Blink.Core.Parsers;
using Blink.Core.Store;

namespace Blink.Core.Indexing;

/// <summary>
/// Recursive indexer. Walks a folder, parses each file, and batch-upserts
/// <see cref="Document"/>s into an <see cref="IIndexStore"/>. Reports progress via
/// <c>IProgress&lt;IndexProgress&gt;</c> and honours cancellation.
///
/// THREE PASSES over a disk-backed <see cref="ScanCache"/> keep peak memory small at
/// target scale (millions of files in one tree):
///   1. scan  — one directory walk, spill every non-excluded path to disk;
///   2. plan  — stream the scan to count bundle candidates per (folder, extension);
///   3. apply — stream again to upsert individuals (incrementally) and collapse large
///              homogeneous groups into bundle markers.
///
/// INCREMENTAL: stored (doc_id, mtime) is read once; unchanged files are skipped
/// (deletions are handled by the pruner).
///
/// BUNDLING: filename-only files (images/data — anything with no content parser) that
/// pile up in one folder under one extension are collapsed, past
/// <see cref="_bundleThreshold"/>, into a single virtual "__bundle__&lt;ext&gt;" document
/// carrying a member count — so a folder of millions of sequentially-named images costs
/// one row, not millions. Content-bearing files (txt/pdf/docx/…) are never bundled.
/// Threshold 0 disables bundling.
/// </summary>
public sealed class Indexer
{
    private const int BatchSize = 500;

    /// <summary>Global ceiling on file size for body extraction; bigger files are filename-only.</summary>
    private const long MaxContentBytes = 100L * 1024 * 1024;

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
        var rootFull = Path.GetFullPath(root);

        // --- Pass 1: scan. One walk, spill paths to disk (no per-file stat yet). ---
        using var scan = new ScanCache();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (!excluder.IsExcluded(path, root))
                scan.Append(path);
        }
        scan.Seal();

        // --- Pass 2: plan. Count bundle candidates per (folder, extension). ---
        var counts = new Dictionary<(string Dir, string Ext), int>();
        int individualCount = 0;
        foreach (var path in scan.ReadAll())
        {
            ct.ThrowIfCancellationRequested();
            if (TryBundleKey(path, rootFull, out var key))
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            else
                individualCount++;
        }

        var bundleGroups = new HashSet<(string Dir, string Ext)>();
        int subThresholdMembers = 0;
        foreach (var (key, count) in counts)
        {
            if (count >= _bundleThreshold) bundleGroups.Add(key);
            else subThresholdMembers += count;
        }

        int total = individualCount + subThresholdMembers + bundleGroups.Count;
        int processed = 0;

        // Stored (doc_id → mtime); small once bundles collapse. Drives incremental skip
        // and bounds member deletions to entries that actually exist.
        var known = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (docId, mtime) in store.IterDocsUnder(root))
            known[docId] = (long)mtime;

        // Drop bundle markers for groups that are no longer bundles.
        var staleBundleIds = counts.Keys.Where(k => !bundleGroups.Contains(k))
            .Select(BundleId).Where(known.ContainsKey).ToList();
        if (staleBundleIds.Count > 0)
            store.DeleteMany(staleBundleIds);

        var batch = new List<Document>(BatchSize);
        var memberDeletes = new List<string>(BatchSize);

        // --- Pass 3: apply. Individuals incrementally; bundle members removed if present. ---
        foreach (var path in scan.ReadAll())
        {
            ct.ThrowIfCancellationRequested();

            if (TryBundleKey(path, rootFull, out var key) && bundleGroups.Contains(key))
            {
                // Member of a bundle: not indexed individually. Remove a prior individual
                // entry only if one exists (so steady-state re-index costs ~nothing).
                var memberId = Path.GetFullPath(path);
                if (known.ContainsKey(memberId))
                {
                    memberDeletes.Add(memberId);
                    if (memberDeletes.Count >= BatchSize) { store.DeleteMany(memberDeletes); memberDeletes.Clear(); }
                }
                continue;
            }

            var docId = Path.GetFullPath(path);
            long mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
            processed++;

            if (known.TryGetValue(docId, out var storedMtime) && storedMtime == mtime)
            {
                progress?.Report(new IndexProgress(processed, total, path));
                continue;
            }

            var parser = ParserRegistry.GetParser(path);
            long size = SafeSize(path);
            // Announce the file BEFORE the (potentially slow) extraction so the UI shows where it
            // is actually stuck, not the previously-finished file.
            progress?.Report(new IndexProgress(processed, total, path));
            string content = ShouldExtract(parser.ReadsContent, size, parser.MaxParseSize, MaxContentBytes)
                ? SafeExtract(parser, path) : string.Empty;

            batch.Add(new Document(docId, path, mtime, size, content));
            if (batch.Count >= BatchSize) { store.UpsertMany(batch); batch.Clear(); }

            progress?.Report(new IndexProgress(processed, total, path));
        }

        if (memberDeletes.Count > 0)
            store.DeleteMany(memberDeletes);

        // Emit one marker per bundle group.
        foreach (var key in bundleGroups)
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(new Document(
                DocId: BundleId(key),
                Path: key.Dir,                    // the folder is the display/open target
                Mtime: 0, Size: 0,                // members are not stat'd; a bundle is just a marker
                Content: key.Ext.TrimStart('.'),  // extension is searchable; folder name comes from Path
                IsBundle: true,
                MemberCount: counts[key]));
            processed++;
            if (batch.Count >= BatchSize) { store.UpsertMany(batch); batch.Clear(); }
            progress?.Report(new IndexProgress(processed, total, key.Dir));
        }

        if (batch.Count > 0)
            store.UpsertMany(batch);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a bundle candidate (filename-only file with an
    /// extension), and if so its (folder, extension) group key.
    /// </summary>
    private bool TryBundleKey(string path, string rootFull, out (string Dir, string Ext) key)
    {
        key = default;
        if (_bundleThreshold <= 0)
            return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext.Length == 0 || ParserRegistry.GetParser(path).ReadsContent)
            return false;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? rootFull;
        key = (dir, ext);
        return true;
    }

    /// <summary>Reserved file-name prefix marking a synthetic bundle entry.</summary>
    public const string BundleMarker = "__bundle__";

    /// <summary>True if <paramref name="docId"/> is a synthetic bundle entry (not a real file).</summary>
    public static bool IsBundleId(string docId)
        => Path.GetFileName(docId).StartsWith(BundleMarker, StringComparison.Ordinal);

    /// <summary>Synthetic doc id for a bundle group: <c>&lt;dir&gt;/__bundle__&lt;ext&gt;</c>.</summary>
    private static string BundleId((string Dir, string Ext) key)
        => Path.Combine(key.Dir, BundleMarker + key.Ext);

    /// <summary>
    /// Pure gate: extract a file's body only if its parser reads content AND its size is within
    /// the tighter of the global cap and the parser's own <see cref="IParser.MaxParseSize"/>.
    /// </summary>
    public static bool ShouldExtract(bool readsContent, long size, long? parserCap, long globalCap)
        => readsContent && size <= Math.Min(globalCap, parserCap ?? long.MaxValue);

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
