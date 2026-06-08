using Blink.Core.Indexing;
using Blink.Core.Indexing.Worker;
using Blink.Core.Store;
using Microsoft.Data.Sqlite;

namespace Blink.Core.Tests;

public sealed class WorkerTests : IDisposable
{
    private readonly List<string> _tempPaths = new();
    private readonly List<string> _tempDirs = new();

    private SqliteFtsStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-worker-{Guid.NewGuid():N}.db");
        _tempPaths.Add(path);
        return new SqliteFtsStore(path);
    }

    private string NewTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-worker-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static void Populate(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "a.txt"), "한글검색 본문");
        File.WriteAllText(Path.Combine(dir, "b.md"), "hello world");
        for (int i = 0; i < 6; i++) // images → should bundle at threshold 3
            File.WriteAllText(Path.Combine(dir, $"IMG_{i:D4}.jpg"), "x");
    }

    [Fact]
    public void ApplyStream_AppliesUpsertsAndDeletes()
    {
        using var store = NewStore();
        // Two upserts then a delete of the first — replayed in order.
        var lines = string.Join("\n", new[]
        {
            "{\"op\":\"up\",\"doc\":{\"DocId\":\"/x/a.txt\",\"Path\":\"/x/a.txt\",\"Mtime\":0,\"Size\":3,\"Content\":\"가나다\",\"IsBundle\":false,\"MemberCount\":0}}",
            "{\"op\":\"up\",\"doc\":{\"DocId\":\"/x/b.txt\",\"Path\":\"/x/b.txt\",\"Mtime\":0,\"Size\":3,\"Content\":\"라마바\",\"IsBundle\":false,\"MemberCount\":0}}",
            "{\"op\":\"del\",\"ids\":[\"/x/a.txt\"]}",
        });

        var upserted = WorkerIndexClient.ApplyStream(new StringReader(lines), store);
        Assert.Equal(2, upserted);
        Assert.Equal(1, store.Count());           // a deleted, b remains
        Assert.Single(store.Search("라마바"));
        Assert.Empty(store.Search("가나다"));
    }

    [Fact]
    public void WorkerPipeline_MatchesDirectIndexer()
    {
        var dir = NewTree();
        Populate(dir);

        // Worker side: emit ops to a buffer (fresh = empty known).
        var buffer = new StringWriter();
        WorkerIndexer.Run(dir, new Dictionary<string, long>(), buffer, bundleThreshold: 3);

        // Client side: replay into a store.
        using var viaWorker = NewStore();
        WorkerIndexClient.ApplyStream(new StringReader(buffer.ToString()), viaWorker);

        // Direct in-process indexer for comparison.
        using var direct = NewStore();
        new Indexer(bundleThreshold: 3).Index(dir, direct, null, CancellationToken.None);

        Assert.Equal(direct.Count(), viaWorker.Count());
        Assert.Equal(direct.Search("한글검색").Count, viaWorker.Search("한글검색").Count);
        Assert.Equal(direct.Search("world").Count, viaWorker.Search("world").Count);
        // The image group collapsed to a bundle in both.
        var bundleId = Path.Combine(Path.GetFullPath(dir), "__bundle__.jpg");
        Assert.Equal(6, viaWorker.GetBundleSizes(new[] { bundleId })[bundleId]);
    }

    [Fact]
    public void WorkerPipeline_Incremental_EmitsNothingWhenUnchanged()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "only.txt"), "변경 없음"); // content-only: no bundle re-emit

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);

        var known = WorkerIndexClient.SnapshotKnown(store, dir);
        var buffer = new StringWriter();
        WorkerIndexer.Run(dir, known, buffer);

        Assert.Equal(0, WorkerIndexClient.ApplyStream(new StringReader(buffer.ToString()), store));
    }

    [Fact]
    public void WorkerPipeline_EmitsDeleteForRemovedFile()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "보관");
        var gone = Path.Combine(dir, "gone.txt");
        File.WriteAllText(gone, "삭제됨");

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);
        Assert.Equal(2, store.Count());

        File.Delete(gone);
        var known = WorkerIndexClient.SnapshotKnown(store, dir);
        var buffer = new StringWriter();
        WorkerIndexer.Run(dir, known, buffer); // prune detection happens worker-side

        WorkerIndexClient.ApplyStream(new StringReader(buffer.ToString()), store);
        Assert.Equal(1, store.Count());
        Assert.Empty(store.Search("삭제됨"));
    }

    [Fact]
    public void WorkerRun_InaccessibleRoot_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"blink-nope-{Guid.NewGuid():N}");
        Assert.Throws<RootUnavailableException>(() =>
            WorkerIndexer.Run(missing, new Dictionary<string, long>(), new StringWriter()));
    }

    // ---- Real process spawn: proves the stdout JSON-line IPC end to end. ----
    [Fact]
    public void IndexViaWorker_SpawnsProcess_AndPopulatesStore()
    {
        var workerDll = Path.Combine(AppContext.BaseDirectory, "Blink.Indexer.Worker.dll");
        Assert.True(File.Exists(workerDll), $"worker dll not found at {workerDll}");

        var dir = NewTree();
        Populate(dir);

        using var store = NewStore();
        var exit = new WorkerIndexClient().IndexViaWorker(dir, store, workerDll, bundleThreshold: 3);

        Assert.Equal(0, exit);
        Assert.Single(store.Search("한글검색"));
        Assert.Single(store.Search("world"));
        var bundleId = Path.Combine(Path.GetFullPath(dir), "__bundle__.jpg");
        Assert.Equal(6, store.GetBundleSizes(new[] { bundleId })[bundleId]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in _tempPaths)
            foreach (var f in new[] { p, p + "-wal", p + "-shm" })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }
}
