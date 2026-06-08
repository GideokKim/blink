using Blink.Core.Indexing;
using Blink.Core.Model;
using Blink.Core.Store;
using Microsoft.Data.Sqlite;

namespace Blink.Core.Tests;

public sealed class BundleTests : IDisposable
{
    private readonly List<string> _tempPaths = new();
    private readonly List<string> _tempDirs = new();

    private SqliteFtsStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-bundle-{Guid.NewGuid():N}.db");
        _tempPaths.Add(path);
        return new SqliteFtsStore(path);
    }

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-bundle-{Guid.NewGuid():N}.db");
        _tempPaths.Add(path);
        return path;
    }

    private string NewTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-bundle-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static void MakeImages(string dir, int n, string ext = ".jpg")
    {
        for (int i = 0; i < n; i++)
            File.WriteAllText(Path.Combine(dir, $"IMG_{i:D5}{ext}"), "x");
    }

    [Fact]
    public void AboveThreshold_CollapsesToSingleBundle()
    {
        var dir = NewTree();
        MakeImages(dir, 5);

        using var store = NewStore();
        new Indexer(bundleThreshold: 3).Index(dir, store, null, CancellationToken.None);

        // 5 images → 1 bundle entry, not 5 rows.
        Assert.Equal(1, store.Count());
        var bundleId = Path.Combine(Path.GetFullPath(dir), "__bundle__.jpg");
        Assert.Equal(5, store.GetBundleSizes(new[] { bundleId })[bundleId]);
    }

    [Fact]
    public void BelowThreshold_IndexesIndividually()
    {
        var dir = NewTree();
        MakeImages(dir, 2);

        using var store = NewStore();
        new Indexer(bundleThreshold: 3).Index(dir, store, null, CancellationToken.None);

        Assert.Equal(2, store.Count()); // individual, no bundle
        Assert.Empty(store.GetBundleSizes(new[] { Path.Combine(Path.GetFullPath(dir), "__bundle__.jpg") }));
    }

    [Fact]
    public void ContentFiles_AreNeverBundled()
    {
        var dir = NewTree();
        for (int i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(dir, $"note{i}.txt"), $"문서 본문 {i}");

        using var store = NewStore();
        new Indexer(bundleThreshold: 3).Index(dir, store, null, CancellationToken.None);

        Assert.Equal(10, store.Count()); // .txt has a content parser → individual
        Assert.Single(store.Search("본문 5"));
    }

    [Fact]
    public void Mixed_BundlesImagesKeepsDocsIndividual()
    {
        var dir = NewTree();
        MakeImages(dir, 6);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "혼합 폴더 설명");

        using var store = NewStore();
        new Indexer(bundleThreshold: 3).Index(dir, store, null, CancellationToken.None);

        // 6 images → 1 bundle; readme.txt → 1 individual. Total 2.
        Assert.Equal(2, store.Count());
        Assert.Single(store.Search("혼합"));         // the txt is still searchable
        Assert.Single(store.Search("jpg"));          // bundle content = extension
    }

    [Fact]
    public void Reindex_IsIdempotent()
    {
        var dir = NewTree();
        MakeImages(dir, 5);

        using var store = NewStore();
        var idx = new Indexer(bundleThreshold: 3);
        idx.Index(dir, store, null, CancellationToken.None);
        idx.Index(dir, store, null, CancellationToken.None);

        Assert.Equal(1, store.Count());
        var bundleId = Path.Combine(Path.GetFullPath(dir), "__bundle__.jpg");
        Assert.Equal(5, store.GetBundleSizes(new[] { bundleId })[bundleId]);
    }

    [Fact]
    public void GroupShrinksBelowThreshold_BundleReplacedByIndividuals()
    {
        var dir = NewTree();
        MakeImages(dir, 5);

        using var store = NewStore();
        var idx = new Indexer(bundleThreshold: 4);
        idx.Index(dir, store, null, CancellationToken.None);
        Assert.Equal(1, store.Count()); // bundled

        // Delete down to 2 → below threshold on re-index.
        File.Delete(Path.Combine(dir, "IMG_00000.jpg"));
        File.Delete(Path.Combine(dir, "IMG_00001.jpg"));
        File.Delete(Path.Combine(dir, "IMG_00002.jpg"));
        idx.Index(dir, store, null, CancellationToken.None);

        Assert.Equal(2, store.Count()); // now individual
        Assert.Empty(store.GetBundleSizes(new[] { Path.Combine(Path.GetFullPath(dir), "__bundle__.jpg") }));
    }

    [Fact]
    public void Pruner_KeepsBundleWhileFolderExists_RemovesWhenFolderGone()
    {
        var parent = NewTree();
        var sub = Path.Combine(parent, "photos");
        Directory.CreateDirectory(sub);
        MakeImages(sub, 5);

        using var store = NewStore();
        new Indexer(bundleThreshold: 3).Index(parent, store, null, CancellationToken.None);
        Assert.Equal(1, store.Count());

        // Folder still present → pruner must NOT remove the bundle.
        Assert.Equal(0, new Pruner().Apply(parent, store));
        Assert.Equal(1, store.Count());

        // Remove the whole folder → bundle becomes stale.
        Directory.Delete(sub, true);
        Assert.Equal(1, new Pruner().Apply(parent, store));
        Assert.Equal(0, store.Count());
    }

    // ---- DB schema migration v1 → v2 ----
    [Fact]
    public void OpeningV1Database_MigratesToV2_PreservingRows()
    {
        var dbPath = TempDbPath();

        // Hand-build a v1 database (no is_bundle/member_count columns, schema_meta=1).
        SQLitePCL.Batteries_V2.Init();
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            Exec(conn, @"
                CREATE TABLE schema_meta(version INTEGER NOT NULL);
                CREATE TABLE documents(
                    rowid INTEGER PRIMARY KEY, doc_id TEXT UNIQUE NOT NULL, path TEXT NOT NULL,
                    mtime REAL NOT NULL, size INTEGER NOT NULL, content TEXT NOT NULL);
                CREATE VIRTUAL TABLE documents_fts USING fts5(tokens, tokenize='unicode61');
                INSERT INTO schema_meta(version) VALUES(1);
                INSERT INTO documents(doc_id, path, mtime, size, content)
                    VALUES('/old/a.txt', '/old/a.txt', 0, 1, 'legacy');");
        }
        SqliteConnection.ClearAllPools();

        // Open with the current store → should migrate without losing the legacy row.
        using var store = new SqliteFtsStore(dbPath);
        Assert.Equal(1, store.Count());

        // New bundle columns now work end-to-end.
        store.Upsert(new Document("/new/__bundle__.jpg", "/new", 0, 0, "jpg", IsBundle: true, MemberCount: 42));
        Assert.Equal(42, store.GetBundleSizes(new[] { "/new/__bundle__.jpg" })["/new/__bundle__.jpg"]);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
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
