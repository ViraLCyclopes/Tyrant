using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class SaveTargetsTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static LoadedMod Mod(string id, params (string Path, string Json)[] files)
    {
        var dir = Path.Combine(TempDir(), id);
        Directory.CreateDirectory(dir);
        foreach (var (path, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, path))!);
            File.WriteAllText(Path.Combine(dir, path), content);
        }
        var manifest = ModManifest.Parse("{\"format\":1,\"id\":\"" + id + "\"}");
        MaterialFolders.Load(manifest, dir, new List<string>());
        return new LoadedMod(manifest, dir);
    }

    private static (string, string) SpeciesFile(string species) =>
        ($"materials/species/{species}/materials.json",
            "{\"format\":1,\"species\":\"" + species + "\",\"materials\":{\"Body\":{\"settings\":{\"_Smoothness\":0.3}}}}");

    private static (string, string) VanillaFile(string species, string skin) =>
        ($"materials/vanilla-skins/{species}-{skin}/materials.json",
            "{\"format\":1,\"species\":\"" + species + "\",\"skin\":\"" + skin + "\",\"materials\":{\"Body\":{\"settings\":{\"_Smoothness\":0.3}}}}");

    [Fact]
    public void Saves_go_to_the_mod_whose_block_wins()
    {
        var table = MaterialTable.Build([Mod("mod-a", SpeciesFile("Titanoboa"), VanillaFile("Titanoboa", "Skin A")),
            Mod("mod-b", SpeciesFile("Titanoboa"), VanillaFile("Titanoboa", "Skin A"))], out _);

        var species = SaveTargets.For(LookScope.Species, "Titanoboa", null, null, false, null, table, "Body");
        Assert.Equal(SaveTargetKind.Mod, species.Kind);
        Assert.Equal("mod-b", species.ModId);
        Assert.Equal(new MaterialOwner(MaterialOwnerKind.Species, null, "Titanoboa", null), species.Owner);

        var skin = SaveTargets.For(LookScope.Skin, "Titanoboa", null, "Titanoboa/Skin A", false, null, table, "Body");
        Assert.Equal("mod-b", skin.ModId);
        Assert.Equal(new MaterialOwner(MaterialOwnerKind.VanillaSkin, null, "Titanoboa", "Skin A"), skin.Owner);

        var modSkin = SaveTargets.For(LookScope.Skin, "Titanoboa", ("mod-a", "mine"), null, false, null, table, "Body");
        Assert.Equal("mod-a", modSkin.ModId);
        Assert.Equal(new MaterialOwner(MaterialOwnerKind.Skin, "mine", null, null), modSkin.Owner);

        Assert.Equal(SaveTargetKind.AnimalLooks, SaveTargets.For(LookScope.Animal, "Titanoboa", null, null, false, null, table, "Body").Kind);
    }

    [Fact]
    public void Vanilla_skin_with_no_mod_creates_vanilla_looks()
    {
        var table = MaterialTable.Build([], out _);

        var skin = SaveTargets.For(LookScope.Skin, "Titanoboa", null, "Titanoboa/Skin A", false, null, table, "Body");
        Assert.Equal(SaveTargetKind.CreateVanillaLooks, skin.Kind);
        Assert.Null(skin.ModId);
        Assert.Equal(MaterialOwnerKind.VanillaSkin, skin.Owner!.Kind);
        Assert.Equal(SaveTargetKind.CreateVanillaLooks, SaveTargets.For(LookScope.Species, "Titanoboa", null, null, false, null, table, "Body").Kind);
    }

    [Fact]
    public void A_custom_species_saves_into_its_own_mod()
    {
        var table = MaterialTable.Build([Mod("mod-a", SpeciesFile("Mine"))], out _);

        var target = SaveTargets.For(LookScope.Species, "Mine", null, null, true, "mod-c", table, "Body");
        Assert.Equal(SaveTargetKind.Mod, target.Kind);
        Assert.Equal("mod-c", target.ModId);
    }

    [Fact]
    public void Vanilla_looks_mod_json_is_valid_and_created_once()
    {
        var mods = TempDir();
        var folder = VanillaLooks.EnsureMod(mods);
        var manifest = ModManifest.Parse(File.ReadAllText(Path.Combine(folder, "mod.json")));
        Assert.Equal("vanilla-looks", manifest.Id);
        Assert.Equal("Vanilla Looks", manifest.Name);

        File.WriteAllText(Path.Combine(folder, "mod.json"), """{"format":1,"id":"vanilla-looks","name":"Vanilla Looks","version":"2.0.0"}""");
        Assert.Equal(folder, VanillaLooks.EnsureMod(mods));
        Assert.Contains("2.0.0", File.ReadAllText(Path.Combine(folder, "mod.json")));
    }
}
