using System.Text;

namespace Blink.Core.Update;

public enum MdBlockKind { Heading, Bullet, Paragraph }

public enum MdInlineKind { Text, Code, Bold, Link }

/// <summary>One inline run; <see cref="Url"/> is set only for <see cref="MdInlineKind.Link"/>.</summary>
public sealed record MdInline(MdInlineKind Kind, string Text, string? Url = null);

/// <summary>Heading <see cref="Level"/> is 1–3; 0 for other kinds.</summary>
public sealed record MdBlock(MdBlockKind Kind, int Level, IReadOnlyList<MdInline> Inlines);

/// <summary>
/// Tiny GitHub-markdown subset parser for release notes: #/##/### headings, -/* bullets,
/// paragraphs, and inline `code` / **bold** / [text](url). Anything fancier degrades to
/// plain text — good enough for auto-generated release notes, no external library.
/// </summary>
public static class MarkdownLite
{
    public static IReadOnlyList<MdBlock> Parse(string? markdown)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrWhiteSpace(markdown)) return blocks;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;

            if (t.StartsWith('#'))
            {
                int level = t.TakeWhile(c => c == '#').Count();
                blocks.Add(new MdBlock(MdBlockKind.Heading, Math.Min(level, 3),
                    ParseInlines(t[level..].TrimStart())));
            }
            else if (t.StartsWith("- ", StringComparison.Ordinal) ||
                     t.StartsWith("* ", StringComparison.Ordinal))
            {
                blocks.Add(new MdBlock(MdBlockKind.Bullet, 0, ParseInlines(t[2..].TrimStart())));
            }
            else
            {
                blocks.Add(new MdBlock(MdBlockKind.Paragraph, 0, ParseInlines(t)));
            }
        }
        return blocks;
    }

    internal static IReadOnlyList<MdInline> ParseInlines(string text)
    {
        var runs = new List<MdInline>();
        var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length > 0)
            {
                runs.Add(new MdInline(MdInlineKind.Text, plain.ToString()));
                plain.Clear();
            }
        }

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    runs.Add(new MdInline(MdInlineKind.Code, text[(i + 1)..end]));
                    i = end + 1;
                    continue;
                }
            }
            else if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    Flush();
                    runs.Add(new MdInline(MdInlineKind.Bold, text[(i + 2)..end]));
                    i = end + 2;
                    continue;
                }
            }
            else if (text[i] == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                {
                    int urlEnd = text.IndexOf(')', close + 2);
                    if (urlEnd > close)
                    {
                        Flush();
                        runs.Add(new MdInline(MdInlineKind.Link,
                            text[(i + 1)..close], text[(close + 2)..urlEnd]));
                        i = urlEnd + 1;
                        continue;
                    }
                }
            }
            plain.Append(text[i]);
            i++;
        }
        Flush();
        return runs;
    }
}
