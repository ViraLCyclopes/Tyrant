using System.Net;
using Tyrant.Core.Errors;
using Tyrant.Core.Updates;

namespace Tyrant.Core.Tests;

public class UpdateCheckerTests
{
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("0.2.0", "0.1.9", 1)]
    [InlineData("v0.2.0", "0.2.0", 0)]
    [InlineData("0.2.0", "0.2.0-beta", 1)]
    [InlineData("0.2.0-beta", "0.1.9", 1)]
    [InlineData("0.10.0", "0.9.0", 1)]
    [InlineData("garbage", "0.0.1", -1)]
    public void Versions_compare_as_semver(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(SemVer.Compare(a, b)));

    [Fact]
    public async Task The_latest_github_release_gives_its_version_page_and_setup()
    {
        var http = new FakeHttp(_ => Json("""
            { "tag_name": "v0.2.0", "html_url": "https://github.com/ViraLCyclopes/Tyrant/releases/tag/v0.2.0",
              "assets": [ { "name": "latest.json", "browser_download_url": "https://x/latest.json" },
                          { "name": "Tyrant_0.2.0_x64-setup.exe", "browser_download_url": "https://x/Tyrant_0.2.0_x64-setup.exe" } ] }
            """));

        var release = await new UpdateChecker(new HttpClient(http), "0.1.0").LatestGitHubReleaseAsync(CancellationToken.None);

        Assert.Equal(new GitHubRelease("0.2.0", "https://github.com/ViraLCyclopes/Tyrant/releases/tag/v0.2.0", "https://x/Tyrant_0.2.0_x64-setup.exe"), release);
        Assert.Equal("https://api.github.com/repos/ViraLCyclopes/Tyrant/releases/latest", http.Requests[0].RequestUri!.ToString());
        Assert.Contains("Tyrant", http.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task No_published_release_is_null()
    {
        var http = new FakeHttp(_ => Json("""{ "message": "Not Found" }""", HttpStatusCode.NotFound));
        Assert.Null(await new UpdateChecker(new HttpClient(http), "0.1.0").LatestGitHubReleaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_rate_limit_or_network_failure_is_an_update_check_error()
    {
        var limited = new FakeHttp(_ => Json("""{ "message": "API rate limit exceeded" }""", HttpStatusCode.Forbidden));
        var offline = new FakeHttp(_ => throw new HttpRequestException("No such host is known."));

        var a = await Assert.ThrowsAsync<TyrantException>(() => new UpdateChecker(new HttpClient(limited), "0.1.0").LatestGitHubReleaseAsync(CancellationToken.None));
        var b = await Assert.ThrowsAsync<TyrantException>(() => new UpdateChecker(new HttpClient(offline), "0.1.0").LatestGitHubReleaseAsync(CancellationToken.None));

        Assert.Equal(TyrantErrorCode.UpdateCheckFailed, a.Code);
        Assert.Equal(TyrantErrorCode.UpdateCheckFailed, b.Code);
    }

    [Fact]
    public async Task Nexus_gives_the_mods_version_without_a_key()
    {
        var http = new FakeHttp(_ => Json("""{ "data": { "mod": { "version": "0.2.0" } } }"""));

        var version = await new UpdateChecker(new HttpClient(http), "0.1.0").NexusVersionAsync(123, CancellationToken.None);

        Assert.Equal("0.2.0", version);
        Assert.Equal("https://api.nexusmods.com/v2/graphql", http.Requests[0].RequestUri!.ToString());
        Assert.Equal("Tyrant", http.Requests[0].Headers.GetValues("Application-Name").Single());
        Assert.False(http.Requests[0].Headers.Contains("apikey"));
    }

    [Fact]
    public async Task Nexus_errors_or_no_mod_id_give_no_version()
    {
        var notFound = new FakeHttp(_ => Json("""{ "errors": [ { "message": "Mod not found" } ], "data": null }"""));
        Assert.Null(await new UpdateChecker(new HttpClient(notFound), "0.1.0").NexusVersionAsync(123, CancellationToken.None));
        var never = new FakeHttp(_ => throw new InvalidOperationException("must not be called"));
        Assert.Null(await new UpdateChecker(new HttpClient(never), "0.1.0").NexusVersionAsync(null, CancellationToken.None));
    }
}
