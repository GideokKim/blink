namespace Blink.Core.Store;

/// <summary>
/// Capability for stores that can return a document's original content by doc id.
/// Used by <c>InProcessProvider</c> for inline match-line extraction. Kept separate
/// from <see cref="IIndexStore"/> so the search contract stays minimal.
/// </summary>
public interface IContentStore
{
    string? GetContent(string docId);

    /// <summary>
    /// For each id that is a bundle entry, the number of real files it represents.
    /// Non-bundle / unknown ids are omitted from the result.
    /// </summary>
    IReadOnlyDictionary<string, long> GetBundleSizes(IEnumerable<string> docIds);
}
