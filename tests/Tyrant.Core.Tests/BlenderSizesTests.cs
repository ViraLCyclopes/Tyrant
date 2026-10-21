using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

/// <summary>The project file's sizes for Blender's Growth slider and Size panel, and sizes sent back from Blender.</summary>
public class BlenderSizesTests
{
    // Tangents as the game's curves have them: a straight line between the keys would be far off (0.625 instead of 0.806 at 0.5).
    private const string Curve = """
        [{"time":0,"value":0.25,"inTangent":1.5,"outTangent":1.5},{"time":1,"value":1,"inTangent":0.05,"outTangent":0.05},
         {"time":2,"value":1.1,"inTangent":0,"outTangent":0}]
        """;

    private static float Game(float t) => UnityCurve.Evaluate(System.Text.Json.JsonDocument.Parse("""{"keys":""" + Curve + "}").RootElement, t);

    /// <summary>The curve as Tyrant reads it from the dump: Unity's Hermite, sampled.</summary>
    private static readonly (float Time, float Value)[] Original = SizeMath.Sample([0f, 1f, 2f], Game);

    private sealed record Ctx(FakeGame Game, Workspace Ws, GameInstall Install, AssetIndex Index, IReadOnlyList<SpeciesSkins> Species,
        FakeAssetReader Reader, PrefabModel Prefab) : IDisposable
    {
        public void Dispose() => Game.Dispose();

        public BlenderProject Open(BlenderOpenRequest request, IReadOnlyList<SpeciesSkins>? species = null) =>
            BlenderProjectFile.Read(BlenderProjectWriter.Write(request, Ws, Install, Index, species ?? Species, Reader, "t.exe").ProjectFile);
    }

    private static Ctx Setup()
    {
        var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), install);
        SkinDumps.Write(ws.DataDir, Curve);
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD0"));
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        return new Ctx(game, ws, install, new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] }, SpeciesSkinsReader.Load(ws),
            new FakeAssetReader { PrefabModelToReturn = prefab }, prefab);
    }

    private static float[][] Keys((float Time, float Value)[] keys) => [.. keys.Select(k => new[] { k.Time, k.Value })];

    private static void AssertKeys(float[][] expected, float[][]? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual!.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i][0], actual[i][0], 5);
            Assert.Equal(expected[i][1], actual[i][1], 5);
        }
    }

    /// <summary>A mod that replaces the vanilla species' model, so the species is its destination.</summary>
    private static ModProject SpeciesModelMod(Ctx c, string id)
    {
        var mod = ModProject.Create(c.Ws, id, null, null);
        Directory.CreateDirectory(Path.Combine(mod.Dir, "models"));
        GltfModelWriter.WriteGlb(c.Prefab, c.Prefab.Renderers[0], Path.Combine(mod.Dir, "models", "carch.glb"), [new GltfMaterial("Carch")]);
        mod.Manifest.Models.Add(new ModelReplacement { Target = "Carcharodontosaurus", Key = SkinDumps.Prefab.ContainerPath, File = "models/carch.glb" });
        mod.Save();
        return mod;
    }

    [Fact]
    public void Project_file_carries_scale_curve_and_sizes()
    {
        using var c = Setup();
        var pack = ModProject.Create(c.Ws, "pack", null, null);
        var key = pack.AddSpecies(c.Species, "mirabilis", "Carcharodontosaurus", "Mirabilis", "Tyrantus", "mirabilis").Key(pack.Id);
        pack.SetSizes(key, new SizeSettings { Adult = 1.2, Baby = 0.3, Male = 1.1 });

        var custom = c.Open(new BlenderOpenRequest(key, null, "pack", false, false), ModProject.WithSpecies(c.Species, pack.Manifest));

        Assert.True(custom.SizesEditable);
        Assert.Equal(new BlenderSizes(1.2, 0.3, 1.1, null, 1.3, 1.35), custom.Sizes); // the template's first skin (Base)
        AssertKeys(Keys(Original), custom.Growth!.OriginalScale);
        AssertKeys(Keys(SizeMath.Rebuild(Original, 1.2, 0.3)), custom.Growth.Scale);
        Assert.Equal(1.3f * 1.2f * 1.1f, custom.Sexes!.Male.Size, 4);
        Assert.Equal(1.35f * 1.2f, custom.Sexes.Female.Size, 4);
        Assert.Equal(0.8f, custom.Sexes.Female.GrowthClamp); // a custom species grows like its template

        // A vanilla species target without sizes: editable, the game's own curve and sex sizes.
        SpeciesModelMod(c, "plain");
        var vanilla = c.Open(new BlenderOpenRequest("Carcharodontosaurus", null, "plain", false, false));
        Assert.True(vanilla.SizesEditable);
        Assert.Equal(new BlenderSizes(null, null, null, null, 1.3, 1.35), vanilla.Sizes);
        AssertKeys(Keys(Original), vanilla.Growth!.Scale);
        Assert.Equal(new BlenderSex(1f, 1.3f), vanilla.Sexes!.Male);
    }

    [Fact]
    public void Growth_slider_curves_follow_the_games_hermite_curve()
    {
        using var c = Setup();
        SpeciesModelMod(c, "plain");
        var unsized = c.Open(new BlenderOpenRequest("Carcharodontosaurus", null, "plain", false, false));
        ModProject.Open(c.Ws, "plain").SetSizes("Carcharodontosaurus", new SizeSettings { Adult = 1.2 });
        var sized = c.Open(new BlenderOpenRequest("Carcharodontosaurus", null, "plain", false, false));

        static float At(float[][] keys, float t) => SizeMath.Evaluate([.. keys.Select(k => (k[0], k[1]))], t);
        foreach (var m in new[] { 0.25f, 0.5f, 0.75f })
        {
            var game = Game(m);
            Assert.InRange(At(unsized.Growth!.OriginalScale!, m) / game, 0.99, 1.01);
            Assert.InRange(At(unsized.Growth.Scale!, m) / game, 0.99, 1.01); // a species without sizes shows the game's shape too
            Assert.InRange(At(sized.Growth!.Scale!, m) * (1 + 0.2 * SizeMath.Fade(m)) / (1.2 * game), 0.99, 1.01);
        }
    }

    [Fact]
    public void Skin_destination_is_not_editable()
    {
        using var c = Setup();
        var mod = ModProject.Create(c.Ws, "reds", null, null);
        var skin = mod.AddSkin(c.Ws, c.Install, c.Index, c.Reader, c.Species, "Carcharodontosaurus", "Red", "1", new SkinTemplateOptions(true, false, false));
        mod.SetSizes("Carcharodontosaurus", new SizeSettings { Adult = 1.5 });

        var project = c.Open(new BlenderOpenRequest("Carcharodontosaurus", skin.Id, "reds", false, false));

        Assert.False(project.SizesEditable);
        Assert.Equal(1.5, project.Sizes!.Adult); // the species' sizes, shown read-only
        Assert.Equal((float)(project.Sizes.MaleOriginal * 1.5), project.Sexes!.Male.Size, 4);
        AssertKeys(Keys(SizeMath.Rebuild(Original, 1.5, null)), project.Growth!.Scale);

        // A game model with no destination yet: nothing to edit, the game's sizes.
        var game = c.Open(new BlenderOpenRequest("Carcharodontosaurus", "Alt 1", null, false, false));
        Assert.False(game.SizesEditable);
        Assert.Equal(new BlenderSizes(null, null, null, null, 1.2, 1.25), game.Sizes);
    }

    [Fact]
    public void Send_writes_the_sizes_to_the_species_and_the_project_shows_them()
    {
        using var c = Setup();
        var opened = BlenderProjectWriter.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false), c.Ws, c.Install, c.Index, c.Species, c.Reader, "t.exe");
        var glb = Path.Combine(opened.Dir, "send.glb");
        GltfModelWriter.WriteGlb(c.Prefab, c.Prefab.Renderers[0], glb, [new GltfMaterial("Carch")]);
        ModProject.Create(c.Ws, "pack", null, null);

        var sent = BlenderService.Send(c.Ws, c.Install, c.Index, c.Species, c.Reader, opened.ProjectFile, glb,
            new BlenderDestination("pack", "Carcharodontosaurus", null), null, sizes: new SizeSettings { Target = "Carcharodontosaurus", Adult = 1.2 });

        Assert.True(sent.Ok, string.Join(" | ", sent.Errors));
        Assert.Equal(1.2, ModProject.Open(c.Ws, "pack").Sizes("Carcharodontosaurus")!.Adult);
        var project = BlenderProjectFile.Read(opened.ProjectFile);
        Assert.True(project.SizesEditable); // the species model is now its destination
        Assert.Equal(1.2, project.Sizes!.Adult);
        AssertKeys(Keys(SizeMath.Rebuild(Original, 1.2, null)), project.Growth!.Scale);

        // The app changes the adult while Blender changes the baby: Blender sends only its key, and both survive.
        ModProject.Open(c.Ws, "pack").SetSizes("Carcharodontosaurus", new SizeSettings { Adult = 1.3 });
        var merged = BlenderService.Send(c.Ws, c.Install, c.Index, c.Species, c.Reader, opened.ProjectFile, glb, null, null,
            sizes: new SizeSettings { Target = "Carcharodontosaurus", Baby = 0.4 });
        Assert.True(merged.Ok, string.Join(" | ", merged.Errors));
        var both = ModProject.Open(c.Ws, "pack").Sizes("Carcharodontosaurus")!;
        Assert.Equal(1.3, both.Adult);
        Assert.Equal(0.4, both.Baby);
        Assert.Equal(new BlenderSizes(1.3, 0.4, null, null, 1.3, 1.35), BlenderProjectFile.Read(opened.ProjectFile).Sizes);

        // A skin's sizes are the species': a send to a skin with sizes is refused before anything is written.
        var mod = ModProject.Open(c.Ws, "pack");
        var skin = mod.AddSkin(c.Ws, c.Install, c.Index, c.Reader, c.Species, "Carcharodontosaurus", "Red", "1", new SkinTemplateOptions(true, false, false));
        var refused = BlenderService.Send(c.Ws, c.Install, c.Index, c.Species, c.Reader, opened.ProjectFile, glb,
            new BlenderDestination("pack", "Carcharodontosaurus", skin.Id), null, sizes: new SizeSettings { Target = "Carcharodontosaurus", Adult = 2 });
        Assert.False(refused.Ok);
        Assert.Contains("Sizes apply to the whole species; set them in the app.", refused.Errors);
        Assert.Equal(1.3, ModProject.Open(c.Ws, "pack").Sizes("Carcharodontosaurus")!.Adult);
        Assert.Null(ModProject.Open(c.Ws, "pack").Skin(skin.Id).Model);
    }
}
