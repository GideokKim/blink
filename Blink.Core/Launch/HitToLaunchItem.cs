using System.Globalization;
using Blink.Core.Model;
using Blink.Core.Search;

namespace Blink.Core.Launch;

/// <summary>
/// Adapts a <see cref="SearchHit"/> (path + bm25 score from the real FTS index) into the
/// launcher's display model <see cref="LaunchResult"/>. Size/modified-time come from a file
/// stat; the body match-lines come from the provider and are stuffed into
/// <see cref="LaunchItem.Content"/> so the existing snippet/preview logic works unchanged.
/// </summary>
public static class HitToLaunchItem
{
    private const int DefaultHue = 250;

    public static LaunchResult Convert(SearchHit hit, string query, ISearchProvider provider, DateTime now)
        => Convert(hit, JoinMatchLines(provider.GetMatchLines(hit.DocId, query, 5)), now);

    /// <summary>Core conversion with the match-line content already fetched (batch path).</summary>
    private static LaunchResult Convert(SearchHit hit, string? content, DateTime now)
    {
        var path = hit.Path;
        bool isDir = Directory.Exists(path);
        var kind = isDir ? LaunchItemKind.Folder : LaunchItemKind.File;

        var title = Path.GetFileName(path.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(title)) title = path;
        var sub = Path.GetDirectoryName(path) ?? path;
        var ext = isDir ? "" : Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        string? size = null, mod = null;
        if (!isDir)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    size = FormatSize(fi.Length);
                    mod = FormatRelative(fi.LastWriteTime, now);
                }
            }
            catch { /* permission / unreachable network path — leave null */ }
        }

        var item = new LaunchItem(
            Id: hit.DocId, Type: kind, Title: title, Sub: sub,
            Glyph: GlyphFor(ext), Hue: HueFor(ext),
            Ext: ext.Length == 0 ? null : ext, Size: size, Mod: mod,
            Content: content, Keywords: null);

        // hit.Score is bm25 (negative; provider pre-sorts). View ignores it → display-only cast.
        return new LaunchResult(item, (int)hit.Score, content is not null);
    }

    public static LaunchResult Convert(SearchHit hit, string query, ISearchProvider provider)
        => Convert(hit, query, provider, DateTime.Now);

    /// <summary>
    /// Converts a batch of hits. Match lines come from ONE
    /// <see cref="ISearchProvider.GetMatchLinesMany"/> call for the whole page (instead of
    /// a per-hit N+1 query), and <paramref name="ct"/> is checked between items so a stale
    /// query can be abandoned promptly — each item still does a file stat.
    /// </summary>
    public static IReadOnlyList<LaunchResult> ConvertAll(
        IReadOnlyList<SearchHit> hits, string query, ISearchProvider provider,
        CancellationToken ct, DateTime? now = null)
    {
        ct.ThrowIfCancellationRequested();
        var lineMap = provider.GetMatchLinesMany(hits.Select(h => h.DocId), query, 5);

        var list = new List<LaunchResult>(hits.Count);
        foreach (var hit in hits)
        {
            ct.ThrowIfCancellationRequested();   // hit당 stat이 비싸므로 매 건 사이 체크
            var lines = lineMap.TryGetValue(hit.DocId, out var l) ? l : Array.Empty<MatchLine>();
            list.Add(Convert(hit, JoinMatchLines(lines), now ?? DateTime.Now));
        }
        return list;
    }

    public static string? JoinMatchLines(IReadOnlyList<MatchLine> lines)
        => lines.Count == 0 ? null : string.Join("\n", lines.Select(l => l.Text));

    public static string GlyphFor(string ext) => ext switch
    {
        "md" or "markdown" => "md",
        "pdf" => "pdf",
        "xls" or "xlsx" or "csv" => "xls",
        "doc" or "docx" => "doc",
        "ppt" or "pptx" => "ppt",
        "txt" => "txt",
        "png" or "jpg" or "jpeg" or "gif" or "svg" or "webp" => "img",
        "" => "▣",
        _ => ext.Length <= 3 ? ext : ext[..3],
    };

    public static int HueFor(string ext) => ext switch
    {
        "md" or "markdown" or "txt" => 250,
        "pdf" => 12,
        "xls" or "xlsx" or "csv" => 150,
        "doc" or "docx" => 220,
        "ppt" or "pptx" => 30,
        "png" or "jpg" or "jpeg" or "gif" or "svg" or "webp" => 320,
        _ => DefaultHue,
    };

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = { "KB", "MB", "GB", "TB" };
        double v = bytes; int u = -1;
        do { v /= 1024; u++; } while (v >= 1024 && u < units.Length - 1);
        return v < 10
            ? $"{v.ToString("F1", CultureInfo.InvariantCulture)} {units[u]}"
            : $"{v.ToString("F0", CultureInfo.InvariantCulture)} {units[u]}";
    }

    public static string FormatRelative(DateTime mod, DateTime now)
    {
        var span = now - mod;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return "방금";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}분 전";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}시간 전";
        int days = (int)(now.Date - mod.Date).TotalDays;   // 캘린더 기준 (47h → "2일 전")
        if (days <= 1) return "어제";
        if (days < 7) return $"{days}일 전";
        if (days < 28) return $"{days / 7}주 전";
        return mod.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
