namespace Tyrant.Core.Tests;

public class ReleaseWorkflowTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tyrant.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    [Fact]
    public void The_release_workflow_builds_signs_and_drafts_from_a_version_tag()
    {
        var yml = Read(".github", "workflows", "release.yml");
        Assert.Contains("tags: ['v*']", yml);
        Assert.Contains("TAURI_SIGNING_PRIVATE_KEY: ${{ secrets.TAURI_SIGNING_PRIVATE_KEY }}", yml);
        Assert.Contains("TAURI_SIGNING_PRIVATE_KEY_PASSWORD: ${{ secrets.TAURI_SIGNING_PRIVATE_KEY_PASSWORD }}", yml);
        Assert.Contains("tauri.release.conf.json", yml);
        Assert.Contains("releaseDraft: true", yml);
        Assert.Contains("game package-framework", yml);
        Assert.Contains("updater-key.pub", yml); // refuses to build a release without the public key
        Assert.DoesNotContain("-p:Version", yml);
    }

    [Fact]
    public void Only_the_release_config_makes_updater_artifacts()
    {
        Assert.Contains("\"createUpdaterArtifacts\": true", Read("studio", "src-tauri", "tauri.release.conf.json"));
        Assert.DoesNotContain("createUpdaterArtifacts", Read("studio", "src-tauri", "tauri.bundle.conf.json"));
        Assert.DoesNotContain("createUpdaterArtifacts", Read("studio", "src-tauri", "tauri.conf.json"));
    }
}
