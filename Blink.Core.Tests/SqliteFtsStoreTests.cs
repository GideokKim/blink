using System.Text;
using Blink.Core.Model;
using Blink.Core.Store;

namespace Blink.Core.Tests;

public sealed class SqliteFtsStoreTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    private SqliteFtsStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-test-{Guid.NewGuid():N}.db");
        _tempPaths.Add(path);
        return new SqliteFtsStore(path);
    }

    private static Document Doc(string docId, string content) =>
        new(DocId: docId, Path: docId, Mtime: 0, Size: content.Length, Content: content);

    /// <summary>
    /// A fresh OS-absolute directory path (not created on disk). Used as a FolderStats root so
    /// stored doc_ids survive <see cref="Path.GetFullPath"/> on every platform — a literal
    /// "/root" would become "C:\\root" on Windows and never match the stored ids.
    /// </summary>
    private static string FreshRoot(string tag) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"blink-{tag}-{Guid.NewGuid():N}"));

    // ---- The mandatory Hangul round-trip correctness gate (written FIRST) ----
    [Fact]
    public void HangulRoundTrip_TwoGramAndMultiGramQueriesHit()
    {
        using var store = NewStore();
        store.Upsert(Doc("/docs/a.txt", "여기 한글검색 테스트"));

        // 2-gram substring query
        Assert.Single(store.Search("글검"));
        // Full 4-char query -> tokenizes to multiple n-grams, AND-joined
        Assert.Single(store.Search("한글검색"));
        // Absent term
        Assert.Empty(store.Search("없는말"));
        // Empty / whitespace query issues no MATCH and does not throw
        Assert.Empty(store.Search(""));
        Assert.Empty(store.Search("   "));
    }

    [Fact]
    public void NfdInput_StillMatchesNfcQuery()
    {
        using var store = NewStore();
        var nfd = "한글검색".Normalize(NormalizationForm.FormD);
        store.Upsert(Doc("/docs/nfd.txt", nfd));

        // Query in NFC must still hit the NFD-stored document (tokenizer normalizes both to NFC).
        Assert.Single(store.Search("한글"));
        Assert.Single(store.Search("한글검색"));
    }

    [Fact]
    public void Bm25Ordering_MoreRelevantRanksFirst()
    {
        using var store = NewStore();
        store.Upsert(Doc("/docs/few.txt", "한글검색 한 번"));
        store.Upsert(Doc("/docs/many.txt", "한글검색 한글검색 한글검색 자주"));

        var hits = store.Search("한글검색");
        Assert.Equal(2, hits.Count);
        // Lower bm25 = better; the doc with more occurrences should rank first.
        Assert.Equal("/docs/many.txt", hits[0].DocId);
        Assert.True(hits[0].Score <= hits[1].Score);
    }

    [Fact]
    public void AsciiSearch_Works()
    {
        using var store = NewStore();
        store.Upsert(Doc("/docs/readme.md", "Hello World from Blink"));
        Assert.Single(store.Search("world"));   // case-insensitive
        Assert.Single(store.Search("blink"));
        Assert.Empty(store.Search("missing"));
    }

    [Fact]
    public void Upsert_SameDocId_ReplacesContent()
    {
        using var store = NewStore();
        store.Upsert(Doc("/docs/x.txt", "처음 내용"));
        store.Upsert(Doc("/docs/x.txt", "바뀐 내용"));

        Assert.Equal(1, store.Count());
        Assert.Empty(store.Search("처음"));
        Assert.Single(store.Search("바뀐"));
    }

    [Fact]
    public void Delete_RemovesDocument()
    {
        using var store = NewStore();
        store.Upsert(Doc("/docs/x.txt", "한글검색"));
        Assert.Equal(1, store.Count());
        store.Delete("/docs/x.txt");
        Assert.Equal(0, store.Count());
        Assert.Empty(store.Search("한글"));
    }

    [Fact]
    public void Count_ReflectsUpserts()
    {
        using var store = NewStore();
        Assert.Equal(0, store.Count());
        store.UpsertMany(new[] { Doc("/a.txt", "가나다"), Doc("/b.txt", "라마바") });
        Assert.Equal(2, store.Count());
    }

    [Fact]
    public void Pagination_LimitOffset()
    {
        using var store = NewStore();
        for (int i = 0; i < 5; i++)
            store.Upsert(Doc($"/docs/{i}.txt", "공통키워드 내용"));

        var page1 = store.Search("공통", limit: 2, offset: 0);
        var page2 = store.Search("공통", limit: 2, offset: 2);
        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Empty(page1.Select(h => h.DocId).Intersect(page2.Select(h => h.DocId)));
    }

    [Fact]
    public void ConcurrentReadsAndWrites_DoNotThrow()
    {
        // Regression for the shared-connection misuse bug (P0-3): background writes
        // (indexer) and foreground reads (search) hit the single connection at once.
        // Before serialization this raised "SQLite Error 21 (misuse)" / "database is
        // locked"; the gate must make it crash-free.
        using var store = NewStore();
        store.Upsert(Doc("/seed.txt", "공통키워드 시드"));

        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        using var cts = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 200 && !cts.IsCancellationRequested; i++)
                    store.Upsert(Doc($"/docs/{i}.txt", $"공통키워드 본문 {i}"));
            }
            catch (Exception ex) { errors.Enqueue(ex); }
            finally { cts.Cancel(); }
        });

        var reader = Task.Run(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    _ = store.Search("공통");
                    _ = store.Count();
                }
            }
            catch (Exception ex) { errors.Enqueue(ex); }
        });

        Task.WaitAll(writer, reader);
        Assert.Empty(errors);
        Assert.True(store.Count() >= 1);
    }

    [Fact]
    public void Search_DoesNotBlock_DuringOpenWriteTransaction()
    {
        // The key WAL two-connection guarantee: while UpsertMany holds the write gate
        // and an open transaction (background indexing batch), Search on the read-only
        // connection must still return promptly instead of queuing behind the writer.
        using var store = NewStore();
        store.Upsert(Doc("/seed.txt", "공통키워드 시드"));

        using var writerEntered = new ManualResetEventSlim(false);
        using var releaseWriter = new ManualResetEventSlim(false);

        IEnumerable<Document> BlockingDocs()
        {
            // Yield one doc so the transaction has done real work, then stall
            // mid-iteration: the write transaction + _gate stay held open.
            yield return Doc("/docs/blocking.txt", "공통키워드 본문");
            writerEntered.Set();
            releaseWriter.Wait();
            yield return Doc("/docs/after.txt", "공통키워드 추가");
        }

        var writer = new Thread(() => store.UpsertMany(BlockingDocs()));
        writer.Start();
        try
        {
            Assert.True(writerEntered.Wait(TimeSpan.FromSeconds(5)),
                "writer thread never entered the upsert transaction");

            IReadOnlyList<SearchHit>? hits = null;
            var search = Task.Run(() => hits = store.Search("공통"));
            Assert.True(search.Wait(TimeSpan.FromSeconds(2)),
                "Search blocked behind an open write transaction");
            Assert.NotNull(hits);
            Assert.Contains(hits!, h => h.DocId == "/seed.txt");
        }
        finally
        {
            // Always unblock and join the writer so a failed assert can't hang the run.
            releaseWriter.Set();
            writer.Join();
        }

        // After the writer commits, its docs are visible.
        Assert.Equal(3, store.Count());
    }

    [Fact]
    public void Search_SeesCommittedWrites()
    {
        // Writes go through the write connection; reads through the read-only one.
        // WAL guarantees a committed write is visible to subsequent reads.
        using var store = NewStore();
        store.Upsert(Doc("/docs/new.txt", "신규문서 내용"));

        var hits = store.Search("신규문서");
        Assert.Single(hits);
        Assert.Equal("/docs/new.txt", hits[0].DocId);
        Assert.Equal("신규문서 내용", store.GetContent("/docs/new.txt"));
    }

    [Fact]
    public void FolderStats_MultipleFilesInNestedSubfolders_ReturnsSummedCountAndBytes()
    {
        using var store = NewStore();
        // a.txt size 100, sub/b.txt size 200 → FileCount=2, TotalBytes=300
        var root = FreshRoot("fs");
        var a = Path.Combine(root, "a.txt");
        var b = Path.Combine(root, "sub", "b.txt");
        store.Upsert(new Document(DocId: a, Path: a, Mtime: 0, Size: 100, Content: "a"));
        store.Upsert(new Document(DocId: b, Path: b, Mtime: 0, Size: 200, Content: "b"));

        var (count, bytes) = store.FolderStats(root);
        Assert.Equal(2L, count);
        Assert.Equal(300L, bytes);
    }

    [Fact]
    public void FolderStats_BundleRow_ContributesMemberCountToFileCount()
    {
        using var store = NewStore();
        // A bundle row under root: IsBundle=true, MemberCount=5, Size=500
        var root = FreshRoot("fs");
        var bundle = Path.Combine(root, "__bundle__.jpg");
        store.Upsert(new Document(DocId: bundle, Path: bundle,
            Mtime: 0, Size: 500, Content: "", IsBundle: true, MemberCount: 5));

        var (count, bytes) = store.FolderStats(root);
        Assert.Equal(5L, count);
        Assert.Equal(500L, bytes);
    }

    [Fact]
    public void FolderStats_DocumentNotUnderRoot_IsExcluded()
    {
        using var store = NewStore();
        var root = FreshRoot("fs");
        var other = FreshRoot("other");
        var a = Path.Combine(root, "a.txt");
        var b = Path.Combine(other, "b.txt");
        store.Upsert(new Document(DocId: a, Path: a, Mtime: 0, Size: 100, Content: "a"));
        store.Upsert(new Document(DocId: b, Path: b, Mtime: 0, Size: 999, Content: "b"));

        var (count, bytes) = store.FolderStats(root);
        Assert.Equal(1L, count);
        Assert.Equal(100L, bytes);
    }

    [Fact]
    public void FolderStats_EmptyRoot_ReturnsZeros()
    {
        using var store = NewStore();
        var root = FreshRoot("fs");
        var other = FreshRoot("other");
        var a = Path.Combine(other, "a.txt");
        store.Upsert(new Document(DocId: a, Path: a, Mtime: 0, Size: 50, Content: "x"));

        var (count, bytes) = store.FolderStats(root);
        Assert.Equal(0L, count);
        Assert.Equal(0L, bytes);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in _tempPaths)
        {
            foreach (var f in new[] { p, p + "-wal", p + "-shm" })
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
            }
        }
    }
}
