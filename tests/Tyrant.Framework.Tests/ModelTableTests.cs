using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ModelTableTests
{
    /// <summary>A loaded mod in a temp folder with a .tmesh (LOD 0) for every model it names.</summary>
    private static LoadedMod Mod(string id, string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), id);
        Directory.CreateDirectory(dir);
        var manifest = ModManifest.Parse(json);
        foreach (var file in manifest.Models.Where(m => m.File.Length > 0).Select(m => m.File).Concat(manifest.Skins.Where(s => s.Model != null).Select(s => s.Model!)))
        {
            var path = Path.GetFullPath(Path.Combine(dir, ModelFiles.Lod(file, 0)));
            if (!ModPaths.IsInside(path, dir)) continue; // never write outside the test folder
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            TMeshTests.Sample().Write(stream);
        }
        return new LoadedMod(manifest, dir);
    }

    private const string Species = """{"format":1,"id":"big-carch","models":[{"target":"Carcharodontosaurus","file":"models/a.glb"}]}""";
    private const string SkinModel = """
        {"format":1,"id":"spiked-carch","models":[],"skins":[{"id":"spiked","species":"Carcharodontosaurus","name":"Spiked","base":"0",
         "male":{"diffuse":"skins/spiked/male_D.png"},"model":"models/b.glb"}]}
        """;

    [Fact]
    public void A_skin_model_wins_over_the_species_model()
    {
        var table = ModelTable.Build([Mod("big-carch", Species), Mod("spiked-carch", SkinModel)]);

        Assert.True(table.TryChoose("Carcharodontosaurus", "spiked-carch/spiked", out var set));
        Assert.Equal("spiked-carch", set.ModId);
        Assert.EndsWith(Path.Combine("models", "b.lod0.tmesh"), set.LodPaths[0]);
    }

    [Fact]
    public void Other_skins_get_the_species_model()
    {
        var table = ModelTable.Build([Mod("big-carch", Species)]);

        Assert.True(table.TryChoose("Carcharodontosaurus", null, out var set));
        Assert.Equal("big-carch", set.ModId);
    }

    [Fact]
    public void Species_without_a_model_keep_their_own()
    {
        Assert.False(ModelTable.Build([Mod("big-carch", Species)]).TryChoose("Stegosaurus", null, out _));
    }

    [Fact]
    public void The_later_mod_in_the_load_order_wins()
    {
        var other = Species.Replace("big-carch", "huge-carch").Replace("models/a.glb", "models/c.glb");

        var table = ModelTable.Build([Mod("big-carch", Species), Mod("huge-carch", other)]);

        Assert.True(table.TryChoose("Carcharodontosaurus", null, out var set));
        Assert.Equal("huge-carch", set.ModId);
    }

    [Fact]
    public void A_model_file_outside_its_mod_is_ignored()
    {
        var escape = Species.Replace("models/a.glb", "../../evil.glb");

        Assert.False(ModelTable.Build([Mod("big-carch", escape)]).TryChoose("Carcharodontosaurus", null, out _));
    }

    [Fact]
    public void A_model_whose_files_were_never_built_is_ignored()
    {
        var mod = Mod("big-carch", Species);
        File.Delete(Path.Combine(mod.Directory, ModelFiles.Lod("models/a.glb", 0)));

        Assert.False(ModelTable.Build([mod]).TryChoose("Carcharodontosaurus", null, out _));
    }

    private const string RigOnly = """{"format":1,"id":"long-carch","models":[{"target":"Carcharodontosaurus","rig":{"Jaw":{"move":[0,0.1,0]}}}]}""";

    [Fact]
    public void The_later_mod_supplies_both_model_and_rig_of_a_species()
    {
        var table = ModelTable.Build([Mod("long-carch", RigOnly), Mod("big-carch", Species)], out var messages);

        Assert.True(table.TryChoose("Carcharodontosaurus", null, out var set));
        Assert.Equal("big-carch", set.ModId);
        Assert.Null(set.Rig); // long-carch's rig does not go onto big-carch's model
        Assert.Contains(messages, m => m.Contains("long-carch") && m.Contains("big-carch"));
    }

    [Fact]
    public void A_rig_only_species_entry_is_chosen_with_no_lods()
    {
        var table = ModelTable.Build([Mod("long-carch", RigOnly)], out var messages);

        Assert.True(table.TryChoose("Carcharodontosaurus", null, out var set));
        Assert.Empty(set.LodPaths);
        Assert.Equal(0.1f, set.Rig!["Jaw"].Move.Y);
        Assert.Empty(messages);
    }

    [Fact]
    public void A_skin_with_only_a_rig_wins_over_the_species_model()
    {
        var json = """
            {"format":1,"id":"big-carch","models":[{"target":"Carcharodontosaurus","file":"models/a.glb"}],
             "skins":[{"id":"long","species":"Carcharodontosaurus","male":{"diffuse":"a.png"},"rig":{"Jaw":{"move":[0,0.1,0]}}}]}
            """;

        var table = ModelTable.Build([Mod("big-carch", json)]);

        Assert.True(table.TryChoose("Carcharodontosaurus", "big-carch/long", out var set));
        Assert.Empty(set.LodPaths); // the game's mesh with the skin's rig, not the species model
        Assert.NotNull(set.Rig);
    }

    [Fact]
    public void A_skin_model_carries_its_rig()
    {
        var json = SkinModel.Replace("\"model\":\"models/b.glb\"", "\"model\":\"models/b.glb\",\"rig\":{\"Jaw\":{\"move\":[0,0.1,0]}}");

        var table = ModelTable.Build([Mod("spiked-carch", json)]);

        Assert.True(table.TryChoose("Carcharodontosaurus", "spiked-carch/spiked", out var set));
        Assert.Single(set.LodPaths);
        Assert.NotNull(set.Rig);
    }

    [Fact]
    public void Clashes_name_the_overridden_mod_once_per_species()
    {
        var a = ModManifest.Parse(RigOnly);
        var b = ModManifest.Parse(Species);
        var c = ModManifest.Parse("""{"format":1,"id":"stego","models":[{"target":"Stegosaurus","file":"m.glb"}]}""");

        var clash = Assert.Single(ModelTable.Clashes([("long-carch", a), ("big-carch", b), ("stego", c)]));

        Assert.Equal(("Carcharodontosaurus", "long-carch", "big-carch"), (clash.Species, clash.Loser, clash.Winner));
        Assert.Contains("big-carch", clash.Message);
    }
}
