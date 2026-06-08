using System.Text;
using UglyToad.PdfPig;

namespace Blink.Core.Parsers;

/// <summary>
/// Extracts the text layer from <c>.pdf</c> files (PdfPig), one page per line.
/// Scanned/image-only PDFs have no text layer and yield empty content (they fall back
/// to filename-only indexing); OCR is a separate, deferred concern. Replaces the
/// Python prototype's pypdf.
/// </summary>
public sealed class PdfParser : IParser
{
    public string Name => "PdfParser";
    public string[] Extensions => [".pdf"];
    public bool ReadsContent => true;

    public string ExtractText(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrEmpty(text))
                sb.AppendLine(text);
        }
        return sb.ToString();
    }
}
