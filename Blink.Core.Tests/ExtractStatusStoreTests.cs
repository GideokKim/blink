using Blink.Core.Model;
using Blink.Core.Store;
using Microsoft.Data.Sqlite;

namespace Blink.Core.Tests;

[Collection(SqliteCollection.Name)]
public sealed class ExtractStatusStoreTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    private string TempDbPath()
    {
        var p = Path.Combine(Path.GetTempPath(), $"blink-xs-{Guid.NewGuid():N}.db");
        _tempPaths.Add(p);
        return p;
    }

    private SqliteFtsStore NewStore() => new(TempDbPath());

    private static string FreshRoot(string tag) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"blink-{tag}-{Guid.NewGuid():N}"));

    private static Document Doc(string id, ExtractStatus? status, string? detail = null, string content = "") =>
        new(id, id, 1, 1, content, Status: status, Detail: detail);

    [Fact]
    public void Upsert_StoresStatus_AndCountsGroupByStatus_IgnoringNull()
    {
        var root = FreshRoot("cnt");
        using var store = NewStore();
        store.UpsertMany(new[]
        {
            Doc(Path.Combine(root, "a.docx"), ExtractStatus.Encrypted, "x"),
            Doc(Path.Combine(root, "b.docx"), ExtractStatus.Encrypted, "y"),
            Doc(Path.Combine(root, "c.txt"), ExtractStatus.Ok, content: "본문"),
            Doc(Path.Combine(root, "d.pdf"), null),
        });

        var counts = store.ExtractStatusCounts(root);
        Assert.Equal(2, counts[ExtractStatus.Encrypted]);
        Assert.Equal(1, counts[ExtractStatus.Ok]);
        Assert.Equal(2, counts.Count);
    }

    [Fact]
    public void Counts_PrefixSiblingNotIncluded()
    {
        var root = FreshRoot("pfx");
        using var store = NewStore();
        store.Upsert(Doc(Path.Combine(root, "a.docx"), ExtractStatus.ParseError));
        store.Upsert(Doc(Path.Combine(root + "2", "b.docx"), ExtractStatus.ParseError));

        Assert.Equal(1, store.ExtractStatusCounts(root)[ExtractStatus.ParseError]);
        Assert.Single(store.IterExtractFailures(root, 100));
    }

    [Fact]
    public void IterExtractFailures_ReportableOnly_OrderedByStatusThenId_Limited()
    {
        var root = FreshRoot("it");
        using var store = NewStore();
        store.UpsertMany(new[]
        {
            Doc(Path.Combine(root, "z.docx"), ExtractStatus.ReadDenied, "d1"),
            Doc(Path.Combine(root, "a.docx"), ExtractStatus.ParseError, "d2"),
            Doc(Path.Combine(root, "b.docx"), ExtractStatus.ReadDenied, "d3"),
            Doc(Path.Combine(root, "ok.txt"), ExtractStatus.Ok, content: "x"),
            Doc(Path.Combine(root, "img.png"), ExtractStatus.NotApplicable),
            Doc(Path.Combine(root, "scan.pdf"), ExtractStatus.EmptyText),
        });

        var all = store.IterExtractFailures(root, 100);
        Assert.Equal(new[] { "b.docx", "z.docx", "a.docx", "scan.pdf" },
            all.Select(f => Path.GetFileName(f.DocId)).ToArray());
        Assert.Equal("d3", all[0].Detail);
        Assert.Equal(2, store.IterExtractFailures(root, 2).Count);
    }

    [Fact]
    public void IterExtractFailures_FailuresComeBeforeInformational_SoTheLimitCannotHideThem()
    {
        var root = FreshRoot("ord");
        using var store = NewStore();
        store.UpsertMany(Enumerable.Range(0, 5)
            .Select(i => Doc(Path.Combine(root, $"big{i}.pdf"), ExtractStatus.TooLarge))
            .Append(Doc(Path.Combine(root, "zz.docx"), ExtractStatus.Encrypted)));

        var top = store.IterExtractFailures(root, 2);
        Assert.Equal(ExtractStatus.Encrypted, top[0].Status);
    }

    [Fact]
    public void Delete_RemovesStatusWithRow()
    {
        var root = FreshRoot("del");
        var id = Path.Combine(root, "a.docx");
        using var store = NewStore();
        store.Upsert(Doc(id, ExtractStatus.Encrypted));
        store.Delete(id);
        Assert.Empty(store.ExtractStatusCounts(root));
    }

    [Fact]
    public void Reupsert_OverwritesStatus()
    {
        var root = FreshRoot("re");
        var id = Path.Combine(root, "a.docx");
        using var store = NewStore();
        store.Upsert(Doc(id, ExtractStatus.Encrypted, "old"));
        store.Upsert(Doc(id, ExtractStatus.Ok, content: "본문"));
        Assert.Empty(store.IterExtractFailures(root, 10));
    }

    [Fact]
    public void ContentExtensions_IncludeParserFormats_NotFilenameOnly()
    {
        var exts = Blink.Core.Parsers.ParserRegistry.ContentExtensions;
        Assert.Contains(".pdf", exts);
        Assert.Contains(".docx", exts);
        Assert.Contains(".hwpx", exts);
        Assert.DoesNotContain(".png", exts);
    }

    [Fact]
    public void OpeningV2Database_ResetsOnlySilentlyEmptiedContentRows()
    {
        var root = FreshRoot("mig");
        string Id(string n) => Path.Combine(root, n);
        var dbPath = TempDbPath();

        SQLitePCL.Batteries_V2.Init();
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_meta(version INTEGER NOT NULL);
                CREATE TABLE documents(
                    rowid INTEGER PRIMARY KEY, doc_id TEXT UNIQUE NOT NULL, path TEXT NOT NULL,
                    mtime REAL NOT NULL, size INTEGER NOT NULL, content TEXT NOT NULL,
                    is_bundle INTEGER NOT NULL DEFAULT 0, member_count INTEGER NOT NULL DEFAULT 0);
                CREATE VIRTUAL TABLE documents_fts USING fts5(tokens, tokenize='unicode61');
                INSERT INTO schema_meta(version) VALUES(2);
                INSERT INTO documents(doc_id, path, mtime, size, content) VALUES($a, $a, 5, 1, '');
                INSERT INTO documents(doc_id, path, mtime, size, content) VALUES($b, $b, 5, 1, 'body');
                INSERT INTO documents(doc_id, path, mtime, size, content) VALUES($c, $c, 5, 1, '');
                INSERT INTO documents(doc_id, path, mtime, size, content) VALUES($d, $d, 5, 1, '');
                INSERT INTO documents(doc_id, path, mtime, size, content, is_bundle, member_count)
                    VALUES($e, $e, 0, 0, 'jpg', 1, 200);";
            cmd.Parameters.AddWithValue("$a", Id("cloud.pdf"));
            cmd.Parameters.AddWithValue("$b", Id("fine.pdf"));
            cmd.Parameters.AddWithValue("$c", Id("photo.png"));
            cmd.Parameters.AddWithValue("$d", Id("UPPER.DOCX"));
            cmd.Parameters.AddWithValue("$e", Id("__bundle__.jpg"));
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using var store = new SqliteFtsStore(dbPath);
        var mtimes = store.IterDocsUnder(root).ToDictionary(x => Path.GetFileName(x.DocId), x => x.Mtime);
        Assert.Equal(0, mtimes["cloud.pdf"]);   // silently emptied → re-extract once
        Assert.Equal(0, mtimes["UPPER.DOCX"]);  // case-insensitive extension match
        Assert.Equal(5, mtimes["fine.pdf"]);    // has content → untouched
        Assert.Equal(5, mtimes["photo.png"]);   // filename-only format → untouched
        Assert.Empty(store.ExtractStatusCounts(root)); // legacy rows: status NULL

        store.Upsert(Doc(Id("cloud.pdf"), ExtractStatus.Encrypted, "x"));
        Assert.Equal(1, store.ExtractStatusCounts(root)[ExtractStatus.Encrypted]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in _tempPaths)
            foreach (var f in new[] { p, p + "-wal", p + "-shm" })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
    }
}
