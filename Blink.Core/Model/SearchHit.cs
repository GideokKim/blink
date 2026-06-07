namespace Blink.Core.Model;

/// <summary>
/// A single search result. <see cref="Score"/> is the raw <c>bm25()</c> value
/// (lower = better; results are ordered ascending then by <see cref="DocId"/>).
/// </summary>
public record SearchHit(string DocId, string Path, double Score, string? Snippet = null);
