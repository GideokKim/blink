using Blink.Core.Indexing;

namespace Blink.Core.Tests;

public sealed class ScanCacheTests
{
    [Fact]
    public void RoundTrips_PathsInOrder()
    {
        var paths = new[] { "/a/b.txt", "/a/c/한글.jpg", "/d e/f.pdf" };
        using var cache = new ScanCache();
        foreach (var p in paths) cache.Append(p);
        cache.Seal();

        Assert.Equal(paths.Length, cache.Count);
        Assert.Equal(paths, cache.ReadAll().ToArray());
        // Re-readable multiple times (the indexer streams it twice).
        Assert.Equal(paths, cache.ReadAll().ToArray());
    }

    [Fact]
    public void Append_AfterSeal_Throws()
    {
        using var cache = new ScanCache();
        cache.Append("/x");
        cache.Seal();
        Assert.Throws<InvalidOperationException>(() => cache.Append("/y"));
    }

    [Fact]
    public void ReadAll_BeforeSeal_Throws()
    {
        using var cache = new ScanCache();
        cache.Append("/x");
        Assert.Throws<InvalidOperationException>(() => cache.ReadAll().ToList());
    }

    [Fact]
    public void Dispose_RemovesBackingFile()
    {
        ScanCache cache = new();
        cache.Append("/x");
        cache.Seal();
        Assert.Single(cache.ReadAll());
        cache.Dispose();
        // After dispose, reading should fail (file gone) — proves cleanup happened.
        Assert.ThrowsAny<Exception>(() => cache.ReadAll().ToList());
    }
}
