using System.Globalization;
using System.Text;

namespace Blink.Core.Parsers;

/// <summary>
/// Extracts plain text from <c>.rtf</c> by stripping RTF control words and groups.
/// Handles the constructs that carry real content: <c>\uN</c> Unicode runs (so Korean
/// survives), <c>\'hh</c> hex bytes, escaped braces, and paragraph/line/tab breaks.
/// Non-content destination groups (font/color/style tables, <c>\*</c> ignorables) are
/// skipped. Dependency-free; replaces the Python prototype's striprtf.
/// </summary>
public sealed class RtfParser : IParser
{
    public string Name => "RtfParser";
    public string[] Extensions => [".rtf"];
    public bool ReadsContent => true;

    // Destination control words whose group contents are not document text.
    private static readonly HashSet<string> SkipDestinations = new(StringComparer.Ordinal)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object",
        "themedata", "colorschememapping", "latentstyles", "datastore",
        "generator", "operator", "company", "filetbl", "listtable", "listoverridetable",
    };

    public string ExtractText(string path)
    {
        var rtf = File.ReadAllText(path);
        return Strip(rtf);
    }

    public static string Strip(string rtf)
    {
        var sb = new StringBuilder(rtf.Length);
        var groupIgnore = new Stack<bool>();
        bool ignore = false;
        int ucSkip = 1;     // \ucN: number of ANSI fallback chars following a \uN run
        int skipChars = 0;  // remaining fallback chars to drop after a \uN

        int i = 0, n = rtf.Length;
        while (i < n)
        {
            char c = rtf[i];

            if (c == '{') { groupIgnore.Push(ignore); i++; continue; }
            if (c == '}') { if (groupIgnore.Count > 0) ignore = groupIgnore.Pop(); skipChars = 0; i++; continue; }

            if (c == '\\')
            {
                if (i + 1 >= n) { i++; continue; }
                char d = rtf[i + 1];

                if (d is '\\' or '{' or '}')
                {
                    if (!ignore && skipChars == 0) sb.Append(d); else if (skipChars > 0) skipChars--;
                    i += 2;
                    continue;
                }
                if (d == '*') { ignore = true; i += 2; continue; } // ignorable destination

                if (d == '\'') // \'hh hex byte
                {
                    if (i + 3 < n + 1 && i + 4 <= n)
                    {
                        var hex = rtf.Substring(i + 2, 2);
                        i += 4;
                        if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                        {
                            if (skipChars > 0) skipChars--;
                            else if (!ignore) sb.Append((char)b);
                        }
                    }
                    else i = n;
                    continue;
                }

                if (char.IsLetter(d)) // control word, optional numeric param
                {
                    int j = i + 1;
                    while (j < n && char.IsLetter(rtf[j])) j++;
                    string word = rtf.Substring(i + 1, j - (i + 1));

                    int k = j;
                    bool neg = false;
                    if (k < n && rtf[k] == '-') { neg = true; k++; }
                    int param = 0; bool hasParam = false;
                    while (k < n && char.IsDigit(rtf[k])) { hasParam = true; param = param * 10 + (rtf[k] - '0'); k++; }
                    if (neg) param = -param;

                    i = k;
                    if (i < n && rtf[i] == ' ') i++; // a single space delimiter is consumed

                    HandleControlWord(word, hasParam, param, sb, ref ignore, ref ucSkip);
                    continue;
                }

                i += 2; // unknown control symbol (e.g. \~, \-)
                continue;
            }

            if (c is '\r' or '\n') { i++; continue; } // RTF newlines are not content
            if (skipChars > 0) { skipChars--; i++; continue; }
            if (!ignore) sb.Append(c);
            i++;
        }
        return sb.ToString();

        void HandleControlWord(string word, bool hasParam, int param, StringBuilder buf, ref bool ign, ref int uc)
        {
            switch (word)
            {
                case "par":
                case "line":
                case "sect":
                    if (!ign) buf.Append('\n');
                    break;
                case "tab":
                    if (!ign) buf.Append('\t');
                    break;
                case "uc":
                    if (hasParam) uc = param;
                    break;
                case "u": // \uN Unicode char; drop the next `uc` fallback chars
                    if (hasParam)
                    {
                        if (!ign) buf.Append((char)(param < 0 ? param + 0x10000 : param));
                        skipChars = uc;
                    }
                    break;
                default:
                    if (SkipDestinations.Contains(word))
                        ign = true; // ignore the rest of this group
                    break;
            }
        }
    }
}
