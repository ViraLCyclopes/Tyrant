using Tyrant.Core.Models;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

public class ModModelMethodsTests
{
    private static async Task<(RpcHarness H, string Ws, FakeAssetReader Reader, string Glb)> Setup(FakeGame game)
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        var reader = new FakeAssetReader { PrefabModelToReturn = prefab };
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir(), reader: reader));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        await h.Call("mods.create", new { id = "big-carch", name = "Big Carch" });
        SkinDumps.Write(Path.Combine(ws, "data"));
        AssetFixtures.WriteIndex(ws, [.. SkinDumps.Textures, SkinDumps.Prefab],
            Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)));
        var glb = Path.Combine(ws, "carch.glb");
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], glb, [new GltfMaterial("Carch")]);
        return (h, ws, reader, glb);
    }

    [Fact]
    public async Task Replace_model_from_the_assets_tab_adds_it_to_the_mod()
    {
        using var game = new FakeGame();
        var (h, _, _, glb) = await Setup(game);

        var detail = await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, prefabRef = SkinDumps.Prefab.Ref });

        var model = detail.GetProperty("models")[0];
        Assert.Equal("Carcharodontosaurus", model.GetProperty("target").GetString());
        Assert.Equal(3, model.GetProperty("lods")[0].GetProperty("vertices").GetInt32());
        Assert.Equal(3, model.GetProperty("lods")[0].GetProperty("vanilla").GetInt32());
        Assert.False(model.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task A_skin_model_is_listed_with_its_skin()
    {
        using var game = new FakeGame();
        var (h, ws, _, glb) = await Setup(game);
        var dir = Path.Combine(ws, "mods", "big-carch");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
        File.Copy(glb, Path.Combine(dir, "skins", "spiked", "male_D.png")); // any file: the slot only has to exist
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"Carcharodontosaurus\",\"name\":\"Spiked\",\"base\":\"1\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");

        var detail = await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, skin = "spiked" });

        var model = detail.GetProperty("models")[0];
        Assert.Equal("spiked", model.GetProperty("skin").GetString());
        Assert.StartsWith("models/carcharodontosaurus-spiked-", detail.GetProperty("skins")[0].GetProperty("model").GetString());
    }

    [Fact]
    public async Task Remove_and_rebuild_take_the_revision()
    {
        using var game = new FakeGame();
        var (h, _, _, glb) = await Setup(game);
        var added = await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });

        var rebuilt = await h.Call("mods.rebuildModels", new { id = "big-carch", revision = added.GetProperty("revision").GetString() });
        var removed = await h.Call("mods.removeModel", new { id = "big-carch", revision = rebuilt.GetProperty("revision").GetString(), target = "Carcharodontosaurus" });

        Assert.Equal(0, removed.GetProperty("models").GetArrayLength());
    }

    [Fact]
    public async Task A_model_with_errors_is_a_clear_rpc_error()
    {
        using var game = new FakeGame();
        var (h, _, reader, glb) = await Setup(game);
        reader.PrefabModelToReturn = reader.PrefabModelToReturn! with
        { Renderers = [reader.PrefabModelToReturn.Renderers[0] with { Materials = [new MaterialModel("Stego", []), new MaterialModel("Eyes", [])] }] };

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Contains("Carch", ex.Message);
    }

    [Fact]
    public async Task The_skins_3D_view_shows_the_skins_own_model()
    {
        using var game = new FakeGame();
        var (h, ws, reader, glb) = await Setup(game);
        reader.ModelMaterials = [new Tyrant.Core.Models.ResolvedMaterial("Carch", null, null, null, []) { Animal = true }, new Tyrant.Core.Models.ResolvedMaterial("Eyes", null, null, null, [])];
        var dir = Path.Combine(ws, "mods", "big-carch");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
        File.Copy(glb, Path.Combine(dir, "skins", "spiked", "male_D.png"));
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"Carcharodontosaurus\",\"name\":\"Spiked\",\"base\":\"1\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");
        var before = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male" });
        Assert.Equal(System.Text.Json.JsonValueKind.Null, before.GetProperty("ownModel").ValueKind); // the game's model

        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, skin = "spiked" });
        var after = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male" });

        var own = after.GetProperty("ownModel");
        Assert.StartsWith(Path.Combine(ws, "cache", "previews") + Path.DirectorySeparatorChar, own.GetProperty("file").GetString());
        var materials = own.GetProperty("materials").EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("skinnable").GetBoolean());
        Assert.True(materials["Carch"]);  // the animal material wears the skin's maps
        Assert.False(materials["Eyes"]);
    }

    [Fact]
    public async Task Model_preview_writes_a_glb_per_lod_in_the_preview_cache()
    {
        using var game = new FakeGame();
        var (h, ws, _, glb) = await Setup(game);
        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });

        var preview = await h.Call("mods.modelPreview", new { id = "big-carch", target = "Carcharodontosaurus" });

        Assert.Equal(SkinDumps.Prefab.Ref, preview.GetProperty("prefabRef").GetString());
        var file = preview.GetProperty("lods")[0].GetProperty("file").GetString()!;
        Assert.StartsWith(Path.Combine(ws, "cache", "previews") + Path.DirectorySeparatorChar, file);
        Assert.Equal(3, preview.GetProperty("lods")[0].GetProperty("vertices").GetInt32());
    }

    [Fact]
    public async Task A_changed_source_file_is_flagged_with_its_path()
    {
        using var game = new FakeGame();
        var (h, _, _, glb) = await Setup(game);
        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });
        File.AppendAllText(glb, " ");
        File.SetLastWriteTimeUtc(glb, DateTime.UtcNow.AddMinutes(5));

        var model = (await h.Call("mods.get", new { id = "big-carch" })).GetProperty("models")[0];

        Assert.Equal(Path.GetFullPath(glb), model.GetProperty("origin").GetString());
        Assert.True(model.GetProperty("originChanged").GetBoolean());
    }

    [Fact]
    public async Task The_model_preview_is_made_once_per_build()
    {
        using var game = new FakeGame();
        var (h, _, reader, glb) = await Setup(game);
        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });

        var first = await h.Call("mods.modelPreview", new { id = "big-carch", target = "Carcharodontosaurus" });
        var again = await h.Call("mods.modelPreview", new { id = "big-carch", target = "Carcharodontosaurus" });

        Assert.Equal(1, reader.ReplacedModelWrites);
        Assert.Equal(first.GetProperty("lods")[0].GetProperty("file").GetString(), again.GetProperty("lods")[0].GetProperty("file").GetString());
    }

    [Fact]
    public async Task Rebuilding_the_lods_makes_a_new_preview()
    {
        using var game = new FakeGame();
        var (h, _, reader, glb) = await Setup(game);
        var detail = await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });
        await h.Call("mods.modelPreview", new { id = "big-carch", target = "Carcharodontosaurus" });

        await h.Call("mods.rebuildModels", new { id = "big-carch", revision = detail.GetProperty("revision").GetString() });
        await h.Call("mods.modelPreview", new { id = "big-carch", target = "Carcharodontosaurus" });

        Assert.Equal(2, reader.ReplacedModelWrites); // the same .glb, but new LOD files: the old preview would be stale
    }

    [Fact]
    public async Task Check_rebuilds_a_changed_model_first()
    {
        using var game = new FakeGame();
        var (h, ws, _, glb) = await Setup(game);
        var detail = await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });
        var copy = Path.Combine(ws, "mods", "big-carch", detail.GetProperty("models")[0].GetProperty("file").GetString()!);
        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(5));

        var check = await h.Call("mods.check", new { id = "big-carch" });

        Assert.DoesNotContain(check.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("changed since it was built"));
    }

    [Fact]
    public async Task Without_a_data_dump_replace_model_says_how_to_get_one()
    {
        using var game = new FakeGame();
        var (h, ws, _, glb) = await Setup(game);
        Directory.Delete(Path.Combine(ws, "data"), recursive: true);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" }));

        Assert.Contains("Run data dump", ex.Message);
    }

    [Fact]
    public async Task An_fbx_without_blender_is_a_clear_rpc_error()
    {
        using var game = new FakeGame();
        var (h, _, _, glb) = await Setup(game);
        var fbx = Path.ChangeExtension(glb, ".fbx");
        File.WriteAllText(fbx, "fbx");

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.replaceModel", new { id = "big-carch", file = fbx, target = "Carcharodontosaurus" }));

        Assert.Equal("BLENDER_MISSING", ex.DataCode);
        Assert.Contains("FBX needs Blender", ex.Message);
    }
}
