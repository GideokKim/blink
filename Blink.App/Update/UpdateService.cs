// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using Blink.Core.Config;
using Blink.Core.Update;

namespace Blink.App.Update;

/// <summary>
/// Update orchestration for the tray app: checks 30 s after startup and every 24 h after,
/// downloads the installer into %TEMP%\Blink, and hands off to a silent Inno Setup run.
/// Automatic-check failures are silent (Trace only) — the updater never disturbs the app.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly AppConfig _config;
    private readonly DispatcherTimer _timer = new();
    private bool _firstTick = true;

    /// <summary>Raised on the UI thread when an automatic check finds an offerable release.</summary>
    public event Action<ReleaseInfo>? UpdateAvailable;

    public UpdateService(AppConfig config) => _config = config;

    /// <summary>
    /// Running version from AssemblyInformationalVersion, build metadata stripped
    /// (the SDK appends "+&lt;sha&gt;"; comparisons ignore it per semver anyway).
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
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

            var release = await FetchLatestAsync();
            if (UpdatePolicy.ShouldOffer(release, CurrentVersion, _config.SkipVersion))
                UpdateAvailable?.Invoke(release!);
        };
        _timer.Start();
    }

    /// <summary>Latest stable release, or null on any failure. Used by auto + "지금 확인".</summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync()
    {
        using var checker = new UpdateChecker();
        return await checker.FetchLatestAsync();
    }

    /// <summary>Release notes for a specific tag (What's New), or null on any failure.</summary>
    public static async Task<ReleaseInfo?> FetchByTagAsync(string tag)
    {
        using var checker = new UpdateChecker();
        return await checker.FetchByTagAsync(tag);
    }

    /// <summary>
    /// Download the installer asset to %TEMP%\Blink, reporting 0–100 progress.
    /// On failure/cancel the partial file is deleted and the exception propagates
    /// (UpdateWindow turns it into a Korean message + retry).
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
    {
        if (release.InstallerUrl is null || release.InstallerName is null)
            throw new InvalidOperationException("release has no installer asset");

        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, release.InstallerName);
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Blink-Updater");
            using var rsp = await http.GetAsync(
                release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            rsp.EnsureSuccessStatusCode();

            long total = rsp.Content.Headers.ContentLength ?? -1;
            await using var src = await rsp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report(100.0 * done / total);
            }
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch { /* partial file may not exist */ }
            throw;
        }
    }

    /// <summary>Launch the silent installer; the caller must then quit so the exe unlocks.</summary>
    public static void LaunchInstaller(string setupPath) =>
        Process.Start(new ProcessStartInfo(setupPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART")
        {
            UseShellExecute = true,
        });

    private static string TempDir => Path.Combine(Path.GetTempPath(), "Blink");

    /// <summary>
    /// Best-effort cleanup of installers left by previous updates. Runs at startup because
    /// the installer can't be deleted right after launch — it is still executing.
    /// </summary>
    public static void CleanupTempInstallers()
    {
        try
        {
            if (!Directory.Exists(TempDir)) return;
            foreach (var f in Directory.EnumerateFiles(TempDir, "Blink-Setup-*.exe"))
            {
                try { File.Delete(f); }
                catch { /* still in use by a running installer — next launch */ }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Blink] temp installer cleanup failed: {ex.Message}");
        }
    }

    public void Dispose() => _timer.Stop();
}
