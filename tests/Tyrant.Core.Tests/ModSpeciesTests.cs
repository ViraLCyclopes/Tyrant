using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModSpeciesTests
{
    [Fact]
    public void Unity_project_is_outside_shared_mod_and_reopening_preserves_authored_files()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var template = Path.Combine(ws.Dir, "template");
        Directory.CreateDirectory(Path.Combine(template, "ProjectSettings"));
        File.WriteAllText(Path.Combine(template, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.62f3");
        File.WriteAllText(Path.Combine(template, "README.md"), "Template instructions");
        var project = UnitySpeciesProject.Create(ws, "animal-pack", template);
        Assert.Equal(Path.Combine(ws.Dir, "authoring", "animal-pack", "Unity"), project);
        File.WriteAllText(Path.Combine(project, "README.md"), "Author's own instructions");
        Assert.Equal(project, UnitySpeciesProject.Create(ws, "animal-pack", template));
        Assert.Equal("Author's own instructions", File.ReadAllText(Path.Combine(project, "README.md")));
    }

    [Fact]
    public void Prefab_bundle_is_preserved_by_species_edits_and_included_in_exports()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        var bundle = Path.Combine(mod.Dir, "prefabs", "animal.bundle");
        Directory.CreateDirectory(Path.GetDirectoryName(bundle)!);
        File.WriteAllBytes(bundle, System.Text.Encoding.ASCII.GetBytes("UnityFS\0test payload"));
        // Simulate a reference supplied by an external SQLite editor; this tests preservation/export,
        // not Unity parsing (the fixture is deliberately not a real game bundle).
        entry.Prefab = new SpeciesPrefab { Bundle = "prefabs/animal.bundle", Asset = "assets/animals/animal.prefab" };
        AnimalDatabaseFile.Save(mod.DatabasePath, mod.Manifest.Species);
        var reopened = ModProject.Open(ws, mod.Id);
        reopened.SetSpecies(entry.Id, "New name", "Example", "animal", 2000, new Dictionary<string, double>());
        var saved = Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath));
        Assert.Equal("assets/animals/animal.prefab", saved.Prefab!.Asset);
        Assert.Contains(saved.Prefab.Bundle, ModSharing.SharedFiles(reopened));
        File.Delete(Path.Combine(mod.Dir, saved.Prefab.Bundle));
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => ModSharing.SharedFiles(reopened));
        reopened.SetSpeciesPrefab(entry.Id, null, null);
        Assert.Null(Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath)).Prefab);
    }

    [Fact]
    public void Attaching_a_model_as_a_bundle_keeps_the_previous_reference()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        var bundle = Path.Combine(ws.Dir, "animal.bundle");
        File.WriteAllText(bundle, "This is not a Unity bundle.");
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => mod.SetSpeciesPrefab(entry.Id, bundle, "assets/animal.prefab"));
        Assert.Null(Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath)).Prefab);
    }

    [Fact]
    public void Creation_copies_template_cost_and_stats_with_explicit_values_taking_precedence()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var defaults = new SpeciesTemplateStats(31762, new Dictionary<string, double> { ["adultMassKg"] = 7040.5, ["maxAgeYears"] = 45 })
        { Fields = [new AnimalField { Path = "/diet", Kind = "text", Value = "Carnivore", TemplateValue = "Carnivore", Editable = true }] };
        mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal", templateStats: defaults);
        var copied = Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath));
        Assert.Equal(31762, copied.Cost);
        Assert.Equal(7040.5, copied.Stats["adultMassKg"]);
        Assert.Equal("Carnivore", Assert.Single(copied.Fields).Value);
        mod.AddSpecies([Template()], "another-animal", "Allosaurus Anax", "Other", "Example", "other", 50000,
            new Dictionary<string, double> { ["adultMassKg"] = 8000 }, defaults);
        var custom = AnimalDatabaseFile.Read(mod.DatabasePath).Single(a => a.Id == "another-animal");
        Assert.Equal(50000, custom.Cost);
        Assert.Equal(8000, custom.Stats["adultMassKg"]);
        Assert.Equal(45, custom.Stats["maxAgeYears"]);
    }

    internal static SpeciesSkins Template(bool vivarium = false) => new("Allosaurus Anax", vivarium,
        [new VanillaSkin(0, "Default", new Dictionary<string, string>(), new Dictionary<string, string>()),
         new VanillaSkin(1, "Albino", new Dictionary<string, string>(), new Dictionary<string, string>())]) { PrefabGuid = "template-prefab" };

    [Fact]
    public void A_species_starts_with_one_chosen_template_skin_and_keeps_it_through_edits()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        Assert.Throws<TyrantException>(() => mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal", startingSkin: 5));
        var entry = mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal", startingSkin: 1);

        var row = ModProject.WithSpecies([Template()], mod.Manifest).Single(s => s.SpeciesId == entry.Key(mod.Id));
        var only = Assert.Single(row.Skins);
        Assert.Equal((0, "Albino"), (only.Index, only.Name));

        mod.SetSpeciesStartingSkin([Template()], entry.Id, 0, " Mirabilis ");
        var png = Path.Combine(ws.Dir, "swatch.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));
        mod.SetSpeciesSwatch(entry.Id, png);
        var reopened = ModProject.Open(ws, mod.Id);
        reopened.SetSpecies(entry.Id, "New name", "Example", "animal", 2000, new Dictionary<string, double>());

        var saved = Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath));
        Assert.Equal((0, "Mirabilis", "icons/new-animal.swatch.png"), (saved.StartingSkin, saved.StartingSkinName, saved.Swatch));
        Assert.Contains("icons/new-animal.swatch.png", ModSharing.SharedFiles(reopened));
        Assert.Equal("Mirabilis", ModProject.WithSpecies([Template()], reopened.Manifest).Single(s => s.SpeciesId == entry.Key(mod.Id)).Skins[0].Name);
        Assert.Throws<TyrantException>(() => reopened.SetSpeciesStartingSkin([Template()], entry.Id, 7, null));
        reopened.SetSpeciesStartingSkin([Template()], entry.Id, 0, "  ");
        Assert.Null(Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath)).StartingSkinName);
        reopened.SetSpeciesSwatch(entry.Id, null);
        Assert.Null(Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath)).Swatch);
    }

    [Fact]
    public void Icon_and_language_files_ship_with_database_keys_and_survive_other_species_edits()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        var png = Path.Combine(ws.Dir, "portrait.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWWQAAAAASUVORK5CYII="));
        mod.SetSpeciesIcon(entry.Id, png);
        var reopened = ModProject.Open(ws, mod.Id);
        reopened.SetSpecies(entry.Id, "New name", "Example", "animal", 2000, new Dictionary<string, double>());
        var saved = Assert.Single(AnimalDatabaseFile.Read(mod.DatabasePath));
        Assert.Equal("icons/new-animal.png", saved.Icon);
        Assert.Equal(entry.Localization, saved.Localization);
        var files = ModSharing.SharedFiles(reopened);
        Assert.Contains("localization/en-US/new-animal/description.txt", files);
        Assert.Contains("localization/en-US/new-animal/name.txt", files);
        Assert.Contains("icons/new-animal.png", files);
        var revision = reopened.Revision();
        File.AppendAllText(AnimalLocalization.FileFor(reopened.Dir, "en-US", saved.Localization["description"]), "\n");
        Assert.NotEqual(revision, reopened.Revision());
        Assert.DoesNotContain("is a custom species based on", File.ReadAllText(mod.DatabasePath));
    }

    [Fact]
    public void New_species_get_a_text_file_per_language_and_other_languages_fall_back_to_english()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        foreach (var code in AnimalLocalization.Codes)
            foreach (var term in entry.Localization.Values)
                Assert.True(File.Exists(AnimalLocalization.FileFor(mod.Dir, code, term)), code + "/" + term);
        Assert.Equal("Animal", File.ReadAllText(AnimalLocalization.FileFor(mod.Dir, "en-US", entry.Localization["name"])));
        Assert.Equal("", File.ReadAllText(AnimalLocalization.FileFor(mod.Dir, "fr", entry.Localization["name"])));
        var loaded = AnimalDatabaseFile.Read(mod.DatabasePath);
        AnimalLocalization.Load(mod.Dir, loaded);
        Assert.Equal("Animal", AnimalText.Resolve(loaded[0].Texts, "name", "French"));
    }

    [Fact]
    public void Authoring_persists_canonical_template_and_keeps_source_available()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        var entry = mod.AddSpecies([Template()], "new-animal", "allosaurus anax", " New animal ", "Example", "animal", 1234);
        var saved = Assert.Single(ModProject.Open(ws, mod.Id).Manifest.Species);
        Assert.Equal("Allosaurus Anax", saved.Base);
        Assert.Equal("New animal", saved.Name);
        Assert.True(File.Exists(mod.DatabasePath));
        Assert.DoesNotContain("species", File.ReadAllText(Path.Combine(mod.Dir, ModManifest.FileName)));
        var available = ModProject.WithSpecies([Template()], mod.Manifest);
        Assert.Equal(2, available.Count);
        Assert.Equal("Allosaurus Anax", available[0].SpeciesId);
        Assert.Equal(entry.Key(mod.Id), available[1].SpeciesId);
        Assert.Equal("template-prefab", available[1].PrefabGuid);
        Assert.Throws<TyrantException>(() => mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Another", "Example", "another"));
        Assert.Single(ModProject.Open(ws, mod.Id).Manifest.Species);
    }

    [Fact]
    public void Vivarium_and_unknown_templates_are_refused_without_changing_mod()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        Assert.Throws<TyrantException>(() => mod.AddSpecies([Template(true)], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal"));
        Assert.Throws<TyrantException>(() => mod.AddSpecies([Template()], "new-animal", "Missing", "Animal", "Example", "animal"));
        Assert.Empty(ModProject.Open(ws, mod.Id).Manifest.Species);
    }

    [Fact]
    public void Checker_accepts_species_only_mod_and_rejects_missing_template()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        var checker = new ModChecker(_ => null);
        var valid = checker.Check(mod, null, [Template()]);
        Assert.True(valid.Ok);
        Assert.DoesNotContain(valid.Warnings, w => w.Contains("does nothing"));
        Assert.False(checker.Check(mod, null, []).Ok);
    }

    [Fact]
    public void External_database_edits_change_revision_and_are_not_overwritten_by_stale_editor()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal");
        var revision = mod.Revision();
        var entries = AnimalDatabaseFile.Read(mod.DatabasePath);
        entries[0].Cost = 789;
        AnimalDatabaseFile.Save(mod.DatabasePath, entries);
        Assert.NotEqual(revision, mod.Revision());
        Assert.Throws<TyrantException>(() => ModProject.Open(ws, mod.Id, revision));
        Assert.Throws<TyrantException>(() => mod.SetSpecies("new-animal", "Old editor", "Example", "animal", 123, new Dictionary<string, double>()));
        Assert.Equal(789, Assert.Single(ModProject.Open(ws, mod.Id).Manifest.Species).Cost);
    }

    [Fact]
    public void Effective_stat_validation_checks_inherited_range_endpoint()
    {
        Assert.Throws<ManifestException>(() => SpeciesDatabase.ValidateEffective(
            new Dictionary<string, double> { ["matureAgeYears"] = 50 }, new Dictionary<string, double> { ["maxAgeYears"] = 40 }));
        SpeciesDatabase.ValidateEffective(new Dictionary<string, double> { ["matureAgeYears"] = 20 },
            new Dictionary<string, double> { ["maxAgeYears"] = 40 });
    }

    [Fact]
    public void Database_survives_zip_export_and_import_and_manifest_undo()
    {
        using var game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(game.LibraryRoot, "workspace"), new GameInstall(game.Root, null));
        var mod = ModProject.Create(ws, "animal-pack", null, null);
        mod.AddSpecies([Template()], "new-animal", "Allosaurus Anax", "Animal", "Example", "animal", 123);
        var zip = ModSharing.Export(mod, Path.Combine(game.LibraryRoot, "animals.zip"), FrameworkInfo.Version);
        var imported = ModSharing.Import(ws, zip, replace: true);
        Assert.Equal(123, Assert.Single(imported.Manifest.Species).Cost);
        Assert.Contains(AnimalDatabaseFile.FileName, ModSharing.SharedFiles(imported));
        var saved = ModProject.SaveManifest(ws, imported.Id, imported.Manifest.ToJson(), imported.Revision());
        Assert.Single(saved.Manifest.Species);
    }
}
