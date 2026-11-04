using System.Text.Json;
using Tyrant.Core.Models;
using Tyrant.Core.Tests;
using static Tyrant.Core.Tests.RigFixture;

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
    public async Task A_new_species_of_the_mod_can_be_given_its_own_model()
    {
        using var game = new FakeGame();
        var (h, _, _, glb) = await Setup(game);
        await h.Call("mods.addSpecies", new
        {
            id = "big-carch", revision = (await h.Call("mods.get", new { id = "big-carch" })).GetProperty("revision").GetString(),
            speciesId = "anax", template = "Carcharodontosaurus", name = "Anax", genus = "Example", epithet = "anax", startingSkin = 1,
        });

        var custom = await h.Call("mods.customSpecies", new { id = "big-carch" });
        var target = Assert.Single(custom.GetProperty("species").EnumerateArray()).GetProperty("speciesId").GetString();
        Assert.Equal("tyrant.big-carch.anax", target);

        await h.Call("mods.readModelRig", new { id = "big-carch", file = glb, target });
        var detail = await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target, rig = "auto" });

        var model = Assert.Single(detail.GetProperty("models").EnumerateArray());
        Assert.Equal(target, model.GetProperty("target").GetString());
        Assert.Equal(JsonValueKind.Null, model.GetProperty("skin").ValueKind);
        Assert.False(model.GetProperty("stale").GetBoolean());
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

    private static async Task<(RpcHarness H, FakeAssetReader Reader)> SkinWithTwoLods(FakeGame game)
    {
        var (h, ws, reader, glb) = await Setup(game);
        var r0 = reader.PrefabModelToReturn!.Renderers[0];
        var lod1 = r0 with
        {
            Name = "Carch_LOD01",
            Mesh = new MeshData
            {
                Name = "Carch_LOD01", Positions = r0.Mesh.Positions[..2], Normals = r0.Mesh.Normals[..2], Uv0 = r0.Mesh.Uv0[..2], Colors = [],
                Skin = r0.Mesh.Skin[..2], Indices = [], SubMeshes = [new SubMesh(0, 0, 0)], BindPoses = r0.Mesh.BindPoses, BlendShapes = r0.Mesh.BlendShapes,
            },
        };
        reader.PrefabModelToReturn = reader.PrefabModelToReturn! with { Renderers = [r0, lod1] };
        var dir = Path.Combine(ws, "mods", "big-carch");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
        File.Copy(glb, Path.Combine(dir, "skins", "spiked", "male_D.png"));
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"Carcharodontosaurus\",\"name\":\"Spiked\",\"base\":\"1\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");
        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, skin = "spiked" });
        return (h, reader);
    }

    [Fact]
    public async Task SkinModel_without_new_parameters_behaves_as_before()
    {
        using var game = new FakeGame();
        var (h, _) = await SkinWithTwoLods(game);

        var plain = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male" });
        var lod0 = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male", lod = 0, strict = false });

        Assert.Equal(plain.GetRawText(), lod0.GetRawText());
        Assert.EndsWith("lod0.glb", plain.GetProperty("ownModel").GetProperty("file").GetString());
    }

    [Fact]
    public async Task SkinModel_lod_picks_that_lods_file()
    {
        using var game = new FakeGame();
        var (h, _) = await SkinWithTwoLods(game);

        var model = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male", lod = 1 });

        Assert.EndsWith("lod1.glb", model.GetProperty("ownModel").GetProperty("file").GetString());
    }

    [Fact]
    public async Task SkinModel_lod_past_the_last_shows_the_last()
    {
        using var game = new FakeGame();
        var (h, _) = await SkinWithTwoLods(game);

        var model = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male", lod = 9 });

        Assert.EndsWith("lod1.glb", model.GetProperty("ownModel").GetProperty("file").GetString());
    }

    [Fact]
    public async Task SkinModel_negative_lod_shows_the_first()
    {
        using var game = new FakeGame();
        var (h, _) = await SkinWithTwoLods(game);

        var model = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male", lod = -1 });

        Assert.EndsWith("lod0.glb", model.GetProperty("ownModel").GetProperty("file").GetString());
    }

    [Fact]
    public async Task SkinModel_strict_throws_the_build_reason()
    {
        using var game = new FakeGame();
        var (h, ws, _, glb) = await Setup(game);
        var dir = Path.Combine(ws, "mods", "big-carch");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
        File.Copy(glb, Path.Combine(dir, "skins", "spiked", "male_D.png"));
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"Carcharodontosaurus\",\"name\":\"Spiked\",\"base\":\"1\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");
        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, skin = "spiked" });
        Directory.Delete(Path.Combine(dir, "models"), true); // the build is gone: no levels of detail to show

        var lenient = await h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male" });
        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.skinModel", new { id = "big-carch", skin = "spiked", sex = "male", strict = true }));

        Assert.Equal(System.Text.Json.JsonValueKind.Null, lenient.GetProperty("ownModel").ValueKind);
        Assert.Contains("no built levels of detail", ex.Message);
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
    public async Task A_custom_species_skins_own_model_can_be_previewed()
    {
        using var game = new FakeGame();
        var (h, ws, _, glb) = await Setup(game);
        var dir = Path.Combine(ws, "mods", "big-carch");
        Tyrant.Framework.Core.AnimalDatabaseFile.Save(Path.Combine(dir, Tyrant.Framework.Core.AnimalDatabaseFile.FileName),
            [new Tyrant.Framework.Core.SpeciesEntry { Id = "giga", Base = "Carcharodontosaurus", Name = "Giga", Genus = "G", Epithet = "giga" }]);
        Directory.CreateDirectory(Path.Combine(dir, "skins", "spiked"));
        File.Copy(glb, Path.Combine(dir, "skins", "spiked", "male_D.png"));
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"big-carch\",\"name\":\"Big Carch\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"spiked\",\"species\":\"tyrant.big-carch.giga\",\"name\":\"Spiked\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/spiked/male_D.png\"}}]}");
        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, skin = "spiked" });

        var preview = await h.Call("mods.modelPreview", new { id = "big-carch", target = "tyrant.big-carch.giga", skin = "spiked" });

        Assert.Equal(SkinDumps.Prefab.Ref, preview.GetProperty("prefabRef").GetString());
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

    [Fact]
    public async Task A_rig_only_species_entry_is_listed_and_its_rig_can_be_shown_and_cleared()
    {
        using var game = new FakeGame();
        var (h, ws, _, _) = await Setup(game);
        File.WriteAllText(Path.Combine(ws, "mods", "big-carch", "mod.json"),
            """{"format":1,"id":"big-carch","name":"Big Carch","version":"1.0.0","replace":[],"models":[{"target":"Carcharodontosaurus","rig":{"Tail":{"move":[0,0.1,0]}}}]}""");

        var detail = await h.Call("mods.get", new { id = "big-carch" });
        var model = detail.GetProperty("models")[0];
        Assert.Equal("", model.GetProperty("file").GetString());
        Assert.Equal("Tail", model.GetProperty("rig")[0].GetProperty("bone").GetString());
        Assert.Equal(0, model.GetProperty("lods").GetArrayLength());

        var rig = await h.Call("mods.rig", new { id = "big-carch", target = "Carcharodontosaurus" });
        Assert.False(rig.GetProperty("hasModel").GetBoolean());
        Assert.Equal(0.1f, rig.GetProperty("bones")[0].GetProperty("move")[1].GetSingle());
        Assert.Contains(rig.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("no model of its own"));

        var cleared = await h.Call("mods.clearRig", new { id = "big-carch", revision = detail.GetProperty("revision").GetString(), target = "Carcharodontosaurus" });
        Assert.Equal(0, cleared.GetProperty("models").GetArrayLength());
    }

    /// <summary>Like Setup, but the game's prefab is the 16-bone animal of the rig tests, so a model can carry a rig edit in its bones.</summary>
    private static async Task<(RpcHarness H, string Ws, PrefabModel Prefab)> SetupRigged(FakeGame game)
    {
        var prefab = Game();
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Quad_LOD0", [])] }] };
        var reader = new FakeAssetReader { PrefabModelToReturn = prefab };
        var h = new RpcHarness(TestStudio.Options(dumperDir: TestStudio.FakeGameModsDir(), reader: reader));
        var ws = TestStudio.TempDir();
        await h.Call("workspace.create", new { dir = ws, gamePath = game.Root });
        await h.Call("mods.create", new { id = "big-carch", name = "Big Carch" });
        SkinDumps.Write(Path.Combine(ws, "data"));
        AssetFixtures.WriteIndex(ws, [.. SkinDumps.Textures, SkinDumps.Prefab],
            Tyrant.Core.Install.GameFingerprint.Compute(new Tyrant.Core.Install.GameInstall(game.Root, null)));
        return (h, ws, prefab);
    }

    private static string[] RigBones(System.Text.Json.JsonElement detail) =>
        [.. detail.GetProperty("models")[0].GetProperty("rig").EnumerateArray().Select(b => b.GetProperty("bone").GetString()!).Order()];

    [Fact]
    public async Task ReadModelRig_reports_the_bones_and_the_question()
    {
        using var game = new FakeGame();
        var (h, _, prefab) = await SetupRigged(game);
        var glb = Export(prefab, R());

        var first = await h.Call("mods.readModelRig", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });
        Assert.Equal("bones", first.GetProperty("source").GetString());
        Assert.Equal(["Neck1", "Tail1"], first.GetProperty("bones").EnumerateArray().Select(b => b.GetProperty("bone").GetString()!).Order());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("current").ValueKind);
        Assert.Equal("none", first.GetProperty("question").GetString());
        Assert.True(first.GetProperty("changes").GetBoolean());
        Assert.Equal(0, first.GetProperty("errors").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("rigFile").ValueKind);

        await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });
        var same = await h.Call("mods.readModelRig", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });
        Assert.Equal("saved", same.GetProperty("source").GetString());
        Assert.False(same.GetProperty("changes").GetBoolean());

        var other = await h.Call("mods.readModelRig", new { id = "big-carch", file = Export(prefab, Rig(Turn("Head", System.Numerics.Vector3.UnitX, 30))), target = "Carcharodontosaurus" });
        Assert.Equal("useOrKeep", other.GetProperty("question").GetString());
        Assert.Equal(2, other.GetProperty("current").GetArrayLength());

        var game2 = await h.Call("mods.readModelRig", new { id = "big-carch", file = Export(prefab, null), target = "Carcharodontosaurus" });
        Assert.Equal("gameSkeleton", game2.GetProperty("source").GetString());
        Assert.Equal("keepOrRemove", game2.GetProperty("question").GetString());
    }

    [Fact]
    public async Task ReadModelRig_names_the_rig_file_it_used()
    {
        using var game = new FakeGame();
        var (h, _, prefab) = await SetupRigged(game);
        var glb = Export(prefab, R());
        File.WriteAllText(glb + ".rig.json", Tyrant.Framework.Core.Json.Write(Tyrant.Framework.Core.RigEdit.ToJson(R())));

        var read = await h.Call("mods.readModelRig", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });

        Assert.Equal("rigFile", read.GetProperty("source").GetString());
        Assert.Equal(Path.GetFileName(glb) + ".rig.json", read.GetProperty("rigFile").GetString());
    }

    [Fact]
    public async Task ReplaceModel_with_keepCurrent_keeps_the_rig_edit()
    {
        using var game = new FakeGame();
        var (h, _, prefab) = await SetupRigged(game);
        await h.Call("mods.replaceModel", new { id = "big-carch", file = Export(prefab, R()), target = "Carcharodontosaurus" });
        var other = Export(prefab, Rig(Turn("Head", System.Numerics.Vector3.UnitX, 30)));

        var kept = await h.Call("mods.replaceModel", new { id = "big-carch", file = other, target = "Carcharodontosaurus", rig = "keepCurrent" });
        Assert.Equal(["Neck1", "Tail1"], RigBones(kept));

        var used = await h.Call("mods.replaceModel", new { id = "big-carch", file = other, target = "Carcharodontosaurus" });
        Assert.Equal(["Head"], RigBones(used));

        var removed = await h.Call("mods.replaceModel", new { id = "big-carch", file = Export(prefab, null), target = "Carcharodontosaurus", rig = "useFile" });
        Assert.Equal(System.Text.Json.JsonValueKind.Null, removed.GetProperty("models")[0].GetProperty("rig").ValueKind);
    }

    [Fact]
    public async Task ReplaceModel_refuses_an_unknown_rig_choice()
    {
        using var game = new FakeGame();
        var (h, _, _, glb) = await Setup(game);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus", rig = "both" }));

        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Contains("both", ex.Message);
    }

    [Fact]
    public async Task ReplaceModel_refuses_a_rig_edit_the_rules_refuse()
    {
        using var game = new FakeGame();
        var (h, ws, prefab) = await SetupRigged(game);
        var glb = Export(prefab, R());
        var rigFile = R(); // matches the bones but names a bone the species does not have
        rigFile["Wing.L"] = Tyrant.Framework.Core.RigOffset.Identity with { Move = new Tyrant.Framework.Core.RigVector3(0, 0.1f, 0) };
        File.WriteAllText(glb + ".rig.json", Tyrant.Framework.Core.Json.Write(Tyrant.Framework.Core.RigEdit.ToJson(rigFile)));
        var path = Path.Combine(ws, "mods", "big-carch", "mod.json");
        var before = File.ReadAllText(path);

        var read = await h.Call("mods.readModelRig", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });
        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" }));

        Assert.Contains(read.GetProperty("errors").EnumerateArray(), e => e.GetString()!.Contains("Wing.L"));
        Assert.Equal("MOD_INVALID", ex.DataCode);
        Assert.Contains("Wing.L", ex.Message);
        Assert.Equal(before, File.ReadAllText(path));
    }
    [Fact]
    public async Task ReadModelRig_gives_the_errors_of_each_choice()
    {
        using var game = new FakeGame();
        var (h, _, prefab) = await SetupRigged(game);
        await h.Call("mods.replaceModel", new { id = "big-carch", file = Export(prefab, Rig(Turn("Head", System.Numerics.Vector3.UnitX, 30))), target = "Carcharodontosaurus" });
        var glb = Export(prefab, R());
        var rigFile = R(); // matches the bones but names a bone the species does not have
        rigFile["Wing.L"] = Tyrant.Framework.Core.RigOffset.Identity with { Move = new Tyrant.Framework.Core.RigVector3(0, 0.1f, 0) };
        File.WriteAllText(glb + ".rig.json", Tyrant.Framework.Core.Json.Write(Tyrant.Framework.Core.RigEdit.ToJson(rigFile)));

        var read = await h.Call("mods.readModelRig", new { id = "big-carch", file = glb, target = "Carcharodontosaurus" });

        Assert.Equal("useOrKeep", read.GetProperty("question").GetString());
        Assert.Contains(read.GetProperty("errorsIfUseFile").EnumerateArray(), e => e.GetString()!.Contains("Wing.L"));
        Assert.Equal(0, read.GetProperty("errorsIfKeep").GetArrayLength());
        var kept = await h.Call("mods.replaceModel", new { id = "big-carch", file = glb, target = "Carcharodontosaurus", rig = "keepCurrent" });
        Assert.Equal(["Head"], RigBones(kept));
    }
}
