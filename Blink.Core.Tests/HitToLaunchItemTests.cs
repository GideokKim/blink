using Blink.Core.Launch;
using Blink.Core.Model;
using Blink.Core.Search;

namespace Blink.Core.Tests;

/// <summary>
/// Unit tests for <see cref="HitToLaunchItem"/> — the SearchHit→LaunchResult adapter that
/// lets the launcher render real FTS hits with the prototype's snippet/preview logic.
/// Pure helpers are tested directly; <see cref="HitToLaunchItem.Convert"/> uses a temp file
/// and a stub provider (clock injected for deterministic relative dates).
/// </summary>
public sealed class HitToLaunchItemTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string TempFile(string ext, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink_hit_{Guid.NewGuid():N}{ext}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
    }

    // ---- FormatSize ----
    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(4300, "4.2 KB")]
    [InlineData(862208, "842 KB")]
    [InlineData(2202010, "2.1 MB")]
    public void FormatSize_Formats(long bytes, string expected)
        => Assert.Equal(expected, HitToLaunchItem.FormatSize(bytes));

    // ---- FormatRelative (clock injected) ----
    [Fact]
    public void FormatRelative_BucketsByElapsed()
    {
        var now = new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal("방금", HitToLaunchItem.FormatRelative(now.AddSeconds(-30), now));
        Assert.Equal("3분 전", HitToLaunchItem.FormatRelative(now.AddMinutes(-3), now));
        Assert.Equal("5시간 전", HitToLaunchItem.FormatRelative(now.AddHours(-5), now));
        Assert.Equal("어제", HitToLaunchItem.FormatRelative(now.AddDays(-1), now));
        Assert.Equal("3일 전", HitToLaunchItem.FormatRelative(now.AddDays(-3), now));
        Assert.Equal("2주 전", HitToLaunchItem.FormatRelative(now.AddDays(-14), now));
        Assert.Equal("방금", HitToLaunchItem.FormatRelative(now.AddMinutes(5), now)); // future clamps to 0
    }

    // ---- GlyphFor / HueFor ----
    [Theory]
    [InlineData("xlsx", "xls")]
    [InlineData("png", "img")]
    [InlineData("", "▣")]
    [InlineData("yaml", "yam")]
    public void GlyphFor_MapsExtension(string ext, string glyph)
        => Assert.Equal(glyph, HitToLaunchItem.GlyphFor(ext));

    [Theory]
    [InlineData("pdf", 12)]
    [InlineData("zzz", 250)]
    public void HueFor_MapsExtension(string ext, int hue)
        => Assert.Equal(hue, HitToLaunchItem.HueFor(ext));

    // ---- Convert ----
    [Fact]
    public void Convert_RealFile_PopulatesSizeAndMod_AndContentHit()
    {
        var path = TempFile(".txt", "본문 한 줄");
        var now = new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Local);
        var hit = new SearchHit("doc-1", path, -2.5);
        var provider = new StubProvider(new[] { new MatchLine(1, "matched line") });

        var result = HitToLaunchItem.Convert(hit, "본문", provider, now);

        Assert.Equal("doc-1", result.Item.Id);
        Assert.Equal(LaunchItemKind.File, result.Item.Type);
        Assert.Equal(Path.GetFileName(path), result.Item.Title);
        Assert.NotNull(result.Item.Size);
        Assert.NotNull(result.Item.Mod);
        Assert.Equal("txt", result.Item.Ext);
        Assert.Equal("matched line", result.Item.Content);
        Assert.True(result.ContentHit);
    }

    [Fact]
    public void Convert_MissingFile_LeavesSizeAndModNull()
    {
        // Build under temp so Windows doesn't reinterpret "/nonexistent" as a drive root.
        var path = Path.Combine(Path.GetTempPath(), $"blink_missing_{Guid.NewGuid():N}.txt");
        var hit = new SearchHit("doc-2", path, -1.0);
        var provider = new StubProvider(Array.Empty<MatchLine>());

        var result = HitToLaunchItem.Convert(hit, "q", provider, DateTime.Now);

        Assert.Null(result.Item.Size);
        Assert.Null(result.Item.Mod);
        Assert.Null(result.Item.Content);
        Assert.False(result.ContentHit);
    }

    [Fact]
    public void Convert_Directory_IsFolderKind()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempFiles.Add(dir); // Dispose's File.Delete won't remove a dir; clean explicitly below
        try
        {
            var hit = new SearchHit("doc-3", dir, -1.0);
            var provider = new StubProvider(Array.Empty<MatchLine>());

            var result = HitToLaunchItem.Convert(hit, "q", provider, DateTime.Now);

            Assert.Equal(LaunchItemKind.Folder, result.Item.Type);
            Assert.Null(result.Item.Size); // directories are not stat'd for size
        }
        finally
        {
            try { Directory.Delete(dir); } catch { }
        }
    }

    /// <summary>Minimal <see cref="ISearchProvider"/> returning fixed match lines.</summary>
    private sealed class StubProvider : ISearchProvider
    {
        private readonly IReadOnlyList<MatchLine> _lines;
        public StubProvider(IReadOnlyList<MatchLine> lines) => _lines = lines;

        public IReadOnlyList<SearchHit> Search(string query, int limit = 50, int offset = 0)
            => Array.Empty<SearchHit>();
        public IReadOnlyList<MatchLine> GetMatchLines(string docId, string query, int maxLines = 5)
            => _lines;
        public IReadOnlyDictionary<string, long> GetBundleSizes(IEnumerable<string> docIds)
            => new Dictionary<string, long>();
    }
}
