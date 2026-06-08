using Blink.Core.Config;
using Blink.Core.Indexing;
using Blink.Core.Search;
using Blink.Core.Store;

// Minimal headless harness for Blink.Core (no WPF).
//   dotnet run --project Blink.Cli -- index <folder>
//   dotnet run --project Blink.Cli -- search <query>
//   dotnet run --project Blink.Cli -- status
//   dotnet run --project Blink.Cli -- prune [<folder>] [--apply]
// Uses the default config DB path (%APPDATA%/Blink/index.db or platform equivalent).

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: blink <index|search|status|prune> [args]");
    return 1;
}

var cfg = AppConfig.Load();
var cmd = args[0].ToLowerInvariant();

switch (cmd)
{
    case "index":
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: blink index <folder>"); return 1; }
        var folder = Path.GetFullPath(args[1]);
        if (!Directory.Exists(folder)) { Console.Error.WriteLine($"no such folder: {folder}"); return 1; }

        using var store = new SqliteFtsStore(cfg.DbPath);
        var progress = new Progress<IndexProgress>(p =>
            Console.Write($"\rindexing {p.Processed}/{p.Total} ..."));
        var indexer = new Indexer();
        // A drive root (L:\) is split into its child folders, each indexed independently.
        foreach (var root in DriveSplit.Expand(folder))
            indexer.Index(root, store, progress, CancellationToken.None);
        Console.WriteLine($"\rdone. {store.Count()} documents indexed -> {cfg.DbPath}");
        return 0;
    }
    case "search":
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: blink search <query>"); return 1; }
        var query = string.Join(' ', args.Skip(1));

        using var store = new SqliteFtsStore(cfg.DbPath);
        var provider = new InProcessProvider(store);
        var hits = provider.Search(query, limit: 20);
        if (hits.Count == 0) { Console.WriteLine("(no results)"); return 0; }

        var bundleSizes = provider.GetBundleSizes(hits.Select(h => h.DocId));
        foreach (var hit in hits)
        {
            if (bundleSizes.TryGetValue(hit.DocId, out var count))
            {
                Console.WriteLine($"[{hit.Score:F3}] {hit.Path}  (bundle: {count:N0} files)");
                continue;
            }
            Console.WriteLine($"[{hit.Score:F3}] {hit.Path}");
            foreach (var line in provider.GetMatchLines(hit.DocId, query, maxLines: 3))
                Console.WriteLine($"    {line.LineNo}: {line.Text}");
        }
        return 0;
    }
    case "status":
    {
        using var store = new SqliteFtsStore(cfg.DbPath);
        var dbExists = File.Exists(cfg.DbPath);
        Console.WriteLine("Blink index status");
        Console.WriteLine($"  db path   : {cfg.DbPath}{(dbExists ? "" : " (not created yet)")}");
        Console.WriteLine($"  documents : {store.Count()}");
        Console.WriteLine($"  tokenizer : n-gram (Hangul 2/3-gram) + unicode61");
        Console.WriteLine($"  folders   : {(cfg.Folders.Length == 0 ? "(none configured)" : "")}");
        foreach (var f in cfg.Folders)
            Console.WriteLine($"    - {f}{(Directory.Exists(f) ? "" : " [unavailable]")}");
        return 0;
    }
    case "prune":
    {
        // blink prune                 -> dry-run over all configured folders
        // blink prune <folder>        -> dry-run over one folder
        // blink prune [<folder>] --apply -> commit deletions
        bool apply = args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        var positional = args.Skip(1).Where(a => !a.StartsWith("--")).ToArray();

        var roots = positional.Length > 0
            ? positional.Select(Path.GetFullPath).ToArray()
            : cfg.Folders;
        if (roots.Length == 0) { Console.Error.WriteLine("no folders to prune (none given and none configured)"); return 1; }

        using var store = new SqliteFtsStore(cfg.DbPath);
        var pruner = new Pruner();
        int totalStale = 0, skipped = 0;

        foreach (var root in roots)
        {
            try
            {
                if (apply)
                {
                    int removed = pruner.Apply(root, store);
                    totalStale += removed;
                    Console.WriteLine($"{root}: removed {removed} stale entr{(removed == 1 ? "y" : "ies")}");
                }
                else
                {
                    var plan = pruner.Plan(root, store);
                    totalStale += plan.StaleCount;
                    Console.WriteLine($"{root}: {plan.StaleCount} stale of {plan.Scanned} indexed");
                    foreach (var id in plan.StaleDocIds)
                        Console.WriteLine($"    - {id}");
                }
            }
            catch (RootUnavailableException)
            {
                skipped++;
                Console.WriteLine($"{root}: SKIPPED (root not accessible)");
            }
        }

        if (!apply && totalStale > 0)
            Console.WriteLine($"\n{totalStale} stale entr{(totalStale == 1 ? "y" : "ies")} found. Re-run with --apply to remove.");
        if (skipped > 0)
            Console.WriteLine($"{skipped} root(s) skipped because they were not accessible.");
        return 0;
    }
    default:
        Console.Error.WriteLine($"unknown command: {cmd}");
        return 1;
}
