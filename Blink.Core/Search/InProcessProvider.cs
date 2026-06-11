using System.Globalization;
using System.Text;
using Blink.Core.Model;
using Blink.Core.Store;

namespace Blink.Core.Search;

/// <summary>
/// In-process <see cref="ISearchProvider"/> over an <see cref="IIndexStore"/>.
/// Search delegates to the store; match-line extraction reads original content via
/// an optional <see cref="IContentStore"/> capability.
/// </summary>
public sealed class InProcessProvider : ISearchProvider
{
    private readonly IIndexStore _store;
    private readonly IContentStore? _content;

    public InProcessProvider(IIndexStore store)
    {
        _store = store;
        _content = store as IContentStore;
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 50, int offset = 0)
        => _store.Search(query, limit, offset);

    public IReadOnlyList<MatchLine> GetMatchLines(string docId, string query, int maxLines = 5)
    {
        if (_content is null || string.IsNullOrWhiteSpace(query))
            return Array.Empty<MatchLine>();

        var content = _content.GetContent(docId);
        if (string.IsNullOrEmpty(content))
            return Array.Empty<MatchLine>();

        return ExtractMatchLines(content, query, maxLines);
    }

    /// <summary>
    /// Batch override: ONE <see cref="IContentStore.GetContents"/> round-trip for the whole
    /// page, then in-memory extraction per doc — instead of N content SELECTs.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<MatchLine>> GetMatchLinesMany(
        IEnumerable<string> docIds, string query, int maxLines = 5)
    {
        var ids = docIds as ICollection<string> ?? docIds.ToList();
        var result = new Dictionary<string, IReadOnlyList<MatchLine>>(ids.Count);

        if (_content is null || string.IsNullOrWhiteSpace(query))
        {
            foreach (var id in ids)
                result[id] = Array.Empty<MatchLine>();
            return result;
        }

        var contents = _content.GetContents(ids);
        foreach (var id in ids)
            result[id] = contents.TryGetValue(id, out var c) && c.Length > 0
                ? ExtractMatchLines(c, query, maxLines)
                : Array.Empty<MatchLine>();
        return result;
    }

    /// <summary>
    /// A line matches if it contains EVERY whitespace-separated query word
    /// (after NFC normalization + lower-casing on both sides). Line numbers are 1-based.
    /// </summary>
    internal static IReadOnlyList<MatchLine> ExtractMatchLines(string content, string query, int maxLines)
    {
        var words = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return Array.Empty<MatchLine>();

        var result = new List<MatchLine>(maxLines);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length && result.Count < maxLines; i++)
        {
            var hay = Normalize(lines[i]);
            if (words.All(w => hay.Contains(w, StringComparison.Ordinal)))
                result.Add(new MatchLine(i + 1, lines[i].TrimEnd()));
        }
        return result;
    }

    public IReadOnlyDictionary<string, long> GetBundleSizes(IEnumerable<string> docIds)
        => _content?.GetBundleSizes(docIds) ?? new Dictionary<string, long>();

    private static string Normalize(string s)
        => s.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
