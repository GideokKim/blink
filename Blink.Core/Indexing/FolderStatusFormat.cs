using System.Globalization;
using System.Text;

namespace Blink.Core.Indexing;

/// <summary>
/// Formats the Settings folder-row subtitle from index stats + last-indexed time.
/// Pure (testable) — the WPF layer feeds it live values.
/// </summary>
public static class FolderStatusFormat
{
    /// <summary>Folder-row note when a chunk was unreachable at prune time.</summary>
    public const string PruneSkipped = "드라이브 연결 안 됨 · 정리 보류";

    private const int MaxRunIssueMessage = 120;

    /// <summary>
    /// Folder-row subtitle. Never indexed (no timestamp) or stats unavailable → "대기 중…".
    /// Indexed but holding 0 documents → "0 파일 · 마지막 인덱싱 {상대시각}" (NOT "대기 중…",
    /// so an empty-but-indexed folder is distinguishable from one still waiting).
    /// Otherwise → "{n} 파일 · {size} · 마지막 인덱싱 {상대시각}".
    /// Then appends " · 본문 실패 {n}" when <paramref name="extractFailures"/> &gt; 0 and
    /// " · {runIssue}" when <paramref name="runIssue"/> is set.
    /// </summary>
    public static string Subtitle((long Count, long Bytes)? stats, DateTime? lastIndexedUtc, DateTime nowUtc,
        long extractFailures = 0, string? runIssue = null)
    {
        var sb = new StringBuilder(BaseSubtitle(stats, lastIndexedUtc, nowUtc));
        if (extractFailures > 0)
            sb.Append(" · 본문 실패 ").Append(extractFailures.ToString("N0", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(runIssue))
            sb.Append(" · ").Append(runIssue);
        return sb.ToString();
    }

    private static string BaseSubtitle((long Count, long Bytes)? stats, DateTime? lastIndexedUtc, DateTime nowUtc)
    {
        if (lastIndexedUtc is null || stats is null)
            return "대기 중…";

        var (count, bytes) = stats.Value;
        var rel = RelTime(nowUtc - lastIndexedUtc.Value);
        return count <= 0
            ? $"0 파일 · 마지막 인덱싱 {rel}"
            : $"{count.ToString("N0", CultureInfo.InvariantCulture)} 파일 · {HumanSize(bytes)} · 마지막 인덱싱 {rel}";
    }

    /// <summary>Folder-row note for a folder whose last run threw.</summary>
    public static string RunFailed(Exception ex)
    {
        var msg = $"{ex.GetType().Name}: {ex.Message}".ReplaceLineEndings(" ");
        if (msg.Length > MaxRunIssueMessage) msg = msg[..MaxRunIssueMessage];
        return "마지막 실행 실패: " + msg;
    }

    /// <summary>Human-readable byte size (1024-based; 1 decimal for KB and up).</summary>
    public static string HumanSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes / 1024.0;
        if (v < 1024) return v.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        v /= 1024.0;
        if (v < 1024) return v.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        v /= 1024.0;
        return v.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>Relative time in Korean: 방금 / N분 전 / N시간 전 / N일 전 (negative spans clamp to 방금).</summary>
    public static string RelTime(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return "방금";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}분 전";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}시간 전";
        return $"{(int)span.TotalDays}일 전";
    }
}
