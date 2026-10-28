namespace Blink.Core.Model;

/// <summary>
/// A single indexed document. <see cref="DocId"/> is the resolved absolute path
/// (immutable primary key and action target); <see cref="Path"/> is the display path.
/// <see cref="Mtime"/>/<see cref="Size"/> drive incremental re-index and pruning.
///
/// A <see cref="IsBundle"/> document is a VIRTUAL entry standing in for a large group of
/// homogeneous files (same folder + extension) that were collapsed to keep the index
/// small at scale — e.g. a folder of millions of sequentially-named images. Its
/// <see cref="MemberCount"/> is how many real files it represents; its <see cref="DocId"/>
/// is a synthetic <c>&lt;dir&gt;/__bundle__&lt;ext&gt;</c> path (the real members are not
/// indexed individually).
///
/// <see cref="Status"/>/<see cref="Detail"/> record how the body was extracted (null for bundle
/// markers and rows written before schema v3).
/// </summary>
public record Document(
    string DocId,
    string Path,
    double Mtime,
    long Size,
    string Content,
    bool IsBundle = false,
    int MemberCount = 0,
    ExtractStatus? Status = null,
    string? Detail = null);
