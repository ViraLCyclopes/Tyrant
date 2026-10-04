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
    public void The_release_build_gets_the_public_key_in_its_config()
    {
        // Tauri's CLI checks the signature against plugins > updater > pubkey, which tauri.conf.json leaves empty.
        var yml = Read(".github", "workflows", "release.yml");
        Assert.Contains("tauri.ci.conf.json", yml);
        Assert.Contains("pubkey", yml);
        Assert.Contains("--config src-tauri/tauri.ci.conf.json", yml);
    }

    [Fact]
    public void A_manual_run_names_its_tag_and_drafts_carry_no_placeholder_notes()
    {
        var yml = Read(".github", "workflows", "release.yml");
        Assert.Contains("inputs.tag", yml);
        Assert.DoesNotContain("tagName: ${{ github.ref_name }}", yml);
        Assert.DoesNotContain("Write what changed here", yml); // latest.json takes its notes from the draft's body
    }

    [Fact]
    public void Only_the_release_config_makes_updater_artifacts()
    {
        Assert.Contains("\"createUpdaterArtifacts\": true", Read("studio", "src-tauri", "tauri.release.conf.json"));
        Assert.DoesNotContain("createUpdaterArtifacts", Read("studio", "src-tauri", "tauri.bundle.conf.json"));
        Assert.DoesNotContain("createUpdaterArtifacts", Read("studio", "src-tauri", "tauri.conf.json"));
    }
}
