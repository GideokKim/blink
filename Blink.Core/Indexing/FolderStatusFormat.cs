using System.Globalization;

namespace Blink.Core.Indexing;

/// <summary>
/// Formats the Settings folder-row subtitle from index stats + last-indexed time.
/// Pure (testable) — the WPF layer feeds it live values.
/// </summary>
public static class FolderStatusFormat
{
    /// <summary>
    /// Folder-row subtitle. Never indexed (no timestamp) or stats unavailable → "대기 중…".
    /// Indexed but holding 0 documents → "0 파일 · 마지막 인덱싱 {상대시각}" (NOT "대기 중…",
    /// so an empty-but-indexed folder is distinguishable from one still waiting).
    /// Otherwise → "{n} 파일 · {size} · 마지막 인덱싱 {상대시각}".
    /// </summary>
    public static string Subtitle((long Count, long Bytes)? stats, DateTime? lastIndexedUtc, DateTime nowUtc)
    {
        if (lastIndexedUtc is null || stats is null)
            return "대기 중…";

        var (count, bytes) = stats.Value;
        var rel = RelTime(nowUtc - lastIndexedUtc.Value);
        return count <= 0
            ? $"0 파일 · 마지막 인덱싱 {rel}"
            : $"{count.ToString("N0", CultureInfo.InvariantCulture)} 파일 · {HumanSize(bytes)} · 마지막 인덱싱 {rel}";
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
