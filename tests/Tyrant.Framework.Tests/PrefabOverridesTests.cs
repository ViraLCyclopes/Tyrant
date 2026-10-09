using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class PrefabOverridesTests
{
    private static string Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "mod");
        Directory.CreateDirectory(Path.Combine(dir, PrefabOverrides.Folder));
        return dir;
    }

    private const string Spino = """
        { "format": 1, "target": "Spinosaurus", "fields": [
          { "object": "", "component": "AnimalPhysics", "field": "inputParams.waterlineHeight", "value": 3.5 },
          { "object": "IK/LegL", "component": "FABRIKComponentAnimal#1", "field": "core.influence", "value": 0.8 },
          { "object": "", "component": "Animator", "field": "applyRootMotion", "value": false },
          { "object": "", "component": "Thing", "field": "offset", "value": { "x": 1, "y": 2, "z": 3 } } ] }
        """;

    [Fact]
    public void Parses_and_writes_back_every_value_kind()
    {
        var o = PrefabOverrides.Parse(Spino);

        Assert.Equal("Spinosaurus", o.Target);
        Assert.Equal(["inputParams.waterlineHeight", "core.influence", "applyRootMotion", "offset"], o.Fields.Select(f => f.Field));
        Assert.Equal(3.5, o.Fields[0].Value);
        Assert.Equal(false, o.Fields[2].Value);
        Assert.Equal(2.0, ((Dictionary<string, object?>)o.Fields[3].Value!)["y"]);
        var again = PrefabOverrides.Parse(o.ToJson());
        Assert.Equal(o.Fields.Select(f => f.Label), again.Fields.Select(f => f.Label));
        Assert.Equal("IK/LegL FABRIKComponentAnimal#1.core.influence", o.Fields[1].Label);
    }

    [Theory]
    [InlineData("""{ "format": 2, "target": "A", "fields": [] }""", "format")]
    [InlineData("""{ "format": 1, "fields": [] }""", "target")]
    [InlineData("""{ "format": 1, "target": "A", "fields": [ { "object": "", "field": "x", "value": 1 } ] }""", "component")]
    [InlineData("not json", "JSON")]
    public void A_broken_file_is_refused_with_why(string json, string word)
    {
        var ex = Assert.Throws<ManifestException>(() => PrefabOverrides.Parse(json));
        Assert.Contains(word, ex.Message);
    }

    [Fact]
    public void File_names_are_safe_and_lowercase()
    {
        Assert.Equal("spinosaurus.json", PrefabOverrides.FileName("Spinosaurus"));
        Assert.Equal("allosaurus anax.json", PrefabOverrides.FileName("Allosaurus Anax"));
        Assert.Equal("tyrant.my-mod.mirabilis.json", PrefabOverrides.FileName("tyrant.my-mod.mirabilis"));
        Assert.Equal("a_b.json", PrefabOverrides.FileName("a/b"));
    }

    [Fact]
    public void Reading_a_folder_skips_broken_files_and_second_files_for_a_target()
    {
        var dir = Dir();
        File.WriteAllText(Path.Combine(dir, "prefabs", "a.json"), Spino);
        File.WriteAllText(Path.Combine(dir, "prefabs", "b.json"), Spino); // same target again
        File.WriteAllText(Path.Combine(dir, "prefabs", "c.json"), "{ nope");
        var problems = new List<string>();

        var read = PrefabOverrides.ReadFolder(dir, problems);

        Assert.Equal(["a.json"], read.Select(r => r.File));
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("b.json") && p.Contains("Spinosaurus") && p.Contains("a.json"));
        Assert.Contains(problems, p => p.Contains("c.json"));
        Assert.Empty(PrefabOverrides.ReadFolder(Dir() + "-missing", null));
    }

    private static LoadedMod Mod(string id, string prefab)
    {
        var dir = Dir();
        File.WriteAllText(Path.Combine(dir, "prefabs", "x.json"), prefab);
        return new LoadedMod(ModManifest.Parse($$"""{"format":1,"id":"{{id}}"}"""), dir);
    }

    [Fact]
    public void The_later_mod_wins_per_field_and_the_clash_is_reported()
    {
        var a = Mod("mod-a", """{"format":1,"target":"Spinosaurus","fields":[{"object":"","component":"AnimalPhysics","field":"inputParams.waterlineHeight","value":1},{"object":"","component":"AnimalPhysics","field":"inputParams.terminalVelocity","value":9}]}""");
        var b = Mod("mod-b", """{"format":1,"target":"Spinosaurus","fields":[{"object":"","component":"AnimalPhysics","field":"inputParams.waterlineHeight","value":2}]}""");

        var table = PrefabTable.Build([a, b], out var messages);

        var fields = table.For("Spinosaurus")!;
        Assert.Equal(2.0, fields.Single(f => f.Field == "inputParams.waterlineHeight").Value);
        Assert.Equal(9.0, fields.Single(f => f.Field == "inputParams.terminalVelocity").Value);
        Assert.Null(table.For("Allosaurus"));
        Assert.True(table.HasAny);
        Assert.Equal(["AnimalPhysics.inputParams.waterlineHeight of Spinosaurus is set by both mod-a and mod-b; mod-b wins."], messages);
        var clash = Assert.Single(PrefabTable.Clashes([("mod-a", a.Directory), ("mod-b", b.Directory)]));
        Assert.Equal(("mod-a", "mod-b"), (clash.Loser, clash.Winner));
    }
}
