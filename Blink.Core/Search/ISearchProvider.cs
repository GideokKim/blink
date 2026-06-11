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

    /// <summary>
    /// Batch variant of <see cref="GetMatchLines"/> — every requested id gets an entry
    /// (empty list when nothing matches). Default implementation loops per doc;
    /// store-backed providers override it with a single batched content fetch so a
    /// result page costs one query instead of N.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<MatchLine>> GetMatchLinesMany(
        IEnumerable<string> docIds, string query, int maxLines = 5)
    {
        var result = new Dictionary<string, IReadOnlyList<MatchLine>>();
        foreach (var id in docIds)
            result[id] = GetMatchLines(id, query, maxLines);
        return result;
    }

    /// <summary>Deferred (bundles out of scope for the slice); returns an empty map.</summary>
    IReadOnlyDictionary<string, long> GetBundleSizes(IEnumerable<string> docIds);
}

/// <summary>A single matching content line for inline expansion.</summary>
public record MatchLine(int LineNo, string Text);
