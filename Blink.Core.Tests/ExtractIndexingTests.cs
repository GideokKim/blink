using System.Text;
using System.Text.Json;
using Blink.Core.Indexing;
using Blink.Core.Indexing.Worker;
using Blink.Core.Model;
using Blink.Core.Store;
using Microsoft.Data.Sqlite;

namespace Blink.Core.Tests;

[Collection(SqliteCollection.Name)]
public sealed class ExtractIndexingTests : IDisposable
{
    private readonly List<string> _tempPaths = new();
    private readonly List<string> _tempDirs = new();

    private SqliteFtsStore NewStore()
    {
        var p = Path.Combine(Path.GetTempPath(), $"blink-xi-{Guid.NewGuid():N}.db");
        _tempPaths.Add(p);
        return new SqliteFtsStore(p);
    }

    private string NewTree()
    {
        var d = Path.Combine(Path.GetTempPath(), $"blink-xt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        _tempDirs.Add(d);
        return d;
    }

    private static void Index(string dir, IIndexStore store, bool retryFailed = false)
        => new Indexer().Index(dir, dir, recursive: true, store, null, CancellationToken.None, retryFailed);

    private static ExtractFailure? FailureFor(IIndexStore store, string dir, string name)
        => store.IterExtractFailures(dir, 100).SingleOrDefault(f => Path.GetFileName(f.DocId) == name);

    [Fact]
    public void Index_RecordsOutcomePerFile()
    {
        var dir = NewTree();
        File.WriteAllText(Path.Combine(dir, "ok.txt"), "정상 본문");
        File.WriteAllBytes(Path.Combine(dir, "drm.docx"), Encoding.ASCII.GetBytes("DRMONE ciphertext"));
        File.WriteAllBytes(Path.Combine(dir, "pic.png"), new byte[] { 1, 2, 3 });

        using var store = NewStore();
        Index(dir, store);

        var counts = store.ExtractStatusCounts(dir);
        Assert.Equal(1, counts[ExtractStatus.Ok]);
        Assert.Equal(1, counts[ExtractStatus.Encrypted]);
        Assert.Equal(1, counts[ExtractStatus.NotApplicable]);
        Assert.NotNull(FailureFor(store, dir, "drm.docx")!.Detail);
        Assert.Single(store.Search("drm"));   // still findable by name
    }

    [Fact]
    public void Index_UnchangedFailedFile_IsNotRetried_UntilRetryFailed()
    {
        var dir = NewTree();
        var path = Path.Combine(dir, "drm.docx");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("DRMONE ciphertext"));
        using var store = NewStore();
        Index(dir, store);
        Assert.Equal(ExtractStatus.Encrypted, FailureFor(store, dir, "drm.docx")!.Status);

        // Bytes change (now a zip header) but mtime is restored → an ordinary run must skip it.
        var mtime = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 });
        File.SetLastWriteTimeUtc(path, mtime);

        Index(dir, store);
        Assert.Equal(ExtractStatus.Encrypted, FailureFor(store, dir, "drm.docx")!.Status);

        Index(dir, store, retryFailed: true);
        Assert.Equal(ExtractStatus.ParseError, FailureFor(store, dir, "drm.docx")!.Status);
    }

    [Fact]
    public void RetryFailed_DoesNotReextractOkFiles()
    {
        var dir = NewTree();
        var path = Path.Combine(dir, "ok.txt");
        File.WriteAllText(path, "원래 본문");
        using var store = NewStore();
        Index(dir, store);

        var mtime = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "바뀐 본문");
        File.SetLastWriteTimeUtc(path, mtime);

        Index(dir, store, retryFailed: true);
        Assert.Single(store.Search("원래"));
        Assert.Empty(store.Search("바뀐"));
    }

    [Fact]
    public void WireOp_RoundTripsStatus_AndOldLinesDeserializeToNull()
    {
        var doc = new Document("/a.docx", "/a.docx", 1, 2, "", Status: ExtractStatus.Encrypted, Detail: "d");
        var line = JsonSerializer.Serialize(new WireOp(WorkerProtocol.OpUpsert, Doc: doc), WorkerProtocol.Json);
        var back = JsonSerializer.Deserialize<WireOp>(line, WorkerProtocol.Json)!;
        Assert.Equal(doc, back.Doc);

        var old = """{"op":"up","doc":{"DocId":"/b","Path":"/b","Mtime":1,"Size":1,"Content":"x","IsBundle":false,"MemberCount":0}}""";
        Assert.Null(JsonSerializer.Deserialize<WireOp>(old, WorkerProtocol.Json)!.Doc!.Status);
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
