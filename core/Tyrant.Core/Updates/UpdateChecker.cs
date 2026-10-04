using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Updates;

/// <summary>Where Tyrant's releases live: GitHub installs, Nexus only notifies.</summary>
public static class UpdateSources
{
    public const string GitHubRepo = "ViraLCyclopes/Tyrant";
    public const string ReleasesPage = "https://github.com/" + GitHubRepo + "/releases";
    public const int NexusGameId = 2097;
    public const string NexusGameDomain = "prehistorickingdom";

    /// <summary>Tyrant's mod id on Nexus (https://www.nexusmods.com/prehistorickingdom/mods/24); null turns the Nexus check off.</summary>
    public static readonly int? NexusModId = 24;

    public static string NexusPage(int modId) => $"https://www.nexusmods.com/{NexusGameDomain}/mods/{modId}";
}

public sealed record GitHubRelease(string Version, string Page, string? SetupUrl);

/// <summary>Asks GitHub for the latest published release and Nexus (public API, no key) for the version on Tyrant's page.</summary>
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

    public async Task<string?> NexusVersionAsync(int? modId, CancellationToken ct)
    {
        if (modId is null) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = $"{{ mod(gameId: {UpdateSources.NexusGameId}, modId: {modId}) {{ version }} }}" }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Application-Name", "Tyrant");
        request.Headers.Add("Application-Version", currentVersion);
        using var response = await Send(request, "Nexus", ct);
        if (!response.IsSuccessStatusCode) return null;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("mod", out var mod) && mod.ValueKind == JsonValueKind.Object
                && mod.TryGetProperty("version", out var version) ? version.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
