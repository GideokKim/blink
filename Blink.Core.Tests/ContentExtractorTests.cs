using System.Text;
using Blink.Core.Indexing;
using Blink.Core.Model;
using Blink.Core.Parsers;

namespace Blink.Core.Tests;

public sealed class ContentExtractorTests : IDisposable
{
    private const long Cap = 100L * 1024 * 1024;
    private readonly List<string> _tempDirs = new();

    private sealed class FakeParser : IParser
    {
        private readonly Func<string, string> _extract;
        private readonly long? _cap;
        public FakeParser(Func<string, string> extract, long? cap = null) { _extract = extract; _cap = cap; }
        public string Name => "Fake";
        public string[] Extensions => [".docx"];
        public bool ReadsContent => true;
        public string ExtractText(string path) => _extract(path);
        public long? MaxParseSize => _cap;
    }

    private string TempFile(string name, byte[] bytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"blink-cx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static readonly byte[] ZipHeader = { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };

    [Fact]
    public void Extract_FilenameOnlyParser_IsNotApplicable()
    {
        var o = ContentExtractor.Extract(new FilenameOnlyParser(), "/x/a.png", 10, Cap);
        Assert.Equal(ExtractStatus.NotApplicable, o.Status);
        Assert.Equal("", o.Text);
        Assert.Null(o.Detail);
    }

    [Fact]
    public void Extract_OverParserCap_IsTooLarge_WithoutCallingParser()
    {
        bool called = false;
        var p = new FakeParser(_ => { called = true; return "x"; }, cap: 10);
        var o = ContentExtractor.Extract(p, "/x/a.docx", 11, Cap);
        Assert.Equal(ExtractStatus.TooLarge, o.Status);
        Assert.False(called);
        Assert.Contains("11", o.Detail);
    }

    [Fact]
    public void Extract_Text_IsOk()
    {
        var o = ContentExtractor.Extract(new FakeParser(_ => "본문"), "/x/a.docx", 5, Cap);
        Assert.Equal(ExtractStatus.Ok, o.Status);
        Assert.Equal("본문", o.Text);
        Assert.Null(o.Detail);
    }

    [Fact]
    public void Extract_WhitespaceText_IsEmptyText()
    {
        var o = ContentExtractor.Extract(new FakeParser(_ => " \r\n\t "), "/x/a.docx", 5, Cap);
        Assert.Equal(ExtractStatus.EmptyText, o.Status);
        Assert.Equal("", o.Text);
        Assert.False(o.Status.IsFailure());
        Assert.True(o.Status.IsReportable());
    }

    [Fact]
    public void Extract_Unauthorized_IsReadDenied_WithDetail()
    {
        var path = TempFile("a.docx", ZipHeader);
        var o = ContentExtractor.Extract(
            new FakeParser(_ => throw new UnauthorizedAccessException("denied")), path, 6, Cap);
        Assert.Equal(ExtractStatus.ReadDenied, o.Status);
        Assert.StartsWith("UnauthorizedAccessException: denied", o.Detail);
    }

    [Fact]
    public void Classify_WrappedUnauthorized_IsReadDenied()
    {
        var path = TempFile("a.docx", ZipHeader);
        var ex = new InvalidOperationException("outer", new UnauthorizedAccessException("inner"));
        Assert.Equal(ExtractStatus.ReadDenied, ContentExtractor.Classify(ex, path));
    }

    [Fact]
    public void Classify_CloudHResult_IsCloudUnavailable()
    {
        var path = TempFile("a.docx", ZipHeader);
        // HRESULT_FROM_WIN32(ERROR_CLOUD_FILE_PROVIDER_NOT_RUNNING = 362)
        var ex = new IOException("provider not running", unchecked((int)0x8007016A));
        Assert.Equal(ExtractStatus.CloudUnavailable, ContentExtractor.Classify(ex, path));
    }

    [Fact]
    public void Classify_Timeout_IsCloudUnavailable()
    {
        var path = TempFile("a.docx", ZipHeader);
        Assert.Equal(ExtractStatus.CloudUnavailable, ContentExtractor.Classify(new TimeoutException(), path));
    }

    [Fact]
    public void Classify_SignatureMismatch_IsEncrypted()
    {
        var path = TempFile("a.docx", Encoding.ASCII.GetBytes("DRMONE-ciphertext-bytes"));
        Assert.Equal(ExtractStatus.Encrypted,
            ContentExtractor.Classify(new InvalidDataException("bad zip"), path));
    }

    [Fact]
    public void Classify_PdfWithoutHeader_IsEncrypted_PdfWithHeader_IsParseError()
    {
        var bad = TempFile("a.pdf", Encoding.ASCII.GetBytes("garbage garbage"));
        var good = TempFile("b.pdf", Encoding.ASCII.GetBytes("\n%PDF-1.7\n..."));
        Assert.Equal(ExtractStatus.Encrypted, ContentExtractor.Classify(new Exception(), bad));
        Assert.Equal(ExtractStatus.ParseError, ContentExtractor.Classify(new Exception(), good));
    }

    [Fact]
    public void Classify_SignatureMatches_IsParseError()
    {
        var path = TempFile("a.docx", ZipHeader);
        Assert.Equal(ExtractStatus.ParseError,
            ContentExtractor.Classify(new InvalidDataException("no main part"), path));
    }

    [Fact]
    public void Classify_TextFormat_NeverEncrypted()
    {
        var path = TempFile("a.txt", new byte[] { 0xFF, 0xFE, 0x00 });
        Assert.Equal(ExtractStatus.ParseError, ContentExtractor.Classify(new Exception("x"), path));
    }

    [Fact]
    public void Classify_MissingFile_IsParseError_NotCrash()
    {
        var ex = new FileNotFoundException("gone");
        Assert.Equal(ExtractStatus.ParseError,
            ContentExtractor.Classify(ex, Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.docx")));
    }

    [Fact]
    public void Detail_IsSingleLine_AndMessageTruncated()
    {
        var path = TempFile("a.docx", ZipHeader);
        var longMsg = "line1\nline2 " + new string('x', 500);
        var o = ContentExtractor.Extract(new FakeParser(_ => throw new InvalidDataException(longMsg)), path, 6, Cap);
        Assert.DoesNotContain('\n', o.Detail);
        Assert.True(o.Detail!.Length <= ContentExtractor.MaxDetailLength + 64); // + optional attrs suffix
        Assert.StartsWith("InvalidDataException: line1 line2", o.Detail);
    }

    [Fact]
    public void FailureSet_IsExactlyTheFourBlockingStatuses()
    {
        var failures = Enum.GetValues<ExtractStatus>().Where(s => s.IsFailure()).ToArray();
        Assert.Equal(new[] { ExtractStatus.ReadDenied, ExtractStatus.CloudUnavailable,
                             ExtractStatus.Encrypted, ExtractStatus.ParseError }, failures);
        Assert.False(ExtractStatus.Ok.IsReportable());
        Assert.False(ExtractStatus.NotApplicable.IsReportable());
        Assert.True(ExtractStatus.TooLarge.IsReportable());
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }
}
