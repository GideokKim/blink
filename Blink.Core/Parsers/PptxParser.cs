using System.Text;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;

namespace Blink.Core.Parsers;

/// <summary>
/// Extracts text runs from every slide of a <c>.pptx</c> presentation (Open XML),
/// one slide per line. Replaces the Python prototype's python-pptx.
/// </summary>
public sealed class PptxParser : IParser
{
    public string Name => "PptxParser";
    public string[] Extensions => [".pptx"];
    public bool ReadsContent => true;
    public long? MaxParseSize => 50L * 1024 * 1024;

    public string ExtractText(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var slidePart in presentationPart.SlideParts)
        {
            if (slidePart.Slide is null)
                continue;
            var texts = slidePart.Slide.Descendants<A.Text>().Select(t => t.Text);
            var line = string.Join(" ", texts.Where(t => t.Length > 0));
            if (line.Length > 0)
                sb.AppendLine(line);
        }
        return sb.ToString();
    }
}
