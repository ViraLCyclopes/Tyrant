using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Updates;

/// <summary>Where Tyrant's releases live: GitHub.</summary>
public static class UpdateSources
{
    public const string GitHubRepo = "ViraLCyclopes/Tyrant";
    public const string ReleasesPage = "https://github.com/" + GitHubRepo + "/releases";
}

public sealed record GitHubRelease(string Version, string Page, string? SetupUrl);

/// <summary>Asks GitHub for the latest published release.</summary>
public sealed class UpdateChecker(HttpClient http, string currentVersion)
{
    public static string CurrentVersion
    {
        get
        {
            var v = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }

    public async Task<GitHubRelease?> LatestGitHubReleaseAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{UpdateSources.GitHubRepo}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Tyrant", currentVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Send(request, "GitHub", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // no release published yet
        if (!response.IsSuccessStatusCode)
            throw new TyrantException(TyrantErrorCode.UpdateCheckFailed, $"GitHub did not answer the update check (HTTP {(int)response.StatusCode}); try again later.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        var setup = root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray().Select(a => a.GetProperty("browser_download_url").GetString()).FirstOrDefault(u => u?.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) == true)
            : null;
        return new GitHubRelease(SemVer.Normalize(root.GetProperty("tag_name").GetString() ?? ""), root.GetProperty("html_url").GetString() ?? UpdateSources.ReleasesPage, setup);
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, string site, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TyrantException(TyrantErrorCode.UpdateCheckFailed, $"Could not reach {site} to check for updates ({ex.Message}). Check your internet connection.");
        }
    }
}
