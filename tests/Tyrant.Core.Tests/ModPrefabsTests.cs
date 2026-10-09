using System.Text.Json;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModPrefabsTests
{
    private static (FakeGame Game, Workspace Ws, ModProject Mod) Setup()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(TestTemp.Root, Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));
        return (game, ws, ModProject.Create(ws, "red-spot", "Red spot", null));
    }

    private static readonly IReadOnlyList<PrefabFieldInfo> Tree = [
        new("", "AnimalPhysics", "inputParams.waterlineHeight", "number", "2.75", true),
        new("", "AnimalPhysics", "inputParams.mode", "enum", "0", true, ["Walk", "Swim"]),
        new("", "AnimalPhysics", "target", "reference", "{}", false),
    ];

    private static JsonElement V(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Setting_a_field_writes_the_targets_file_and_resetting_the_last_one_removes_it()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;

        mod.SetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.waterlineHeight", V("3.5"), Tree);
        mod.SetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.mode", V("\"Swim\""), Tree);
        mod.SetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.waterlineHeight", V("4"), Tree); // replaces

        var (file, overrides) = Assert.Single(ModProject.Open(ws, "red-spot").PrefabTargets());
        Assert.Equal("spinosaurus.json", file);
        Assert.Equal([4.0, (object)"Swim"], overrides.Fields.Select(f => f.Value));
        Assert.DoesNotContain("prefab", File.ReadAllText(Path.Combine(mod.Dir, "mod.json")));

        mod.ResetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.mode");
        mod.ResetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.waterlineHeight");
        Assert.Empty(mod.PrefabTargets());
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "prefabs")));
    }

    [Theory]
    [InlineData("inputParams.waterlineHeight", "\"deep\"", "needs a finite number")]
    [InlineData("target", "1", "cannot be changed")]
    [InlineData("inputParams.nope", "1", "has no field")]
    public void A_bad_value_or_unknown_field_is_refused_and_nothing_is_written(string field, string json, string word)
    {
        var (game, _, mod) = Setup();
        using var __ = game;

        var ex = Assert.Throws<TyrantException>(() => mod.SetPrefabField("Spinosaurus", "", "AnimalPhysics", field, V(json), Tree));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains(word, ex.Message);
        Assert.Empty(mod.PrefabTargets());
    }

    [Fact]
    public void Revision_covers_prefab_files_and_a_snapshot_restores_them()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var before = mod.Revision();
        var manifest = File.ReadAllText(Path.Combine(mod.Dir, "mod.json"));

        mod.SetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.waterlineHeight", V("3.5"), Tree);
        Assert.NotEqual(before, mod.Revision());

        ModProject.RestoreSnapshot(ws, "red-spot", manifest, new Dictionary<string, string>(), mod.Revision());
        Assert.Empty(ModProject.Open(ws, "red-spot").PrefabTargets());
        Assert.Equal(before, ModProject.Open(ws, "red-spot").Revision());

        var file = """{"format":1,"target":"Allosaurus","fields":[]}""";
        ModProject.RestoreSnapshot(ws, "red-spot", manifest, new Dictionary<string, string> { ["allosaurus.json"] = file }, before);
        Assert.Equal("Allosaurus", Assert.Single(ModProject.Open(ws, "red-spot").PrefabTargets()).Overrides.Target);
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("notes.txt")]
    public void A_snapshot_with_an_unsafe_file_name_is_refused(string name)
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var manifest = File.ReadAllText(Path.Combine(mod.Dir, "mod.json"));

        var ex = Assert.Throws<TyrantException>(() => ModProject.RestoreSnapshot(ws, "red-spot", manifest, new Dictionary<string, string> { [name] = "{}" }, mod.Revision()));

        Assert.Equal(TyrantErrorCode.ModInvalid, ex.Code);
    }

    [Fact]
    public void A_broken_prefab_file_is_never_overwritten()
    {
        var (game, _, mod) = Setup();
        using var _ = game;
        Directory.CreateDirectory(Path.Combine(mod.Dir, "prefabs"));
        var path = Path.Combine(mod.Dir, "prefabs", "spinosaurus.json");
        File.WriteAllText(path, "{ not json");

        var set = Assert.Throws<TyrantException>(() => mod.SetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.waterlineHeight", V("3.5"), Tree));
        Assert.Equal(TyrantErrorCode.ModInvalid, set.Code);
        Assert.Contains("prefabs/spinosaurus.json cannot be read", set.Message);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.ResetPrefabField("Spinosaurus", "", "AnimalPhysics", "x")).Code);
        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() => mod.RemovePrefabTarget("Spinosaurus")).Code);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public void A_snapshot_puts_back_an_unparseable_file_exactly()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var manifest = File.ReadAllText(Path.Combine(mod.Dir, "mod.json"));
        const string broken = "{ not json\r\n";

        ModProject.RestoreSnapshot(ws, "red-spot", manifest, new Dictionary<string, string> { ["spinosaurus.json"] = broken }, mod.Revision());

        Assert.Equal(broken, File.ReadAllText(Path.Combine(mod.Dir, "prefabs", "spinosaurus.json")));
    }

    [Theory]
    [InlineData("a|b.json")]
    [InlineData("")]
    public void A_snapshot_with_an_invalid_file_name_is_refused(string name)
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var manifest = File.ReadAllText(Path.Combine(mod.Dir, "mod.json"));

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() =>
            ModProject.RestoreSnapshot(ws, "red-spot", manifest, new Dictionary<string, string> { [name] = "{}" }, mod.Revision())).Code);
    }

    [Fact]
    public void A_snapshot_with_names_differing_only_by_case_is_refused()
    {
        var (game, ws, mod) = Setup();
        using var _ = game;
        var manifest = File.ReadAllText(Path.Combine(mod.Dir, "mod.json"));

        Assert.Equal(TyrantErrorCode.ModInvalid, Assert.Throws<TyrantException>(() =>
            ModProject.RestoreSnapshot(ws, "red-spot", manifest, new Dictionary<string, string> { ["a.json"] = "{}", ["A.json"] = "{}" }, mod.Revision())).Code);
    }

    [Fact]
    public void Export_carries_the_prefab_files()
    {
        var (game, _, mod) = Setup();
        using var __ = game;
        mod.SetPrefabField("Spinosaurus", "", "AnimalPhysics", "inputParams.waterlineHeight", V("3.5"), Tree);

        Assert.Contains("prefabs/spinosaurus.json", ModSharing.SharedFiles(mod));
    }
}
