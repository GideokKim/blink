using System.Net;
using Blink.Core.Update;

namespace Blink.Core.Tests;

public sealed class UpdateCheckerTests
{
    /// <summary>요청을 가로채 미리 정한 응답을 돌려주는 가짜 핸들러.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? LastRequest;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private static StubHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    private const string FullRelease = """
        {
          "tag_name": "v1.2.3",
          "body": "## What's Changed\n* fix: something",
          "assets": [
            { "name": "Blink-Setup-1.2.3.exe",
              "browser_download_url": "https://github.com/GideokKim/blink/releases/download/v1.2.3/Blink-Setup-1.2.3.exe" },
            { "name": "checksums.txt", "browser_download_url": "https://example.com/x" }
          ]
        }
        """;

    [Fact]
    public async Task FetchLatest_ParsesVersionBodyAndInstallerAsset()
    {
        using var checker = new UpdateChecker(Json(FullRelease));
        var r = await checker.FetchLatestAsync();

        Assert.NotNull(r);
        Assert.Equal("v1.2.3", r!.TagName);
        Assert.Equal("1.2.3", r.Version.ToString());
        Assert.StartsWith("## What's Changed", r.Body);
        Assert.Equal("Blink-Setup-1.2.3.exe", r.InstallerName);
        Assert.Equal(
            "https://github.com/GideokKim/blink/releases/download/v1.2.3/Blink-Setup-1.2.3.exe",
            r.InstallerUrl);
    }

    [Fact]
    public async Task FetchLatest_PrefersCdnManifest_WhenAvailable()
    {
        const string manifest = """
            {
              "version": "2.0.0",
              "tag": "v2.0.0",
              "notes": "## Manifest notes\n* faster",
              "installerName": "Blink-Setup-2.0.0.exe",
              "installerUrl": "https://github.com/GideokKim/blink/releases/download/v2.0.0/Blink-Setup-2.0.0.exe"
            }
            """;
        // CDN host serves the manifest; the REST API errors — so a parsed result proves
        // the manifest path was used (and the API was not relied upon).
        var stub = new StubHandler(req => req.RequestUri!.Host == "github.com"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) }
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var checker = new UpdateChecker(stub);
        var r = await checker.FetchLatestAsync();

        Assert.NotNull(r);
        Assert.Equal("v2.0.0", r!.TagName);
        Assert.Equal("2.0.0", r.Version.ToString());
        Assert.StartsWith("## Manifest notes", r.Body);
        Assert.Equal("Blink-Setup-2.0.0.exe", r.InstallerName);
    }

    [Fact]
    public async Task FetchLatest_FallsBackToApi_WhenManifestMissing()
    {
        // CDN manifest 404s (e.g. a release cut before the manifest existed) → REST API used.
        var stub = new StubHandler(req => req.RequestUri!.Host == "github.com"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FullRelease) });
        using var checker = new UpdateChecker(stub);
        var r = await checker.FetchLatestAsync();

        Assert.NotNull(r);
        Assert.Equal("v1.2.3", r!.TagName);
        Assert.Equal("Blink-Setup-1.2.3.exe", r.InstallerName);
    }

    [Fact]
    public async Task FetchLatest_CallsLatestEndpoint_WithUserAgent()
    {
        var stub = Json(FullRelease);
        using var checker = new UpdateChecker(stub);
        await checker.FetchLatestAsync();

        Assert.Equal(
            "https://api.github.com/repos/GideokKim/blink/releases/latest",
            stub.LastRequest!.RequestUri!.ToString());
        // GitHub API는 User-Agent 없는 요청을 403으로 거부한다.
        Assert.NotEmpty(stub.LastRequest.Headers.UserAgent);
    }

    [Fact]
    public async Task FetchByTag_CallsTagEndpoint()
    {
        var stub = Json(FullRelease);
        using var checker = new UpdateChecker(stub);
        var r = await checker.FetchByTagAsync("v1.2.3");

        Assert.NotNull(r);
        Assert.Equal(
            "https://api.github.com/repos/GideokKim/blink/releases/tags/v1.2.3",
            stub.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task FetchLatest_NoInstallerAsset_ReturnsReleaseWithoutUrl()
    {
        using var checker = new UpdateChecker(Json(
            """{ "tag_name": "v1.2.3", "body": "notes", "assets": [] }"""));
        var r = await checker.FetchLatestAsync();

        Assert.NotNull(r);                 // What's New 용도로는 자산 없이도 유효
        Assert.Null(r!.InstallerUrl);
        Assert.Null(r.InstallerName);
    }

    [Fact]
    public async Task FetchLatest_NullBody_BecomesEmpty()
    {
        using var checker = new UpdateChecker(Json(
            """{ "tag_name": "v1.2.3", "body": null, "assets": [] }"""));
        var r = await checker.FetchLatestAsync();
        Assert.Equal("", r!.Body);
    }

    [Theory]
    [InlineData("{ not valid json !!")]                          // JSON 깨짐
    [InlineData("""{ "body": "no tag" }""")]                     // tag_name 누락
    [InlineData("""{ "tag_name": "not-a-version" }""")]          // 태그 파싱 실패
    public async Task FetchLatest_BadPayload_ReturnsNull(string body)
    {
        using var checker = new UpdateChecker(Json(body));
        Assert.Null(await checker.FetchLatestAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]       // rate limit
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FetchLatest_HttpError_ReturnsNull(HttpStatusCode status)
    {
        using var checker = new UpdateChecker(Json("{}", status));
        Assert.Null(await checker.FetchLatestAsync());
    }

    [Fact]
    public async Task FetchLatest_NetworkException_ReturnsNull()
    {
        using var checker = new UpdateChecker(
            new StubHandler(_ => throw new HttpRequestException("offline")));
        Assert.Null(await checker.FetchLatestAsync());
    }
}
