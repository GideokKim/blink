using System.Text;
using Blink.Core.Model;
using Blink.Core.Search;
using Blink.Core.Store;

namespace Blink.Core.Tests;

public sealed class InProcessProviderTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    private SqliteFtsStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blink-prov-{Guid.NewGuid():N}.db");
        _tempPaths.Add(path);
        return new SqliteFtsStore(path);
    }

    [Fact]
    public void Search_DelegatesToStore()
    {
        using var store = NewStore();
        store.Upsert(new Document("/d.txt", "/d.txt", 0, 0, "한글검색 내용"));
        var provider = new InProcessProvider(store);
        Assert.Single(provider.Search("글검"));
    }

    [Fact]
    public void GetMatchLines_ReturnsLinesContainingAllWords_CappedAt5()
    {
        using var store = NewStore();
        var content = string.Join("\n", new[]
        {
            "first line about apple",
            "second line apple and banana",
            "third line banana only",
            "apple banana again four",
            "apple banana again five",
            "apple banana again six",
            "apple banana again seven",
        });
        store.Upsert(new Document("/m.txt", "/m.txt", 0, 0, content));
        var provider = new InProcessProvider(store);

        var lines = provider.GetMatchLines("/m.txt", "apple banana", maxLines: 5);
        Assert.Equal(5, lines.Count); // capped
        Assert.All(lines, l => Assert.Contains("apple", l.Text));
        Assert.All(lines, l => Assert.Contains("banana", l.Text));
        // Line numbers are 1-based; the first match is line 2 ("second line apple and banana").
        Assert.Equal(2, lines[0].LineNo);
    }

    [Fact]
    public void GetMatchLines_Korean_NfcInsensitive()
    {
        using var store = NewStore();
        store.Upsert(new Document("/k.txt", "/k.txt", 0, 0, "첫째 줄\n한글검색 이 줄\n셋째 줄"));
        var provider = new InProcessProvider(store);

        var lines = provider.GetMatchLines("/k.txt", "한글검색");
        Assert.Single(lines);
        Assert.Equal(2, lines[0].LineNo);
    }

    [Fact]
    public void GetMatchLines_NoMatch_OrMissingDoc_ReturnsEmpty()
    {
        using var store = NewStore();
        store.Upsert(new Document("/k.txt", "/k.txt", 0, 0, "내용 없음"));
        var provider = new InProcessProvider(store);

        Assert.Empty(provider.GetMatchLines("/k.txt", "존재안함"));
        Assert.Empty(provider.GetMatchLines("/missing.txt", "내용"));
        Assert.Empty(provider.GetMatchLines("/k.txt", ""));
    }

    [Fact]
    public void GetMatchLinesMany_EqualsPerDocResults()
    {
        using var store = NewStore();
        store.Upsert(new Document("/k.txt", "/k.txt", 0, 0, "첫째 줄\n한글검색 이 줄\n셋째 줄"));
        store.Upsert(new Document("/m.txt", "/m.txt", 0, 0, "한글검색 첫 줄\n둘째 줄\n한글검색 셋째 줄"));
        var provider = new InProcessProvider(store);
        var ids = new[] { "/k.txt", "/m.txt", "/missing.txt" };

        var batch = provider.GetMatchLinesMany(ids, "한글검색");

        Assert.Equal(ids.Length, batch.Count); // every requested id gets an entry
        foreach (var id in ids)
            Assert.Equal(provider.GetMatchLines(id, "한글검색"), batch[id]);
    }

    // ---- ExtractMatchLines (pure helper) ----
    [Fact]
    public void ExtractMatchLines_CapsAtMaxLines_AndRequiresAllWords()
    {
        var content = string.Join("\n",
            "first line about apple",
            "second line apple and banana",
            "third line banana only",
            "apple banana again four",
            "apple banana again five",
            "apple banana again six");

        var lines = InProcessProvider.ExtractMatchLines(content, "apple banana", maxLines: 3);

        Assert.Equal(3, lines.Count); // capped
        Assert.All(lines, l => Assert.Contains("apple", l.Text));
        Assert.All(lines, l => Assert.Contains("banana", l.Text)); // ALL words required (AND)
        Assert.Equal(2, lines[0].LineNo); // 1-based; first match is line 2
    }

    [Fact]
    public void ExtractMatchLines_Korean_NfcInsensitive()
    {
        var nfd = "한글검색 이 줄".Normalize(NormalizationForm.FormD);

        var lines = InProcessProvider.ExtractMatchLines("첫째 줄\n" + nfd, "한글검색", maxLines: 5);

        Assert.Single(lines);
        Assert.Equal(2, lines[0].LineNo);
    }

    [Fact]
    public void GetBundleSizes_IsEmpty()
    {
        using var store = NewStore();
        var provider = new InProcessProvider(store);
        Assert.Empty(provider.GetBundleSizes(new[] { "/a", "/b" }));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in _tempPaths)
            foreach (var f in new[] { p, p + "-wal", p + "-shm" })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
    }
}
