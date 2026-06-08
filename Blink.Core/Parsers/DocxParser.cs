using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Blink.Core.Parsers;

/// <summary>
/// Extracts paragraph text from <c>.docx</c> documents (Open XML), one paragraph per
/// line. Replaces the Python prototype's python-docx.
/// </summary>
public sealed class DocxParser : IParser
{
    public string Name => "DocxParser";
    public string[] Extensions => [".docx"];
    public bool ReadsContent => true;

    public string ExtractText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            var text = para.InnerText;
            if (text.Length > 0)
                sb.AppendLine(text);
        }
        return sb.ToString();
    }
}
