using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ModelManifestTests
{
    private const string Json = """
        {"format":1,"id":"big-carch","name":"Big Carch","version":"1.0.0","replace":[],
         "models":[{"target":"Carcharodontosaurus","key":"Assets/Prefabs/Animals/V2-MainPrefabs/Carcharodontosaurus.V2.prefab","file":"models/carch-1a2b3c4d.glb"}],
         "skins":[{"id":"spiked","species":"Carcharodontosaurus","name":"Spiked","base":"0","male":{"diffuse":"skins/spiked/male_D.png"},"model":"models/carch-spiked-5e6f7a8b.glb"}]}
        """;

    [Fact]
    public void Models_and_a_skin_model_are_read()
    {
        var m = ModManifest.Parse(Json);

        var model = Assert.Single(m.Models);
        Assert.Equal(("Carcharodontosaurus", "models/carch-1a2b3c4d.glb"), (model.Target, model.File));
        Assert.EndsWith("Carcharodontosaurus.V2.prefab", model.Key);
        Assert.Equal("models/carch-spiked-5e6f7a8b.glb", m.Skins[0].Model);
    }

    [Fact]
    public void Models_survive_a_round_trip()
    {
        var again = ModManifest.Parse(ModManifest.Parse(Json).ToJson());

        Assert.Equal("Carcharodontosaurus", Assert.Single(again.Models).Target);
        Assert.Equal("models/carch-spiked-5e6f7a8b.glb", again.Skins[0].Model);
    }

    [Theory]
    [InlineData("""{"format":1,"id":"big-carch","models":[{"file":"models/a.glb"}]}""", "has no \"target\"")]
    [InlineData("""{"format":1,"id":"big-carch","models":[{"target":"Carcharodontosaurus"}]}""", "has no \"file\"")]
    [InlineData("""{"format":1,"id":"big-carch","models":["x"]}""", "must be an object")]
    public void A_bad_model_entry_is_refused(string json, string message)
    {
        Assert.Contains(message, Assert.Throws<ManifestException>(() => ModManifest.Parse(json)).Message);
    }

    [Fact]
    public void A_mod_without_models_writes_no_models_list()
    {
        var m = ModManifest.Parse("""{"format":1,"id":"plain-mod","replace":[]}""");

        Assert.DoesNotContain("\"models\"", m.ToJson());
    }

    [Fact]
    public void A_species_entry_may_be_a_rig_without_a_file()
    {
        var m = ModManifest.Parse("""{"format":1,"id":"abc","models":[{"target":"Carcharodontosaurus","rig":{"Jaw":{"move":[0,0.1,0]}}}]}""");

        Assert.Equal("", m.Models[0].File);
        Assert.Single(m.Models[0].Rig!);
        Assert.DoesNotContain("\"file\"", m.ToJson());
        Assert.Equal(m.ToJson(), ModManifest.Parse(m.ToJson()).ToJson());
    }

    [Fact]
    public void A_species_entry_without_file_or_rig_is_refused_with_both_named()
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse("""{"format":1,"id":"abc","models":[{"target":"Carcharodontosaurus","rig":{}}]}"""));

        Assert.Contains("\"file\" or \"rig\"", ex.Message);
    }

    [Fact]
    public void A_skin_keeps_its_rig()
    {
        var json = """{"format":1,"id":"abc","skins":[{"id":"long","species":"AllosaurusAnax","male":{"diffuse":"a.png"},"rig":{"Neck.002":{"move":[0,0.2,0]}}}]}""";

        var m = ModManifest.Parse(json);

        Assert.Equal(new RigVector3(0, 0.2f, 0), m.Skins[0].Rig!["Neck.002"].Move);
        Assert.Contains("\"rig\"", m.ToJson());
        Assert.Equal(m.Skins[0].Rig!["Neck.002"].Move, ModManifest.Parse(m.ToJson()).Skins[0].Rig!["Neck.002"].Move);
    }

    [Fact]
    public void A_bad_skin_rig_skips_only_that_skin_in_game()
    {
        var json = """{"format":1,"id":"abc","skins":[{"id":"long","species":"AllosaurusAnax","male":{"diffuse":"a.png"},"rig":{"Jaw":{"scale":[0,1,1]}}}]}""";
        var skipped = new List<string>();

        var m = ModManifest.Parse(json, skipped);

        Assert.Empty(m.Skins);
        Assert.Contains(skipped, s => s.Contains("scale"));
    }
}
