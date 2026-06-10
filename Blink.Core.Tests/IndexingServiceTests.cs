using Blink.Core.Indexing;
using Blink.Core.Model;
using Blink.Core.Store;

namespace Blink.Core.Tests;

public sealed class IndexingServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTree(string fileName = "a.txt")
    {
        var dir = Path.Combine(Path.GetTempPath(), "blink-svc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), "내용 " + fileName);
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public async Task ReindexAsync_IndexesFolders_FiresFolderCompletedAndCompleted()
    {
        var a = NewTree();
        var b = NewTree();
        using var store = new HookStore();
        using var svc = new IndexingService();
        var completedFolders = new List<string>();
        int completed = 0;
        svc.FolderCompleted += f => { lock (completedFolders) completedFolders.Add(f); };
        svc.Completed += () => Interlocked.Increment(ref completed);

        await svc.ReindexAsync(new[] { a, b }, store);

        Assert.Equal(new[] { a, b }, completedFolders);
        Assert.Equal(1, completed);
        Assert.True(store.Upserted.Count >= 2);
    }

    [Fact]
    public async Task ReindexAsync_OneFolderThrows_FiresFolderFailed_OthersStillIndexed_RunCompletes()
    {
        var bad = NewTree();
        var good = NewTree();
        using var store = new HookStore
        {
            // Any upsert containing a doc under `bad` blows up — simulates a network share
            // hiccup / access-denied mid-folder.
            OnUpsertMany = docs =>
            {
                if (docs.Any(d => d.DocId.StartsWith(bad, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("network share dropped");
            },
        };
        using var svc = new IndexingService();
        var failed = new List<(string Folder, Exception Ex)>();
        var completedFolders = new List<string>();
        int completed = 0;
        svc.FolderFailed += (f, ex) => { lock (failed) failed.Add((f, ex)); };
        svc.FolderCompleted += f => { lock (completedFolders) completedFolders.Add(f); };
        svc.Completed += () => Interlocked.Increment(ref completed);

        await svc.ReindexAsync(new[] { bad, good }, store);

        Assert.Single(failed);
        Assert.Equal(bad, failed[0].Folder);
        Assert.IsType<IOException>(failed[0].Ex);
        Assert.Equal(new[] { good }, completedFolders); // failed folder gets no completion stamp
        Assert.Equal(1, completed);                     // the run still finishes
    }

    [Fact]
    public async Task IsBusy_TrueWhileRunning_FalseAfterCompletion()
    {
        var dir = NewTree();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var store = new HookStore
        {
            OnUpsertMany = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); },
        };
        using var svc = new IndexingService();

        Assert.False(svc.IsBusy);
        var run = svc.ReindexAsync(new[] { dir }, store);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "indexing should reach the store");
        Assert.True(svc.IsBusy);

        release.Set();
        await run;
        Assert.False(svc.IsBusy);
    }

    [Fact]
    public async Task ReindexAsync_CancelledMidRun_DoesNotFireCompleted_AndClearsBusy()
    {
        var dir = NewTree();
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var store = new HookStore
        {
            OnUpsertMany = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); },
        };
        using var svc = new IndexingService();
        int completed = 0;
        svc.Completed += () => Interlocked.Increment(ref completed);

        var run = svc.ReindexAsync(new[] { dir }, store);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "indexing should reach the store");
        svc.CancelOngoing();
        release.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, completed);
        Assert.False(svc.IsBusy);
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }

    /// <summary>Minimal in-memory store with an injectable UpsertMany hook.</summary>
    private sealed class HookStore : IIndexStore
    {
        public Action<IReadOnlyList<Document>>? OnUpsertMany;
        public List<Document> Upserted { get; } = new();

        public void Upsert(Document doc) => UpsertMany(new[] { doc });
        public void UpsertMany(IEnumerable<Document> docs)
        {
            var list = docs.ToList();
            OnUpsertMany?.Invoke(list);
            lock (Upserted) Upserted.AddRange(list);
        }
        public void Delete(string docId) { }
        public void DeleteMany(IEnumerable<string> docIds) { }
        public IEnumerable<(string DocId, double Mtime)> IterDocsUnder(string root) => Enumerable.Empty<(string, double)>();
        public IReadOnlyList<SearchHit> Search(string query, int limit = 50, int offset = 0) => Array.Empty<SearchHit>();
        public int Count() { lock (Upserted) return Upserted.Count; }
        public (long FileCount, long TotalBytes) FolderStats(string root) => (0, 0);
        public void Dispose() { }
    }
}
