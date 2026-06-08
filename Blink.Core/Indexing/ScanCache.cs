using System.Text;

namespace Blink.Core.Indexing;

/// <summary>
/// A disk-backed list of scanned file paths, written once and re-read across the
/// indexer's passes. At target scale a single root can hold millions of files; holding
/// every path in memory to plan bundles would cost hundreds of MB, so the scan is spilled
/// to a temp file and streamed back instead. One path per line (UTF-8). Disposing deletes
/// the backing file.
/// </summary>
public sealed class ScanCache : IDisposable
{
    private readonly string _path;
    private StreamWriter? _writer;
    private int _count;

    public ScanCache()
    {
        _path = Path.Combine(Path.GetTempPath(), $"blink-scan-{Guid.NewGuid():N}.lst");
        _writer = new StreamWriter(_path, append: false, new UTF8Encoding(false));
    }

    /// <summary>Number of paths appended so far.</summary>
    public int Count => _count;

    /// <summary>Append one path to the scan. Invalid after <see cref="Seal"/>.</summary>
    public void Append(string filePath)
    {
        if (_writer is null)
            throw new InvalidOperationException("ScanCache is sealed; cannot append.");
        _writer.WriteLine(filePath);
        _count++;
    }

    /// <summary>Flush and close for writing so the scan can be re-read.</summary>
    public void Seal()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    /// <summary>Stream every appended path. Call <see cref="Seal"/> first.</summary>
    public IEnumerable<string> ReadAll()
    {
        if (_writer is not null)
            throw new InvalidOperationException("ScanCache must be sealed before reading.");

        using var reader = new StreamReader(_path);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0)
                yield return line;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best effort */ }
    }
}
