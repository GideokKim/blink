using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Blink.Core.Parsers;

/// <summary>
/// Extracts text from Hancom <c>.hwpx</c> files. HWPX is an OpenXML-style ZIP whose
/// body lives in <c>Contents/section*.xml</c>; this reads every XML part under
/// <c>Contents/</c> and concatenates its text nodes. No third-party dependency —
/// just <see cref="System.IO.Compression"/> + LINQ-to-XML.
/// </summary>
public sealed class HwpxParser : IParser
{
    public string Name => "HwpxParser";
    public string[] Extensions => [".hwpx"];
    public bool ReadsContent => true;
    public long? MaxParseSize => 25L * 1024 * 1024;

    public string ExtractText(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var sb = new StringBuilder();

        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase)
                || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;

            using var stream = entry.Open();
            XDocument xml;
            try { xml = XDocument.Load(stream); }
            catch { continue; } // skip malformed parts

            foreach (var text in xml.Descendants().SelectMany(e => e.Nodes()).OfType<XText>())
            {
                var value = text.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sb.Append(value);
                    sb.Append(' ');
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
