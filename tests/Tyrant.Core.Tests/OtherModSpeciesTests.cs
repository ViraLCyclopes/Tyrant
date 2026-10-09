using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class OtherModSpeciesTests
{
    private static SpeciesSkins Template() => new("Allosaurus Anax", false,
        [new VanillaSkin(0, "Default", new Dictionary<string, string> { ["diffuse"] = SkinDumps.MaleDiffuse }, new Dictionary<string, string> { ["diffuse"] = SkinDumps.FemaleDiffuse }),
         new VanillaSkin(1, "Albino", new Dictionary<string, string>(), new Dictionary<string, string>())]);

    private static Workspace NewWorkspace(FakeGame game) => Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));

    [Fact]
    public void Lists_other_workspace_mods_species_but_not_this_mods()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        var a = ModProject.Create(ws, "mod-a", "Mod A", null);
        a.AddSpecies([Template()], "mirabilis", "Allosaurus Anax", "Mirabilis", "Example", "mirabilis", startingSkin: 1);
        var b = ModProject.Create(ws, "mod-b", null, null);
        Directory.CreateDirectory(Path.Combine(ModProject.RootOf(ws), "broken"));
        File.WriteAllText(Path.Combine(ModProject.RootOf(ws), "broken", "mod.json"), "{ not json");

        var others = OtherModSpecies.Load(ws, [], b.Id);

        var mirabilis = Assert.Single(others.Species);
        Assert.Equal(("tyrant.mod-a.mirabilis", "mod-a", "Mod A", 1), (mirabilis.Key, mirabilis.ModId, mirabilis.ModName, mirabilis.Entry.StartingSkin));
        Assert.Equal(new OtherModState("Mod A", true, false, false), others.Mods["mod-a"]);
        Assert.False(others.Mods.ContainsKey("mod-b"));
        Assert.Empty(OtherModSpecies.Load(ws, [], a.Id).Species);
    }

    [Fact]
    public void A_workspace_mod_that_cannot_be_read_is_skipped()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        var a = ModProject.Create(ws, "mod-a", "Mod A", null);
        a.AddSpecies([Template()], "mirabilis", "Allosaurus Anax", "Mirabilis", "Example", "mirabilis");
        var b = ModProject.Create(ws, "mod-b", null, null);
        using var locked = new FileStream(Path.Combine(a.Dir, ModManifest.FileName), FileMode.Open, FileAccess.Read, FileShare.None);

        var others = OtherModSpecies.Load(ws, [], b.Id);

        Assert.Empty(others.Species);
        Assert.False(others.Mods.ContainsKey("mod-a"));
    }

    [Fact]
    public void Installed_mods_count_as_found_and_the_workspace_copy_wins()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        var a = ModProject.Create(ws, "mod-a", "Mod A", null);
        a.AddSpecies([Template()], "mirabilis", "Allosaurus Anax", "Mirabilis", "Example", "mirabilis");
        var installedDir = Path.Combine(game.LibraryRoot, "installed", "mod-c");
        Directory.CreateDirectory(installedDir);
        File.WriteAllText(Path.Combine(installedDir, ModManifest.FileName), """{ "format": 1, "id": "mod-c", "name": "Mod C", "version": "1.0.0" }""");
        AnimalDatabaseFile.Save(Path.Combine(installedDir, AnimalDatabaseFile.FileName),
            [new SpeciesEntry { Id = "rex", Base = "Allosaurus Anax", Name = "Rex", Genus = "R", Epithet = "rex" }]);
        var installed = new List<InstalledMod>
        {
            new("mod-a", "Mod A (installed)", "1.0.0", 0, 0, true, a.Dir, null),
            new("mod-c", "Mod C", "1.0.0", 0, 0, false, installedDir, null),
        };

        var others = OtherModSpecies.Load(ws, installed, "mod-b");

        Assert.Equal(["tyrant.mod-a.mirabilis", "tyrant.mod-c.rex"], others.Species.Select(s => s.Key).Order());
        Assert.Equal(new OtherModState("Mod A", true, true, true), others.Mods["mod-a"]);
        Assert.Equal(new OtherModState("Mod C", false, true, false), others.Mods["mod-c"]);
    }

    [Fact]
    public void Species_use_names_only_other_mods()
    {
        var manifest = new ModManifest { Id = "mod-b", Name = "B" };
        manifest.Skins.Add(new SkinEntry { Id = "one", Species = "tyrant.mod-a.mirabilis", Name = "One", Base = "0" });
        manifest.Skins.Add(new SkinEntry { Id = "two", Species = "tyrant.mod-a.mirabilis", Name = "Two", Base = "0" });
        manifest.Skins.Add(new SkinEntry { Id = "own", Species = "tyrant.mod-b.rex", Name = "Own", Base = "0" });
        manifest.Skins.Add(new SkinEntry { Id = "vanilla", Species = "Allosaurus Anax", Name = "V", Base = "0" });

        Assert.Equal([new SpeciesUseCount("mod-a", 2)], SpeciesUse.Of(manifest));
        Assert.Equal("mod-a", SpeciesUse.ModOf("tyrant.mod-a.mirabilis"));
        Assert.Null(SpeciesUse.ModOf("Allosaurus Anax"));
        Assert.Null(SpeciesUse.ModOf("tyrant.Bad.x"));
    }

    [Fact]
    public void Add_skin_targets_another_mods_species_with_base_0()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        var a = ModProject.Create(ws, "mod-a", "Mod A", null);
        a.AddSpecies([Template()], "mirabilis", "Allosaurus Anax", "Mirabilis", "Example", "mirabilis");
        var b = ModProject.Create(ws, "mod-b", null, null);
        var others = OtherModSpecies.Load(ws, [], b.Id).Species;

        var skin = b.AddSkin(ws, new GameInstall(game.Root, null), SkinDumps.Index(), new FakeAssetReader(), [Template()], "tyrant.mod-a.mirabilis",
            "Blue", null, new SkinTemplateOptions(true, true, false), others: others);

        Assert.Equal(("tyrant.mod-a.mirabilis", "0"), (skin.Species, skin.Base));
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => b.AddSkin(ws, new GameInstall(game.Root, null), SkinDumps.Index(), new FakeAssetReader(), [Template()],
            "tyrant.mod-a.mirabilis", "Green", null, new SkinTemplateOptions(true, true, false)));
    }
}
