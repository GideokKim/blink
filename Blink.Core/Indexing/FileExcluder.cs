using System.Text;
using System.Text.RegularExpressions;

namespace Blink.Core.Indexing;

/// <summary>
/// Decides which files the indexer skips. Mirrors the prototype's <c>excludes.py</c>:
/// junk like Office lock files (<c>~$*.xlsx</c>), temp files, and OS metadata never
/// reach the index. Patterns use a small gitignore-style glob subset:
/// <list type="bullet">
///   <item><c>*</c> matches any run of non-separator chars; <c>?</c> matches one.</item>
///   <item>A pattern with no <c>/</c> matches against the file NAME only (e.g. <c>~$*</c>).</item>
///   <item>A pattern ending in <c>/</c> matches a DIRECTORY name anywhere in the path
///         (e.g. <c>.git/</c> excludes everything under any <c>.git</c> folder).</item>
///   <item>Otherwise the pattern matches against the path RELATIVE to the index root,
///         using <c>/</c> as the separator regardless of OS.</item>
/// </list>
/// Matching is case-insensitive (Windows file systems are). Blank lines and lines
/// starting with <c>#</c> in a <c>.blinkignore</c> file are ignored.
/// </summary>
public sealed class FileExcluder
{
    /// <summary>
    /// Built-in junk patterns, always applied. Covers Office/temp lock files and OS
    /// metadata that the prototype excludes by default.
    /// </summary>
    public static readonly string[] DefaultPatterns =
    {
        "~$*",          // Office lock/owner files: ~$Book.xlsx, ~$doc.docx
        "*.tmp",
        "*.temp",
        "*~",           // editor backup files
        ".~lock.*",     // LibreOffice lock files
        "Thumbs.db",
        "ehthumbs.db",
        "desktop.ini",
        ".DS_Store",
        ".git/",
        ".svn/",
        "node_modules/",
        "$RECYCLE.BIN/",
        "*.crdownload", // partial downloads
        "*.part",
        ".blinkignore", // our own control file
    };

    private const string IgnoreFileName = ".blinkignore";

    private readonly List<Regex> _nameRules = new();   // match file name only
    private readonly List<Regex> _dirRules = new();    // match any directory segment
    private readonly List<Regex> _pathRules = new();   // match root-relative path

    public FileExcluder(IEnumerable<string> patterns)
    {
        foreach (var raw in patterns)
        {
            var pattern = raw.Trim();
            if (pattern.Length == 0 || pattern.StartsWith('#'))
                continue;

            if (pattern.EndsWith('/'))
            {
                var dir = pattern.TrimEnd('/');
                if (dir.Length > 0)
                    _dirRules.Add(GlobToRegex(dir));
            }
            else if (pattern.Contains('/'))
            {
                _pathRules.Add(GlobToRegex(pattern.TrimStart('/')));
            }
            else
            {
                _nameRules.Add(GlobToRegex(pattern));
            }
        }
    }

    /// <summary>Default excluder (built-in patterns only).</summary>
    public static FileExcluder Default() => new(DefaultPatterns);

    /// <summary>
    /// Build an excluder from the built-in defaults plus a <c>.blinkignore</c> file at
    /// <paramref name="root"/>, if present. User patterns are additive to the defaults.
    /// </summary>
    public static FileExcluder ForRoot(string root)
    {
        var patterns = new List<string>(DefaultPatterns);
        var ignorePath = Path.Combine(root, IgnoreFileName);
        if (File.Exists(ignorePath))
        {
            try { patterns.AddRange(File.ReadAllLines(ignorePath)); }
            catch { /* unreadable ignore file → defaults only */ }
        }
        return new FileExcluder(patterns);
    }

    /// <summary>True if the file at <paramref name="fullPath"/> (under <paramref name="root"/>) should be skipped.</summary>
    public bool IsExcluded(string fullPath, string root)
    {
        var name = Path.GetFileName(fullPath);
        foreach (var rule in _nameRules)
            if (rule.IsMatch(name))
                return true;

        // Relative path with forward slashes; split into segments for dir-rule checks.
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Directory rules: any segment except the final (file name) one.
        for (int i = 0; i < segments.Length - 1; i++)
            foreach (var rule in _dirRules)
                if (rule.IsMatch(segments[i]))
                    return true;

        foreach (var rule in _pathRules)
            if (rule.IsMatch(rel))
                return true;

        return false;
    }

    private static Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        foreach (var c in glob)
        {
            switch (c)
            {
                case '*': sb.Append("[^/]*"); break;
                case '?': sb.Append("[^/]"); break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
