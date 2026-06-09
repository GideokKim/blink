using Blink.Core.Parsers;

namespace Blink.Core.Tests;

/// <summary>
/// Verifies the per-parser <see cref="IParser.MaxParseSize"/> caps. Heavy parsers declare a
/// byte ceiling above which their body is skipped (filename search still works); light parsers
/// leave it null (no cap).
/// </summary>
public sealed class ParserCapTests
{
    private const long MB = 1024 * 1024;

    [Fact]
    public void DefaultMember_IsNull_ForUncappedParser()
    {
        // default interface member is only reachable through the interface type (CS1061 otherwise).
        Assert.Null(((IParser)new TextParser()).MaxParseSize);
    }

    [Theory]
    [InlineData(typeof(XlsxParser), 25 * MB)]
    [InlineData(typeof(HwpxParser), 25 * MB)]
    [InlineData(typeof(PptxParser), 50 * MB)]
    public void HeavyParsers_DeclareCap(Type parserType, long expected)
    {
        // Parameterless construction must keep working (no ctor injection — see XLSX caps test).
        var parser = (IParser)Activator.CreateInstance(parserType)!;
        Assert.Equal(expected, parser.MaxParseSize);
    }
}
