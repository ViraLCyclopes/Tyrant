using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModSpeciesTextTests
{
    private static (FakeGame Game, ModProject Mod, SpeciesEntry Entry) NewSpecies()
    {
        var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([ModSpeciesTests.Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        return (game, mod, entry);
    }

    private static string Read(ModProject mod, string code, string term) => File.ReadAllText(AnimalLocalization.FileFor(mod.Dir, code, term));

    [Fact]
    public void Texts_of_a_species_list_every_language_and_field_with_missing_files_empty()
    {
        var (game, mod, entry) = NewSpecies();
        using var _ = game;
        File.Delete(AnimalLocalization.FileFor(mod.Dir, "de", entry.Localization["socialInfo"]));

        var texts = mod.SpeciesTexts("new-animal");

        Assert.Equal(AnimalLocalization.Codes, texts.Keys);
        Assert.Equal("Animal", texts["en-US"]["name"]);
        Assert.Equal("Animal is a custom species based on Allosaurus Anax.", texts["en-US"]["description"]);
        Assert.Equal("", texts["fr"]["description"]);
        Assert.Equal("", texts["de"]["socialInfo"]);
        Assert.Equal(AnimalText.Keys, texts["fr"].Keys);
    }

    [Fact]
    public void Setting_texts_writes_only_the_given_files_and_an_empty_translation_falls_back_to_english()
    {
        var (game, mod, entry) = NewSpecies();
        using var _ = game;
        File.WriteAllText(AnimalLocalization.FileFor(mod.Dir, "es", entry.Localization["description"]), "Texto");
        var description = "A big one.\n\n<b>Very</b> big, ünïcode 🦖.";

        mod.SetSpeciesTexts("new-animal", new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-US"] = new Dictionary<string, string> { ["description"] = description, ["characterNotes"] = "Grumpy." },
            ["fr"] = new Dictionary<string, string> { ["description"] = "Un grand.", ["name"] = "Ultimasaure" },
            ["es"] = new Dictionary<string, string> { ["description"] = "" },
        });

        Assert.Equal(description, Read(mod, "en-US", entry.Localization["description"]));
        Assert.Equal("Grumpy.", Read(mod, "en-US", entry.Localization["characterNotes"]));
        Assert.Equal("Un grand.", Read(mod, "fr", entry.Localization["description"]));
        Assert.Equal("", Read(mod, "es", entry.Localization["description"]));
        Assert.Equal("", Read(mod, "en-US", entry.Localization["socialInfo"]));
        var loaded = AnimalDatabaseFile.Read(mod.DatabasePath);
        AnimalLocalization.Load(mod.Dir, loaded);
        Assert.Equal("Ultimasaure", AnimalText.Resolve(loaded[0].Texts, "name", "French"));
        Assert.Equal("Animal", AnimalText.Resolve(loaded[0].Texts, "name", "Spanish"));
        Assert.Equal(description, AnimalText.Resolve(loaded[0].Texts, "description", "Spanish"));
    }

    [Fact]
    public void Setting_texts_refuses_unknown_species_languages_fields_and_the_english_name()
    {
        var (game, mod, entry) = NewSpecies();
        using var _ = game;
        static Dictionary<string, IReadOnlyDictionary<string, string>> One(string code, string slot) =>
            new() { [code] = new Dictionary<string, string> { [slot] = "x" } };

        Assert.Throws<TyrantException>(() => mod.SetSpeciesTexts("missing", One("fr", "description")));
        Assert.Contains("Unknown language", Assert.Throws<TyrantException>(() => mod.SetSpeciesTexts("new-animal", One("xx", "description"))).Message);
        Assert.Contains("Unknown text", Assert.Throws<TyrantException>(() => mod.SetSpeciesTexts("new-animal", One("fr", "biography"))).Message);
        Assert.Contains("Display name", Assert.Throws<TyrantException>(() => mod.SetSpeciesTexts("new-animal", One("en-US", "name"))).Message);
        Assert.Equal("", Read(mod, "fr", entry.Localization["description"]));
    }

    [Fact]
    public void Setting_text_for_a_field_without_a_key_gives_it_the_default_key()
    {
        var (game, mod, entry) = NewSpecies();
        using var _ = game;
        var keys = entry.Localization.Where(p => p.Key != "socialInfo").ToDictionary(p => p.Key, p => p.Value);
        mod.SetSpecies("new-animal", entry.Name, entry.Genus, entry.Epithet, entry.Cost, entry.Stats, localization: keys);

        mod.SetSpeciesTexts("new-animal", new Dictionary<string, IReadOnlyDictionary<string, string>>
        { ["en-US"] = new Dictionary<string, string> { ["socialInfo"] = "Lives alone." } });

        var saved = AnimalDatabaseFile.Read(mod.DatabasePath).Single();
        Assert.Equal("new-animal/socialInfo", saved.Localization["socialInfo"]);
        Assert.Equal("Lives alone.", Read(mod, "en-US", "new-animal/socialInfo"));
        Assert.Equal("", Read(mod, "fr", "new-animal/socialInfo"));
    }

    [Fact]
    public void A_blank_translation_is_saved_empty_so_the_game_uses_english()
    {
        var (game, mod, entry) = NewSpecies();
        using var _ = game;
        mod.SetSpeciesTexts("new-animal", new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["fr"] = new Dictionary<string, string> { ["description"] = "  \n " },
            ["en-US"] = new Dictionary<string, string> { ["characterNotes"] = " " },
        });
        Assert.Equal("", Read(mod, "fr", entry.Localization["description"]));
        Assert.Equal(" ", Read(mod, "en-US", entry.Localization["characterNotes"]));
    }

    [Fact]
    public void Texts_of_an_older_mod_with_combined_language_files_show_what_the_game_shows()
    {
        var (game, mod, entry) = NewSpecies();
        using var _ = game;
        File.Delete(AnimalLocalization.FileFor(mod.Dir, "fr", entry.Localization["description"]));
        File.WriteAllText(Path.Combine(mod.Dir, "localization", "fr.txt"), "[" + entry.Localization["description"] + "]\nAncien texte.");

        Assert.Equal("Ancien texte.", mod.SpeciesTexts("new-animal")["fr"]["description"]);
    }

    [Fact]
    public void Renaming_the_species_updates_its_english_name_file()
    {
        var (game, mod, entry) = NewSpecies();
        using var _ = game;
        mod.SetSpecies("new-animal", "Ultimasaurus", entry.Genus, entry.Epithet, entry.Cost, entry.Stats);
        Assert.Equal("Ultimasaurus", Read(mod, "en-US", entry.Localization["name"]));
        Assert.Equal("", Read(mod, "fr", entry.Localization["name"]));
    }
}
