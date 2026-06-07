namespace Blink.Core.Parsers;

/// <summary>
/// Fallback parser for unknown/binary file extensions. Returns empty content;
/// the indexer contributes the filename as the indexable text.
/// </summary>
public sealed class FilenameOnlyParser : IParser
{
    public string Name => "FilenameOnlyParser";
    public string[] Extensions => [];
    public bool ReadsContent => false;

    public string ExtractText(string path) => string.Empty;
}
