namespace Blink.Core.Indexing;

/// <summary>
/// Progress payload reported via <c>IProgress&lt;IndexProgress&gt;</c> during indexing.
/// <see cref="Total"/> is the discovered file count (0 until enumeration completes if
/// the indexer streams); <see cref="Processed"/> counts files handled so far.
/// </summary>
public record IndexProgress(int Processed, int Total, string? CurrentPath = null);
