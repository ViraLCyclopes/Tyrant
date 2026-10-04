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
        foreach (var file in manifest.Models.Select(m => m.File).Concat(manifest.Skins.Where(s => s.Model != null).Select(s => s.Model!)))
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
}
