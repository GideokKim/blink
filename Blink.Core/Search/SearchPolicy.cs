namespace Blink.Core.Search;

/// <summary>Pure query→search-parameter policy decisions (no I/O, no state).</summary>
public static class SearchPolicy
{
    /// <summary>
    /// 단일 문자 쿼리는 매칭이 과다하므로 결과 상한을 축소(50→20) — FTS 후처리(변환·
    /// match-line) 비용을 줄인다. 그 외에는 <paramref name="defaultLimit"/> 그대로.
    /// </summary>
    public static int EffectiveLimit(string query, int defaultLimit)
        => (query ?? "").Trim().Length == 1 ? Math.Min(20, defaultLimit) : defaultLimit;
}
