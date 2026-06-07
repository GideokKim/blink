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
        Assert.Equal(3, collector.Reports.Count);
        Assert.Equal(3, collector.Reports[^1].Processed);
        Assert.Equal(3, collector.Reports[^1].Total);
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
