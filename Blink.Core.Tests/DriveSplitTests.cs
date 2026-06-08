using Blink.Core.Indexing;

namespace Blink.Core.Tests;

public sealed class DriveSplitTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public void IsDriveRoot_TrueForVolumeRoot()
    {
        Assert.True(DriveSplit.IsDriveRoot(Path.GetPathRoot(Path.GetFullPath("."))!));
    }

    [Fact]
    public void IsDriveRoot_FalseForRegularFolder()
    {
        Assert.False(DriveSplit.IsDriveRoot(NewTree()));
    }

    [Fact]
    public void Expand_RegularFolder_ReturnsItself()
    {
        var dir = NewTree();
        var roots = DriveSplit.Expand(dir);
        Assert.Equal(new[] { Path.GetFullPath(dir) }, roots);
    }

    [Fact]
    public void Expand_ForceSplit_ReturnsImmediateChildren()
    {
        var dir = NewTree();
        var a = Path.Combine(dir, "shareA");
        var b = Path.Combine(dir, "shareB");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(dir, "loose.txt"), "x"); // not a dir → not a split root

        var roots = DriveSplit.Expand(dir, forceSplit: true);
        Assert.Equal(2, roots.Count);
        Assert.Contains(a, roots);
        Assert.Contains(b, roots);
    }

    [Fact]
    public void Expand_MissingRoot_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"blink-nope-{Guid.NewGuid():N}");
        Assert.Empty(DriveSplit.Expand(missing));
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }
}
