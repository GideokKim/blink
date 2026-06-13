using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blink.Core.Update;

/// <summary>
/// Unauthenticated GitHub Releases API client. Every failure mode (network, HTTP error,
/// rate limit, broken JSON, unparsable tag) returns null — the updater must never
/// disturb the app (silent + retry next cycle).
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _repo;

    public UpdateChecker(HttpMessageHandler? handler = null, string repo = "GideokKim/blink")
    {
        _repo = repo;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri($"https://api.github.com/repos/{repo}/");
        _http.Timeout = TimeSpan.FromSeconds(15);
        // GitHub API rejects requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Blink-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Latest stable release, or null on any failure. Prefers the CDN-served
    /// <c>latest.json</c> manifest (served by github.com release downloads, which are NOT
    /// subject to the 60-request/hour unauthenticated REST API limit); falls back to the
    /// REST API when the manifest is missing/unreadable (e.g. releases cut before it existed).
    /// </summary>
    public async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct = default) =>
        await FetchManifestAsync(ct).ConfigureAwait(false)
        ?? await FetchAsync("releases/latest", ct).ConfigureAwait(false);

    /// <summary>
    /// Read the latest-release manifest from the stable CDN URL
    /// (<c>/releases/latest/download/latest.json</c>, which always redirects to the latest
    /// stable release's asset). Null on any failure so the caller falls back to the API.
    /// </summary>
    private async Task<ReleaseInfo?> FetchManifestAsync(CancellationToken ct)
    {
        try
        {
            var url = $"https://github.com/{_repo}/releases/latest/download/latest.json";
            using var rsp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!rsp.IsSuccessStatusCode) return null;

            var json = await rsp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<ManifestDto>(json);
            var tag = dto?.Tag ?? (dto?.Version is { } v ? $"v{v}" : null);
            if (tag is null || !SemVer.TryParse(tag, out var version)) return null;

            return new ReleaseInfo
            {
                Version = version!,
                TagName = tag,
                Body = dto!.Notes ?? "",
                InstallerUrl = dto.InstallerUrl,
                InstallerName = dto.InstallerName,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Release for a specific tag (e.g. "v1.2.3"), or null on any failure.</summary>
    public Task<ReleaseInfo?> FetchByTagAsync(string tag, CancellationToken ct = default) =>
        FetchAsync($"releases/tags/{tag}", ct);

    private async Task<ReleaseInfo?> FetchAsync(string path, CancellationToken ct)
    {
        try
        {
            using var rsp = await _http.GetAsync(path, ct).ConfigureAwait(false);
            if (!rsp.IsSuccessStatusCode) return null;

            var json = await rsp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<ReleaseDto>(json);
            if (dto?.TagName is null) return null;
            if (!SemVer.TryParse(dto.TagName, out var version)) return null;

            var asset = dto.Assets?.FirstOrDefault(a =>
                a.Name is not null &&
                a.Name.StartsWith("Blink-Setup-", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            return new ReleaseInfo
            {
                Version = version!,
                TagName = dto.TagName,
                Body = dto.Body ?? "",
                InstallerUrl = asset?.BrowserDownloadUrl,
                InstallerName = asset?.Name,
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Shape of the CDN <c>latest.json</c> manifest published by the release workflow.</summary>
    private sealed class ManifestDto
    {
        [JsonPropertyName("tag")] public string? Tag { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("installerName")] public string? InstallerName { get; set; }
        [JsonPropertyName("installerUrl")] public string? InstallerUrl { get; set; }
    }

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
