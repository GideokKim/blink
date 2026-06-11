using Blink.Core.Search;

namespace Blink.Core.Tests;

/// <summary>
/// Unit tests for <see cref="SearchPolicy"/> — the pure query→limit policy that shrinks
/// the result cap for single-character queries (they match far too many rows).
/// </summary>
public sealed class SearchPolicyTests
{
    [Theory]
    [InlineData("", 50, 50)]    // empty → default
    [InlineData("a", 50, 20)]   // single char → capped at 20
    [InlineData("한", 50, 20)]  // single char (Hangul) → capped
    [InlineData("ab", 50, 50)]  // two chars → default
    [InlineData(" a ", 50, 20)] // whitespace-trimmed length decides
    [InlineData("  ", 50, 50)]  // whitespace-only trims to empty → default
    [InlineData("a", 10, 10)]   // defaultLimit already below 20 → keep the smaller
    public void EffectiveLimit_CapsSingleCharQueries(string query, int defaultLimit, int expected)
        => Assert.Equal(expected, SearchPolicy.EffectiveLimit(query, defaultLimit));

    [Fact]
    public void EffectiveLimit_NullQuery_ReturnsDefault()
        => Assert.Equal(50, SearchPolicy.EffectiveLimit(null!, 50));
}
