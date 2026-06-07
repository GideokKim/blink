namespace Blink.Core.Model;

/// <summary>
/// A single indexed document. <see cref="DocId"/> is the resolved absolute path
/// (immutable primary key and action target); <see cref="Path"/> is the display path.
/// <see cref="Mtime"/>/<see cref="Size"/> are forward-looking scaffolding for the
/// deferred incremental re-index / pruner and are unused by the vertical slice.
/// </summary>
public record Document(string DocId, string Path, double Mtime, long Size, string Content);
