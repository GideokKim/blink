using Blink.Core.Model;

namespace Blink.Core.Store;

/// <summary>
/// Persistence + search backend for <see cref="Document"/>s.
/// The vertical slice ships a single FTS5-backed implementation (<c>SqliteFtsStore</c>).
/// </summary>
public interface IIndexStore : IDisposable
{
    /// <summary>Insert or replace a single document (originals + tokenized FTS row in one transaction).</summary>
    void Upsert(Document doc);

    /// <summary>Batch insert/replace. Implementations should wrap the batch in one transaction.</summary>
    void UpsertMany(IEnumerable<Document> docs);

    /// <summary>Remove a single document by its <see cref="Document.DocId"/>.</summary>
    void Delete(string docId);

    /// <summary>Batch remove by doc id.</summary>
    void DeleteMany(IEnumerable<string> docIds);

    /// <summary>
    /// Enumerate (docId, mtime) for every document whose path is under <paramref name="root"/>.
    /// Forward-looking scaffolding for the deferred pruner / incremental re-index.
    /// </summary>
    IEnumerable<(string DocId, double Mtime)> IterDocsUnder(string root);

    /// <summary>
    /// Tokenize <paramref name="query"/> with the same n-gram tokenizer used for indexing,
    /// AND-join the resulting quoted terms, and return hits ordered by <c>bm25()</c> ascending
    /// then by <see cref="Document.DocId"/>. An empty/whitespace query returns an empty list
    /// (no MATCH is issued).
    /// </summary>
    IReadOnlyList<SearchHit> Search(string query, int limit = 50, int offset = 0);

    /// <summary>Total number of indexed documents.</summary>
    int Count();

    /// <summary>
    /// Aggregate stats for all documents whose path is under <paramref name="root"/>:
    /// file count (bundle entries count as their member_count) and total bytes (SUM(size)).
    /// A root with no documents returns (0, 0).
    /// </summary>
    (long FileCount, long TotalBytes) FolderStats(string root);
}
