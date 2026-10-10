using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModPortraitsTests
{
    [Fact]
    public void Portraits_are_copied_or_written_marked_rendered_and_cleared_without_deleting_the_file()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var art = ModEditTests.Png(Path.Combine(ws.Dir, "art"), "male.png");
        var rendered = File.ReadAllBytes(art);

        var set = mod.SetPortraits("blue", new Dictionary<string, PortraitSource?>
        {
            ["adultMale"] = new(Png: art),
            ["infant"] = new(Data: rendered, Rendered: true),
        });

        Assert.Equal("skins/blue/portraits/adultMale.png", set["adultMale"]);
        Assert.Equal("skins/blue/portraits/infant.render.png", set["infant"]);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "portraits", "infant.render.png")));
        Assert.Equal(set, ModProject.Open(ws, "red-spot").Skin("blue").Portraits);
        Assert.Contains("skins/blue/portraits/adultMale.png", ModSharing.SharedFiles(mod));

        var cleared = mod.SetPortraits("blue", new Dictionary<string, PortraitSource?> { ["adultMale"] = null });
        Assert.False(cleared.ContainsKey("adultMale"));
        Assert.True(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "portraits", "adultMale.png"))); // undo puts the entry back
    }

    [Fact]
    public void An_unknown_slot_or_non_png_data_is_refused_and_nothing_is_written()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var art = ModEditTests.Png(Path.Combine(ws.Dir, "art"));

        var slot = Assert.Throws<TyrantException>(() => mod.SetPortraits("blue", new Dictionary<string, PortraitSource?> { ["adultMale"] = new(Png: art), ["adult"] = new(Png: art) }));
        Assert.Contains("adultMale", slot.Message);
        Assert.Throws<TyrantException>(() => mod.SetPortraits("blue", new Dictionary<string, PortraitSource?> { ["infant"] = new(Data: [1, 2, 3]) }));
        Assert.Null(ModProject.Open(ws, "red-spot").Skin("blue").Portraits);
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "skins", "blue", "portraits")));
    }

    [Fact]
    public void Removing_a_skin_with_its_files_removes_its_portraits_and_their_folder()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.SetPortraits("blue", new Dictionary<string, PortraitSource?> { ["adultMale"] = new(Png: ModEditTests.Png(Path.Combine(ws.Dir, "art"))) });

        mod.RemoveSkin("blue", deleteFiles: true);

        Assert.False(File.Exists(Path.Combine(mod.Dir, "skins", "blue", "portraits", "adultMale.png")));
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "skins", "blue", "portraits")));
    }

    [Fact]
    public void Species_portraits_go_to_animals_db_and_ship_with_the_mod()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([ModSpeciesTests.Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        var art = ModEditTests.Png(Path.Combine(ws.Dir, "art"));

        mod.SetSpeciesPortraits(entry.Id, new Dictionary<string, PortraitSource?> { ["adultFemale"] = new(Png: art) });

        var saved = Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath));
        Assert.Equal("portraits/new-animal/adultFemale.png", saved.Portraits["adultFemale"]);
        Assert.Contains("portraits/new-animal/adultFemale.png", ModSharing.SharedFiles(ModProject.Open(ws, mod.Id)));
    }

    [Fact]
    public void Shown_portraits_are_the_mods_own_then_the_base_skins()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        mod.SetPortraits("blue", new Dictionary<string, PortraitSource?> { ["adultMale"] = new(Data: File.ReadAllBytes(ModEditTests.Png(Path.Combine(ws.Dir, "art"))), Rendered: true) });
        var based = new VanillaSkin(0, "Default", new Dictionary<string, string>(), new Dictionary<string, string>())
        {
            Portraits = new Dictionary<string, string> { ["adultMale"] = "aaaa", ["adultFemale"] = "bbbb" },
        };

        var shown = ShownPortraits.ForSkin(mod, mod.Skin("blue"), based);

        Assert.Equal(PortraitSlots.All, shown.Select(s => s.Slot));
        Assert.Equal((true, true), (shown[0].Own, shown[0].Rendered));
        Assert.StartsWith("file:", shown[0].Source);
        Assert.Equal(("guid:bbbb", false), (shown[4].Source, shown[4].Own));
        Assert.Null(shown[8].Source); // the dump has no infant picture
    }

    [Fact]
    public void A_skin_on_this_mods_species_falls_back_to_the_species_portraits()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([ModSpeciesTests.Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        mod.SetSpeciesPortraits(entry.Id, new Dictionary<string, PortraitSource?> { ["infant"] = new(Png: ModEditTests.Png(Path.Combine(ws.Dir, "art"))) });
        var skin = new SkinEntry { Id = "spotty", Species = entry.Key(mod.Id), Male = new() { ["diffuse"] = "skins/spotty/male_D.png" } };

        var shown = ShownPortraits.ForSkin(mod, skin, based: null);

        Assert.Equal((false, true), (shown[8].Own, shown[8].Source!.EndsWith("infant.png")));
        Assert.True(shown[8].Inherited);
        Assert.False(shown[0].Inherited);
    }

    [Fact]
    public void A_missing_png_in_a_later_slot_writes_nothing_and_leaves_mod_json_unchanged()
    {
        var (game, ws, mod) = ModEditTests.Setup();
        using var _ = game;
        mod = ModEditTests.WithSkins(ws, mod);
        var art = ModEditTests.Png(Path.Combine(ws.Dir, "art"), "male.png");
        var before = File.ReadAllText(Path.Combine(mod.Dir, "mod.json"));

        var ex = Assert.Throws<TyrantException>(() => mod.SetPortraits("blue", new Dictionary<string, PortraitSource?>
        {
            ["adultMale"] = new(Png: art),
            ["infant"] = new(Png: Path.Combine(ws.Dir, "art", "missing.png")),
        }));

        Assert.Contains("does not exist", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(mod.Dir, "skins", "blue", "portraits")));
        Assert.Equal(before, File.ReadAllText(Path.Combine(mod.Dir, "mod.json")));
    }
}
