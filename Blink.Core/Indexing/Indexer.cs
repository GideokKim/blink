using Blink.Core.Model;
using Blink.Core.Parsers;
using Blink.Core.Store;

namespace Blink.Core.Indexing;

/// <summary>
/// Single-pass recursive indexer. Walks a folder, parses each file, and batch-upserts
/// <see cref="Document"/>s into an <see cref="IIndexStore"/>. Reports progress via
/// <c>IProgress&lt;IndexProgress&gt;</c> and honours cancellation between files.
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
        int total = files.Count;
        int processed = 0;
        var batch = new List<Document>(BatchSize);

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            var parser = ParserRegistry.GetParser(path);
            string content = parser.ReadsContent ? SafeExtract(parser, path) : string.Empty;

            var doc = new Document(
                DocId: Path.GetFullPath(path),
                Path: path,
                Mtime: new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds(),
                Size: SafeSize(path),
                // Pure content; the store also indexes the filename for findability.
                Content: content);

            batch.Add(doc);
            processed++;

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
