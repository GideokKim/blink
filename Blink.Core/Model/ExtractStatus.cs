namespace Blink.Core.Model;

/// <summary>
/// Outcome of extracting a document's body text. Persisted per document (schema v3) so the
/// UI can explain why a file is searchable by name only. NULL in the store = legacy row whose
/// outcome was never recorded.
/// </summary>
public enum ExtractStatus
{
    Ok = 0,
    /// <summary>No content parser for this format (filename-only by design).</summary>
    NotApplicable = 1,
    /// <summary>Over the global or parser-specific size cap; body skipped.</summary>
    TooLarge = 2,
    ReadDenied = 3,
    /// <summary>On-demand / placeholder file whose content could not be fetched.</summary>
    CloudUnavailable = 4,
    /// <summary>Parser failed and the bytes don't carry the format's signature (DRM / encryption).</summary>
    Encrypted = 5,
    ParseError = 6,
    /// <summary>Parsed fine but no text (e.g. a scanned PDF).</summary>
    EmptyText = 7,
}

public static class ExtractStatusInfo
{
    /// <summary>Statuses that mean "body should be searchable but isn't" — what the badge counts.</summary>
    public static bool IsFailure(this ExtractStatus s)
        => s is ExtractStatus.ReadDenied or ExtractStatus.CloudUnavailable
             or ExtractStatus.Encrypted or ExtractStatus.ParseError;

    /// <summary>Statuses listed in the failures window: failures plus informational skips.</summary>
    public static bool IsReportable(this ExtractStatus s)
        => s.IsFailure() || s is ExtractStatus.TooLarge or ExtractStatus.EmptyText;
}

/// <summary>One document whose body was not extracted, as listed by the store.</summary>
public sealed record ExtractFailure(string DocId, ExtractStatus Status, string? Detail);
