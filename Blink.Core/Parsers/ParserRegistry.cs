namespace Blink.Core.Parsers;

/// <summary>
/// Selects the appropriate <see cref="IParser"/> for a given file path based on extension.
/// Unknown extensions fall back to <see cref="FilenameOnlyParser"/>.
/// </summary>
public static class ParserRegistry
{
    private static readonly FilenameOnlyParser _filenameParser = new();

    // Content parsers, registered by the extensions they declare. Add a parser here and
    // its Extensions are auto-mapped; unknown extensions fall back to filename-only.
    private static readonly IParser[] _contentParsers =
    {
        new TextParser(),
        new XlsxParser(),
        new PdfParser(),
        new DocxParser(),
        new PptxParser(),
        new HwpxParser(),
        new RtfParser(),
    };

    private static readonly Dictionary<string, IParser> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);

    static ParserRegistry()
    {
        foreach (var parser in _contentParsers)
            foreach (var ext in parser.Extensions)
                _byExtension[ext] = parser;
    }

    /// <summary>Returns the best-matching parser for <paramref name="path"/>.</summary>
    public static IParser GetParser(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return _byExtension.TryGetValue(ext, out var parser) ? parser : _filenameParser;
    }
}
