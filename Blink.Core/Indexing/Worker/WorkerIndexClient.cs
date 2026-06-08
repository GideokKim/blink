using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Blink.Core.Model;
using Blink.Core.Store;

namespace Blink.Core.Indexing.Worker;

/// <summary>
/// The main-process side of worker-based indexing. Owns ALL database access: it snapshots
/// the current (doc_id → mtime) state for the worker, then replays the worker's JSON-line
/// op stream into the real store. It performs no file I/O on the indexed tree, so an
/// EDR/AV policy that blocks the main executable's SMB reads does not stop indexing.
/// </summary>
public sealed class WorkerIndexClient
{
    private const int BatchSize = 500;

    /// <summary>Replay a worker op stream (<see cref="WireOp"/> JSON lines) into <paramref name="store"/>.</summary>
    /// <returns>The number of documents upserted.</returns>
    public static int ApplyStream(TextReader input, IIndexStore store)
    {
        var batch = new List<Document>(BatchSize);
        int upserted = 0;

        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;

            var op = JsonSerializer.Deserialize<WireOp>(line, WorkerProtocol.Json);
            if (op is null)
                continue;

            switch (op.Op)
            {
                case WorkerProtocol.OpUpsert when op.Doc is not null:
                    batch.Add(op.Doc);
                    upserted++;
                    if (batch.Count >= BatchSize) { store.UpsertMany(batch); batch.Clear(); }
                    break;

                case WorkerProtocol.OpDelete when op.Ids is { Length: > 0 }:
                    if (batch.Count > 0) { store.UpsertMany(batch); batch.Clear(); } // preserve order
                    store.DeleteMany(op.Ids);
                    break;
            }
        }

        if (batch.Count > 0)
            store.UpsertMany(batch);
        return upserted;
    }

    /// <summary>Snapshot the store's (doc_id → mtime) under <paramref name="root"/> for the worker.</summary>
    public static Dictionary<string, long> SnapshotKnown(IIndexStore store, string root)
    {
        var known = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (docId, mtime) in store.IterDocsUnder(root))
            known[docId] = (long)mtime;
        return known;
    }

    public static void WriteKnown(TextWriter w, IReadOnlyDictionary<string, long> known)
    {
        foreach (var (id, m) in known)
            w.WriteLine(JsonSerializer.Serialize(new KnownEntry(id, m), WorkerProtocol.Json));
    }

    public static Dictionary<string, long> ReadKnown(TextReader r)
    {
        var known = new Dictionary<string, long>(StringComparer.Ordinal);
        string? line;
        while ((line = r.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var e = JsonSerializer.Deserialize<KnownEntry>(line, WorkerProtocol.Json);
            if (e is not null)
                known[e.DocId] = e.Mtime;
        }
        return known;
    }

    /// <summary>
    /// Spawn the worker process to index <paramref name="root"/> and apply its output to
    /// <paramref name="store"/>. <paramref name="workerExe"/> is the worker invocation: either
    /// the path to a native <c>Blink.Indexer.Worker(.exe)</c>, or (when it ends in <c>.dll</c>)
    /// it is launched via <c>dotnet</c>. Returns the worker exit code (0 = success).
    /// </summary>
    public int IndexViaWorker(string root, IIndexStore store, string workerExe, int bundleThreshold = 100)
    {
        var known = SnapshotKnown(store, root);
        var knownFile = Path.Combine(Path.GetTempPath(), $"blink-known-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var kw = new StreamWriter(knownFile, append: false, new UTF8Encoding(false)))
                WriteKnown(kw, known);

            var psi = BuildStartInfo(workerExe, root, knownFile, bundleThreshold);
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("failed to start indexer worker");

            ApplyStream(proc.StandardOutput, store);
            proc.WaitForExit();
            return proc.ExitCode;
        }
        finally
        {
            try { if (File.Exists(knownFile)) File.Delete(knownFile); } catch { /* best effort */ }
        }
    }

    private static ProcessStartInfo BuildStartInfo(string workerExe, string root, string knownFile, int bundleThreshold)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        // A .dll is run through the shared `dotnet` host; a native exe is launched directly.
        if (workerExe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(workerExe);
        }
        else
        {
            psi.FileName = workerExe;
        }

        psi.ArgumentList.Add("--root");
        psi.ArgumentList.Add(root);
        psi.ArgumentList.Add("--known");
        psi.ArgumentList.Add(knownFile);
        psi.ArgumentList.Add("--bundle-threshold");
        psi.ArgumentList.Add(bundleThreshold.ToString());
        return psi;
    }
}
