using Blink.Core.Indexing;
using Blink.Core.Indexing.Worker;

// Blink indexer worker. Does ALL file I/O (enumerate/stat/parse/existence checks) and
// streams store operations as JSON lines to stdout, so the main process never reads the
// indexed tree. In EDR/AV-locked environments this is the process that gets signed/allow-
// listed for SMB access.
//
//   Blink.Indexer.Worker --root <folder> --known <known.jsonl> [--bundle-threshold N]
//
// Exit codes: 0 = ok, 1 = bad usage, 2 = root inaccessible (caller should skip), 3 = error.

var opts = ParseArgs(args);
if (opts is null)
{
    Console.Error.WriteLine("usage: Blink.Indexer.Worker --root <folder> --known <file> [--bundle-threshold N]");
    return 1;
}

var (root, knownFile, bundleThreshold) = opts.Value;

try
{
    var known = ReadKnown(knownFile);

    // Stream JSON lines to stdout; the client replays them into the real database.
    var stdout = Console.Out;
    WorkerIndexer.Run(root, known, stdout, bundleThreshold);
    stdout.Flush();
    return 0;
}
catch (RootUnavailableException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"worker error: {ex.Message}");
    return 3;
}

static (string Root, string KnownFile, int BundleThreshold)? ParseArgs(string[] args)
{
    string? root = null, known = null;
    int threshold = 100;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--root" when i + 1 < args.Length: root = args[++i]; break;
            case "--known" when i + 1 < args.Length: known = args[++i]; break;
            case "--bundle-threshold" when i + 1 < args.Length:
                if (int.TryParse(args[++i], out var t)) threshold = t;
                break;
        }
    }

    if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(known))
        return null;
    return (root, known, threshold);
}

static Dictionary<string, long> ReadKnown(string knownFile)
{
    if (!File.Exists(knownFile))
        return new Dictionary<string, long>(StringComparer.Ordinal);
    using var r = new StreamReader(knownFile);
    return WorkerIndexClient.ReadKnown(r);
}
