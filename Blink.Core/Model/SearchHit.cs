namespace Blink.Core.Model;

/// <summary>
/// A single search result. <see cref="Score"/> is the raw <c>bm25()</c> value
/// (lower = better; results are ordered ascending then by <see cref="DocId"/>).
/// <see cref="Size"/>/<see cref="Mtime"/> (unix seconds) / <see cref="IsBundle"/> carry the
/// indexed DB metadata so display conversion needs no filesystem stat (a stat on a network
/// drive can take hundreds of ms per hit). Null on legacy hits → callers fall back to stat.
/// </summary>
public record SearchHit(string DocId, string Path, double Score, string? Snippet = null,
    long? Size = null, double? Mtime = null, bool IsBundle = false);
