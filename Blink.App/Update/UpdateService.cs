// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
//
// UNVERIFIED (authored on macOS, not compiled): Velopack-backed updater. Every line marked
// `// VP?` touches the Velopack API and MUST be confirmed against the installed package on
// Windows (signatures drift across Velopack versions). Behaviour contract preserved from the
// Inno version: automatic-check failures are silent (Trace only) and never disturb the app.
using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using Blink.Core.Config;
using Blink.Core.Update;
using Velopack;            // VP?
using Velopack.Sources;    // VP? (GithubSource)

namespace Blink.App.Update;

/// <summary>
/// Update orchestration for the tray app, backed by Velopack. Checks 30 s after startup and
/// every 24 h after; download + apply are delegated to Velopack (delta, background staging,
/// 1-click restart). Replaces the old Inno "download installer to %TEMP% and silent-run" flow.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    // Pinned release feed. VP? confirm GithubSource ctor (repoUrl, accessToken, prerelease).
    private const string RepoUrl = "https://github.com/GideokKim/blink";

    private readonly AppConfig _config;
    private readonly DispatcherTimer _timer = new();
    private bool _firstTick = true;

    // Shared manager + the most recent check result, so DownloadAndApplyAsync can act on it.
    private static readonly UpdateManager Manager =
        new(new GithubSource(RepoUrl, null, prerelease: false)); // VP?
    private static UpdateInfo? _pending;                          // VP?

    /// <summary>Raised on the UI thread when an automatic check finds an offerable release.</summary>
    public event Action<ReleaseInfo>? UpdateAvailable;

    public UpdateService(AppConfig config) => _config = config;

    /// <summary>
    /// Running version. Velopack's installed version is authoritative; fall back to the assembly's
    /// informational version for dev runs / the legacy (non-Velopack) layout so What's New keeps working.
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            try
            {
                var v = Manager.CurrentVersion; // VP? SemanticVersion? (null when not Velopack-installed)
                if (v is not null) return v.ToString();
            }
            catch { /* not installed via Velopack — fall through to assembly version */ }

            var info = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0";
            int plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }

    /// <summary>Arm the 30 s + 24 h check cadence. update_check=false skips the call, not the
    /// timer, so re-enabling in Settings takes effect on the next tick without re-arming.</summary>
    public void Start()
    {
        _timer.Interval = InitialDelay;
        _timer.Tick += async (_, _) =>
        {
            if (_firstTick)
            {
                _firstTick = false;
                _timer.Interval = CheckInterval;
            }
            if (!_config.UpdateCheck) return;

            var release = await CheckAsync();
            // Velopack already guarantees "strictly newer"; we only enforce the user's skip choice.
            if (release is not null &&
                !string.Equals(release.Version.ToString(), _config.SkipVersion, StringComparison.OrdinalIgnoreCase))
                UpdateAvailable?.Invoke(release);
        };
        _timer.Start();
    }

    /// <summary>
    /// Velopack check. Returns a ReleaseInfo (adapter for the existing UI) when a newer release
    /// exists, else null. Notes are fetched by tag from GitHub so the update card stays populated.
    /// Any failure (offline, not Velopack-installed) returns null — silent, retry next cycle.
    /// </summary>
    public static async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            _pending = await Manager.CheckForUpdatesAsync(); // VP? UpdateInfo?
            if (_pending is null) return null;

            var version = _pending.TargetFullRelease.Version; // VP? NuGet/SemanticVersion
            var tag = $"v{version}";
            if (!SemVer.TryParse(tag, out var sem) || sem is null) return null;

            // Reuse the GitHub release-notes fetch for the body (Velopack carries no markdown notes).
            var notes = await FetchByTagAsync(tag);

            return new ReleaseInfo
            {
                Version = sem,
                TagName = tag,
                Body = notes?.Body ?? "",
                // Sentinel so UpdatePolicy.IsNewer's null-check passes; Velopack owns the actual asset.
                InstallerUrl = "velopack",
                InstallerName = null,
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Blink] Velopack check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Latest stable release, or null. Kept for the manual "지금 확인" button.</summary>
    public static Task<ReleaseInfo?> FetchLatestAsync() => CheckAsync();

    /// <summary>Release notes for a specific tag (What's New), or null on any failure.
    /// Still served straight from the GitHub Releases API — Velopack does not expose markdown notes.</summary>
    public static async Task<ReleaseInfo?> FetchByTagAsync(string tag)
    {
        using var checker = new UpdateChecker();
        return await checker.FetchByTagAsync(tag);
    }

    /// <summary>
    /// Download the pending update (delta if available) reporting 0–100 progress, then apply it
    /// and restart into the new version. Replaces the old download-to-%TEMP% + silent-Inno flow;
    /// the process exits inside ApplyUpdatesAndRestart, so control does not return on success.
    /// </summary>
    public static async Task DownloadAndApplyAsync(
        ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
    {
        if (_pending is null)
            throw new InvalidOperationException("no pending Velopack update (call CheckAsync first)");

        await Manager.DownloadUpdatesAsync(_pending, p => progress.Report(p), cancelToken: ct); // VP?
        ct.ThrowIfCancellationRequested();
        Manager.ApplyUpdatesAndRestart(_pending); // VP? exits the process
    }

    /// <summary>No-op under Velopack (it manages its own staging/cleanup); kept for call-site
    /// compatibility with App.OnStartup until that call is removed in the cleanup phase.</summary>
    public static void CleanupTempInstallers() { /* Velopack self-manages */ }

    public void Dispose() => _timer.Stop();
}
