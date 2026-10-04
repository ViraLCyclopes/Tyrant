using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModProjectModelsTests
{
    private static (FakeGame Game, Workspace Ws, GameInstall Install, ModProject Mod, FakeAssetReader Reader, string Glb) Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir);
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD00"), skinned: true);
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        var reader = new FakeAssetReader { PrefabModelToReturn = prefab };
        var glb = Path.Combine(ws.Dir, "carch-edit.glb");
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], glb, [new GltfMaterial("Carch")]);
        return (game, ws, install, ModProject.Create(ws, "big-carch", null, null), reader, glb);
    }

    private static AssetIndex Index() => new() { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] };

    [Fact]
    public void Replacing_a_species_model_records_it_and_builds_its_files()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;

        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);

        var entry = Assert.Single(mod.Manifest.Models);
        Assert.Equal("Carcharodontosaurus", entry.Target);
        Assert.Equal(SkinDumps.Prefab.ContainerPath, entry.Key);
        Assert.Matches(@"^models/carcharodontosaurus-[0-9a-f]{8}\.glb$", entry.File);
        Assert.True(File.Exists(Path.Combine(mod.Dir, ModelFiles.Lod(entry.File, 0))));
        Assert.Equal(entry.File, ModProject.Open(ws, "big-carch").Manifest.Models[0].File); // saved
    }

    [Fact]
    public void The_assets_tab_names_the_prefab_and_tyrant_finds_its_species()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;

        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, null, SkinDumps.Prefab.Ref, null);

        Assert.Equal("Carcharodontosaurus", Assert.Single(mod.Manifest.Models).Target);
    }

    [Fact]
    public void A_prefab_that_is_no_species_is_refused_for_now()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        var fence = new AssetRecord("@data/sharedassets0.assets", 30593, "GameObject", "Fence_AdobeClay_Segment_1m", null, null, null);

        var ex = Assert.Throws<TyrantException>(() =>
            mod.ReplaceModel(install, new AssetIndex { Assets = [fence] }, SpeciesSkinsReader.Load(ws), reader, glb, null, fence.Ref, null));

        Assert.Contains("objects come in a later update", ex.Message);
    }

    [Fact]
    public void A_skin_model_is_recorded_on_the_skin()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        mod.AddSkin(ws, install, Index(), reader, SpeciesSkinsReader.Load(ws), "Carcharodontosaurus", "Spiked", "1",
            new SkinTemplateOptions(Male: true, Female: false, Maps: false));
        var skinId = mod.Manifest.Skins[0].Id;

        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, null, null, skinId);

        Assert.Matches($@"^models/carcharodontosaurus-{skinId}-[0-9a-f]{{8}}\.glb$", mod.Manifest.Skins[0].Model);
        Assert.Empty(mod.Manifest.Models);
    }

    [Fact]
    public void A_model_with_errors_is_refused_and_mod_json_is_unchanged()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        reader.PrefabModelToReturn = reader.PrefabModelToReturn! with
        { Renderers = [reader.PrefabModelToReturn.Renderers[0] with { Materials = [new MaterialModel("Stego", [])] }] };
        var before = File.ReadAllText(Path.Combine(mod.Dir, "mod.json"));

        var ex = Assert.Throws<TyrantException>(() => mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null));

        Assert.Contains("Carch", ex.Message);
        Assert.Equal(before, File.ReadAllText(Path.Combine(mod.Dir, "mod.json")));
    }

    [Fact]
    public void Replacing_twice_keeps_the_first_files_for_undo()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);
        var first = mod.Manifest.Models[0].File;
        File.AppendAllText(glb, " "); // a re-export: different content

        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);

        Assert.NotEqual(first, Assert.Single(mod.Manifest.Models).File);
        Assert.True(File.Exists(Path.Combine(mod.Dir, first)));
        Assert.True(File.Exists(Path.Combine(mod.Dir, ModelFiles.Lod(first, 0))));
    }

    [Fact]
    public void A_changed_glb_is_rebuilt_on_check()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);
        File.SetLastWriteTimeUtc(Path.Combine(mod.Dir, mod.Manifest.Models[0].File), DateTime.UtcNow.AddMinutes(5));

        var rebuilt = mod.RebuildStaleModels(install, Index(), SpeciesSkinsReader.Load(ws), reader);

        Assert.Single(rebuilt);
        Assert.False(ModelBuilder.IsStale(mod.Dir, mod.Manifest.Models[0].File));
    }

    [Fact]
    public void Removing_a_model_keeps_its_files()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);
        var file = mod.Manifest.Models[0].File;

        mod.RemoveModel("Carcharodontosaurus", null);

        Assert.Empty(mod.Manifest.Models);
        Assert.True(File.Exists(Path.Combine(mod.Dir, file))); // undo can bring it back
    }

    [Fact]
    public void Check_reports_a_missing_model_file()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);
        File.Delete(Path.Combine(mod.Dir, mod.Manifest.Models[0].File));

        var result = new ModChecker(_ => (2, 2)).Check(mod, Index(), SpeciesSkinsReader.Load(ws));

        Assert.Contains(result.Errors, e => e.Contains("Carcharodontosaurus") && e.Contains("is missing"));
    }

    [Fact]
    public void Check_reports_a_models_build_errors()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);
        var file = mod.Manifest.Models[0].File;
        var broken = reader.PrefabModelToReturn! with { Renderers = [reader.PrefabModelToReturn.Renderers[0] with { Materials = [new MaterialModel("Stego", [])] }] };
        ModelBuilder.Build(mod.Dir, file, broken); // as a rebuild after a game update would

        var result = new ModChecker(_ => (2, 2)).Check(mod, Index(), SpeciesSkinsReader.Load(ws));

        Assert.Contains(result.Errors, e => e.StartsWith("Model of Carcharodontosaurus:") && e.Contains("Carch"));
    }

    [Fact]
    public void A_mod_with_only_a_model_is_not_empty()
    {
        var (game, ws, install, mod, reader, glb) = Setup();
        using var _ = game;
        mod.ReplaceModel(install, Index(), SpeciesSkinsReader.Load(ws), reader, glb, "Carcharodontosaurus", null, null);

        var result = new ModChecker(_ => (2, 2)).Check(mod, Index(), SpeciesSkinsReader.Load(ws));

        Assert.DoesNotContain(result.Warnings, w => w.Contains("does nothing yet"));
    }
}
