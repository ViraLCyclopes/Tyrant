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
}
