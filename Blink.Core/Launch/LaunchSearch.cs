using System.Globalization;
using System.Text.RegularExpressions;

namespace Blink.Core.Launch;

/// <summary>A highlighted content snippet: text before the hit, the matched term, text after.</summary>
public sealed record LaunchSnippet(string Pre, string Hit, string Post);

/// <summary>An evaluated inline-calculator result: the source expression and its formatted value.</summary>
public sealed record CalcResult(string Expr, string Value);

/// <summary>A scored search result. <see cref="ContentHit"/> is true when a query token matched inside the document body.</summary>
public sealed record LaunchResult(LaunchItem Item, int Score, bool ContentHit);

/// <summary>
/// Launcher ranking, snippet, and calculator — a faithful port of the prototype's
/// <c>data.js</c> (<c>search()</c>, <c>snippet()</c>, <c>tryCalc()</c>). Scoring weights:
/// title prefix +50, title substring +30, keyword/path hit +12, content hit +18, plus a
/// type prior (app +8, command +2). Results are sorted by score descending (ties keep index
/// order) and the top 9 are returned.
/// </summary>
public static class LaunchSearch
{
    private const int MaxResults = 9;

    private static readonly Regex CalcChars = new(@"^[-+/*().\d\s]+$", RegexOptions.Compiled);
    private static readonly Regex CalcOperator = new(@"[-+/*]", RegexOptions.Compiled);

    /// <summary>Score <paramref name="index"/> against <paramref name="query"/> and return the top results.</summary>
    public static IReadOnlyList<LaunchResult> Search(string query, IEnumerable<LaunchItem> index)
    {
        var q = (query ?? "").Trim().ToLowerInvariant();
        if (q.Length == 0)
            return Array.Empty<LaunchResult>();

        var toks = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<LaunchResult>();

        foreach (var it in index)
        {
            var title = it.Title.ToLowerInvariant();
            var hay = (it.Title + " " + (it.Keywords ?? "") + " " + (it.Sub ?? "")).ToLowerInvariant();
            var contentHay = (it.Content ?? "").ToLowerInvariant();

            int score = 0;
            bool contentHit = false;

            foreach (var tk in toks)
            {
                if (title.StartsWith(tk, StringComparison.Ordinal)) score += 50;
                else if (title.Contains(tk, StringComparison.Ordinal)) score += 30;

                if (hay.Contains(tk, StringComparison.Ordinal)) score += 12;
                if (contentHay.Contains(tk, StringComparison.Ordinal)) { score += 18; contentHit = true; }
            }

            // type priors
            if (it.Type == LaunchItemKind.App) score += 8;
            if (it.Type == LaunchItemKind.Command) score += 2;

            if (score > 0)
                scored.Add(new LaunchResult(it, score, contentHit));
        }

        // Stable sort by score desc (OrderByDescending preserves input order for ties),
        // then take the top 9 — matching the prototype's slice(0, 9).
        return scored.OrderByDescending(r => r.Score).Take(MaxResults).ToList();
    }

    /// <summary>
    /// Build a highlighted snippet around the first matched token in the item's content
    /// (~48 chars before, ~90 after, edges ellipsized). Returns null if the item has no
    /// content. If no token matches, returns the first 120 chars as <see cref="LaunchSnippet.Pre"/>.
    /// </summary>
    public static LaunchSnippet? Snippet(LaunchItem item, string query)
    {
        if (string.IsNullOrEmpty(item.Content))
            return null;

        var text = item.Content;
        var lower = text.ToLowerInvariant();
        var toks = (query ?? "").Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        int pos = -1, hitLen = 0;
        foreach (var tk in toks)
        {
            int p = lower.IndexOf(tk, StringComparison.Ordinal);
            if (p != -1 && (pos == -1 || p < pos)) { pos = p; hitLen = tk.Length; }
        }

        if (pos == -1)
            return new LaunchSnippet(text[..Math.Min(120, text.Length)], "", "");

        int start = Math.Max(0, pos - 48);
        int end = Math.Min(text.Length, pos + hitLen + 90);
        return new LaunchSnippet(
            Pre: (start > 0 ? "…" : "") + text[start..pos],
            Hit: text[pos..(pos + hitLen)],
            Post: text[(pos + hitLen)..end] + (end < text.Length ? "…" : ""));
    }

    /// <summary>
    /// If <paramref name="query"/> is a pure arithmetic expression (only <c>-+/*().</c>,
    /// digits, whitespace; contains an operator; length ≥ 3), evaluate it. Value formatting
    /// matches the prototype: integers print plainly; non-integers use up to 4 decimals with
    /// trailing zeros trimmed.
    /// </summary>
    public static CalcResult? TryCalc(string query)
    {
        var s = (query ?? "").Trim();
        if (s.Length < 3 || !CalcChars.IsMatch(s) || !CalcOperator.IsMatch(s))
            return null;

        var val = ArithmeticEvaluator.Evaluate(s);
        if (val is not double v || !double.IsFinite(v))
            return null;

        string value = v == Math.Floor(v)
            ? v.ToString("0", CultureInfo.InvariantCulture)
            : v.ToString("F4", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return new CalcResult(s, value);
    }
}
