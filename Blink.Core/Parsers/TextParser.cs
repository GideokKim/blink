using System.Text;

namespace Blink.Core.Parsers;

/// <summary>
/// Reads plain text content from .txt and .md files, with BOM auto-detection.
/// </summary>
public sealed class TextParser : IParser
{
    public string Name => "TextParser";
    public string[] Extensions => [".txt", ".md"];
    public bool ReadsContent => true;

    public string ExtractText(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
