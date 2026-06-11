using Blink.Core.Update;

namespace Blink.Core.Tests;

public sealed class MarkdownLiteTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Parse_Empty_ReturnsNoBlocks(string? md) =>
        Assert.Empty(MarkdownLite.Parse(md));

    [Theory]
    [InlineData("# Title", 1)]
    [InlineData("## What's Changed", 2)]
    [InlineData("### Sub", 3)]
    [InlineData("##### Deep", 3)] // 3단계로 캡
    public void Parse_Heading_LevelAndText(string md, int level)
    {
        var b = Assert.Single(MarkdownLite.Parse(md));
        Assert.Equal(MdBlockKind.Heading, b.Kind);
        Assert.Equal(level, b.Level);
    }

    [Theory]
    [InlineData("- item")]
    [InlineData("* item")]
    [InlineData("  - item")] // 들여쓴 불릿도 불릿
    public void Parse_Bullet(string md)
    {
        var b = Assert.Single(MarkdownLite.Parse(md));
        Assert.Equal(MdBlockKind.Bullet, b.Kind);
        Assert.Equal("item", Assert.Single(b.Inlines).Text);
    }

    [Fact]
    public void Parse_Paragraph_AndSkipsBlankLines()
    {
        var blocks = MarkdownLite.Parse("first\r\n\r\nsecond\n");
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.Paragraph, b.Kind));
    }

    [Fact]
    public void Parse_InlineCode()
    {
        var b = Assert.Single(MarkdownLite.Parse("use `dotnet test` now"));
        Assert.Collection(b.Inlines,
            i => { Assert.Equal(MdInlineKind.Text, i.Kind); Assert.Equal("use ", i.Text); },
            i => { Assert.Equal(MdInlineKind.Code, i.Kind); Assert.Equal("dotnet test", i.Text); },
            i => { Assert.Equal(MdInlineKind.Text, i.Kind); Assert.Equal(" now", i.Text); });
    }

    [Fact]
    public void Parse_Bold()
    {
        var b = Assert.Single(MarkdownLite.Parse("**Full Changelog**: link"));
        Assert.Equal(MdInlineKind.Bold, b.Inlines[0].Kind);
        Assert.Equal("Full Changelog", b.Inlines[0].Text);
    }

    [Fact]
    public void Parse_Link()
    {
        var b = Assert.Single(MarkdownLite.Parse("see [notes](https://example.com/x)"));
        var link = b.Inlines[1];
        Assert.Equal(MdInlineKind.Link, link.Kind);
        Assert.Equal("notes", link.Text);
        Assert.Equal("https://example.com/x", link.Url);
    }

    [Theory]
    [InlineData("`unterminated")]
    [InlineData("**unterminated")]
    [InlineData("[text](no-close")]
    [InlineData("[text] (not-a-link)")]
    public void Parse_UnterminatedMarkers_DegradeToPlainText(string md)
    {
        var b = Assert.Single(MarkdownLite.Parse(md));
        var run = Assert.Single(b.Inlines);
        Assert.Equal(MdInlineKind.Text, run.Kind);
        Assert.Equal(md, run.Text);
    }

    [Fact]
    public void Parse_TypicalGitHubAutoNotes()
    {
        var blocks = MarkdownLite.Parse(
            "## What's Changed\n" +
            "* feat: 검색 개선 by @GideokKim in https://github.com/GideokKim/blink/pull/1\n" +
            "\n" +
            "**Full Changelog**: https://github.com/GideokKim/blink/compare/v0.1.0...v0.2.0");
        Assert.Equal(3, blocks.Count);
        Assert.Equal(MdBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[2].Kind);
    }
}
