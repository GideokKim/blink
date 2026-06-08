namespace Blink.Core.Indexing;

/// <summary>
/// Expands a configured root into the set of roots actually indexed. A whole-drive root
/// (e.g. <c>L:\</c>) is split into its immediate child folders so each is indexed and
/// pruned independently: one unreachable share doesn't stall the rest, and the pruner's
/// per-root availability guard applies per child instead of nuking the whole drive when a
/// single mount drops. Mirrors the prototype's <c>drive_split</c>.
/// </summary>
public static class DriveSplit
{
    /// <summary>True if <paramref name="path"/> is a volume/drive root (its own path root), e.g. <c>L:\</c> or <c>/</c>.</summary>
    public static bool IsDriveRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return !string.IsNullOrEmpty(root)
            && string.Equals(
                full.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The effective roots for <paramref name="root"/>. A drive root (or any root when
    /// <paramref name="forceSplit"/> is set) becomes its immediate, accessible subfolders;
    /// otherwise the result is just the root itself. Inaccessible roots yield an empty list.
    /// </summary>
    public static IReadOnlyList<string> Expand(string root, bool forceSplit = false)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
            return Array.Empty<string>();

        if (!forceSplit && !IsDriveRoot(full))
            return new[] { full };

        try
        {
            return Directory.GetDirectories(full);
        }
        catch
        {
            // Can't enumerate children → fall back to treating the root as a single unit.
            return new[] { full };
        }
    }
}
