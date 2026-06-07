namespace Blink.Core.Store;

/// <summary>
/// Capability for stores that can return a document's original content by doc id.
/// Used by <c>InProcessProvider</c> for inline match-line extraction. Kept separate
/// from <see cref="IIndexStore"/> so the search contract stays minimal.
/// </summary>
public interface IContentStore
{
    string? GetContent(string docId);
}
