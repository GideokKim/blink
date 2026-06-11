namespace Blink.Core.Update;

/// <summary>One GitHub release as the updater sees it.</summary>
public sealed class ReleaseInfo
{
    public required SemVer Version { get; init; }

    /// <summary>Original tag (e.g. "v1.2.3").</summary>
    public required string TagName { get; init; }

    /// <summary>Release notes body (GitHub-flavored markdown; empty when absent).</summary>
    public string Body { get; init; } = "";

    /// <summary>Direct download URL of the Blink-Setup-*.exe asset; null when the release has none.</summary>
    public string? InstallerUrl { get; init; }

    public string? InstallerName { get; init; }
}
