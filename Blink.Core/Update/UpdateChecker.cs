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

    public UpdateChecker(HttpMessageHandler? handler = null, string repo = "GideokKim/blink")
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri($"https://api.github.com/repos/{repo}/");
        _http.Timeout = TimeSpan.FromSeconds(15);
        // GitHub API rejects requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Blink-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Latest stable release (the API excludes pre-releases), or null on any failure.</summary>
    public Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct = default) =>
        FetchAsync("releases/latest", ct);

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
