namespace Blink.Core.Parsers;

/// <summary>
/// Extracts indexable text from a file. The slice ships <c>TextParser</c> (.txt/.md)
/// and a <c>FilenameOnlyParser</c> fallback for unknown/binary extensions.
/// </summary>
public interface IParser
{
    /// <summary>Human-readable parser name (for diagnostics).</summary>
    string Name { get; }

    /// <summary>Lower-case file extensions this parser handles, including the leading dot (e.g. ".txt").</summary>
    string[] Extensions { get; }

    /// <summary>Whether this parser reads file content (false = filename-only fallback).</summary>
    bool ReadsContent { get; }

    /// <summary>Extract text content from the file at <paramref name="path"/>.</summary>
    string ExtractText(string path);
}
