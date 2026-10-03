using PK.Core.Assets;
using PK.Core.Errors;
using PK.Core.Install;
using PK.Core.Models;
using PK.Core.Workspaces;

namespace PK.Core.Tests;

public class ModelExporterTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteModels_writes_one_glb_per_renderer_and_dedupes_names()
    {
        var a = ModelFixture.Prefab(ModelFixture.Triangle("Body_LOD00"));
        var twin = a.Renderers[0] with { Name = "Twin" };
        var prefab = a with { Renderers = [a.Renderers[0], twin], Failures = ["Dung: streamed"] };
        var dir = TempDir();

        var results = new ModelExporter().WriteModels(prefab, dir);

        Assert.True(File.Exists(Path.Combine(dir, "Body_LOD00.glb")));
        Assert.True(File.Exists(Path.Combine(dir, "Body_LOD00_2.glb")));
        Assert.Equal(2, results.Count(r => r.Success));
        var failed = Assert.Single(results, r => !r.Success);
        Assert.Contains("streamed", failed.Error);
    }

    [Fact]
    public void WriteModels_reports_a_renderer_that_cannot_be_written()
    {
        var bad = ModelFixture.Triangle("Broken");
        bad.Skin[0] = new BoneWeight4(9, 0, 0, 0, 1, 0, 0, 0);
        var good = ModelFixture.Prefab(ModelFixture.Triangle("Fine"));
        var prefab = good with { Renderers = [good.Renderers[0], good.Renderers[0] with { Mesh = bad }] };

        var results = new ModelExporter().WriteModels(prefab, TempDir());

        Assert.True(results.Single(r => r.Name == "Fine").Success);
        Assert.False(results.Single(r => r.Name == "Broken").Success);
    }

    [Fact]
    public void ReadPrefab_rejects_records_that_are_not_game_objects()
    {
        using var game = new FakeGame();
        using var session = new AssetSession(new GameInstall(game.Root, null));
        var texture = new AssetRecord("StandaloneWindows64/x.bundle", 1, "Texture2D", "T", null, null, null);

        Assert.Equal(PkErrorCode.AssetNotFound, Assert.Throws<PkException>(() => new ModelExporter().ReadPrefab(session, texture)).Code);
    }

    [Fact]
    public void Export_of_missing_bundle_reports_not_found()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(TempDir(), install);
        var prefab = new AssetRecord("StandaloneWindows64/gone.bundle", 1, "GameObject", "Stego", "Assets/Prefabs/Animals/V2-MainPrefabs/Stego.V2.prefab", null, null);

        Assert.Equal(PkErrorCode.AssetNotFound, Assert.Throws<PkException>(() => new ModelExporter().Export(install, ws, prefab)).Code);
    }
}
