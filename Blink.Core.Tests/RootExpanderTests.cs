using Blink.Core.Indexing;

namespace Blink.Core.Tests;

public sealed class RootExpanderTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-exp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static string Dir(string parent, string name)
    {
        var d = Path.Combine(parent, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void File_(string dir, string name) =>
        File.WriteAllText(Path.Combine(dir, name), "x");

    /// <summary>All files actually reachable through the chunk set, honouring each Recursive flag.</summary>
    private static List<string> Covered(IReadOnlyList<RootChunk> chunks) =>
        chunks.SelectMany(c => Directory.EnumerateFiles(
                c.EnumRoot, "*", c.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            .Select(Path.GetFullPath)
            .ToList();

    private static List<string> AllFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(Path.GetFullPath).ToList();

    /// <summary>The defining invariant: chunks PARTITION the tree — cover every file, exactly once.</summary>
    private static void AssertPartition(string root, IReadOnlyList<RootChunk> chunks)
    {
        var covered = Covered(chunks);
        Assert.Equal(covered.Count, covered.Distinct().Count());                 // no double-index
        Assert.Equal(AllFiles(root).OrderBy(p => p), covered.OrderBy(p => p));   // no file lost
    }

    [Fact]
    public void MissingRoot_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"blink-nope-{Guid.NewGuid():N}");
        Assert.Empty(RootExpander.Expand(missing));
    }

    [Fact]
    public void LeafFolder_BecomesSingleRecursiveChunk()
    {
        var root = NewRoot();
        File_(root, "a.txt");
        File_(root, "b.txt");

        var chunks = RootExpander.Expand(root);

        Assert.Single(chunks);
        Assert.True(chunks[0].Recursive);
        Assert.Equal(Path.GetFullPath(root), chunks[0].EnumRoot);
    }

    [Fact]
    public void ThinChain_TunnelsToContentFolder_AsOneRecursiveChunk()
    {
        // root/a/b/c/d/e — content only at the bottom; thin passages must not strand it.
        var root = NewRoot();
        var e = Dir(Dir(Dir(Dir(Dir(root, "a"), "b"), "c"), "d"), "e");
        File_(e, "deep1.txt");
        File_(e, "deep2.txt");

        var chunks = RootExpander.Expand(root);

        Assert.Single(chunks);
        Assert.True(chunks[0].Recursive);
        Assert.Equal(Path.GetFullPath(e), chunks[0].EnumRoot);
        AssertPartition(root, chunks);
    }

    [Fact]
    public void NasShape_ThinThenWide_SplitsAtTheWideLevel()
    {
        // root/dept/2024/{p1,p2,p3} — tunnel the thin prefix, split at the branch level.
        var root = NewRoot();
        var year = Dir(Dir(root, "dept"), "2024");
        foreach (var p in new[] { "p1", "p2", "p3" })
            File_(Dir(year, p), "file.txt");

        var chunks = RootExpander.Expand(root);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.Recursive));
        AssertPartition(root, chunks);
    }

    [Fact]
    public void PassageWithLooseFiles_EmitsNonRecursiveChunkForThem()
    {
        // root has a few loose files (≤ K) AND one subdir → passage; loose files still covered.
        var root = NewRoot();
        File_(root, "loose1.txt");
        File_(root, "loose2.txt");
        var sub = Dir(root, "sub");
        File_(sub, "inner.txt");

        var chunks = RootExpander.Expand(root);

        Assert.Contains(chunks, c => c.EnumRoot == Path.GetFullPath(root) && !c.Recursive);
        Assert.Contains(chunks, c => c.EnumRoot == Path.GetFullPath(sub) && c.Recursive);
        AssertPartition(root, chunks);
    }

    [Fact]
    public void DeepBranchingTree_StopsAtMaxDepth_AndStillPartitions()
    {
        // Branch (2 subdirs) at every level, deeper than MaxDepth, so the cap kicks in.
        var root = NewRoot();
        void Build(string dir, int depth)
        {
            if (depth == 0) { File_(dir, "leaf.txt"); return; }
            Build(Dir(dir, "x"), depth - 1);
            Build(Dir(dir, "y"), depth - 1);
        }
        Build(root, 5);

        var chunks = RootExpander.Expand(root);

        Assert.NotEmpty(chunks);
        AssertPartition(root, chunks); // cap-stopped chunks are recursive → tail never lost
    }

    [Fact]
    public void PathologicalDepth_HitsAbsoluteGuard_AndStillCoversBottomFile()
    {
        // A thin chain longer than AbsMaxDepth: the absolute guard must stop recursion while a
        // recursive chunk still swallows everything below it.
        var root = NewRoot();
        var cur = root;
        for (int i = 0; i < RootExpander.AbsMaxDepth + 6; i++)
            cur = Dir(cur, "n");
        File_(cur, "bottom.txt");

        var chunks = RootExpander.Expand(root);

        Assert.NotEmpty(chunks);
        AssertPartition(root, chunks);
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }
}
