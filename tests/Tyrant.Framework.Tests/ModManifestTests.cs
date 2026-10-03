using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ModManifestTests
{
    private const string Full = """
        {
          "format": 1,
          "id": "red-spot-carcharo",
          "name": "Red-spot Carcharodontosaurus",
          "version": "1.0.0",
          "author": "you",
          "description": "Big red spots.",
          "requires": { "tyrant": ">=0.1" },
          "dependencies": ["base-pack"],
          "assembly": "RedSpot.dll",
          "replace": [
            {
              "texture": "T_carcharodontosaurus_alt1_male_D",
              "key": "Assets/Art/Animals/Dinosaurs/Carcharodontosaurus/Textures/T_carcharodontosaurus_alt1_male_D.png",
              "guid": "e3583acd2b3b5b14c875f42d110d97ce",
              "file": "textures/T_carcharodontosaurus_alt1_male_D.png"
            }
          ]
        }
        """;

    [Fact]
    public void Reads_every_field()
    {
        var m = ModManifest.Parse(Full);

        Assert.Equal((1, "red-spot-carcharo", "Red-spot Carcharodontosaurus", "1.0.0", "you"), (m.Format, m.Id, m.Name, m.Version, m.Author));
        Assert.Equal(("Big red spots.", ">=0.1", "RedSpot.dll"), (m.Description, m.RequiresTyrant, m.Assembly));
        Assert.Equal(new[] { "base-pack" }, m.Dependencies);
        var r = Assert.Single(m.Replace);
        Assert.Equal(("T_carcharodontosaurus_alt1_male_D", "e3583acd2b3b5b14c875f42d110d97ce", "textures/T_carcharodontosaurus_alt1_male_D.png"), (r.Texture, r.Guid, r.File));
        Assert.StartsWith("Assets/Art/", r.Key);
    }

    [Fact]
    public void Name_defaults_to_the_id_and_version_to_1_0_0()
    {
        var m = ModManifest.Parse("""{ "format": 1, "id": "tiny" }""");

        Assert.Equal(("tiny", "1.0.0"), (m.Name, m.Version));
        Assert.Empty(m.Replace);
    }

    [Theory]
    [InlineData("""{ "id": "tiny" }""", "format")]
    [InlineData("""{ "format": 2, "id": "tiny" }""", "newer Tyrant framework")]
    [InlineData("""{ "format": 1, "id": "Bad Id" }""", "\"id\"")]
    [InlineData("""{ "format": 1, "id": "tiny", "replace": [ { "file": "a.png" } ] }""", "\"texture\"")]
    [InlineData("""{ "format": 1, "id": "tiny", "replace": [ { "texture": "T_x" } ] }""", "\"file\"")]
    [InlineData("""[1]""", "object")]
    [InlineData("""{ "format": 1, "id": """, "not valid JSON")]
    public void Problems_are_explained(string json, string expected)
    {
        var ex = Assert.Throws<ManifestException>(() => ModManifest.Parse(json));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Written_manifest_reads_back_the_same()
    {
        var m = ModManifest.Parse(Full);

        var back = ModManifest.Parse(m.ToJson());

        Assert.Equal(m.ToJson(), back.ToJson());
        Assert.Equal("base-pack", Assert.Single(back.Dependencies));
    }

    [Theory]
    [InlineData("red-spot-carcharo", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("-abc", false)]
    [InlineData("abc-", false)]
    [InlineData("Abc", false)]
    [InlineData("a_c", false)]
    [InlineData(null, false)]
    public void Mod_ids_are_lowercase_letters_digits_and_dashes(string? id, bool valid)
    {
        Assert.Equal(valid, ModId.IsValid(id));
    }
}
