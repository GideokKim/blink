using Blink.Core.Config;
using Blink.Core.Indexing;
using Blink.Core.Search;
using Blink.Core.Store;

// Minimal headless smoke harness for Blink.Core (no WPF).
//   dotnet run --project Blink.Cli -- index <folder>
//   dotnet run --project Blink.Cli -- search <query>
// Uses the default config DB path (%APPDATA%/Blink/index.db or platform equivalent).

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: blink <index|search> <arg>");
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
        new Indexer().Index(folder, store, progress, CancellationToken.None);
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

        foreach (var hit in hits)
        {
            Console.WriteLine($"[{hit.Score:F3}] {hit.Path}");
            foreach (var line in provider.GetMatchLines(hit.DocId, query, maxLines: 3))
                Console.WriteLine($"    {line.LineNo}: {line.Text}");
        }
        return 0;
    }
    default:
        Console.Error.WriteLine($"unknown command: {cmd}");
        return 1;
}
