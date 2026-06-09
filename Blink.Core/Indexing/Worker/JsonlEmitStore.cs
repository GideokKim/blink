using System.Text.Json;
using Blink.Core.Model;
using Blink.Core.Store;

namespace Blink.Core.Indexing.Worker;

/// <summary>
/// An <see cref="IIndexStore"/> that does not persist anything: it WRITES each mutation as
/// a <see cref="WireOp"/> JSON line to a <see cref="TextWriter"/>, and answers
/// <see cref="IterDocsUnder"/> from a caller-supplied snapshot. Running the normal
/// <see cref="Indexer"/>/<see cref="Pruner"/> against this turns their store operations
/// into a stdout stream the worker client replays into the real database — so all file
/// I/O stays in the worker process and all SQLite I/O stays in the main process.
///
/// Query methods (<see cref="Search"/>, <see cref="Count"/>) are not used during indexing
/// and throw.
/// </summary>
public sealed class JsonlEmitStore : IIndexStore
{
    private readonly TextWriter _out;
    private readonly IReadOnlyList<(string DocId, double Mtime)> _known;

    public JsonlEmitStore(TextWriter output, IReadOnlyDictionary<string, long> known)
    {
        _out = output;
        _known = known.Select(kv => (kv.Key, (double)kv.Value)).ToList();
    }

    public void Upsert(Document doc) => WriteLine(new WireOp(WorkerProtocol.OpUpsert, Doc: doc));

    public void UpsertMany(IEnumerable<Document> docs)
    {
        foreach (var doc in docs)
            WriteLine(new WireOp(WorkerProtocol.OpUpsert, Doc: doc));
    }

    public void Delete(string docId) => WriteLine(new WireOp(WorkerProtocol.OpDelete, Ids: new[] { docId }));

    public void DeleteMany(IEnumerable<string> docIds)
    {
        var ids = docIds as string[] ?? docIds.ToArray();
        if (ids.Length > 0)
            WriteLine(new WireOp(WorkerProtocol.OpDelete, Ids: ids));
    }

    public IEnumerable<(string DocId, double Mtime)> IterDocsUnder(string root) => _known;

    public IReadOnlyList<SearchHit> Search(string query, int limit = 50, int offset = 0)
        => throw new NotSupportedException("JsonlEmitStore is write-only (worker emit).");

    public int Count() => throw new NotSupportedException("JsonlEmitStore is write-only (worker emit).");

    public (long FileCount, long TotalBytes) FolderStats(string root)
        => throw new NotSupportedException("JsonlEmitStore is write-only (worker emit).");

    public void Dispose() => _out.Flush();

    private void WriteLine(WireOp op) => _out.WriteLine(JsonSerializer.Serialize(op, WorkerProtocol.Json));
}
