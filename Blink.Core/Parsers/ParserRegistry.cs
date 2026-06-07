namespace Blink.Core.Parsers;

/// <summary>
/// Selects the appropriate <see cref="IParser"/> for a given file path based on extension.
/// Unknown extensions fall back to <see cref="FilenameOnlyParser"/>.
/// </summary>
public static class ParserRegistry
{
    private static readonly TextParser _textParser = new();
    private static readonly FilenameOnlyParser _filenameParser = new();

    private static readonly Dictionary<string, IParser> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);

    static ParserRegistry()
    {
        foreach (var ext in _textParser.Extensions)
            _byExtension[ext] = _textParser;
    }

    /// <summary>Returns the best-matching parser for <paramref name="path"/>.</summary>
    public static IParser GetParser(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return _byExtension.TryGetValue(ext, out var parser) ? parser : _filenameParser;
    }
}
