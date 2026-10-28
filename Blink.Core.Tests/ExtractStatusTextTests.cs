using Blink.Core.Indexing;
using Blink.Core.Model;

namespace Blink.Core.Tests;

public sealed class ExtractStatusTextTests
{
    [Fact]
    public void EveryReportableStatus_HasLabelAndHint()
    {
        foreach (var s in Enum.GetValues<ExtractStatus>().Where(s => s.IsReportable()))
        {
            Assert.False(string.IsNullOrWhiteSpace(ExtractStatusText.Label(s)));
            Assert.False(string.IsNullOrWhiteSpace(ExtractStatusText.Hint(s)));
        }
    }

    [Fact]
    public void EncryptedHint_MentionsAllowingBlink()
        => Assert.Contains("Blink.exe", ExtractStatusText.Hint(ExtractStatus.Encrypted));

    [Fact]
    public void FailureCount_IgnoresInformational()
    {
        var counts = new Dictionary<ExtractStatus, long>
        {
            [ExtractStatus.Ok] = 100, [ExtractStatus.Encrypted] = 3, [ExtractStatus.ReadDenied] = 2,
            [ExtractStatus.EmptyText] = 7, [ExtractStatus.TooLarge] = 1, [ExtractStatus.NotApplicable] = 50,
        };
        Assert.Equal(5, ExtractStatusText.FailureCount(counts));
        Assert.Equal(0, ExtractStatusText.FailureCount(new Dictionary<ExtractStatus, long>()));
    }

    [Fact]
    public void ToTsv_HeaderPlusRows_SanitizesTabsAndNewlines()
    {
        var tsv = ExtractStatusText.ToTsv(new[]
        {
            new ExtractFailure("C:\\d\\a.docx", ExtractStatus.Encrypted, "bad\tzip\nheader"),
            new ExtractFailure("C:\\d\\b.pdf", ExtractStatus.EmptyText, null),
        });
        var lines = tsv.Split('\n');
        Assert.Equal("상태\t경로\t상세", lines[0]);
        Assert.Equal("암호화된 것으로 보임\tC:\\d\\a.docx\tbad zip header", lines[1]);
        Assert.Equal("텍스트 없음\tC:\\d\\b.pdf\t", lines[2]);
        Assert.Equal(3, lines.Length);
    }
}
