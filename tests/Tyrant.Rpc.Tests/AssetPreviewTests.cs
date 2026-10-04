using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Tests;
using static Tyrant.Rpc.Tests.AssetFixtures;

namespace Tyrant.Rpc.Tests;

public class AssetPreviewTests
{
    [Fact]
    public async Task Texture_preview_is_written_once_per_build()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await Opened(game);

        var first = await h.Call("assets.preview", new { @ref = StegoD.Ref });
        var second = await h.Call("assets.preview", new { @ref = StegoD.Ref });

        Assert.Equal("texture", first.GetProperty("kind").GetString());
        var file = first.GetProperty("files")[0].GetString()!;
        Assert.StartsWith(Path.Combine(Path.GetFullPath(ws), "cache", "previews") + Path.DirectorySeparatorChar, file);
        Assert.True(File.Exists(file));
        Assert.Equal(64, first.GetProperty("width").GetInt32());
        Assert.Equal("DXT5", first.GetProperty("format").GetString());
        Assert.Equal(file, second.GetProperty("files")[0].GetString());
        Assert.Equal(1, reader.Textures);
    }

    [Fact]
    public async Task A_new_build_gets_new_previews()
    {
        using var game = new FakeGame(buildGuid: "build-one");
        var (h, _, reader) = await Opened(game);
        var before = (await h.Call("assets.preview", new { @ref = StegoD.Ref })).GetProperty("files")[0].GetString();

        File.WriteAllText(new GameInstall(game.Root, null).BootConfigPath, "build-guid=build-two\n");
        var after = (await h.Call("assets.preview", new { @ref = StegoD.Ref })).GetProperty("files")[0].GetString();

        Assert.NotEqual(before, after);
        Assert.Contains("build-two", after);
        Assert.Equal(2, reader.Textures);
        var oldBuildFolder = Path.GetDirectoryName(Path.GetDirectoryName(before))!;
        Assert.False(Directory.Exists(oldBuildFolder), "previews of the old build are removed");
    }

    [Fact]
    public async Task A_model_preview_whose_texture_was_deleted_is_made_again()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);
        reader.ModelTextureFiles = ["T_Stego_D.png"];
        var first = await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });
        var png = Path.Combine(Path.GetDirectoryName(first.GetProperty("files")[0].GetString()!)!, "textures", "T_Stego_D.png");
        Assert.True(File.Exists(png));

        File.Delete(png);
        await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });

        Assert.Equal(2, reader.Models);
        Assert.True(File.Exists(png));
    }

    [Fact]
    public async Task Model_preview_for_a_prefab_lists_its_glb_files()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var preview = await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });

        Assert.Equal("model", preview.GetProperty("kind").GetString());
        Assert.Equal(2, preview.GetProperty("files").GetArrayLength());
        Assert.True(preview.GetProperty("skinned").GetBoolean());
        Assert.Equal(100, preview.GetProperty("triangles").GetInt32());
    }

    [Fact]
    public async Task Assets_without_a_visual_preview_say_so()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);

        var preview = await h.Call("assets.preview", new { @ref = Menu.Ref });

        Assert.Equal("none", preview.GetProperty("kind").GetString());
        Assert.Contains("MonoBehaviour", preview.GetProperty("message").GetString());
        Assert.Equal(0, reader.Textures + reader.Models);
    }

    [Fact]
    public async Task A_failing_preview_reports_the_error_with_its_fix_and_can_be_retried()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);
        reader.FailFor.Add(StegoD.Ref);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("assets.preview", new { @ref = StegoD.Ref }));
        reader.FailFor.Clear();
        var retried = await h.Call("assets.preview", new { @ref = StegoD.Ref });

        Assert.Equal("ASSET_UNREADABLE", ex.DataCode);
        Assert.Equal("REFRESH_WORKSPACE", ex.Fix);
        Assert.Equal("texture", retried.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Export_job_exports_every_asset_and_reports_failures()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);
        reader.FailFor.Add(RexD.Ref);

        var result = await h.RunJob("assets.export", new { refs = new[] { StegoD.Ref, RexD.Ref, Menu.Ref } });

        Assert.Equal(2, result.GetProperty("exported").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.True(File.Exists(result.GetProperty("reportPath").GetString()));
        Assert.Equal("T_Rex_D", result.GetProperty("failures")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Export_needs_at_least_one_asset()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("assets.export", new { refs = Array.Empty<string>() }));

        Assert.Equal(-32602, ex.Code);
    }

    [Fact]
    public async Task Species_pack_job_reports_where_it_wrote()
    {
        using var game = new FakeGame();
        var (h, ws, _) = await Opened(game);

        var result = await h.RunJob("species.pack", new { key = "stego" });

        Assert.Equal(Path.Combine(Path.GetFullPath(ws), "assets", "species", "stegosaurus"), result.GetProperty("directory").GetString());
    }

    [Fact]
    public async Task Unknown_species_is_not_found()
    {
        using var game = new FakeGame();
        var (h, _, _) = await Opened(game);

        var ex = await Assert.ThrowsAsync<RpcCallException>(() => h.Call("species.pack", new { key = "nope" }));

        Assert.Equal("ASSET_NOT_FOUND", ex.DataCode);
    }

    [Fact]
    public async Task Without_a_build_id_a_changed_game_still_gets_new_previews()
    {
        using var game = new FakeGame(buildGuid: null);
        var (h, _, reader) = await Opened(game);
        var before = (await h.Call("assets.preview", new { @ref = StegoD.Ref })).GetProperty("files")[0].GetString();

        File.AppendAllText(new GameInstall(game.Root, null).AssemblyCSharpPath, "patched");
        var after = (await h.Call("assets.preview", new { @ref = StegoD.Ref })).GetProperty("files")[0].GetString();

        Assert.NotEqual(before, after);
        Assert.Equal(2, reader.Textures);
    }

    [Fact]
    public async Task Model_preview_shows_only_the_most_detailed_lod()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);
        reader.ModelParts = ["Acro_LOD02", "Acro_LOD01", "Acro_LOD00"];

        var preview = await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });

        var file = Assert.Single(preview.GetProperty("files").EnumerateArray()).GetString()!;
        Assert.EndsWith("Acro_LOD00.glb", file);
        Assert.Equal(50, preview.GetProperty("triangles").GetInt32());
        Assert.Contains("1 of 3 parts", preview.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_rebuilt_normal_map_preview_says_so()
    {
        using var game = new FakeGame();
        var normal = new Tyrant.Core.Assets.AssetRecord("animals/stego_assets_assets/textures.bundle", 9, "Texture2D", "T_Stego_N", null, null, null);
        var (h, _, _) = await Opened(game, [.. All, normal]);

        var preview = await h.Call("assets.preview", new { @ref = normal.Ref });

        Assert.Contains("Normal map rebuilt", preview.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Previews_are_keyed_by_the_preview_format_so_tool_updates_redo_them()
    {
        using var game = new FakeGame(buildGuid: "build-one");
        var (h, _, _) = await Opened(game);

        var file = (await h.Call("assets.preview", new { @ref = StegoD.Ref })).GetProperty("files")[0].GetString()!;

        var buildFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)))!;
        Assert.EndsWith($"-p{Tyrant.Rpc.Assets.PreviewCache.FormatVersion}", buildFolder);
        Assert.True(Tyrant.Rpc.Assets.PreviewCache.FormatVersion >= 5, "scenery materials (format 5) must not reuse untextured format-4 previews");
    }

    private static readonly AssetRecord StegoAlt = new("animals/stego_assets_assets/textures.bundle", 7, "Texture2D", "T_Stego_alt1_D", null, null, null);
    private static readonly AssetRecord StegoPortrait = new("animals/stego_assets_assets/textures.bundle", 8, "Texture2D", "port_Stego_D", null, null, null);

    [Fact]
    public async Task A_prefab_preview_lists_its_materials_and_the_species_skins()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game, [.. All, StegoAlt, StegoPortrait]);
        reader.ModelMaterials = [new ResolvedMaterial("Stego", StegoD, "_AdultDiffuse", StegoN, []), new ResolvedMaterial("Eyes", null, null, null, [])];

        var preview = await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });

        var materials = preview.GetProperty("materials").EnumerateArray()
            .Select(m => (m.GetProperty("name").GetString(), m.GetProperty("baseColor").GetString(), m.GetProperty("normal").GetString(),
                m.GetProperty("skinnable").GetBoolean()))
            .ToList();
        Assert.Equal(new (string?, string?, string?, bool)[] { ("Stego", "T_Stego_D", "T_Stego_N", true), ("Eyes", null, null, false) }, materials);
        var skins = preview.GetProperty("skins").EnumerateArray().Select(s => (s.GetProperty("name").GetString(), s.GetProperty("current").GetBoolean())).ToList();
        Assert.Equal(new (string?, bool)[] { ("T_Stego_alt1_D", false), ("T_Stego_D", true) }, skins); // Rex's skin and the portrait are left out
    }

    [Fact]
    public async Task A_bare_mesh_preview_offers_no_skins()
    {
        using var game = new FakeGame();
        var mesh = new AssetRecord("animals/stego_assets_assets/prefabs.bundle", 9, "Mesh", "Stego_Body", null, null, null);
        var (h, _, reader) = await Opened(game, [.. All, mesh]);
        reader.ModelMaterials = [new ResolvedMaterial("Stego", StegoD, "_AdultDiffuse", null, [])];

        var preview = await h.Call("assets.preview", new { @ref = mesh.Ref });

        Assert.Equal(System.Text.Json.JsonValueKind.Null, preview.GetProperty("skins").ValueKind);
    }

    [Fact]
    public async Task A_prefab_preview_from_an_index_without_archives_asks_for_a_new_index_and_is_made_again_after_one()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await Opened(game, archives: false);

        var first = await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });
        WriteIndex(ws, All, GameFingerprint.Compute(new GameInstall(game.Root, null)));
        var second = await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });

        Assert.Contains("Index assets", first.GetProperty("message").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, second.GetProperty("message").ValueKind);
        Assert.Equal(2, reader.Models);
    }

    [Fact]
    public async Task Texture_failures_are_noted_in_the_preview()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);
        reader.ModelTextureFailures = ["T_Stego_D: Texture format BC7 is not supported."];

        var preview = await h.Call("assets.preview", new { @ref = StegoPrefab.Ref });

        Assert.Contains("1 texture(s) could not be decoded", preview.GetProperty("message").GetString());
        Assert.Contains("BC7", preview.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Export_job_passes_texture_notes_on()
    {
        using var game = new FakeGame();
        var (h, _, reader) = await Opened(game);
        reader.ModelNotes = ["Textures kept in other bundles need a newer asset index: click Index assets on the Workspace tab."];

        var result = await h.RunJob("assets.export", new { refs = new[] { StegoPrefab.Ref } });

        Assert.Contains("Index assets", result.GetProperty("notes")[0].GetString());
    }

    [Fact]
    public async Task Species_pack_job_passes_texture_notes_on()
    {
        using var game = new FakeGame();
        var (h, ws, reader) = await Opened(game);
        var dir = Path.Combine(ws, "assets", "species", "stegosaurus");
        reader.SpeciesPack = new Tyrant.Core.Species.SpeciesPackResult(dir, [], [], Path.Combine(dir, "targets.json"))
        {
            Notes = ["Texture T_Stego_D could not be decoded, so its material is plain: BC7"],
        };

        var result = await h.RunJob("species.pack", new { key = "stegosaurus" });

        Assert.Contains("BC7", result.GetProperty("notes")[0].GetString());
    }
}
