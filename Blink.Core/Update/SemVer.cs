using System.Globalization;

namespace Blink.Core.Update;

/// <summary>
/// Minimal semantic version: parsing + precedence comparison (semver.org §11).
/// Accepts an optional leading "v" (release tags are vX.Y.Z) and surrounding whitespace.
/// Build metadata ("+abc") is parsed off and ignored for comparison.
/// </summary>
public sealed class SemVer : IComparable<SemVer>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Dot-separated pre-release identifiers; empty for a stable release.</summary>
    public IReadOnlyList<string> PreRelease { get; }

    private SemVer(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public static bool TryParse(string? input, out SemVer? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();
        if (s[0] is 'v' or 'V') s = s[1..];

        int plus = s.IndexOf('+');                 // strip build metadata
        if (plus >= 0) s = s[..plus];

        string[] pre = Array.Empty<string>();
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            var preStr = s[(dash + 1)..];
            s = s[..dash];
            if (preStr.Length == 0) return false;
            pre = preStr.Split('.');
            if (pre.Any(p => p.Length == 0)) return false;
        }

        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        if (!TryParseNum(parts[0], out int maj) ||
            !TryParseNum(parts[1], out int min) ||
            !TryParseNum(parts[2], out int pat)) return false;

        version = new SemVer(maj, min, pat, pre);
        return true;
    }

    // NumberStyles.None: 부호/공백/지수 표기를 전부 거부 — "1.-2.3" 같은 입력 차단.
    private static bool TryParseNum(string s, out int n) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n);

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        c = Patch.CompareTo(other.Patch); if (c != 0) return c;

        // Stable > pre-release at the same numeric version (0.1.0-rc1 < 0.1.0).
        if (PreRelease.Count == 0) return other.PreRelease.Count == 0 ? 0 : 1;
        if (other.PreRelease.Count == 0) return -1;

        int len = Math.Min(PreRelease.Count, other.PreRelease.Count);
        for (int i = 0; i < len; i++)
        {
            c = CompareIdentifier(PreRelease[i], other.PreRelease[i]);
            if (c != 0) return c;
        }
        // 같은 prefix면 식별자 수가 적은 쪽이 낮다 (rc < rc.1).
        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    // Numeric identifiers compare as numbers and rank below alphanumeric; others ordinal.
    private static int CompareIdentifier(string a, string b)
    {
        bool an = a.All(char.IsAsciiDigit), bn = b.All(char.IsAsciiDigit);
        if (an && bn) return long.Parse(a, CultureInfo.InvariantCulture)
            .CompareTo(long.Parse(b, CultureInfo.InvariantCulture));
        if (an) return -1;
        if (bn) return 1;
        return string.CompareOrdinal(a, b);
    }

    public override string ToString() =>
        PreRelease.Count == 0
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{string.Join('.', PreRelease)}";
}
