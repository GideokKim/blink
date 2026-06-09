using Blink.Core.Indexing;

namespace Blink.Core.Tests;

/// <summary>
/// Verifies <see cref="Indexer.ShouldExtract"/> — the pure gate that decides whether a file's
/// body is parsed. A small global cap is injected so the boundary is testable without huge files.
/// </summary>
public sealed class ContentCapTests
{
    [Fact]
    public void FilenameOnlyParser_NeverExtracts()
        => Assert.False(Indexer.ShouldExtract(readsContent: false, size: 10, parserCap: null, globalCap: 1000));

    [Fact]
    public void UnderGlobalCap_NoParserCap_Extracts()
        => Assert.True(Indexer.ShouldExtract(readsContent: true, size: 999, parserCap: null, globalCap: 1000));

    [Fact]
    public void AtGlobalCap_Extracts()
        => Assert.True(Indexer.ShouldExtract(readsContent: true, size: 1000, parserCap: null, globalCap: 1000));

    [Fact]
    public void OverGlobalCap_Skips()
        => Assert.False(Indexer.ShouldExtract(readsContent: true, size: 1001, parserCap: null, globalCap: 1000));

    [Fact]
    public void ParserCapTighterThanGlobal_Wins()
    {
        // parserCap=500 < globalCap=1000: 600 is over the parser's own ceiling.
        Assert.True(Indexer.ShouldExtract(readsContent: true, size: 500, parserCap: 500, globalCap: 1000));
        Assert.False(Indexer.ShouldExtract(readsContent: true, size: 501, parserCap: 500, globalCap: 1000));
    }

    [Fact]
    public void GlobalCapTighterThanParser_Wins()
    {
        // parserCap=5000 > globalCap=1000: global still bounds it.
        Assert.True(Indexer.ShouldExtract(readsContent: true, size: 1000, parserCap: 5000, globalCap: 1000));
        Assert.False(Indexer.ShouldExtract(readsContent: true, size: 1001, parserCap: 5000, globalCap: 1000));
    }
}
