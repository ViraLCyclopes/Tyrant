using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class SpeciesFieldRowsTests
{
    private static AnimalField Row(string path, string kind, string value, bool editable = true) =>
        new() { Path = path, Kind = kind, Value = value, TemplateValue = value, Editable = editable };

    private static readonly AnimalField[] OldTemplate =
    [
        Row("/brain/social/adult/socialLeaderType", "text", "Either"),
        Row("/brain/social/adult/socialMaleCountRange", "object", "{}", false),
    ];

    private static readonly AnimalField[] NewTemplate =
    [
        Row("/brain/social/adult/socialLeaderType", "text", "Either"),
        Row("/brain/social/adult/socialMaleCountRange/x", "number", "1"),
        Row("/brain/social/adult/socialMaleCountRange/y", "number", "3"),
    ];

    [Fact]
    public void An_empty_object_row_the_dump_now_fills_gets_the_templates_rows_and_keeps_the_edits()
    {
        var stored = OldTemplate.Select(f => f.Copy()).ToList();
        stored[0].Value = "Matriarch";

        var merged = SpeciesDatabase.FillEmptyObjects(stored, NewTemplate);

        Assert.Equal(["/brain/social/adult/socialLeaderType", "/brain/social/adult/socialMaleCountRange/x", "/brain/social/adult/socialMaleCountRange/y"],
            merged.Select(f => f.Path));
        Assert.Equal("Matriarch", merged[0].Value);
        Assert.Equal("3", merged[2].Value);
        Assert.Equal("3", merged[2].TemplateValue);
        Assert.True(merged[2].Editable);
        Assert.NotSame(NewTemplate[1], merged[1]);
    }

    [Fact]
    public void Group_sizes_already_partly_stored_are_kept_and_only_the_missing_rows_are_added()
    {
        var stored = OldTemplate.Select(f => f.Copy()).Append(Row("/brain/social/adult/socialMaleCountRange/x", "number", "2")).ToList();

        var merged = SpeciesDatabase.FillEmptyObjects(stored, NewTemplate);

        Assert.Equal("2", merged.Single(f => f.Path == "/brain/social/adult/socialMaleCountRange/x").Value);
        Assert.Single(merged, f => f.Path == "/brain/social/adult/socialMaleCountRange/y");
        Assert.DoesNotContain(merged, f => f.Path == "/brain/social/adult/socialMaleCountRange");
    }

    [Theory]
    [InlineData("2.5")]
    [InlineData("-1")]
    [InlineData("1e9")]
    public void A_group_size_must_be_a_whole_number_the_game_can_hold(string value)
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var template = new SpeciesTemplateStats(null, new Dictionary<string, double>()) { Fields = NewTemplate };
        var entry = mod.AddSpecies([ModSpeciesTests.Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal", templateStats: template);

        var ex = Assert.Throws<TyrantException>(() => mod.SetSpecies(entry.Id, entry.Name, entry.Genus, entry.Epithet, entry.Cost, entry.Stats,
            new Dictionary<string, string> { ["/brain/social/adult/socialMaleCountRange/y"] = value }, template));
        Assert.Contains("whole number", ex.Message);
        mod.SetSpecies(entry.Id, entry.Name, entry.Genus, entry.Epithet, entry.Cost, entry.Stats,
            new Dictionary<string, string> { ["/brain/social/adult/socialMaleCountRange/y"] = "6" }, template);
    }

    [Fact]
    public void Without_new_rows_the_stored_rows_stay_as_they_are()
    {
        var stored = OldTemplate.Select(f => f.Copy()).ToList();
        Assert.Equal(stored.Select(f => f.Path), SpeciesDatabase.FillEmptyObjects(stored, OldTemplate).Select(f => f.Path));
        Assert.Equal(stored.Select(f => f.Path), SpeciesDatabase.FillEmptyObjects(stored, null).Select(f => f.Path));
    }

    [Fact]
    public void A_species_saved_before_the_dump_fix_can_edit_the_new_rows()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([ModSpeciesTests.Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal",
            templateStats: new SpeciesTemplateStats(null, new Dictionary<string, double>()) { Fields = OldTemplate });

        mod.SetSpecies(entry.Id, entry.Name, entry.Genus, entry.Epithet, entry.Cost, entry.Stats,
            new Dictionary<string, string> { ["/brain/social/adult/socialMaleCountRange/y"] = "6" },
            new SpeciesTemplateStats(null, new Dictionary<string, double>()) { Fields = NewTemplate });

        var saved = Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath)).Fields;
        Assert.Equal("6", saved.Single(f => f.Path == "/brain/social/adult/socialMaleCountRange/y").Value);
        Assert.Equal("1", saved.Single(f => f.Path == "/brain/social/adult/socialMaleCountRange/x").Value);
        Assert.DoesNotContain(saved, f => f.Path == "/brain/social/adult/socialMaleCountRange");
    }
}
