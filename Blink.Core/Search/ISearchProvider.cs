using Blink.Core.Model;

namespace Blink.Core.Search;

/// <summary>
/// GUI-facing search facade over an <c>IIndexStore</c>. Adds match-line extraction
/// for inline result expansion. Bundle sizing is stubbed (bundles deferred).
/// </summary>
public interface ISearchProvider
{
    /// <summary>Run a paginated search (delegates to the underlying store).</summary>
    IReadOnlyList<SearchHit> Search(string query, int limit = 50, int offset = 0);

    /// <summary>
    /// Return up to <paramref name="maxLines"/> content lines of the document identified by
    /// <paramref name="docId"/> that contain ALL query tokens (after NFC normalization +
    /// lower-casing). Used for inline match-line expansion.
    /// </summary>
    IReadOnlyList<MatchLine> GetMatchLines(string docId, string query, int maxLines = 5);

    /// <summary>Deferred (bundles out of scope for the slice); returns an empty map.</summary>
    IReadOnlyDictionary<string, long> GetBundleSizes(IEnumerable<string> docIds);
}

/// <summary>A single matching content line for inline expansion.</summary>
public record MatchLine(int LineNo, string Text);
