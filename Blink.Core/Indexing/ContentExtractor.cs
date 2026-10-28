using System.Globalization;
using System.Text;
using Blink.Core.Model;
using Blink.Core.Parsers;

namespace Blink.Core.Indexing;

/// <summary>Body text plus how it was obtained (or why it wasn't).</summary>
public readonly record struct ExtractOutcome(string Text, ExtractStatus Status, string? Detail);

/// <summary>
/// Runs a parser over one file and classifies the result. Replaces the indexer's old
/// "swallow every exception → empty body" so a failure leaves evidence: on DRM drives the
/// parser reads ciphertext (signature mismatch → <see cref="ExtractStatus.Encrypted"/>), on
/// on-demand drives the read fails with a cloud-file error or on an offline placeholder
/// (→ <see cref="ExtractStatus.CloudUnavailable"/>).
/// </summary>
public static class ContentExtractor
{
    /// <summary>Max length of the "Type: message" part of a detail string.</summary>
    public const int MaxDetailLength = 200;

    // Not in the FileAttributes enum: cloud-file placeholder flags.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes CloudFlags = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    // HRESULT_FROM_WIN32 of the ERROR_CLOUD_FILE_* block (358–409) and the timeouts.
    private const int CloudFirst = unchecked((int)0x80070166);
    private const int CloudLast = unchecked((int)0x80070199);
    private const int SemTimeout = unchecked((int)0x80070079);
    private const int Win32Timeout = unchecked((int)0x800705B4);

    // The file is open in another program (Excel/Word hold an exclusive handle while editing).
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly Dictionary<string, byte[]> PrefixSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = Zip, [".xlsx"] = Zip, [".pptx"] = Zip, [".hwpx"] = Zip,
        [".rtf"] = Encoding.ASCII.GetBytes("{\\rtf"),
    };
    private static readonly byte[] PdfMagic = Encoding.ASCII.GetBytes("%PDF");
    private const int PdfHeaderWindow = 1024; // PDF readers accept junk before %PDF within 1 KB

    public static ExtractOutcome Extract(IParser parser, string path, long size, long globalCap)
    {
        if (!parser.ReadsContent)
            return new(string.Empty, ExtractStatus.NotApplicable, null);

        if (!Indexer.ShouldExtract(true, size, parser.MaxParseSize, globalCap))
        {
            long cap = Math.Min(globalCap, parser.MaxParseSize ?? long.MaxValue);
            return new(string.Empty, ExtractStatus.TooLarge,
                string.Create(CultureInfo.InvariantCulture, $"size {size:N0} B exceeds cap {cap:N0} B"));
        }

        try
        {
            var text = parser.ExtractText(path);
            return string.IsNullOrWhiteSpace(text)
                ? new(string.Empty, ExtractStatus.EmptyText, null)
                : new(text, ExtractStatus.Ok, null);
        }
        catch (Exception ex)
        {
            return new(string.Empty, Classify(ex, path), Detail(ex, path));
        }
    }

    public static ExtractStatus Classify(Exception ex, string path)
    {
        var attrs = TryGetAttributes(path);
        bool cloudAttr = attrs is FileAttributes a && (a & CloudFlags) != 0;

        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException)
                return ExtractStatus.ReadDenied;
            if (e is IOException { HResult: SharingViolation or LockViolation })
                return ExtractStatus.Locked;
            if (e is TimeoutException)
                return ExtractStatus.CloudUnavailable;
            if (e is IOException io && (IsCloudHResult(io.HResult) || cloudAttr))
                return ExtractStatus.CloudUnavailable;
        }

        return SignatureMatches(path) == false ? ExtractStatus.Encrypted : ExtractStatus.ParseError;
    }

    private static bool IsCloudHResult(int hr)
        => (hr >= CloudFirst && hr <= CloudLast) || hr == SemTimeout || hr == Win32Timeout;

    /// <summary>True/false when the extension has a known signature and the file is readable; null otherwise.</summary>
    private static bool? SignatureMatches(string path)
    {
        var ext = Path.GetExtension(path);
        bool isPdf = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf && !PrefixSignatures.ContainsKey(ext))
            return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[isPdf ? PdfHeaderWindow : PrefixSignatures[ext].Length];
            int n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            var head = buf.AsSpan(0, n);
            return isPdf ? head.IndexOf(PdfMagic) >= 0 : head.SequenceEqual(PrefixSignatures[ext]);
        }
        catch
        {
            return null; // can't read the bytes either → no signature evidence
        }
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch { return null; }
    }

    private static string Detail(Exception ex, string path)
    {
        var head = $"{ex.GetType().Name}: {ex.Message}".ReplaceLineEndings(" ");
        if (head.Length > MaxDetailLength)
            head = head[..MaxDetailLength];
        var flags = AttributeFlags(path);
        return flags.Length == 0 ? head : $"{head} [attrs: {flags}]";
    }

    private static string AttributeFlags(string path)
    {
        if (TryGetAttributes(path) is not FileAttributes a)
            return string.Empty;
        var names = new List<string>();
        if ((a & FileAttributes.Offline) != 0) names.Add("Offline");
        if ((a & FileAttributes.ReparsePoint) != 0) names.Add("ReparsePoint");
        if ((a & FileAttributes.Encrypted) != 0) names.Add("Encrypted");
        if ((a & RecallOnOpen) != 0) names.Add("RecallOnOpen");
        if ((a & RecallOnDataAccess) != 0) names.Add("RecallOnDataAccess");
        return string.Join(", ", names);
    }
}
