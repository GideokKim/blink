using Blink.Core.Indexing;
using Blink.Core.Store;

namespace Blink.Core.Tests;

/// <summary>
/// Covers the chunk-aware <see cref="Indexer.Index(string,string,bool,IIndexStore,IProgress{IndexProgress},CancellationToken)"/>
/// overload and the DB-compatibility invariant: chunked indexing produces the same stored
/// document set as single-root indexing, so an existing database upgrades cleanly.
/// </summary>
public sealed class ChunkIndexingTests : IDisposable
{
    private readonly List<string> _tempDirs = new();
    private readonly List<string> _tempDbs = new();

    private string NewRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-chunk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private SqliteFtsStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-chunk-{Guid.NewGuid():N}.db");
        _tempDbs.Add(path);
        return new SqliteFtsStore(path);
    }

    private static string Dir(string parent, string name)
    {
        var d = Path.Combine(parent, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static HashSet<string> DocIds(SqliteFtsStore store, string root) =>
        store.IterDocsUnder(root).Select(d => d.DocId).ToHashSet(StringComparer.Ordinal);

    /// <summary>Index a tree the way IndexingService/CLI now do: chunk it, anchor excludes at the root.</summary>
    private static void IndexChunked(Indexer indexer, string root, IIndexStore store)
    {
        foreach (var chunk in RootExpander.Expand(root))
            indexer.Index(chunk.EnumRoot, root, chunk.Recursive, store, null, CancellationToken.None);
    }

    [Fact]
    public void NonRecursiveChunk_IndexesDirectFilesOnly()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "top.txt"), "top");
        var sub = Dir(root, "sub");
        File.WriteAllText(Path.Combine(sub, "inner.txt"), "inner");

        using var store = NewStore();
        new Indexer().Index(root, root, recursive: false, store, null, CancellationToken.None);

        Assert.Equal(1, store.Count()); // only top.txt; sub/inner.txt belongs to a sibling chunk
        Assert.Contains(DocIds(store, root), id => id.EndsWith("top.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ExclusionRules_AreAnchoredAtExcludeRoot_NotEnumRoot()
    {
        // .blinkignore lives at the configured root; a deeper chunk must still honour it.
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, ".blinkignore"), "*.skip\n");
        var data = Dir(root, "data");
        File.WriteAllText(Path.Combine(data, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(data, "drop.skip"), "drop");

        using var store = NewStore();
        // enumRoot is the deeper chunk; excludeRoot is the configured root that owns .blinkignore.
        new Indexer().Index(data, root, recursive: true, store, null, CancellationToken.None);

        var ids = DocIds(store, root);
        Assert.Contains(ids, id => id.EndsWith("keep.txt", StringComparison.Ordinal));
        Assert.DoesNotContain(ids, id => id.EndsWith("drop.skip", StringComparison.Ordinal));
    }

    [Fact]
    public void ChunkedIndexing_ProducesSameDocSet_AsSingleRoot()
    {
        // A varied tree: loose files, a thin passage, a wide branch, content at several depths.
        var single = NewRoot();
        File.WriteAllText(Path.Combine(single, "root1.txt"), "r1");
        File.WriteAllText(Path.Combine(single, "root2.md"), "r2");
        var passage = Dir(Dir(single, "p"), "q");          // thin passage p/q
        File.WriteAllText(Path.Combine(passage, "deep.txt"), "deep");
        var wide = Dir(single, "wide");
        foreach (var name in new[] { "w1", "w2", "w3" })
            File.WriteAllText(Path.Combine(Dir(wide, name), "f.txt"), name);
        var chunked = MirrorTree(single);                  // identical structure under a different path

        using var s1 = NewStore();
        using var s2 = NewStore();
        new Indexer().Index(single, s1, null, CancellationToken.None); // old single-root path
        IndexChunked(new Indexer(), chunked, s2);                      // new chunked path

        // Compare by tree-relative paths so the two distinct roots line up.
        Assert.Equal(RelIds(s1, single), RelIds(s2, chunked));
        Assert.Equal(s1.Count(), s2.Count());
    }

    [Fact]
    public void ExistingDb_ReindexedWithChunks_IsStable_NoDuplicatesNoGrowth()
    {
        // Simulate upgrade: a DB built by the old single-root indexer, re-indexed by the new
        // chunked path into the SAME store. Incremental skip + partition ⇒ no churn.
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");
        var sub = Dir(root, "sub");
        File.WriteAllText(Path.Combine(sub, "b.txt"), "b");
        var deeper = Dir(Dir(sub, "x"), "y");
        File.WriteAllText(Path.Combine(deeper, "c.txt"), "c");

        using var store = NewStore();
        new Indexer().Index(root, store, null, CancellationToken.None); // "old version" populated it
        var before = DocIds(store, root);
        var beforeCount = store.Count();

        IndexChunked(new Indexer(), root, store);                       // "new version" re-indexes

        Assert.Equal(beforeCount, store.Count());     // no growth
        Assert.Equal(before, DocIds(store, root));    // identical doc set, no duplicates
    }

    [Fact]
    public void ChunkedIndexing_BundlesIdentically_ToSingleRoot()
    {
        // A folder of filename-only files past the bundle threshold must collapse to the same
        // marker whether indexed whole or chunked (a folder's files always land in one chunk).
        var single = NewRoot();
        var imgs = Dir(single, "imgs");
        for (int i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(imgs, $"p{i}.png"), "img");
        var chunked = MirrorTree(single);

        using var s1 = NewStore();
        using var s2 = NewStore();
        new Indexer(bundleThreshold: 3).Index(single, s1, null, CancellationToken.None);
        foreach (var chunk in RootExpander.Expand(chunked))
            new Indexer(bundleThreshold: 3).Index(chunk.EnumRoot, chunked, chunk.Recursive, s2, null, CancellationToken.None);

        Assert.Equal(RelIds(s1, single), RelIds(s2, chunked)); // same bundle marker, same set
        Assert.Contains(RelIds(s1, single), id => id.Contains(Indexer.BundleMarker, StringComparison.Ordinal));
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private string MirrorTree(string source)
    {
        var dest = NewRoot();
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.Ordinal));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, dest, StringComparison.Ordinal));
        return dest;
    }

    private static HashSet<string> RelIds(SqliteFtsStore store, string root)
    {
        var full = Path.GetFullPath(root);
        return store.IterDocsUnder(root)
            .Select(d => Path.GetRelativePath(full, d.DocId).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        foreach (var db in _tempDbs)
            try { if (File.Exists(db)) File.Delete(db); } catch { }
    }
}
