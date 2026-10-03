using PK.Core.Assets;
using PK.Core.Errors;
using PK.Core.Install;
using PK.Core.Models;
using PK.Core.Species;
using PK.Core.Workspaces;

namespace PK.Core.Tests;

public class SpeciesPackTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));

    private static SpeciesEntry Stego() => new("stegosaurusstenops", "Stegosaurus Stenops", false,
        new AssetRecord("StandaloneWindows64/stego_assets_assets/p.bundle", 42, "GameObject", "Stego",
            "Assets/Prefabs/Animals/V2-MainPrefabs/Stegosaurus Stenops.V2.prefab", "09eb8efa8e623a043864dffadc868eb7", null),
        "StandaloneWindows64/stego_assets_assets");

    [Fact]
    public void Targets_list_prefab_ids_models_full_skeleton_and_textures()
    {
        var dir = TempDir();
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle("Stego_LOD00"));
        var models = new ModelExporter().WriteModels(prefab, Path.Combine(dir, "models"));
        var texture = new AssetRecord("StandaloneWindows64/stego_assets_assets/t.bundle", 7, "Texture2D", "T_stego_D",
            "Assets/Art/T_stego_D.png", "19b2ba2040871f64cac8f8c0c66ca525", null);
        TextureExportResult[] textures =
        [
            new(texture, true, Path.Combine(dir, "textures", "Art", "T_stego_D.png"), null),
            new(texture with { PathId = 8 }, false, Path.Combine(dir, "textures", "x.png"), "boom"),
        ];

        var targets = SpeciesTargets.Build(Stego(), prefab, models, textures, dir, "ce7261bf");
        var path = Path.Combine(dir, SpeciesTargets.FileName);
        SpeciesTargets.Save(targets, path);
        var loaded = SpeciesTargets.Load(path);

        Assert.Equal("09eb8efa8e623a043864dffadc868eb7", loaded.Prefab.Guid);
        Assert.Equal("ce7261bf", loaded.GameBuild);
        var model = Assert.Single(loaded.Models);
        Assert.Equal("models/Stego_LOD00.glb", model.File);
        Assert.Equal(new[] { "Infant" }, model.BlendShapes);
        Assert.Equal(new[] { "Hip", "Tail" }, model.Bones);
        Assert.Equal(new[] { "Animal", "Animal/Hip", "Animal/Hip/Tail" }, loaded.Skeleton.Select(b => b.Path));
        Assert.Equal("Animal/Hip", loaded.Skeleton[2].Parent);
        var tex = Assert.Single(loaded.Textures);
        Assert.Equal("textures/Art/T_stego_D.png", tex.File);
        Assert.Equal("19b2ba2040871f64cac8f8c0c66ca525", tex.Guid);
    }

    [Fact]
    public void Failed_repack_keeps_the_previous_pack()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(TempDir(), install);
        var stale = Path.Combine(ws.AssetsDir, "species", "stegosaurusstenops", "models", "old.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old");

        var ex = Assert.Throws<PkException>(() =>
            new SpeciesPackExporter().Export(install, ws, new AssetIndex(), Stego(), null, CancellationToken.None));

        Assert.Equal(PkErrorCode.AssetNotFound, ex.Code);
        Assert.True(File.Exists(stale));
    }
}
