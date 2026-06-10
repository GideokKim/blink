using Blink.Core.Indexing;

namespace Blink.Core.Tests;

public sealed class FolderStatusFormatTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    // ── Subtitle ────────────────────────────────────────────────────────────

    [Fact]
    public void Subtitle_NeverIndexed_ShowsWaiting()
        => Assert.Equal("대기 중…", FolderStatusFormat.Subtitle((0, 0), lastIndexedUtc: null, Now));

    [Fact]
    public void Subtitle_StatsUnavailable_ShowsWaiting()
        => Assert.Equal("대기 중…", FolderStatusFormat.Subtitle(stats: null, Now.AddMinutes(-5), Now));

    [Fact]
    public void Subtitle_IndexedButEmpty_ShowsZeroFilesWithTimestamp_NotWaiting()
        => Assert.Equal("0 파일 · 마지막 인덱싱 5분 전",
            FolderStatusFormat.Subtitle((0, 0), Now.AddMinutes(-5), Now));

    [Fact]
    public void Subtitle_IndexedWithFiles_ShowsCountSizeAndTimestamp()
        => Assert.Equal("158 파일 · 1.2 GB · 마지막 인덱싱 2시간 전",
            FolderStatusFormat.Subtitle((158, (long)(1.2 * 1024 * 1024 * 1024)), Now.AddHours(-2), Now));

    [Fact]
    public void Subtitle_LargeCount_UsesThousandsSeparator()
        => Assert.StartsWith("62,296 파일",
            FolderStatusFormat.Subtitle((62_296, 1024), Now.AddMinutes(-5), Now));

    // ── HumanSize ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    public void HumanSize_FormatsByMagnitude(long bytes, string expected)
        => Assert.Equal(expected, FolderStatusFormat.HumanSize(bytes));

    // ── RelTime ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "방금")]
    [InlineData(59, "방금")]
    [InlineData(60, "1분 전")]
    [InlineData(59 * 60, "59분 전")]
    [InlineData(60 * 60, "1시간 전")]
    [InlineData(23 * 60 * 60, "23시간 전")]
    [InlineData(24 * 60 * 60, "1일 전")]
    public void RelTime_FormatsBySpan(int seconds, string expected)
        => Assert.Equal(expected, FolderStatusFormat.RelTime(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void RelTime_NegativeSpan_ClampsToJustNow()
        => Assert.Equal("방금", FolderStatusFormat.RelTime(TimeSpan.FromSeconds(-30)));
}
