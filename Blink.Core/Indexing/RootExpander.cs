namespace Blink.Core.Indexing;

/// <summary>
/// One unit of work produced by <see cref="RootExpander.Expand"/>: a folder to enumerate and
/// whether to recurse into its subfolders. A chunk set is a PARTITION of a configured root's
/// tree — every file is covered by exactly one chunk, so chunks never double-index and never
/// drop files.
/// </summary>
public readonly record struct RootChunk(string EnumRoot, bool Recursive);

/// <summary>
/// Expands a configured index root into independently-indexable <see cref="RootChunk"/>s.
/// Instead of indexing a huge tree (a drive root, a NAS share) as one unit — where the
/// indexer's whole-tree scan must finish before anything is committed, so a cancel/quit loses
/// all progress — the tree is split into chunks that each commit and prune independently. A
/// completed chunk survives interruption and is skipped (incremental) on the next run.
///
/// Splitting is a pure runtime traversal decision: it is never persisted, and because the
/// index is keyed by absolute path it stays fully compatible with an existing database.
///
/// Adaptive descent (see <see cref="Expand"/>):
///   * A folder with no subdirectories, or at the branch-depth cap, becomes one recursive
///     chunk that swallows its whole subtree — so nothing below the stop point is ever lost.
///   * A "passage" (a thin pass-through folder: one subdirectory, almost no loose files) is
///     tunnelled through WITHOUT spending branch-depth budget, so a deep-but-thin chain
///     (e.g. <c>share/a/b/c/d/e</c>, content only at the bottom) is descended to its real
///     content/branch level rather than indexed whole at the top.
///   * Loose files in a descended folder become their own non-recursive chunk.
/// </summary>
public static class RootExpander
{
    /// <summary>Max loose files a folder may hold to still count as a thin "passage".</summary>
    public const int PassageFileLimit = 8;

    /// <summary>Branch-depth cap (passages don't count). Drive roots force one level first.</summary>
    public const int MaxDepth = 3;

    /// <summary>Absolute recursion guard against pathological depth / symlink loops.</summary>
    public const int AbsMaxDepth = 64;

    /// <summary>
    /// The chunks to index for <paramref name="configuredRoot"/>. An inaccessible/missing root
    /// yields an empty list (matching the indexer's "skip unreachable root" behaviour).
    /// </summary>
    public static IReadOnlyList<RootChunk> Expand(string configuredRoot)
    {
        var full = Path.GetFullPath(configuredRoot);
        if (!Directory.Exists(full))
            return Array.Empty<RootChunk>();

        var chunks = new List<RootChunk>();

        if (DriveSplit.IsDriveRoot(full))
        {
            // A drive root always splits one level so a single unreachable share can't stall
            // (or, on prune, wipe) the rest — preserving DriveSplit's mount-isolation guarantee.
            // Cover the drive root's own loose files so the split drops nothing.
            if (FileProbe(full) > 0)
                chunks.Add(new RootChunk(full, Recursive: false));
            foreach (var child in SafeGetDirs(full))
                Descend(child, depth: 1, absDepth: 1, chunks);
        }
        else
        {
            Descend(full, depth: 0, absDepth: 0, chunks);
        }

        return chunks;
    }

    private static void Descend(string dir, int depth, int absDepth, List<RootChunk> chunks)
    {
        if (absDepth >= AbsMaxDepth) { chunks.Add(new RootChunk(dir, Recursive: true)); return; }

        var subdirs = SafeGetDirs(dir);
        if (subdirs.Length == 0) { chunks.Add(new RootChunk(dir, Recursive: true)); return; } // leaf
        if (depth >= MaxDepth)   { chunks.Add(new RootChunk(dir, Recursive: true)); return; } // depth cap

        // Probe direct files, capped at PassageFileLimit+1 (we only need the threshold, not the
        // exact count — avoids walking a folder that holds millions of loose files).
        int probe = FileProbe(dir);
        bool isPassage = subdirs.Length == 1 && probe <= PassageFileLimit;
        int nextDepth = isPassage ? depth : depth + 1; // passages don't spend branch-depth budget

        if (probe > 0)
            chunks.Add(new RootChunk(dir, Recursive: false)); // this folder's loose files

        foreach (var sub in subdirs)
            Descend(sub, nextDepth, absDepth + 1, chunks);
    }

    /// <summary>Number of direct files in <paramref name="dir"/>, counted only up to
    /// <see cref="PassageFileLimit"/>+1. Inaccessible folder → 0.</summary>
    private static int FileProbe(string dir)
    {
        try { return Directory.EnumerateFiles(dir).Take(PassageFileLimit + 1).Count(); }
        catch { return 0; }
    }

    /// <summary>Immediate subdirectories of <paramref name="dir"/>; inaccessible → none (so the
    /// folder is treated as a recursive leaf and the indexer handles the access error).</summary>
    private static string[] SafeGetDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }
}
