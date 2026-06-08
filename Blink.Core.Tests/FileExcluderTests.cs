using Blink.Core.Indexing;

namespace Blink.Core.Tests;

public sealed class FileExcluderTests
{
    private static string P(params string[] parts) => Path.Combine(parts.Prepend(Root).ToArray());
    private const string Root = "/root";

    [Theory]
    [InlineData("~$Book1.xlsx", true)]   // Office lock file — the real-world offender
    [InlineData("~$report.docx", true)]
    [InlineData("draft.tmp", true)]
    [InlineData("notes.temp", true)]
    [InlineData("backup~", true)]
    [InlineData("Thumbs.db", true)]
    [InlineData("desktop.ini", true)]
    [InlineData(".DS_Store", true)]
    [InlineData("download.crdownload", true)]
    [InlineData("Book1.xlsx", false)]     // a real workbook is NOT excluded
    [InlineData("report.docx", false)]
    [InlineData("notes.txt", false)]
    public void DefaultPatterns_MatchJunkByName(string fileName, bool excluded)
    {
        var ex = FileExcluder.Default();
        Assert.Equal(excluded, ex.IsExcluded(P(fileName), Root));
    }

    [Fact]
    public void DirectoryRule_ExcludesEverythingUnderMatchedFolder()
    {
        var ex = FileExcluder.Default();
        Assert.True(ex.IsExcluded(P(".git", "config"), Root));
        Assert.True(ex.IsExcluded(P("pkg", "node_modules", "lib", "a.js"), Root));
        Assert.False(ex.IsExcluded(P("src", "a.js"), Root));
    }

    [Fact]
    public void CustomPattern_PathAndNameRules()
    {
        var ex = new FileExcluder(new[] { "*.log", "build/", "secret/keys.txt" });
        Assert.True(ex.IsExcluded(P("app.log"), Root));
        Assert.True(ex.IsExcluded(P("build", "out.dll"), Root));
        Assert.True(ex.IsExcluded(P("secret", "keys.txt"), Root));
        Assert.False(ex.IsExcluded(P("secret", "other.txt"), Root));
        Assert.False(ex.IsExcluded(P("app.txt"), Root));
    }

    [Fact]
    public void CommentsAndBlankLines_AreIgnored()
    {
        var ex = new FileExcluder(new[] { "", "  ", "# a comment", "*.bak" });
        Assert.True(ex.IsExcluded(P("x.bak"), Root));
        Assert.False(ex.IsExcluded(P("comment"), Root));
    }
}
