namespace Blink.Core.Indexing;

/// <summary>
/// The auto-index cadence options surfaced in Settings ("자동 인덱싱 주기") and their real
/// polling periods. <c>"off"</c> (수동) maps to <c>null</c> — the scheduler stays disabled and
/// indexing only runs on demand ("지금 다시 인덱싱"). Unknown/blank keys fall back to <see cref="Default"/>.
/// </summary>
public static class AutoIndexInterval
{
    /// <summary>The cadence used when none is configured: hourly.</summary>
    public const string Default = "1h";

    /// <summary>A selectable cadence: <paramref name="Key"/> is persisted; <paramref name="Period"/> null = manual.</summary>
    public sealed record Option(string Key, string Label, string Description, string Tag, TimeSpan? Period);

    /// <summary>The four options, in display order (matches the design handoff dropdown).</summary>
    public static readonly IReadOnlyList<Option> Options = new[]
    {
        new Option("15m", "15분마다", "자주 바뀌는 작업 폴더에 적합 (권장)", "15m", TimeSpan.FromMinutes(15)),
        new Option("1h",  "1시간마다", "균형 잡힌 기본값",                 "1h",  TimeSpan.FromHours(1)),
        new Option("6h",  "6시간마다", "배터리·성능 절약",                 "6h",  TimeSpan.FromHours(6)),
        new Option("off", "수동",     "\"지금 다시 인덱싱\"을 누를 때만",  "off", null),
    };

    /// <summary>Look up an option by key (case-insensitive); the default option if unknown/blank.</summary>
    public static Option Resolve(string? key) =>
        Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? Options.First(o => o.Key == Default);

    /// <summary>Map a cadence key to its polling period; <c>null</c> = manual (scheduler off).</summary>
    public static TimeSpan? ToPeriod(string? key) => Resolve(key).Period;

    /// <summary>True when the cadence disables automatic indexing (the "off"/수동 option).</summary>
    public static bool IsManual(string? key) => Resolve(key).Period is null;
}
