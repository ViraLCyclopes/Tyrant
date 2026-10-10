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

    [Fact]
    public void Texture_compression_round_trips_and_is_left_out_when_default()
    {
        var plain = ModManifest.Parse("""{"format":1,"id":"red-spot"}""");
        Assert.Equal(TextureCompression.Auto, plain.Textures.Compression);
        Assert.Empty(plain.Textures.Overrides);
        Assert.DoesNotContain("\"textures\"", plain.ToJson());

        var json = """{"format":1,"id":"red-spot","textures":{"compression":"bc7","overrides":{"skins/blue/male_N.png":"auto","skins/blue/male_D.png":"bc7"}}}""";
        var parsed = ModManifest.Parse(json);
        Assert.Equal(TextureCompression.Bc7, parsed.Textures.Compression);
        Assert.Equal(TextureCompression.Auto, parsed.Textures.For("SKINS/blue/male_N.png")); // paths match in any case
        Assert.Equal(TextureCompression.Bc7, parsed.Textures.For("textures/other.png")); // the default

        var again = ModManifest.Parse(parsed.ToJson());
        Assert.Equal(TextureCompression.Bc7, again.Textures.Compression);
        Assert.Equal(2, again.Textures.Overrides.Count);
        Assert.True(parsed.ToJson().IndexOf("male_D", StringComparison.Ordinal) < parsed.ToJson().IndexOf("male_N", StringComparison.Ordinal)); // sorted, stable diffs
    }

    [Theory]
    [InlineData("""{"compression":"BC7"}""", "textures.compression")]
    [InlineData("""{"compression":"dxt1"}""", "textures.compression")]
    [InlineData("""{"overrides":{"a.png":"fast"}}""", "a.png")]
    [InlineData("""{"overrides":["a.png"]}""", "overrides")]
    [InlineData("\"bc7\"", "textures")]
    public void A_bad_textures_block_is_an_error_for_tyrant_and_ignored_in_game(string block, string named)
    {
        var json = $$"""{"format":1,"id":"red-spot","textures":{{block}}}""";
        Assert.Contains(named, Assert.Throws<ManifestException>(() => ModManifest.Parse(json)).Message);

        var messages = new List<string>();
        var inGame = ModManifest.Parse(json, messages);
        Assert.Equal(TextureCompression.Auto, inGame.Textures.Compression);
        Assert.Empty(inGame.Textures.Overrides);
        Assert.Contains(messages, m => m.Contains("\"textures\" was ignored"));
    }
}
