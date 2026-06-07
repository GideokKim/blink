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

    public void Index(string root, IIndexStore store, IProgress<IndexProgress>? progress, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
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
