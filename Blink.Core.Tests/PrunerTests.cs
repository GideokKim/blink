using Blink.Core.Indexing;
using Blink.Core.Store;

namespace Blink.Core.Tests;

public sealed class PrunerTests : IDisposable
{
    private readonly List<string> _tempPaths = new();
    private readonly List<string> _tempDirs = new();

    private SqliteFtsStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-prune-{Guid.NewGuid():N}.db");
        _tempPaths.Add(path);
        return new SqliteFtsStore(path);
    }

    private string NewTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-prune-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public void Plan_FindsDeletedFiles_DoesNotMutate()
    {
        var dir = NewTree();
        var keep = Path.Combine(dir, "keep.txt");
        var gone = Path.Combine(dir, "gone.txt");
        File.WriteAllText(keep, "보관 문서");
        File.WriteAllText(gone, "삭제될 문서");

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);
        Assert.Equal(2, store.Count());

        File.Delete(gone);

        var plan = new Pruner().Plan(dir, store);
        Assert.Equal(2, plan.Scanned);
        Assert.Equal(1, plan.StaleCount);
        Assert.Contains(Path.GetFullPath(gone), plan.StaleDocIds);
        Assert.Equal(2, store.Count()); // Plan is read-only
    }

    [Fact]
    public void Apply_RemovesStaleEntries()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "보관");
        var gone = Path.Combine(dir, "gone.txt");
        File.WriteAllText(gone, "사라짐");

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);
        File.Delete(gone);

        var removed = new Pruner().Apply(dir, store);
        Assert.Equal(1, removed);
        Assert.Equal(1, store.Count());
        Assert.Empty(store.Search("사라짐"));
        Assert.Single(store.Search("보관"));
    }

    [Fact]
    public void Apply_NoStale_IsNoOp()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "a.txt"), "내용");

        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);

        Assert.Equal(0, new Pruner().Apply(dir, store));
        Assert.Equal(1, store.Count());
    }

    [Fact]
    public void Plan_InaccessibleRoot_Throws_AndDoesNotWipeIndex()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "a.txt"), "내용");
        using var store = NewStore();
        new Indexer().Index(dir, store, null, CancellationToken.None);

        var missingRoot = Path.Combine(Path.GetTempPath(), $"blink-nope-{Guid.NewGuid():N}");
        var pruner = new Pruner();

        Assert.Throws<RootUnavailableException>(() => pruner.Plan(missingRoot, store));
        Assert.Throws<RootUnavailableException>(() => pruner.Apply(missingRoot, store));
        Assert.Equal(1, store.Count()); // untouched
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
