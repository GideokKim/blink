using Blink.Core.Indexing;
using Blink.Core.Store;

namespace Blink.Core.Tests;

public sealed class IndexerTests : IDisposable
{
    private readonly List<string> _tempPaths = new();
    private readonly List<string> _tempDirs = new();

    private SqliteFtsStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-idx-{Guid.NewGuid():N}.db");
        _tempPaths.Add(path);
        return new SqliteFtsStore(path);
    }

    private string NewTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public void Index_PopulatesStore_AndIsSearchable()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "a.txt"), "한글검색 본문 내용");
        File.WriteAllText(Path.Combine(dir, "b.md"), "# Title\nHello world markdown");
        File.WriteAllText(Path.Combine(dir, "ignore.bin"), "binary-ish");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "c.txt"), "중첩 폴더 문서");

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);

        Assert.Equal(4, store.Count()); // .bin is indexed filename-only, still a document
        Assert.Single(store.Search("한글검색"));
        Assert.Single(store.Search("world"));
        Assert.Single(store.Search("중첩"));
        // Filename-only doc is findable by its name
        Assert.Single(store.Search("ignore"));
    }

    [Fact]
    public void Index_SkipsJunkFiles()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "real.txt"), "실제 문서 내용");
        File.WriteAllText(Path.Combine(dir, "~$real.xlsx"), "office lock garbage"); // must be skipped
        File.WriteAllText(Path.Combine(dir, "Thumbs.db"), "thumb cache");           // must be skipped
        File.WriteAllText(Path.Combine(dir, "scratch.tmp"), "temp");                // must be skipped

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);

        Assert.Equal(1, store.Count());          // only real.txt
        Assert.Single(store.Search("real"));
        Assert.Empty(store.Search("Thumbs"));    // junk not indexed (not even by name)
    }

    [Fact]
    public void Index_HonorsBlinkignore()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "보관 문서");
        File.WriteAllText(Path.Combine(dir, "drop.txt"), "제외 문서");
        File.WriteAllText(Path.Combine(dir, ".blinkignore"), "drop.txt\n# comment\n");

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);

        Assert.Single(store.Search("보관"));
        Assert.Empty(store.Search("제외"));
    }

    [Fact]
    public void Index_ReportsProgress()
    {
        var dir = NewTree();
        for (int i = 0; i < 3; i++)
            File.WriteAllText(Path.Combine(dir, $"f{i}.txt"), $"문서 {i}");

        var reports = new List<IndexProgress>();
        using var store = NewStore();
        new Indexer().Index(dir, store, new Progress<IndexProgress>(p => { }), CancellationToken.None);

        // Use a synchronous collector (Progress<T> posts async); verify via a direct IProgress impl.
        var collector = new SyncProgress();
        using var store2 = NewStore();
        new Indexer().Index(dir, store2, collector, CancellationToken.None);
        // Each new file reports twice: once before extraction (current-file display) and once
        // after it is added. 3 fresh files → 6 reports; final processed/total still 3.
        Assert.Equal(6, collector.Reports.Count);
        Assert.Equal(3, collector.Reports[^1].Processed);
        Assert.Equal(3, collector.Reports[^1].Total);
        Assert.Contains(collector.Reports, r => r.CurrentPath is not null && r.CurrentPath.EndsWith(".txt"));
    }

    [Fact]
    public void Index_Cancellation_StopsEarly_LeavesConsistentDb()
    {
        var dir = NewTree();
        for (int i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(dir, $"f{i}.txt"), $"취소 테스트 {i}");

        using var store = NewStore();
        var cts = new CancellationTokenSource();
        var cancelAfterFirst = new SyncProgress(onReport: _ => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() =>
            new Indexer().Index(dir, store, cancelAfterFirst, cts.Token));

        // DB remains queryable (consistent) after cancellation.
        Assert.True(store.Count() >= 0);
        _ = store.Search("취소"); // does not throw
    }

    [Fact]
    public void Index_Incremental_SkipsUnchanged_ReindexesModified()
    {
        var dir = NewTree();
        var a = Path.Combine(dir, "a.txt");
        var b = Path.Combine(dir, "b.txt");
        File.WriteAllText(a, "처음 내용 알파");
        File.WriteAllText(b, "둘째 내용 베타");

        using var store = NewStore();
        var counting = new CountingStore(store);

        // First pass: both files indexed.
        new Indexer().Index(dir, counting, null, CancellationToken.None);
        Assert.Equal(2, counting.UpsertedDocs);
        Assert.Equal(2, store.Count());

        // Second pass with no changes: nothing re-upserted.
        counting.UpsertedDocs = 0;
        new Indexer().Index(dir, counting, null, CancellationToken.None);
        Assert.Equal(0, counting.UpsertedDocs);

        // Modify a's content AND bump its mtime; only a should be re-indexed.
        File.WriteAllText(a, "바뀐 내용 감마");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddSeconds(5));
        counting.UpsertedDocs = 0;
        new Indexer().Index(dir, counting, null, CancellationToken.None);
        Assert.Equal(1, counting.UpsertedDocs);

        Assert.Empty(store.Search("처음"));   // old content gone
        Assert.Single(store.Search("감마"));   // new content present
        Assert.Single(store.Search("베타"));   // b untouched
    }

    [Fact]
    public void Index_Incremental_NewFileIsAdded()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "a.txt"), "기존 문서");

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);
        Assert.Equal(1, store.Count());

        File.WriteAllText(Path.Combine(dir, "new.txt"), "신규 문서");
        new Indexer().Index(dir, store, null, CancellationToken.None);
        Assert.Equal(2, store.Count());
        Assert.Single(store.Search("신규"));
    }

    /// <summary>Wraps a store, counting how many documents pass through Upsert/UpsertMany.</summary>
    private sealed class CountingStore : IIndexStore
    {
        private readonly IIndexStore _inner;
        public int UpsertedDocs;
        public CountingStore(IIndexStore inner) => _inner = inner;

        public void Upsert(Blink.Core.Model.Document doc) { UpsertedDocs++; _inner.Upsert(doc); }
        public void UpsertMany(IEnumerable<Blink.Core.Model.Document> docs)
        {
            var list = docs.ToList();
            UpsertedDocs += list.Count;
            _inner.UpsertMany(list);
        }
        public void Delete(string docId) => _inner.Delete(docId);
        public void DeleteMany(IEnumerable<string> ids) => _inner.DeleteMany(ids);
        public IEnumerable<(string DocId, double Mtime)> IterDocsUnder(string root) => _inner.IterDocsUnder(root);
        public IReadOnlyList<Blink.Core.Model.SearchHit> Search(string q, int limit = 50, int offset = 0) => _inner.Search(q, limit, offset);
        public int Count() => _inner.Count();
        public (long FileCount, long TotalBytes) FolderStats(string root) => _inner.FolderStats(root);
        public void Dispose() { /* inner disposed by the test */ }
    }

    private sealed class SyncProgress : IProgress<IndexProgress>
    {
        public List<IndexProgress> Reports { get; } = new();
        private readonly Action<IndexProgress>? _onReport;
        public SyncProgress(Action<IndexProgress>? onReport = null) => _onReport = onReport;
        public void Report(IndexProgress value) { Reports.Add(value); _onReport?.Invoke(value); }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in _tempPaths)
            foreach (var f in new[] { p, p + "-wal", p + "-shm" })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }
}
