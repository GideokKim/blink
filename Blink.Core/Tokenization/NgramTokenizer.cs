using System.Globalization;
using System.Text;

namespace Blink.Core.Tokenization;

/// <summary>
/// Pure, stateless tokenizer that produces n-gram tokens for FTS5 indexing.
/// Hangul syllable runs → 2-grams + 3-grams (distinct, sorted).
/// ASCII/digit runs → whole word tokens.
/// </summary>
public static class NgramTokenizer
{
    // Hangul syllable block: U+AC00–U+D7A3
    // Hangul Jamo: U+1100–U+11FF
    // Hangul Compatibility Jamo: U+3130–U+318F
    private static bool IsHangul(char c) =>
        (c >= '가' && c <= '힣') ||
        (c >= 'ᄀ' && c <= 'ᇿ') ||
        (c >= '㄰' && c <= '㆏');

    private static bool IsAsciiAlphaNum(char c) =>
        (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

    /// <summary>Returns the space-joined, distinct, sorted token string for a given text.</summary>
    public static string Tokenize(string text) =>
        string.Join(' ', Tokens(text));

    /// <summary>Returns the distinct, sorted token list for a given text.</summary>
    public static IReadOnlyList<string> Tokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        // NFC-normalize, then lowercase (invariant culture)
        var normalized = text.Normalize(NormalizationForm.FormC);
        var lower = normalized.ToLowerInvariant();

        var tokens = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;

        while (i < lower.Length)
        {
            char c = lower[i];

            if (IsHangul(c))
            {
                // Collect entire Hangul run
                int start = i;
                while (i < lower.Length && IsHangul(lower[i]))
                    i++;

                var run = lower.AsSpan(start, i - start);
                int len = run.Length;

                // Emit 2-grams
                for (int k = 0; k < len - 1; k++)
                    tokens.Add(new string(run.Slice(k, 2)));

                // Emit 3-grams
                for (int k = 0; k < len - 2; k++)
                    tokens.Add(new string(run.Slice(k, 3)));

                // Single-char Hangul with no neighbor: emit itself so it's findable
                if (len == 1)
                    tokens.Add(new string(run));
            }
            else if (IsAsciiAlphaNum(c))
            {
                // Collect entire ASCII/digit run
                int start = i;
                while (i < lower.Length && IsAsciiAlphaNum(lower[i]))
                    i++;

                tokens.Add(lower[start..i]);
            }
            else
            {
                i++;
            }
        }

        var result = new List<string>(tokens);
        result.Sort(StringComparer.Ordinal);
        return result;
    }
}
