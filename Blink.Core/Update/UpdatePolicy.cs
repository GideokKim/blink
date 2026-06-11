namespace Blink.Core.Update;

/// <summary>
/// Pure decision logic for the updater, kept in Core so it is unit-testable.
/// The App layer only moves these verdicts onto the screen.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>
    /// Manual-check verdict: the release is strictly newer than the running version AND
    /// carries an installer asset. Ignores skip_version — "지금 확인" always answers.
    /// </summary>
    public static bool IsNewer(ReleaseInfo? release, string currentVersion)
    {
        if (release?.InstallerUrl is null) return false;
        if (!SemVer.TryParse(currentVersion, out var current)) return false;
        return release.Version.CompareTo(current) > 0;
    }

    /// <summary>
    /// Automatic-check verdict: newer + installable, and not the version the user chose
    /// to skip. A release newer than the skipped one notifies again.
    /// </summary>
    public static bool ShouldOffer(ReleaseInfo? release, string currentVersion, string skipVersion)
    {
        if (!IsNewer(release, currentVersion)) return false;
        return !(SemVer.TryParse(skipVersion, out var skip) && release!.Version.CompareTo(skip) == 0);
    }
}

/// <summary>What the app should do about the "새로워진 점" window on this launch.</summary>
public enum WhatsNewAction
{
    /// <summary>Nothing to show (same/lower version, or current version unreadable).</summary>
    None,

    /// <summary>No prior record — set last_seen_version to current without showing a window.</summary>
    InitializeOnly,

    /// <summary>Current version is newer than last seen — fetch notes and show the window.</summary>
    Show,
}

/// <summary>Decides the What's New action from last_seen_version vs the running version.</summary>
public static class WhatsNewGate
{
    public static WhatsNewAction Decide(string lastSeenVersion, string currentVersion)
    {
        if (!SemVer.TryParse(currentVersion, out var current)) return WhatsNewAction.None;
        if (!SemVer.TryParse(lastSeenVersion, out var lastSeen)) return WhatsNewAction.InitializeOnly;
        return current!.CompareTo(lastSeen) > 0 ? WhatsNewAction.Show : WhatsNewAction.None;
    }
}
