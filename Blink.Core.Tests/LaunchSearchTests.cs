using Blink.Core.Launch;

namespace Blink.Core.Tests;

public sealed class LaunchSearchTests
{
    private static IReadOnlyList<LaunchResult> Blink() => LaunchSearch.Search("blink", DemoIndex.Items);

    [Fact]
    public void Search_Empty_ReturnsEmpty()
    {
        Assert.Empty(LaunchSearch.Search("", DemoIndex.Items));
        Assert.Empty(LaunchSearch.Search("   ", DemoIndex.Items));
    }

    [Fact]
    public void Search_Blink_RanksByScore_ExactPrototypeOrder()
    {
        var r = Blink();
        // Top-9 per data.js scoring (ties keep index order). The first 6 are the screenshot rows.
        var ids = r.Select(x => x.Item.Id).ToArray();
        Assert.Equal(new[]
        {
            "file-arch",      // 80  (title prefix 50 + kw 12 + content 18)
            "fold-blink",     // 62  (title prefix 50 + kw 12)
            "file-logo",      // 62
            "file-readme",    // 30  (kw 12 + content 18)
            "file-roadmap",   // 30
            "file-notes",     // 30
            "file-spec",      // 12  (kw only)
            "file-screenshot",// 12
            "app-vscode",     // 8   (app type prior)
        }, ids);
    }

    [Fact]
    public void Search_Blink_ScoresAndContentHits()
    {
        var r = Blink();
        var arch = r.First(x => x.Item.Id == "file-arch");
        Assert.Equal(80, arch.Score);
        Assert.True(arch.ContentHit);

        Assert.False(r.First(x => x.Item.Id == "fold-blink").ContentHit); // title/kw only
        Assert.True(r.First(x => x.Item.Id == "file-readme").ContentHit); // content match, no title
        Assert.Equal(62, r.First(x => x.Item.Id == "fold-blink").Score);
        Assert.Equal(8, r.First(x => x.Item.Id == "app-vscode").Score);   // unconditional app prior
    }

    [Fact]
    public void Search_CapsAtNine()
    {
        Assert.True(Blink().Count <= 9);
        Assert.Equal(9, Blink().Count);
    }

    [Fact]
    public void Snippet_HighlightsFirstHit_WithWindow()
    {
        var arch = DemoIndex.Items.First(i => i.Id == "file-arch");
        var s = LaunchSearch.Snippet(arch, "blink");
        Assert.NotNull(s);
        Assert.Equal("The ", s!.Pre);          // pos 4, start 0 → no leading ellipsis
        Assert.Equal("Blink", s.Hit);          // original case preserved
        Assert.StartsWith(".Core engine", s.Post);
        Assert.EndsWith("…", s.Post);          // content longer than the 90-char tail
    }

    [Fact]
    public void Snippet_NoContent_ReturnsNull()
    {
        var folder = DemoIndex.Items.First(i => i.Id == "fold-blink");
        Assert.Null(LaunchSearch.Snippet(folder, "blink"));
    }

    [Fact]
    public void Snippet_NoMatch_ReturnsLeadingText()
    {
        var arch = DemoIndex.Items.First(i => i.Id == "file-arch");
        var s = LaunchSearch.Snippet(arch, "zzzznomatch");
        Assert.NotNull(s);
        Assert.Equal("", s!.Hit);
        Assert.StartsWith("The Blink.Core", s.Pre);
    }

    [Theory]
    [InlineData("1+1", "2")]
    [InlineData("12*12", "144")]
    [InlineData("1+2*3", "7")]        // precedence
    [InlineData("(2+3)*4", "20")]     // parentheses
    [InlineData("10/4", "2.5")]
    [InlineData("10/3", "3.3333")]    // 4 decimals, trailing handled
    [InlineData("2 + 2", "4")]        // whitespace
    [InlineData("-5 + 8", "3")]       // unary minus
    public void TryCalc_EvaluatesArithmetic(string expr, string expected)
    {
        var c = LaunchSearch.TryCalc(expr);
        Assert.NotNull(c);
        Assert.Equal(expected, c!.Value);
        Assert.Equal(expr, c.Expr);
    }

    [Theory]
    [InlineData("blink")]    // not arithmetic
    [InlineData("5")]        // too short, no operator
    [InlineData("12")]       // no operator
    [InlineData("()")]       // too short / no operator
    [InlineData("1/0")]      // infinity → rejected
    public void TryCalc_RejectsNonExpressions(string q)
    {
        Assert.Null(LaunchSearch.TryCalc(q));
    }
}
